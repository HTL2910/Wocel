using System.Globalization;
using System.Xml.Linq;
using Wocel.Core.Models;

namespace Wocel.Excel.IO;

/// <summary>
/// Dựng và đọc phần xl/styles.xml của tệp .xlsx — nơi Excel lưu phông chữ, cỡ chữ,
/// màu nền và định dạng số. Thiếu phần này thì mọi định dạng mất khi lưu.
/// </summary>
internal sealed class XlsxStyleTable
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>Mã định dạng số của Wocel → chuỗi định dạng theo chuẩn của Excel.</summary>
    private static readonly Dictionary<string, string> NumberFormatCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["integer"] = "#,##0",
        ["decimal2"] = "#,##0.00",
        ["thousands"] = "#,##0.##",
        ["currency"] = "#,##0\\ \"₫\"",
        ["percent"] = "0.0%",
        ["date"] = "dd/mm/yyyy",
        ["scientific"] = "0.000E+00"
    };

    private readonly List<CellStyle> _styles = new();
    private readonly Dictionary<string, int> _indexByKey = new(StringComparer.Ordinal);

    public XlsxStyleTable()
    {
        // Chỉ số 0 luôn là định dạng mặc định, Excel bắt buộc phải có.
        _styles.Add(new CellStyle());
        _indexByKey[KeyOf(new CellStyle())] = 0;
    }

    public bool HasCustomStyles => _styles.Count > 1;

    /// <summary>Ghi nhận một định dạng và trả về chỉ số dùng cho thuộc tính s= của ô.</summary>
    public int Register(CellStyle? style)
    {
        if (style == null || style.IsDefault) return 0;

        var key = KeyOf(style);
        if (_indexByKey.TryGetValue(key, out int existing)) return existing;

        _styles.Add(style.Clone());
        int index = _styles.Count - 1;
        _indexByKey[key] = index;
        return index;
    }

    private static string KeyOf(CellStyle s) =>
        $"{s.IsBold}|{s.IsItalic}|{s.IsUnderline}|{s.FontSize}|{s.FontFamily}|"
        + $"{s.TextColor}|{s.BackgroundColor}|{s.NumberFormat}|{s.HorizontalAlignment}";

    // ─────────────────────────────────────────────────────────────────────
    //  GHI
    // ─────────────────────────────────────────────────────────────────────
    public XDocument BuildDocument()
    {
        // Mỗi định dạng có phông riêng; giữ song song 1-1 cho đơn giản và chắc chắn.
        var fonts = new XElement(S + "fonts", new XAttribute("count", _styles.Count));
        foreach (var style in _styles)
        {
            var font = new XElement(S + "font",
                new XElement(S + "sz", new XAttribute("val",
                    (style.FontSize ?? 12).ToString(CultureInfo.InvariantCulture))),
                new XElement(S + "name", new XAttribute("val", style.FontFamily ?? "Calibri")));

            if (style.IsBold) font.Add(new XElement(S + "b"));
            if (style.IsItalic) font.Add(new XElement(S + "i"));
            if (style.IsUnderline) font.Add(new XElement(S + "u"));
            if (style.TextColor is { Length: > 0 } textColor)
                font.Add(new XElement(S + "color", new XAttribute("rgb", ToArgb(textColor))));

            fonts.Add(font);
        }

        // Excel yêu cầu hai kiểu tô đầu tiên phải là none và gray125.
        var fills = new XElement(S + "fills",
            new XElement(S + "fill", new XElement(S + "patternFill", new XAttribute("patternType", "none"))),
            new XElement(S + "fill", new XElement(S + "patternFill", new XAttribute("patternType", "gray125"))));

        var fillIndex = new int[_styles.Count];
        for (int i = 0; i < _styles.Count; i++)
        {
            if (_styles[i].BackgroundColor is not { Length: > 0 } background) { fillIndex[i] = 0; continue; }

            fills.Add(new XElement(S + "fill",
                new XElement(S + "patternFill",
                    new XAttribute("patternType", "solid"),
                    new XElement(S + "fgColor", new XAttribute("rgb", ToArgb(background))),
                    new XElement(S + "bgColor", new XAttribute("indexed", "64")))));

            fillIndex[i] = fills.Elements().Count() - 1;
        }
        fills.Add(new XAttribute("count", fills.Elements().Count()));

        // Định dạng số tự đặt, đánh số từ 164 theo quy ước của Excel.
        var numberFormats = new XElement(S + "numFmts");
        var numberFormatIds = new int[_styles.Count];
        var assigned = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int nextId = 164;

        for (int i = 0; i < _styles.Count; i++)
        {
            var code = _styles[i].NumberFormat;
            if (code == null || !NumberFormatCodes.TryGetValue(code, out var pattern)) { numberFormatIds[i] = 0; continue; }

            if (!assigned.TryGetValue(code, out int id))
            {
                id = nextId++;
                assigned[code] = id;
                numberFormats.Add(new XElement(S + "numFmt",
                    new XAttribute("numFmtId", id),
                    new XAttribute("formatCode", pattern)));
            }

            numberFormatIds[i] = id;
        }
        numberFormats.Add(new XAttribute("count", assigned.Count));

        var cellFormats = new XElement(S + "cellXfs", new XAttribute("count", _styles.Count));
        for (int i = 0; i < _styles.Count; i++)
        {
            var style = _styles[i];
            var xf = new XElement(S + "xf",
                new XAttribute("numFmtId", numberFormatIds[i]),
                new XAttribute("fontId", i),
                new XAttribute("fillId", fillIndex[i]),
                new XAttribute("borderId", 0),
                new XAttribute("xfId", 0),
                new XAttribute("applyFont", 1));

            if (numberFormatIds[i] != 0) xf.Add(new XAttribute("applyNumberFormat", 1));
            if (fillIndex[i] != 0) xf.Add(new XAttribute("applyFill", 1));

            if (style.HorizontalAlignment is "Center" or "Right")
            {
                xf.Add(new XAttribute("applyAlignment", 1));
                xf.Add(new XElement(S + "alignment",
                    new XAttribute("horizontal", style.HorizontalAlignment.ToLowerInvariant())));
            }

            cellFormats.Add(xf);
        }

        var root = new XElement(S + "styleSheet");
        if (assigned.Count > 0) root.Add(numberFormats);
        root.Add(fonts, fills,
            new XElement(S + "borders", new XAttribute("count", 1), new XElement(S + "border")),
            new XElement(S + "cellStyleXfs", new XAttribute("count", 1),
                new XElement(S + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0),
                             new XAttribute("fillId", 0), new XAttribute("borderId", 0))),
            cellFormats);

        return new XDocument(root);
    }

    private static string ToArgb(string color)
    {
        var hex = color.TrimStart('#');
        if (hex.Length == 6) hex = "FF" + hex;
        return hex.Length == 8 ? hex.ToUpperInvariant() : "FF000000";
    }

    // ─────────────────────────────────────────────────────────────────────
    //  ĐỌC
    // ─────────────────────────────────────────────────────────────────────
    /// <summary>Đọc styles.xml thành danh sách định dạng, tra theo chỉ số s= của ô.</summary>
    public static List<CellStyle?> Parse(XDocument document)
    {
        var result = new List<CellStyle?>();

        var numberFormats = document.Descendants(S + "numFmt")
            .ToDictionary(
                e => e.Attribute("numFmtId")?.Value ?? "0",
                e => CodeFromPattern(e.Attribute("formatCode")?.Value ?? string.Empty));

        var fonts = document.Descendants(S + "fonts").FirstOrDefault()?.Elements(S + "font").ToList()
                    ?? new List<XElement>();

        var fills = document.Descendants(S + "fills").FirstOrDefault()?.Elements(S + "fill").ToList()
                    ?? new List<XElement>();

        var cellFormats = document.Descendants(S + "cellXfs").FirstOrDefault()?.Elements(S + "xf").ToList()
                          ?? new List<XElement>();

        foreach (var xf in cellFormats)
        {
            var style = new CellStyle();

            if (int.TryParse(xf.Attribute("fontId")?.Value, out int fontId) && fontId < fonts.Count)
            {
                var font = fonts[fontId];
                style.IsBold = font.Element(S + "b") != null;
                style.IsItalic = font.Element(S + "i") != null;
                style.IsUnderline = font.Element(S + "u") != null;

                var size = font.Element(S + "sz")?.Attribute("val")?.Value;
                if (double.TryParse(size, NumberStyles.Any, CultureInfo.InvariantCulture, out double points))
                    style.FontSize = (int)Math.Round(points);

                var name = font.Element(S + "name")?.Attribute("val")?.Value;
                if (!string.IsNullOrWhiteSpace(name) && name != "Calibri") style.FontFamily = name;

                var color = font.Element(S + "color")?.Attribute("rgb")?.Value;
                if (!string.IsNullOrWhiteSpace(color)) style.TextColor = FromArgb(color);
            }

            if (int.TryParse(xf.Attribute("fillId")?.Value, out int fillId) && fillId > 1 && fillId < fills.Count)
            {
                var fill = fills[fillId].Element(S + "patternFill")?.Element(S + "fgColor")?.Attribute("rgb")?.Value;
                if (!string.IsNullOrWhiteSpace(fill)) style.BackgroundColor = FromArgb(fill);
            }

            var numberFormatId = xf.Attribute("numFmtId")?.Value ?? "0";
            if (numberFormats.TryGetValue(numberFormatId, out var code)) style.NumberFormat = code;

            var alignment = xf.Element(S + "alignment")?.Attribute("horizontal")?.Value;
            if (alignment is "center") style.HorizontalAlignment = "Center";
            else if (alignment is "right") style.HorizontalAlignment = "Right";

            result.Add(style.IsDefault ? null : style);
        }

        return result;
    }

    private static string? CodeFromPattern(string pattern) =>
        NumberFormatCodes.FirstOrDefault(kv => kv.Value == pattern).Key;

    private static string FromArgb(string argb)
    {
        var hex = argb.Trim();
        if (hex.Length == 8) hex = hex[2..];
        return "#" + hex.ToUpperInvariant();
    }
}
