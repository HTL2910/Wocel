using System.Text;

namespace Wocel.Core.Pdf;

/// <summary>
/// Thông tin font cần cho việc trích xuất văn bản: bảng ToUnicode, encoding
/// và bề rộng ký tự (để ước lượng vị trí cột khi tách bảng).
/// </summary>
public sealed class PdfFontInfo
{
    public string BaseFont { get; private set; } = "Unknown";
    public string SubType { get; private set; } = "Type1";
    public bool IsTwoByte { get; private set; }
    public double DefaultWidth { get; private set; } = 500;

    private readonly Dictionary<uint, string> _toUnicode = new();
    private readonly Dictionary<uint, double> _widths = new();
    private readonly Dictionary<uint, string> _differences = new();
    private string _baseEncoding = "StandardEncoding";

    public static PdfFontInfo Load(PdfDictionary fontDict, PdfDocument doc)
    {
        var font = new PdfFontInfo
        {
            BaseFont = fontDict.GetName("BaseFont", doc) ?? "Unknown",
            SubType = fontDict.GetName("Subtype", doc) ?? "Type1"
        };

        var descendants = fontDict.GetArray("DescendantFonts", doc);
        var descendant = descendants != null && descendants.Count > 0
            ? doc.Resolve(descendants[0]) as PdfDictionary
            : null;

        if (font.SubType == "Type0")
        {
            font.IsTwoByte = true; // Identity-H và hầu hết CMap nhúng dùng mã 2 byte
            var encodingName = fontDict.GetName("Encoding", doc);
            if (encodingName != null && encodingName.Contains("Identity")) font.IsTwoByte = true;

            if (descendant != null)
            {
                font.DefaultWidth = descendant.GetReal("DW", 1000, doc);
                font.LoadCidWidths(descendant.GetArray("W", doc), doc);
            }
        }
        else
        {
            font.LoadSimpleWidths(fontDict, doc);
            font.LoadEncoding(fontDict, doc);
        }

        if (fontDict.GetStream("ToUnicode", doc) is { } cmapStream)
        {
            try { font.ParseToUnicodeCMap(cmapStream.GetDecodedData(doc)); }
            catch { /* CMap hỏng — vẫn dùng được encoding thường */ }
        }

        // Font đơn (Type1/TrueType/Type3) luôn dùng mã 1 byte, kể cả khi CMap khai báo dải 2 byte.
        if (font.SubType != "Type0") font.IsTwoByte = false;

        return font;
    }

    private void LoadSimpleWidths(PdfDictionary fontDict, PdfDocument doc)
    {
        var widths = fontDict.GetArray("Widths", doc);
        int firstChar = fontDict.GetInt("FirstChar", 0, doc);
        var descriptor = fontDict.GetDictionary("FontDescriptor", doc);
        DefaultWidth = descriptor.GetReal("MissingWidth", 500, doc);

        if (widths == null) return;

        for (int i = 0; i < widths.Count; i++)
            if (doc.Resolve(widths[i]) is PdfNumber n)
                _widths[(uint)(firstChar + i)] = n.Value;
    }

    private void LoadCidWidths(PdfArray? w, PdfDocument doc)
    {
        if (w == null) return;

        int i = 0;
        while (i < w.Count)
        {
            if (doc.Resolve(w[i]) is not PdfNumber start) break;
            i++;
            if (i >= w.Count) break;

            var next = doc.Resolve(w[i]);
            if (next is PdfArray list)
            {
                for (int k = 0; k < list.Count; k++)
                    if (doc.Resolve(list[k]) is PdfNumber width)
                        _widths[(uint)(start.AsInt + k)] = width.Value;
                i++;
            }
            else if (next is PdfNumber end && i + 1 < w.Count && doc.Resolve(w[i + 1]) is PdfNumber value)
            {
                for (uint c = (uint)start.AsInt; c <= (uint)end.AsInt && c - start.AsInt < 65536; c++)
                    _widths[c] = value.Value;
                i += 2;
            }
            else
            {
                break;
            }
        }
    }

