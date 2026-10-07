using System.Buffers.Binary;
using System.Text;

namespace Wocel.Core.Pdf;

/// <summary>Ảnh đã tách khỏi PDF.</summary>
public sealed class PdfExtractedImage
{
    public required string FileName { get; init; }
    public required byte[] Data { get; init; }
    public required string Extension { get; init; }
    public int PageIndex { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

/// <summary>Kết quả đọc một tệp ảnh từ đĩa.</summary>
public sealed class RasterImage
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    /// <summary>Số kênh màu: 1 = xám, 3 = RGB.</summary>
    public required int Channels { get; init; }
    public required int BitsPerComponent { get; init; }
    /// <summary>Mẫu màu thô, đã bỏ kênh alpha.</summary>
    public required byte[] Samples { get; init; }
    /// <summary>Kênh alpha 8-bit (nếu ảnh có nền trong suốt).</summary>
    public byte[]? Alpha { get; init; }
    /// <summary>Nếu khác null: dữ liệu JPEG gốc, nhúng thẳng vào PDF không cần giải nén.</summary>
    public byte[]? JpegPassthrough { get; init; }
}

/// <summary>
/// Đọc/ghi ảnh không dùng thư viện ngoài: JPEG (nhúng thẳng), PNG (giải mã + mã hoá), BMP.
/// Dùng cho chức năng "Ảnh → PDF" và "Tách ảnh khỏi PDF".
/// </summary>
public static class PdfImageCodec
{
    public static bool IsSupportedImageExtension(string extension) =>
        extension.ToLowerInvariant().TrimStart('.') is "jpg" or "jpeg" or "png" or "bmp";

    // ─────────────────────────────────────────────────────────────────────
    //  ĐỌC TỆP ẢNH
    // ─────────────────────────────────────────────────────────────────────
    public static RasterImage ReadImageFile(byte[] data, string extension)
    {
        var kind = DetectFormat(data, extension);
        return kind switch
        {
            "jpg" => ReadJpeg(data),
            "png" => ReadPng(data),
            "bmp" => ReadBmp(data),
            _ => throw new NotSupportedException($"Định dạng ảnh không hỗ trợ: {extension}")
        };
    }

    private static string DetectFormat(byte[] data, string extension)
    {
        if (data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8) return "jpg";
        if (data.Length > 8 && data[0] == 0x89 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G') return "png";
        if (data.Length > 2 && data[0] == 'B' && data[1] == 'M') return "bmp";

        return extension.ToLowerInvariant().TrimStart('.') switch
        {
            "jpg" or "jpeg" => "jpg",
            "png" => "png",
            "bmp" => "bmp",
            _ => "unknown"
        };
    }

    private static RasterImage ReadJpeg(byte[] data)
    {
        int width = 0, height = 0, components = 3;
        int i = 2;

        while (i + 9 < data.Length)
        {
            if (data[i] != 0xFF) { i++; continue; }

            byte marker = data[i + 1];
            if (marker is 0xD8 or 0x01 or >= 0xD0 and <= 0xD7) { i += 2; continue; }

            int segmentLength = (data[i + 2] << 8) | data[i + 3];
            bool isStartOfFrame = marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);

            if (isStartOfFrame)
            {
                height = (data[i + 5] << 8) | data[i + 6];
                width = (data[i + 7] << 8) | data[i + 8];
                components = data[i + 9];
                break;
            }

            i += 2 + segmentLength;
        }

        if (width <= 0 || height <= 0) throw new InvalidDataException("Không đọc được kích thước ảnh JPEG.");

        return new RasterImage
        {
            Width = width,
            Height = height,
            Channels = components,
            BitsPerComponent = 8,
            Samples = Array.Empty<byte>(),
            JpegPassthrough = data
        };
    }

