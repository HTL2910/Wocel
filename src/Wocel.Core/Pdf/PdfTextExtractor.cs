using System.Text;

namespace Wocel.Core.Pdf;

/// <summary>Một đoạn văn bản kèm vị trí trên trang (đơn vị point, gốc ở góc dưới-trái).</summary>
public sealed class PdfTextFragment
{
    public string Text { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double FontSize { get; set; }
    public string FontName { get; set; } = string.Empty;

    public double Right => X + Width;
    public override string ToString() => $"({X:F1},{Y:F1}) {Text}";
}

/// <summary>
/// Trích xuất văn bản thật từ content stream của PDF: đọc toán tử hiển thị chữ,
/// áp ma trận văn bản để biết toạ độ, và dùng ToUnicode/Encoding của font để ra Unicode.
/// </summary>
public static class PdfTextExtractor
{
    private sealed class TextState
    {
        public PdfFontInfo? Font;
        public string FontName = string.Empty;
        public double FontSize = 12;
        public double CharSpacing;
        public double WordSpacing;
        public double HorizontalScale = 1;
        public double Leading;
        public double Rise;
    }

    private static readonly double[] Identity = { 1, 0, 0, 1, 0, 0 };

    /// <summary>Toàn bộ văn bản của tài liệu, các trang cách nhau bằng ngắt trang.</summary>
    public static string ExtractText(PdfDocument document, string pageSeparator = "\n\f\n")
    {
        var pages = document.Pages;
        var sb = new StringBuilder();

        for (int i = 0; i < pages.Count; i++)
        {
            if (i > 0) sb.Append(pageSeparator);
            sb.Append(ExtractPageText(document, pages[i]));
        }

        return sb.ToString();
    }

    public static string ExtractPageText(PdfDocument document, PdfPage page)
        => BuildLines(ExtractFragments(document, page)).Aggregate(
            new StringBuilder(),
            (sb, line) => sb.Length == 0 ? sb.Append(line) : sb.Append('\n').Append(line)).ToString();

    /// <summary>Gom các đoạn văn bản cùng một dòng lại thành chuỗi, chèn khoảng trắng theo khoảng cách thật.</summary>
    public static List<string> BuildLines(List<PdfTextFragment> fragments)
    {
        var lines = new List<string>();
        foreach (var group in GroupIntoLines(fragments))
        {
            var sb = new StringBuilder();
            PdfTextFragment? previous = null;

            foreach (var fragment in group)
            {
                if (previous != null)
                {
                    double gap = fragment.X - previous.Right;
                    double space = Math.Max(1, previous.FontSize * 0.25);
                    if (gap > space * 4) sb.Append("    ");
                    else if (gap > space) sb.Append(' ');
                }

                sb.Append(fragment.Text);
                previous = fragment;
            }

            var text = sb.ToString().TrimEnd();
            if (text.Length > 0) lines.Add(text);
        }

        return lines;
    }

    /// <summary>Nhóm các đoạn thành từng dòng theo toạ độ Y (dung sai theo cỡ chữ).</summary>
    public static List<List<PdfTextFragment>> GroupIntoLines(List<PdfTextFragment> fragments)
    {
        var result = new List<List<PdfTextFragment>>();
        if (fragments.Count == 0) return result;

        var ordered = fragments.OrderByDescending(f => f.Y).ThenBy(f => f.X).ToList();
        var current = new List<PdfTextFragment> { ordered[0] };
        double lineY = ordered[0].Y;

        foreach (var fragment in ordered.Skip(1))
        {
            double tolerance = Math.Max(2.0, fragment.FontSize * 0.5);
            if (Math.Abs(fragment.Y - lineY) <= tolerance)
            {
                current.Add(fragment);
            }
            else
            {
                result.Add(current.OrderBy(f => f.X).ToList());
                current = new List<PdfTextFragment> { fragment };
                lineY = fragment.Y;
            }
        }

        result.Add(current.OrderBy(f => f.X).ToList());
        return result;
    }

    // ── Bộ máy đọc content stream ────────────────────────────────────────
    public static List<PdfTextFragment> ExtractFragments(PdfDocument document, PdfPage page)
    {
        var fragments = new List<PdfTextFragment>();
        byte[] content;

        try { content = page.GetContentBytes(); }
        catch { return fragments; }

        var fontCache = new Dictionary<string, PdfFontInfo>(StringComparer.Ordinal);
        Run(document, content, page.Resources, (double[])Identity.Clone(), fragments, fontCache, 0);
        return fragments;
    }

