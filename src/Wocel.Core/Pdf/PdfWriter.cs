using System.Globalization;
using System.Text;

namespace Wocel.Core.Pdf;

/// <summary>Tuỳ chọn khi ghi file PDF.</summary>
public sealed class PdfSaveOptions
{
    /// <summary>Loại bỏ đối tượng không còn ai tham chiếu tới (giảm dung lượng).</summary>
    public bool RemoveUnusedObjects { get; set; } = true;

    /// <summary>Nén lại mọi stream chưa nén bằng FlateDecode.</summary>
    public bool CompressStreams { get; set; }

    /// <summary>Gộp các đối tượng nhỏ vào /ObjStm và dùng xref stream — giảm dung lượng đáng kể.</summary>
    public bool UseObjectStreams { get; set; }

    /// <summary>Gộp các đối tượng trùng lặp byte-for-byte (ảnh, font lặp lại sau khi gộp file).</summary>
    public bool DeduplicateObjects { get; set; }

    /// <summary>Ghi kèm mã hoá tiêu chuẩn (đặt mật khẩu).</summary>
    public PdfEncryptionSettings? Encryption { get; set; }

    public string? ForceVersion { get; set; }

    public static PdfSaveOptions Default => new();

    public static PdfSaveOptions MaxCompression => new()
    {
        RemoveUnusedObjects = true,
        CompressStreams = true,
        UseObjectStreams = true,
        DeduplicateObjects = true
    };
}

/// <summary>Ghi <see cref="PdfDocument"/> ra byte theo cú pháp PDF chuẩn.</summary>
public static class PdfWriter
{
    public static byte[] Write(PdfDocument document, PdfSaveOptions options)
    {
        var alias = options.DeduplicateObjects ? BuildAliasMap(document) : new Dictionary<int, int>();
        var order = CollectReachable(document, alias, options.RemoveUnusedObjects);

        // old number → new number (1-based, thứ tự duyệt để các đối tượng liên quan nằm gần nhau)
        var map = new Dictionary<int, int>(order.Count);
        for (int i = 0; i < order.Count; i++) map[order[i]] = i + 1;

        foreach (var (from, to) in alias)
            if (map.TryGetValue(to, out int mapped)) map[from] = mapped;

        int size = order.Count + 1;

        // Khi đặt mật khẩu, /ID phải là giá trị mới vì nó tham gia sinh khoá mã hoá.
        var idBytes = options.Encryption != null ? Guid.NewGuid().ToByteArray() : BuildFileId(document);

        var encryption = options.Encryption == null
            ? null
            : PdfEncryption.CreateForWriting(options.Encryption, idBytes);

        string version = options.ForceVersion ?? document.Version;
        if (encryption?.RequiredVersion is { } required && string.CompareOrdinal(version, required) < 0)
            version = required;
        if (options.UseObjectStreams && encryption == null && string.CompareOrdinal(version, "1.5") < 0) version = "1.5";

        using var output = new MemoryStream();
        WriteAscii(output, $"%PDF-{version}\n");
        output.Write(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' });

        var offsets = new Dictionary<int, long>();
        var inObjectStream = new Dictionary<int, (int container, int index)>();

        if (options.UseObjectStreams && encryption == null)
        {
            WriteWithObjectStreams(document, options, order, map, output, offsets, inObjectStream, encryption);
        }
        else
        {
            foreach (int oldNumber in order)
            {
                int newNumber = map[oldNumber];
                var obj = document.GetObject(oldNumber) ?? PdfObject.Null;
                offsets[newNumber] = output.Position;
                WriteIndirectObject(output, newNumber, obj, map, options, encryption);
            }
        }

        int encryptNumber = 0;
        if (encryption != null)
        {
            // Bản thân dictionary /Encrypt không bao giờ được mã hoá.
            encryptNumber = order.Count + 1;
            offsets[encryptNumber] = output.Position;
            WriteIndirectObject(output, encryptNumber, encryption.BuildEncryptDictionary(), map, options, null);
            size = encryptNumber + 1;
        }

        long startXref;
        if (inObjectStream.Count > 0)
        {
            startXref = WriteXrefStream(document, output, offsets, inObjectStream, map, idBytes);
        }
        else
        {
            startXref = WriteXrefTable(output, offsets, size);
            WriteTrailerDictionary(document, output, map, size, idBytes, encryptNumber, options);
        }

        WriteAscii(output, "startxref\n");
        WriteAscii(output, startXref.ToString(CultureInfo.InvariantCulture) + "\n");
        WriteAscii(output, "%%EOF\n");

        return output.ToArray();
    }

    // ── Thu thập & đánh số lại ───────────────────────────────────────────
    private static List<int> CollectReachable(PdfDocument document, Dictionary<int, int> alias, bool prune)
    {
        var roots = new List<PdfObject?>
        {
            document.Trailer["Root"],
            document.Trailer["Info"]
        };

        var ordered = new List<int>();
        var seen = new HashSet<int>();
        var queue = new Queue<PdfObject>();

        foreach (var root in roots)
            if (root != null) queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            switch (current)
            {
                case PdfRef reference:
                {
                    int number = alias.TryGetValue(reference.Number, out int canonical) ? canonical : reference.Number;
                    if (!seen.Add(number)) break;
                    ordered.Add(number);
                    var target = document.GetObject(number);
                    if (target != null) queue.Enqueue(target);
                    break;
                }
                case PdfArray array:
                    foreach (var item in array.Items) queue.Enqueue(item);
                    break;
                case PdfDictionary dict:
                    foreach (var value in dict.Items.Values) queue.Enqueue(value);
                    break;
                case PdfStream stream:
                    foreach (var value in stream.Dictionary.Items.Values) queue.Enqueue(value);
                    break;
            }
        }

        if (!prune)
        {
            foreach (int number in document.Objects.Keys.OrderBy(n => n))
            {
                if (alias.ContainsKey(number)) continue;
                if (seen.Add(number)) ordered.Add(number);
            }
        }

        return ordered;
    }

