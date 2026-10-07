using System.Globalization;
using System.Text;

namespace Wocel.Core.Pdf;

/// <summary>
/// Mô hình đối tượng PDF (COS objects) theo ISO 32000-1 §7.3.
/// Không phụ thuộc thư viện ngoài.
/// </summary>
public abstract class PdfObject
{
    public static readonly PdfNull Null = PdfNull.Instance;
}

public sealed class PdfNull : PdfObject
{
    public static readonly PdfNull Instance = new();
    private PdfNull() { }
    public override string ToString() => "null";
}

public sealed class PdfBool : PdfObject
{
    public static readonly PdfBool True = new(true);
    public static readonly PdfBool False = new(false);
    public bool Value { get; }
    private PdfBool(bool value) => Value = value;
    public static PdfBool Of(bool v) => v ? True : False;
    public override string ToString() => Value ? "true" : "false";
}

public sealed class PdfNumber : PdfObject
{
    public double Value { get; }
    public bool IsInteger { get; }

    public PdfNumber(double value)
    {
        Value = value;
        IsInteger = false;
    }

    public PdfNumber(long value)
    {
        Value = value;
        IsInteger = true;
    }

    public int AsInt => (int)Math.Round(Value);
    public long AsLong => (long)Math.Round(Value);
    public float AsFloat => (float)Value;

    public override string ToString() => IsInteger
        ? AsLong.ToString(CultureInfo.InvariantCulture)
        : Value.ToString("0.######", CultureInfo.InvariantCulture);
}

/// <summary>Tên PDF, lưu KHÔNG kèm dấu '/' đứng đầu.</summary>
public sealed class PdfName : PdfObject
{
    public string Value { get; }
    public PdfName(string value) => Value = value;
    public override string ToString() => "/" + Value;
    public override bool Equals(object? obj) => obj is PdfName n && n.Value == Value;
    public override int GetHashCode() => Value.GetHashCode();
}

/// <summary>Chuỗi PDF — luôn giữ ở dạng byte thô để không mất dữ liệu nhị phân.</summary>
public sealed class PdfString : PdfObject
{
    public byte[] Bytes { get; set; }
    public bool PreferHex { get; set; }

    public PdfString(byte[] bytes, bool preferHex = false)
    {
        Bytes = bytes;
        PreferHex = preferHex;
    }

    public PdfString(string text) : this(EncodeText(text)) { }

    /// <summary>Giải mã theo quy ước PDF: UTF-16BE nếu có BOM, ngược lại PDFDocEncoding ~ Latin1.</summary>
    public string AsText()
    {
        if (Bytes.Length >= 2 && Bytes[0] == 0xFE && Bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(Bytes, 2, Bytes.Length - 2);
        return Encoding.Latin1.GetString(Bytes);
    }

    /// <summary>Mã hoá text: Latin1 nếu biểu diễn được, ngược lại UTF-16BE kèm BOM (hỗ trợ tiếng Việt).</summary>
    public static byte[] EncodeText(string text)
    {
        bool ascii = text.All(c => c <= 0xFF);
        if (ascii) return Encoding.Latin1.GetBytes(text);
        var utf = Encoding.BigEndianUnicode.GetBytes(text);
        var result = new byte[utf.Length + 2];
        result[0] = 0xFE;
        result[1] = 0xFF;
        Buffer.BlockCopy(utf, 0, result, 2, utf.Length);
        return result;
    }

    public override string ToString() => "(" + AsText() + ")";
}

public sealed class PdfArray : PdfObject
{
    public List<PdfObject> Items { get; } = new();

    public PdfArray() { }
    public PdfArray(IEnumerable<PdfObject> items) => Items.AddRange(items);
    public PdfArray(params double[] numbers) => Items.AddRange(numbers.Select(n => (PdfObject)new PdfNumber(n)));

    public int Count => Items.Count;
    public PdfObject this[int index] => Items[index];
    public void Add(PdfObject obj) => Items.Add(obj);

