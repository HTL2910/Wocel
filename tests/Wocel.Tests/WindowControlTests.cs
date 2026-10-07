using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Wocel.Core.Events;
using Wocel.Excel;
using ModuleRegistry = Wocel.Shell.Services.ModuleRegistry;
using Wocel.Shell.ViewModels;
using Wocel.Shell.Views;
using Xunit;

namespace Wocel.Tests;

/// <summary>Nút thu nhỏ · phóng to · đóng của khung cửa sổ tự vẽ.</summary>
public class WindowControlTests
{
    private static MainWindow Open()
    {
        var registry = new ModuleRegistry();
        registry.RegisterModule(new Wocel.Word.WordModule());
        registry.RegisterModule(new ExcelModule());

        var window = new MainWindow { DataContext = new ShellWorkspaceViewModel(registry, new EventBus()) };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Click(MainWindow window, string name)
    {
        window.FindControl<Button>(name)!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void WindowButtons_Exist()
    {
        var window = Open();

        foreach (var name in new[] { "BtnWindowMinimize", "BtnWindowMaximize", "BtnWindowClose" })
            Assert.True(window.FindControl<Button>(name) != null, $"Thiếu nút {name}.");
    }

    [AvaloniaFact]
    public void CustomChrome_IsEnabledSoButtonsAreTheOnlyOnes()
    {
        var window = Open();

        // Khung tự vẽ: nếu vẫn dùng khung hệ điều hành thì sẽ có hai bộ nút chồng nhau.
        Assert.True(window.ExtendClientAreaToDecorationsHint);
    }

    [AvaloniaFact]
    public void MinimizeButton_MinimizesWindow()
    {
        var window = Open();
        Assert.NotEqual(WindowState.Minimized, window.WindowState);

        Click(window, "BtnWindowMinimize");

        Assert.Equal(WindowState.Minimized, window.WindowState);
    }

    [AvaloniaFact]
    public void MaximizeButton_TogglesBetweenMaximizedAndNormal()
    {
        var window = Open();
        Assert.Equal(WindowState.Normal, window.WindowState);

        Click(window, "BtnWindowMaximize");
        Assert.Equal(WindowState.Maximized, window.WindowState);

        Click(window, "BtnWindowMaximize");
        Assert.Equal(WindowState.Normal, window.WindowState);
    }

    [AvaloniaFact]
    public void MaximizeButton_ChangesIconWithState()
    {
        var window = Open();
        var button = window.FindControl<Button>("BtnWindowMaximize")!;

        var normalIcon = button.Content?.ToString();

        Click(window, "BtnWindowMaximize");
        var maximizedIcon = button.Content?.ToString();

        Assert.NotEqual(normalIcon, maximizedIcon);
    }

    [AvaloniaFact]
    public void CloseButton_ClosesWindow()
    {
        var window = Open();
        bool closed = false;
        window.Closed += (_, _) => closed = true;

        Click(window, "BtnWindowClose");
        Dispatcher.UIThread.RunJobs();

        Assert.True(closed, "Bấm nút đóng phải đóng cửa sổ.");
    }

    [AvaloniaFact]
    public void WindowButtons_HaveTooltips()
    {
        var window = Open();

        foreach (var name in new[] { "BtnWindowMinimize", "BtnWindowMaximize", "BtnWindowClose" })
        {
            var button = window.FindControl<Button>(name)!;
            Assert.True(ToolTip.GetTip(button) != null, $"Nút {name} chưa có chú thích.");
        }
    }
}
