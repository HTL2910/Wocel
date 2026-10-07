using System.Globalization;
using System.Text;

namespace Wocel.Core.Pdf;

/// <summary>
/// Bảng ánh xạ mã ký tự / tên glyph sang Unicode cho các encoding chuẩn của PDF.
/// </summary>
public static class PdfGlyphList
{
    // WinAnsiEncoding = CP1252, chỉ khác Latin1 ở dải 0x80–0x9F.
    private const string WinAnsiHigh =
        "€�‚ƒ„…†‡ˆ‰Š‹Œ�Ž�" +
        "�‘’“”•–—˜™š›œ�žŸ";

    private const string MacRomanHigh =
        "ÄÅÇÉÑÖÜáàâäãåçéè" +
        "êëíìîïñóòôöõúùûü" +
        "†°¢£§•¶ß®©™´¨≠ÆØ" +
        "∞±≤≥¥µ∂∑∏π∫ªºΩæø" +
        "¿¡¬√ƒ≈∆«»… ÀÃÕŒœ" +
        "–—“”‘’÷◊ÿŸ⁄€‹›ﬁﬂ" +
        "‡·‚„‰ÂÊÁËÈÍÎÏÌÓÔ" +
        "ÒÚÛÙıˆ˜¯˘˙˚¸˝˛ˇ";

    private static readonly Dictionary<string, string> Names = BuildNameTable();

    public static string WinAnsiToUnicode(byte code)
    {
        if (code < 32) return string.Empty;
        if (code is >= 0x80 and <= 0x9F)
        {
            char c = WinAnsiHigh[code - 0x80];
            return c == '�' ? string.Empty : c.ToString();
        }
        return ((char)code).ToString();
    }

    public static string MacRomanToUnicode(byte code)
    {
        if (code < 32) return string.Empty;
        if (code < 128) return ((char)code).ToString();
        return MacRomanHigh[code - 128].ToString();
    }

