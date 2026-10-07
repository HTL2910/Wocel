using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Wocel.Core.Services;

namespace Wocel.Core.Diagnostics;

/// <summary>
/// Nhật ký hoạt động của ứng dụng: ghi ngay xuống tệp cục bộ (luôn hoạt động, kể cả
/// khi mất mạng) rồi đẩy dần lên bảng Postgres trên Supabase để truy vết khi có lỗi.
///
/// Nguyên tắc: chỉ ghi thông tin về *thao tác* (công cụ nào, tệp bao nhiêu byte, mất
/// bao lâu, lỗi gì). Không bao giờ ghi nội dung tài liệu của người dùng.
/// </summary>
public static class ActivityLog
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private static string _logDirectory = string.Empty;
    private static string _pendingPath = string.Empty;
    private static Timer? _uploadTimer;
    private static bool _uploading;
    private static bool _started;

    public static string SessionId { get; private set; } = Guid.NewGuid().ToString("N");
    public static string AppVersion { get; private set; } = "1.0.0";
    public static string EnvironmentName { get; private set; } = "Development";
    public static bool RemoteEnabled { get; private set; }
    public static string? LastRemoteError { get; private set; }
    public static DateTime? LastUploadAt { get; private set; }
    public static int UploadedCount { get; private set; }

    /// <summary>Thư mục chứa các tệp nhật ký cục bộ.</summary>
    public static string LogDirectory => _logDirectory;

    /// <summary>Tên bảng trên Supabase (có thể đổi bằng biến môi trường WOCEL_LOG_TABLE).</summary>
    public static string TableName { get; private set; } = "wocel_activity_logs";

    // ─────────────────────────────────────────────────────────────────────
    //  KHỞI TẠO
    // ─────────────────────────────────────────────────────────────────────
    public static void Start(string appVersion)
    {
        if (_started) return;
        _started = true;

        AppVersion = appVersion;
        EnvLoader.Load();
        EnvironmentName = EnvLoader.Get("WOCEL_ENV", "Development");
        TableName = EnvLoader.Get("WOCEL_LOG_TABLE", "wocel_activity_logs");

        _logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Wocel", "logs");
        Directory.CreateDirectory(_logDirectory);
        _pendingPath = Path.Combine(_logDirectory, "pending.ndjson");

        var url = EnvLoader.Get("SUPABASE_URL");
        var key = EnvLoader.Get("SUPABASE_ANON_KEY");
        RemoteEnabled = EnvLoader.GetBool("WOCEL_ENABLE_ACTIVITY_LOG", true)
                        && url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        && key.Length > 20
                        && !key.Contains("your_supabase", StringComparison.OrdinalIgnoreCase);

        if (RemoteEnabled)
        {
            Http.DefaultRequestHeaders.TryAddWithoutValidation("apikey", key);
            Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
            Http.DefaultRequestHeaders.TryAddWithoutValidation("Prefer", "return=minimal");

            int intervalSeconds = Math.Max(10, int.TryParse(EnvLoader.Get("WOCEL_LOG_FLUSH_SECONDS", "20"), out int v) ? v : 20);
            _uploadTimer = new Timer(_ => _ = FlushAsync(), null,
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(intervalSeconds));
        }

        Info("app", "start", message: $"Wocel {appVersion} khởi động ({RuntimeInformationText()})");
    }

    /// <summary>Ghi nốt phần còn lại rồi dừng — gọi khi thoát ứng dụng.</summary>
    public static void Stop()
    {
        if (!_started) return;

        Info("app", "stop", message: "Ứng dụng đóng");
        _uploadTimer?.Dispose();
        _uploadTimer = null;

        try { FlushAsync().Wait(TimeSpan.FromSeconds(8)); }
        catch { /* thoát ứng dụng không được vì log mà chậm */ }
    }

    private static string RuntimeInformationText() =>
        $"{Environment.OSVersion.VersionString} · .NET {Environment.Version}";

    // ─────────────────────────────────────────────────────────────────────
    //  GHI NHẬT KÝ
    // ─────────────────────────────────────────────────────────────────────
    public static void Write(ActivityLogEntry entry)
    {
        entry.SessionId = SessionId;
        entry.AppVersion = AppVersion;
        entry.Environment = EnvironmentName;
        entry.Device = SafeDeviceName();
        entry.Os = OperatingSystemName();

        try
        {
            lock (Gate)
            {
                if (_logDirectory.Length == 0)
                {
                    _logDirectory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Wocel", "logs");
                    Directory.CreateDirectory(_logDirectory);
                    _pendingPath = Path.Combine(_logDirectory, "pending.ndjson");
                }

                var line = JsonSerializer.Serialize(entry, Json);
                File.AppendAllText(DailyLogPath(), line + Environment.NewLine, Encoding.UTF8);
                if (RemoteEnabled) File.AppendAllText(_pendingPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Không bao giờ để việc ghi log làm hỏng thao tác của người dùng.
        }
    }

    public static void Info(string category, string action, string? toolId = null, string? message = null,
        string? fileName = null, long? fileSize = null, long durationMs = 0)
        => Write(new ActivityLogEntry
        {
            Level = "info",
            Category = category,
            Action = action,
            ToolId = toolId,
            Message = message,
            FileName = fileName,
            FileExtension = fileName == null ? null : Path.GetExtension(fileName).ToLowerInvariant(),
            FileSizeBytes = fileSize,
            DurationMs = durationMs,
            Success = true
        });

    public static void Warning(string category, string action, string message, string? toolId = null)
        => Write(new ActivityLogEntry
        {
            Level = "warning",
            Category = category,
            Action = action,
            ToolId = toolId,
            Message = message,
            Success = true
        });

    public static void Error(string category, string action, Exception error, string? toolId = null, string? fileName = null)
        => Write(new ActivityLogEntry
        {
            Level = "error",
            Category = category,
            Action = action,
            ToolId = toolId,
            FileName = fileName,
            FileExtension = fileName == null ? null : Path.GetExtension(fileName).ToLowerInvariant(),
            Success = false,
            Message = error.Message,
            ErrorType = error.GetType().FullName,
            ErrorDetail = Trim(error.ToString(), 4000)
        });

    /// <summary>
    /// Bọc một thao tác: tự đo thời gian và ghi lại kết quả (thành công hay lỗi).
    /// </summary>
    public static async Task<T> MeasureAsync<T>(string category, string action, Func<Task<T>> work,
        string? toolId = null, string? fileName = null, long? fileSize = null)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var result = await work();
            watch.Stop();

            Write(new ActivityLogEntry
            {
                Level = "info",
                Category = category,
                Action = action,
                ToolId = toolId,
                FileName = fileName,
                FileExtension = fileName == null ? null : Path.GetExtension(fileName).ToLowerInvariant(),
                FileSizeBytes = fileSize,
                ResultSizeBytes = result is byte[] bytes ? bytes.LongLength : null,
                DurationMs = watch.ElapsedMilliseconds,
                Success = true
            });

            return result;
        }
        catch (Exception error)
        {
            watch.Stop();

            Write(new ActivityLogEntry
            {
                Level = "error",
                Category = category,
                Action = action,
                ToolId = toolId,
                FileName = fileName,
                FileExtension = fileName == null ? null : Path.GetExtension(fileName).ToLowerInvariant(),
                FileSizeBytes = fileSize,
                DurationMs = watch.ElapsedMilliseconds,
                Success = false,
                Message = error.Message,
                ErrorType = error.GetType().FullName,
                ErrorDetail = Trim(error.ToString(), 4000)
            });

            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  ĐỌC LẠI NHẬT KÝ (dùng cho màn hình "Nhật ký hoạt động")
    // ─────────────────────────────────────────────────────────────────────
    public static List<ActivityLogEntry> ReadRecent(int maxEntries = 200)
    {
        var entries = new List<ActivityLogEntry>();
        if (_logDirectory.Length == 0 || !Directory.Exists(_logDirectory)) return entries;

        var files = Directory.GetFiles(_logDirectory, "activity-*.ndjson")
            .OrderByDescending(f => f)
            .Take(5)
            .ToList();

        foreach (var file in files)
        {
            string[] lines;
            try { lines = File.ReadAllLines(file); }
            catch { continue; }

            for (int i = lines.Length - 1; i >= 0 && entries.Count < maxEntries; i--)
            {
                if (lines[i].Length == 0) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<ActivityLogEntry>(lines[i]);
                    if (entry != null) entries.Add(entry);
                }
                catch { /* dòng hỏng thì bỏ qua */ }
            }

            if (entries.Count >= maxEntries) break;
        }

        return entries;
    }

    public static int PendingCount()
    {
        try
        {
            return _pendingPath.Length > 0 && File.Exists(_pendingPath)
                ? File.ReadAllLines(_pendingPath).Count(l => l.Length > 0)
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  ĐẨY LÊN SUPABASE
    // ─────────────────────────────────────────────────────────────────────
    /// <summary>Gửi các dòng đang chờ lên Supabase. Gửi được tới đâu thì xoá khỏi hàng đợi tới đó.</summary>
    public static async Task<int> FlushAsync()
    {
        if (!RemoteEnabled || _uploading) return 0;

        List<string> batch;
        lock (Gate)
        {
            if (!File.Exists(_pendingPath)) return 0;

            try { batch = File.ReadAllLines(_pendingPath).Where(l => l.Length > 0).Take(200).ToList(); }
            catch { return 0; }

            if (batch.Count == 0) return 0;
        }

        _uploading = true;
        try
        {
            var payload = "[" + string.Join(",", batch) + "]";
            var url = EnvLoader.Get("SUPABASE_URL").TrimEnd('/') + $"/rest/v1/{TableName}";

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await Http.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                LastRemoteError = $"HTTP {(int)response.StatusCode}: {Trim(body, 300)}";
                return 0;
            }

            LastRemoteError = null;
            LastUploadAt = DateTime.Now;
            UploadedCount += batch.Count;

            lock (Gate)
            {
                var remaining = File.ReadAllLines(_pendingPath).Where(l => l.Length > 0).Skip(batch.Count).ToList();
                File.WriteAllLines(_pendingPath, remaining, Encoding.UTF8);
            }

            return batch.Count;
        }
        catch (Exception error)
        {
            LastRemoteError = error.Message;
            return 0;
        }
        finally
        {
            _uploading = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  TIỆN ÍCH
    // ─────────────────────────────────────────────────────────────────────
    private static string DailyLogPath() =>
        Path.Combine(_logDirectory, $"activity-{DateTime.Now:yyyyMMdd}.ndjson");

    private static string SafeDeviceName()
    {
        try { return Environment.MachineName; }
        catch { return "unknown"; }
    }

    private static string OperatingSystemName()
    {
        if (OperatingSystem.IsMacOS()) return "macOS " + Environment.OSVersion.Version;
        if (OperatingSystem.IsWindows()) return "Windows " + Environment.OSVersion.Version;
        if (OperatingSystem.IsLinux()) return "Linux " + Environment.OSVersion.Version;
        return Environment.OSVersion.VersionString;
    }

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
