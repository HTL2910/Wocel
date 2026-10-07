using System.Text.Json;
using Wocel.Core.Contracts;
using Wocel.Core.Models;
using Wocel.Excel.Sessions;
using Wocel.Word.Sessions;

namespace Wocel.Shell.Services;

public class DocumentTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public OfficeModuleType ModuleType { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public string ThumbnailPath { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
}

public class TemplateService
{
    private static readonly string TemplatesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Wocel",
        "Templates");

    private readonly List<DocumentTemplate> _templates = new();

    public TemplateService()
    {
        EnsureTemplatesDirectory();
        LoadTemplates();
        CreateDefaultTemplates();
    }

    public IReadOnlyList<DocumentTemplate> GetTemplates(OfficeModuleType? filterType = null)
    {
        if (filterType.HasValue)
        {
            return _templates.Where(t => t.ModuleType == filterType.Value).ToList();
        }
        return _templates.ToList();
    }

    public DocumentTemplate? GetTemplateById(string id)
    {
        return _templates.FirstOrDefault(t => t.Id == id);
    }

    public void AddTemplate(DocumentTemplate template)
    {
        _templates.Add(template);
        SaveTemplates();
    }

    public void DeleteTemplate(string id)
    {
        var template = _templates.FirstOrDefault(t => t.Id == id);
        if (template != null)
        {
            _templates.Remove(template);
            
            // Delete template file
            var templateFile = Path.Combine(TemplatesPath, $"{id}.template");
            if (File.Exists(templateFile))
            {
                File.Delete(templateFile);
            }
            
            SaveTemplates();
        }
    }

    public async Task<IOfficeDocumentSession?> CreateFromTemplateAsync(string templateId, IOfficeModule module)
    {
        var template = GetTemplateById(templateId);
        if (template == null) return null;

        var templateFile = Path.Combine(TemplatesPath, $"{templateId}.template");
        if (!File.Exists(templateFile)) return null;

        try
        {
            using var stream = File.OpenRead(templateFile);
            var extension = template.ModuleType == OfficeModuleType.Word ? ".docx" : ".xlsx";
            var session = await module.OpenSessionAsync(templateFile);
            
            // Clear file path since it's a template
            session.FilePath = null;
            session.ClearDirty();
            
            return session;
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveAsTemplateAsync(IOfficeDocumentSession session, string name, string description, string category = "General")
    {
        var templateId = Guid.NewGuid().ToString("N");
        var templateFile = Path.Combine(TemplatesPath, $"{templateId}.template");
        
        var template = new DocumentTemplate
        {
            Id = templateId,
            Name = name,
            Description = description,
            ModuleType = session.ModuleType,
            Category = category
        };

        try
        {
            var extension = session.ModuleType == OfficeModuleType.Word ? ".docx" : ".xlsx";
            using var stream = File.Create(templateFile);
            await session.SaveAsync(stream, extension);
            
            AddTemplate(template);
        }
        catch
        {
            throw new InvalidOperationException("Không thể lưu template");
        }
    }

    private void LoadTemplates()
    {
        try
        {
            var metadataFile = Path.Combine(TemplatesPath, "templates.json");
            if (File.Exists(metadataFile))
            {
                var json = File.ReadAllText(metadataFile);
                var loaded = JsonSerializer.Deserialize<List<DocumentTemplate>>(json);
                if (loaded != null)
                {
                    _templates.Clear();
                    _templates.AddRange(loaded);
                }
            }
        }
        catch
        {
            _templates.Clear();
        }
    }

    private void SaveTemplates()
    {
        try
        {
            var metadataFile = Path.Combine(TemplatesPath, "templates.json");
            var json = JsonSerializer.Serialize(_templates, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(metadataFile, json);
        }
        catch
        {
            // Ignore save errors
        }
    }

    private void CreateDefaultTemplates()
    {
        // Only create if no templates exist
        if (_templates.Count > 0) return;

        var defaultTemplates = new List<DocumentTemplate>
        {
            new DocumentTemplate
            {
                Name = "Blank Document",
                Description = "Tài liệu trống",
                ModuleType = OfficeModuleType.Word,
                Category = "Basic"
            },
            new DocumentTemplate
            {
                Name = "Blank Spreadsheet",
                Description = "Bảng tính trống",
                ModuleType = OfficeModuleType.Excel,
                Category = "Basic"
            },
            new DocumentTemplate
            {
                Name = "Report Template",
                Description = "Mẫu báo cáo cơ bản",
                ModuleType = OfficeModuleType.Word,
                Category = "Business"
            },
            new DocumentTemplate
            {
                Name = "Invoice Template",
                Description = "Mẫu hóa đơn",
                ModuleType = OfficeModuleType.Excel,
                Category = "Business"
            }
        };

        _templates.AddRange(defaultTemplates);
        SaveTemplates();
    }

    private void EnsureTemplatesDirectory()
    {
        try
        {
            if (!Directory.Exists(TemplatesPath))
            {
                Directory.CreateDirectory(TemplatesPath);
            }
        }
        catch
        {
            // Ignore directory creation errors
        }
    }

    public List<string> GetCategories()
    {
        return _templates.Select(t => t.Category).Distinct().OrderBy(c => c).ToList();
    }
}
