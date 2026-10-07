using System.Text;
using System.Text.RegularExpressions;

namespace Wocel.Core.Pdf;

/// <summary>
/// Một trang PDF đã được "phẳng hoá": mọi thuộc tính kế thừa (Resources, MediaBox,
/// CropBox, Rotate) đã được sao xuống chính dictionary của trang, nên việc
/// tách/gộp/đảo trang không bao giờ làm mất thông tin từ node cha.
/// </summary>
public sealed class PdfPage
{
    public PdfDictionary Dictionary { get; }
    public PdfDocument Document { get; internal set; }

    internal PdfPage(PdfDictionary dictionary, PdfDocument document)
    {
        Dictionary = dictionary;
        Document = document;
    }

    public static readonly double[] A4 = { 0, 0, 595.276, 841.89 };
    public static readonly double[] Letter = { 0, 0, 612, 792 };

    public double[] MediaBox
    {
        get => Dictionary["MediaBox"].AsRectangle(Document) ?? (double[])A4.Clone();
        set => Dictionary["MediaBox"] = value.ToPdfArray();
    }

    public double[]? CropBox
    {
        get => Dictionary["CropBox"].AsRectangle(Document);
        set => Dictionary["CropBox"] = value?.ToPdfArray() ?? (PdfObject?)null!;
    }

    /// <summary>Góc xoay đã chuẩn hoá về 0/90/180/270.</summary>
    public int Rotate
    {
        get
        {
            int value = Dictionary.GetInt("Rotate", 0, this.Document);
            value %= 360;
            if (value < 0) value += 360;
            return value / 90 * 90;
        }
        set
        {
            int normalized = ((value % 360) + 360) % 360 / 90 * 90;
            if (normalized == 0) Dictionary.Remove("Rotate");
            else Dictionary.SetInt("Rotate", normalized);
        }
    }

    public PdfDictionary Resources
    {
        get
        {
            var res = Dictionary.GetDictionary("Resources", Document);
            if (res != null) return res;
            res = new PdfDictionary();
            Dictionary["Resources"] = res;
            return res;
        }
    }

    public double WidthPoints => MediaBox[2] - MediaBox[0];
    public double HeightPoints => MediaBox[3] - MediaBox[1];

    /// <summary>Kích thước hiển thị (đã tính góc xoay).</summary>
    public double DisplayWidth => Rotate is 90 or 270 ? HeightPoints : WidthPoints;
    public double DisplayHeight => Rotate is 90 or 270 ? WidthPoints : HeightPoints;

    /// <summary>Nối các content stream của trang thành một mảng byte đã giải nén.</summary>
    public byte[] GetContentBytes()
    {
        var contents = Document.Resolve(Dictionary["Contents"]);
        var parts = new List<byte[]>();

        switch (contents)
        {
            case PdfStream s:
                parts.Add(s.GetDecodedData(Document));
                break;
            case PdfArray arr:
                foreach (var item in arr.Items)
                    if (Document.Resolve(item) is PdfStream ps)
                        parts.Add(ps.GetDecodedData(Document));
                break;
        }

        if (parts.Count == 0) return Array.Empty<byte>();
        if (parts.Count == 1) return parts[0];

        using var ms = new MemoryStream();
        foreach (var part in parts)
        {
            ms.Write(part, 0, part.Length);
            ms.WriteByte((byte)'\n');
        }
        return ms.ToArray();
    }

    /// <summary>Thay toàn bộ nội dung trang bằng một content stream mới.</summary>
    public void SetContent(byte[] content)
    {
        var stream = PdfStream.FromDecoded(new PdfDictionary(), content);
        Dictionary["Contents"] = Document.Add(stream);
    }

    /// <summary>Bọc nội dung sẵn có giữa hai đoạn toán tử (dùng khi cần đổi hệ toạ độ trang).</summary>
    public void WrapContent(byte[] prefix, byte[] suffix)
    {
        var array = new PdfArray();
        array.Add(Document.Add(PdfStream.FromDecoded(new PdfDictionary(), prefix)));

        var current = Dictionary["Contents"];
        if (Document.Resolve(current) is PdfArray existing)
        {
            foreach (var item in existing.Items) array.Add(item);
        }
        else if (current != null && current is not PdfNull)
        {
            array.Add(current);
        }

        array.Add(Document.Add(PdfStream.FromDecoded(new PdfDictionary(), suffix)));
        Dictionary["Contents"] = array;
    }

