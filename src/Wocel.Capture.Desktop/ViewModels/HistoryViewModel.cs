using Wocel.Capture.Cloud;
using Wocel.Capture.Models;
using Wocel.Capture.Persistence;
using Wocel.Capture.Presentation;

namespace Wocel.Capture.Desktop.ViewModels;

public sealed class HistoryViewModel(
    IHistoryRepository history,
    UploadQueue uploadQueue,
    Func<CancellationToken, Task>? reauthorize = null)
{
    private IReadOnlyList<CaptureRecord> _all = [];
    private string _searchText = string.Empty;
    private HistoryStateFilter _stateFilter;

    public IReadOnlyList<CaptureRecord> Items { get; private set; } = [];

    public string SearchText
    {
        get => _searchText;
        set { _searchText = value ?? string.Empty; ApplyFilter(); }
    }

    public HistoryStateFilter StateFilter
    {
        get => _stateFilter;
        set { _stateFilter = value; ApplyFilter(); }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _all = await history.GetAllAsync(cancellationToken).ConfigureAwait(false);
        ApplyFilter();
    }

    public async Task RetryAsync(CaptureRecord capture, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (!HistoryFilter.CanRetry(capture))
        {
            throw new InvalidOperationException("This capture is not retryable.");
        }
        if (capture.UploadState == UploadState.AuthenticationRequired)
        {
            if (reauthorize is null) throw new InvalidOperationException("Google reauthorization is required.");
            await reauthorize(cancellationToken).ConfigureAwait(false);
        }
        await uploadQueue.RetryAsync(capture.Id, cancellationToken).ConfigureAwait(false);
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    private void ApplyFilter() => Items = HistoryFilter.Apply(_all, _searchText, _stateFilter);
}
