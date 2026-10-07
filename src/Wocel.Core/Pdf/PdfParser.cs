using System.Globalization;
using System.Text;

namespace Wocel.Core.Pdf;

/// <summary>
/// Bộ phân tích cú pháp đối tượng PDF (COS syntax, ISO 32000-1 §7.2–7.3).
/// Làm việc trực tiếp trên mảng byte để không làm hỏng dữ liệu nhị phân.
/// </summary>
public sealed class PdfParser
{
    private readonly byte[] _data;

    public PdfParser(byte[] data, int position = 0)
    {
        _data = data;
        Position = position;
    }

    public int Position { get; set; }
    public int Length => _data.Length;
    public byte[] Buffer => _data;

    /// <summary>Dùng để phân giải /Length dạng tham chiếu gián tiếp khi đọc stream.</summary>
    public Func<PdfRef, PdfObject?>? IndirectResolver { get; set; }

    public static bool IsWhitespace(byte b) => b is 0 or 9 or 10 or 12 or 13 or 32;

    public static bool IsDelimiter(byte b) =>
        b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']'
          or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

    public static bool IsRegular(byte b) => !IsWhitespace(b) && !IsDelimiter(b);

    public bool AtEnd => Position >= _data.Length;

    /// <summary>Xem byte tại vị trí hiện tại (không tiêu thụ).</summary>
    public byte PeekByte(int offset = 0) => Peek(offset);

    private byte Peek(int offset = 0)
    {
        int index = Position + offset;
        return index >= 0 && index < _data.Length ? _data[index] : (byte)0;
    }

    public void SkipWhitespace()
    {
        while (Position < _data.Length)
        {
            byte b = _data[Position];
            if (IsWhitespace(b))
            {
                Position++;
            }
            else if (b == '%')
            {
                while (Position < _data.Length && _data[Position] != '\n' && _data[Position] != '\r') Position++;
            }
            else
            {
                return;
            }
        }
    }

    /// <summary>Đọc một token thô (từ khoá hoặc số). Trả về chuỗi rỗng nếu gặp delimiter.</summary>
    public string ReadToken()
    {
        SkipWhitespace();
        int start = Position;
        while (Position < _data.Length && IsRegular(_data[Position])) Position++;
        return Position > start ? Encoding.ASCII.GetString(_data, start, Position - start) : string.Empty;
    }

    public string PeekToken()
    {
        int saved = Position;
        var token = ReadToken();
        Position = saved;
        return token;
    }

    /// <summary>Nếu token kế tiếp đúng bằng <paramref name="keyword"/> thì tiêu thụ nó.</summary>
    public bool TryConsumeKeyword(string keyword)
    {
        int saved = Position;
        if (ReadToken() == keyword) return true;
        Position = saved;
        return false;
    }

    // ── Đối tượng ────────────────────────────────────────────────────────
    public PdfObject ParseObject()
    {
        SkipWhitespace();
        if (AtEnd) return PdfObject.Null;

        byte b = Peek();
        switch (b)
        {
            case (byte)'/': return ParseName();
            case (byte)'(': return ParseLiteralString();
            case (byte)'[': return ParseArray();
            case (byte)'<':
                return Peek(1) == '<' ? ParseDictionaryOrStream() : ParseHexString();
            case (byte)']':
            case (byte)'>':
            case (byte)'}':
            case (byte)')':
                Position++; // token lạc — bỏ qua để không kẹt vòng lặp
                return PdfObject.Null;
            case (byte)'{':
                Position++;
                return PdfObject.Null;
        }

        if (b == '+' || b == '-' || b == '.' || (b >= '0' && b <= '9'))
            return ParseNumberOrReference();

        var token = ReadToken();
        return token switch
        {
            "true" => PdfBool.True,
            "false" => PdfBool.False,
            "null" => PdfObject.Null,
            "" => Advance(),
            _ => PdfObject.Null
        };

        PdfObject Advance()
        {
            Position++;
            return PdfObject.Null;
        }
    }

    public PdfName ParseName()
    {
        Position++; // '/'
        var sb = new StringBuilder();

        while (Position < _data.Length && IsRegular(_data[Position]))
        {
            byte b = _data[Position++];
            if (b == '#' && Position + 1 < _data.Length)
            {
                int hi = HexValue(_data[Position]), lo = HexValue(_data[Position + 1]);
                if (hi >= 0 && lo >= 0)
                {
                    sb.Append((char)((hi << 4) | lo));
                    Position += 2;
                    continue;
                }
            }
            sb.Append((char)b);
        }

        return new PdfName(sb.ToString());
    }

