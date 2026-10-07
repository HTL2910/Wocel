using System.Globalization;
using System.Text;

namespace Wocel.Core.Pdf;

/// <summary>
/// Một font dùng để sinh nội dung mới. Nếu máy có font TrueType Unicode thì font đó
/// được nhúng (rút gọn) nên tiếng Việt hiển thị đúng dấu; nếu không sẽ lùi về
/// Helvetica chuẩn và bỏ dấu.
/// </summary>
public sealed class PdfFontResource
{
    internal PdfTrueTypeFont? TrueType;
    internal readonly HashSet<ushort> UsedGlyphs = new();
    internal readonly Dictionary<ushort, string> GlyphText = new();
    internal readonly PdfDictionary FontDictionary = new();

    public string ResourceName { get; internal set; } = "F1";
    public bool IsBold { get; internal set; }
    public PdfRef Reference { get; internal set; } = new(0);
    public bool IsEmbedded => TrueType != null;

    /// <summary>Chuỗi đã mã hoá sẵn sàng đặt vào toán tử Tj.</summary>
    public string EncodeForShow(string text)
    {
        if (TrueType == null) return "(" + EscapeLiteral(StripDiacritics(text)) + ")";

        var sb = new StringBuilder("<");
        foreach (var (codepoint, source) in EnumerateCodepoints(text))
        {
            ushort glyph = TrueType.GetGlyphId(codepoint);
            if (glyph == 0 && codepoint != ' ') glyph = TrueType.GetGlyphId('?');

            UsedGlyphs.Add(glyph);
            GlyphText[glyph] = source;
            sb.Append(glyph.ToString("X4"));
        }
        return sb.Append('>').ToString();
    }

    /// <summary>Bề rộng chuỗi tính bằng point ở cỡ chữ cho trước.</summary>
    public double Measure(string text, double fontSize)
    {
        if (TrueType == null) return StripDiacritics(text).Length * fontSize * 0.5;

        double total = 0;
        foreach (var (codepoint, _) in EnumerateCodepoints(text))
            total += TrueType.GetAdvanceWidth(TrueType.GetGlyphId(codepoint));

        return total / 1000.0 * fontSize;
    }

    /// <summary>Cắt chuỗi cho vừa bề rộng cho trước, thêm dấu … nếu bị cắt.</summary>
    public string Truncate(string text, double fontSize, double maxWidth)
    {
        if (Measure(text, fontSize) <= maxWidth) return text;

        var sb = new StringBuilder();
        foreach (char c in text)
        {
            if (Measure(sb.ToString() + c + "…", fontSize) > maxWidth) break;
            sb.Append(c);
        }
        return sb.Append('…').ToString();
    }

