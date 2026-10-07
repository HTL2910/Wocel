using System.Text.Json;
using Wocel.Core.Contracts;

namespace Wocel.Shell.Services;

public class AutoSaveService
{
    private static readonly string AutoSavePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Wocel",
        "AutoSave");

    private readonly Dictionary<string, Timer> _autoSaveTimers = new();
    private readonly TimeSpan _autoSaveInterval = TimeSpan.FromMinutes(2); // Auto-save every 2 minutes

    public AutoSaveService()
    {
        EnsureAutoSaveDirectory();
    }

    public void EnableAutoSave(IOfficeDocumentSession session, string sessionId)
    {
        if (_autoSaveTimers.ContainsKey(sessionId))
        {
            _autoSaveTimers[sessionId].Dispose();
        }

        var timer = new Timer(_ => AutoSaveSession(session, sessionId), null, 
            _autoSaveInterval, _autoSaveInterval);
        
        _autoSaveTimers[sessionId] = timer;
    }

    public void DisableAutoSave(string sessionId)
    {
        if (_autoSaveTimers.TryGetValue(sessionId, out var timer))
        {
            timer.Dispose();
            _autoSaveTimers.Remove(sessionId);
        }
    }

    public void DisableAllAutoSaves()
    {
        foreach (var timer in _autoSaveTimers.Values)
        {
            timer.Dispose();
        }
        _autoSaveTimers.Clear();
    }

    private async void AutoSaveSession(IOfficeDocumentSession session, string sessionId)
    {
        try
        {
            if (!session.IsDirty) return;

            var autoSaveFile = Path.Combine(AutoSavePath, $"{sessionId}.autosave");
            var extension = session.ModuleType == OfficeModuleType.Word ? ".docx" : ".xlsx";
            
            using var stream = File.Create(autoSaveFile);
            await session.SaveAsync(stream, extension);

            // Save metadata
            var metadata = new AutoSaveMetadata
            {
                SessionId = sessionId,
                OriginalFilePath = session.FilePath,
                Title = session.Title,
                ModuleType = session.ModuleType,
                LastAutoSaveTime = DateTime.Now,
                AutoSaveFilePath = autoSaveFile
            };

            var metadataFile = Path.Combine(AutoSavePath, $"{sessionId}.metadata");
            var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(metadataFile, metadataJson);
        }
        catch
        {
            // Ignore auto-save errors
        }
    }

    public List<AutoSaveMetadata> GetAvailableAutoSaves()
    {
        var autoSaves = new List<AutoSaveMetadata>();
        
        try
        {
            if (!Directory.Exists(AutoSavePath)) return autoSaves;

            var metadataFiles = Directory.GetFiles(AutoSavePath, "*.metadata");
            foreach (var metadataFile in metadataFiles)
            {
                try
                {
                    var json = File.ReadAllText(metadataFile);
                    var metadata = JsonSerializer.Deserialize<AutoSaveMetadata>(json);
                    if (metadata != null)
                    {
                        autoSaves.Add(metadata);
                    }
                }
                catch
                {
                    // Skip corrupted metadata files
                }
            }
        }
        catch
        {
            // Return empty list on error
        }

        return autoSaves.OrderByDescending(a => a.LastAutoSaveTime).ToList();
    }

    public async Task<IOfficeDocumentSession?> RestoreFromAutoSave(AutoSaveMetadata metadata, IOfficeModule module)
    {
        try
        {
            if (!File.Exists(metadata.AutoSaveFilePath)) return null;

            using var stream = File.OpenRead(metadata.AutoSaveFilePath);
            var extension = metadata.ModuleType == OfficeModuleType.Word ? ".docx" : ".xlsx";
            var session = await module.OpenSessionAsync(metadata.AutoSaveFilePath);
            
            // Update session metadata
            session.FilePath = metadata.OriginalFilePath;
            session.Title = metadata.Title + " (Recovered)";

            return session;
        }
        catch
        {
            return null;
        }
    }

    public void DeleteAutoSave(string sessionId)
    {
        try
        {
            var autoSaveFile = Path.Combine(AutoSavePath, $"{sessionId}.autosave");
            var metadataFile = Path.Combine(AutoSavePath, $"{sessionId}.metadata");

            if (File.Exists(autoSaveFile)) File.Delete(autoSaveFile);
            if (File.Exists(metadataFile)) File.Delete(metadataFile);

            DisableAutoSave(sessionId);
        }
        catch
        {
            // Ignore deletion errors
        }
    }

    public void CleanOldAutoSaves(TimeSpan maxAge)
    {
        try
        {
            if (!Directory.Exists(AutoSavePath)) return;

            var cutoffTime = DateTime.Now - maxAge;
            var autoSaves = GetAvailableAutoSaves();

            foreach (var autoSave in autoSaves)
            {
                if (autoSave.LastAutoSaveTime < cutoffTime)
                {
                    DeleteAutoSave(autoSave.SessionId);
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private void EnsureAutoSaveDirectory()
    {
        try
        {
            if (!Directory.Exists(AutoSavePath))
            {
                Directory.CreateDirectory(AutoSavePath);
            }
        }
        catch
        {
            // Ignore directory creation errors
        }
    }
}

public class AutoSaveMetadata
{
    public string SessionId { get; set; } = string.Empty;
    public string? OriginalFilePath { get; set; }
    public string Title { get; set; } = string.Empty;
    public OfficeModuleType ModuleType { get; set; }
    public DateTime LastAutoSaveTime { get; set; }
    public string AutoSaveFilePath { get; set; } = string.Empty;

    public string FormattedTime => LastAutoSaveTime.ToString("dd/MM/yyyy HH:mm");
    public string TimeAgo => GetTimeAgoString(LastAutoSaveTime);

    private static string GetTimeAgoString(DateTime dateTime)
    {
        var span = DateTime.Now - dateTime;
        
        if (span.TotalMinutes < 1) return "Vừa xong";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} phút trước";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} giờ trước";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays} ngày trước";
        
        return dateTime.ToString("dd/MM/yyyy");
    }
}
