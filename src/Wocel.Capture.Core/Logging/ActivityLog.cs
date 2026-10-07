using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Wocel.Capture.Logging;

public enum ActivityLevel
{
    Information,
    Warning,
    Error
}

public sealed record ActivityEvent(
    DateTimeOffset Timestamp,
    Guid CorrelationId,
    string EventName,
    ActivityLevel Level,
    long DurationMs,
    bool Succeeded,
    string? ErrorCode = null,
    string? Message = null,
    int? Width = null,
    int? Height = null,
    long? ByteSize = null);

public interface IActivityLog
{
    Task WriteAsync(ActivityEvent activityEvent, CancellationToken cancellationToken = default);
    Task PruneAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

public interface IActivityLogReader
{
    Task<IReadOnlyList<ActivityEvent>> ReadRecentAsync(int limit, CancellationToken cancellationToken = default);
}

public static partial class SensitiveDataRedactor
{
    public static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var redacted = AuthorizationRegex().Replace(value, "$1[REDACTED]");
        redacted = BearerRegex().Replace(redacted, "$1[REDACTED]");
        return TokenRegex().Replace(redacted, match =>
            match.Groups[1].Value + match.Groups[2].Value + "[REDACTED]");
    }

    [GeneratedRegex("(\\\\?\"?Authorization\\\\?\"?\\s*:\\s*\\\\?\"?)(?:Bearer\\s+)?[^\\\"\\s,;}]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationRegex();

    [GeneratedRegex("(\\bBearer\\s+)[A-Za-z0-9._~+/=-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerRegex();

    [GeneratedRegex("(access_token|refresh_token|code)(\\\"?\\s*[:=]\\s*\\\"?)([^\\\"&\\s}]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}

public sealed class NdjsonActivityLog(string directory) : IActivityLog, IActivityLogReader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task WriteAsync(ActivityEvent activityEvent, CancellationToken cancellationToken = default)
    {
        var safe = activityEvent with
        {
            EventName = NormalizeEventName(activityEvent.EventName),
            ErrorCode = activityEvent.ErrorCode is null ? null : SensitiveDataRedactor.Redact(activityEvent.ErrorCode),
            Message = activityEvent.Message is null ? null : SensitiveDataRedactor.Redact(activityEvent.Message)
        };
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"activity-{safe.Timestamp:yyyyMMdd}.ndjson");
        var line = JsonSerializer.Serialize(safe, Options) + Environment.NewLine;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(path, line, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task PruneAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
        {
            return Task.CompletedTask;
        }

        var cutoff = DateOnly.FromDateTime(now.UtcDateTime.Date.AddDays(-30));
        foreach (var path in Directory.EnumerateFiles(directory, "activity-????????.ndjson"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileNameWithoutExtension(path);
            if (DateOnly.TryParseExact(name[9..], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date < cutoff)
            {
                File.Delete(path);
            }
        }

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<ActivityEvent>> ReadRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var result = new List<ActivityEvent>(limit);
        foreach (var path in Directory.EnumerateFiles(directory, "activity-????????.ndjson").OrderByDescending(path => path, StringComparer.Ordinal))
        {
            var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
            for (var index = lines.Length - 1; index >= 0 && result.Count < limit; index--)
            {
                try
                {
                    var entry = JsonSerializer.Deserialize<ActivityEvent>(lines[index], Options);
                    if (entry is not null)
                    {
                        result.Add(entry with { Message = entry.Message is null ? null : SensitiveDataRedactor.Redact(entry.Message) });
                    }
                }
                catch (JsonException)
                {
                    // A partial final line must not make the rest of the activity history unreadable.
                }
            }

            if (result.Count >= limit)
            {
                break;
            }
        }

        return result.OrderByDescending(entry => entry.Timestamp).Take(limit).ToArray();
    }

    private static string NormalizeEventName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new string(value.Trim().Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_').ToArray());
    }
}