    private static void Run(
        PdfDocument document, byte[] content, PdfDictionary resources, double[] baseMatrix,
        List<PdfTextFragment> output, Dictionary<string, PdfFontInfo> fontCache, int depth)
    {
        if (depth > 8 || content.Length == 0) return;

        var parser = new PdfParser(content);
        var operands = new List<PdfObject>();
        var graphicsStack = new Stack<double[]>();
        var ctm = (double[])baseMatrix.Clone();

        var state = new TextState();
        var stateStack = new Stack<TextState>();
        double[] tm = (double[])Identity.Clone();
        double[] tlm = (double[])Identity.Clone();

        while (!parser.AtEnd)
        {
            parser.SkipWhitespace();
            if (parser.AtEnd) break;

            byte b = parser.PeekByte();
            bool isOperandStart = b is (byte)'/' or (byte)'(' or (byte)'[' or (byte)'<' or (byte)'+' or (byte)'-' or (byte)'.'
                                  || (b >= '0' && b <= '9');

            if (isOperandStart)
            {
                int before = parser.Position;
                operands.Add(parser.ParseObject());
                if (parser.Position == before) parser.Position++;
                if (operands.Count > 64) operands.RemoveRange(0, operands.Count - 64);
                continue;
            }

            var op = parser.ReadToken();
            if (op.Length == 0)
            {
                parser.Position++;
                continue;
            }

            double Number(int indexFromEnd)
            {
                int index = operands.Count - indexFromEnd;
                return index >= 0 && index < operands.Count && operands[index] is PdfNumber n ? n.Value : 0;
            }

            switch (op)
            {
                case "q":
                    graphicsStack.Push((double[])ctm.Clone());
                    stateStack.Push(Copy(state));
                    break;

                case "Q":
                    if (graphicsStack.Count > 0) ctm = graphicsStack.Pop();
                    if (stateStack.Count > 0) state = stateStack.Pop();
                    break;

                case "cm":
                    ctm = Multiply(new[] { Number(6), Number(5), Number(4), Number(3), Number(2), Number(1) }, ctm);
                    break;

                case "BT":
                    tm = (double[])Identity.Clone();
                    tlm = (double[])Identity.Clone();
                    break;

                case "ET":
                    break;

                case "Tf":
                    state.FontSize = Number(1);
                    if (operands.Count >= 2 && operands[^2] is PdfName fontName)
                    {
                        state.FontName = fontName.Value;
                        state.Font = LoadFont(document, resources, fontName.Value, fontCache);
                    }
                    break;

                case "Tc": state.CharSpacing = Number(1); break;
                case "Tw": state.WordSpacing = Number(1); break;
                case "Tz": state.HorizontalScale = Number(1) / 100.0; break;
                case "TL": state.Leading = Number(1); break;
                case "Ts": state.Rise = Number(1); break;

                case "Td":
                    tlm = Multiply(new[] { 1, 0, 0, 1, Number(2), Number(1) }, tlm);
                    tm = (double[])tlm.Clone();
                    break;

                case "TD":
                    state.Leading = -Number(1);
                    tlm = Multiply(new[] { 1, 0, 0, 1, Number(2), Number(1) }, tlm);
                    tm = (double[])tlm.Clone();
                    break;

                case "Tm":
                    tlm = new[] { Number(6), Number(5), Number(4), Number(3), Number(2), Number(1) };
                    tm = (double[])tlm.Clone();
                    break;

                case "T*":
                    tlm = Multiply(new[] { 1, 0, 0, 1, 0, -state.Leading }, tlm);
                    tm = (double[])tlm.Clone();
                    break;

                case "Tj":
                case "'":
                case "\"":
                {
                    if (op != "Tj")
                    {
                        if (op == "\"")
                        {
                            state.WordSpacing = Number(3);
                            state.CharSpacing = Number(2);
                        }
                        tlm = Multiply(new[] { 1, 0, 0, 1, 0, -state.Leading }, tlm);
                        tm = (double[])tlm.Clone();
                    }

                    if (operands.Count > 0 && operands[^1] is PdfString str)
                        ShowText(str.Bytes, state, ref tm, ctm, output);
                    break;
                }

                case "TJ":
                {
                    if (operands.Count > 0 && operands[^1] is PdfArray array)
                    {
                        foreach (var item in array.Items)
                        {
                            if (item is PdfString piece)
                            {
                                ShowText(piece.Bytes, state, ref tm, ctm, output);
                            }
                            else if (item is PdfNumber adjust)
                            {
                                double tx = -adjust.Value / 1000.0 * state.FontSize * state.HorizontalScale;
                                tm = Multiply(new[] { 1, 0, 0, 1, tx, 0 }, tm);
                            }
                        }
                    }
                    break;
                }

                case "Do":
                {
                    if (operands.Count > 0 && operands[^1] is PdfName xobjectName)
                        RunFormXObject(document, resources, xobjectName.Value, ctm, output, fontCache, depth);
                    break;
                }

                case "BI":
                    SkipInlineImage(parser);
                    break;
            }

            operands.Clear();
        }
    }

    private static TextState Copy(TextState state) => new()
    {
        Font = state.Font,
        FontName = state.FontName,
        FontSize = state.FontSize,
        CharSpacing = state.CharSpacing,
        WordSpacing = state.WordSpacing,
        HorizontalScale = state.HorizontalScale,
        Leading = state.Leading,
        Rise = state.Rise
    };

