using System.Text;
using Wocel.Core.Pdf;

namespace Wocel.Core.Services;

/// <summary>Các thao tác thêm/sửa nội dung hiển thị trên trang PDF.</summary>
public static partial class PdfToolkit
{
    // ─────────────────────────────────────────────────────────────────────
    //  WATERMARK
    // ─────────────────────────────────────────────────────────────────────
    public static byte[] AddWatermark(byte[] data, PdfWatermarkOptions options, string? password = null)
    {
        if (string.IsNullOrWhiteSpace(options.Text))
            throw new PdfToolException("Chưa nhập nội dung watermark.");

        var document = Open(data, password);
        var fonts = new PdfFontLibrary(document);
        var font = fonts.Get(options.Bold);

        var selected = string.IsNullOrWhiteSpace(options.PageRange)
            ? Enumerable.Range(1, document.PageCount).ToList()
            : PdfPageRange.Parse(options.PageRange, document.PageCount);

        if (selected.Count == 0)
            throw new PdfToolException("Không có trang nào khớp lựa chọn.");

        double opacity = Math.Clamp(options.Opacity, 0.05, 1.0);

        foreach (int number in selected)
        {
            var page = document.Pages[number - 1];
            double width = page.WidthPoints;
            double height = page.HeightPoints;
            var box = page.MediaBox;

            var content = new PdfContentBuilder();
            content.Save();

            string alphaName = EnsureAlphaState(document, page, opacity);
            content.SetExtGState(alphaName);
            content.SetFillColor(options.Color.R, options.Color.G, options.Color.B);

            switch (options.Layout)
            {
                case PdfWatermarkLayout.Tiled:
                {
                    double size = Math.Max(8, options.FontSize * 0.5);
                    double textWidth = font.Measure(options.Text, size);
                    double stepX = textWidth + 60;
                    double stepY = size * 4;

                    for (double y = box[1]; y < box[1] + height + stepY; y += stepY)
                    {
                        for (double x = box[0] - textWidth; x < box[0] + width + stepX; x += stepX)
                        {
                            content.Save()
                                .Rotate(30, x, y)
                                .Text(font, size, 0, 0, options.Text)
                                .Restore();
                        }
                    }
                    break;
                }

                case PdfWatermarkLayout.Center:
                {
                    double size = FitFontSize(font, options.Text, options.FontSize, width * 0.8);
                    double textWidth = font.Measure(options.Text, size);
                    content.Text(font, size,
                        box[0] + (width - textWidth) / 2,
                        box[1] + height / 2 - size / 2,
                        options.Text);
                    break;
                }

                default:
                {
                    double diagonal = Math.Sqrt(width * width + height * height);
                    double size = FitFontSize(font, options.Text, options.FontSize, diagonal * 0.8);
                    double textWidth = font.Measure(options.Text, size);

                    content.Rotate(options.AngleDegrees, box[0] + width / 2, box[1] + height / 2)
                        .Text(font, size, -textWidth / 2, -size / 3, options.Text);
                    break;
                }
            }

            content.Restore();
            fonts.ApplyTo(page);
            page.AddContentLayer(content.ToArray(), onTop: !options.BehindContent);
        }

        fonts.Flush();
        document.SetPages(document.Pages);
        StampProducer(document);
        return document.Save(DefaultSave);
    }

    private static double FitFontSize(PdfFontResource font, string text, double desired, double maxWidth)
    {
        double size = desired;
        while (size > 6 && font.Measure(text, size) > maxWidth) size -= 1;
        return size;
    }

