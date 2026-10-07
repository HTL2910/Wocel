using Wocel.Core.Contracts;

namespace Wocel.Shell.ViewModels;

public class TabItemViewModel
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; }
    public OfficeModuleType ModuleType { get; }
    public IOfficeDocumentSession Session { get; }
    public string? FilePath => Session.FilePath;
    public bool IsDirty => Session.IsDirty;

    public TabItemViewModel(IOfficeDocumentSession session, string title, OfficeModuleType moduleType)
    {
        Session = session;
        Title = title;
        ModuleType = moduleType;
    }
}
