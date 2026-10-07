using System.Text;
using Wocel.Core.Pdf;

namespace Wocel.Core.Services;

/// <summary>
/// Bộ công cụ xử lý PDF hoàn toàn ngoại tuyến, xây trên bộ phân tích PDF của Wocel.
/// Mọi thao tác nhận byte[] và trả về byte[] nên dùng được cả ở UI lẫn dòng lệnh.
/// </summary>
public static partial class PdfToolkit
{
    // ─────────────────────────────────────────────────────────────────────
    //  MỞ FILE
    // ─────────────────────────────────────────────────────────────────────
    public static PdfDocument Open(byte[] data, string? password = null)
    {
        if (data == null || data.Length == 0)
            throw new PdfToolException("Tệp rỗng hoặc không đọc được.");

        PdfDocument document;
        try
        {
            document = PdfDocument.Load(data, password);
        }
        catch (Exception ex)
        {
            throw new PdfToolException($"Không phân tích được tệp PDF: {ex.Message}");
        }

        switch (document.Status)
        {
            case PdfLoadStatus.EncryptedNeedsPassword:
                throw new PdfToolException("Tệp PDF này được đặt mật khẩu. Hãy nhập mật khẩu mở tệp rồi thử lại.");
            case PdfLoadStatus.EncryptedUnsupported:
                throw new PdfToolException("Mật khẩu không đúng, hoặc tệp dùng kiểu bảo vệ mà Wocel chưa hỗ trợ.");
        }

        if (document.PageCount == 0)
            throw new PdfToolException("Không tìm thấy trang nào trong tệp. Tệp có thể đã hỏng — hãy thử công cụ “Sửa tệp PDF”.");

        return document;
    }

    private static PdfSaveOptions DefaultSave => new() { RemoveUnusedObjects = true };

