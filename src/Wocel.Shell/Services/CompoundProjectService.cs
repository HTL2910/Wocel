using Wocel.Core.Models;
using Wocel.Excel.Sessions;
using Wocel.Word.Sessions;

namespace Wocel.Shell.Services;

public class CompoundProjectService
{
    public static async Task SaveCompoundProjectAsync(string filePath, WordDocumentSession? wordSession, ExcelDocumentSession? excelSession)
    {
        var bundle = new WocelBundle
        {
            Name = Path.GetFileNameWithoutExtension(filePath)
        };

        if (wordSession != null)
        {
            bundle.Documents.Add(wordSession.Document);
        }

        if (excelSession != null)
        {
            bundle.Spreadsheets.Add(excelSession.Document);
        }

        using var fileStream = File.Create(filePath);
        await WocelBundle.SaveToZipAsync(bundle, fileStream);
    }

    public static async Task<(WordDocumentSession? wordSession, ExcelDocumentSession? excelSession)> LoadCompoundProjectAsync(string filePath)
    {
        using var fileStream = File.OpenRead(filePath);
        var bundle = await WocelBundle.LoadFromZipAsync(fileStream);

        WordDocumentSession? wordSession = null;
        ExcelDocumentSession? excelSession = null;

        if (bundle.Documents.Count > 0)
        {
            wordSession = new WordDocumentSession(bundle.Documents[0].Title)
            {
                Document = bundle.Documents[0],
                FilePath = filePath
            };
        }

        if (bundle.Spreadsheets.Count > 0)
        {
            excelSession = new ExcelDocumentSession(bundle.Spreadsheets[0].Title)
            {
                Document = bundle.Spreadsheets[0],
                FilePath = filePath
            };
        }

        return (wordSession, excelSession);
    }
}