    /// <summary>Chèn nội dung vào trước (lớp dưới) hoặc sau (lớp trên) nội dung sẵn có.</summary>
    public void AddContentLayer(byte[] content, bool onTop)
    {
        var stream = PdfStream.FromDecoded(new PdfDictionary(), content);
        var reference = Document.Add(stream);

        var current = Dictionary["Contents"];
        var array = new PdfArray();

        if (Document.Resolve(current) is PdfArray existing)
        {
            foreach (var item in existing.Items) array.Add(item);
        }
        else if (current != null && current is not PdfNull)
        {
            array.Add(current);
        }

        if (onTop) array.Add(reference);
        else array.Items.Insert(0, reference);

        Dictionary["Contents"] = array;
    }
}

/// <summary>Kết quả chẩn đoán khi mở file.</summary>
public enum PdfLoadStatus
{
    Ok,
    Repaired,
    EncryptedNeedsPassword,
    EncryptedUnsupported
}

/// <summary>
/// Tài liệu PDF đọc/ghi được, tự phân tích bảng xref (cả xref stream + object stream),
/// tự sửa file hỏng, và phẳng hoá cây trang.
/// </summary>
public sealed class PdfDocument : IPdfResolver
{
    private readonly Dictionary<int, PdfObject> _objects = new();
    private readonly Dictionary<int, long> _xrefOffsets = new();
    private readonly Dictionary<int, (int container, int index)> _compressed = new();
    private readonly HashSet<int> _encryptExemptObjects = new();

    private byte[] _sourceBytes = Array.Empty<byte>();
    private int _nextObjectNumber = 1;
    private List<PdfPage>? _pages;

    public PdfDictionary Trailer { get; private set; } = new();
    public string Version { get; set; } = "1.7";
    public PdfLoadStatus Status { get; private set; } = PdfLoadStatus.Ok;
    public string? EncryptionDescription { get; private set; }
    public PdfEncryption? Encryption { get; private set; }
    public bool IsEncrypted => Encryption != null || Trailer["Encrypt"] != null;

    public IReadOnlyDictionary<int, PdfObject> Objects => _objects;
    public int ObjectCount => _objects.Count;

    // ── Tạo mới ──────────────────────────────────────────────────────────
    public static PdfDocument Create()
    {
        var doc = new PdfDocument();
        var pages = new PdfDictionary();
        pages.SetName("Type", "Pages");
        pages["Kids"] = new PdfArray();
        pages.SetInt("Count", 0);
        var pagesRef = doc.Add(pages);

        var catalog = new PdfDictionary();
        catalog.SetName("Type", "Catalog");
        catalog["Pages"] = pagesRef;
        var catalogRef = doc.Add(catalog);

        doc.Trailer["Root"] = catalogRef;
        doc._pages = new List<PdfPage>();
        return doc;
    }

    // ── Mở file ──────────────────────────────────────────────────────────
    public static PdfDocument Load(byte[] data, string? password = null)
    {
        var doc = new PdfDocument { _sourceBytes = data };
        doc.Version = ReadHeaderVersion(data);

        bool xrefOk = false;
        try
        {
            xrefOk = doc.ReadXrefChain(data);
        }
        catch
        {
            xrefOk = false;
        }

        if (xrefOk)
        {
            try { doc.LoadObjectsFromXref(data); }
            catch { /* rơi xuống chế độ sửa chữa */ }
        }

        if (!doc.HasUsableCatalog())
        {
            doc.Repair(data);
            doc.Status = PdfLoadStatus.Repaired;
        }

        doc._nextObjectNumber = doc._objects.Count == 0 ? 1 : doc._objects.Keys.Max() + 1;
        doc.SetupDecryption(password);
        doc.BuildPages();
        return doc;
    }

    public static PdfDocument Load(string path, string? password = null)
        => Load(File.ReadAllBytes(path), password);

    private static string ReadHeaderVersion(byte[] data)
    {
        int limit = Math.Min(data.Length, 1024);
        var head = Encoding.ASCII.GetString(data, 0, limit);
        var match = Regex.Match(head, @"%PDF-(\d\.\d)");
        return match.Success ? match.Groups[1].Value : "1.7";
    }

