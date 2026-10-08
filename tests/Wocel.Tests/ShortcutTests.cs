using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Wocel.Core.Events;
using Wocel.Excel;
using Wocel.Excel.Sessions;
using Wocel.Shell.Controls;
using Wocel.Shell.Services;
using Wocel.Shell.ViewModels;
using Wocel.Shell.Views;
using Wocel.Word.Sessions;
using Xunit;

namespace Wocel.Tests;

/// <summary>Tổ hợp phím cơ bản phải chạy trong cả bảng tính lẫn trình soạn thảo văn bản.</summary>
public class ShortcutTests
{
    private static (MainWindow window, ShellWorkspaceViewModel vm) Open(bool spreadsheet)
    {
        var registry = new ModuleRegistry();
        registry.RegisterModule(new Wocel.Word.WordModule());
        registry.RegisterModule(new ExcelModule());

        var viewModel = new ShellWorkspaceViewModel(registry, new EventBus());
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.FindControl<Button>(spreadsheet ? "BtnNewExcelBook" : "BtnNewWordDoc")!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        return (window, viewModel);
    }

    // ── Bảng tính ────────────────────────────────────────────────────────
    [AvaloniaFact]
    public void Excel_CtrlZ_UndoesCellEdit()
    {
        var (window, vm) = Open(spreadsheet: true);
        var grid = window.FindControl<ExcelCanvasGrid>("MainExcelCanvasGrid")!;
        var sheet = ((ExcelDocumentSession)vm.ActiveTab!.Session).Document.GetOrCreateActiveSheet();

        grid.SelectCell(1, 1);
        foreach (char c in "123") window.KeyTextInput(c.ToString());
        window.KeyPress(Key.Enter, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
        Assert.Equal("123", sheet.GetCell("A1").GetDisplayString());

        grid.Focus();
        window.KeyPress(Key.Z, RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(string.Empty, sheet.GetCell("A1").GetDisplayString());
    }

    [AvaloniaFact]
    public void Excel_CtrlY_RedoesAfterUndo()
    {
        var (window, vm) = Open(spreadsheet: true);
        var grid = window.FindControl<ExcelCanvasGrid>("MainExcelCanvasGrid")!;
        var sheet = ((ExcelDocumentSession)vm.ActiveTab!.Session).Document.GetOrCreateActiveSheet();

        grid.SelectCell(1, 1);
        foreach (char c in "xin chao") window.KeyTextInput(c.ToString());
        window.KeyPress(Key.Enter, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);

        grid.Focus();
        window.KeyPress(Key.Z, RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(string.Empty, sheet.GetCell("A1").GetDisplayString());

        window.KeyPress(Key.Y, RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("xin chao", sheet.GetCell("A1").GetDisplayString());
    }

    [AvaloniaFact]
    public void Excel_UndoRestoresFormatting()
    {
        var (window, vm) = Open(spreadsheet: true);
        var grid = window.FindControl<ExcelCanvasGrid>("MainExcelCanvasGrid")!;
        var sheet = ((ExcelDocumentSession)vm.ActiveTab!.Session).Document.GetOrCreateActiveSheet();

        sheet.SetValue("B2", "tiêu đề");
        grid.SelectCell(2, 2);

        window.FindControl<Button>("BtnBold")!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(sheet.GetCell("B2").Style?.IsBold);

        grid.Focus();
        window.KeyPress(Key.Z, RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(sheet.GetCell("B2").Style?.IsBold != true);
    }

    // ── Văn bản ──────────────────────────────────────────────────────────
    [AvaloniaFact]
    public void Word_CtrlZ_UndoesTyping()
    {
        var (window, vm) = Open(spreadsheet: false);
        var editor = window.FindControl<WordCanvasEditor>("WordDocumentEditor")!;
        var document = ((WordDocumentSession)vm.ActiveTab!.Session).Document;

        editor.Focus();
        foreach (char c in "abc") window.KeyTextInput(c.ToString());
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("abc", document.ToPlainText());

        window.KeyPress(Key.Z, RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain("abc", document.ToPlainText());
    }

    [AvaloniaFact]
    public void Word_CtrlZ_UndoesFormatting()
    {
        var (window, vm) = Open(spreadsheet: false);
        var editor = window.FindControl<WordCanvasEditor>("WordDocumentEditor")!;
        var document = ((WordDocumentSession)vm.ActiveTab!.Session).Document;

        editor.SetPlainText("chữ thử");
        editor.SelectAll();

        window.FindControl<Button>("BtnBold")!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(document.Blocks[0].Inlines, r => r.IsBold);

        editor.Focus();
        window.KeyPress(Key.Z, RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(document.Blocks[0].Inlines, r => r.IsBold);
    }

    // ── Bảng tạm ─────────────────────────────────────────────────────────
    [AvaloniaFact]
    public void Word_ClipboardShortcuts_AreHandled()
    {
        var (window, _) = Open(spreadsheet: false);
        var editor = window.FindControl<WordCanvasEditor>("WordDocumentEditor")!;

        bool copied = false, cut = false, pasted = false;
        editor.CopyRequested += () => copied = true;
        editor.CutRequested += () => cut = true;
        editor.PasteRequested += () => pasted = true;

        editor.Focus();
        window.KeyPress(Key.C, RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
        window.KeyPress(Key.X, RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
        window.KeyPress(Key.V, RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);

        Assert.True(copied, "Ctrl+C trong trình soạn thảo văn bản chưa được xử lý.");
        Assert.True(cut, "Ctrl+X trong trình soạn thảo văn bản chưa được xử lý.");
        Assert.True(pasted, "Ctrl+V trong trình soạn thảo văn bản chưa được xử lý.");
    }

    [AvaloniaFact]
    public void Word_CutRemovesSelectedText()
    {
        var (window, vm) = Open(spreadsheet: false);
        var editor = window.FindControl<WordCanvasEditor>("WordDocumentEditor")!;
        var document = ((WordDocumentSession)vm.ActiveTab!.Session).Document;

        editor.SetPlainText("giữ lại phần này");
        editor.SetCaret(0, 0, extend: false);
        editor.SetCaret(0, 8, extend: true);

        editor.Focus();
        window.KeyPress(Key.X, RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("phần này", document.ToPlainText().Trim());
    }

    // ── Đóng thẻ ─────────────────────────────────────────────────────────
    [AvaloniaFact]
    public void CtrlW_ClosesActiveTab()
    {
        var (window, vm) = Open(spreadsheet: true);
        Assert.Single(vm.Tabs);

        window.FindControl<Button>("BtnAddWordTab")!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, vm.Tabs.Count);

        window.KeyPress(Key.W, RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(vm.Tabs);
    }
}