    /// <summary>Khai báo trạng thái đồ hoạ trong suốt cho trang và trả về tên tài nguyên.</summary>
    private static string EnsureAlphaState(PdfDocument document, PdfPage page, double alpha)
    {
        string name = $"WcGS{(int)Math.Round(alpha * 100)}";
        var resources = page.Resources;

        var states = resources.GetDictionary("ExtGState", document);
        if (states == null)
        {
            states = new PdfDictionary();
            resources["ExtGState"] = states;
        }

        if (!states.Has(name))
        {
            var state = new PdfDictionary();
            state.SetName("Type", "ExtGState");
            state.SetReal("ca", Math.Round(alpha, 3));
            state.SetReal("CA", Math.Round(alpha, 3));
            states[name] = state;
        }

        return name;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  SỐ TRANG
    // ─────────────────────────────────────────────────────────────────────
    public static byte[] AddPageNumbers(byte[] data, PdfPageNumberOptions options, string? password = null)
    {
        var document = Open(data, password);
        var fonts = new PdfFontLibrary(document);
        var font = fonts.Regular;

        var selected = string.IsNullOrWhiteSpace(options.PageRange)
            ? Enumerable.Range(1, document.PageCount).ToList()
            : PdfPageRange.Parse(options.PageRange, document.PageCount);

        int total = document.PageCount;
        int printed = 0;

        for (int index = 0; index < document.Pages.Count; index++)
        {
            int pageNumber = index + 1;
            if (!selected.Contains(pageNumber)) continue;
            if (options.SkipFirstPage && pageNumber == 1) continue;

            var page = document.Pages[index];
            string label;
            try
            {
                label = string.Format(options.Format, options.StartNumber + printed, total);
            }
            catch (FormatException)
            {
                label = (options.StartNumber + printed).ToString();
            }

            printed++;

            var (x, y) = ResolvePosition(page, options.Position, font.Measure(label, options.FontSize),
                options.FontSize, options.Margin);

            var content = new PdfContentBuilder()
                .Save()
                .SetFillColor(options.Color.R, options.Color.G, options.Color.B)
                .Text(font, options.FontSize, x, y, label)
                .Restore()
                .ToArray();

            fonts.ApplyTo(page);
            page.AddContentLayer(content, onTop: true);
        }

        if (printed == 0)
            throw new PdfToolException("Không có trang nào được đánh số với lựa chọn hiện tại.");

        fonts.Flush();
        document.SetPages(document.Pages);
        StampProducer(document);
        return document.Save(DefaultSave);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  ĐẦU TRANG / CHÂN TRANG
    // ─────────────────────────────────────────────────────────────────────
    public static byte[] AddHeaderFooter(byte[] data, PdfHeaderFooterOptions options, string? password = null)
    {
        bool hasAny = new[]
        {
            options.HeaderLeft, options.HeaderCenter, options.HeaderRight,
            options.FooterLeft, options.FooterCenter, options.FooterRight
        }.Any(v => !string.IsNullOrWhiteSpace(v));

        if (!hasAny)
            throw new PdfToolException("Chưa nhập nội dung đầu trang hoặc chân trang nào.");

        var document = Open(data, password);
        var fonts = new PdfFontLibrary(document);
        var font = fonts.Regular;
        int total = document.PageCount;

        for (int index = 0; index < document.Pages.Count; index++)
        {
            var page = document.Pages[index];
            var content = new PdfContentBuilder();
            content.Save().SetFillColor(options.Color.R, options.Color.G, options.Color.B);

            void Draw(string? template, PdfBoxPosition position)
            {
                if (string.IsNullOrWhiteSpace(template)) return;

                string text = ExpandPlaceholders(template, index + 1, total, options.FileName);
                double textWidth = font.Measure(text, options.FontSize);
                var (x, y) = ResolvePosition(page, position, textWidth, options.FontSize, options.Margin);
                content.Text(font, options.FontSize, x, y, text);
            }

            Draw(options.HeaderLeft, PdfBoxPosition.TopLeft);
            Draw(options.HeaderCenter, PdfBoxPosition.TopCenter);
            Draw(options.HeaderRight, PdfBoxPosition.TopRight);
            Draw(options.FooterLeft, PdfBoxPosition.BottomLeft);
            Draw(options.FooterCenter, PdfBoxPosition.BottomCenter);
            Draw(options.FooterRight, PdfBoxPosition.BottomRight);

            content.Restore();
            fonts.ApplyTo(page);
            page.AddContentLayer(content.ToArray(), onTop: true);
        }

        fonts.Flush();
        document.SetPages(document.Pages);
        StampProducer(document);
        return document.Save(DefaultSave);
    }

    private static string ExpandPlaceholders(string template, int page, int total, string fileName) =>
        template
            .Replace("{page}", page.ToString())
            .Replace("{pages}", total.ToString())
            .Replace("{date}", DateTime.Now.ToString("dd/MM/yyyy"))
            .Replace("{time}", DateTime.Now.ToString("HH:mm"))
            .Replace("{file}", fileName);

    private static (double x, double y) ResolvePosition(
        PdfPage page, PdfBoxPosition position, double textWidth, double fontSize, double margin)
    {
        var box = page.CropBox ?? page.MediaBox;
        double left = box[0], bottom = box[1];
        double width = box[2] - box[0], height = box[3] - box[1];

        double x = position switch
        {
            PdfBoxPosition.TopLeft or PdfBoxPosition.BottomLeft => left + margin,
            PdfBoxPosition.TopRight or PdfBoxPosition.BottomRight => left + width - margin - textWidth,
            _ => left + (width - textWidth) / 2
        };

        double y = position switch
        {
            PdfBoxPosition.TopLeft or PdfBoxPosition.TopCenter or PdfBoxPosition.TopRight
                => bottom + height - margin - fontSize,
            PdfBoxPosition.Center => bottom + height / 2 - fontSize / 2,
            _ => bottom + margin - fontSize * 0.25
        };

        return (x, y);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  LÀM PHẲNG
    // ─────────────────────────────────────────────────────────────────────
    /// <summary>
    /// "In" chú thích và trường biểu mẫu thẳng vào nội dung trang rồi xoá chúng đi,
    /// giúp tệp hiển thị giống nhau ở mọi trình đọc và không còn chỉnh sửa được.
    /// </summary>
    public static (byte[] Data, int FlattenedCount) Flatten(byte[] data, string? password = null)
    {
        var document = Open(data, password);
        int flattened = 0;

        foreach (var page in document.Pages)
        {
            var annotations = page.Dictionary.GetArray("Annots", document);
            if (annotations == null || annotations.Count == 0) continue;

            var xobjects = page.Resources.GetDictionary("XObject", document);
            if (xobjects == null)
            {
                xobjects = new PdfDictionary();
                page.Resources["XObject"] = xobjects;
            }

            var content = new PdfContentBuilder();
            int slot = 0;

            foreach (var item in annotations.Items)
            {
                if (document.Resolve(item) is not PdfDictionary annotation) continue;
                if (annotation.GetName("Subtype", document) is "Link" or "Popup") continue;

                // Chú thích ẩn (bit 2 của /F) thì bỏ qua.
                int flags = annotation.GetInt("F", 0, document);
                if ((flags & 2) != 0) continue;

                var appearance = annotation.GetDictionary("AP", document);
                var normal = appearance?.Get("N", document);

                if (normal is PdfDictionary states)
                {
                    var stateName = annotation.GetName("AS", document);
                    normal = stateName != null ? states.Get(stateName, document) : states.Items.Values.FirstOrDefault();
                    normal = document.Resolve(normal);
                }

                if (normal is not PdfStream form) continue;

                var rect = annotation["Rect"].AsRectangle(document);
                if (rect == null) continue;

                var bbox = form.Dictionary["BBox"].AsRectangle(document) ?? new[] { 0.0, 0.0, 1.0, 1.0 };
                double bboxWidth = Math.Max(0.001, bbox[2] - bbox[0]);
                double bboxHeight = Math.Max(0.001, bbox[3] - bbox[1]);

                double scaleX = (rect[2] - rect[0]) / bboxWidth;
                double scaleY = (rect[3] - rect[1]) / bboxHeight;

                string name = $"WcAn{slot++}";
                xobjects[name] = document.Add(form);

                content.Save()
                    .Transform(scaleX, 0, 0, scaleY, rect[0] - bbox[0] * scaleX, rect[1] - bbox[1] * scaleY)
                    .Raw($"/{name} Do")
                    .Restore();

                flattened++;
            }

            if (slot > 0) page.AddContentLayer(content.ToArray(), onTop: true);
            page.Dictionary.Remove("Annots");
        }

        document.GetCatalog()?.Remove("AcroForm");
        document.SetPages(document.Pages);
        StampProducer(document);
        return (document.Save(DefaultSave), flattened);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  TÌM / THAY THẾ / BÔI ĐEN
    // ─────────────────────────────────────────────────────────────────────
    /// <summary>Tìm các đoạn văn bản khớp trên từng trang, trả về vị trí để hiển thị hoặc bôi đen.</summary>
    public static List<(int Page, PdfTextFragment Fragment)> FindText(byte[] data, string term, bool caseSensitive = false, string? password = null)
    {
        if (string.IsNullOrEmpty(term)) return new List<(int, PdfTextFragment)>();

        var document = Open(data, password);
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var results = new List<(int, PdfTextFragment)>();

        for (int i = 0; i < document.PageCount; i++)
        {
            foreach (var fragment in PdfTextExtractor.ExtractFragments(document, document.Pages[i]))
                if (fragment.Text.Contains(term, comparison))
                    results.Add((i + 1, fragment));
        }

        return results;
    }

    /// <summary>
    /// Bôi đen (redact): xoá hẳn chuỗi khớp khỏi content stream rồi phủ hộp đen lên vị trí cũ,
    /// nên nội dung không còn sao chép hay tìm kiếm được nữa.
    /// </summary>
    public static (byte[] Data, int Count) Redact(byte[] data, IEnumerable<string> terms, bool caseSensitive = false, string? password = null)
    {
        var termList = terms.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (termList.Count == 0)
            throw new PdfToolException("Chưa nhập nội dung cần bôi đen.");

        var document = Open(data, password);
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int redacted = 0;

        foreach (var page in document.Pages)
        {
            var boxes = new List<PdfTextFragment>();

            foreach (var fragment in PdfTextExtractor.ExtractFragments(document, page))
                if (termList.Any(term => fragment.Text.Contains(term, comparison)))
                    boxes.Add(fragment);

            if (boxes.Count == 0) continue;

            int removed = PdfContentRewriter.RemoveText(document, page, termList, caseSensitive);
            redacted += Math.Max(removed, boxes.Count);

            var content = new PdfContentBuilder();
            content.Save().SetFillColor(0, 0, 0);

            foreach (var box in boxes)
            {
                double padding = box.FontSize * 0.2;
                content.Rectangle(box.X - padding, box.Y - box.FontSize * 0.25,
                    Math.Max(box.Width, box.FontSize) + padding * 2, box.FontSize * 1.25, fill: true, stroke: false);
            }

            content.Restore();
            page.AddContentLayer(content.ToArray(), onTop: true);
        }

        if (redacted == 0)
            throw new PdfToolException("Không tìm thấy nội dung nào khớp để bôi đen.");

        document.SetPages(document.Pages);
        StampProducer(document);
        return (document.Save(DefaultSave), redacted);
    }

    /// <summary>Thay thế văn bản trong PDF (áp dụng cho chữ nằm trong content stream).</summary>
    public static (byte[] Data, int Count) ReplaceText(byte[] data, string search, string replacement, bool caseSensitive = false, string? password = null)
    {
        if (string.IsNullOrEmpty(search))
            throw new PdfToolException("Chưa nhập nội dung cần tìm.");

        var document = Open(data, password);
        var fallbackFonts = new PdfFontLibrary(document);
        int count = 0;

        foreach (var page in document.Pages)
            count += PdfContentRewriter.ReplaceText(document, page, search, replacement, caseSensitive, fallbackFonts);

        if (count == 0)
            throw new PdfToolException($"Không tìm thấy “{search}” trong tệp.");

        fallbackFonts.Flush();
        document.SetPages(document.Pages);
        StampProducer(document);
        return (document.Save(DefaultSave), count);
    }
}
