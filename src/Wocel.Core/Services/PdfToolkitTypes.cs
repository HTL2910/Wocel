using Wocel.Core.Pdf;

namespace Wocel.Core.Services;

/// <summary>Lỗi nghiệp vụ khi xử lý PDF — thông điệp đã sẵn sàng hiển thị cho người dùng.</summary>
public sealed class PdfToolException : Exception
{
    public PdfToolException(string message) : base(message) { }
}

/// <summary>Khổ giấy chuẩn (đơn vị point, 1 inch = 72 point).</summary>
public static class PdfPageSizes
{
    public static readonly double[] A3 = { 0, 0, 841.89, 1190.55 };
    public static readonly double[] A4 = { 0, 0, 595.276, 841.89 };
    public static readonly double[] A5 = { 0, 0, 419.53, 595.276 };
    public static readonly double[] Letter = { 0, 0, 612, 792 };
    public static readonly double[] Legal = { 0, 0, 612, 1008 };

    public static double[] ByName(string name) => name.Trim().ToUpperInvariant() switch
    {
        "A3" => (double[])A3.Clone(),
        "A5" => (double[])A5.Clone(),
        "LETTER" => (double[])Letter.Clone(),
        "LEGAL" => (double[])Legal.Clone(),
        _ => (double[])A4.Clone()
    };

    public static double[] Landscape(double[] size) => new[] { 0, 0, Math.Max(size[2], size[3]), Math.Min(size[2], size[3]) };
    public static double[] Portrait(double[] size) => new[] { 0, 0, Math.Min(size[2], size[3]), Math.Max(size[2], size[3]) };

    public static readonly string[] Names = { "A4", "A3", "A5", "Letter", "Legal" };
}

/// <summary>Vị trí đặt nội dung phụ (số trang, header/footer, watermark).</summary>
public enum PdfBoxPosition
{
    TopLeft, TopCenter, TopRight,
    BottomLeft, BottomCenter, BottomRight,
    Center
}

public enum PdfWatermarkLayout
{
    /// <summary>Một chữ lớn nằm chéo giữa trang.</summary>
    Diagonal,
    /// <summary>Một chữ nằm ngang giữa trang.</summary>
    Center,
    /// <summary>Lặp lại phủ kín trang.</summary>
    Tiled
}

public sealed class PdfWatermarkOptions
{
    public string Text { get; set; } = "WOCEL";
    public double FontSize { get; set; } = 48;
    public double Opacity { get; set; } = 0.25;
    public PdfWatermarkLayout Layout { get; set; } = PdfWatermarkLayout.Diagonal;
    public double AngleDegrees { get; set; } = 45;
    public (double R, double G, double B) Color { get; set; } = (0.6, 0.6, 0.6);
    public bool Bold { get; set; } = true;
    /// <summary>Đặt dưới nội dung để không che chữ.</summary>
    public bool BehindContent { get; set; }
    public string? PageRange { get; set; }
}

public sealed class PdfPageNumberOptions
{
    /// <summary>{0} = số trang hiện tại, {1} = tổng số trang.</summary>
    public string Format { get; set; } = "{0}";
    public PdfBoxPosition Position { get; set; } = PdfBoxPosition.BottomCenter;
    public int StartNumber { get; set; } = 1;
    public double FontSize { get; set; } = 10;
    public double Margin { get; set; } = 28;
    public bool SkipFirstPage { get; set; }
    public string? PageRange { get; set; }
    public (double R, double G, double B) Color { get; set; } = (0.2, 0.2, 0.2);
}

public sealed class PdfHeaderFooterOptions
{
    public string? HeaderLeft { get; set; }
    public string? HeaderCenter { get; set; }
    public string? HeaderRight { get; set; }
    public string? FooterLeft { get; set; }
    public string? FooterCenter { get; set; }
    public string? FooterRight { get; set; }
    public double FontSize { get; set; } = 9;
    public double Margin { get; set; } = 24;
    public (double R, double G, double B) Color { get; set; } = (0.35, 0.35, 0.35);
    /// <summary>Cho phép các ký hiệu {page}, {pages}, {date}, {file}.</summary>
    public string FileName { get; set; } = string.Empty;
}

public sealed class PdfTextToPdfOptions
{
    public double[] PageSize { get; set; } = PdfPageSizes.A4;
    public double Margin { get; set; } = 56;
    public double FontSize { get; set; } = 11;
    public double LineSpacing { get; set; } = 1.35;
    public string Title { get; set; } = string.Empty;
    public bool ShowTitleOnFirstPage { get; set; } = true;
}

