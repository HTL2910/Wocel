using Wocel.Capture.Models;

namespace Wocel.Capture.Persistence;

public interface IHistoryRepository
{
    Task<IReadOnlyList<CaptureRecord>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<CaptureRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task UpsertAsync(CaptureRecord capture, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface ISettingsRepository
{
    Task<CaptureSettings> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CaptureSettings settings, CancellationToken cancellationToken = default);
}

public sealed class JsonHistoryRepository(string path) : IHistoryRepository
{
    private readonly AtomicJsonFile<List<CaptureRecord>> _file = new(path);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<CaptureRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = await _file.LoadAsync(cancellationToken).ConfigureAwait(false) ?? [];
            return items.OrderByDescending(item => item.CreatedAt).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CaptureRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await GetAllAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(item => item.Id == id);

    public async Task UpsertAsync(CaptureRecord capture, CancellationToken cancellationToken = default)
    {
        capture.Validate();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = await _file.LoadAsync(cancellationToken).ConfigureAwait(false) ?? [];
            var index = items.FindIndex(item => item.Id == capture.Id);
            if (index >= 0)
            {
                items[index] = capture;
            }
            else
            {
                items.Add(capture);
            }

            await _file.SaveAsync(items, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = await _file.LoadAsync(cancellationToken).ConfigureAwait(false) ?? [];
            items.RemoveAll(item => item.Id == id);
            await _file.SaveAsync(items, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed class JsonSettingsRepository(string path) : ISettingsRepository
{
    private readonly AtomicJsonFile<CaptureSettings> _file = new(path);

    public async Task<CaptureSettings> GetAsync(CancellationToken cancellationToken = default) =>
        await _file.LoadAsync(cancellationToken).ConfigureAwait(false) ?? CaptureSettings.CreateCurrentPlatformDefault();

    public Task SaveAsync(CaptureSettings settings, CancellationToken cancellationToken = default) =>
        _file.SaveAsync(settings, cancellationToken);
}
