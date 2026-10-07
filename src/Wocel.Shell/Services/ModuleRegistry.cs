using Wocel.Core.Contracts;

namespace Wocel.Shell.Services;

public class ModuleRegistry
{
    private readonly Dictionary<OfficeModuleType, IOfficeModule> _modules = new();

    public void RegisterModule(IOfficeModule module)
    {
        _modules[module.ModuleType] = module;
    }

    public IOfficeModule GetModule(OfficeModuleType type)
    {
        if (_modules.TryGetValue(type, out var module))
            return module;
        throw new KeyNotFoundException($"Module {type} chưa được đăng ký trong hệ thống.");
    }

    public IOfficeModule? FindModuleForExtension(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return _modules.Values.FirstOrDefault(m => 
            m.SupportedImportExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase));
    }

    public IEnumerable<IOfficeModule> GetAllModules() => _modules.Values;
}
