using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Wocel.Capture.Desktop.Composition;

namespace Wocel.Capture.Desktop;

public sealed partial class App : Application
{
    private AppComposition? _composition;
    private TrayIcon? _trayIcon;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _composition = new AppComposition();
            if (_composition.PlatformServices?.SingleInstance.IsPrimary == false)
            {
                _composition.PlatformServices.SingleInstance.NotifyPrimary();
                _composition.Dispose();
                // Avalonia 12 ném lỗi nếu Shutdown trước khi vòng lặp chính chạy, nên hoãn qua dispatcher.
                Dispatcher.UIThread.Post(() => desktop.Shutdown());
                return;
            }
            var window = _composition.CreateMainWindow();
            desktop.MainWindow = window;
            if (!AppLaunchOptions.Parse(Environment.GetCommandLineArgs()).StartHidden)
            {
                window.Show();
            }

            if (_composition.PlatformServices is { } platformServices)
            {
                platformServices.SingleInstance.Activated += (_, _) =>
                    Dispatcher.UIThread.Post(() => ShowWindow(window));
            }

            _composition.CaptureRequested += async (_, _) =>
            {
                await _composition.StartCaptureFlowAsync(window);
            };

            _trayIcon = CreateTray(window, desktop, _composition.ViewModel);
            desktop.Exit += (_, _) =>
            {
                _trayIcon?.Dispose();
                _composition.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static TrayIcon CreateTray(Window window, IClassicDesktopStyleApplicationLifetime desktop, ViewModels.MainWindowViewModel viewModel)
    {
        var show = new NativeMenuItem("Show Wocel Capture");
        show.Click += (_, _) => ShowWindow(window);
        var capture = new NativeMenuItem("Capture screen") { Command = viewModel.CaptureCommand };
        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) => desktop.Shutdown();
        return new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Wocel.Capture.Desktop/Assets/app-icon.png"))),
            ToolTipText = "Wocel Capture",
            Menu = new NativeMenu { Items = { show, capture, new NativeMenuItemSeparator(), exit } }
        };
    }

    private static void ShowWindow(Window window)
    {
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }
}