    private static void ShowText(byte[] bytes, TextState state, ref double[] tm, double[] ctm, List<PdfTextFragment> output)
    {
        var font = state.Font;
        var sb = new StringBuilder();

        var startMatrix = Multiply(
            new[] { state.FontSize * state.HorizontalScale, 0, 0, state.FontSize, 0, state.Rise },
            Multiply(tm, ctm));

        double startX = startMatrix[4];
        double startY = startMatrix[5];
        double scale = Math.Sqrt(Math.Abs(tm[0] * tm[3] - tm[1] * tm[2]))
                       * Math.Sqrt(Math.Abs(ctm[0] * ctm[3] - ctm[1] * ctm[2]));
        if (scale <= 0 || double.IsNaN(scale)) scale = 1;

        double totalAdvance = 0;
        var codes = font != null ? font.SplitCodes(bytes) : bytes.Select(x => (uint)x);

        foreach (uint code in codes)
        {
            string glyph = font != null
                ? font.Decode(code)
                : (code is >= 32 and < 256 ? ((char)code).ToString() : string.Empty);

            sb.Append(glyph);

            double width = (font?.GetWidth(code) ?? 500) / 1000.0 * state.FontSize;
            double advance = (width + state.CharSpacing
                              + (code == 32 && font is { IsTwoByte: false } ? state.WordSpacing : 0))
                             * state.HorizontalScale;
            totalAdvance += advance;
        }

        tm = Multiply(new[] { 1, 0, 0, 1, totalAdvance, 0 }, tm);

        var text = sb.ToString();
        if (text.Trim().Length == 0) return;

        output.Add(new PdfTextFragment
        {
            Text = text,
            X = startX,
            Y = startY,
            // Bề rộng phải nhân cả hệ số của ma trận văn bản, vì nhiều tệp đặt cỡ chữ 1
            // rồi phóng to bằng Tm — nếu bỏ qua sẽ đo hụt và sinh khoảng trắng thừa.
            Width = Math.Abs(totalAdvance) * scale,
            FontSize = Math.Max(1, state.FontSize * scale),
            FontName = state.FontName
        });
    }

    private static PdfFontInfo? LoadFont(PdfDocument document, PdfDictionary resources, string name, Dictionary<string, PdfFontInfo> cache)
    {
        if (cache.TryGetValue(name, out var cached)) return cached;

        var fonts = resources.GetDictionary("Font", document);
        if (fonts?.Get(name, document) is not PdfDictionary fontDict) return null;

        var info = PdfFontInfo.Load(fontDict, document);
        cache[name] = info;
        return info;
    }

    private static void RunFormXObject(
        PdfDocument document, PdfDictionary resources, string name, double[] ctm,
        List<PdfTextFragment> output, Dictionary<string, PdfFontInfo> fontCache, int depth)
    {
        var xobjects = resources.GetDictionary("XObject", document);
        if (xobjects?.Get(name, document) is not PdfStream form) return;
        if (form.Dictionary.GetName("Subtype", document) != "Form") return;

        var matrix = form.Dictionary.GetArray("Matrix", document);
        var formMatrix = matrix != null && matrix.Count >= 6
            ? matrix.Items.Take(6).Select(i => document.Resolve(i).AsDouble()).ToArray()
            : (double[])Identity.Clone();

        var innerResources = form.Dictionary.GetDictionary("Resources", document) ?? resources;

        try
        {
            Run(document, form.GetDecodedData(document), innerResources, Multiply(formMatrix, ctm), output,
                new Dictionary<string, PdfFontInfo>(StringComparer.Ordinal), depth + 1);
        }
        catch
        {
            // Form hỏng thì bỏ qua, phần còn lại của trang vẫn đọc được.
        }

        _ = fontCache;
    }

    private static void SkipInlineImage(PdfParser parser)
    {
        // Bỏ qua BI … ID <dữ liệu nhị phân> EI
        int idIndex = parser.IndexOf("ID", parser.Position);
        if (idIndex < 0) { parser.Position = parser.Length; return; }

        int search = idIndex + 2;
        while (search < parser.Length - 1)
        {
            if (parser.Buffer[search] == 'E' && parser.Buffer[search + 1] == 'I'
                && (search == 0 || PdfParser.IsWhitespace(parser.Buffer[search - 1]))
                && (search + 2 >= parser.Length || PdfParser.IsWhitespace(parser.Buffer[search + 2])))
            {
                parser.Position = search + 2;
                return;
            }
            search++;
        }

        parser.Position = parser.Length;
    }

    // ── Ma trận 2D dạng [a b c d e f] ────────────────────────────────────
    private static double[] Multiply(double[] m1, double[] m2) => new[]
    {
        m1[0] * m2[0] + m1[1] * m2[2],
        m1[0] * m2[1] + m1[1] * m2[3],
        m1[2] * m2[0] + m1[3] * m2[2],
        m1[2] * m2[1] + m1[3] * m2[3],
        m1[4] * m2[0] + m1[5] * m2[2] + m2[4],
        m1[4] * m2[1] + m1[5] * m2[3] + m2[5]
    };
}
