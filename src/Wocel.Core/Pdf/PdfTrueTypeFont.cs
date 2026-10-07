using System.Buffers.Binary;
using System.Text;

namespace Wocel.Core.Pdf;

/// <summary>
/// Đọc tệp TrueType (.ttf) và tạo bản rút gọn chỉ chứa các glyph thực sự dùng.
/// Nhờ vậy PDF sinh ra hiển thị đúng tiếng Việt mà dung lượng vẫn nhỏ.
/// </summary>
public sealed class PdfTrueTypeFont
{
    private readonly byte[] _data;
    private readonly Dictionary<string, (int offset, int length)> _tables = new(StringComparer.Ordinal);
    private readonly Dictionary<int, ushort> _cmap = new();
    private ushort[] _advanceWidths = Array.Empty<ushort>();
    private uint[] _loca = Array.Empty<uint>();

    public string PostScriptName { get; private set; } = "EmbeddedFont";
    public int UnitsPerEm { get; private set; } = 1000;
    public int NumGlyphs { get; private set; }
    public short Ascender { get; private set; } = 800;
    public short Descender { get; private set; } = -200;
    public short CapHeight { get; private set; } = 700;
    public double ItalicAngle { get; private set; }
    public short[] BoundingBox { get; private set; } = { 0, -200, 1000, 900 };
    public bool IsBold { get; private set; }

    private PdfTrueTypeFont(byte[] data) => _data = data;

