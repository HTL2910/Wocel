using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Wocel.Core.Events;
using Wocel.Core.Models;
using Wocel.Excel;
using Wocel.Excel.IO;
using Wocel.Excel.Sessions;
using Wocel.Shell.Controls;
using Wocel.Shell.ViewModels;
using Wocel.Shell.Views;
using Xunit;
using ModuleRegistry = Wocel.Shell.Services.ModuleRegistry;

namespace Wocel.Tests;

/// <summary>Cỡ chữ và phông trong bảng tính: áp cho ô, vẽ ra, và còn nguyên khi lưu.</summary>
public class CellFormattingTests
{
    private static (MainWindow window, ExcelDocumentSession session, ExcelCanvasGrid grid) OpenWorkbook()
    {
        var registry = new ModuleRegistry();
        registry.RegisterModule(new Wocel.Word.WordModule());
        registry.RegisterModule(new ExcelModule());

        var window = new MainWindow { DataContext = new ShellWorkspaceViewModel(registry, new EventBus()) };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.FindControl<Button>("BtnNewExcelBook")!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var vm = (ShellWorkspaceViewModel)window.DataContext!;
        return (window, (ExcelDocumentSession)vm.ActiveTab!.Session,
                window.FindControl<ExcelCanvasGrid>("MainExcelCanvasGrid")!);
    }

    // ── Qua đúng thanh công cụ ───────────────────────────────────────────
    [AvaloniaFact]
    public void FontSizeBox_ChangesSelectedCells()
    {
        var (window, session, grid) = OpenWorkbook();
        var sheet = session.Document.GetOrCreateActiveSheet();

        sheet.SetValue("C3", "a");
        grid.SelectCell(3, 3);

        var sizeBox = window.FindControl<ComboBox>("CboFontSize")!;
        sizeBox.SelectedItem = sizeBox.Items.OfType<ComboBoxItem>().First(i => i.Content?.ToString() == "24");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(24, sheet.GetCell("C3").Style?.FontSize);
    }

    [AvaloniaFact]
    public void FontSize_AppliesToWholeSelectedRange()
    {
        var (window, session, grid) = OpenWorkbook();
        var sheet = session.Document.GetOrCreateActiveSheet();

        foreach (var address in new[] { "A1", "B1", "A2", "B2" }) sheet.SetValue(address, "x");

        grid.SelectCell(1, 1);
        grid.ExtendSelectionTo(2, 2);

        var sizeBox = window.FindControl<ComboBox>("CboFontSize")!;
        sizeBox.SelectedItem = sizeBox.Items.OfType<ComboBoxItem>().First(i => i.Content?.ToString() == "18");
        Dispatcher.UIThread.RunJobs();

        foreach (var address in new[] { "A1", "B1", "A2", "B2" })
            Assert.Equal(18, sheet.GetCell(address).Style?.FontSize);
    }

    [AvaloniaFact]
    public void FontFamilyBox_ChangesSelectedCells()
    {
        var (window, session, grid) = OpenWorkbook();
        var sheet = session.Document.GetOrCreateActiveSheet();

        sheet.SetValue("A1", "chữ");
        grid.SelectCell(1, 1);

        var familyBox = window.FindControl<ComboBox>("CboFontFamily")!;
        familyBox.SelectedItem = familyBox.Items.OfType<ComboBoxItem>()
            .First(i => i.Content?.ToString() == "Times New Roman");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Times New Roman", sheet.GetCell("A1").Style?.FontFamily);
    }

    [AvaloniaFact]
    public void StepButtons_GrowAndShrinkCellFont()
    {
        var (window, session, grid) = OpenWorkbook();
        var sheet = session.Document.GetOrCreateActiveSheet();

        sheet.SetValue("A1", "x");
        grid.SelectCell(1, 1);

        window.FindControl<Button>("BtnFontSizeUp")!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(13, sheet.GetCell("A1").Style?.FontSize);   // 12 mặc định + 1

        window.FindControl<Button>("BtnFontSizeDown")!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(12, sheet.GetCell("A1").Style?.FontSize);
    }

    [AvaloniaFact]
    public void ToolbarShowsSizeOfSelectedCell()
    {
        var (window, session, grid) = OpenWorkbook();
        var sheet = session.Document.GetOrCreateActiveSheet();

        sheet.GetOrCreateStyle("B2").FontSize = 28;
        sheet.SetValue("B2", "to");

        grid.SelectCell(1, 1);
        Dispatcher.UIThread.RunJobs();
        grid.SelectCell(2, 2);
        Dispatcher.UIThread.RunJobs();

        var box = window.FindControl<ComboBox>("CboFontSize")!;
        Assert.Equal("28", (box.SelectedItem as ComboBoxItem)?.Content?.ToString());
    }

    /// <summary>Gõ giá trị mới vào ô không được làm mất định dạng đã đặt.</summary>
    [AvaloniaFact]
    public void TypingIntoFormattedCell_KeepsItsFormatting()
    {
        var (_, session, grid) = OpenWorkbook();
        var sheet = session.Document.GetOrCreateActiveSheet();

        var style = sheet.GetOrCreateStyle("A1");
        style.IsBold = true;
        style.FontSize = 20;

        sheet.SetValue("A1", "nội dung mới");

        Assert.True(sheet.GetCell("A1").Style?.IsBold);
        Assert.Equal(20, sheet.GetCell("A1").Style?.FontSize);
        Assert.Equal("nội dung mới", sheet.GetCell("A1").GetDisplayString());

        _ = grid;
    }