    // ── XRef ─────────────────────────────────────────────────────────────
    private bool ReadXrefChain(byte[] data)
    {
        long start = FindStartXref(data);
        if (start < 0) return false;

        var visited = new HashSet<long>();
        long offset = start;
        bool any = false;

        while (offset >= 0 && offset < data.Length && visited.Add(offset))
        {
            var parser = new PdfParser(data, (int)offset);
            parser.SkipWhitespace();

            PdfDictionary? trailer = parser.PeekToken() == "xref"
                ? ReadXrefTable(parser)
                : ReadXrefStream(parser);

            if (trailer == null) break;
            any = true;
            MergeTrailer(trailer);

            // File "hybrid-reference": bảng cũ + xref stream bổ sung.
            if (trailer["XRefStm"] is PdfNumber hybrid && visited.Add(hybrid.AsLong))
            {
                try { ReadXrefStream(new PdfParser(data, hybrid.AsInt)); }
                catch { /* bỏ qua phần bổ sung nếu hỏng */ }
            }

            offset = trailer["Prev"] is PdfNumber prev ? prev.AsLong : -1;
        }

        return any && (_xrefOffsets.Count > 0 || _compressed.Count > 0);
    }

    private static long FindStartXref(byte[] data)
    {
        int tailLength = Math.Min(data.Length, 2048);
        var tail = Encoding.ASCII.GetString(data, data.Length - tailLength, tailLength);
        int index = tail.LastIndexOf("startxref", StringComparison.Ordinal);
        if (index < 0) return -1;

        var match = Regex.Match(tail[index..], @"startxref\s+(\d+)");
        return match.Success && long.TryParse(match.Groups[1].Value, out long value) ? value : -1;
    }

    private PdfDictionary? ReadXrefTable(PdfParser parser)
    {
        if (!parser.TryConsumeKeyword("xref")) return null;

        while (true)
        {
            parser.SkipWhitespace();
            var token = parser.PeekToken();
            if (token == "trailer")
            {
                parser.ReadToken();
                return parser.ParseObject() as PdfDictionary;
            }

            if (!int.TryParse(token, out int first)) return null;
            parser.ReadToken();

            if (!int.TryParse(parser.ReadToken(), out int count)) return null;

            for (int i = 0; i < count; i++)
            {
                var offsetToken = parser.ReadToken();
                var genToken = parser.ReadToken();
                var kindToken = parser.ReadToken();
                if (offsetToken.Length == 0 || kindToken.Length == 0) return null;

                if (kindToken != "n") continue;
                if (!long.TryParse(offsetToken, out long offset)) continue;
                _ = genToken;

                int objectNumber = first + i;
                if (offset > 0) _xrefOffsets.TryAdd(objectNumber, offset);
            }
        }
    }

    private PdfDictionary? ReadXrefStream(PdfParser parser)
    {
        var obj = parser.ParseIndirectObject(out _, out _);
        if (obj is not PdfStream stream) return null;

        var dict = stream.Dictionary;
        var w = dict.GetArray("W");
        if (w == null || w.Count < 3) return null;

        var widths = w.Items.Select(i => (i as PdfNumber)?.AsInt ?? 0).ToArray();
        var data = stream.GetDecodedData();

        var index = dict.GetArray("Index");
        var ranges = new List<(int start, int count)>();
        if (index != null)
        {
            for (int i = 0; i + 1 < index.Count; i += 2)
                ranges.Add(((index[i] as PdfNumber)?.AsInt ?? 0, (index[i + 1] as PdfNumber)?.AsInt ?? 0));
        }
        else
        {
            ranges.Add((0, dict.GetInt("Size", 0)));
        }

        int entryWidth = widths.Take(3).Sum();
        if (entryWidth <= 0) return null;

        int pos = 0;
        foreach (var (start, count) in ranges)
        {
            for (int i = 0; i < count && pos + entryWidth <= data.Length; i++)
            {
                long ReadField(int fieldIndex)
                {
                    int width = widths[fieldIndex];
                    long value = 0;
                    for (int b = 0; b < width; b++) value = (value << 8) | data[pos++];
                    return value;
                }

                long type = widths[0] == 0 ? 1 : ReadField(0);
                long f2 = ReadField(1);
                long f3 = ReadField(2);

                int objectNumber = start + i;
                switch (type)
                {
                    case 1 when f2 > 0:
                        _xrefOffsets.TryAdd(objectNumber, f2);
                        break;
                    case 2:
                        if (!_xrefOffsets.ContainsKey(objectNumber))
                            _compressed.TryAdd(objectNumber, ((int)f2, (int)f3));
                        break;
                }
            }
        }

        return dict;
    }

