namespace Wocel.Capture.Platform.Services;

public interface IProtectedTokenStore
{
    Task SaveAsync(string token, CancellationToken cancellationToken = default);
    Task<string?> LoadAsync(CancellationToken cancellationToken = default);
    Task DeleteAsync(CancellationToken cancellationToken = default);
}

public interface IStartupService
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}

public interface ISingleInstanceService : IDisposable
{
    bool IsPrimary { get; }
    event EventHandler? Activated;
    void NotifyPrimary();
}

public interface IPlatformPaths
{
    string AppDataDirectory { get; }
    string CacheDirectory { get; }
    string LogDirectory { get; }
    string PicturesDirectory { get; }
}

public interface IBrowserLauncher
{
    Task OpenAsync(Uri uri, CancellationToken cancellationToken = default);
}
