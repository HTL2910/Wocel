using System.Text;
using Wocel.Core.Models;
using Wocel.Core.Pdf;

namespace Wocel.Core.Services;

/// <summary>Chuyển đổi qua lại giữa PDF và văn bản, bảng, ảnh.</summary>
public static partial class PdfToolkit
{
    // ─────────────────────────────────────────────────────────────────────
    //  VĂN BẢN → PDF
    // ─────────────────────────────────────────────────────────────────────
    public static byte[] TextToPdf(string text, PdfTextToPdfOptions? options = null)
    {
        options ??= new PdfTextToPdfOptions();

        var document = PdfDocument.Create();
        var fonts = new PdfFontLibrary(document);
        var body = fonts.Regular;
        var heading = fonts.Bold;

        double pageWidth = options.PageSize[2] - options.PageSize[0];
        double pageHeight = options.PageSize[3] - options.PageSize[1];
        double usableWidth = pageWidth - options.Margin * 2;
        double leading = options.FontSize * options.LineSpacing;

        if (usableWidth <= 20) throw new PdfToolException("Lề quá lớn so với khổ giấy.");

        var pages = new List<PdfPage>();
        var currentLines = new List<string>();
        double cursorY = pageHeight - options.Margin;
        bool firstPage = true;

        PdfPage StartPage()
        {
            var dict = new PdfDictionary();
            dict.SetName("Type", "Page");
            dict["MediaBox"] = options.PageSize.ToPdfArray();
            return new PdfPage(dict, document);
        }

        var page = StartPage();
        var content = new PdfContentBuilder();
        content.SetFillColor(0, 0, 0);

        void FlushLines()
        {
            if (currentLines.Count == 0) return;
            content.TextBlock(body, options.FontSize, options.Margin, cursorY, leading, currentLines);
            cursorY -= leading * currentLines.Count;
            currentLines.Clear();
        }

        void FinishPage()
        {
            FlushLines();
            fonts.ApplyTo(page);
            page.SetContent(content.ToArray());
            pages.Add(page);
        }

        void NewPage()
        {
            FinishPage();
            page = StartPage();
            content = new PdfContentBuilder();
            content.SetFillColor(0, 0, 0);
            cursorY = pageHeight - options.Margin;
        }

        if (options.ShowTitleOnFirstPage && !string.IsNullOrWhiteSpace(options.Title))
        {
            double titleSize = options.FontSize + 6;
            content.Text(heading, titleSize, options.Margin, cursorY - titleSize, options.Title);
            cursorY -= titleSize * 2;
        }

        foreach (var rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            foreach (var segment in rawLine.Split('\f'))
            {
                foreach (var line in WrapText(segment, body, options.FontSize, usableWidth))
                {
                    if (cursorY - leading * (currentLines.Count + 1) < options.Margin)
                    {
                        NewPage();
                        firstPage = false;
                    }

                    currentLines.Add(line);
                }
            }

            if (rawLine.Contains('\f'))
            {
                NewPage();
                firstPage = false;
            }
        }

        _ = firstPage;
        FinishPage();

        if (pages.Count == 0) pages.Add(StartPage());

        document.SetPages(pages);
        fonts.Flush();

        var info = document.GetOrCreateInfo();
        if (!string.IsNullOrWhiteSpace(options.Title)) info.SetText("Title", options.Title);
        StampProducer(document);

        return document.Save(DefaultSave);
    }

    /// <summary>Ngắt dòng theo bề rộng thật của font, ưu tiên ngắt ở khoảng trắng.</summary>
    internal static List<string> WrapText(string text, PdfFontResource font, double fontSize, double maxWidth)
    {
        var lines = new List<string>();
        if (text.Length == 0) { lines.Add(string.Empty); return lines; }

        text = text.Replace("\t", "    ");
        var words = text.Split(' ');
        var current = new StringBuilder();

        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;

            if (font.Measure(candidate, fontSize) <= maxWidth)
            {
                current.Clear().Append(candidate);
                continue;
            }

            if (current.Length > 0)
            {
                lines.Add(current.ToString());
                current.Clear();
            }

            // Từ dài hơn cả dòng: cắt cứng theo ký tự.
            var chunk = new StringBuilder();
            foreach (char c in word)
            {
                if (font.Measure(chunk.ToString() + c, fontSize) > maxWidth && chunk.Length > 0)
                {
                    lines.Add(chunk.ToString());
                    chunk.Clear();
                }
                chunk.Append(c);
            }

            current.Append(chunk);
        }

