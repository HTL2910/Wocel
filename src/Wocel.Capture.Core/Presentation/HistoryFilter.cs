using Wocel.Capture.Logging;
using Wocel.Capture.Models;

namespace Wocel.Capture.Presentation;

public enum HistoryStateFilter
{
    All,
    Uploaded,
    NotUploaded,
    Failed
}

public static class HistoryFilter
{
    public static IReadOnlyList<CaptureRecord> Apply(
        IEnumerable<CaptureRecord> source,
        string? search,
        HistoryStateFilter state)
    {
        ArgumentNullException.ThrowIfNull(source);
        var query = source;
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(item => item.DisplayName.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase));
        }

        query = state switch
        {
            HistoryStateFilter.Uploaded => query.Where(item => item.UploadState is UploadState.UploadedPrivate or UploadState.UploadedShared),
            HistoryStateFilter.NotUploaded => query.Where(item => item.UploadState is UploadState.LocalOnly or UploadState.Pending or UploadState.Uploading),
            HistoryStateFilter.Failed => query.Where(item => item.UploadState is UploadState.Failed or UploadState.AuthenticationRequired),
            _ => query
        };
        return query.OrderByDescending(item => item.CreatedAt).ToArray();
    }

    public static bool CanRetry(CaptureRecord capture) =>
        capture.UploadState is UploadState.Failed or UploadState.Pending or UploadState.AuthenticationRequired;
}

public static class ActivityLogFilter
{
    public static IReadOnlyList<ActivityEvent> Apply(IEnumerable<ActivityEvent> source, string? search)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(search))
        {
            return source.OrderByDescending(item => item.Timestamp).ToArray();
        }

        var term = search.Trim();
        return source.Where(item =>
                item.EventName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (item.ErrorCode?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || SafeMessage(item).Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Timestamp)
            .ToArray();
    }

    public static string SafeMessage(ActivityEvent entry) =>
        SensitiveDataRedactor.Redact(entry.Message ?? string.Empty);
}
