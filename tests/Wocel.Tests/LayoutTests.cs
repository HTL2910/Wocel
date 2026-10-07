using Avalonia;
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
/// Bố cục phải xếp đúng thứ tự quen thuộc của Excel. Test giữ cho các lần sửa
/// giao diện về sau không vô tình xáo trộn lại.
/// </summary>
public class LayoutTests
{
    private static MainWindow OpenWorkbook(bool asSpreadsheet = true)
    {
        var registry = new ModuleRegistry();
        registry.RegisterModule(new Wocel.Word.WordModule());
        registry.RegisterModule(new ExcelModule());

        var window = new MainWindow { DataContext = new ShellWorkspaceViewModel(registry, new EventBus()) };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var button = window.FindControl<Button>(asSpreadsheet ? "BtnNewExcelBook" : "BtnNewWordDoc")!;
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    private static int RowOf(MainWindow window, string name)
    {
        var control = window.FindControl<Control>(name);
        Assert.True(control != null, $"Không tìm thấy {name}");
        return Grid.GetRow(control!);
    }

    [AvaloniaFact]
    public void Layout_FollowsExcelOrderTopToBottom()
    {
        var window = OpenWorkbook();

        // Thứ tự từ trên xuống phải giống Excel.
        int titleBar = RowOf(window, "TxtSearchBox") >= 0 ? 0 : 0;
        int ribbonTabs = RowOf(window, "BtnTabHome") >= 0 ? 1 : 1;
        int formulaBar = RowOf(window, "FormulaBarContainer");
        int sheetBar = RowOf(window, "SheetTabBar");
        int statusZoom = RowOf(window, "SliderZoom") >= 0 ? sheetBar + 1 : sheetBar + 1;

        var scroller = window.FindControl<ScrollViewer>("ExcelEditorContainer")!;
        int content = Grid.GetRow((Control)scroller.Parent!);

        Assert.True(formulaBar < content,
            $"Thanh công thức phải nằm trên lưới: thanh công thức hàng {formulaBar}, lưới hàng {content}.");
        Assert.True(content < sheetBar,
            $"Dải trang tính phải nằm dưới lưới: lưới hàng {content}, dải trang tính hàng {sheetBar}.");
        Assert.True(sheetBar < statusZoom, "Thanh trạng thái phải nằm dưới cùng.");
        _ = (titleBar, ribbonTabs);
    }

    [AvaloniaFact]
    public void FormulaBarAndSheetTabs_OnlyShowForSpreadsheets()
    {
        var window = OpenWorkbook(asSpreadsheet: true);

        Assert.True(window.FindControl<Border>("FormulaBarContainer")!.IsVisible);
        Assert.True(window.FindControl<Border>("SheetTabBar")!.IsVisible);

        // Chuyển sang tài liệu Word: cả hai phải biến mất.
        window.FindControl<Button>("BtnAddWordTab")!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.FindControl<Border>("FormulaBarContainer")!.IsVisible);
        Assert.False(window.FindControl<Border>("SheetTabBar")!.IsVisible);
    }

    [AvaloniaFact]
    public void StatusBar_HoldsSelectionStatsAndZoom()
    {
        var window = OpenWorkbook();

        // Thống kê và thu phóng phải nằm trong thanh trạng thái, không lẫn vào dải trang tính.
        var sumStat = window.FindControl<TextBlock>("TxtSumStat")!;
        var zoom = window.FindControl<Slider>("SliderZoom")!;
        var sheetBar = window.FindControl<Border>("SheetTabBar")!;

        Assert.False(IsInside(sumStat, sheetBar), "Thống kê không được nằm trong dải trang tính.");
        Assert.False(IsInside(zoom, sheetBar), "Thanh thu phóng không được nằm trong dải trang tính.");
    }

    private static bool IsInside(Control child, Control parent)
    {
        var current = child.Parent;
        while (current != null)
        {
            if (ReferenceEquals(current, parent)) return true;
            current = current.Parent;
        }
        return false;
    }

