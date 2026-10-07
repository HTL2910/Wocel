using System.IO.Compression;
using System.Text;

namespace Wocel.Core.Pdf;

/// <summary>
/// Bộ giải mã/mã hoá filter của PDF (ISO 32000-1 §7.4): Flate, LZW, ASCIIHex,
/// ASCII85, RunLength cùng predictor PNG/TIFF. Toàn bộ dùng BCL, không thư viện ngoài.
/// </summary>
public static class PdfFilters
{
    private static readonly HashSet<string> ImageFilters = new(StringComparer.Ordinal)
    {
        "DCTDecode", "DCT", "JPXDecode", "JBIG2Decode", "CCITTFaxDecode", "CCF"
    };

    public static bool IsImageFilter(string name) => ImageFilters.Contains(name);

    /// <summary>
    /// Giải mã dữ liệu stream theo chuỗi filter khai báo trong dictionary.
    /// Gặp filter ảnh (DCTDecode…) thì dừng và trả về dữ liệu ở dạng ảnh nén.
    /// </summary>
    public static byte[] Decode(byte[] data, PdfDictionary dict, IPdfResolver? resolver = null)
    {
        var filters = ReadNameList(dict["Filter"], resolver);
        if (filters.Count == 0) return data;

        var parms = ReadParmsList(dict["DecodeParms"] ?? dict["DP"], resolver, filters.Count);

        for (int i = 0; i < filters.Count; i++)
        {
            var name = filters[i];
            if (IsImageFilter(name)) break;

            var parm = parms[i];
            data = name switch
            {
                "FlateDecode" or "Fl" => ApplyPredictor(FlateDecode(data), parm, resolver),
                "LZWDecode" or "LZW" => ApplyPredictor(LzwDecode(data, parm.GetInt("EarlyChange", 1, resolver)), parm, resolver),
                "ASCIIHexDecode" or "AHx" => AsciiHexDecode(data),
                "ASCII85Decode" or "A85" => Ascii85Decode(data),
                "RunLengthDecode" or "RL" => RunLengthDecode(data),
                "Crypt" => data,
                _ => data
            };
        }

        return data;
    }

    public static List<string> ReadNameList(PdfObject? obj, IPdfResolver? resolver)
    {
        obj = resolver != null ? resolver.Resolve(obj) : obj;
        return obj switch
        {
            PdfName n => new List<string> { n.Value },
            PdfArray a => a.Items
                .Select(i => (resolver != null ? resolver.Resolve(i) : i) as PdfName)
                .Where(n => n != null).Select(n => n!.Value).ToList(),
            _ => new List<string>()
        };
    }

    private static List<PdfDictionary> ReadParmsList(PdfObject? obj, IPdfResolver? resolver, int count)
    {
        obj = resolver != null ? resolver.Resolve(obj) : obj;
        var list = new List<PdfDictionary>();

        if (obj is PdfArray arr)
        {
            foreach (var item in arr.Items)
            {
                var resolved = resolver != null ? resolver.Resolve(item) : item;
                list.Add(resolved as PdfDictionary ?? new PdfDictionary());
            }
        }
        else if (obj is PdfDictionary d)
        {
            list.Add(d);
        }

        while (list.Count < count) list.Add(new PdfDictionary());
        return list;
    }

    // ── Flate ────────────────────────────────────────────────────────────
    public static byte[] FlateDecode(byte[] data)
    {
        if (data.Length == 0) return data;

        // Thử zlib (có header 0x78…), sau đó raw deflate, cuối cùng bỏ qua rác đầu stream.
        var attempt = TryInflate(data, zlibHeader: true);
        if (attempt != null) return attempt;

        attempt = TryInflate(data, zlibHeader: false);
        if (attempt != null) return attempt;

        // Một số file thừa khoảng trắng/newline trước dữ liệu nén.
        int skip = 0;
        while (skip < data.Length && (data[skip] == '\r' || data[skip] == '\n' || data[skip] == ' ' || data[skip] == '\t')) skip++;
        if (skip > 0 && skip < data.Length)
        {
            var trimmed = data[skip..];
            attempt = TryInflate(trimmed, zlibHeader: true) ?? TryInflate(trimmed, zlibHeader: false);
            if (attempt != null) return attempt;
        }

        return Array.Empty<byte>();
    }

