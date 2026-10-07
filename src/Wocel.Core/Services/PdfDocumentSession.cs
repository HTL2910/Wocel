using Wocel.Core.Contracts;
using Wocel.Core.Pdf;

namespace Wocel.Core.Services;

/// <summary>
/// Phiên làm việc với tệp PDF: đọc bằng bộ phân tích thật của Wocel nên lấy được
/// văn bản đúng theo từng trang (kể cả tiếng Việt) thay vì dò chuỗi thô.
/// </summary>
public class PdfDocumentSession : IOfficeDocumentSession
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Document.pdf";
    public string? FilePath { get; set; }
    public bool IsDirty => false;
    public OfficeModuleType ModuleType => OfficeModuleType.Word; // dùng chung khung xem của Word

    public string ExtractedText { get; private set; } = string.Empty;
    public List<string> Pages { get; private set; } = new();
    public int CurrentPage { get; set; } = 1;
    public int TotalPages => Math.Max(1, Pages.Count);

    /// <summary>Byte gốc của tệp — để chuyển thẳng sang bộ công cụ PDF mà không phải đọc lại đĩa.</summary>
    public byte[] SourceBytes { get; private set; } = Array.Empty<byte>();

    public bool IsEncrypted { get; private set; }
    public bool WasRepaired { get; private set; }

    public PdfDocumentSession(string? title = null)
    {
        if (!string.IsNullOrEmpty(title)) Title = title;
    }

    public async Task LoadAsync(Stream stream, string fileExtension)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        SourceBytes = buffer.ToArray();

        Pages = new List<string>();

        try
        {
            var document = PdfDocument.Load(SourceBytes);
            IsEncrypted = document.IsEncrypted;
            WasRepaired = document.Status == PdfLoadStatus.Repaired;

            if (document.Status is PdfLoadStatus.EncryptedNeedsPassword or PdfLoadStatus.EncryptedUnsupported)
            {
                Pages.Add("🔒 Tệp PDF này được đặt mật khẩu.\n\nHãy dùng công cụ “Mở khoá PDF” và nhập mật khẩu để xem nội dung.");
                ExtractedText = string.Empty;
                CurrentPage = 1;
                return;
            }

            foreach (var page in document.Pages)
            {
                var text = PdfTextExtractor.ExtractPageText(document, page);
                Pages.Add(text.Length > 0
                    ? text
                    : "(Trang này không có văn bản — có thể là ảnh chụp/scan.)");
            }

            ExtractedText = string.Join("\n\f\n", Pages);
        }
        catch (Exception ex)
        {
            Pages.Add($"Không đọc được nội dung PDF: {ex.Message}");
            ExtractedText = string.Empty;
        }

        if (Pages.Count == 0)
            Pages.Add("Tài liệu PDF không có trang nào đọc được.");

        CurrentPage = 1;
    }

    public Task SaveAsync(Stream stream, string fileExtension) =>
        SourceBytes.Length > 0 ? stream.WriteAsync(SourceBytes, 0, SourceBytes.Length) : Task.CompletedTask;

    public async Task ExportAsync(Stream destinationStream, string targetExtension)
    {
        var extension = targetExtension.TrimStart('.').ToLowerInvariant();

        switch (extension)
        {
            case "txt":
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(ExtractedText);
                await destinationStream.WriteAsync(bytes);
                break;
            }
            case "csv":
            {
                var rows = PdfToolkit.ExtractTables(SourceBytes);
                var bytes = System.Text.Encoding.UTF8.GetBytes(PdfToolkit.ToCsv(rows));
                await destinationStream.WriteAsync(bytes);
                break;
            }
            default:
                if (SourceBytes.Length > 0) await destinationStream.WriteAsync(SourceBytes);
                break;
        }
    }

    public void MarkDirty() { }
    public void ClearDirty() { }
    public void Dispose() { }
}
