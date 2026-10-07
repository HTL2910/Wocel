using Wocel.Capture.Models;
using Wocel.Capture.Persistence;
using Wocel.Capture.Platform.Capture;
using Wocel.Capture.Platform.Services;

namespace Wocel.Capture.Desktop.ViewModels;

public sealed class SettingsViewModel(
    ISettingsRepository settings,
    IScreenCaptureService screenCapture,
    IStartupService startup,
    bool isFirstRun,
    Func<CancellationToken, Task<bool>> hasGoogleCredential)
{
    public CaptureSettings Value { get; private set; } = CaptureSettings.CreateCurrentPlatformDefault();
    public CapturePermissionStatus PermissionStatus { get; private set; } = CapturePermissionStatus.Unknown;
    public bool ShouldShowGooglePrompt { get; private set; }
    public string PermissionStatusText => PermissionStatus switch
    {
        CapturePermissionStatus.Denied => "Screen Recording is denied. Open System Settings to allow Wocel Capture.",
        CapturePermissionStatus.RestartRequired => "Screen Recording was granted. Restart Wocel Capture to continue.",
        CapturePermissionStatus.Granted or CapturePermissionStatus.NotRequired => "Screen capture permission is ready.",
        _ => "Screen capture permission has not been checked."
    };

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Value = await settings.GetAsync(cancellationToken).ConfigureAwait(false);
        PermissionStatus = await screenCapture.GetPermissionStatusAsync(cancellationToken).ConfigureAwait(false);
        ShouldShowGooglePrompt = isFirstRun && !await hasGoogleCredential(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(CaptureSettings value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        startup.SetEnabled(value.LaunchAtSignIn);
        await settings.SaveAsync(value, cancellationToken).ConfigureAwait(false);
        Value = value;
    }
}