    private static RasterImage ReadPng(byte[] data)
    {
        int width = 0, height = 0, bitDepth = 8, colorType = 6, interlace = 0;
        byte[]? palette = null;
        byte[]? transparency = null;
        using var idat = new MemoryStream();

        int pos = 8;
        while (pos + 8 <= data.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos, 4));
            string type = Encoding.ASCII.GetString(data, pos + 4, 4);
            int dataStart = pos + 8;
            if (length < 0 || dataStart + length > data.Length) break;

            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(dataStart, 4));
                    height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(dataStart + 4, 4));
                    bitDepth = data[dataStart + 8];
                    colorType = data[dataStart + 9];
                    interlace = data[dataStart + 12];
                    break;
                case "PLTE":
                    palette = data[dataStart..(dataStart + length)];
                    break;
                case "tRNS":
                    transparency = data[dataStart..(dataStart + length)];
                    break;
                case "IDAT":
                    idat.Write(data, dataStart, length);
                    break;
                case "IEND":
                    pos = data.Length;
                    break;
            }

            pos = dataStart + length + 4;
        }

        if (width <= 0 || height <= 0) throw new InvalidDataException("Tệp PNG không hợp lệ.");
        if (interlace != 0) throw new NotSupportedException("Chưa hỗ trợ PNG interlaced (Adam7). Hãy lưu lại ảnh ở dạng thường.");

        int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 3 };

        var inflated = PdfFilters.FlateDecode(idat.ToArray());
        var parms = new PdfDictionary();
        parms.SetInt("Predictor", 15);
        parms.SetInt("Colors", channels);
        parms.SetInt("BitsPerComponent", bitDepth);
        parms.SetInt("Columns", width);
        var raw = PdfFilters.ApplyPredictor(inflated, parms, null);

        // Bảng màu → RGB, và tách kênh alpha ra riêng.
        if (colorType == 3 && palette != null)
        {
            var expanded = ExpandIndexed(raw, width, height, bitDepth, palette);
            byte[]? alphaFromTrns = null;

            if (transparency != null)
            {
                alphaFromTrns = new byte[width * height];
                var indices = ExpandIndices(raw, width, height, bitDepth);
                for (int i = 0; i < alphaFromTrns.Length; i++)
                {
                    int index = indices[i];
                    alphaFromTrns[i] = index < transparency.Length ? transparency[index] : (byte)255;
                }
            }

            return new RasterImage
            {
                Width = width, Height = height, Channels = 3, BitsPerComponent = 8,
                Samples = expanded, Alpha = alphaFromTrns
            };
        }

        if (colorType is 4 or 6 && bitDepth == 8)
        {
            int colorChannels = colorType == 4 ? 1 : 3;
            var samples = new byte[width * height * colorChannels];
            var alpha = new byte[width * height];

            for (int i = 0, s = 0, a = 0; i + colorChannels < raw.Length; i += colorChannels + 1)
            {
                for (int c = 0; c < colorChannels; c++) samples[s++] = raw[i + c];
                alpha[a++] = raw[i + colorChannels];
            }

            return new RasterImage
            {
                Width = width, Height = height, Channels = colorChannels, BitsPerComponent = 8,
                Samples = samples, Alpha = alpha
            };
        }

        return new RasterImage
        {
            Width = width, Height = height,
            Channels = colorType == 0 ? 1 : 3,
            BitsPerComponent = bitDepth,
            Samples = raw
        };
    }

    private static int[] ExpandIndices(byte[] raw, int width, int height, int bitDepth)
    {
        var indices = new int[width * height];
        int rowBytes = (width * bitDepth + 7) / 8;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int bitPosition = x * bitDepth;
                int byteIndex = y * rowBytes + bitPosition / 8;
                if (byteIndex >= raw.Length) continue;

                int shift = 8 - bitDepth - bitPosition % 8;
                indices[y * width + x] = (raw[byteIndex] >> shift) & ((1 << bitDepth) - 1);
            }
        }

        return indices;
    }

    private static byte[] ExpandIndexed(byte[] raw, int width, int height, int bitDepth, byte[] palette)
    {
        var indices = ExpandIndices(raw, width, height, bitDepth);
        var output = new byte[width * height * 3];

        for (int i = 0; i < indices.Length; i++)
        {
            int offset = indices[i] * 3;
            output[i * 3] = offset < palette.Length ? palette[offset] : (byte)0;
            output[i * 3 + 1] = offset + 1 < palette.Length ? palette[offset + 1] : (byte)0;
            output[i * 3 + 2] = offset + 2 < palette.Length ? palette[offset + 2] : (byte)0;
        }

        return output;
    }

    private static RasterImage ReadBmp(byte[] data)
    {
        if (data.Length < 54) throw new InvalidDataException("Tệp BMP không hợp lệ.");

        int dataOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(10, 4));
        int width = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(18, 4));
        int height = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(22, 4));
        int bpp = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(28, 2));
        int compression = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(30, 4));

        if (compression != 0 || bpp is not (24 or 32))
            throw new NotSupportedException("Chỉ hỗ trợ BMP 24/32-bit không nén.");

        bool bottomUp = height > 0;
        height = Math.Abs(height);

        int bytesPerPixel = bpp / 8;
        int rowSize = (width * bytesPerPixel + 3) / 4 * 4;
        var samples = new byte[width * height * 3];

        for (int y = 0; y < height; y++)
        {
            int sourceRow = bottomUp ? height - 1 - y : y;
            int rowStart = dataOffset + sourceRow * rowSize;

            for (int x = 0; x < width; x++)
            {
                int source = rowStart + x * bytesPerPixel;
                if (source + 2 >= data.Length) continue;

                int target = (y * width + x) * 3;
                samples[target] = data[source + 2];
                samples[target + 1] = data[source + 1];
                samples[target + 2] = data[source];
            }
        }

        return new RasterImage
        {
            Width = width, Height = height, Channels = 3, BitsPerComponent = 8, Samples = samples
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    //  TẠO XOBJECT ẢNH TRONG PDF
    // ─────────────────────────────────────────────────────────────────────
    public static PdfRef CreateImageXObject(PdfDocument document, RasterImage image)
    {
        var dict = new PdfDictionary();
        dict.SetName("Type", "XObject");
        dict.SetName("Subtype", "Image");
        dict.SetInt("Width", image.Width);
        dict.SetInt("Height", image.Height);
        dict.SetInt("BitsPerComponent", image.JpegPassthrough != null ? 8 : image.BitsPerComponent);

        int channels = image.JpegPassthrough != null ? image.Channels : image.Channels;
        dict.SetName("ColorSpace", channels switch
        {
            1 => "DeviceGray",
            4 => "DeviceCMYK",
            _ => "DeviceRGB"
        });

        PdfStream stream;
        if (image.JpegPassthrough != null)
        {
            dict.SetName("Filter", "DCTDecode");
            dict.SetInt("Length", image.JpegPassthrough.Length);
            stream = new PdfStream(dict, image.JpegPassthrough);
        }
        else
        {
            stream = PdfStream.FromDecoded(dict, image.Samples);
        }

        if (image.Alpha is { Length: > 0 })
        {
            var maskDict = new PdfDictionary();
            maskDict.SetName("Type", "XObject");
            maskDict.SetName("Subtype", "Image");
            maskDict.SetInt("Width", image.Width);
            maskDict.SetInt("Height", image.Height);
            maskDict.SetInt("BitsPerComponent", 8);
            maskDict.SetName("ColorSpace", "DeviceGray");
            var mask = PdfStream.FromDecoded(maskDict, image.Alpha);
            dict["SMask"] = document.Add(mask);
        }

        return document.Add(stream);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  TÁCH ẢNH KHỎI PDF
    // ─────────────────────────────────────────────────────────────────────
    public static List<PdfExtractedImage> ExtractImages(PdfDocument document, string baseName = "image")
    {
        var results = new List<PdfExtractedImage>();
        var seen = new HashSet<PdfStream>();

        for (int pageIndex = 0; pageIndex < document.Pages.Count; pageIndex++)
        {
            var page = document.Pages[pageIndex];
            CollectImages(document, page.Resources, pageIndex, baseName, results, seen, 0);
        }

        return results;
    }

    private static void CollectImages(
        PdfDocument document, PdfDictionary resources, int pageIndex, string baseName,
        List<PdfExtractedImage> results, HashSet<PdfStream> seen, int depth)
    {
        if (depth > 6) return;

        var xobjects = resources.GetDictionary("XObject", document);
        if (xobjects == null) return;

        foreach (var key in xobjects.Items.Keys.ToList())
        {
            if (document.Resolve(xobjects[key]) is not PdfStream stream) continue;

            var subtype = stream.Dictionary.GetName("Subtype", document);

            if (subtype == "Form")
            {
                var inner = stream.Dictionary.GetDictionary("Resources", document);
                if (inner != null) CollectImages(document, inner, pageIndex, baseName, results, seen, depth + 1);
                continue;
            }

            if (subtype != "Image" || !seen.Add(stream)) continue;

            try
            {
                var extracted = ConvertImageStream(document, stream, pageIndex, baseName, results.Count + 1);
                if (extracted != null) results.Add(extracted);
            }
            catch
            {
                // Ảnh dùng bộ lọc chưa hỗ trợ (JBIG2/CCITT) — bỏ qua ảnh đó.
            }
        }
    }

    private static PdfExtractedImage? ConvertImageStream(
        PdfDocument document, PdfStream stream, int pageIndex, string baseName, int index)
    {
        int width = stream.Dictionary.GetInt("Width", 0, document);
        int height = stream.Dictionary.GetInt("Height", 0, document);
        if (width <= 0 || height <= 0) return null;

        var imageFilter = stream.ImageFilterName(document);
        var data = stream.GetDecodedData(document);

        if (imageFilter is "DCTDecode" or "DCT")
        {
            return new PdfExtractedImage
            {
                FileName = $"{baseName}_p{pageIndex + 1}_{index}.jpg",
                Data = data, Extension = ".jpg",
                PageIndex = pageIndex, Width = width, Height = height
            };
        }

        if (imageFilter == "JPXDecode")
        {
            return new PdfExtractedImage
            {
                FileName = $"{baseName}_p{pageIndex + 1}_{index}.jp2",
                Data = data, Extension = ".jp2",
                PageIndex = pageIndex, Width = width, Height = height
            };
        }

        if (imageFilter != null) return null; // CCITTFax/JBIG2 — chưa hỗ trợ

        int bpc = stream.Dictionary.GetInt("BitsPerComponent", 8, document);
        var (samples, channels) = NormalizeToRgbOrGray(document, stream, data, width, height, bpc);
        if (samples.Length == 0) return null;

        var png = EncodePng(samples, width, height, channels, 8);
        return new PdfExtractedImage
        {
            FileName = $"{baseName}_p{pageIndex + 1}_{index}.png",
            Data = png, Extension = ".png",
            PageIndex = pageIndex, Width = width, Height = height
        };
    }

    private static (byte[] samples, int channels) NormalizeToRgbOrGray(
        PdfDocument document, PdfStream stream, byte[] data, int width, int height, int bpc)
    {
        var colorSpace = document.Resolve(stream.Dictionary["ColorSpace"]);

        if (stream.Dictionary.GetBool("ImageMask", false, document))
        {
            var mask = new byte[width * height];
            int rowBytes = (width + 7) / 8;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int byteIndex = y * rowBytes + x / 8;
                    int bit = byteIndex < data.Length ? (data[byteIndex] >> (7 - x % 8)) & 1 : 1;
                    mask[y * width + x] = (byte)(bit == 0 ? 0 : 255);
                }
            return (mask, 1);
        }

        // /Indexed → bung ra RGB
        if (colorSpace is PdfArray array && array.Count >= 4
            && document.Resolve(array[0]) is PdfName { Value: "Indexed" or "I" })
        {
            var lookup = document.Resolve(array[3]) switch
            {
                PdfString s => s.Bytes,
                PdfStream ls => ls.GetDecodedData(document),
                _ => Array.Empty<byte>()
            };

            var baseName = document.Resolve(array[1]) as PdfName;
            int baseChannels = baseName?.Value switch
            {
                "DeviceGray" or "CalGray" or "G" => 1,
                "DeviceCMYK" => 4,
                _ => 3
            };

            var indices = ExpandIndices(data, width, height, bpc);
            var output = new byte[width * height * 3];
            for (int i = 0; i < indices.Length; i++)
            {
                int offset = indices[i] * baseChannels;
                if (baseChannels == 1)
                {
                    byte g = offset < lookup.Length ? lookup[offset] : (byte)0;
                    output[i * 3] = output[i * 3 + 1] = output[i * 3 + 2] = g;
                }
                else if (baseChannels == 4)
                {
                    var rgb = CmykToRgb(
                        Sample(lookup, offset), Sample(lookup, offset + 1),
                        Sample(lookup, offset + 2), Sample(lookup, offset + 3));
                    output[i * 3] = rgb.r; output[i * 3 + 1] = rgb.g; output[i * 3 + 2] = rgb.b;
                }
                else
                {
                    output[i * 3] = Sample(lookup, offset);
                    output[i * 3 + 1] = Sample(lookup, offset + 1);
                    output[i * 3 + 2] = Sample(lookup, offset + 2);
                }
            }
            return (output, 3);
        }

        int channels = ColorSpaceChannels(document, colorSpace);

        if (bpc != 8)
        {
            // Bung mẫu 1/2/4/16-bit về 8-bit.
            data = ResampleTo8Bit(data, width, height, channels, bpc);
            bpc = 8;
        }

        if (channels == 4)
        {
            var rgbOutput = new byte[width * height * 3];
            for (int i = 0, o = 0; i + 3 < data.Length && o + 2 < rgbOutput.Length; i += 4, o += 3)
            {
                var (r, g, b) = CmykToRgb(data[i], data[i + 1], data[i + 2], data[i + 3]);
                rgbOutput[o] = r; rgbOutput[o + 1] = g; rgbOutput[o + 2] = b;
            }
            return (rgbOutput, 3);
        }

        int expected = width * height * channels;
        if (data.Length < expected) Array.Resize(ref data, expected);
        return (data, channels);
    }

    private static byte Sample(byte[] data, int index) => index >= 0 && index < data.Length ? data[index] : (byte)0;

    private static int ColorSpaceChannels(PdfDocument document, PdfObject? colorSpace)
    {
        switch (colorSpace)
        {
            case PdfName name:
                return name.Value switch
                {
                    "DeviceGray" or "CalGray" or "G" => 1,
                    "DeviceCMYK" or "CMYK" => 4,
                    _ => 3
                };
            case PdfArray array when array.Count > 0:
            {
                var first = document.Resolve(array[0]) as PdfName;
                if (first?.Value == "ICCBased" && array.Count > 1 && document.Resolve(array[1]) is PdfStream icc)
                    return icc.Dictionary.GetInt("N", 3, document);
                if (first?.Value is "DeviceN" && array.Count > 1 && document.Resolve(array[1]) is PdfArray names)
                    return names.Count;
                if (first?.Value is "Separation") return 1;
                if (first?.Value is "CalGray") return 1;
                if (first?.Value is "DeviceCMYK") return 4;
                return 3;
            }
            default:
                return 1;
        }
    }

    private static byte[] ResampleTo8Bit(byte[] data, int width, int height, int channels, int bpc)
    {
        var output = new byte[width * height * channels];

        if (bpc == 16)
        {
            for (int i = 0; i * 2 + 1 < data.Length && i < output.Length; i++) output[i] = data[i * 2];
            return output;
        }

        int rowBits = width * channels * bpc;
        int rowBytes = (rowBits + 7) / 8;
        int max = (1 << bpc) - 1;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width * channels; x++)
            {
                int bitPosition = x * bpc;
                int byteIndex = y * rowBytes + bitPosition / 8;
                if (byteIndex >= data.Length) continue;

                int shift = 8 - bpc - bitPosition % 8;
                int value = (data[byteIndex] >> shift) & max;
                int target = y * width * channels + x;
                if (target < output.Length) output[target] = (byte)(value * 255 / max);
            }
        }

        return output;
    }

    private static (byte r, byte g, byte b) CmykToRgb(byte c, byte m, byte y, byte k)
    {
        double cc = c / 255.0, mm = m / 255.0, yy = y / 255.0, kk = k / 255.0;
        return (
            (byte)Math.Clamp(255 * (1 - cc) * (1 - kk), 0, 255),
            (byte)Math.Clamp(255 * (1 - mm) * (1 - kk), 0, 255),
            (byte)Math.Clamp(255 * (1 - yy) * (1 - kk), 0, 255));
    }

    // ─────────────────────────────────────────────────────────────────────
    //  GHI PNG
    // ─────────────────────────────────────────────────────────────────────
    public static byte[] EncodePng(byte[] samples, int width, int height, int channels, int bitDepth)
    {
        byte colorType = channels switch { 1 => 0, 2 => 4, 4 => 6, _ => 2 };
        int rowBytes = width * channels * bitDepth / 8;

        using var raw = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            raw.WriteByte(0); // filter None
            int offset = y * rowBytes;
            int count = Math.Min(rowBytes, Math.Max(0, samples.Length - offset));
            if (count > 0) raw.Write(samples, offset, count);
            for (int pad = count; pad < rowBytes; pad++) raw.WriteByte(0);
        }

        using var output = new MemoryStream();
        output.Write(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A });

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = (byte)bitDepth;
        header[9] = colorType;
        WriteChunk(output, "IHDR", header);
        WriteChunk(output, "IDAT", PdfFilters.FlateEncode(raw.ToArray()));
        WriteChunk(output, "IEND", Array.Empty<byte>());

        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        uint crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] a, byte[] b)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var x in a) crc = CrcTable[(crc ^ x) & 0xFF] ^ (crc >> 8);
        foreach (var x in b) crc = CrcTable[(crc ^ x) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFF;
    }
}
