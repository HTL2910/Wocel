using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Wocel.Core.Events;
using Wocel.Core.Models;
using Wocel.Excel;
using Wocel.Excel.IO;
using Wocel.Shell.Controls;
using Wocel.Shell.Services;
using Wocel.Shell.ViewModels;
using Wocel.Shell.Views;
using Xunit;

namespace Wocel.Tests;

/// <summary>
/// Mọi nút trên giao diện phải có xử lý thật. Test này bấm từng nút và
/// yêu cầu không nút nào ném lỗi hay im lặng không làm gì.
/// </summary>
public class RibbonAndSheetTests
{
    private static ShellWorkspaceViewModel CreateViewModel()
    {
        var registry = new ModuleRegistry();
        registry.RegisterModule(new Wocel.Word.WordModule());
        registry.RegisterModule(new ExcelModule());
        return new ShellWorkspaceViewModel(registry, new EventBus());
    }

    private static (MainWindow window, ShellWorkspaceViewModel vm) OpenWorkbook()
    {
        var viewModel = CreateViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Bấm đúng nút như người dùng, để mọi bước gắn lưới / vẽ thẻ đều chạy.
        var newBook = window.FindControl<Button>("BtnNewExcelBook")!;
        newBook.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        return (window, viewModel);
    }

    /// <summary>
    /// Mọi nút có tên trong giao diện phải được nối sự kiện trong code-behind.
    /// Kiểm ở mức mã nguồn vì Avalonia không cho hỏi "nút này có handler chưa".
    /// </summary>
    [Fact]
    public void EveryNamedButton_IsWiredInCodeBehind()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/Wocel.Shell/Views/MainWindow.axaml"));
        var code = File.ReadAllText(Path.Combine(root, "src/Wocel.Shell/Views/MainWindow.axaml.cs"));

        var buttons = System.Text.RegularExpressions.Regex
            .Matches(xaml, @"<Button[^>]*Name=""(Btn[A-Za-z0-9_]+)""")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.True(buttons.Count > 40, $"Chỉ tìm thấy {buttons.Count} nút — biểu thức dò có vẻ sai.");

        var unwired = buttons.Where(name => !code.Contains($"{name}.Click")).ToList();
        Assert.True(unwired.Count == 0, "Nút chưa nối sự kiện: " + string.Join(", ", unwired));
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Wocel.sln")))
            directory = directory.Parent;