    public override string ToString() => "[" + string.Join(" ", Items) + "]";
}

public sealed class PdfDictionary : PdfObject
{
    public Dictionary<string, PdfObject> Items { get; } = new(StringComparer.Ordinal);

    public PdfDictionary() { }
    public PdfDictionary(IDictionary<string, PdfObject> items)
    {
        foreach (var kv in items) Items[kv.Key] = kv.Value;
    }

    /// <summary>Truy cập theo tên khoá (không kèm '/'). Trả về null nếu không có.</summary>
    public PdfObject? this[string key]
    {
        get => Items.TryGetValue(key, out var v) ? v : null;
        set
        {
            if (value is null) Items.Remove(key);
            else Items[key] = value;
        }
    }

    public bool Has(string key) => Items.ContainsKey(key);
    public void Remove(string key) => Items.Remove(key);

    public void Set(string key, PdfObject value) => Items[key] = value;
    public void SetName(string key, string name) => Items[key] = new PdfName(name);
    public void SetInt(string key, long value) => Items[key] = new PdfNumber(value);
    public void SetReal(string key, double value) => Items[key] = new PdfNumber(value);
    public void SetText(string key, string value) => Items[key] = new PdfString(value);

    public PdfDictionary Clone()
    {
        var d = new PdfDictionary();
        foreach (var kv in Items) d.Items[kv.Key] = kv.Value;
        return d;
    }

    public override string ToString() =>
        "<<" + string.Join(" ", Items.Select(kv => "/" + kv.Key + " " + kv.Value)) + ">>";
}

/// <summary>Stream = dictionary + dữ liệu thô (chưa giải mã filter).</summary>
public sealed class PdfStream : PdfObject
{
    public PdfDictionary Dictionary { get; }
    /// <summary>Dữ liệu đúng như nằm trong file (đã áp filter).</summary>
    public byte[] RawData { get; set; }

    private byte[]? _decodedCache;

    public PdfStream(PdfDictionary dict, byte[] rawData)
    {
        Dictionary = dict;
        RawData = rawData;
    }

    /// <summary>Tạo stream mới từ dữ liệu chưa nén, tự nén FlateDecode.</summary>
    public static PdfStream FromDecoded(PdfDictionary dict, byte[] data, bool compress = true)
    {
        if (compress)
        {
            var packed = PdfFilters.FlateEncode(data);
            dict.SetName("Filter", "FlateDecode");
            dict.SetInt("Length", packed.Length);
            return new PdfStream(dict, packed) { _decodedCache = data };
        }

        dict.Remove("Filter");
        dict.SetInt("Length", data.Length);
        return new PdfStream(dict, data) { _decodedCache = data };
    }

    /// <summary>Giải mã toàn bộ filter (trừ filter ảnh như DCTDecode/JPXDecode — giữ nguyên).</summary>
    public byte[] GetDecodedData(IPdfResolver? resolver = null)
    {
        if (_decodedCache != null) return _decodedCache;
        _decodedCache = PdfFilters.Decode(RawData, Dictionary, resolver);
        return _decodedCache;
    }

    public void SetDecodedData(byte[] data, bool compress = true)
    {
        if (compress)
        {
            RawData = PdfFilters.FlateEncode(data);
            Dictionary.SetName("Filter", "FlateDecode");
            Dictionary.Remove("DecodeParms");
        }
        else
        {
            RawData = data;
            Dictionary.Remove("Filter");
            Dictionary.Remove("DecodeParms");
        }

        Dictionary.SetInt("Length", RawData.Length);
        _decodedCache = data;
    }

    /// <summary>Tên filter cuối cùng còn lại sau khi giải mã (ví dụ DCTDecode với ảnh JPEG).</summary>
    public string? ImageFilterName(IPdfResolver? resolver = null)
    {
        var filter = resolver?.Resolve(Dictionary["Filter"]) ?? Dictionary["Filter"];
        var names = filter switch
        {
            PdfName n => new[] { n.Value },
            PdfArray a => a.Items.Select(i => (resolver?.Resolve(i) ?? i) as PdfName)
                                 .Where(n => n != null).Select(n => n!.Value).ToArray(),
            _ => Array.Empty<string>()
        };
        return names.FirstOrDefault(PdfFilters.IsImageFilter);
    }