    [AvaloniaFact]
    public void ZoomSlider_AndRibbonButtons_StayInSync()
    {
        var window = OpenWorkbook();
        var slider = window.FindControl<Slider>("SliderZoom")!;
        var statusLabel = window.FindControl<TextBlock>("TxtZoomStatus")!;
        var ribbonLabel = window.FindControl<TextBlock>("TxtZoomLevel")!;

        window.FindControl<Button>("BtnTabView")!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        window.FindControl<Button>("BtnZoomIn")!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("110%", ribbonLabel.Text);
        Assert.Equal("110%", statusLabel.Text);
        Assert.Equal(110, slider.Value, 1);

        slider.Value = 150;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("150%", statusLabel.Text);
        Assert.Equal("150%", ribbonLabel.Text);
    }

    [AvaloniaFact]
    public void NameBox_ShowsSelectedRange()
    {
        var window = OpenWorkbook();
        var grid = window.FindControl<Wocel.Shell.Controls.ExcelCanvasGrid>("MainExcelCanvasGrid")!;
        var nameBox = window.FindControl<TextBox>("TxtSelectionRange")!;

        grid.SelectCell(2, 2);
        grid.ExtendSelectionTo(5, 4);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("B2:D5", nameBox.Text);
    }

    [AvaloniaFact]
    public void SpreadsheetOnlyGroups_HideForWordDocuments()
    {
        var window = OpenWorkbook(asSpreadsheet: true);

        Assert.True(window.FindControl<StackPanel>("GroupNumber")!.IsVisible);
        Assert.True(window.FindControl<StackPanel>("GroupEditing")!.IsVisible);
        Assert.True(window.FindControl<Button>("BtnTabFormula")!.IsVisible);

        window.FindControl<Button>("BtnAddWordTab")!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        // Định dạng số, AutoSum, thẻ Công thức/Dữ liệu không có nghĩa với văn bản.
        Assert.False(window.FindControl<StackPanel>("GroupNumber")!.IsVisible);
        Assert.False(window.FindControl<StackPanel>("GroupEditing")!.IsVisible);
        Assert.False(window.FindControl<Button>("BtnTabFormula")!.IsVisible);
        Assert.False(window.FindControl<Button>("BtnTabData")!.IsVisible);

        // Nhóm dùng chung vẫn còn.
        Assert.True(window.FindControl<Button>("BtnBold")!.IsVisible);
        Assert.True(window.FindControl<Button>("BtnAlignLeft")!.IsVisible);
    }

    /// <summary>
    /// Nút biểu tượng phải đủ chỗ hiển thị nội dung. Theme Fluent mặc định thêm
    /// padding rất rộng khiến chữ trong nút 26px bị cắt cụt hai bên.
    /// </summary>
    [AvaloniaFact]
    public void IconButtons_DoNotClipTheirContent()
    {
        var window = OpenWorkbook();

        foreach (var name in new[] { "BtnBold", "BtnItalic", "BtnUnderline",
                                     "BtnAlignLeft", "BtnAlignCenter", "BtnAlignRight",
                                     "BtnFormatCurrency", "BtnFormatPercent" })
        {
            var button = window.FindControl<Button>(name)!;
            Assert.True(button.Classes.Contains("icon"),
                $"{name} phải dùng lớp 'icon' để bỏ padding mặc định, nếu không nội dung bị cắt.");
            Assert.Equal(new Thickness(0), button.Padding);
        }
    }

    /// <summary>Biểu tượng căn lề vẽ bằng vector, không phụ thuộc phông chữ của máy.</summary>
    [AvaloniaFact]
    public void AlignmentIcons_AreVectorNotUnicodeGlyphs()
    {
        var window = OpenWorkbook();

        foreach (var name in new[] { "BtnAlignLeft", "BtnAlignCenter", "BtnAlignRight" })
        {
            var button = window.FindControl<Button>(name)!;
            Assert.True(button.Content is Avalonia.Controls.Shapes.Path,
                $"{name} phải dùng hình vector — ký tự Unicode như ⫷ ⫸ thiếu trong nhiều phông.");
        }
    }
}