public enum PdfImageFit
{
    /// <summary>Giữ tỉ lệ, vừa trong lề trang.</summary>
    Fit,
    /// <summary>Trang có kích thước đúng bằng ảnh.</summary>
    MatchImage
}

public sealed class PdfImageToPdfOptions
{
    public double[] PageSize { get; set; } = PdfPageSizes.A4;
    public double Margin { get; set; } = 28;
    public PdfImageFit Fit { get; set; } = PdfImageFit.Fit;
    public bool AutoOrientation { get; set; } = true;
}

public sealed class PdfTableToPdfOptions
{
    public double[] PageSize { get; set; } = PdfPageSizes.Landscape(PdfPageSizes.A4);
    public double Margin { get; set; } = 36;
    public double FontSize { get; set; } = 9;
    public string Title { get; set; } = string.Empty;
    public bool FirstRowIsHeader { get; set; } = true;
    public bool RepeatHeader { get; set; } = true;
}

/// <summary>Thông tin tổng quan của một file PDF.</summary>
public sealed class PdfDocumentSummary
{
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string Version { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public int ObjectCount { get; set; }
    public bool IsEncrypted { get; set; }
    public bool WasRepaired { get; set; }
    public string? EncryptionDescription { get; set; }
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? Subject { get; set; }
    public string? Keywords { get; set; }
    public string? Creator { get; set; }
    public string? Producer { get; set; }
    public string? CreationDate { get; set; }
    public string? ModifiedDate { get; set; }
    public int ImageCount { get; set; }
    public int FontCount { get; set; }
    public int CharacterCount { get; set; }
    public List<string> FontNames { get; } = new();
    public List<string> PageSizes { get; } = new();
}

public sealed class PdfMetadata
{
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? Subject { get; set; }
    public string? Keywords { get; set; }
    public string? Creator { get; set; }
    public string? Producer { get; set; }
}

/// <summary>Kết quả so sánh hai file PDF.</summary>
public sealed class PdfCompareResult
{
    public int PageCountLeft { get; set; }
    public int PageCountRight { get; set; }
    public int ChangedPages { get; set; }
    public int AddedLines { get; set; }
    public int RemovedLines { get; set; }
    public bool Identical => ChangedPages == 0 && AddedLines == 0 && RemovedLines == 0 && PageCountLeft == PageCountRight;
    public List<string> Report { get; } = new();
}

/// <summary>Kết quả một thao tác tạo ra nhiều file.</summary>
public sealed class PdfOutputFile
{
    public required string FileName { get; init; }
    public required byte[] Data { get; init; }
    public string Description { get; init; } = string.Empty;
}

/// <summary>Chế độ tách file.</summary>
public enum PdfSplitMode
{
    /// <summary>Mỗi trang một file.</summary>
    EveryPage,
    /// <summary>Chia thành các tệp N trang liên tiếp.</summary>
    EveryNPages,
    /// <summary>Mỗi khoảng trang do người dùng nhập là một file.</summary>
    ByRanges
}

public sealed class PdfSplitOptions
{
    public PdfSplitMode Mode { get; set; } = PdfSplitMode.EveryPage;
    public int PagesPerFile { get; set; } = 1;
    /// <summary>Chuỗi khoảng trang, các file cách nhau bằng dấu ';' — ví dụ "1-3; 4-6; 7".</summary>
    public string Ranges { get; set; } = string.Empty;
    public string BaseName { get; set; } = "document";
}

public enum PdfCompressionLevel
{
    /// <summary>Chỉ dọn đối tượng thừa, không đụng tới ảnh.</summary>
    Light,
    /// <summary>Nén lại toàn bộ stream, gộp đối tượng trùng.</summary>
    Balanced,
    /// <summary>Như Balanced và giảm chất lượng ảnh bitmap lớn.</summary>
    Strong
}

public sealed class PdfCompressionResult
{
    public required byte[] Data { get; init; }
    public long OriginalSize { get; init; }
    public long NewSize => Data.LongLength;
    public int RemovedObjects { get; init; }
    public int RecompressedStreams { get; init; }
    public int DownscaledImages { get; init; }

    public double SavedPercent => OriginalSize <= 0 ? 0 : Math.Max(0, 100.0 - NewSize * 100.0 / OriginalSize);
}
