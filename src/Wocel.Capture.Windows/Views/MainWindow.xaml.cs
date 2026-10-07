using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Wocel.Capture.Logging;
using Wocel.Capture.Cloud;
using Wocel.Capture.Models;
using Wocel.Capture.Persistence;
using Wocel.Capture.Presentation;
using Wocel.Capture.Windows.Capture;
using Wocel.Capture.Windows.Cloud;
using Wocel.Capture.Windows.Services;

namespace Wocel.Capture.Windows.Views;

public partial class MainWindow : Window
{
    private readonly IScreenCaptureService _capture;
    private readonly ImageExportService _export;
    private readonly IHistoryRepository _history;
    private readonly ISettingsRepository _settings;
    private readonly IActivityLog _log;
    private readonly IActivityLogReader? _logReader;
    private readonly GoogleOAuthService _oauth;
    private readonly UploadQueue _uploadQueue;
    private readonly HotkeyService _hotkey = new();
    private bool _allowClose;
    private bool _captureInProgress;
    private nint _windowHandle;
    private IReadOnlyList<CaptureRecord> _historyItems = [];
    private IReadOnlyList<Wocel.Capture.Logging.ActivityEvent> _logItems = [];

    public MainWindow(
        IScreenCaptureService capture,
        ImageExportService export,
        IHistoryRepository history,
        ISettingsRepository settings,
        IActivityLog log,
        GoogleOAuthService oauth,
        UploadQueue uploadQueue)
    {
        InitializeComponent();
        _capture = capture;
        _export = export;
        _history = history;
        _settings = settings;
        _log = log;
        _logReader = log as IActivityLogReader;
        _oauth = oauth;
        _uploadQueue = uploadQueue;
        SourceInitialized += OnSourceInitialized;
        Loaded += async (_, _) =>
        {
            await RefreshAllAsync();
            await PromptForInitialGoogleSignInAsync();
        };
        Closing += (_, args) =>
        {
            if (_allowClose)
            {
                return;
            }

            args.Cancel = true;
            Hide();
        };
    }

