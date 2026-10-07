using Wocel.Core.Contracts;
using Wocel.Core.Models;
using Wocel.Word.IO;

namespace Wocel.Word.Sessions;

public class WordDocumentSession : IOfficeDocumentSession
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Untitled Document";
    public string? FilePath { get; set; }
    public bool IsDirty { get; private set; }
    public OfficeModuleType ModuleType => OfficeModuleType.Word;

    public DocumentDocument Document { get; set; } = new();

    public WordDocumentSession(string? title = null)
    {
        if (!string.IsNullOrEmpty(title))
        {
            Title = title;
            Document.Title = title;
        }
    }

    public void MarkDirty() => IsDirty = true;
    public void ClearDirty() => IsDirty = false;

    public async Task LoadAsync(Stream stream, string fileExtension)
    {
        var ext = fileExtension.ToLowerInvariant().TrimStart('.');
        Document = ext switch
        {
            "docx" => DocxReader.Read(stream),
            "rtf" => RtfHandler.ReadRtf(stream),
            "txt" => LoadPlainText(stream),
            _ => throw new NotSupportedException($"Định dạng '.{ext}' không được hỗ trợ để mở.")
        };
        ClearDirty();
        await Task.CompletedTask;
    }

    public async Task SaveAsync(Stream stream, string fileExtension)
    {
        var ext = fileExtension.ToLowerInvariant().TrimStart('.');
        switch (ext)
        {
            case "docx":
                DocxWriter.Write(Document, stream);
                break;
            case "rtf":
                RtfHandler.WriteRtf(Document, stream);
                break;
            case "txt":
                SavePlainText(stream);
                break;
            default:
                throw new NotSupportedException($"Định dạng '.{ext}' không được hỗ trợ để lưu.");
        }
        ClearDirty();
        await Task.CompletedTask;
    }

    public async Task ExportAsync(Stream destinationStream, string targetExtension)
    {
        var ext = targetExtension.ToLowerInvariant().TrimStart('.');
        switch (ext)
        {
            case "pdf":
                PdfExporter.ExportToPdf(Document, destinationStream);
                break;
            case "docx":
                DocxWriter.Write(Document, destinationStream);
                break;
            case "rtf":
                RtfHandler.WriteRtf(Document, destinationStream);
                break;
            case "txt":
                SavePlainText(destinationStream);
                break;
            default:
                throw new NotSupportedException($"Không thể xuất sang định dạng '.{ext}'.");
        }
        await Task.CompletedTask;
    }

    private DocumentDocument LoadPlainText(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        var doc = new DocumentDocument();
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            var block = new DocBlock { Type = BlockType.Paragraph };
            block.Inlines.Add(new TextRun { Text = line });
            doc.Blocks.Add(block);
        }
        return doc;
    }

    private void SavePlainText(Stream stream)
    {
        using var writer = new StreamWriter(stream, leaveOpen: true);
        writer.Write(Document.ToPlainText());
    }

    public void Dispose()
    {
        // Cleanup resources
    }
}