    private void LoadEncoding(PdfDictionary fontDict, PdfDocument doc)
    {
        var encoding = doc.Resolve(fontDict["Encoding"]);

        if (encoding is PdfName name)
        {
            _baseEncoding = name.Value;
            return;
        }

        if (encoding is not PdfDictionary encodingDict) return;

        _baseEncoding = encodingDict.GetName("BaseEncoding", doc) ?? "StandardEncoding";
        var differences = encodingDict.GetArray("Differences", doc);
        if (differences == null) return;

        uint code = 0;
        foreach (var item in differences.Items)
        {
            var resolved = doc.Resolve(item);
            if (resolved is PdfNumber number) code = (uint)number.AsInt;
            else if (resolved is PdfName glyph) _differences[code++] = glyph.Value;
        }
    }

    // ── ToUnicode CMap ───────────────────────────────────────────────────
    private void ParseToUnicodeCMap(byte[] data)
    {
        var parser = new PdfParser(data);
        var operands = new List<PdfObject>();

        while (!parser.AtEnd)
        {
            parser.SkipWhitespace();
            if (parser.AtEnd) break;

            byte b = parser.PeekByte();
            if (b is (byte)'/' or (byte)'(' or (byte)'[' or (byte)'<' or (byte)'+' or (byte)'-' or (byte)'.'
                || (b >= '0' && b <= '9'))
            {
                int before = parser.Position;
                operands.Add(parser.ParseObject());
                if (parser.Position == before) parser.Position++;
                continue;
            }

            var op = parser.ReadToken();
            if (op.Length == 0) { parser.Position++; continue; }

            switch (op)
            {
                case "beginbfchar":
                    ReadBfChar(parser);
                    break;
                case "beginbfrange":
                    ReadBfRange(parser);
                    break;
                case "begincodespacerange":
                    ReadCodespaceRange(parser);
                    break;
            }

            operands.Clear();
        }
    }

    private void ReadCodespaceRange(PdfParser parser)
    {
        while (true)
        {
            parser.SkipWhitespace();
            if (parser.AtEnd || parser.PeekByte() != '<') return;

            var low = parser.ParseHexString();
            parser.SkipWhitespace();
            if (parser.AtEnd || parser.PeekByte() != '<') return;
            parser.ParseHexString();

            if (low.Bytes.Length >= 2) IsTwoByte = true;
        }
    }

    private void ReadBfChar(PdfParser parser)
    {
        while (true)
        {
            parser.SkipWhitespace();
            if (parser.AtEnd) return;
            if (parser.PeekByte() != '<')
            {
                parser.ReadToken(); // endbfchar
                return;
            }

            var src = parser.ParseHexString();
            parser.SkipWhitespace();
            if (parser.AtEnd) return;

            if (parser.PeekByte() == '<')
            {
                var dst = parser.ParseHexString();
                if (src.Bytes.Length >= 2) IsTwoByte = true;
                _toUnicode[ToCode(src.Bytes)] = FromUtf16Be(dst.Bytes);
            }
            else if (parser.PeekByte() == '/')
            {
                var glyph = parser.ParseName();
                _toUnicode[ToCode(src.Bytes)] = PdfGlyphList.ToUnicode(glyph.Value);
            }
            else
            {
                return;
            }
        }
    }

    private void ReadBfRange(PdfParser parser)
    {
        while (true)
        {
            parser.SkipWhitespace();
            if (parser.AtEnd) return;
            if (parser.PeekByte() != '<')
            {
                parser.ReadToken(); // endbfrange
                return;
            }

            var lowString = parser.ParseHexString();
            parser.SkipWhitespace();
            if (parser.AtEnd || parser.PeekByte() != '<') return;
            var highString = parser.ParseHexString();

            uint low = ToCode(lowString.Bytes);
            uint high = ToCode(highString.Bytes);
            if (lowString.Bytes.Length >= 2) IsTwoByte = true;
            if (high < low || high - low > 65535) high = low;

            parser.SkipWhitespace();
            if (parser.AtEnd) return;

            if (parser.PeekByte() == '[')
            {
                var array = parser.ParseArray();
                for (int i = 0; i < array.Count && low + i <= high; i++)
                    if (array[i] is PdfString s)
                        _toUnicode[low + (uint)i] = FromUtf16Be(s.Bytes);
            }
            else if (parser.PeekByte() == '<')
            {
                var dst = parser.ParseHexString();
                var text = FromUtf16Be(dst.Bytes);
                if (text.Length == 0) continue;

                for (uint c = low; c <= high; c++)
                {
                    // Ký tự cuối tăng dần trong khoảng.
                    var chars = text.ToCharArray();
                    chars[^1] = (char)(chars[^1] + (c - low));
                    _toUnicode[c] = new string(chars);
                }
            }
            else
            {
                return;
            }
        }
    }