    private async void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        _hotkey.Pressed += (_, _) => BeginCapture();
        var settings = await _settings.GetAsync();
        if (!TryRegisterHotkey(settings.Hotkey))
        {
            StatusText.Text = "Không thể đăng ký phím tắt. Bạn vẫn có thể bấm Chụp vùng.";
        }
    }

    private bool TryRegisterHotkey(string value) =>
        GlobalHotkey.TryParse(value, out var hotkey)
        && _hotkey.Register(_windowHandle, hotkey.Modifiers, hotkey.VirtualKey);

    private void OnCaptureClick(object sender, RoutedEventArgs e) => BeginCapture();

    public void RequestExit()
    {
        _allowClose = true;
        _hotkey.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    public async void BeginCapture()
    {
        if (_captureInProgress)
        {
            Show();
            Activate();
            StatusText.Text = "Một phiên chụp đang mở.";
            return;
        }

        _captureInProgress = true;
        var correlationId = Guid.NewGuid();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            Hide();
            await Task.Delay(120);
            var desktop = _capture.CaptureVirtualDesktop();
            var overlay = new SelectionOverlay(desktop);
            if (overlay.ShowDialog() != true || overlay.SelectedRegion is null)
            {
                StatusText.Text = "Đã hủy chụp.";
                return;
            }

            var image = _capture.Crop(desktop, overlay.SelectedRegion.Value);
            var settings = await _settings.GetAsync();
            Show();
            Activate();
            var editor = new EditorWindow(image, settings.AutoUpload, _export, async result =>
            {
                try
                {
                    await RecordExportAsync(Guid.NewGuid(), correlationId, result);
                }
                catch (Exception exception)
                {
                    StatusText.Text = "Không thể lưu hoặc upload ảnh.";
                    await _log.WriteAsync(new Wocel.Capture.Logging.ActivityEvent(
                        DateTimeOffset.Now,
                        correlationId,
                        "export_failed",
                        ActivityLevel.Error,
                        0,
                        false,
                        "EXPORT_FAILED",
                        exception.Message));
                    throw;
                }
            }) { Owner = this };
            editor.ShowDialog();
            await _log.WriteAsync(new Wocel.Capture.Logging.ActivityEvent(DateTimeOffset.Now, correlationId, "capture_completed", ActivityLevel.Information, stopwatch.ElapsedMilliseconds, true, Width: image.PixelWidth, Height: image.PixelHeight));
            await RefreshAllAsync();
        }
        catch (Exception exception)
        {
            StatusText.Text = "Không thể chụp màn hình. Hãy thử lại.";
            await _log.WriteAsync(new Wocel.Capture.Logging.ActivityEvent(DateTimeOffset.Now, correlationId, "capture_failed", ActivityLevel.Error, stopwatch.ElapsedMilliseconds, false, "CAPTURE_FAILED", exception.Message));
        }
        finally
        {
            _captureInProgress = false;
            Show();
            Activate();
        }
    }

    private async Task RecordExportAsync(Guid captureId, Guid correlationId, EditorExportResult result)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WocelCapture", "captures");
        Directory.CreateDirectory(root);
        var extension = result.Format == CaptureImageFormat.Jpeg ? ".jpg" : ".png";
        var cachePath = Path.Combine(root, captureId.ToString("N") + extension);
        var bytes = await _export.SaveAsync(result.Image, cachePath, result.Format);
        var record = new CaptureRecord
        {
            Id = captureId,
            DisplayName = result.Path is null ? Path.GetFileName(cachePath) : Path.GetFileName(result.Path),
            CreatedAt = DateTimeOffset.Now,
            Size = new PixelSize(result.Image.PixelWidth, result.Image.PixelHeight),
            Format = result.Format,
            ByteSize = bytes,
            LocalPath = cachePath,
            UploadState = result.UploadRequested ? UploadState.Pending : UploadState.LocalOnly
        };
        await _history.UpsertAsync(record);
        if (result.UploadRequested)
        {
            if (!_oauth.IsConfigured || !await _oauth.HasStoredCredentialAsync())
            {
                await _history.UpsertAsync(record with { UploadState = UploadState.AuthenticationRequired, LastErrorCode = "AUTH_REQUIRED" });
                StatusText.Text = "Hãy đăng nhập Google để upload ảnh.";
            }
            else
            {
                await _uploadQueue.EnqueueAsync(record.Id);
                var uploaded = await _history.GetAsync(record.Id);
                if (uploaded?.DriveWebLink is { } link)
                {
                    System.Windows.Clipboard.SetText(link.AbsoluteUri);
                    StatusText.Text = "Đã upload và copy link Google Drive.";
                }
            }
        }
        await _log.WriteAsync(new Wocel.Capture.Logging.ActivityEvent(DateTimeOffset.Now, correlationId, "export_completed", ActivityLevel.Information, 0, true, Width: record.Size.Width, Height: record.Size.Height, ByteSize: bytes));
        if (!result.UploadRequested)
        {
            StatusText.Text = "Đã lưu ảnh vào lịch sử.";
        }
    }

    private async Task RefreshAllAsync()
    {
        _historyItems = await _history.GetAllAsync();
        _logItems = _logReader is null ? [] : await _logReader.ReadRecentAsync(500);
        ApplyHistoryFilter();
        ApplyLogFilter();
        var settings = await _settings.GetAsync();
        HotkeyText.Text = settings.Hotkey;
        FolderText.Text = settings.LocalFolder;
        AutoUploadCheck.IsChecked = settings.AutoUpload;
        LaunchAtSignInCheck.IsChecked = settings.LaunchAtSignIn;
        var authenticationRequired = _historyItems.Any(item => item.UploadState == UploadState.AuthenticationRequired);
        var signedIn = _oauth.IsConfigured && await _oauth.HasStoredCredentialAsync() && !authenticationRequired;
        GoogleStatusText.Text = !_oauth.IsConfigured
            ? "Chưa cấu hình Google OAuth client ID"
            : signedIn ? "Đã đăng nhập Google" : authenticationRequired ? "Phiên Google hết hạn — cần đăng nhập lại" : "Chưa đăng nhập";
        GoogleSignInButton.IsEnabled = _oauth.IsConfigured && !signedIn;
        GoogleSignOutButton.IsEnabled = signedIn;
    }

    private void ApplyHistoryFilter()
    {
        var state = HistoryStateFilter.All;
        if (HistoryStatePicker.SelectedItem is ComboBoxItem item && item.Tag is string value)
        {
            state = Enum.Parse<HistoryStateFilter>(value);
        }
        HistoryGrid.ItemsSource = HistoryFilter.Apply(_historyItems, HistorySearch.Text, state);
    }

    private void ApplyLogFilter() => LogGrid.ItemsSource = ActivityLogFilter.Apply(_logItems, LogSearch.Text);
    private void OnHistoryFilterChanged(object sender, EventArgs e) { if (IsLoaded) ApplyHistoryFilter(); }
    private void OnLogFilterChanged(object sender, TextChangedEventArgs e) { if (IsLoaded) ApplyLogFilter(); }
    private async void OnTabChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) await RefreshAllAsync(); }

    private CaptureRecord? SelectedCapture => HistoryGrid.SelectedItem as CaptureRecord;

    private void OnOpenHistory(object sender, RoutedEventArgs e)
    {
        if (SelectedCapture?.LocalPath is { } path && File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    private void OnCopyHistoryLink(object sender, RoutedEventArgs e)
    {
        if (SelectedCapture?.DriveWebLink is { } link)
        {
            System.Windows.Clipboard.SetText(link.AbsoluteUri);
            StatusText.Text = "Đã copy link Google Drive.";
        }
    }

    private async void OnRetryHistory(object sender, RoutedEventArgs e)
    {
        if (SelectedCapture is not { } capture || !HistoryFilter.CanRetry(capture))
        {
            StatusText.Text = "Ảnh này không cần thử lại.";
            return;
        }
        if (!await _oauth.HasStoredCredentialAsync())
        {
            StatusText.Text = "Hãy đăng nhập Google trước khi thử lại.";
            return;
        }
        await _uploadQueue.RetryAsync(capture.Id);
        await RefreshAllAsync();
        StatusText.Text = "Đã xử lý lại upload.";
    }

    private async void OnDeleteHistory(object sender, RoutedEventArgs e)
    {
        if (SelectedCapture is not { } capture || MessageBox.Show(this, "Xóa mục này khỏi lịch sử? File trên Drive sẽ không bị xóa.", "Wocel Capture", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }
        await _history.RemoveAsync(capture.Id);
        await RefreshAllAsync();
    }

    private void OnCopyLog(object sender, RoutedEventArgs e)
    {
        if (LogGrid.SelectedItem is Wocel.Capture.Logging.ActivityEvent entry)
        {
            System.Windows.Clipboard.SetText($"{entry.Timestamp:O} | {entry.CorrelationId} | {entry.EventName} | {entry.ErrorCode} | {ActivityLogFilter.SafeMessage(entry)}");
        }
    }

    private async void OnSaveSettings(object sender, RoutedEventArgs e)
    {
        var hotkeyText = string.IsNullOrWhiteSpace(HotkeyText.Text) ? "PrintScreen" : HotkeyText.Text.Trim();
        if (!GlobalHotkey.TryParse(hotkeyText, out _))
        {
            StatusText.Text = "Phím tắt không hợp lệ. Ví dụ: PrintScreen hoặc Ctrl+Shift+S.";
            return;
        }
        await _settings.SaveAsync(new CaptureSettings
        {
            Hotkey = hotkeyText,
            LocalFolder = FolderText.Text.Trim(),
            AutoUpload = AutoUploadCheck.IsChecked == true,
            LaunchAtSignIn = LaunchAtSignInCheck.IsChecked == true
        });
        StartupService.SetEnabled(LaunchAtSignInCheck.IsChecked == true);
        StatusText.Text = TryRegisterHotkey(hotkeyText)
            ? "Đã lưu cài đặt và áp dụng phím tắt."
            : "Đã lưu nhưng phím tắt đang được ứng dụng khác sử dụng.";
    }

    private async void OnGoogleSignIn(object sender, RoutedEventArgs e)
    {
        await SignInGoogleAsync();
    }

    private async Task PromptForInitialGoogleSignInAsync()
    {
        if (!_oauth.IsConfigured || await _oauth.HasStoredCredentialAsync())
        {
            return;
        }

        MainTabs.SelectedIndex = 2;
        if (MessageBox.Show(this, "Đăng nhập Google để Wocel Capture có thể upload ảnh và tạo link chia sẻ.", "Thiết lập Google Drive", MessageBoxButton.OKCancel, MessageBoxImage.Information) == MessageBoxResult.OK)
        {
            await SignInGoogleAsync();
        }
    }

    private async Task SignInGoogleAsync()
    {
        GoogleSignInButton.IsEnabled = false;
        GoogleStatusText.Text = "Đang mở trình duyệt để đăng nhập...";
        try
        {
            await _oauth.SignInAsync();
            GoogleStatusText.Text = "Đã đăng nhập Google";
            GoogleSignOutButton.IsEnabled = true;
            foreach (var capture in (await _history.GetAllAsync()).Where(item => item.UploadState is UploadState.Pending or UploadState.AuthenticationRequired))
            {
                await _uploadQueue.RetryAsync(capture.Id);
            }
            await RefreshAllAsync();
        }
        catch (Exception exception)
        {
            GoogleStatusText.Text = "Đăng nhập không thành công.";
            StatusText.Text = SensitiveDataRedactor.Redact(exception.Message);
            GoogleSignInButton.IsEnabled = true;
        }
    }

    private async void OnGoogleSignOut(object sender, RoutedEventArgs e)
    {
        try
        {
            await _oauth.SignOutAsync();
            StatusText.Text = "Đã đăng xuất Google. Ảnh trên Drive không bị xóa.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Đã xóa phiên cục bộ; lỗi thu hồi từ xa: {SensitiveDataRedactor.Redact(exception.Message)}";
        }
        finally
        {
            await RefreshAllAsync();
        }
    }
}
