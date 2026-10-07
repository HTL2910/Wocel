using System.Text.Json;
using Wocel.Core.Contracts;

namespace Wocel.Shell.Services;

public class RecentFileItem
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public OfficeModuleType ModuleType { get; set; }
    public DateTime LastOpened { get; set; } = DateTime.Now;

    public string FormattedDate => LastOpened.ToString("dd/MM/yyyy HH:mm");
    public string TypeBadge => ModuleType == OfficeModuleType.Word ? "DOCX" : "XLSX";
}

public class RecentFilesService
{
    private static readonly string StoragePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
        "Wocel", 
        "recent_files.json");

    private List<RecentFileItem> _items = new();

    public RecentFilesService()
    {
        Load();
    }

    public IReadOnlyList<RecentFileItem> GetRecentFiles(OfficeModuleType? filterType = null)
    {
        if (filterType.HasValue)
        {
            return _items.Where(i => i.ModuleType == filterType.Value).OrderByDescending(i => i.LastOpened).ToList();
        }
        return _items.OrderByDescending(i => i.LastOpened).ToList();
    }

    public void AddRecentFile(string filePath, OfficeModuleType moduleType)
    {
        try
        {
            _items.RemoveAll(i => string.Equals(i.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            
            _items.Insert(0, new RecentFileItem
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                ModuleType = moduleType,
                LastOpened = DateTime.Now
            });

            if (_items.Count > 20)
            {
                _items = _items.Take(20).ToList();
            }

            Save();
        }
        catch
        {
            // Ignore file save errors in sandbox
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(StoragePath))
            {
                var json = File.ReadAllText(StoragePath);
                _items = JsonSerializer.Deserialize<List<RecentFileItem>>(json) ?? new();
            }
        }
        catch
        {
            _items = new();
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(StoragePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var json = JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StoragePath, json);
        }
        catch
        {
            // Ignore file save errors in sandbox
        }
    }
}
