using System.Text.Json;

namespace Wocel.Shell.Services;

public class AppSettings
{
    public string Theme { get; set; } = "Light";
    public string DefaultFontFamily { get; set; } = "Segoe UI";
    public int DefaultFontSize { get; set; } = 14;
    public bool AutoSaveEnabled { get; set; } = true;
    public int AutoSaveIntervalMinutes { get; set; } = 2;
    public bool ShowWelcomeScreenOnStartup { get; set; } = true;
    public int MaxRecentFiles { get; set; } = 20;
    public bool EnableSpellCheck { get; set; } = true;
    public string DefaultSaveLocation { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public WindowState WindowState { get; set; } = new();
}

public class WindowState
{
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 800;
    public double X { get; set; } = 100;
    public double Y { get; set; } = 100;
    public bool IsMaximized { get; set; } = false;
}

public class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Wocel",
        "settings.json");

    private AppSettings _settings = new();
    private readonly object _lock = new();

    public AppSettings Settings
    {
        get
        {
            lock (_lock)
            {
                return _settings;
            }
        }
    }

    public SettingsService()
    {
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    lock (_lock)
                    {
                        _settings = loaded;
                    }
                }
            }
        }
        catch
        {
            // Use default settings on error
            lock (_lock)
            {
                _settings = new AppSettings();
            }
        }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            lock (_lock)
            {
                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(SettingsPath, json);
            }
        }
        catch
        {
            // Ignore save errors
        }
    }

    public void UpdateTheme(string theme)
    {
        lock (_lock)
        {
            _settings.Theme = theme;
        }
        Save();
    }

    public void UpdateFontSettings(string fontFamily, int fontSize)
    {
        lock (_lock)
        {
            _settings.DefaultFontFamily = fontFamily;
            _settings.DefaultFontSize = fontSize;
        }
        Save();
    }

    public void UpdateAutoSaveSettings(bool enabled, int intervalMinutes)
    {
        lock (_lock)
        {
            _settings.AutoSaveEnabled = enabled;
            _settings.AutoSaveIntervalMinutes = intervalMinutes;
        }
        Save();
    }

    public void UpdateWindowState(double width, double height, double x, double y, bool isMaximized)
    {
        lock (_lock)
        {
            _settings.WindowState.Width = width;
            _settings.WindowState.Height = height;
            _settings.WindowState.X = x;
            _settings.WindowState.Y = y;
            _settings.WindowState.IsMaximized = isMaximized;
        }
        Save();
    }

    public void ResetToDefaults()
    {
        lock (_lock)
        {
            _settings = new AppSettings();
        }
        Save();
    }

    public void ExportSettings(string exportPath)
    {
        try
        {
            lock (_lock)
            {
                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(exportPath, json);
            }
        }
        catch
        {
            throw new InvalidOperationException("Không thể xuất settings");
        }
    }

    public void ImportSettings(string importPath)
    {
        try
        {
            if (!File.Exists(importPath))
            {
                throw new FileNotFoundException("File settings không tồn tại", importPath);
            }

            var json = File.ReadAllText(importPath);
            var imported = JsonSerializer.Deserialize<AppSettings>(json);
            
            if (imported != null)
            {
                lock (_lock)
                {
                    _settings = imported;
                }
                Save();
            }
        }
        catch
        {
            throw new InvalidOperationException("Không thể nhập settings");
        }
    }
}