    // ── Nạp ──────────────────────────────────────────────────────────────
    public static PdfTrueTypeFont? FromFile(string path)
    {
        try { return FromBytes(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path)); }
        catch { return null; }
    }

    public static PdfTrueTypeFont? FromBytes(byte[] data, string fallbackName = "EmbeddedFont")
    {
        try
        {
            var font = new PdfTrueTypeFont(data);
            return font.Parse(fallbackName) ? font : null;
        }
        catch
        {
            return null;
        }
    }

    private bool Parse(string fallbackName)
    {
        if (_data.Length < 12) return false;

        uint version = ReadUInt32(0);
        if (version is not (0x00010000 or 0x74727565)) return false; // chỉ nhận TrueType outline

        int numTables = ReadUInt16(4);
        for (int i = 0; i < numTables; i++)
        {
            int record = 12 + i * 16;
            if (record + 16 > _data.Length) break;

            string tag = Encoding.ASCII.GetString(_data, record, 4);
            _tables[tag] = ((int)ReadUInt32(record + 8), (int)ReadUInt32(record + 12));
        }

        if (!_tables.ContainsKey("glyf") || !_tables.ContainsKey("loca") || !_tables.ContainsKey("head")) return false;

        var head = _tables["head"];
        UnitsPerEm = ReadUInt16(head.offset + 18);
        if (UnitsPerEm == 0) UnitsPerEm = 1000;
        BoundingBox = new[]
        {
            ReadInt16(head.offset + 36), ReadInt16(head.offset + 38),
            ReadInt16(head.offset + 40), ReadInt16(head.offset + 42)
        };
        short indexToLocFormat = ReadInt16(head.offset + 50);
        IsBold = (ReadUInt16(head.offset + 44) & 1) != 0;

        NumGlyphs = _tables.TryGetValue("maxp", out var maxp) ? ReadUInt16(maxp.offset + 4) : 0;
        if (NumGlyphs == 0) return false;

        if (_tables.TryGetValue("hhea", out var hhea))
        {
            Ascender = ReadInt16(hhea.offset + 4);
            Descender = ReadInt16(hhea.offset + 6);
            int numberOfHMetrics = ReadUInt16(hhea.offset + 34);
            LoadHmtx(numberOfHMetrics);
        }

        if (_tables.TryGetValue("OS/2", out var os2) && os2.offset + 90 <= _data.Length)
        {
            short capHeight = ReadInt16(os2.offset + 88);
            if (capHeight > 0) CapHeight = capHeight;
        }

        if (_tables.TryGetValue("post", out var post) && post.offset + 8 <= _data.Length)
            ItalicAngle = ReadInt32(post.offset + 4) / 65536.0;

        LoadLoca(indexToLocFormat);
        LoadCmap();
        PostScriptName = ReadPostScriptName() ?? fallbackName.Replace(" ", string.Empty);
        return _loca.Length > 0;
    }

    private void LoadHmtx(int numberOfHMetrics)
    {
        if (!_tables.TryGetValue("hmtx", out var hmtx) || numberOfHMetrics <= 0) return;

        _advanceWidths = new ushort[NumGlyphs];
        ushort last = 0;

        for (int i = 0; i < NumGlyphs; i++)
        {
            if (i < numberOfHMetrics)
            {
                int offset = hmtx.offset + i * 4;
                if (offset + 2 > _data.Length) break;
                last = ReadUInt16(offset);
            }
            _advanceWidths[i] = last;
        }
    }

    private void LoadLoca(short indexToLocFormat)
    {
        if (!_tables.TryGetValue("loca", out var loca)) return;

        _loca = new uint[NumGlyphs + 1];
        for (int i = 0; i <= NumGlyphs; i++)
        {
            if (indexToLocFormat == 0)
            {
                int offset = loca.offset + i * 2;
                if (offset + 2 > _data.Length) break;
                _loca[i] = (uint)ReadUInt16(offset) * 2;
            }
            else
            {
                int offset = loca.offset + i * 4;
                if (offset + 4 > _data.Length) break;
                _loca[i] = ReadUInt32(offset);
            }
        }
    }

    private void LoadCmap()
    {
        if (!_tables.TryGetValue("cmap", out var cmap)) return;

        int numSubtables = ReadUInt16(cmap.offset + 2);
        int best = -1, bestScore = -1;

        for (int i = 0; i < numSubtables; i++)
        {
            int record = cmap.offset + 4 + i * 8;
            if (record + 8 > _data.Length) break;

            int platform = ReadUInt16(record);
            int encoding = ReadUInt16(record + 2);
            int subtableOffset = cmap.offset + (int)ReadUInt32(record + 4);

            int score = (platform, encoding) switch
            {
                (3, 10) => 5,
                (0, 4) or (0, 6) => 4,
                (3, 1) => 3,
                (0, _) => 2,
                (3, 0) => 1,
                _ => 0
            };

            if (score > bestScore) { bestScore = score; best = subtableOffset; }
        }

        if (best < 0 || best + 4 > _data.Length) return;

        int format = ReadUInt16(best);
        if (format == 4) ParseCmapFormat4(best);
        else if (format == 12) ParseCmapFormat12(best);
        else if (format == 6) ParseCmapFormat6(best);
    }

    private void ParseCmapFormat4(int offset)
    {
        int segCountX2 = ReadUInt16(offset + 6);
        int segCount = segCountX2 / 2;

        int endCodes = offset + 14;
        int startCodes = endCodes + segCountX2 + 2;
        int idDeltas = startCodes + segCountX2;
        int idRangeOffsets = idDeltas + segCountX2;

        for (int s = 0; s < segCount; s++)
        {
            int end = ReadUInt16(endCodes + s * 2);
            int start = ReadUInt16(startCodes + s * 2);
            short delta = ReadInt16(idDeltas + s * 2);
            int rangeOffset = ReadUInt16(idRangeOffsets + s * 2);

            if (start > end) continue;

            for (int c = start; c <= end && c <= 0xFFFF; c++)
            {
                ushort glyph;
                if (rangeOffset == 0)
                {
                    glyph = (ushort)((c + delta) & 0xFFFF);
                }
                else
                {
                    int glyphIndexAddress = idRangeOffsets + s * 2 + rangeOffset + (c - start) * 2;
                    if (glyphIndexAddress + 2 > _data.Length) continue;
                    glyph = ReadUInt16(glyphIndexAddress);
                    if (glyph != 0) glyph = (ushort)((glyph + delta) & 0xFFFF);
                }

                if (glyph != 0) _cmap.TryAdd(c, glyph);
            }
        }
    }

    private void ParseCmapFormat6(int offset)
    {
        int first = ReadUInt16(offset + 6);
        int count = ReadUInt16(offset + 8);
        for (int i = 0; i < count; i++)
        {
            ushort glyph = ReadUInt16(offset + 10 + i * 2);
            if (glyph != 0) _cmap.TryAdd(first + i, glyph);
        }
    }

    private void ParseCmapFormat12(int offset)
    {
        uint groups = ReadUInt32(offset + 12);
        for (uint g = 0; g < groups && g < 100_000; g++)
        {
            int record = offset + 16 + (int)g * 12;
            if (record + 12 > _data.Length) break;

            uint start = ReadUInt32(record);
            uint end = ReadUInt32(record + 4);
            uint startGlyph = ReadUInt32(record + 8);

            for (uint c = start; c <= end && c - start < 65536; c++)
                _cmap.TryAdd((int)c, (ushort)(startGlyph + (c - start)));
        }
    }

    private string? ReadPostScriptName()
    {
        if (!_tables.TryGetValue("name", out var name)) return null;

        int count = ReadUInt16(name.offset + 2);
        int stringOffset = name.offset + ReadUInt16(name.offset + 4);

        for (int i = 0; i < count; i++)
        {
            int record = name.offset + 6 + i * 12;
            if (record + 12 > _data.Length) break;

            int nameId = ReadUInt16(record + 6);
            if (nameId != 6) continue;

            int platform = ReadUInt16(record);
            int length = ReadUInt16(record + 8);
            int offset = stringOffset + ReadUInt16(record + 10);
            if (offset + length > _data.Length) continue;

            var value = platform == 3
                ? Encoding.BigEndianUnicode.GetString(_data, offset, length)
                : Encoding.ASCII.GetString(_data, offset, length);

            value = new string(value.Where(c => c > 32 && c < 127 && c != '/' && c != '(' && c != ')').ToArray());
            if (value.Length > 0) return value;
        }

        return null;
    }

    // ── Tra cứu ──────────────────────────────────────────────────────────
    public ushort GetGlyphId(int codepoint) => _cmap.TryGetValue(codepoint, out var glyph) ? glyph : (ushort)0;

    public bool HasGlyph(int codepoint) => _cmap.ContainsKey(codepoint);

    /// <summary>Bề rộng glyph quy về hệ 1000 đơn vị/em như PDF yêu cầu.</summary>
    public double GetAdvanceWidth(ushort glyphId)
    {
        if (glyphId >= _advanceWidths.Length) return 500;
        return _advanceWidths[glyphId] * 1000.0 / UnitsPerEm;
    }

    public short ScaleToPdf(short value) => (short)Math.Round(value * 1000.0 / UnitsPerEm);

    // ── Rút gọn font ─────────────────────────────────────────────────────
    /// <summary>
    /// Tạo tệp font mới chỉ giữ dữ liệu đường viền của các glyph được dùng.
    /// Chỉ số glyph không đổi nên vẫn dùng được Identity-H / CIDToGIDMap Identity.
    /// </summary>
    public byte[] BuildSubset(IEnumerable<ushort> usedGlyphs)
    {
        var keep = new HashSet<ushort> { 0 };
        foreach (var glyph in usedGlyphs)
        {
            keep.Add(glyph);
            AddCompositeComponents(glyph, keep, 0);
        }

        var glyf = _tables["glyf"];
        using var glyfOutput = new MemoryStream();
        var newLoca = new uint[NumGlyphs + 1];

        for (int gid = 0; gid < NumGlyphs; gid++)
        {
            newLoca[gid] = (uint)glyfOutput.Length;

            if (!keep.Contains((ushort)gid)) continue;
            if (gid + 1 >= _loca.Length) continue;

            int start = glyf.offset + (int)_loca[gid];
            int length = (int)(_loca[gid + 1] - _loca[gid]);
            if (length <= 0 || start + length > _data.Length) continue;

            glyfOutput.Write(_data, start, length);
            while (glyfOutput.Length % 4 != 0) glyfOutput.WriteByte(0);
        }

        newLoca[NumGlyphs] = (uint)glyfOutput.Length;

        var locaBytes = new byte[(NumGlyphs + 1) * 4];
        for (int i = 0; i <= NumGlyphs; i++)
            BinaryPrimitives.WriteUInt32BigEndian(locaBytes.AsSpan(i * 4, 4), newLoca[i]);

        // head phải đổi indexToLocFormat sang dạng offset dài.
        var headBytes = CopyTable("head");
        if (headBytes.Length >= 52) BinaryPrimitives.WriteInt16BigEndian(headBytes.AsSpan(50, 2), 1);

        var output = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["head"] = headBytes,
            ["hhea"] = CopyTable("hhea"),
            ["maxp"] = CopyTable("maxp"),
            ["hmtx"] = CopyTable("hmtx"),
            ["loca"] = locaBytes,
            ["glyf"] = glyfOutput.ToArray()
        };

        foreach (var optional in new[] { "cvt ", "fpgm", "prep" })
            if (_tables.ContainsKey(optional)) output[optional] = CopyTable(optional);

        return BuildFontFile(output);
    }

    private void AddCompositeComponents(ushort glyphId, HashSet<ushort> keep, int depth)
    {
        if (depth > 8 || glyphId + 1 >= _loca.Length) return;

        var glyf = _tables["glyf"];
        int start = glyf.offset + (int)_loca[glyphId];
        int length = (int)(_loca[glyphId + 1] - _loca[glyphId]);
        if (length < 10 || start + length > _data.Length) return;

        short numberOfContours = ReadInt16(start);
        if (numberOfContours >= 0) return; // glyph đơn

        int pos = start + 10;
        while (pos + 4 <= start + length)
        {
            int flags = ReadUInt16(pos);
            ushort component = ReadUInt16(pos + 2);
            pos += 4;

            if (keep.Add(component)) AddCompositeComponents(component, keep, depth + 1);

            pos += (flags & 1) != 0 ? 4 : 2;          // ARG_1_AND_2_ARE_WORDS
            if ((flags & 8) != 0) pos += 2;            // WE_HAVE_A_SCALE
            else if ((flags & 0x40) != 0) pos += 4;    // X_AND_Y_SCALE
            else if ((flags & 0x80) != 0) pos += 8;    // TWO_BY_TWO

            if ((flags & 0x20) == 0) break;            // MORE_COMPONENTS
        }
    }

    private byte[] CopyTable(string tag)
    {
        if (!_tables.TryGetValue(tag, out var table)) return Array.Empty<byte>();
        int length = Math.Min(table.length, _data.Length - table.offset);
        if (length <= 0) return Array.Empty<byte>();

        var copy = new byte[length];
        Buffer.BlockCopy(_data, table.offset, copy, 0, length);
        return copy;
    }

    private static byte[] BuildFontFile(Dictionary<string, byte[]> tables)
    {
        var tags = tables.Keys.OrderBy(t => t, StringComparer.Ordinal).ToList();
        int numTables = tags.Count;

        int searchRange = 16;
        int entrySelector = 0;
        while (searchRange * 2 <= numTables * 16) { searchRange *= 2; entrySelector++; }

        using var output = new MemoryStream();
        var header = new byte[12];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4, 2), (ushort)numTables);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6, 2), (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8, 2), (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(10, 2), (ushort)(numTables * 16 - searchRange));
        output.Write(header);

        int offset = 12 + numTables * 16;
        var records = new List<byte[]>();

        foreach (var tag in tags)
        {
            var data = tables[tag];
            var record = new byte[16];
            Encoding.ASCII.GetBytes(tag).CopyTo(record, 0);
            BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(4, 4), Checksum(data));
            BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(8, 4), (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(12, 4), (uint)data.Length);
            records.Add(record);
            offset += (data.Length + 3) / 4 * 4;
        }

        foreach (var record in records) output.Write(record);

        foreach (var tag in tags)
        {
            var data = tables[tag];
            output.Write(data);
            for (int pad = data.Length; pad % 4 != 0; pad++) output.WriteByte(0);
        }

        return output.ToArray();
    }

    private static uint Checksum(byte[] data)
    {
        uint sum = 0;
        for (int i = 0; i + 3 < data.Length; i += 4)
            sum += BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i, 4));
        return sum;
    }

    // ── Đọc số ───────────────────────────────────────────────────────────
    private ushort ReadUInt16(int offset) =>
        offset + 2 <= _data.Length ? BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(offset, 2)) : (ushort)0;

    private short ReadInt16(int offset) =>
        offset + 2 <= _data.Length ? BinaryPrimitives.ReadInt16BigEndian(_data.AsSpan(offset, 2)) : (short)0;

    private uint ReadUInt32(int offset) =>
        offset + 4 <= _data.Length ? BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(offset, 4)) : 0u;

    private int ReadInt32(int offset) =>
        offset + 4 <= _data.Length ? BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(offset, 4)) : 0;

    // ── Tìm font hệ thống ────────────────────────────────────────────────
    private static readonly Dictionary<string, PdfTrueTypeFont?> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] SearchDirectories =
    {
        "/System/Library/Fonts/Supplemental", "/System/Library/Fonts", "/Library/Fonts",
        "C:\\Windows\\Fonts",
        "/usr/share/fonts/truetype/dejavu", "/usr/share/fonts/truetype/liberation",
        "/usr/share/fonts/truetype", "/usr/share/fonts"
    };

    private static readonly string[] RegularCandidates =
    {
        "Arial Unicode.ttf", "Arial.ttf", "arial.ttf", "Helvetica.ttf",
        "segoeui.ttf", "tahoma.ttf", "verdana.ttf",
        "DejaVuSans.ttf", "LiberationSans-Regular.ttf", "NotoSans-Regular.ttf", "FreeSans.ttf"
    };

    private static readonly string[] BoldCandidates =
    {
        "Arial Bold.ttf", "arialbd.ttf", "Arial-Bold.ttf", "segoeuib.ttf", "tahomabd.ttf",
        "DejaVuSans-Bold.ttf", "LiberationSans-Bold.ttf", "NotoSans-Bold.ttf", "FreeSansBold.ttf"
    };

    /// <summary>
    /// Tìm một font TrueType Unicode có sẵn trên máy để nhúng vào PDF.
    /// Trả về null nếu không tìm thấy — khi đó phải dùng font chuẩn Helvetica.
    /// </summary>
    public static PdfTrueTypeFont? FindSystemFont(bool bold = false)
    {
        string key = bold ? "bold" : "regular";
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var candidates = bold ? BoldCandidates.Concat(RegularCandidates) : RegularCandidates;

        foreach (var directory in SearchDirectories)
        {
            if (!Directory.Exists(directory)) continue;

            foreach (var candidate in candidates)
            {
                var path = Path.Combine(directory, candidate);
                if (!File.Exists(path)) continue;

                var font = FromFile(path);
                if (font != null)
                {
                    Cache[key] = font;
                    return font;
                }
            }
        }

        Cache[key] = null;
        return null;
    }
}