    private void MergeTrailer(PdfDictionary trailer)
    {
        foreach (var kv in trailer.Items)
        {
            if (kv.Key is "Prev" or "XRefStm") continue;
            if (!Trailer.Has(kv.Key)) Trailer[kv.Key] = kv.Value;
        }
    }

    // ── Nạp đối tượng ────────────────────────────────────────────────────
    private void LoadObjectsFromXref(byte[] data)
    {
        var parser = new PdfParser(data) { IndirectResolver = LookupForLength };

        foreach (var (number, offset) in _xrefOffsets.OrderBy(kv => kv.Value))
        {
            if (offset < 0 || offset >= data.Length) continue;

            parser.Position = (int)offset;
            var obj = parser.ParseIndirectObject(out int parsedNumber, out _);
            if (obj == null) continue;
            if (parsedNumber != number) continue; // offset sai — để bước sửa chữa lo
            _objects[number] = obj;
        }

        LoadCompressedObjects();
    }

    private PdfObject? LookupForLength(PdfRef reference)
    {
        if (_objects.TryGetValue(reference.Number, out var cached)) return cached;
        if (!_xrefOffsets.TryGetValue(reference.Number, out long offset)) return null;
        if (offset < 0 || offset >= _sourceBytes.Length) return null;

        var parser = new PdfParser(_sourceBytes, (int)offset);
        var obj = parser.ParseIndirectObject(out int number, out _);
        if (obj == null || number != reference.Number) return null;

        _objects[number] = obj;
        return obj;
    }

    private void LoadCompressedObjects()
    {
        foreach (var group in _compressed.GroupBy(kv => kv.Value.container))
        {
            if (!_objects.TryGetValue(group.Key, out var containerObj) || containerObj is not PdfStream container)
                continue;

            ExtractObjectStream(container, group.Select(kv => kv.Key).ToHashSet());
        }
    }

    /// <summary>Bung các đối tượng nằm trong /Type /ObjStm.</summary>
    private void ExtractObjectStream(PdfStream container, HashSet<int>? wanted)
    {
        byte[] decoded;
        try { decoded = container.GetDecodedData(this); }
        catch { return; }

        int count = container.Dictionary.GetInt("N", 0, this);
        int first = container.Dictionary.GetInt("First", 0, this);
        if (count <= 0 || first <= 0 || first > decoded.Length) return;

        var header = new PdfParser(decoded);
        var entries = new List<(int number, int offset)>(count);

        for (int i = 0; i < count; i++)
        {
            if (!int.TryParse(header.ReadToken(), out int number)) break;
            if (!int.TryParse(header.ReadToken(), out int offset)) break;
            entries.Add((number, offset));
        }

        foreach (var (number, offset) in entries)
        {
            if (wanted != null && !wanted.Contains(number)) continue;
            if (_objects.ContainsKey(number) && wanted == null) continue;

            int absolute = first + offset;
            if (absolute < 0 || absolute >= decoded.Length) continue;

            var body = new PdfParser(decoded, absolute) { IndirectResolver = LookupForLength };
            _objects[number] = body.ParseObject();
        }
    }

    private bool HasUsableCatalog()
    {
        if (_objects.Count == 0) return false;
        var catalog = FindCatalog();
        if (catalog == null) return false;
        var pages = catalog.GetDictionary("Pages", this);
        return pages != null || catalog.GetArray("Kids", this) != null;
    }