    private static Dictionary<int, int> BuildAliasMap(PdfDocument document)
    {
        var alias = new Dictionary<int, int>();
        var byHash = new Dictionary<string, int>(StringComparer.Ordinal);
        var identity = new Dictionary<int, int>();

        foreach (var (number, obj) in document.Objects.OrderBy(kv => kv.Key))
        {
            // Không gộp trang: mỗi trang phải là một đối tượng riêng.
            if (obj is PdfDictionary d && d.GetName("Type") is "Page" or "Pages" or "Catalog") continue;

            using var buffer = new MemoryStream();
            WriteObject(buffer, obj, identity, PdfSaveOptions.Default, null, 0);
            if (obj is PdfStream stream) buffer.Write(stream.RawData, 0, stream.RawData.Length);

            string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(buffer.ToArray()));
            if (byHash.TryGetValue(key, out int canonical)) alias[number] = canonical;
            else byHash[key] = number;
        }

        return alias;
    }

    // ── Object stream + xref stream ──────────────────────────────────────
    private static void WriteWithObjectStreams(
        PdfDocument document, PdfSaveOptions options, List<int> order, Dictionary<int, int> map,
        MemoryStream output, Dictionary<int, long> offsets,
        Dictionary<int, (int container, int index)> inObjectStream, PdfEncryption? encryption)
    {
        var packable = new List<int>();
        var standalone = new List<int>();

        foreach (int oldNumber in order)
        {
            var obj = document.GetObject(oldNumber);
            // Stream không được nằm trong ObjStm; catalog để ngoài cho trình đọc cũ dễ tìm.
            if (obj is PdfStream) standalone.Add(oldNumber);
            else packable.Add(oldNumber);
        }

        foreach (int oldNumber in standalone)
        {
            int newNumber = map[oldNumber];
            offsets[newNumber] = output.Position;
            WriteIndirectObject(output, newNumber, document.GetObject(oldNumber) ?? PdfObject.Null, map, options, encryption);
        }

        const int chunkSize = 100;
        int containerNumber = order.Count + 1;

        for (int start = 0; start < packable.Count; start += chunkSize)
        {
            var chunk = packable.Skip(start).Take(chunkSize).ToList();
            var header = new StringBuilder();
            using var body = new MemoryStream();

            for (int i = 0; i < chunk.Count; i++)
            {
                int newNumber = map[chunk[i]];
                header.Append(newNumber).Append(' ').Append(body.Position).Append(' ');
                WriteObject(body, document.GetObject(chunk[i]) ?? PdfObject.Null, map, options, encryption, newNumber);
                body.WriteByte((byte)'\n');
                inObjectStream[newNumber] = (containerNumber, i);
            }

            var headerBytes = Encoding.ASCII.GetBytes(header.ToString());
            var payload = new byte[headerBytes.Length + body.Length];
            Buffer.BlockCopy(headerBytes, 0, payload, 0, headerBytes.Length);
            Buffer.BlockCopy(body.ToArray(), 0, payload, headerBytes.Length, (int)body.Length);

            var dict = new PdfDictionary();
            dict.SetName("Type", "ObjStm");
            dict.SetInt("N", chunk.Count);
            dict.SetInt("First", headerBytes.Length);
            var container = PdfStream.FromDecoded(dict, payload);

            offsets[containerNumber] = output.Position;
            WriteIndirectObject(output, containerNumber, container, map, options, encryption);
            containerNumber++;
        }
    }

    private static long WriteXrefStream(
        PdfDocument document, MemoryStream output, Dictionary<int, long> offsets,
        Dictionary<int, (int container, int index)> inObjectStream, Dictionary<int, int> map,
        byte[] idBytes)
    {
        int maxNumber = Math.Max(
            offsets.Count == 0 ? 0 : offsets.Keys.Max(),
            inObjectStream.Count == 0 ? 0 : inObjectStream.Keys.Max());

        int xrefNumber = maxNumber + 1;
        long xrefOffset = output.Position;
        int total = xrefNumber + 1;

        using var data = new MemoryStream();
        void WriteEntry(byte type, long field2, int field3)
        {
            data.WriteByte(type);
            data.WriteByte((byte)(field2 >> 24));
            data.WriteByte((byte)(field2 >> 16));
            data.WriteByte((byte)(field2 >> 8));
            data.WriteByte((byte)field2);
            data.WriteByte((byte)(field3 >> 8));
            data.WriteByte((byte)field3);
        }

        WriteEntry(0, 0, 0xFFFF);
        for (int number = 1; number < total; number++)
        {
            if (number == xrefNumber) WriteEntry(1, xrefOffset, 0);
            else if (offsets.TryGetValue(number, out long offset)) WriteEntry(1, offset, 0);
            else if (inObjectStream.TryGetValue(number, out var location)) WriteEntry(2, location.container, location.index);
            else WriteEntry(0, 0, 0xFFFF);
        }

        var dict = new PdfDictionary();
        dict.SetName("Type", "XRef");
        dict.SetInt("Size", total);
        dict["W"] = new PdfArray(new PdfObject[] { new PdfNumber(1L), new PdfNumber(4L), new PdfNumber(2L) });
        if (document.Trailer["Root"] is { } root) dict["Root"] = Remap(root, map);
        if (document.Trailer["Info"] is { } info) dict["Info"] = Remap(info, map);
        dict["ID"] = new PdfArray(new PdfObject[] { new PdfString(idBytes, true), new PdfString(idBytes, true) });

        var stream = PdfStream.FromDecoded(dict, data.ToArray());
        WriteIndirectObject(output, xrefNumber, stream, EmptyMap, PdfSaveOptions.Default, null);
        return xrefOffset;
    }

    private static readonly Dictionary<int, int> EmptyMap = new();

    private static PdfObject Remap(PdfObject obj, Dictionary<int, int> map)
        => obj is PdfRef r && map.TryGetValue(r.Number, out int mapped) ? new PdfRef(mapped) : obj;

    // ── Bảng xref cổ điển ────────────────────────────────────────────────
    private static long WriteXrefTable(MemoryStream output, Dictionary<int, long> offsets, int size)
    {
        long start = output.Position;
        WriteAscii(output, "xref\n");
        WriteAscii(output, $"0 {size}\n");
        WriteAscii(output, "0000000000 65535 f \n");

        for (int number = 1; number < size; number++)
        {
            long offset = offsets.TryGetValue(number, out long value) ? value : 0;
            WriteAscii(output, offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        return start;
    }

    private static void WriteTrailerDictionary(
        PdfDocument document, MemoryStream output, Dictionary<int, int> map, int size,
        byte[] idBytes, int encryptNumber, PdfSaveOptions options)
    {
        var trailer = new PdfDictionary();
        trailer.SetInt("Size", size);
        if (document.Trailer["Root"] is { } root) trailer["Root"] = Remap(root, map);
        if (document.Trailer["Info"] is { } info) trailer["Info"] = Remap(info, map);
        trailer["ID"] = new PdfArray(new PdfObject[] { new PdfString(idBytes, true), new PdfString(idBytes, true) });
        if (encryptNumber > 0) trailer["Encrypt"] = new PdfRef(encryptNumber);

        // Các tham chiếu trong trailer đã là số hiệu cuối cùng — dùng bảng rỗng để không đổi số lần nữa.
        WriteAscii(output, "trailer\n");
        WriteObject(output, trailer, EmptyMap, options, null, 0);
        WriteAscii(output, "\n");
    }

    private static byte[] BuildFileId(PdfDocument document)
    {
        if (document.Resolve(document.Trailer["ID"]) is PdfArray array && array.Count > 0
            && document.Resolve(array[0]) is PdfString existing && existing.Bytes.Length > 0)
            return existing.Bytes;

        return Guid.NewGuid().ToByteArray();
    }

    // ── Tuần tự hoá ──────────────────────────────────────────────────────
    private static void WriteIndirectObject(
        MemoryStream output, int number, PdfObject obj, Dictionary<int, int> map,
        PdfSaveOptions options, PdfEncryption? encryption)
    {
        WriteAscii(output, $"{number} 0 obj\n");
        WriteObject(output, obj, map, options, encryption, number);
        WriteAscii(output, "\nendobj\n");
    }

    private static void WriteObject(
        Stream output, PdfObject? obj, Dictionary<int, int> map, PdfSaveOptions options,
        PdfEncryption? encryption, int objectNumber)
    {
        switch (obj)
        {
            case null:
            case PdfNull:
                WriteAscii(output, "null");
                break;

            case PdfBool b:
                WriteAscii(output, b.Value ? "true" : "false");
                break;

            case PdfNumber n:
                WriteAscii(output, n.ToString());
                break;

            case PdfName name:
                WriteAscii(output, EncodeName(name.Value));
                break;

            case PdfString str:
            {
                var bytes = encryption != null && objectNumber > 0
                    ? encryption.Encrypt(str.Bytes, objectNumber, 0, isString: true)
                    : str.Bytes;
                WriteStringLiteral(output, bytes, str.PreferHex || encryption != null);
                break;
            }

            case PdfRef reference:
            {
                int mapped = map.TryGetValue(reference.Number, out int value) ? value : reference.Number;
                WriteAscii(output, $"{mapped} 0 R");
                break;
            }

            case PdfArray array:
            {
                WriteAscii(output, "[");
                for (int i = 0; i < array.Count; i++)
                {
                    if (i > 0) WriteAscii(output, " ");
                    WriteObject(output, array[i], map, options, encryption, objectNumber);
                }
                WriteAscii(output, "]");
                break;
            }

            case PdfDictionary dict:
                WriteDictionary(output, dict, map, options, encryption, objectNumber);
                break;

            case PdfStream stream:
            {
                var raw = stream.RawData;

                if (options.CompressStreams && stream.Dictionary["Filter"] == null && raw.Length > 128)
                {
                    var packed = PdfFilters.FlateEncode(stream.GetDecodedData());
                    if (packed.Length < raw.Length)
                    {
                        raw = packed;
                        stream.Dictionary.SetName("Filter", "FlateDecode");
                    }
                }

                if (encryption != null && objectNumber > 0)
                    raw = encryption.Encrypt(raw, objectNumber, 0, isString: false);

                stream.Dictionary.SetInt("Length", raw.Length);
                WriteDictionary(output, stream.Dictionary, map, options, encryption, objectNumber);
                WriteAscii(output, "\nstream\n");
                output.Write(raw, 0, raw.Length);
                WriteAscii(output, "\nendstream");
                break;
            }
        }
    }

    private static void WriteDictionary(
        Stream output, PdfDictionary dict, Dictionary<int, int> map, PdfSaveOptions options,
        PdfEncryption? encryption, int objectNumber)
    {
        WriteAscii(output, "<<");
        bool first = true;

        foreach (var kv in dict.Items)
        {
            if (!first) WriteAscii(output, " ");
            first = false;
            WriteAscii(output, EncodeName(kv.Key));
            WriteAscii(output, " ");
            WriteObject(output, kv.Value, map, options, encryption, objectNumber);
        }

        WriteAscii(output, ">>");
    }

    private static string EncodeName(string name)
    {
        var sb = new StringBuilder("/");
        foreach (char c in name)
        {
            if (c < '!' || c > '~' || c is '#' or '/' or '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '%')
                sb.Append('#').Append(((int)c & 0xFF).ToString("X2"));
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static void WriteStringLiteral(Stream output, byte[] bytes, bool hex)
    {
        if (hex)
        {
            WriteAscii(output, "<" + Convert.ToHexString(bytes) + ">");
            return;
        }

        WriteAscii(output, "(");
        foreach (var b in bytes)
        {
            switch (b)
            {
                case (byte)'(': WriteAscii(output, "\\("); break;
                case (byte)')': WriteAscii(output, "\\)"); break;
                case (byte)'\\': WriteAscii(output, "\\\\"); break;
                case (byte)'\r': WriteAscii(output, "\\r"); break;
                case (byte)'\n': WriteAscii(output, "\\n"); break;
                case (byte)'\t': WriteAscii(output, "\\t"); break;
                case 8: WriteAscii(output, "\\b"); break;
                case 12: WriteAscii(output, "\\f"); break;
                default: output.WriteByte(b); break;
            }
        }
        WriteAscii(output, ")");
    }

    private static void WriteAscii(Stream output, string text)
    {
        var bytes = Encoding.Latin1.GetBytes(text);
        output.Write(bytes, 0, bytes.Length);
    }
}