    [AvaloniaFact]
    public void ClearingCell_KeepsFormattingLikeExcel()
    {
        var (_, session, _) = OpenWorkbook();
        var sheet = session.Document.GetOrCreateActiveSheet();

        sheet.SetValue("B2", "xoá đi");
        sheet.GetOrCreateStyle("B2").FontSize = 18;

        sheet.SetValue("B2", "");

        Assert.Equal(string.Empty, sheet.GetCell("B2").GetDisplayString());
        Assert.Equal(18, sheet.GetCell("B2").Style?.FontSize);
    }

    [AvaloniaFact]
    public void RowHeightGrows_SoLargeTextIsNotClipped()
    {
        var (window, session, grid) = OpenWorkbook();
        var sheet = session.Document.GetOrCreateActiveSheet();

        double before = grid.RowHeight;

        sheet.SetValue("A1", "chữ to");
        grid.SelectCell(1, 1);

        var sizeBox = window.FindControl<ComboBox>("CboFontSize")!;
        sizeBox.SelectedItem = sizeBox.Items.OfType<ComboBoxItem>().First(i => i.Content?.ToString() == "36");
        Dispatcher.UIThread.RunJobs();

        Assert.True(grid.RowHeight > before,
            $"Dòng phải cao lên cho vừa chữ cỡ 36 (trước {before}, sau {grid.RowHeight}).");
    }

    [AvaloniaFact]
    public void UndoRestoresPreviousFontSize()
    {
        var (window, session, grid) = OpenWorkbook();
        var sheet = session.Document.GetOrCreateActiveSheet();

        sheet.SetValue("A1", "x");
        grid.SelectCell(1, 1);

        var sizeBox = window.FindControl<ComboBox>("CboFontSize")!;
        sizeBox.SelectedItem = sizeBox.Items.OfType<ComboBoxItem>().First(i => i.Content?.ToString() == "24");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(24, sheet.GetCell("A1").Style?.FontSize);

        grid.Focus();
        window.KeyPress(Avalonia.Input.Key.Z, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(sheet.GetCell("A1").Style?.FontSize is null or 12);
    }

    // ── Lưu ra .xlsx rồi mở lại ──────────────────────────────────────────
    [Fact]
    public void CellFormatting_SurvivesXlsxRoundTrip()
    {
        var document = new SpreadsheetDocument();
        var sheet = document.GetOrCreateActiveSheet();

        sheet.SetValue("A1", "Tiêu đề");
        var heading = sheet.GetOrCreateStyle("A1");
        heading.IsBold = true;
        heading.FontSize = 20;
        heading.FontFamily = "Times New Roman";
        heading.HorizontalAlignment = "Center";

        sheet.SetValue("B1", 1500000.0);
        var money = sheet.GetOrCreateStyle("B1");
        money.NumberFormat = "currency";
        money.TextColor = "#C00000";

        sheet.SetValue("C1", "nghiêng");
        sheet.GetOrCreateStyle("C1").IsItalic = true;

        using var buffer = new MemoryStream();
        XlsxWriter.Write(document, buffer);
        buffer.Position = 0;

        var reloaded = XlsxReader.Read(buffer).GetOrCreateActiveSheet();

        var reloadedHeading = reloaded.GetCell("A1").Style;
        Assert.True(reloadedHeading?.IsBold);
        Assert.Equal(20, reloadedHeading?.FontSize);
        Assert.Equal("Times New Roman", reloadedHeading?.FontFamily);
        Assert.Equal("Center", reloadedHeading?.HorizontalAlignment);

        Assert.Equal("currency", reloaded.GetCell("B1").Style?.NumberFormat);
        Assert.True(reloaded.GetCell("C1").Style?.IsItalic);
    }

    [Fact]
    public void PlainCells_StayWithoutStyleAfterRoundTrip()
    {
        var document = new SpreadsheetDocument();
        var sheet = document.GetOrCreateActiveSheet();
        sheet.SetValue("A1", "thường");

        using var buffer = new MemoryStream();
        XlsxWriter.Write(document, buffer);
        buffer.Position = 0;

        var reloaded = XlsxReader.Read(buffer).GetOrCreateActiveSheet();
        Assert.Null(reloaded.GetCell("A1").Style);
        Assert.Equal("thường", reloaded.GetCell("A1").GetDisplayString());
    }

    /// <summary>
    /// Ô cỡ chữ phải là ComboBox: bấm vào là mở danh sách ngay.
    /// AutoCompleteBox chỉ mở khi gõ nên người dùng bấm vào không thấy gì.
    /// </summary>
    [AvaloniaFact]
    public void FontSizeBox_IsADropDownWithUsableSizes()
    {
        var (window, _, _) = OpenWorkbook();
        var box = window.FindControl<ComboBox>("CboFontSize");

        Assert.True(box != null, "Ô cỡ chữ phải là ComboBox để bấm vào là mở danh sách.");

        var sizes = box!.Items.OfType<ComboBoxItem>()
            .Select(i => int.Parse(i.Content!.ToString()!))
            .ToList();

        Assert.Contains(8, sizes);
        Assert.Contains(24, sizes);
        Assert.Contains(72, sizes);
        Assert.True(box.SelectedIndex >= 0, "Phải có sẵn một cỡ được chọn, không để trống.");
    }

    [AvaloniaFact]
    public void FontSizeBox_FollowsTheSelectedCell()
    {
        var (window, session, grid) = OpenWorkbook();
        var sheet = session.Document.GetOrCreateActiveSheet();
        var box = window.FindControl<ComboBox>("CboFontSize")!;

        sheet.SetValue("A1", "nhỏ");
        sheet.SetValue("B1", "to");
        sheet.GetOrCreateStyle("B1").FontSize = 24;

        grid.SelectCell(1, 1);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("12", (box.SelectedItem as ComboBoxItem)?.Content?.ToString());

        grid.SelectCell(1, 2);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("24", (box.SelectedItem as ComboBoxItem)?.Content?.ToString());
    }
}