    /// <summary>Chuyển tên glyph (ví dụ /aacute, /uni01B5, /space) sang chuỗi Unicode.</summary>
    public static string ToUnicode(string glyphName)
    {
        if (string.IsNullOrEmpty(glyphName)) return string.Empty;

        // Hậu tố biến thể: a.sc, quoteright.alt
        int dot = glyphName.IndexOf('.');
        if (dot > 0) glyphName = glyphName[..dot];

        if (Names.TryGetValue(glyphName, out var mapped)) return mapped;

        if (glyphName.Length == 1) return glyphName;

        if (glyphName.StartsWith("uni", StringComparison.Ordinal) && glyphName.Length >= 7)
        {
            var text = string.Empty;
            for (int i = 3; i + 4 <= glyphName.Length; i += 4)
            {
                if (int.TryParse(glyphName.AsSpan(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
                    text += char.ConvertFromUtf32(value);
            }
            return text;
        }

        if (glyphName.StartsWith('u') && glyphName.Length is >= 5 and <= 7
            && int.TryParse(glyphName.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
            return char.ConvertFromUtf32(code);

        return string.Empty;
    }

    private static Dictionary<string, string> BuildNameTable()
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["space"] = " ", ["exclam"] = "!", ["quotedbl"] = "\"", ["numbersign"] = "#",
            ["dollar"] = "$", ["percent"] = "%", ["ampersand"] = "&", ["quotesingle"] = "'",
            ["parenleft"] = "(", ["parenright"] = ")", ["asterisk"] = "*", ["plus"] = "+",
            ["comma"] = ",", ["hyphen"] = "-", ["period"] = ".", ["slash"] = "/",
            ["zero"] = "0", ["one"] = "1", ["two"] = "2", ["three"] = "3", ["four"] = "4",
            ["five"] = "5", ["six"] = "6", ["seven"] = "7", ["eight"] = "8", ["nine"] = "9",
            ["colon"] = ":", ["semicolon"] = ";", ["less"] = "<", ["equal"] = "=", ["greater"] = ">",
            ["question"] = "?", ["at"] = "@", ["bracketleft"] = "[", ["backslash"] = "\\",
            ["bracketright"] = "]", ["asciicircum"] = "^", ["underscore"] = "_", ["grave"] = "`",
            ["braceleft"] = "{", ["bar"] = "|", ["braceright"] = "}", ["asciitilde"] = "~",
            ["quoteleft"] = "‘", ["quoteright"] = "’", ["quotedblleft"] = "“",
            ["quotedblright"] = "”", ["quotesinglbase"] = "‚", ["quotedblbase"] = "„",
            ["endash"] = "–", ["emdash"] = "—", ["bullet"] = "•", ["ellipsis"] = "…",
            ["dagger"] = "†", ["daggerdbl"] = "‡", ["perthousand"] = "‰",
            ["guilsinglleft"] = "‹", ["guilsinglright"] = "›", ["fraction"] = "⁄",
            ["Euro"] = "€", ["trademark"] = "™", ["fi"] = "ﬁ", ["fl"] = "ﬂ",
            ["exclamdown"] = "¡", ["cent"] = "¢", ["sterling"] = "£", ["currency"] = "¤",
            ["yen"] = "¥", ["brokenbar"] = "¦", ["section"] = "§", ["dieresis"] = "¨",
            ["copyright"] = "©", ["ordfeminine"] = "ª", ["guillemotleft"] = "«",
            ["logicalnot"] = "¬", ["registered"] = "®", ["macron"] = "¯",
            ["degree"] = "°", ["plusminus"] = "±", ["acute"] = "´", ["mu"] = "µ",
            ["paragraph"] = "¶", ["periodcentered"] = "·", ["cedilla"] = "¸",
            ["ordmasculine"] = "º", ["guillemotright"] = "»", ["onequarter"] = "¼",
            ["onehalf"] = "½", ["threequarters"] = "¾", ["questiondown"] = "¿",
            ["AE"] = "Æ", ["Eth"] = "Ð", ["multiply"] = "×", ["Oslash"] = "Ø",
            ["Thorn"] = "Þ", ["germandbls"] = "ß", ["ae"] = "æ", ["eth"] = "ð",
            ["divide"] = "÷", ["oslash"] = "ø", ["thorn"] = "þ",
            ["OE"] = "Œ", ["oe"] = "œ", ["Scaron"] = "Š", ["scaron"] = "š",
            ["Ydieresis"] = "Ÿ", ["Zcaron"] = "Ž", ["zcaron"] = "ž",
            ["Dcroat"] = "Đ", ["dcroat"] = "đ", ["Dslash"] = "Đ", ["dslash"] = "đ",
            ["Eng"] = "Ŋ", ["eng"] = "ŋ", ["Lslash"] = "Ł", ["lslash"] = "ł",
            ["dotlessi"] = "ı", ["Idotaccent"] = "İ", ["napostrophe"] = "ŉ",
            ["horncomb"] = "̛", ["hookabovecomb"] = "̉", ["dotbelowcomb"] = "̣",
            ["florin"] = "ƒ", ["circumflex"] = "ˆ", ["caron"] = "ˇ",
            ["breve"] = "˘", ["dotaccent"] = "˙", ["ring"] = "˚",
            ["ogonek"] = "˛", ["tilde"] = "˜", ["hungarumlaut"] = "˝"
        };

        // Chữ cái Latin có dấu: tên = chữ cái + tên dấu (ví dụ aacute, Ocircumflex, ntilde).
        var accents = new (string suffix, string mark)[]
        {
            ("acute", "́"), ("grave", "̀"), ("circumflex", "̂"),
            ("tilde", "̃"), ("dieresis", "̈"), ("ring", "̊"),
            ("cedilla", "̧"), ("caron", "̌"), ("breve", "̆"),
            ("macron", "̄"), ("ogonek", "̨"), ("hungarumlaut", "̋"),
            ("dotaccent", "̇")
        };

        // Nguyên âm có móc của tiếng Việt: ohorn, uhorn và các biến thể mang dấu thanh.
        foreach (var (name, letter) in new[] { ("Ohorn", "Ơ"), ("ohorn", "ơ"), ("Uhorn", "Ư"), ("uhorn", "ư") })
        {
            table.TryAdd(name, letter);
            foreach (var (suffix, mark) in new[]
                     { ("acute", "́"), ("grave", "̀"), ("hookabove", "̉"), ("tilde", "̃"), ("dotbelow", "̣") })
                table.TryAdd(name + suffix, (letter + mark).Normalize(NormalizationForm.FormC));
        }

        for (char c = 'A'; c <= 'z'; c++)
        {
            if (!char.IsLetter(c)) continue;
            table.TryAdd(c.ToString(), c.ToString());
            foreach (var (suffix, mark) in accents)
                table.TryAdd(c + suffix, (c + mark).Normalize(NormalizationForm.FormC));

            // Dấu tiếng Việt viết dưới dạng tên ghép: adotbelow, ehookabove, ocircumflexacute…
            foreach (var (suffix, mark) in new[] { ("dotbelow", "̣"), ("hookabove", "̉") })
                table.TryAdd(c + suffix, (c + mark).Normalize(NormalizationForm.FormC));
        }

        return table;
    }
}
