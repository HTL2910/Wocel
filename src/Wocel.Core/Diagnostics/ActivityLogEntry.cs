using System.Text.Json.Serialization;

namespace Wocel.Core.Diagnostics;

public enum ActivityLevel
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Một dòng nhật ký hoạt động. Chỉ ghi thông tin về thao tác — không bao giờ
/// chứa nội dung tệp của người dùng.
/// </summary>
public sealed class ActivityLogEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("occurred_at")] public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("session_id")] public string SessionId { get; set; } = string.Empty;
    [JsonPropertyName("app_version")] public string AppVersion { get; set; } = string.Empty;
    [JsonPropertyName("environment")] public string Environment { get; set; } = string.Empty;
    [JsonPropertyName("device")] public string Device { get; set; } = string.Empty;
    [JsonPropertyName("os")] public string Os { get; set; } = string.Empty;

    [JsonPropertyName("level")] public string Level { get; set; } = "info";
    [JsonPropertyName("category")] public string Category { get; set; } = "app";
    [JsonPropertyName("action")] public string Action { get; set; } = string.Empty;
    [JsonPropertyName("tool_id")] public string? ToolId { get; set; }

    [JsonPropertyName("file_name")] public string? FileName { get; set; }
    [JsonPropertyName("file_ext")] public string? FileExtension { get; set; }
    [JsonPropertyName("file_size_bytes")] public long? FileSizeBytes { get; set; }
    [JsonPropertyName("result_size_bytes")] public long? ResultSizeBytes { get; set; }
    [JsonPropertyName("page_count")] public int? PageCount { get; set; }
    [JsonPropertyName("item_count")] public int? ItemCount { get; set; }

    [JsonPropertyName("duration_ms")] public long DurationMs { get; set; }
    [JsonPropertyName("success")] public bool Success { get; set; } = true;
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("error_type")] public string? ErrorType { get; set; }
    [JsonPropertyName("error_detail")] public string? ErrorDetail { get; set; }

    [JsonIgnore] public string LevelText => Level;

    public override string ToString()
    {
        var status = Success ? "OK " : "LỖI";
        var scope = string.IsNullOrEmpty(ToolId) ? Category : $"{Category}/{ToolId}";
        var file = string.IsNullOrEmpty(FileName) ? string.Empty : $"  [{FileName}]";
        var detail = string.IsNullOrEmpty(Message) ? string.Empty : $"  — {Message}";
        return $"{OccurredAt.ToLocalTime():dd/MM HH:mm:ss}  {status}  {scope,-28} {DurationMs,6} ms{file}{detail}";
    }
}