        lines.Add(current.ToString());
        return lines;
    }


    // ─────────────────────────────────────────────────────────────────────
    //  TÀI LIỆU VĂN BẢN → PDF
    // ─────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Xuất tài liệu văn bản ra PDF, giữ đậm/nghiêng/gạch chân và cỡ chữ của
    /// từng đoạn chữ. Ngắt dòng theo bề rộng thật của phông đang dùng.
    /// </summary>
    public static byte[] DocumentToPdf(DocumentDocument source, PdfTextToPdfOptions? options = null)
    {
        options ??= new PdfTextToPdfOptions();

        var document = PdfDocument.Create();
        var fonts = new PdfFontLibrary(document);

        double pageWidth = options.PageSize[2] - options.PageSize[0];
        double pageHeight = options.PageSize[3] - options.PageSize[1];
        double usableWidth = pageWidth - options.Margin * 2;
        if (usableWidth <= 20) throw new PdfToolException("Lề quá lớn so với khổ giấy.");

        var pages = new List<PdfPage>();
        PdfPage? page = null;
        PdfContentBuilder? content = null;
        double cursorY = 0;

        PdfPage StartPage()
        {
            var dict = new PdfDictionary();
            dict.SetName("Type", "Page");
            dict["MediaBox"] = options.PageSize.ToPdfArray();
            return new PdfPage(dict, document);
        }

        void FinishPage()
        {
            if (page == null || content == null) return;
            fonts.ApplyTo(page);
            page.SetContent(content.ToArray());
            pages.Add(page);
        }

        void NewPage()
        {
            FinishPage();
            page = StartPage();
            content = new PdfContentBuilder();
            content.SetFillColor(0, 0, 0);
            cursorY = pageHeight - options.Margin;
        }

        NewPage();

        if (options.ShowTitleOnFirstPage && !string.IsNullOrWhiteSpace(options.Title))
        {
            double titleSize = options.FontSize + 6;
            content!.Text(fonts.Bold, titleSize, options.Margin, cursorY - titleSize, options.Title);
            cursorY -= titleSize * 2;
        }

        foreach (var block in source.Blocks)
        {
            // Mỗi đoạn chữ giữ định dạng riêng, nên phải xếp từng mẩu một.
            var pieces = new List<(string Text, TextRun Run)>();
            foreach (var run in block.Inlines)
                foreach (var token in SplitKeepingSpaces(run.Text))
                    pieces.Add((token, run));

            if (pieces.Count == 0)
            {
                cursorY -= options.FontSize * options.LineSpacing;
                if (cursorY < options.Margin) NewPage();
                continue;
            }

            double x = options.Margin;
            double lineHeight = 0;

            foreach (var (token, run) in pieces)
            {
                var font = run.IsBold ? fonts.Bold : fonts.Regular;
                double size = run.FontSize is > 0 ? run.FontSize.Value : options.FontSize;
                double width = font.Measure(token, size);

                if (x > options.Margin && x + width > options.Margin + usableWidth && token.Trim().Length > 0)
                {
                    cursorY -= Math.Max(lineHeight, size) * options.LineSpacing;
                    if (cursorY < options.Margin) NewPage();
                    x = options.Margin;
                    lineHeight = 0;
                }

                if (token.Trim().Length > 0)
                {
                    content!.Text(font, size, x, cursorY - size, token);

                    if (run.IsUnderline)
                    {
                        double underlineY = cursorY - size - 2;
                        content.SetLineWidth(Math.Max(0.5, size / 16))
                               .Line(x, underlineY, x + width, underlineY);
                    }
                }

                x += width;
                lineHeight = Math.Max(lineHeight, size);
            }

            cursorY -= Math.Max(lineHeight, options.FontSize) * options.LineSpacing;
            if (cursorY < options.Margin) NewPage();
        }

        FinishPage();
        if (pages.Count == 0) pages.Add(StartPage());

        document.SetPages(pages);
        fonts.Flush();

        var info = document.GetOrCreateInfo();
        if (!string.IsNullOrWhiteSpace(options.Title)) info.SetText("Title", options.Title);
        StampProducer(document);

        return document.Save(DefaultSave);
    }

    /// <summary>Tách chuỗi thành từng từ, giữ khoảng trắng dính vào cuối từ để ngắt dòng đúng.</summary>
    private static IEnumerable<string> SplitKeepingSpaces(string text)
    {
        if (text.Length == 0) yield break;

        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != ' ') continue;

            int end = i;
            while (end < text.Length && text[end] == ' ') end++;

            yield return text[start..end];
            start = end;
            i = end - 1;
        }

        if (start < text.Length) yield return text[start..];
    }

    // ─────────────────────────────────────────────────────────────────────
    //  ẢNH → PDF
    // ─────────────────────────────────────────────────────────────────────
    public static byte[] ImagesToPdf(IEnumerable<(string FileName, byte[] Data)> images, PdfImageToPdfOptions? options = null)
    {
        options ??= new PdfImageToPdfOptions();

        var list = images.ToList();
        if (list.Count == 0) throw new PdfToolException("Chưa chọn ảnh nào.");

        var document = PdfDocument.Create();
        var pages = new List<PdfPage>();
        var failures = new List<string>();

        foreach (var (fileName, data) in list)
        {
            RasterImage image;
            try
            {
                image = PdfImageCodec.ReadImageFile(data, Path.GetExtension(fileName));
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(fileName)}: {ex.Message}");
                continue;
            }

            var imageRef = PdfImageCodec.CreateImageXObject(document, image);

            double[] pageBox;
            double drawX, drawY, drawWidth, drawHeight;

            if (options.Fit == PdfImageFit.MatchImage)
            {
                // 1 pixel = 1 point ở 72 DPI; ảnh lớn được thu về khổ hợp lý.
                double scale = Math.Min(1, 1684.0 / Math.Max(image.Width, image.Height));
                drawWidth = image.Width * scale;
                drawHeight = image.Height * scale;
                pageBox = new[] { 0, 0, drawWidth + options.Margin * 2, drawHeight + options.Margin * 2 };
                drawX = options.Margin;
                drawY = options.Margin;
            }
            else
            {
                var size = (double[])options.PageSize.Clone();
                if (options.AutoOrientation && image.Width > image.Height)
                    size = PdfPageSizes.Landscape(size);

                pageBox = size;
                double usableWidth = size[2] - size[0] - options.Margin * 2;
                double usableHeight = size[3] - size[1] - options.Margin * 2;

                double scale = Math.Min(usableWidth / image.Width, usableHeight / image.Height);
                drawWidth = image.Width * scale;
                drawHeight = image.Height * scale;
                drawX = size[0] + (size[2] - size[0] - drawWidth) / 2;
                drawY = size[1] + (size[3] - size[1] - drawHeight) / 2;
            }

            var pageDict = new PdfDictionary();
            pageDict.SetName("Type", "Page");
            pageDict["MediaBox"] = pageBox.ToPdfArray();

            var page = new PdfPage(pageDict, document);
            var xobjects = new PdfDictionary();
            xobjects["WcIm0"] = imageRef;
            var resources = new PdfDictionary();
            resources["XObject"] = xobjects;
            page.Dictionary["Resources"] = resources;

            page.SetContent(new PdfContentBuilder()
                .Image("WcIm0", drawX, drawY, drawWidth, drawHeight)
                .ToArray());

            pages.Add(page);
        }

        if (pages.Count == 0)
            throw new PdfToolException("Không đọc được ảnh nào.\n" + string.Join("\n", failures));

        document.SetPages(pages);
        StampProducer(document);
        return document.Save(DefaultSave);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  BẢNG → PDF
    // ─────────────────────────────────────────────────────────────────────
    public static byte[] TableToPdf(IReadOnlyList<IReadOnlyList<string>> rows, PdfTableToPdfOptions? options = null)
    {
        options ??= new PdfTableToPdfOptions();
        if (rows.Count == 0) throw new PdfToolException("Bảng không có dữ liệu.");

        var document = PdfDocument.Create();
        var fonts = new PdfFontLibrary(document);
        var body = fonts.Regular;
        var bold = fonts.Bold;

        int columnCount = rows.Max(r => r.Count);
        double pageWidth = options.PageSize[2] - options.PageSize[0];
        double pageHeight = options.PageSize[3] - options.PageSize[1];
        double usableWidth = pageWidth - options.Margin * 2;

        // Bề rộng cột tỉ lệ theo nội dung dài nhất, có chặn trên/dưới.
        var weights = new double[columnCount];
        for (int c = 0; c < columnCount; c++)
        {
            double longest = 1;
            foreach (var row in rows)
            {
                if (c >= row.Count) continue;
                longest = Math.Max(longest, body.Measure(row[c] ?? string.Empty, options.FontSize));
            }
            weights[c] = Math.Clamp(longest, options.FontSize * 3, usableWidth * 0.45);
        }

        double totalWeight = weights.Sum();
        var columnWidths = weights.Select(w => w / totalWeight * usableWidth).ToArray();

        double rowHeight = options.FontSize * 1.9;
        double cellPadding = options.FontSize * 0.35;

        var pages = new List<PdfPage>();
        PdfPage? page = null;
        PdfContentBuilder? content = null;
        double cursorY = 0;

        void NewPage()
        {
            if (page != null && content != null)
            {
                fonts.ApplyTo(page);
                page.SetContent(content.ToArray());
                pages.Add(page);
            }

            var dict = new PdfDictionary();
            dict.SetName("Type", "Page");
            dict["MediaBox"] = options.PageSize.ToPdfArray();
            page = new PdfPage(dict, document);
            content = new PdfContentBuilder();
            cursorY = pageHeight - options.Margin;

            if (!string.IsNullOrWhiteSpace(options.Title))
            {
                double titleSize = options.FontSize + 5;
                content.SetFillColor(0.1, 0.1, 0.1)
                    .Text(bold, titleSize, options.Margin, cursorY - titleSize, options.Title);
                cursorY -= titleSize * 2;
            }
        }

        void DrawRow(IReadOnlyList<string> row, bool header)
        {
            double y = cursorY - rowHeight;

            if (header)
            {
                content!.SetFillColor(0.90, 0.94, 0.91)
                    .Rectangle(options.Margin, y, usableWidth, rowHeight, fill: true, stroke: false);
            }

            content!.SetStrokeColor(0.75, 0.78, 0.76).SetLineWidth(0.5);
            content.Rectangle(options.Margin, y, usableWidth, rowHeight, fill: false, stroke: true);

            double x = options.Margin;
            var font = header ? bold : body;
            content.SetFillColor(header ? 0.05 : 0.15, header ? 0.3 : 0.15, header ? 0.15 : 0.15);

            for (int c = 0; c < columnCount; c++)
            {
                if (c > 0) content.Line(x, y, x, y + rowHeight);

                var text = c < row.Count ? row[c] ?? string.Empty : string.Empty;
                text = font.Truncate(text.Replace("\n", " ").Trim(), options.FontSize, columnWidths[c] - cellPadding * 2);
                if (text.Length > 0)
                    content.Text(font, options.FontSize, x + cellPadding, y + rowHeight / 2 - options.FontSize * 0.35, text);

                x += columnWidths[c];
            }

            cursorY = y;
        }

        NewPage();

        bool hasHeader = options.FirstRowIsHeader && rows.Count > 0;
        if (hasHeader) DrawRow(rows[0], header: true);

        for (int i = hasHeader ? 1 : 0; i < rows.Count; i++)
        {
            if (cursorY - rowHeight < options.Margin)
            {
                NewPage();
                if (hasHeader && options.RepeatHeader) DrawRow(rows[0], header: true);
            }

            DrawRow(rows[i], header: false);
        }

        if (page != null && content != null)
        {
            fonts.ApplyTo(page);
            page.SetContent(content.ToArray());
            pages.Add(page);
        }

        document.SetPages(pages);
        fonts.Flush();

        if (!string.IsNullOrWhiteSpace(options.Title))
            document.GetOrCreateInfo().SetText("Title", options.Title);
        StampProducer(document);

        return document.Save(DefaultSave);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  PDF → VĂN BẢN / BẢNG / ẢNH
    // ─────────────────────────────────────────────────────────────────────
    public static string ExtractText(byte[] data, string? password = null)
    {
        var document = Open(data, password);
        return PdfTextExtractor.ExtractText(document);
    }

    public static List<string> ExtractTextPerPage(byte[] data, string? password = null)
    {
        var document = Open(data, password);
        return document.Pages.Select(p => PdfTextExtractor.ExtractPageText(document, p)).ToList();
    }

    /// <summary>
    /// Nhận diện bảng bằng cách gom các đoạn chữ theo dòng rồi tách cột dựa trên
    /// khoảng trống ngang — phù hợp với hoá đơn, bảng kê, báo cáo.
    /// </summary>
    public static List<List<string>> ExtractTables(byte[] data, string? password = null)
    {
        var document = Open(data, password);
        var rows = new List<List<string>>();

        foreach (var page in document.Pages)
        {
            var fragments = PdfTextExtractor.ExtractFragments(document, page);
            if (fragments.Count == 0) continue;

            var columnBoundaries = DetectColumns(fragments);

            foreach (var line in PdfTextExtractor.GroupIntoLines(fragments))
            {
                var cells = new string[Math.Max(1, columnBoundaries.Count)];
                for (int i = 0; i < cells.Length; i++) cells[i] = string.Empty;

                foreach (var fragment in line)
                {
                    int column = 0;
                    for (int i = 0; i < columnBoundaries.Count; i++)
                        if (fragment.X >= columnBoundaries[i] - 1) column = i;

                    cells[column] = string.IsNullOrEmpty(cells[column])
                        ? fragment.Text.Trim()
                        : cells[column] + " " + fragment.Text.Trim();
                }

                if (cells.Any(c => c.Length > 0)) rows.Add(cells.ToList());
            }
        }

        return rows;
    }

    /// <summary>Tìm các mốc bắt đầu cột bằng cách gom toạ độ X của mọi đoạn chữ.</summary>
    private static List<double> DetectColumns(List<PdfTextFragment> fragments)
    {
        var starts = fragments.Select(f => Math.Round(f.X, 0)).OrderBy(x => x).ToList();
        var columns = new List<double>();

        foreach (double x in starts)
        {
            if (columns.Count == 0 || x - columns[^1] > 12) columns.Add(x);
        }

        // Quá nhiều "cột" nghĩa là văn bản chảy tự do, không phải bảng.
        return columns.Count > 24 ? new List<double> { columns[0] } : columns;
    }

    public static string ToCsv(IReadOnlyList<IReadOnlyList<string>> rows, char separator = ',')
    {
        var sb = new StringBuilder();

        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(separator, row.Select(cell =>
            {
                var value = cell ?? string.Empty;
                bool needsQuotes = value.Contains(separator) || value.Contains('"') || value.Contains('\n');
                return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
            })));
        }

        return sb.ToString();
    }

    public static List<List<string>> ParseCsv(string content, char separator = ',')
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"') { cell.Append('"'); i++; }
                    else inQuotes = false;
                }
                else
                {
                    cell.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case var _ when c == separator:
                    row.Add(cell.ToString());
                    cell.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(cell.ToString());
                    cell.Clear();
                    rows.Add(row);
                    row = new List<string>();
                    break;
                default:
                    cell.Append(c);
                    break;
            }
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }

        return rows.Where(r => r.Any(c => c.Trim().Length > 0)).ToList();
    }

    public static List<PdfExtractedImage> ExtractImages(byte[] data, string baseName = "image", string? password = null)
    {
        var document = Open(data, password);
        var images = PdfImageCodec.ExtractImages(document, baseName);

        if (images.Count == 0)
            throw new PdfToolException("Không tìm thấy ảnh nhúng nào trong tệp PDF này.");

        return images;
    }
}