    private PdfObject ParseNumberOrReference()
    {
        int saved = Position;
        var first = ReadToken();
        if (!TryParseNumber(first, out double value, out bool isInt))
        {
            Position = saved;
            Position++;
            return PdfObject.Null;
        }

        if (isInt && value >= 0)
        {
            int afterFirst = Position;
            var second = ReadToken();
            if (int.TryParse(second, NumberStyles.Integer, CultureInfo.InvariantCulture, out int generation) && generation >= 0)
            {
                var third = ReadToken();
                if (third == "R") return new PdfRef((int)value, generation);
            }
            Position = afterFirst;
        }

        return isInt ? new PdfNumber((long)value) : new PdfNumber(value);
    }

    private static bool TryParseNumber(string token, out double value, out bool isInteger)
    {
        isInteger = !token.Contains('.') && !token.Contains('e') && !token.Contains('E');
        if (isInteger && long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
        {
            value = l;
            return true;
        }

        // PDF cho phép dạng lạ như "--5" hay ".5" hoặc "4."
        var cleaned = token.TrimStart('+');
        while (cleaned.StartsWith("--")) cleaned = cleaned[1..];
        if (cleaned.EndsWith('.')) cleaned += "0";
        if (cleaned.StartsWith('.')) cleaned = "0" + cleaned;
        if (cleaned.StartsWith("-.")) cleaned = "-0" + cleaned[1..];

        isInteger = false;
        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public PdfString ParseLiteralString()
    {
        Position++; // '('
        var bytes = new List<byte>();
        int depth = 1;

        while (Position < _data.Length)
        {
            byte b = _data[Position++];

            if (b == '\\')
            {
                if (Position >= _data.Length) break;
                byte esc = _data[Position++];
                switch (esc)
                {
                    case (byte)'n': bytes.Add((byte)'\n'); break;
                    case (byte)'r': bytes.Add((byte)'\r'); break;
                    case (byte)'t': bytes.Add((byte)'\t'); break;
                    case (byte)'b': bytes.Add(8); break;
                    case (byte)'f': bytes.Add(12); break;
                    case (byte)'(': bytes.Add((byte)'('); break;
                    case (byte)')': bytes.Add((byte)')'); break;
                    case (byte)'\\': bytes.Add((byte)'\\'); break;
                    case (byte)'\r':
                        if (Position < _data.Length && _data[Position] == '\n') Position++;
                        break;
                    case (byte)'\n':
                        break;
                    default:
                        if (esc >= '0' && esc <= '7')
                        {
                            int octal = esc - '0';
                            for (int i = 0; i < 2 && Position < _data.Length && _data[Position] >= '0' && _data[Position] <= '7'; i++)
                                octal = octal * 8 + (_data[Position++] - '0');
                            bytes.Add((byte)(octal & 0xFF));
                        }
                        else
                        {
                            bytes.Add(esc);
                        }
                        break;
                }
                continue;
            }

            if (b == '(') depth++;
            else if (b == ')' && --depth == 0) break;

            bytes.Add(b);
        }

        return new PdfString(bytes.ToArray());
    }

    public PdfString ParseHexString()
    {
        Position++; // '<'
        var bytes = new List<byte>();
        int high = -1;

        while (Position < _data.Length)
        {
            byte b = _data[Position++];
            if (b == '>') break;

            int value = HexValue(b);
            if (value < 0) continue;

            if (high < 0) high = value;
            else
            {
                bytes.Add((byte)((high << 4) | value));
                high = -1;
            }
        }

        if (high >= 0) bytes.Add((byte)(high << 4));
        return new PdfString(bytes.ToArray(), preferHex: true);
    }

    public PdfArray ParseArray()
    {
        Position++; // '['
        var array = new PdfArray();

        while (true)
        {
            SkipWhitespace();
            if (AtEnd) break;

            if (Peek() == ']')
            {
                Position++;
                break;
            }

            // Bảo vệ chống file hỏng: token 'endobj'/'endstream' kết thúc mảng.
            var peek = PeekToken();
            if (peek is "endobj" or "endstream" or "stream") break;

            int before = Position;
            array.Add(ParseObject());
            if (Position == before) Position++;
        }

        return array;
    }

    public PdfObject ParseDictionaryOrStream()
    {
        Position += 2; // '<<'
        var dict = new PdfDictionary();

        while (true)
        {
            SkipWhitespace();
            if (AtEnd) break;

            if (Peek() == '>' && Peek(1) == '>')
            {
                Position += 2;
                break;
            }

            if (Peek() != '/')
            {
                var peek = PeekToken();
                if (peek is "endobj" or "stream" or "endstream" or "") 
                {
                    if (peek == "") Position++;
                    break;
                }
                ParseObject();
                continue;
            }

            var key = ParseName();
            int before = Position;
            var value = ParseObject();
            if (Position == before) Position++;
            dict[key.Value] = value;
        }

        SkipWhitespace();
        if (!LooksLikeKeyword("stream")) return dict;

        return ParseStreamBody(dict);
    }

    private bool LooksLikeKeyword(string keyword)
    {
        if (Position + keyword.Length > _data.Length) return false;
        for (int i = 0; i < keyword.Length; i++)
            if (_data[Position + i] != keyword[i]) return false;
        return true;
    }

    private PdfStream ParseStreamBody(PdfDictionary dict)
    {
        Position += "stream".Length;

        // Sau từ khoá 'stream' phải là CRLF hoặc LF (§7.3.8.1).
        if (Position < _data.Length && _data[Position] == '\r') Position++;
        if (Position < _data.Length && _data[Position] == '\n') Position++;

        int start = Position;
        int length = ResolveStreamLength(dict);

        bool lengthValid = length >= 0 && start + length <= _data.Length;
        if (lengthValid)
        {
            int check = start + length;
            // Xác nhận ngay sau dữ liệu là 'endstream' (cho phép vài ký tự trắng).
            int probe = check;
            while (probe < _data.Length && IsWhitespace(_data[probe])) probe++;
            if (!MatchesAt(probe, "endstream")) lengthValid = false;
        }

        if (!lengthValid)
        {
            int found = IndexOf("endstream", start);
            length = found < 0 ? _data.Length - start : found - start;
            // Bỏ ký tự xuống dòng ngay trước 'endstream'
            while (length > 0 && (_data[start + length - 1] == '\n' || _data[start + length - 1] == '\r')) length--;
            if (length < 0) length = 0;
        }

        var raw = new byte[length];
        System.Buffer.BlockCopy(_data, start, raw, 0, length);
        Position = start + length;

        SkipWhitespace();
        TryConsumeKeyword("endstream");

        dict.SetInt("Length", length);
        return new PdfStream(dict, raw);
    }

    private int ResolveStreamLength(PdfDictionary dict)
    {
        var lengthObj = dict["Length"];
        if (lengthObj is PdfNumber n) return n.AsInt;
        if (lengthObj is PdfRef reference && IndirectResolver != null)
        {
            if (IndirectResolver(reference) is PdfNumber resolved) return resolved.AsInt;
        }
        return -1;
    }

    private bool MatchesAt(int index, string keyword)
    {
        if (index + keyword.Length > _data.Length) return false;
        for (int i = 0; i < keyword.Length; i++)
            if (_data[index + i] != keyword[i]) return false;
        return true;
    }

    public int IndexOf(string needle, int from)
    {
        var pattern = Encoding.ASCII.GetBytes(needle);
        int limit = _data.Length - pattern.Length;
        for (int i = Math.Max(0, from); i <= limit; i++)
        {
            bool ok = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (_data[i + j] != pattern[j]) { ok = false; break; }
            }
            if (ok) return i;
        }
        return -1;
    }

    // ── Đối tượng gián tiếp ──────────────────────────────────────────────
    /// <summary>Đọc "n g obj … endobj" tại vị trí hiện tại.</summary>
    public PdfObject? ParseIndirectObject(out int number, out int generation)
    {
        number = -1;
        generation = 0;

        SkipWhitespace();
        var numToken = ReadToken();
        if (!int.TryParse(numToken, out number)) return null;

        var genToken = ReadToken();
        if (!int.TryParse(genToken, out generation)) return null;

        if (ReadToken() != "obj") return null;

        var obj = ParseObject();
        SkipWhitespace();
        TryConsumeKeyword("endobj");
        return obj;
    }

    private static int HexValue(byte b) => (char)b switch
    {
        >= '0' and <= '9' => b - '0',
        >= 'a' and <= 'f' => b - 'a' + 10,
        >= 'A' and <= 'F' => b - 'A' + 10,
        _ => -1
    };
}