    public override string ToString() => $"stream({RawData.Length} bytes) {Dictionary}";
}

/// <summary>Tham chiếu gián tiếp "n g R".</summary>
public sealed class PdfRef : PdfObject
{
    public int Number { get; }
    public int Generation { get; }

    public PdfRef(int number, int generation = 0)
    {
        Number = number;
        Generation = generation;
    }

    public override string ToString() => $"{Number} {Generation} R";
    public override bool Equals(object? obj) => obj is PdfRef r && r.Number == Number && r.Generation == Generation;
    public override int GetHashCode() => HashCode.Combine(Number, Generation);
}

/// <summary>Giao diện phân giải tham chiếu gián tiếp.</summary>
public interface IPdfResolver
{
    PdfObject? Resolve(PdfObject? obj);
}

/// <summary>Các hàm tiện ích đọc giá trị đã phân giải tham chiếu.</summary>
public static class PdfObjectExtensions
{
    public static PdfObject? Get(this PdfDictionary? dict, string key, IPdfResolver? resolver)
        => dict == null ? null : (resolver != null ? resolver.Resolve(dict[key]) : dict[key]);

    public static PdfDictionary? GetDictionary(this PdfDictionary? dict, string key, IPdfResolver? resolver = null)
        => dict.Get(key, resolver) switch
        {
            PdfDictionary d => d,
            PdfStream s => s.Dictionary,
            _ => null
        };

    public static PdfArray? GetArray(this PdfDictionary? dict, string key, IPdfResolver? resolver = null)
        => dict.Get(key, resolver) as PdfArray;

    public static PdfStream? GetStream(this PdfDictionary? dict, string key, IPdfResolver? resolver = null)
        => dict.Get(key, resolver) as PdfStream;

    public static string? GetName(this PdfDictionary? dict, string key, IPdfResolver? resolver = null)
        => (dict.Get(key, resolver) as PdfName)?.Value;

    public static int GetInt(this PdfDictionary? dict, string key, int fallback = 0, IPdfResolver? resolver = null)
        => dict.Get(key, resolver) is PdfNumber n ? n.AsInt : fallback;

    public static double GetReal(this PdfDictionary? dict, string key, double fallback = 0, IPdfResolver? resolver = null)
        => dict.Get(key, resolver) is PdfNumber n ? n.Value : fallback;

    public static bool GetBool(this PdfDictionary? dict, string key, bool fallback = false, IPdfResolver? resolver = null)
        => dict.Get(key, resolver) is PdfBool b ? b.Value : fallback;

    public static string? GetText(this PdfDictionary? dict, string key, IPdfResolver? resolver = null)
        => (dict.Get(key, resolver) as PdfString)?.AsText();

    public static double AsDouble(this PdfObject? obj, double fallback = 0)
        => obj is PdfNumber n ? n.Value : fallback;

    /// <summary>Đọc mảng 4 số (ví dụ MediaBox) và chuẩn hoá về [llx lly urx ury].</summary>
    public static double[]? AsRectangle(this PdfObject? obj, IPdfResolver? resolver = null)
    {
        if (resolver != null) obj = resolver.Resolve(obj);
        if (obj is not PdfArray arr || arr.Count < 4) return null;

        var v = new double[4];
        for (int i = 0; i < 4; i++)
        {
            var item = resolver != null ? resolver.Resolve(arr[i]) : arr[i];
            if (item is not PdfNumber num) return null;
            v[i] = num.Value;
        }

        return new[]
        {
            Math.Min(v[0], v[2]), Math.Min(v[1], v[3]),
            Math.Max(v[0], v[2]), Math.Max(v[1], v[3])
        };
    }

    public static PdfArray ToPdfArray(this double[] rect) =>
        new(rect.Select(v => (PdfObject)new PdfNumber(v)));
}
