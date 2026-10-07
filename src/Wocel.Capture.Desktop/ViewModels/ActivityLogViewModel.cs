using System.Globalization;
using Wocel.Capture.Logging;

namespace Wocel.Capture.Desktop.ViewModels;

public interface ITextClipboard
{
    Task WriteTextAsync(string text, CancellationToken cancellationToken = default);
}

public sealed class ActivityLogViewModel(IActivityLogReader reader, ITextClipboard clipboard)
{
    public IReadOnlyList<ActivityEvent> Items { get; private set; } = [];

    public async Task RefreshAsync(int limit = 500, CancellationToken cancellationToken = default) =>
        Items = await reader.ReadRecentAsync(limit, cancellationToken).ConfigureAwait(false);

    public Task CopyAsync(ActivityEvent entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var text = string.Join(" | ", new[]
        {
            entry.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            entry.EventName,
            entry.Succeeded ? "success" : "failed",
            entry.ErrorCode ?? string.Empty,
            entry.Message ?? string.Empty
        });
        return clipboard.WriteTextAsync(SensitiveDataRedactor.Redact(text), cancellationToken);
    }
}
