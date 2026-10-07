using Wocel.Core.Contracts;
using Wocel.Excel.Sessions;

namespace Wocel.Excel;

public class ExcelModule : IOfficeModule
{
    public OfficeModuleType ModuleType => OfficeModuleType.Excel;
    public string DisplayName => "Wocel Spreadsheet (Excel)";

    public IReadOnlyList<string> SupportedImportExtensions { get; } = new[]
    {
        ".xlsx",
        ".csv"
    };

    public IReadOnlyList<string> SupportedExportExtensions { get; } = new[]
    {
        ".xlsx",
        ".csv"
    };

    public IOfficeDocumentSession CreateNewSession(string? initialTitle = null)
    {
        return new ExcelDocumentSession(initialTitle ?? "New Spreadsheet");
    }

    public async Task<IOfficeDocumentSession> OpenSessionAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Không tìm thấy file: {filePath}", filePath);

        var ext = Path.GetExtension(filePath);
        var session = new ExcelDocumentSession(Path.GetFileNameWithoutExtension(filePath))
        {
            FilePath = filePath
        };

        using var fileStream = File.OpenRead(filePath);
        await session.LoadAsync(fileStream, ext);
        return session;
    }
}
