using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Wocel.Core.Events;
using Wocel.Excel;
using Wocel.Shell.Services;
using Wocel.Shell.ViewModels;
using Wocel.Shell.Views;
using Xunit;

namespace Wocel.Tests;

/// <summary>
/// Dựng cửa sổ chính thật để bắt lỗi nối sự kiện thiếu hoặc điều khiển bị đổi tên —
/// những thứ trình biên dịch không phát hiện được vì đều là gán runtime.
/// </summary>
public class MainWindowSmokeTests
{
    private static ShellWorkspaceViewModel CreateViewModel()
    {
        var registry = new ModuleRegistry();
        registry.RegisterModule(new Wocel.Word.WordModule());
        registry.RegisterModule(new ExcelModule());
        return new ShellWorkspaceViewModel(registry, new EventBus());
    }

    [AvaloniaFact]
    public void MainWindow_OpensAndWiresEveryControl()
    {
        var window = new MainWindow { DataContext = CreateViewModel() };

        var error = Record.Exception(() =>
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
        });

        Assert.True(error == null, $"Cửa sổ chính không mở được: {error?.Message}");
    }

    [AvaloniaFact]
    public void MainWindow_CreatingWorkbookDoesNotThrow()
    {
        var viewModel = CreateViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var error = Record.Exception(() =>
        {
            viewModel.CreateNewExcelWorkbook();
            Dispatcher.UIThread.RunJobs();
        });

        Assert.True(error == null, $"Tạo sổ tính mới bị lỗi: {error?.Message}");
        Assert.NotNull(viewModel.ActiveTab);
    }
}
