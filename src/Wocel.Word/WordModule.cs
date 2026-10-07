using Wocel.Core.Contracts;
using Wocel.Core.Services;
using Wocel.Word.Sessions;

namespace Wocel.Word;

public class WordModule : IOfficeModule
{
    public OfficeModuleType ModuleType => OfficeModuleType.Word;
    public string DisplayName => "Wocel Document (Word & PDF)";

    public IReadOnlyList<string> SupportedImportExtensions { get; } = new[]
    {
        ".docx",
        ".rtf",
        ".txt",
        ".pdf"
    };

    public IReadOnlyList<string> SupportedExportExtensions { get; } = new[]
    {
        ".docx",
        ".rtf",
        ".txt",
        ".pdf"
    };

    public IOfficeDocumentSession CreateNewSession(string? initialTitle = null)
    {
        return new WordDocumentSession(initialTitle ?? "New Document");
    }

    public async Task<IOfficeDocumentSession> OpenSessionAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Không tìm thấy file: {filePath}", filePath);

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        
        if (ext == ".pdf")
        {
            var pdfSession = new PdfDocumentSession(Path.GetFileName(filePath))
            {
                FilePath = filePath
            };
            using var fileStream = File.OpenRead(filePath);
            await pdfSession.LoadAsync(fileStream, ext);
            return pdfSession;
        }

        var session = new WordDocumentSession(Path.GetFileNameWithoutExtension(filePath))
        {
            FilePath = filePath
        };

        using var fStream = File.OpenRead(filePath);
        await session.LoadAsync(fStream, ext);
        return session;
    }
}