    // ─────────────────────────────────────────────────────────────────────
    //  GỘP
    // ─────────────────────────────────────────────────────────────────────
    /// <summary>Gộp nhiều tệp PDF thành một, giữ nguyên nội dung và tài nguyên của từng trang.</summary>
    public static byte[] Merge(IEnumerable<byte[]> files, IEnumerable<string?>? passwords = null)
    {
        var list = files.ToList();
        if (list.Count < 2)
            throw new PdfToolException("Cần ít nhất 2 tệp PDF để gộp.");

        var passwordList = passwords?.ToList() ?? new List<string?>();
        var output = PdfDocument.Create();
        var pages = new List<PdfPage>();

        for (int i = 0; i < list.Count; i++)
        {
            var password = i < passwordList.Count ? passwordList[i] : null;
            var source = Open(list[i], password);
            var map = new Dictionary<int, PdfRef>();

            foreach (var page in source.Pages)
                pages.Add(output.ImportPage(page, map));
        }

        output.SetPages(pages);
        StampProducer(output);
        return output.Save(DefaultSave);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  TÁCH
    // ─────────────────────────────────────────────────────────────────────
    public static List<PdfOutputFile> Split(byte[] data, PdfSplitOptions options, string? password = null)
    {
        var document = Open(data, password);
        int pageCount = document.PageCount;
        var groups = new List<(string label, List<int> pages)>();

        switch (options.Mode)
        {
            case PdfSplitMode.EveryPage:
                for (int p = 1; p <= pageCount; p++) groups.Add(($"trang_{p}", new List<int> { p }));
                break;

            case PdfSplitMode.EveryNPages:
            {
                int size = Math.Max(1, options.PagesPerFile);
                for (int start = 1; start <= pageCount; start += size)
                {
                    int end = Math.Min(pageCount, start + size - 1);
                    groups.Add(($"trang_{start}-{end}", Enumerable.Range(start, end - start + 1).ToList()));
                }
                break;
            }

            case PdfSplitMode.ByRanges:
            {
                var chunks = options.Ranges.Split(';', StringSplitOptions.RemoveEmptyEntries);
                if (chunks.Length == 0)
                    throw new PdfToolException("Chưa nhập khoảng trang. Ví dụ: 1-3; 4-6; 7");

                foreach (var chunk in chunks)
                {
                    var pages = PdfPageRange.Parse(chunk, pageCount);
                    if (pages.Count > 0) groups.Add(($"trang_{PdfPageRange.Describe(pages).Replace(", ", "_")}", pages));
                }
                break;
            }
        }

        if (groups.Count == 0)
            throw new PdfToolException("Không có trang nào khớp với lựa chọn tách.");

        var results = new List<PdfOutputFile>();
        foreach (var (label, pages) in groups)
        {
            var part = BuildDocumentFromPages(document, pages);
            results.Add(new PdfOutputFile
            {
                FileName = $"{options.BaseName}_{label}.pdf",
                Data = part,
                Description = $"{pages.Count} trang ({PdfPageRange.Describe(pages)})"
            });
        }

        return results;
    }

    /// <summary>Trích các trang được chọn ra một tệp PDF mới.</summary>
    public static byte[] ExtractPages(byte[] data, string pageExpression, string? password = null)
    {
        var document = Open(data, password);
        var pages = PdfPageRange.Parse(pageExpression, document.PageCount);

        if (pages.Count == 0)
            throw new PdfToolException($"Không có trang nào khớp “{pageExpression}”. Tệp có {document.PageCount} trang.");

        return BuildDocumentFromPages(document, pages);
    }

    private static byte[] BuildDocumentFromPages(PdfDocument source, IEnumerable<int> pageNumbers)
    {
        var output = PdfDocument.Create();
        var map = new Dictionary<int, PdfRef>();
        var pages = new List<PdfPage>();

        foreach (int number in pageNumbers)
        {
            if (number < 1 || number > source.PageCount) continue;
            pages.Add(output.ImportPage(source.Pages[number - 1], map));
        }

        if (pages.Count == 0) throw new PdfToolException("Không chọn được trang hợp lệ nào.");

        output.SetPages(pages);
        CopyMetadata(source, output);
        StampProducer(output);
        return output.Save(DefaultSave);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  XOÁ / ĐẢO THỨ TỰ / XOAY
    // ─────────────────────────────────────────────────────────────────────
    public static byte[] RemovePages(byte[] data, string pageExpression, string? password = null)
    {
        var document = Open(data, password);
        var remove = PdfPageRange.Parse(pageExpression, document.PageCount).ToHashSet();

        if (remove.Count == 0)
            throw new PdfToolException("Chưa chọn trang nào để xoá.");
        if (remove.Count >= document.PageCount)
            throw new PdfToolException("Không thể xoá toàn bộ trang — tệp PDF phải còn ít nhất một trang.");

        var keep = Enumerable.Range(1, document.PageCount).Where(p => !remove.Contains(p));
        return BuildDocumentFromPages(document, keep);
    }

    /// <summary>Sắp xếp lại trang. <paramref name="order"/> là danh sách số trang theo thứ tự mong muốn.</summary>
    public static byte[] ReorderPages(byte[] data, IEnumerable<int> order, string? password = null)
    {
        var document = Open(data, password);
        var list = order.Where(p => p >= 1 && p <= document.PageCount).ToList();

        if (list.Count == 0)
            throw new PdfToolException("Thứ tự trang không hợp lệ.");

        // Trang không được nhắc tới sẽ giữ nguyên ở cuối để không mất dữ liệu.
        foreach (int page in Enumerable.Range(1, document.PageCount))
            if (!list.Contains(page)) list.Add(page);

        return BuildDocumentFromPages(document, list);
    }

    /// <summary>Đảo ngược thứ tự toàn bộ trang.</summary>
    public static byte[] ReversePages(byte[] data, string? password = null)
    {
        var document = Open(data, password);
        return BuildDocumentFromPages(document, Enumerable.Range(1, document.PageCount).Reverse());
    }

    /// <summary>Xoay trang. <paramref name="pageExpression"/> để trống nghĩa là mọi trang.</summary>
    public static byte[] RotatePages(byte[] data, int degrees, string? pageExpression = null, string? password = null)
    {
        var document = Open(data, password);
        var selected = string.IsNullOrWhiteSpace(pageExpression)
            ? Enumerable.Range(1, document.PageCount).ToList()
            : PdfPageRange.Parse(pageExpression, document.PageCount);

        if (selected.Count == 0)
            throw new PdfToolException("Không có trang nào khớp lựa chọn.");

        foreach (int number in selected)
        {
            var page = document.Pages[number - 1];
            page.Rotate += degrees;
        }

        document.SetPages(document.Pages);
        StampProducer(document);
        return document.Save(DefaultSave);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  ĐỔI KHỔ GIẤY / CẮT LỀ
    // ─────────────────────────────────────────────────────────────────────
    /// <summary>Đưa mọi trang về cùng một khổ giấy, nội dung được co giãn giữ nguyên tỉ lệ.</summary>
    public static byte[] ResizePages(byte[] data, double[] targetSize, bool keepOrientation = true, string? password = null)
    {
        var document = Open(data, password);

        foreach (var page in document.Pages.ToList())
        {
            double sourceWidth = page.DisplayWidth;
            double sourceHeight = page.DisplayHeight;
            if (sourceWidth <= 0 || sourceHeight <= 0) continue;

            var target = keepOrientation && sourceWidth > sourceHeight
                ? PdfPageSizes.Landscape(targetSize)
                : (double[])targetSize.Clone();

            double targetWidth = target[2] - target[0];
            double targetHeight = target[3] - target[1];

            double scale = Math.Min(targetWidth / sourceWidth, targetHeight / sourceHeight);
            double offsetX = (targetWidth - sourceWidth * scale) / 2;
            double offsetY = (targetHeight - sourceHeight * scale) / 2;

            var box = page.MediaBox;
            var prefix = new PdfContentBuilder()
                .Save()
                .Transform(scale, 0, 0, scale, offsetX - box[0] * scale, offsetY - box[1] * scale)
                .ToArray();

            page.WrapContent(prefix, Encoding.ASCII.GetBytes("Q\n"));
            page.Dictionary.Remove("CropBox");
            page.MediaBox = target;
        }

        document.SetPages(document.Pages);
        StampProducer(document);
        return document.Save(DefaultSave);
    }

    /// <summary>Cắt lề trang theo tỉ lệ phần trăm mỗi cạnh (0–40%).</summary>
    public static byte[] CropPages(byte[] data, double leftPercent, double bottomPercent,
        double rightPercent, double topPercent, string? pageExpression = null, string? password = null)
    {
        var document = Open(data, password);
        var selected = string.IsNullOrWhiteSpace(pageExpression)
            ? Enumerable.Range(1, document.PageCount).ToList()
            : PdfPageRange.Parse(pageExpression, document.PageCount);

        double Clamp(double value) => Math.Clamp(value, 0, 40) / 100.0;
        double left = Clamp(leftPercent), bottom = Clamp(bottomPercent);
        double right = Clamp(rightPercent), top = Clamp(topPercent);

        if (left + right >= 0.9 || bottom + top >= 0.9)
            throw new PdfToolException("Phần cắt quá lớn — trang sẽ không còn nội dung.");

        foreach (int number in selected)
        {
            var page = document.Pages[number - 1];
            var box = page.CropBox ?? page.MediaBox;
            double width = box[2] - box[0], height = box[3] - box[1];

            var cropped = new[]
            {
                box[0] + width * left,
                box[1] + height * bottom,
                box[2] - width * right,
                box[3] - height * top
            };

            page.CropBox = cropped;
            page.MediaBox = cropped;
        }

        document.SetPages(document.Pages);
        StampProducer(document);
        return document.Save(DefaultSave);
    }

    /// <summary>Xếp nhiều trang lên một tờ (2, 4, 6, 9 trang/tờ).</summary>
    public static byte[] NUp(byte[] data, int pagesPerSheet, double[]? sheetSize = null, string? password = null)
    {
        int[] allowed = { 2, 4, 6, 9 };
        if (!allowed.Contains(pagesPerSheet))
            throw new PdfToolException("Chỉ hỗ trợ 2, 4, 6 hoặc 9 trang trên một tờ.");

        var source = Open(data, password);
        var (columns, rows) = pagesPerSheet switch
        {
            2 => (2, 1),
            4 => (2, 2),
            6 => (3, 2),
            _ => (3, 3)
        };

        var sheet = sheetSize ?? (pagesPerSheet == 2
            ? PdfPageSizes.Landscape(PdfPageSizes.A4)
            : (double[])PdfPageSizes.A4.Clone());

        double sheetWidth = sheet[2] - sheet[0];
        double sheetHeight = sheet[3] - sheet[1];
        const double gap = 10;

        double cellWidth = (sheetWidth - gap * (columns + 1)) / columns;
        double cellHeight = (sheetHeight - gap * (rows + 1)) / rows;

        var output = PdfDocument.Create();
        var map = new Dictionary<int, PdfRef>();
        var newPages = new List<PdfPage>();

        for (int start = 0; start < source.PageCount; start += pagesPerSheet)
        {
            var pageDict = new PdfDictionary();
            pageDict.SetName("Type", "Page");
            pageDict["MediaBox"] = sheet.ToPdfArray();
            var target = new PdfPage(pageDict, output);

            var xobjects = new PdfDictionary();
            var content = new PdfContentBuilder();

            for (int slot = 0; slot < pagesPerSheet && start + slot < source.PageCount; slot++)
            {
                var sourcePage = source.Pages[start + slot];
                var formRef = ImportPageAsForm(output, sourcePage, map);

                string name = $"WcP{slot}";
                xobjects[name] = formRef;

                int column = slot % columns;
                int row = slot / columns;

                double cellX = gap + column * (cellWidth + gap);
                double cellY = sheetHeight - gap - (row + 1) * cellHeight - row * gap;

                double scale = Math.Min(cellWidth / Math.Max(1, sourcePage.DisplayWidth),
                                        cellHeight / Math.Max(1, sourcePage.DisplayHeight));
                double drawWidth = sourcePage.DisplayWidth * scale;
                double drawHeight = sourcePage.DisplayHeight * scale;

                content.Save()
                    .Transform(scale, 0, 0, scale,
                        cellX + (cellWidth - drawWidth) / 2,
                        cellY + (cellHeight - drawHeight) / 2)
                    .Raw($"/{name} Do")
                    .Restore();
            }

            var resources = new PdfDictionary();
            resources["XObject"] = xobjects;
            target.Dictionary["Resources"] = resources;
            target.SetContent(content.ToArray());
            newPages.Add(target);
        }

        output.SetPages(newPages);
        CopyMetadata(source, output);
        StampProducer(output);
        return output.Save(DefaultSave);
    }

    /// <summary>Chuyển một trang nguồn thành Form XObject trong tài liệu đích (đã áp góc xoay).</summary>
    internal static PdfRef ImportPageAsForm(PdfDocument target, PdfPage source, Dictionary<int, PdfRef> map)
    {
        var imported = target.ImportPage(source, map);

        var box = imported.MediaBox;
        double width = box[2] - box[0];
        double height = box[3] - box[1];
        int rotate = imported.Rotate;

        // Ma trận đưa nội dung gốc về hệ toạ độ hiển thị bắt đầu tại (0,0).
        double[] matrix = rotate switch
        {
            90 => new double[] { 0, -1, 1, 0, -box[1], width + box[0] },
            180 => new double[] { -1, 0, 0, -1, width + box[0], height + box[1] },
            270 => new double[] { 0, 1, -1, 0, height + box[1], -box[0] },
            _ => new double[] { 1, 0, 0, 1, -box[0], -box[1] }
        };

        var dict = new PdfDictionary();
        dict.SetName("Type", "XObject");
        dict.SetName("Subtype", "Form");
        dict.SetInt("FormType", 1);
        dict["BBox"] = new[] { 0, 0, rotate is 90 or 270 ? height : width, rotate is 90 or 270 ? width : height }.ToPdfArray();
        dict["Matrix"] = matrix.ToPdfArray();
        dict["Resources"] = imported.Dictionary["Resources"] ?? new PdfDictionary();

        var stream = PdfStream.FromDecoded(dict, imported.GetContentBytes());
        return target.Add(stream);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  METADATA / THÔNG TIN
    // ─────────────────────────────────────────────────────────────────────
    private static void CopyMetadata(PdfDocument source, PdfDocument target)
    {
        var info = source.GetInfo();
        if (info == null) return;

        var copy = target.GetOrCreateInfo();
        foreach (var key in new[] { "Title", "Author", "Subject", "Keywords", "Creator", "CreationDate" })
            if (info.GetText(key, source) is { } value && value.Length > 0)
                copy.SetText(key, value);
    }

    private static void StampProducer(PdfDocument document)
    {
        var info = document.GetOrCreateInfo();
        info.SetText("Producer", "Wocel Office PDF Tools");
        info.SetText("ModDate", FormatPdfDate(DateTime.Now));
        if (!info.Has("CreationDate")) info.SetText("CreationDate", FormatPdfDate(DateTime.Now));
    }

    internal static string FormatPdfDate(DateTime value)
    {
        var offset = TimeZoneInfo.Local.GetUtcOffset(value);
        string sign = offset.Ticks >= 0 ? "+" : "-";
        return $"D:{value:yyyyMMddHHmmss}{sign}{Math.Abs(offset.Hours):D2}'{Math.Abs(offset.Minutes):D2}'";
    }

    internal static string? ReadableDate(string? pdfDate)
    {
        if (string.IsNullOrWhiteSpace(pdfDate)) return null;

        var text = pdfDate.Trim();
        if (text.StartsWith("D:", StringComparison.Ordinal)) text = text[2..];

        if (text.Length >= 14
            && DateTime.TryParseExact(text[..14], "yyyyMMddHHmmss", null,
                System.Globalization.DateTimeStyles.None, out var parsed))
            return parsed.ToString("dd/MM/yyyy HH:mm");

        if (text.Length >= 8
            && DateTime.TryParseExact(text[..8], "yyyyMMdd", null,
                System.Globalization.DateTimeStyles.None, out var dateOnly))
            return dateOnly.ToString("dd/MM/yyyy");

        return pdfDate;
    }

    public static PdfMetadata GetMetadata(byte[] data, string? password = null)
    {
        var document = Open(data, password);
        var info = document.GetInfo();

        return new PdfMetadata
        {
            Title = info.GetText("Title", document),
            Author = info.GetText("Author", document),
            Subject = info.GetText("Subject", document),
            Keywords = info.GetText("Keywords", document),
            Creator = info.GetText("Creator", document),
            Producer = info.GetText("Producer", document)
        };
    }

    public static byte[] SetMetadata(byte[] data, PdfMetadata metadata, string? password = null)
    {
        var document = Open(data, password);
        var info = document.GetOrCreateInfo();

        void Apply(string key, string? value)
        {
            if (value == null) return;
            if (value.Length == 0) info.Remove(key);
            else info.SetText(key, value);
        }

        Apply("Title", metadata.Title);
        Apply("Author", metadata.Author);
        Apply("Subject", metadata.Subject);
        Apply("Keywords", metadata.Keywords);
        Apply("Creator", metadata.Creator);
        Apply("Producer", metadata.Producer);
        info.SetText("ModDate", FormatPdfDate(DateTime.Now));

        // XMP cũ sẽ mâu thuẫn với Info mới — bỏ đi để trình đọc dùng Info.
        document.GetCatalog()?.Remove("Metadata");

        return document.Save(DefaultSave);
    }

    public static PdfDocumentSummary Inspect(byte[] data, string fileName, string? password = null)
    {
        var summary = new PdfDocumentSummary
        {
            FileName = fileName,
            FileSizeBytes = data.LongLength
        };

        PdfDocument document;
        try
        {
            document = PdfDocument.Load(data, password);
        }
        catch (Exception ex)
        {
            throw new PdfToolException($"Không đọc được tệp: {ex.Message}");
        }

        summary.Version = document.Version;
        summary.IsEncrypted = document.IsEncrypted || document.Status is PdfLoadStatus.EncryptedNeedsPassword;
        summary.EncryptionDescription = document.EncryptionDescription;
        summary.WasRepaired = document.Status == PdfLoadStatus.Repaired;
        summary.ObjectCount = document.ObjectCount;

        if (document.Status is PdfLoadStatus.EncryptedNeedsPassword or PdfLoadStatus.EncryptedUnsupported)
            return summary;

        summary.PageCount = document.PageCount;

        var info = document.GetInfo();
        summary.Title = info.GetText("Title", document);
        summary.Author = info.GetText("Author", document);
        summary.Subject = info.GetText("Subject", document);
        summary.Keywords = info.GetText("Keywords", document);
        summary.Creator = info.GetText("Creator", document);
        summary.Producer = info.GetText("Producer", document);
        summary.CreationDate = ReadableDate(info.GetText("CreationDate", document));
        summary.ModifiedDate = ReadableDate(info.GetText("ModDate", document));

        var fonts = new HashSet<string>(StringComparer.Ordinal);
        int images = 0;

        foreach (var page in document.Pages)
        {
            summary.PageSizes.Add($"{page.DisplayWidth:F0} × {page.DisplayHeight:F0} pt");

            var fontDict = page.Resources.GetDictionary("Font", document);
            if (fontDict != null)
            {
                foreach (var key in fontDict.Items.Keys)
                    if (fontDict.Get(key, document) is PdfDictionary font)
                        fonts.Add(font.GetName("BaseFont", document) ?? "(không tên)");
            }

            var xobjects = page.Resources.GetDictionary("XObject", document);
            if (xobjects != null)
            {
                foreach (var key in xobjects.Items.Keys)
                    if (xobjects.Get(key, document) is PdfStream stream
                        && stream.Dictionary.GetName("Subtype", document) == "Image")
                        images++;
            }
        }

        summary.ImageCount = images;
        summary.FontCount = fonts.Count;
        summary.FontNames.AddRange(fonts.OrderBy(f => f));
        summary.PageSizes.Sort(StringComparer.Ordinal);

        try { summary.CharacterCount = PdfTextExtractor.ExtractText(document).Length; }
        catch { summary.CharacterCount = 0; }

        return summary;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  SỬA TỆP HỎNG
    // ─────────────────────────────────────────────────────────────────────
    /// <summary>Dựng lại bảng tham chiếu chéo và cây trang cho tệp PDF hỏng.</summary>
    public static (byte[] Data, string Report) Repair(byte[] data, string? password = null)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Load(data, password);
        }
        catch (Exception ex)
        {
            throw new PdfToolException($"Tệp hỏng nặng, không khôi phục được: {ex.Message}");
        }

        if (document.Status is PdfLoadStatus.EncryptedNeedsPassword)
            throw new PdfToolException("Tệp có mật khẩu — hãy nhập mật khẩu trước khi sửa.");

        if (document.PageCount == 0)
            throw new PdfToolException("Không tìm thấy trang nào để khôi phục trong tệp này.");

        document.SetPages(document.Pages);
        StampProducer(document);
        var output = document.Save(DefaultSave);

        var report = new StringBuilder();
        report.AppendLine(document.Status == PdfLoadStatus.Repaired
            ? "Bảng xref hỏng — đã quét lại toàn bộ tệp để dựng lại danh sách đối tượng."
            : "Cấu trúc tệp đọc được bình thường — đã ghi lại tệp sạch.");
        report.AppendLine($"Khôi phục {document.PageCount} trang, {document.ObjectCount} đối tượng.");
        report.AppendLine($"Dung lượng: {data.Length:N0} B → {output.Length:N0} B");

        return (output, report.ToString());
    }

    // ─────────────────────────────────────────────────────────────────────
    //  NÉN
    // ─────────────────────────────────────────────────────────────────────
    public static PdfCompressionResult Compress(byte[] data, PdfCompressionLevel level = PdfCompressionLevel.Balanced, string? password = null)
    {
        var document = Open(data, password);
        int before = document.ObjectCount;
        int recompressed = 0;
        int downscaled = 0;

        if (level != PdfCompressionLevel.Light)
        {
            foreach (var obj in document.Objects.Values.OfType<PdfStream>().ToList())
            {
                // Bỏ qua ảnh nén sẵn (JPEG/JPX) — nén lại chỉ làm hỏng.
                if (obj.ImageFilterName(document) != null) continue;
                if (obj.Dictionary.GetName("Filter", document) == "FlateDecode") continue;

                try
                {
                    var decoded = obj.GetDecodedData(document);
                    if (decoded.Length == 0) continue;
                    obj.SetDecodedData(decoded);
                    recompressed++;
                }
                catch
                {
                    // Stream không giải mã được thì giữ nguyên.
                }
            }
        }

        if (level == PdfCompressionLevel.Strong)
            downscaled = DownscaleImages(document, maxDimension: 1400);

        var options = level switch
        {
            PdfCompressionLevel.Light => new PdfSaveOptions { RemoveUnusedObjects = true },
            _ => PdfSaveOptions.MaxCompression
        };

        StampProducer(document);
        var output = document.Save(options);

        // Nếu "nén" lại to hơn bản gốc thì trả lại bản gốc cho người dùng.
        if (output.LongLength >= data.LongLength && level == PdfCompressionLevel.Light)
            output = data;

        return new PdfCompressionResult
        {
            Data = output,
            OriginalSize = data.LongLength,
            RemovedObjects = Math.Max(0, before - CountReachable(document)),
            RecompressedStreams = recompressed,
            DownscaledImages = downscaled
        };
    }

    private static int CountReachable(PdfDocument document)
    {
        var seen = new HashSet<int>();
        var queue = new Queue<PdfObject>();
        if (document.Trailer["Root"] is { } root) queue.Enqueue(root);
        if (document.Trailer["Info"] is { } info) queue.Enqueue(info);

        while (queue.Count > 0)
        {
            switch (queue.Dequeue())
            {
                case PdfRef reference when seen.Add(reference.Number):
                    if (document.GetObject(reference.Number) is { } target) queue.Enqueue(target);
                    break;
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

        return seen.Count;
    }

    /// <summary>Giảm độ phân giải các ảnh bitmap lớn (chỉ ảnh không nén JPEG).</summary>
    private static int DownscaleImages(PdfDocument document, int maxDimension)
    {
        int count = 0;

        foreach (var stream in document.Objects.Values.OfType<PdfStream>().ToList())
        {
            var dict = stream.Dictionary;
            if (dict.GetName("Subtype", document) != "Image") continue;
            if (stream.ImageFilterName(document) != null) continue;
            if (dict.GetInt("BitsPerComponent", 8, document) != 8) continue;

            int width = dict.GetInt("Width", 0, document);
            int height = dict.GetInt("Height", 0, document);
            if (width <= maxDimension && height <= maxDimension) continue;

            int channels = dict.GetName("ColorSpace", document) switch
            {
                "DeviceGray" => 1,
                "DeviceCMYK" => 4,
                "DeviceRGB" => 3,
                _ => 0
            };
            if (channels == 0) continue;

            byte[] samples;
            try { samples = stream.GetDecodedData(document); }
            catch { continue; }
            if (samples.Length < width * height * channels) continue;

            int factor = Math.Max(2, (int)Math.Ceiling(Math.Max(width, height) / (double)maxDimension));
            int newWidth = Math.Max(1, width / factor);
            int newHeight = Math.Max(1, height / factor);

            var resized = new byte[newWidth * newHeight * channels];
            for (int y = 0; y < newHeight; y++)
            {
                for (int x = 0; x < newWidth; x++)
                {
                    for (int c = 0; c < channels; c++)
                    {
                        int sum = 0, taken = 0;
                        for (int dy = 0; dy < factor; dy++)
                        {
                            int sourceY = y * factor + dy;
                            if (sourceY >= height) break;
                            for (int dx = 0; dx < factor; dx++)
                            {
                                int sourceX = x * factor + dx;
                                if (sourceX >= width) break;
                                sum += samples[(sourceY * width + sourceX) * channels + c];
                                taken++;
                            }
                        }
                        resized[(y * newWidth + x) * channels + c] = (byte)(taken == 0 ? 0 : sum / taken);
                    }
                }
            }

            stream.SetDecodedData(resized);
            dict.SetInt("Width", newWidth);
            dict.SetInt("Height", newHeight);
            count++;
        }

        return count;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  BẢO VỆ / GỠ BẢO VỆ
    // ─────────────────────────────────────────────────────────────────────
    /// <summary>Đặt mật khẩu và giới hạn quyền cho tệp PDF.</summary>
    public static byte[] Protect(byte[] data, PdfEncryptionSettings settings, string? currentPassword = null)
    {
        if (string.IsNullOrEmpty(settings.UserPassword) && string.IsNullOrEmpty(settings.OwnerPassword))
            throw new PdfToolException("Hãy nhập ít nhất một mật khẩu (mở tệp hoặc chủ sở hữu).");

        var document = Open(data, currentPassword);
        StampProducer(document);

        return document.Save(new PdfSaveOptions
        {
            RemoveUnusedObjects = true,
            Encryption = settings
        });
    }

    /// <summary>
    /// Gỡ mật khẩu khỏi tệp — bắt buộc người dùng cung cấp đúng mật khẩu hiện tại.
    /// Wocel không dò tìm mật khẩu.
    /// </summary>
    public static byte[] Unlock(byte[] data, string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new PdfToolException("Hãy nhập mật khẩu hiện tại của tệp. Wocel không dò tìm mật khẩu giúp bạn.");

        var document = Open(data, password);
        if (!document.IsEncrypted && document.Encryption == null)
            throw new PdfToolException("Tệp này vốn không có mật khẩu.");

        StampProducer(document);
        return document.Save(DefaultSave);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  SO SÁNH
    // ─────────────────────────────────────────────────────────────────────
    public static PdfCompareResult Compare(byte[] left, byte[] right, string? leftPassword = null, string? rightPassword = null)
    {
        var a = Open(left, leftPassword);
        var b = Open(right, rightPassword);

        var result = new PdfCompareResult
        {
            PageCountLeft = a.PageCount,
            PageCountRight = b.PageCount
        };

        if (a.PageCount != b.PageCount)
            result.Report.Add($"⚠ Số trang khác nhau: {a.PageCount} và {b.PageCount}.");

        int pages = Math.Max(a.PageCount, b.PageCount);

        for (int i = 0; i < pages; i++)
        {
            var leftLines = i < a.PageCount ? SplitLines(PdfTextExtractor.ExtractPageText(a, a.Pages[i])) : new List<string>();
            var rightLines = i < b.PageCount ? SplitLines(PdfTextExtractor.ExtractPageText(b, b.Pages[i])) : new List<string>();

            var removed = new List<string>(leftLines);
            var added = new List<string>(rightLines);

            foreach (var line in leftLines.ToList())
                if (added.Remove(line)) removed.Remove(line);

            if (removed.Count == 0 && added.Count == 0) continue;

            result.ChangedPages++;
            result.RemovedLines += removed.Count;
            result.AddedLines += added.Count;

            result.Report.Add($"── Trang {i + 1} ──");
            foreach (var line in removed.Take(15)) result.Report.Add($"  − {line}");
            if (removed.Count > 15) result.Report.Add($"  … và {removed.Count - 15} dòng bị bỏ nữa");
            foreach (var line in added.Take(15)) result.Report.Add($"  + {line}");
            if (added.Count > 15) result.Report.Add($"  … và {added.Count - 15} dòng thêm mới nữa");
        }

        if (result.Identical) result.Report.Add("✅ Hai tệp có nội dung văn bản giống hệt nhau.");
        return result;
    }

    private static List<string> SplitLines(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
}
