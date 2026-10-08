using Application = Avalonia.Application;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Wocel.Capture.Cloud;
using Wocel.Capture.Desktop.Capture;
using Wocel.Capture.Desktop.Services;
using Wocel.Capture.Desktop.ViewModels;
using Wocel.Capture.Desktop.Views;
using Wocel.Capture.Models;
using Wocel.Capture.Platform;
using Wocel.Capture.Platform.Capture;
using Wocel.Capture.Platform.Hotkeys;
using Wocel.Capture.Platform.Windows;

namespace Wocel.Capture.Desktop.Composition;

public sealed class AppComposition : IDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly HttpClient _httpClient = new();
    private readonly GoogleOAuthService? _oauthService;

    public AppComposition(PlatformServiceSet? platformServices = null)
    {
        platformServices ??= OperatingSystem.IsWindows() ? WindowsPlatformServices.Create() : null;
        PlatformServices = platformServices;
        if (platformServices is not null)
        {
            _oauthService = new GoogleOAuthService(
                OAuthConfiguration.FindClientId(), _httpClient, platformServices.TokenStore, platformServices.Browser);
        }
        MainWindowViewModel? viewModel = null;
        viewModel = new MainWindowViewModel(
            capture: () => CaptureRequested?.Invoke(this, EventArgs.Empty),
            showHistory: () => viewModel!.SelectedTabIndex = 0,
            showLog: () => viewModel!.SelectedTabIndex = 0,
            showSettings: () => viewModel!.SelectedTabIndex = viewModel.SelectedTabIndex == 1 ? 0 : 1,
            signIn: async () =>
            {
                if (viewModel == null) return;
                try
                {
                    if (_oauthService is null)
                    {
                        viewModel.StatusText = "Google Drive is unavailable until native platform services are loaded.";
                        return;
                    }
                    viewModel.StatusText = "Đang mở trình duyệt để đăng nhập tài khoản Google...";
                    await _oauthService.SignInAsync(ShutdownToken);
                    viewModel.StatusText = "Google Drive: Đã đăng nhập tài khoản Google thành công!";
                }
                catch (DriveOperationException exception)
                {
                    viewModel.StatusText = $"Google sign-in failed ({exception.ErrorCode}).";
                }
                catch (Exception)
                {
                    viewModel.StatusText = "Google sign-in failed. Please try again.";
                }
            });
        ViewModel = viewModel;
        if (PlatformServices is not null
            && HotkeyGesture.TryParse(CaptureSettings.CreateCurrentPlatformDefault().Hotkey, out var hotkey))
        {
            PlatformServices.GlobalHotkey.Pressed += OnGlobalHotkeyPressed;
            var registration = PlatformServices.GlobalHotkey.Register(hotkey);
            if (!registration.Succeeded)
            {
                ViewModel.StatusText = $"Global shortcut unavailable ({registration.ErrorCode}); use Capture from the window or tray.";
            }
        }
    }

    public PlatformServiceSet? PlatformServices { get; }
    public MainWindowViewModel ViewModel { get; }
    public CancellationToken ShutdownToken => _shutdown.Token;
    public event EventHandler? CaptureRequested;

    private void OnGlobalHotkeyPressed(object? sender, EventArgs e) => CaptureRequested?.Invoke(this, EventArgs.Empty);

    public MainWindow CreateMainWindow() => new() { DataContext = ViewModel };

    // Thời gian chờ để Windows (DWM) chạy xong hiệu ứng ẩn và vẽ lại màn hình trước khi chụp.
    private static readonly TimeSpan HideSettleDelay = TimeSpan.FromMilliseconds(350);
    private bool _captureInProgress;

    public async Task StartCaptureFlowAsync(Window? ownerWindow)
    {
        if (_captureInProgress) return;
        _captureInProgress = true;
        var hiddenWindows = new List<Window>();
        try
        {
            ViewModel.StatusText = "Đang chụp màn hình...";
            HideAppWindows(ownerWindow, hiddenWindows);
            await Task.Delay(HideSettleDelay, ShutdownToken);

            var captureService = PlatformServices?.ScreenCapture ?? new DesktopScreenCaptureService();
            var frame = await captureService.CaptureVirtualDesktopAsync(ShutdownToken);
            var rawDisplays = await captureService.GetDisplaysAsync(ShutdownToken);

            var primaryDisplay = rawDisplays.FirstOrDefault(d => d.IsPrimary) ?? rawDisplays.FirstOrDefault();
            var scale = primaryDisplay?.ScaleFactor ?? (OperatingSystem.IsMacOS() ? 2.0 : 1.0);
            var displayGeometries = new List<DisplayGeometry>
            {
                new DisplayGeometry(
                    "primary",
                    frame.PhysicalBounds,
                    new LogicalRect(0, 0, frame.Width / scale, frame.Height / scale),
                    scale,
                    isPrimary: true)
            };

            var controller = SelectionController.Begin(displayGeometries, frame);
            var overlayVm = new SelectionOverlayViewModel(displayGeometries[0].Id, controller);
            var overlayWindow = new SelectionOverlayWindow(displayGeometries[0], overlayVm, frame);

            var tcs = new TaskCompletionSource<PixelRect?>();

            overlayWindow.Confirmed += (_, _) =>
            {
                if (controller.TryComplete(out var sel))
                {
                    tcs.TrySetResult(sel);
                }
                else
                {
                    tcs.TrySetResult(new PixelRect(0, 0, frame.Width, frame.Height));
                }
                overlayWindow.Close();
            };

            overlayWindow.Cancelled += (_, _) =>
            {
                tcs.TrySetResult(null);
                overlayWindow.Close();
            };

            overlayWindow.Show();
            overlayWindow.Activate();

            var selectedRect = await tcs.Task;

            if (selectedRect is { } rect && rect.Width >= 2 && rect.Height >= 2)
            {
                ViewModel.StatusText = $"Đã chọn vùng ({rect.Width} × {rect.Height} px). Đang mở Editor...";
                var editorFrame = (rect.Width == frame.Width && rect.Height == frame.Height && rect.X == 0 && rect.Y == 0)
                    ? frame
                    : DesktopScreenCaptureService.Crop(frame, rect);

                var editor = new EditorWindow(editorFrame, ViewModel.CreatePublicLink);
                editor.Closed += (_, _) =>
                {
                    ownerWindow?.Show();
                    ownerWindow?.Activate();
                    ViewModel.StatusText = "Sẵn sàng chụp (Ready)";
                };
                RestoreWindows(hiddenWindows, except: ownerWindow);
                editor.Show();
                editor.Activate();
            }
            else
            {
                RestoreWindows(hiddenWindows, except: null);
                if (ownerWindow?.IsVisible == true) ownerWindow.Activate();
                ViewModel.StatusText = "Đã hủy chụp.";
            }
        }
        catch (Exception ex)
        {
            RestoreWindows(hiddenWindows, except: null);
            ownerWindow?.Show();
            ownerWindow?.Activate();
            ViewModel.StatusText = $"Lỗi chụp màn hình: {ex.Message}";
        }
        finally
        {
            _captureInProgress = false;
        }
    }

    /// <summary>Ẩn cửa sổ chính và mọi cửa sổ khác của app (kể cả Editor cũ) để không bị chụp vào ảnh.</summary>
    private static void HideAppWindows(Window? ownerWindow, List<Window> hidden)
    {
        var windows = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.Windows.ToList()
            : [];
        if (ownerWindow is not null && !windows.Contains(ownerWindow)) windows.Add(ownerWindow);
        foreach (var window in windows.Where(w => w.IsVisible))
        {
            window.Hide();
            hidden.Add(window);
        }
    }

    private static void RestoreWindows(List<Window> hidden, Window? except)
    {
        foreach (var window in hidden.Where(w => w != except)) window.Show();
        hidden.Clear();
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        if (PlatformServices is not null)
        {
            PlatformServices.GlobalHotkey.Pressed -= OnGlobalHotkeyPressed;
            PlatformServices.GlobalHotkey.Dispose();
            PlatformServices.SingleInstance.Dispose();
        }
        _shutdown.Dispose();
        _httpClient.Dispose();
    }
}
