using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Wocel.Shell.Views;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Wocel.Tests.HeadlessAppBuilder))]

namespace Wocel.Tests;

public static class HeadlessAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<Wocel.Shell.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>
/// Dựng thật bộ công cụ trong môi trường không màn hình để chắc chắn mọi bảng
/// tuỳ chọn của 29 công cụ đều tạo được, không rơi vào lỗi lúc chạy.
/// </summary>
public class PdfToolsPanelTests
{
    [AvaloniaFact]
    public void Panel_BuildsEveryToolForm()
    {
        var panel = new PdfToolsPanel();
        var window = new Window { Content = panel, Width = 1200, Height = 800 };
        window.Show();

        var toolIds = PdfToolsPanel.ToolIds;
        Assert.True(toolIds.Count is >= 10 and <= 12, $"Danh sách công cụ nên gọn, hiện có {toolIds.Count}");

        foreach (var id in toolIds)
        {
            var exception = Record.Exception(() => panel.SelectToolById(id));
            Assert.True(exception == null, $"Công cụ “{id}” dựng lỗi: {exception?.Message}");
            Assert.True(panel.VisibleOptionCount > 0, $"Công cụ “{id}” không hiện tuỳ chọn nào.");
        }
    }

    [AvaloniaFact]
    public void Panel_RejectsUnknownToolId()
    {
        var panel = new PdfToolsPanel();
        new Window { Content = panel }.Show();

        Assert.False(panel.SelectToolById("khong-ton-tai"));
    }
}