    private static uint ToCode(byte[] bytes)
    {
        uint value = 0;
        foreach (var b in bytes.Take(4)) value = (value << 8) | b;
        return value;
    }

    private static string FromUtf16Be(byte[] bytes)
    {
        if (bytes.Length == 0) return string.Empty;
        if (bytes.Length == 1) return ((char)bytes[0]).ToString();
        return Encoding.BigEndianUnicode.GetString(bytes, 0, bytes.Length - (bytes.Length % 2));
    }

    // ── Giải mã chuỗi hiển thị ───────────────────────────────────────────
    /// <summary>Tách chuỗi trong content stream thành từng mã ký tự.</summary>
    public IEnumerable<uint> SplitCodes(byte[] bytes)
    {
        if (IsTwoByte)
        {
            for (int i = 0; i + 1 < bytes.Length; i += 2)
                yield return (uint)((bytes[i] << 8) | bytes[i + 1]);
            if (bytes.Length % 2 == 1) yield return bytes[^1];
        }
        else
        {
            foreach (var b in bytes) yield return b;
        }
    }

    public string Decode(uint code)
    {
        if (_toUnicode.TryGetValue(code, out var mapped)) return mapped;
        if (_differences.TryGetValue(code, out var glyph)) return PdfGlyphList.ToUnicode(glyph);

        if (IsTwoByte)
            return code is >= 32 and < 0xFFFE ? ((char)code).ToString() : string.Empty;

        return _baseEncoding switch
        {
            "WinAnsiEncoding" => PdfGlyphList.WinAnsiToUnicode((byte)code),
            "MacRomanEncoding" => PdfGlyphList.MacRomanToUnicode((byte)code),
            _ => code is >= 32 and < 256 ? ((char)code).ToString() : string.Empty
        };
    }

    private Dictionary<string, uint>? _reverse;

    /// <summary>Mã hoá ngược chuỗi Unicode về mã ký tự của font. Trả về false nếu font thiếu ký tự.</summary>
    public bool TryEncode(string text, out byte[] bytes)
    {
        _reverse ??= BuildReverseMap();
        var buffer = new List<byte>();

        foreach (var element in Split(text))
        {
            if (!_reverse.TryGetValue(element, out uint code))
            {
                bytes = Array.Empty<byte>();
                return false;
            }

            if (IsTwoByte)
            {
                buffer.Add((byte)(code >> 8));
                buffer.Add((byte)code);
            }
            else
            {
                buffer.Add((byte)code);
            }
        }

        bytes = buffer.ToArray();
        return true;
    }

    private static IEnumerable<string> Split(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                yield return text.Substring(i, 2);
                i++;
            }
            else
            {
                yield return text[i].ToString();
            }
        }
    }

    private Dictionary<string, uint> BuildReverseMap()
    {
        var map = new Dictionary<string, uint>(StringComparer.Ordinal);

        if (_toUnicode.Count > 0)
        {
            foreach (var (code, text) in _toUnicode)
                if (text.Length > 0) map.TryAdd(text, code);
            return map;
        }

        foreach (var (code, glyph) in _differences)
        {
            var text = PdfGlyphList.ToUnicode(glyph);
            if (text.Length > 0) map.TryAdd(text, code);
        }

        for (uint code = 32; code < 256; code++)
        {
            var text = _baseEncoding switch
            {
                "WinAnsiEncoding" => PdfGlyphList.WinAnsiToUnicode((byte)code),
                "MacRomanEncoding" => PdfGlyphList.MacRomanToUnicode((byte)code),
                _ => ((char)code).ToString()
            };
            if (text.Length > 0) map.TryAdd(text, code);
        }

        return map;
    }

    /// <summary>Bề rộng ký tự tính theo đơn vị 1/1000 em.</summary>
    public double GetWidth(uint code) => _widths.TryGetValue(code, out var w) ? w : DefaultWidth;
}