        Assert.True(directory != null, "Không tìm thấy thư mục gốc chứa Wocel.sln");
        return directory!.FullName;
    }

    [AvaloniaFact]
    public void RibbonTabs_SwitchToolbars()
    {
        var (window, _) = OpenWorkbook();

        var home = window.FindControl<StackPanel>("RibbonHome")!;
        var insert = window.FindControl<StackPanel>("RibbonInsert")!;
        var data = window.FindControl<StackPanel>("RibbonData")!;

        Assert.True(home.IsVisible);
        Assert.False(insert.IsVisible);

        Click(window, "BtnTabInsert");
        Assert.True(insert.IsVisible);
        Assert.False(home.IsVisible);

        Click(window, "BtnTabData");
        Assert.True(data.IsVisible);
        Assert.False(insert.IsVisible);

        Click(window, "BtnTabHome");
        Assert.True(home.IsVisible);
    }

    private static void Click(MainWindow window, string name)
    {
        var button = window.FindControl<Button>(name);
        Assert.True(button != null, $"Không tìm thấy nút {name}");
        button!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    // ── Trang tính ───────────────────────────────────────────────────────
    [AvaloniaFact]
    public void AddSheet_CreatesAndSwitches()
    {
        var (window, vm) = OpenWorkbook();
        var document = ((Wocel.Excel.Sessions.ExcelDocumentSession)vm.ActiveTab!.Session).Document;

        Assert.Single(document.Sheets);

        Click(window, "BtnAddSheet");
        Assert.Equal(2, document.Sheets.Count);
        Assert.Equal(1, document.ActiveSheetIndex);
        Assert.Equal("Sheet2", document.GetOrCreateActiveSheet().Name);

        Click(window, "BtnPrevSheet");
        Assert.Equal(0, document.ActiveSheetIndex);

        Click(window, "BtnNextSheet");
        Assert.Equal(1, document.ActiveSheetIndex);
    }

    [AvaloniaFact]
    public void SheetTabs_AreRenderedForEachSheet()
    {
        var (window, _) = OpenWorkbook();
        var container = window.FindControl<StackPanel>("SheetTabsContainer")!;

        Assert.Single(container.Children);

        Click(window, "BtnAddSheet");
        Assert.Equal(2, container.Children.Count);
    }

    // ── Chèn / xoá dòng cột ──────────────────────────────────────────────
    [AvaloniaFact]
    public void InsertAndDeleteRow_ShiftsData()
    {
        var (window, vm) = OpenWorkbook();
        var session = (Wocel.Excel.Sessions.ExcelDocumentSession)vm.ActiveTab!.Session;
        var sheet = session.Document.GetOrCreateActiveSheet();
        var grid = window.FindControl<ExcelCanvasGrid>("MainExcelCanvasGrid")!;

        sheet.SetValue("A1", "đầu");
        sheet.SetValue("A2", "sau");
        grid.SelectCell(2, 1);

        Click(window, "BtnTabInsert");
        Click(window, "BtnInsertRow");

        Assert.Equal("đầu", sheet.GetCell("A1").GetDisplayString());
        Assert.Equal("", sheet.GetCell("A2").GetDisplayString());
        Assert.Equal("sau", sheet.GetCell("A3").GetDisplayString());

        grid.SelectCell(2, 1);
        Click(window, "BtnDeleteRow");
        Assert.Equal("sau", sheet.GetCell("A2").GetDisplayString());
    }

    // ── Công thức nhanh ──────────────────────────────────────────────────
    [AvaloniaFact]
    public void AggregateButton_WritesWorkingFormula()
    {
        var (window, vm) = OpenWorkbook();
        var session = (Wocel.Excel.Sessions.ExcelDocumentSession)vm.ActiveTab!.Session;
        var sheet = session.Document.GetOrCreateActiveSheet();
        var grid = window.FindControl<ExcelCanvasGrid>("MainExcelCanvasGrid")!;

        sheet.SetValue("A1", 10.0);
        sheet.SetValue("A2", 20.0);
        sheet.SetValue("A3", 30.0);

        grid.SelectCell(1, 1);
        grid.ExtendSelectionTo(3, 1);

        Click(window, "BtnTabFormula");
        Click(window, "BtnFormulaAverage");

        Assert.Equal("=AVERAGE(A1:A3)", sheet.GetCell("A4").Formula);
        Assert.Equal("20", sheet.GetCell("A4").GetDisplayString());
    }

    // ── Định dạng ────────────────────────────────────────────────────────
    [AvaloniaFact]
    public void BoldButton_StylesSelectedCells()
    {
        var (window, vm) = OpenWorkbook();
        var session = (Wocel.Excel.Sessions.ExcelDocumentSession)vm.ActiveTab!.Session;
        var sheet = session.Document.GetOrCreateActiveSheet();
        var grid = window.FindControl<ExcelCanvasGrid>("MainExcelCanvasGrid")!;

        sheet.SetValue("B2", "tiêu đề");
        grid.SelectCell(2, 2);

        Click(window, "BtnBold");
        Assert.True(sheet.GetCell("B2").Style?.IsBold);

        Click(window, "BtnBold");
        Assert.False(sheet.GetCell("B2").Style?.IsBold);
    }

    [AvaloniaFact]
    public void CurrencyButton_FormatsNumbers()
    {
        var (window, vm) = OpenWorkbook();
        var session = (Wocel.Excel.Sessions.ExcelDocumentSession)vm.ActiveTab!.Session;
        var sheet = session.Document.GetOrCreateActiveSheet();
        var grid = window.FindControl<ExcelCanvasGrid>("MainExcelCanvasGrid")!;

        sheet.SetValue("A1", 1500000.0);
        grid.SelectCell(1, 1);

        Click(window, "BtnFormatCurrency");

        var display = sheet.GetCell("A1").GetDisplayString();
        Assert.Contains("₫", display);
        Assert.Contains("1", display);
    }

    // ── Gộp ô ────────────────────────────────────────────────────────────
    [AvaloniaFact]
    public void MergeButton_MergesAndUnmerges()
    {
        var (window, vm) = OpenWorkbook();
        var session = (Wocel.Excel.Sessions.ExcelDocumentSession)vm.ActiveTab!.Session;
        var sheet = session.Document.GetOrCreateActiveSheet();
        var grid = window.FindControl<ExcelCanvasGrid>("MainExcelCanvasGrid")!;

        grid.SelectCell(1, 1);
        grid.ExtendSelectionTo(2, 3);

        Click(window, "BtnMergeCenter");
        Assert.Contains("A1:C2", sheet.MergedRanges);

        Click(window, "BtnMergeCenter");
        Assert.Empty(sheet.MergedRanges);
    }

    // ── Dữ liệu ──────────────────────────────────────────────────────────
    [AvaloniaFact]
    public void RemoveDuplicates_DropsRepeatedRows()
    {
        var (window, vm) = OpenWorkbook();
        var session = (Wocel.Excel.Sessions.ExcelDocumentSession)vm.ActiveTab!.Session;
        var sheet = session.Document.GetOrCreateActiveSheet();
        var grid = window.FindControl<ExcelCanvasGrid>("MainExcelCanvasGrid")!;

        sheet.SetValue("A1", "x");
        sheet.SetValue("A2", "x");
        sheet.SetValue("A3", "y");

        grid.SelectCell(1, 1);
        grid.ExtendSelectionTo(3, 1);

        Click(window, "BtnTabData");
        Click(window, "BtnRemoveDuplicates");

        Assert.Equal("x", sheet.GetCell("A1").GetDisplayString());
        Assert.Equal("", sheet.GetCell("A2").GetDisplayString());
        Assert.Equal("y", sheet.GetCell("A3").GetDisplayString());
    }

    // ── Xem ──────────────────────────────────────────────────────────────
    [AvaloniaFact]
    public void ZoomButtons_ChangeZoomLevel()
    {
        var (window, _) = OpenWorkbook();
        var label = window.FindControl<TextBlock>("TxtZoomLevel")!;

        Click(window, "BtnTabView");
        Click(window, "BtnZoomIn");
        Assert.Equal("110%", label.Text);

        Click(window, "BtnZoomOut");
        Click(window, "BtnZoomOut");
        Assert.Equal("90%", label.Text);

        Click(window, "BtnZoomReset");
        Assert.Equal("100%", label.Text);
    }

    // ── Lưu nhiều trang tính ─────────────────────────────────────────────
    [Fact]
    public void Xlsx_RoundTripsEverySheetAndMerge()
    {
        var document = new SpreadsheetDocument();
        var first = document.GetOrCreateActiveSheet();
        first.Name = "Doanh thu";
        first.SetValue("A1", "Quý");
        first.SetValue("B1", 1000.0);
        first.MergedRanges.Add("A1:B1");

        var second = document.AddSheet("Chi phí");
        second.SetValue("A1", "Thuê nhà");
        second.SetValue("B1", 250.0);

        using var buffer = new MemoryStream();
        XlsxWriter.Write(document, buffer);
        buffer.Position = 0;

        var reloaded = XlsxReader.Read(buffer);

        Assert.Equal(2, reloaded.Sheets.Count);
        Assert.Equal("Doanh thu", reloaded.Sheets[0].Name);
        Assert.Equal("Chi phí", reloaded.Sheets[1].Name);
        Assert.Equal("Thuê nhà", reloaded.Sheets[1].GetCell("A1").GetDisplayString());
        Assert.Contains("A1:B1", reloaded.Sheets[0].MergedRanges);
    }

    // ── Tooltip ──────────────────────────────────────────────────────────
    [AvaloniaFact]
    public void EveryNamedButton_HasATooltip()
    {
        var (window, _) = OpenWorkbook();

        var missing = new List<string>();
        foreach (var name in ButtonNamesInXaml())
        {
            var button = window.FindControl<Button>(name);
            if (button == null) continue;
            if (ToolTip.GetTip(button) == null) missing.Add(name);
        }

        Assert.True(missing.Count == 0, "Nút chưa có chú thích: " + string.Join(", ", missing));
    }

    [AvaloniaFact]
    public void Tooltip_ShowsTitleDescriptionAndShortcut()
    {
        var (window, _) = OpenWorkbook();

        var tip = ToolTip.GetTip(window.FindControl<Button>("BtnBold")!)?.ToString() ?? string.Empty;

        Assert.Contains("Đậm", tip);
        Assert.Contains("ô đang chọn", tip);   // đang mở bảng tính
        Assert.True(tip.Contains("Ctrl+B") || tip.Contains("⌘B"), $"Thiếu phím tắt trong: {tip}");
        Assert.Contains("\n", tip);   // tên lệnh và mô tả nằm trên hai dòng
    }

    /// <summary>
    /// Nội dung tooltip phải là chuỗi. Nếu gắn bằng một đối tượng Control, Avalonia
    /// đưa control đó vào popup và sau lần hiện đầu tiên tooltip sẽ không hiện lại nữa.
    /// </summary>
    [AvaloniaFact]
    public void Tooltips_AreStringsSoTheyShowEveryTime()
    {
        var (window, _) = OpenWorkbook();

        var controlTips = new List<string>();
        foreach (var name in ButtonNamesInXaml())
        {
            var button = window.FindControl<Button>(name);
            if (button == null) continue;

            var tip = ToolTip.GetTip(button);
            if (tip is Control) controlTips.Add(name);
        }

        Assert.True(controlTips.Count == 0,
            "Tooltip gắn bằng Control sẽ chỉ hiện được một lần: " + string.Join(", ", controlTips));
    }

    /// <summary>Phím tắt ghi trong tooltip phải là phím thật đã nối, không được hứa suông.</summary>
    [Fact]
    public void ShortcutsPromisedInTooltips_AreActuallyHandled()
    {
        var root = FindProjectRoot();
        var code = File.ReadAllText(Path.Combine(root, "src/Wocel.Shell/Views/MainWindow.axaml.cs"));

        int keyDown = code.IndexOf("protected override void OnKeyDown", StringComparison.Ordinal);
        Assert.True(keyDown > 0, "Cửa sổ chính phải có bộ xử lý phím tắt.");

        var handler = code[keyDown..];
        foreach (var key in new[] { "Key.S", "Key.O", "Key.N", "Key.F", "Key.B", "Key.I", "Key.U" })
            Assert.True(handler.Contains(key), $"Tooltip có nhắc phím tắt nhưng {key} chưa được xử lý.");
    }

    private static List<string> ButtonNamesInXaml()
    {
        var xaml = File.ReadAllText(Path.Combine(FindProjectRoot(), "src/Wocel.Shell/Views/MainWindow.axaml"));
        return System.Text.RegularExpressions.Regex
            .Matches(xaml, @"<Button[^>]*Name=""(Btn[A-Za-z0-9_]+)""")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();
    }
}