    // ── Sửa chữa file hỏng ───────────────────────────────────────────────
    /// <summary>
    /// Quét toàn bộ file tìm "n g obj" khi bảng xref sai/thiếu — đây cũng chính là
    /// thuật toán dùng cho chức năng "Sửa file PDF hỏng".
    /// </summary>
    private void Repair(byte[] data)
    {
        var text = Encoding.Latin1.GetString(data);
        var parser = new PdfParser(data) { IndirectResolver = LookupForLength };

        foreach (Match match in Regex.Matches(text, @"(?<=^|[\r\n\s>])(\d+)\s+(\d+)\s+obj\b"))
        {
            parser.Position = match.Index;
            var obj = parser.ParseIndirectObject(out int number, out _);
            if (obj == null || number < 0) continue;
            _objects[number] = obj; // bản xuất hiện sau ghi đè bản trước (incremental update)
        }

        // Bung mọi object stream tìm được.
        foreach (var stream in _objects.Values.OfType<PdfStream>().ToList())
        {
            if (stream.Dictionary.GetName("Type") == "ObjStm")
                ExtractObjectStream(stream, null);
        }

        // Lấy trailer mới nhất còn đọc được.
        foreach (Match match in Regex.Matches(text, @"trailer\b"))
        {
            var trailerParser = new PdfParser(data, match.Index + "trailer".Length);
            if (trailerParser.ParseObject() is PdfDictionary dict)
            {
                foreach (var kv in dict.Items)
                {
                    if (kv.Key is "Prev" or "XRefStm") continue;
                    Trailer[kv.Key] = kv.Value;
                }
            }
        }

        if (FindCatalog() == null)
        {
            // Không có trailer hợp lệ: tìm object /Type /Catalog, rồi tới /Type /Pages.
            foreach (var (number, obj) in _objects.OrderBy(kv => kv.Key))
            {
                if (obj is PdfDictionary dict && dict.GetName("Type") == "Catalog")
                {
                    Trailer["Root"] = new PdfRef(number);
                    break;
                }
            }
        }

        if (FindCatalog() == null)
        {
            var pageNumbers = _objects
                .Where(kv => kv.Value is PdfDictionary d && d.GetName("Type") == "Page")
                .OrderBy(kv => kv.Key)
                .Select(kv => kv.Key)
                .ToList();

            if (pageNumbers.Count > 0)
            {
                _nextObjectNumber = _objects.Keys.Max() + 1;
                var pagesDict = new PdfDictionary();
                pagesDict.SetName("Type", "Pages");
                pagesDict["Kids"] = new PdfArray(pageNumbers.Select(n => (PdfObject)new PdfRef(n)));
                pagesDict.SetInt("Count", pageNumbers.Count);
                var pagesRef = Add(pagesDict);

                var catalog = new PdfDictionary();
                catalog.SetName("Type", "Catalog");
                catalog["Pages"] = pagesRef;
                Trailer["Root"] = Add(catalog);
            }
        }
    }

    private PdfDictionary? FindCatalog()
    {
        if (Resolve(Trailer["Root"]) is PdfDictionary catalog && catalog.Items.Count > 0)
            return catalog;
        return null;
    }

    // ── Mã hoá ───────────────────────────────────────────────────────────
    private void SetupDecryption(string? password)
    {
        var encryptRef = Trailer["Encrypt"];
        if (encryptRef == null) return;

        if (encryptRef is PdfRef r) _encryptExemptObjects.Add(r.Number);
        if (Resolve(encryptRef) is not PdfDictionary encryptDict) return;

        var idArray = Resolve(Trailer["ID"]) as PdfArray;
        var firstId = idArray != null && idArray.Count > 0 && Resolve(idArray[0]) is PdfString s
            ? s.Bytes
            : Array.Empty<byte>();

        var handler = PdfEncryption.TryCreate(encryptDict, firstId, password ?? string.Empty, this, out var description);
        EncryptionDescription = description;

        if (handler == null)
        {
            Status = string.IsNullOrEmpty(password)
                ? PdfLoadStatus.EncryptedNeedsPassword
                : PdfLoadStatus.EncryptedUnsupported;
            return;
        }

        Encryption = handler;
        DecryptAllObjects(handler);
        Trailer.Remove("Encrypt"); // đã giải mã trong bộ nhớ
    }

    private void DecryptAllObjects(PdfEncryption handler)
    {
        foreach (var (number, obj) in _objects.ToList())
        {
            if (_encryptExemptObjects.Contains(number)) continue;
            // Đối tượng nằm trong object stream đã được giải mã cùng container.
            if (_compressed.ContainsKey(number)) continue;
            DecryptObject(obj, number, handler, new HashSet<PdfObject>());
        }
    }

