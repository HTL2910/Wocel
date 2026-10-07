using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wocel.Capture.Persistence;

public sealed class AtomicJsonFile<T>
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private bool _loadedFromBackup;

    public AtomicJsonFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task<T?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return default;
        }

        try
        {
            var value = await DeserializeAsync(_path, cancellationToken).ConfigureAwait(false);
            _loadedFromBackup = false;
            return value;
        }
        catch (JsonException) when (File.Exists(BackupPath))
        {
            var value = await DeserializeAsync(BackupPath, cancellationToken).ConfigureAwait(false);
            _loadedFromBackup = true;
            return value;
        }
    }

    public async Task SaveAsync(T value, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("File has no directory.");
        Directory.CreateDirectory(directory);
        var tempPath = _path + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(_path) && !_loadedFromBackup)
            {
                File.Copy(_path, BackupPath, true);
            }

            File.Move(tempPath, _path, true);
            _loadedFromBackup = false;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private string BackupPath => _path + ".bak";

    private static async Task<T?> DeserializeAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken).ConfigureAwait(false);
    }
}