    private static byte[]? TryInflate(byte[] data, bool zlibHeader)
    {
        try
        {
            using var input = new MemoryStream(data);
            using Stream decompressor = zlibHeader
                ? new ZLibStream(input, CompressionMode.Decompress)
                : new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            // Dữ liệu hỏng ở cuối stream là chuyện thường gặp — giữ lại phần đã bung được.
            var buffer = new byte[8192];
            int read;
            try
            {
                while ((read = decompressor.Read(buffer, 0, buffer.Length)) > 0)
                    output.Write(buffer, 0, read);
            }
            catch (InvalidDataException) when (output.Length > 0) { }

            return output.Length > 0 ? output.ToArray() : null;
        }
        catch
        {
            return null;
        }
    }

    public static byte[] FlateEncode(byte[] data, CompressionLevel level = CompressionLevel.SmallestSize)
    {
        using var output = new MemoryStream();
        using (var deflate = new ZLibStream(output, level, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    // ── Predictor (PNG §7.4.4.4 / TIFF) ──────────────────────────────────
    public static byte[] ApplyPredictor(byte[] data, PdfDictionary parms, IPdfResolver? resolver)
    {
        int predictor = parms.GetInt("Predictor", 1, resolver);
        if (predictor <= 1 || data.Length == 0) return data;

        int colors = Math.Max(1, parms.GetInt("Colors", 1, resolver));
        int bpc = Math.Max(1, parms.GetInt("BitsPerComponent", 8, resolver));
        int columns = Math.Max(1, parms.GetInt("Columns", 1, resolver));

        int bpp = Math.Max(1, colors * bpc / 8);
        int rowLength = (columns * colors * bpc + 7) / 8;

        if (predictor == 2)
            return TiffPredictor(data, colors, bpc, columns);

        // PNG predictors: mỗi hàng có 1 byte tag ở đầu.
        var output = new List<byte>(data.Length);
        var previous = new byte[rowLength];
        int pos = 0;

        while (pos + 1 <= data.Length)
        {
            int tag = data[pos++];
            int available = Math.Min(rowLength, data.Length - pos);
            if (available <= 0) break;

            var row = new byte[rowLength];
            Buffer.BlockCopy(data, pos, row, 0, available);
            pos += available;

            switch (tag)
            {
                case 0: break;
                case 1:
                    for (int i = bpp; i < rowLength; i++) row[i] = (byte)(row[i] + row[i - bpp]);
                    break;
                case 2:
                    for (int i = 0; i < rowLength; i++) row[i] = (byte)(row[i] + previous[i]);
                    break;
                case 3:
                    for (int i = 0; i < rowLength; i++)
                    {
                        int left = i >= bpp ? row[i - bpp] : 0;
                        row[i] = (byte)(row[i] + ((left + previous[i]) >> 1));
                    }
                    break;
                case 4:
                    for (int i = 0; i < rowLength; i++)
                    {
                        int a = i >= bpp ? row[i - bpp] : 0;
                        int b = previous[i];
                        int c = i >= bpp ? previous[i - bpp] : 0;
                        row[i] = (byte)(row[i] + Paeth(a, b, c));
                    }
                    break;
                default: break;
            }

            output.AddRange(row);
            previous = row;
        }

        return output.ToArray();
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        return pb <= pc ? b : c;
    }

    private static byte[] TiffPredictor(byte[] data, int colors, int bpc, int columns)
    {
        if (bpc != 8) return data; // chỉ hỗ trợ 8-bit, các trường hợp khác giữ nguyên
        int rowLength = columns * colors;
        for (int row = 0; row + rowLength <= data.Length; row += rowLength)
            for (int i = colors; i < rowLength; i++)
                data[row + i] = (byte)(data[row + i] + data[row + i - colors]);
        return data;
    }

    // ── LZW ──────────────────────────────────────────────────────────────
    public static byte[] LzwDecode(byte[] data, int earlyChange = 1)
    {
        var output = new MemoryStream();
        var table = new List<byte[]>(4096);

        void ResetTable()
        {
            table.Clear();
            for (int i = 0; i < 256; i++) table.Add(new[] { (byte)i });
            table.Add(Array.Empty<byte>()); // 256 = clear
            table.Add(Array.Empty<byte>()); // 257 = EOD
        }

        ResetTable();

        int codeBits = 9;
        int bitBuffer = 0, bitCount = 0;
        byte[]? previous = null;

        foreach (var b in data)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitCount += 8;

            while (bitCount >= codeBits)
            {
                int code = (bitBuffer >> (bitCount - codeBits)) & ((1 << codeBits) - 1);
                bitCount -= codeBits;

                if (code == 256)
                {
                    ResetTable();
                    codeBits = 9;
                    previous = null;
                    continue;
                }

                if (code == 257) return output.ToArray();

                byte[] entry;
                if (code < table.Count)
                {
                    entry = table[code];
                    if (previous != null)
                        table.Add(previous.Concat(new[] { entry[0] }).ToArray());
                }
                else if (previous != null)
                {
                    entry = previous.Concat(new[] { previous[0] }).ToArray();
                    table.Add(entry);
                }
                else
                {
                    return output.ToArray();
                }

                output.Write(entry, 0, entry.Length);
                previous = entry;

                int next = table.Count + earlyChange;
                codeBits = next switch
                {
                    >= 2048 => 12,
                    >= 1024 => 11,
                    >= 512 => 10,
                    _ => 9
                };
            }
        }

        return output.ToArray();
    }

    // ── ASCIIHex ─────────────────────────────────────────────────────────
    public static byte[] AsciiHexDecode(byte[] data)
    {
        var output = new MemoryStream();
        int high = -1;

        foreach (var b in data)
        {
            char c = (char)b;
            if (c == '>') break;

            int value = HexValue(c);
            if (value < 0) continue;

            if (high < 0)
            {
                high = value;
            }
            else
            {
                output.WriteByte((byte)((high << 4) | value));
                high = -1;
            }
        }

        if (high >= 0) output.WriteByte((byte)(high << 4));
        return output.ToArray();
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1
    };

    public static byte[] AsciiHexEncode(byte[] data)
    {
        var sb = new StringBuilder(data.Length * 2 + 1);
        foreach (var b in data) sb.Append(b.ToString("X2"));
        sb.Append('>');
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    // ── ASCII85 ──────────────────────────────────────────────────────────
    public static byte[] Ascii85Decode(byte[] data)
    {
        var output = new MemoryStream();
        uint tuple = 0;
        int count = 0;
        int start = 0;

        if (data.Length >= 2 && data[0] == '<' && data[1] == '~') start = 2;

        for (int i = start; i < data.Length; i++)
        {
            char c = (char)data[i];
            if (char.IsWhiteSpace(c)) continue;
            if (c == '~') break;

            if (c == 'z' && count == 0)
            {
                output.Write(new byte[4], 0, 4);
                continue;
            }

            if (c < '!' || c > 'u') continue;

            tuple = tuple * 85 + (uint)(c - '!');
            if (++count != 5) continue;

            output.WriteByte((byte)(tuple >> 24));
            output.WriteByte((byte)(tuple >> 16));
            output.WriteByte((byte)(tuple >> 8));
            output.WriteByte((byte)tuple);
            tuple = 0;
            count = 0;
        }

        if (count > 0)
        {
            for (int i = count; i < 5; i++) tuple = tuple * 85 + 84;
            for (int i = 0; i < count - 1; i++)
                output.WriteByte((byte)(tuple >> (24 - i * 8)));
        }

        return output.ToArray();
    }

    // ── RunLength ────────────────────────────────────────────────────────
    public static byte[] RunLengthDecode(byte[] data)
    {
        var output = new MemoryStream();
        int i = 0;

        while (i < data.Length)
        {
            int length = data[i++];
            if (length == 128) break;

            if (length < 128)
            {
                int copy = Math.Min(length + 1, data.Length - i);
                output.Write(data, i, copy);
                i += copy;
            }
            else if (i < data.Length)
            {
                byte value = data[i++];
                for (int r = 0; r < 257 - length; r++) output.WriteByte(value);
            }
        }

        return output.ToArray();
    }
}