    private void DecryptObject(PdfObject obj, int number, PdfEncryption handler, HashSet<PdfObject> seen)
    {
        switch (obj)
        {
            case PdfString str:
                str.Bytes = handler.Decrypt(str.Bytes, number, 0, isString: true);
                break;
            case PdfArray arr when seen.Add(arr):
                foreach (var item in arr.Items) DecryptObject(item, number, handler, seen);
                break;
            case PdfDictionary dict when seen.Add(dict):
                foreach (var value in dict.Items.Values) DecryptObject(value, number, handler, seen);
                break;
            case PdfStream stream when seen.Add(stream):
                foreach (var value in stream.Dictionary.Items.Values) DecryptObject(value, number, handler, seen);
                if (stream.Dictionary.GetName("Type") != "XRef")
                    stream.RawData = handler.Decrypt(stream.RawData, number, 0, isString: false);
                break;
        }
    }

    // ── Phân giải & quản lý đối tượng ────────────────────────────────────
    public PdfObject? Resolve(PdfObject? obj)
    {
        int guard = 0;
        while (obj is PdfRef reference && guard++ < 64)
            obj = _objects.TryGetValue(reference.Number, out var target) ? target : null;
        return obj is PdfNull ? null : obj;
    }

    public T? ResolveAs<T>(PdfObject? obj) where T : PdfObject => Resolve(obj) as T;

    public PdfObject? GetObject(int number) => _objects.TryGetValue(number, out var obj) ? obj : null;

    public PdfRef Add(PdfObject obj)
    {
        int number = _nextObjectNumber++;
        _objects[number] = obj;
        return new PdfRef(number);
    }

    public void Replace(int number, PdfObject obj)
    {
        _objects[number] = obj;
        if (number >= _nextObjectNumber) _nextObjectNumber = number + 1;
    }

    // ── Cây trang ────────────────────────────────────────────────────────
    private static readonly string[] InheritableKeys = { "Resources", "MediaBox", "CropBox", "Rotate" };

    public List<PdfPage> Pages
    {
        get
        {
            if (_pages == null) BuildPages();
            return _pages!;
        }
    }

    public int PageCount => Pages.Count;

    private void BuildPages()
    {
        var pages = new List<PdfPage>();
        var catalog = FindCatalog();
        var root = catalog.GetDictionary("Pages", this);

        if (root != null)
        {
            var visited = new HashSet<PdfDictionary>();
            Walk(root, new PdfDictionary(), visited, pages, 0);
        }

        if (pages.Count == 0)
        {
            // Cây trang hỏng — gom mọi object /Type /Page theo số hiệu.
            foreach (var (_, obj) in _objects.OrderBy(kv => kv.Key))
                if (obj is PdfDictionary dict && dict.GetName("Type") == "Page")
                    pages.Add(new PdfPage(dict, this));
        }

        _pages = pages;
    }

    private void Walk(PdfDictionary node, PdfDictionary inherited, HashSet<PdfDictionary> visited, List<PdfPage> output, int depth)
    {
        if (depth > 64 || !visited.Add(node)) return;

        var current = inherited.Clone();
        foreach (var key in InheritableKeys)
            if (node.Has(key)) current[key] = node[key]!;

        var kids = node.GetArray("Kids", this);
        bool isLeaf = kids == null || node.GetName("Type") == "Page";

        if (isLeaf)
        {
            foreach (var key in InheritableKeys)
                if (!node.Has(key) && current.Has(key)) node[key] = current[key]!;

            node.SetName("Type", "Page");
            node.Remove("Parent");
            output.Add(new PdfPage(node, this));
            return;
        }

        foreach (var kid in kids!.Items)
        {
            if (Resolve(kid) is PdfDictionary child)
                Walk(child, current, visited, output, depth + 1);
        }
    }

