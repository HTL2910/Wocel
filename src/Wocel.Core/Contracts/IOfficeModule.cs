namespace Wocel.Core.Contracts;

public enum OfficeModuleType
{
    Word,
    Excel
}

public interface IOfficeDocumentSession : IDisposable
{
    string Id { get; }
    string Title { get; set; }
    string? FilePath { get; set; }
    bool IsDirty { get; }
    OfficeModuleType ModuleType { get; }

    Task LoadAsync(Stream stream, string fileExtension);
    Task SaveAsync(Stream stream, string fileExtension);
    Task ExportAsync(Stream destinationStream, string targetExtension);
    void MarkDirty();
    void ClearDirty();
}

public interface IOfficeModule
{
    OfficeModuleType ModuleType { get; }
    string DisplayName { get; }
    IReadOnlyList<string> SupportedImportExtensions { get; }
    IReadOnlyList<string> SupportedExportExtensions { get; }

    IOfficeDocumentSession CreateNewSession(string? initialTitle = null);
    Task<IOfficeDocumentSession> OpenSessionAsync(string filePath);
}