    private static IEnumerable<(int codepoint, string source)> EnumerateCodepoints(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                yield return (char.ConvertToUtf32(text[i], text[i + 1]), text.Substring(i, 2));
                i++;
            }
            else
            {
                yield return (text[i], text[i].ToString());
            }
        }
    }

    internal static string EscapeLiteral(string text)
    {
        var sb = new StringBuilder();
        foreach (char c in text)
        {
            switch (c)
            {
                case '(': sb.Append("\\("); break;
                case ')': sb.Append("\\)"); break;
                case '\\': sb.Append("\\\\"); break;
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 256) sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Bỏ dấu tiếng Việt — chỉ dùng khi buộc phải lùi về font chuẩn.</summary>
    public static string StripDiacritics(string text)
    {
        var normalized = text.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (char c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}

/// <summary>Quản lý các font dùng để sinh nội dung mới trong một tài liệu.</summary>
public sealed class PdfFontLibrary
{
    private readonly PdfDocument _document;
    private readonly Dictionary<string, PdfFontResource> _fonts = new(StringComparer.Ordinal);
    private int _counter;

    public PdfFontLibrary(PdfDocument document) => _document = document;

    public PdfFontResource Regular => Get(bold: false);
    public PdfFontResource Bold => Get(bold: true);

    public PdfFontResource Get(bool bold)
    {
        string key = bold ? "bold" : "regular";
        if (_fonts.TryGetValue(key, out var existing)) return existing;

        var resource = new PdfFontResource
        {
            TrueType = PdfTrueTypeFont.FindSystemFont(bold),
            ResourceName = $"WcF{++_counter}",
            IsBold = bold
        };
        resource.Reference = _document.Add(resource.FontDictionary);

        _fonts[key] = resource;
        return resource;
    }

    /// <summary>Khai báo font vào tài nguyên của một trang.</summary>
    public void ApplyTo(PdfPage page)
    {
        var resources = page.Resources;
        var fonts = resources.GetDictionary("Font", _document);

        if (fonts == null)
        {
            fonts = new PdfDictionary();
            resources["Font"] = fonts;
        }

        foreach (var font in _fonts.Values)
            fonts[font.ResourceName] = font.Reference;
    }

    /// <summary>Ghi dữ liệu font vào tài liệu. Gọi sau khi đã sinh xong mọi nội dung.</summary>
    public void Flush()
    {
        foreach (var font in _fonts.Values)
        {
            if (font.TrueType != null) BuildEmbeddedFont(font);
            else BuildStandardFont(font);
        }
    }

    private void BuildStandardFont(PdfFontResource font)
    {
        var dict = font.FontDictionary;
        dict.Items.Clear();
        dict.SetName("Type", "Font");
        dict.SetName("Subtype", "Type1");
        dict.SetName("BaseFont", font.IsBold ? "Helvetica-Bold" : "Helvetica");
        dict.SetName("Encoding", "WinAnsiEncoding");
    }

    private void BuildEmbeddedFont(PdfFontResource font)
    {
        var ttf = font.TrueType!;
        var glyphs = font.UsedGlyphs.OrderBy(g => g).ToList();
        if (glyphs.Count == 0) glyphs.Add(0);

        var subset = ttf.BuildSubset(glyphs);

        var fileDict = new PdfDictionary();
        fileDict.SetInt("Length1", subset.Length);
        var fileStream = PdfStream.FromDecoded(fileDict, subset);
        var fileRef = _document.Add(fileStream);

        var descriptor = new PdfDictionary();
        descriptor.SetName("Type", "FontDescriptor");
        descriptor.SetName("FontName", ttf.PostScriptName);
        descriptor.SetInt("Flags", 4); // symbolic — dùng cmap nhúng của font
        descriptor["FontBBox"] = new PdfArray(new PdfObject[]
        {
            new PdfNumber((long)ttf.ScaleToPdf(ttf.BoundingBox[0])),
            new PdfNumber((long)ttf.ScaleToPdf(ttf.BoundingBox[1])),
            new PdfNumber((long)ttf.ScaleToPdf(ttf.BoundingBox[2])),
            new PdfNumber((long)ttf.ScaleToPdf(ttf.BoundingBox[3]))
        });
        descriptor.SetInt("ItalicAngle", (long)ttf.ItalicAngle);
        descriptor.SetInt("Ascent", ttf.ScaleToPdf(ttf.Ascender));
        descriptor.SetInt("Descent", ttf.ScaleToPdf(ttf.Descender));
        descriptor.SetInt("CapHeight", ttf.ScaleToPdf(ttf.CapHeight));
        descriptor.SetInt("StemV", ttf.IsBold ? 140 : 80);
        descriptor["FontFile2"] = fileRef;
        var descriptorRef = _document.Add(descriptor);

        // /W: bề rộng từng glyph được dùng
        var widths = new PdfArray();
        int index = 0;
        while (index < glyphs.Count)
        {
            int runEnd = index;
            while (runEnd + 1 < glyphs.Count && glyphs[runEnd + 1] == glyphs[runEnd] + 1) runEnd++;

            widths.Add(new PdfNumber((long)glyphs[index]));
            var run = new PdfArray();
            for (int i = index; i <= runEnd; i++)
                run.Add(new PdfNumber(Math.Round(ttf.GetAdvanceWidth(glyphs[i]))));
            widths.Add(run);

            index = runEnd + 1;
        }

        var descendant = new PdfDictionary();
        descendant.SetName("Type", "Font");
        descendant.SetName("Subtype", "CIDFontType2");
        descendant.SetName("BaseFont", ttf.PostScriptName);

        var systemInfo = new PdfDictionary();
        systemInfo.SetText("Registry", "Adobe");
        systemInfo.SetText("Ordering", "Identity");
        systemInfo.SetInt("Supplement", 0);
        descendant["CIDSystemInfo"] = systemInfo;
        descendant["FontDescriptor"] = descriptorRef;
        descendant.SetInt("DW", 1000);
        descendant["W"] = widths;
        descendant.SetName("CIDToGIDMap", "Identity");
        var descendantRef = _document.Add(descendant);

        var dict = font.FontDictionary;
        dict.Items.Clear();
        dict.SetName("Type", "Font");
        dict.SetName("Subtype", "Type0");
        dict.SetName("BaseFont", ttf.PostScriptName);
        dict.SetName("Encoding", "Identity-H");
        dict["DescendantFonts"] = new PdfArray(new PdfObject[] { descendantRef });
        dict["ToUnicode"] = _document.Add(BuildToUnicodeCMap(font));
    }

    private static PdfStream BuildToUnicodeCMap(PdfFontResource font)
    {
        var entries = font.GlyphText
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .OrderBy(kv => kv.Key)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("/CIDInit /ProcSet findresource begin");
        sb.AppendLine("12 dict begin");
        sb.AppendLine("begincmap");
        sb.AppendLine("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def");
        sb.AppendLine("/CMapName /Adobe-Identity-UCS def");
        sb.AppendLine("/CMapType 2 def");
        sb.AppendLine("1 begincodespacerange");
        sb.AppendLine("<0000> <FFFF>");
        sb.AppendLine("endcodespacerange");

        for (int start = 0; start < entries.Count; start += 100)
        {
            var chunk = entries.Skip(start).Take(100).ToList();
            sb.AppendLine($"{chunk.Count} beginbfchar");

            foreach (var (glyph, text) in chunk)
            {
                var hex = string.Concat(Encoding.BigEndianUnicode.GetBytes(text).Select(b => b.ToString("X2")));
                sb.AppendLine($"<{glyph:X4}> <{hex}>");
            }

            sb.AppendLine("endbfchar");
        }

        sb.AppendLine("endcmap");
        sb.AppendLine("CMapName currentdict /CMap defineresource pop");
        sb.AppendLine("end");
        sb.AppendLine("end");

        return PdfStream.FromDecoded(new PdfDictionary(), Encoding.ASCII.GetBytes(sb.ToString()));
    }
}

/// <summary>Sinh content stream (chuỗi toán tử vẽ) cho trang PDF.</summary>
public sealed class PdfContentBuilder
{
    private readonly StringBuilder _sb = new();

    private static string N(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    public PdfContentBuilder Save() { _sb.AppendLine("q"); return this; }
    public PdfContentBuilder Restore() { _sb.AppendLine("Q"); return this; }

    public PdfContentBuilder SetFillColor(double r, double g, double b)
    {
        _sb.AppendLine($"{N(r)} {N(g)} {N(b)} rg");
        return this;
    }

    public PdfContentBuilder SetStrokeColor(double r, double g, double b)
    {
        _sb.AppendLine($"{N(r)} {N(g)} {N(b)} RG");
        return this;
    }

    public PdfContentBuilder SetLineWidth(double width)
    {
        _sb.AppendLine($"{N(width)} w");
        return this;
    }

    public PdfContentBuilder SetExtGState(string name)
    {
        _sb.AppendLine($"/{name} gs");
        return this;
    }

    public PdfContentBuilder Transform(double a, double b, double c, double d, double e, double f)
    {
        _sb.AppendLine($"{N(a)} {N(b)} {N(c)} {N(d)} {N(e)} {N(f)} cm");
        return this;
    }

    public PdfContentBuilder Rotate(double degrees, double originX, double originY)
    {
        double radians = degrees * Math.PI / 180.0;
        double cos = Math.Cos(radians), sin = Math.Sin(radians);
        Transform(1, 0, 0, 1, originX, originY);
        Transform(cos, sin, -sin, cos, 0, 0);
        return this;
    }

    public PdfContentBuilder Text(PdfFontResource font, double size, double x, double y, string text)
    {
        _sb.AppendLine("BT");
        _sb.AppendLine($"/{font.ResourceName} {N(size)} Tf");
        _sb.AppendLine($"1 0 0 1 {N(x)} {N(y)} Tm");
        _sb.AppendLine($"{font.EncodeForShow(text)} Tj");
        _sb.AppendLine("ET");
        return this;
    }

    /// <summary>Vẽ nhiều dòng liên tiếp, cách nhau theo leading.</summary>
    public PdfContentBuilder TextBlock(PdfFontResource font, double size, double x, double y, double leading, IEnumerable<string> lines)
    {
        _sb.AppendLine("BT");
        _sb.AppendLine($"/{font.ResourceName} {N(size)} Tf");
        _sb.AppendLine($"{N(leading)} TL");
        _sb.AppendLine($"1 0 0 1 {N(x)} {N(y)} Tm");

        bool first = true;
        foreach (var line in lines)
        {
            if (!first) _sb.AppendLine("T*");
            first = false;
            if (line.Length > 0) _sb.AppendLine($"{font.EncodeForShow(line)} Tj");
        }

        _sb.AppendLine("ET");
        return this;
    }

    public PdfContentBuilder Rectangle(double x, double y, double width, double height, bool fill, bool stroke)
    {
        _sb.AppendLine($"{N(x)} {N(y)} {N(width)} {N(height)} re");
        _sb.AppendLine(fill && stroke ? "B" : fill ? "f" : "S");
        return this;
    }

    public PdfContentBuilder Line(double x1, double y1, double x2, double y2)
    {
        _sb.AppendLine($"{N(x1)} {N(y1)} m {N(x2)} {N(y2)} l S");
        return this;
    }

    public PdfContentBuilder Image(string resourceName, double x, double y, double width, double height)
    {
        _sb.AppendLine("q");
        _sb.AppendLine($"{N(width)} 0 0 {N(height)} {N(x)} {N(y)} cm");
        _sb.AppendLine($"/{resourceName} Do");
        _sb.AppendLine("Q");
        return this;
    }

    public PdfContentBuilder Raw(string operators)
    {
        _sb.AppendLine(operators);
        return this;
    }

    public byte[] ToArray() => Encoding.Latin1.GetBytes(_sb.ToString());
    public override string ToString() => _sb.ToString();
}