    /// <summary>Ghi lại danh sách trang (dùng sau khi tách/gộp/đảo/xoá trang).</summary>
    public void SetPages(IEnumerable<PdfPage> pages)
    {
        var list = pages.ToList();
        var catalog = FindCatalog();

        if (catalog == null)
        {
            catalog = new PdfDictionary();
            catalog.SetName("Type", "Catalog");
            Trailer["Root"] = Add(catalog);
        }

        var pagesDict = catalog.GetDictionary("Pages", this);
        PdfRef pagesRef;

        if (pagesDict == null)
        {
            pagesDict = new PdfDictionary();
            pagesRef = Add(pagesDict);
            catalog["Pages"] = pagesRef;
        }
        else
        {
            pagesRef = catalog["Pages"] as PdfRef ?? Add(pagesDict);
            catalog["Pages"] = pagesRef;
        }

        var kids = new PdfArray();
        foreach (var page in list)
        {
            page.Document = this;
            page.Dictionary.SetName("Type", "Page");
            page.Dictionary["Parent"] = pagesRef;
            kids.Add(FindOrAddReference(page.Dictionary));
        }

        pagesDict.SetName("Type", "Pages");
        pagesDict["Kids"] = kids;
        pagesDict.SetInt("Count", list.Count);

        // Outline/Names trỏ tới trang cũ có thể sai sau khi đổi cấu trúc — bỏ cho an toàn.
        catalog.Remove("Outlines");
        catalog.Remove("Names");
        catalog.Remove("PageLabels");
        catalog.Remove("StructTreeRoot");
        catalog.Remove("AcroForm");

        _pages = list;
    }

    private PdfRef FindOrAddReference(PdfObject obj)
    {
        foreach (var (number, existing) in _objects)
            if (ReferenceEquals(existing, obj)) return new PdfRef(number);
        return Add(obj);
    }

    // ── Nhập đối tượng từ tài liệu khác ──────────────────────────────────
    /// <summary>Sao chép sâu một trang từ tài liệu nguồn sang tài liệu này.</summary>
    public PdfPage ImportPage(PdfPage source, Dictionary<int, PdfRef>? sharedMap = null)
    {
        var map = sharedMap ?? new Dictionary<int, PdfRef>();
        var copied = (PdfDictionary)ImportObject(source.Document, source.Dictionary, map);
        copied.Remove("Parent");
        copied.SetName("Type", "Page");
        return new PdfPage(copied, this);
    }

    private PdfObject ImportObject(PdfDocument source, PdfObject obj, Dictionary<int, PdfRef> map, int depth = 0)
    {
        if (depth > 128) return PdfObject.Null;

        switch (obj)
        {
            case PdfRef reference:
            {
                if (map.TryGetValue(reference.Number, out var existing)) return existing;

                var target = source.GetObject(reference.Number);
                if (target == null) return PdfObject.Null;

                // Đặt chỗ trước để xử lý tham chiếu vòng (Parent ↔ Kids).
                int number = _nextObjectNumber++;
                var placeholder = new PdfRef(number);
                map[reference.Number] = placeholder;
                _objects[number] = PdfObject.Null;
                _objects[number] = ImportObject(source, target, map, depth + 1);
                return placeholder;
            }

            case PdfArray array:
                return new PdfArray(array.Items.Select(i => ImportObject(source, i, map, depth + 1)));

            case PdfDictionary dict:
            {
                var copy = new PdfDictionary();
                foreach (var kv in dict.Items)
                    copy[kv.Key] = ImportObject(source, kv.Value, map, depth + 1);
                return copy;
            }

            case PdfStream stream:
            {
                var copyDict = new PdfDictionary();
                foreach (var kv in stream.Dictionary.Items)
                    copyDict[kv.Key] = ImportObject(source, kv.Value, map, depth + 1);
                return new PdfStream(copyDict, (byte[])stream.RawData.Clone());
            }

            default:
                return obj;
        }
    }

    // ── Metadata ─────────────────────────────────────────────────────────
    public PdfDictionary GetOrCreateInfo()
    {
        if (Resolve(Trailer["Info"]) is PdfDictionary info) return info;
        var created = new PdfDictionary();
        Trailer["Info"] = Add(created);
        return created;
    }

    public PdfDictionary? GetInfo() => Resolve(Trailer["Info"]) as PdfDictionary;
    public PdfDictionary? GetCatalog() => FindCatalog();

    // ── Ghi file ─────────────────────────────────────────────────────────
    public byte[] Save(PdfSaveOptions? options = null) => PdfWriter.Write(this, options ?? new PdfSaveOptions());

    public void Save(string path, PdfSaveOptions? options = null) => File.WriteAllBytes(path, Save(options));
}
