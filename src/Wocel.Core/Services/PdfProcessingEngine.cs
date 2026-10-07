using Wocel.Core.Pdf;

namespace Wocel.Core.Services;

/// <summary>
/// Lớp bọc tương thích cho mã cũ. Toàn bộ phần xử lý thật nằm ở <see cref="PdfToolkit"/>,
/// chạy trên bộ phân tích PDF đầy đủ của Wocel (không phụ thuộc thư viện ngoài).
/// </summary>
public static class PdfProcessingEngine
{
    public static Task<byte[]> CompressAsync(byte[] pdfBytes)
        => Task.Run(() => PdfToolkit.Compress(pdfBytes).Data);

    public static Task<byte[]> MergeAsync(IEnumerable<byte[]> pdfFiles)
        => Task.Run(() => PdfToolkit.Merge(pdfFiles));

    public static Task<List<byte[]>> SplitAsync(byte[] pdfBytes, int? startPage = null, int? endPage = null)
        => Task.Run(() =>
        {
            var document = PdfToolkit.Open(pdfBytes);
            int first = Math.Max(1, startPage ?? 1);
            int last = Math.Min(document.PageCount, endPage ?? document.PageCount);

            var results = new List<byte[]>();
            for (int page = first; page <= last; page++)
                results.Add(PdfToolkit.ExtractPages(pdfBytes, page.ToString()));

            return results;
        });

    public static Task<byte[]> RotateAsync(byte[] pdfBytes, int degrees)
        => Task.Run(() => PdfToolkit.RotatePages(pdfBytes, degrees));

    public static Task<byte[]> RemovePagesAsync(byte[] pdfBytes, IEnumerable<int> pagesToRemove)
        => Task.Run(() => PdfToolkit.RemovePages(pdfBytes, string.Join(",", pagesToRemove)));

    public static Task<byte[]> AddWatermarkAsync(byte[] pdfBytes, string watermarkText, float opacity = 0.3f)
        => Task.Run(() => PdfToolkit.AddWatermark(pdfBytes, new PdfWatermarkOptions
        {
            Text = watermarkText,
            Opacity = opacity
        }));

    public static Task<string> ExtractTextAsync(byte[] pdfBytes)
        => Task.Run(() => PdfToolkit.ExtractText(pdfBytes));

    public static Task<byte[]> PlainTextToPdfAsync(string text, string title = "Document")
        => Task.Run(() => PdfToolkit.TextToPdf(text, new PdfTextToPdfOptions { Title = title }));

    public static Task<List<List<string>>> ExtractTablesAsync(byte[] pdfBytes)
        => Task.Run(() => PdfToolkit.ExtractTables(pdfBytes));
}
