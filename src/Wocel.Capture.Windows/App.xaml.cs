using System.IO;
using System.Windows;
using System.Windows.Interop;
using Wocel.Capture.Logging;
using Wocel.Capture.Persistence;
using Wocel.Capture.Cloud;
using Wocel.Capture.Windows.Cloud;
using Wocel.Capture.Windows.Capture;
using Wocel.Capture.Windows.Security;
using Wocel.Capture.Windows.Services;
using Wocel.Capture.Windows.Views;

namespace Wocel.Capture.Windows;

public partial class App : System.Windows.Application
{
    private SingleInstanceService? _singleInstance;
    private TrayService? _tray;
    private MainWindow? _mainWindow;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(10) };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new SingleInstanceService("WocelCapture.SingleInstance");
        if (!_singleInstance.IsPrimary)
        {
            _singleInstance.NotifyPrimary();
            Shutdown();
            return;
        }

        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WocelCapture");
        Directory.CreateDirectory(root);
        var history = new JsonHistoryRepository(Path.Combine(root, "history.json"));
        var settings = new JsonSettingsRepository(Path.Combine(root, "settings.json"));
        var logger = new NdjsonActivityLog(Path.Combine(root, "logs"));
        _ = PruneLogsAsync(logger);
        var tokenStore = new DpapiTokenStore(Path.Combine(root, "google-refresh-token.bin"));
        var oauth = new GoogleOAuthService(OAuthConfiguration.FindClientId(), _httpClient, tokenStore);
        var drive = new GoogleDriveClient(_httpClient, oauth);
        var uploadQueue = new UploadQueue(history, drive, logger);
        _ = ResumePendingUploadsAsync(uploadQueue, history, oauth, logger);
        _mainWindow = new MainWindow(new ScreenCaptureService(), new ImageExportService(), history, settings, logger, oauth, uploadQueue);
        _singleInstance.Activated += (_, _) => Dispatcher.Invoke(ShowMainWindow);
        _tray = new TrayService(ShowMainWindow, () => _mainWindow.BeginCapture(), _mainWindow.RequestExit);
        if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase))
        {
            ShowMainWindow();
        }
        else
        {
            _ = new WindowInteropHelper(_mainWindow).EnsureHandle();
        }
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _singleInstance?.Dispose();
        _httpClient.Dispose();
        base.OnExit(e);
    }

    private static async Task PruneLogsAsync(IActivityLog logger)
    {
        try
        {
            await logger.PruneAsync(DateTimeOffset.UtcNow);
        }
        catch
        {
            // Retention maintenance must not prevent startup.
        }
    }

    private static async Task ResumePendingUploadsAsync(UploadQueue queue, IHistoryRepository history, GoogleOAuthService oauth, IActivityLog logger)
    {
        try
        {
            await queue.RecoverInterruptedAsync();
            if (!oauth.IsConfigured || !await oauth.HasStoredCredentialAsync())
            {
                return;
            }

            foreach (var capture in (await history.GetAllAsync()).Where(item => item.UploadState == Wocel.Capture.Models.UploadState.Pending))
            {
                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(capture.RetryCount, 5))));
                await Task.Delay(delay);
                await queue.EnqueueAsync(capture.Id);
            }
        }
        catch (Exception exception)
        {
            try
            {
                await logger.WriteAsync(new ActivityEvent(DateTimeOffset.UtcNow, Guid.NewGuid(), "upload_recovery_failed", ActivityLevel.Warning, 0, false, "RECOVERY_FAILED", exception.Message));
            }
            catch
            {
                // Startup recovery and diagnostics are both best-effort.
            }
        }
    }
}
