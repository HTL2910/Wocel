using System.Diagnostics;
using Wocel.Capture.Platform.Services;

namespace Wocel.Capture.Platform.Windows;

public static class WindowsPlatformServices
{
    public static PlatformServiceSet Create()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows platform services require Windows.");
        var paths = new WindowsPlatformPaths();
        return new PlatformServiceSet(
            new WindowsScreenCaptureService(),
            new WindowsHotkeyService(),
            new WindowsTokenStore(Path.Combine(paths.AppDataDirectory, "google-token.dat")),
            new WindowsStartupService(),
            new WindowsSingleInstanceService(),
            paths,
            new WindowsBrowserLauncher());
    }
}

public sealed class WindowsBrowserLauncher : IBrowserLauncher
{
    public Task OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme is not ("https" or "http")) throw new ArgumentException("Only HTTP browser URLs are allowed.", nameof(uri));
        cancellationToken.ThrowIfCancellationRequested();
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        return Task.CompletedTask;
    }
}
