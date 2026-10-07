using Wocel.Core.Contracts;
using Wocel.Core.Models;
using Wocel.Excel.Engine;
using Wocel.Excel.IO;

namespace Wocel.Excel.Sessions;

public class ExcelDocumentSession : IOfficeDocumentSession
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Untitled Sheet";
    public string? FilePath { get; set; }
    public bool IsDirty { get; private set; }
    public OfficeModuleType ModuleType => OfficeModuleType.Excel;

    public SpreadsheetDocument Document { get; set; } = new();
    public FormulaEngine FormulaEngine { get; private set; }

    public ExcelDocumentSession(string? title = null)
    {
        if (!string.IsNullOrEmpty(title))
        {
            Title = title;
            Document.Title = title;
        }
        FormulaEngine = new FormulaEngine(Document.GetOrCreateActiveSheet());
    }

    public void MarkDirty() => IsDirty = true;
    public void ClearDirty() => IsDirty = false;

    public void Recalculate()
    {
        FormulaEngine = new FormulaEngine(Document.GetOrCreateActiveSheet());
        FormulaEngine.RecalculateAll();
    }

    public async Task LoadAsync(Stream stream, string fileExtension)
    {
        var ext = fileExtension.ToLowerInvariant().TrimStart('.');
        Document = ext switch
        {
            "xlsx" => XlsxReader.Read(stream),
            "csv" => CsvHandler.ReadCsv(stream),
            _ => throw new NotSupportedException($"Định dạng '.{ext}' không được hỗ trợ để mở.")
        };
        FormulaEngine = new FormulaEngine(Document.GetOrCreateActiveSheet());
        FormulaEngine.RecalculateAll();
        ClearDirty();
        await Task.CompletedTask;
    }

    public async Task SaveAsync(Stream stream, string fileExtension)
    {
        Recalculate();
        var ext = fileExtension.ToLowerInvariant().TrimStart('.');
        switch (ext)
        {
            case "xlsx":
                XlsxWriter.Write(Document, stream);
                break;
            case "csv":
                CsvHandler.WriteCsv(Document, stream);
                break;
            default:
                throw new NotSupportedException($"Định dạng '.{ext}' không được hỗ trợ để lưu.");
        }
        ClearDirty();
        await Task.CompletedTask;
    }

    public async Task ExportAsync(Stream destinationStream, string targetExtension)
    {
        Recalculate();
        var ext = targetExtension.ToLowerInvariant().TrimStart('.');
        switch (ext)
        {
            case "xlsx":
                XlsxWriter.Write(Document, destinationStream);
                break;
            case "csv":
                CsvHandler.WriteCsv(Document, destinationStream);
                break;
            default:
                throw new NotSupportedException($"Không thể xuất sang định dạng '.{ext}'.");
        }
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        // Cleanup resources
    }
}
