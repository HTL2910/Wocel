using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Wocel.Excel.Sessions;
using Wocel.Shell.Controls;
using Xunit;

namespace Wocel.Tests;

/// <summary>
/// Mô phỏng đúng đường đi thật: gõ trên lưới → ghi vào sổ tính → tính lại → hiển thị.
/// Đây là luồng mà MainWindow nối với nhau.
/// </summary>
public class ExcelEndToEndTests
{
    private static (Window window, ExcelCanvasGrid grid, ExcelDocumentSession session) CreateWorkbook()
    {
        var session = new ExcelDocumentSession("thu.xlsx");
        var sheet = session.Document.GetOrCreateActiveSheet();

        var grid = new ExcelCanvasGrid { Worksheet = sheet };

        // Nối y hệt MainWindow.OnCanvasCellValueCommitted
        grid.CellValueCommitted += (a1, value) =>
        {
            sheet.SetValue(a1, value);
            session.Recalculate();
            grid.InvalidateVisual();
        };

        var window = new Window { Content = grid, Width = 1000, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        grid.Focus();

        return (window, grid, session);
    }

    private static void Type(Window window, string text)
    {
        foreach (char c in text) window.KeyTextInput(c.ToString());
    }

    private static string Display(ExcelDocumentSession session, string a1) =>
        session.Document.GetOrCreateActiveSheet().GetCell(a1).GetDisplayString();

    [AvaloniaFact]
    public void TypingNumbersThenFormula_Calculates()
    {
        var (window, grid, session) = CreateWorkbook();

        grid.SelectCell(1, 1);
        Type(window, "10");
        window.KeyPress(Key.Enter, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);   // A1 = 10, xuống A2

        Type(window, "32");
        window.KeyPress(Key.Enter, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);   // A2 = 32, xuống A3

        Type(window, "=SUM(A1:A2)");
        window.KeyPress(Key.Enter, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);

        Assert.Equal("10", Display(session, "A1"));
        Assert.Equal("32", Display(session, "A2"));
        Assert.Equal("42", Display(session, "A3"));
    }

    [AvaloniaFact]
    public void FormulaTypedWithSuggestionAccepted_Calculates()
    {
        var (window, grid, session) = CreateWorkbook();

        grid.SelectCell(1, 1);
        Type(window, "8");
        window.KeyPress(Key.Enter, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
        Type(window, "9");
        window.KeyPress(Key.Enter, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);

        // Gõ =SU rồi Tab để chọn SUM từ danh sách gợi ý
        Type(window, "=SU");
        window.KeyPress(Key.Tab, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
        Type(window, "A1:A2)");
        window.KeyPress(Key.Enter, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);

        Assert.Equal("17", Display(session, "A3"));
    }

    [AvaloniaFact]
    public void CellReferenceAfterOpenParen_DoesNotHijackEnter()
    {
        var (window, grid, session) = CreateWorkbook();

        grid.SelectCell(1, 1);
        Type(window, "5");
        window.KeyPress(Key.Enter, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);

        // "=SUM(A" — chữ A ở đây là ô A1, gõ Enter phải ghi công thức chứ
        // không được biến thành hàm ABS/AND từ danh sách gợi ý.
        grid.SelectCell(3, 1);
        Type(window, "=SUM(A1)");
        window.KeyPress(Key.Enter, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);

        Assert.Equal("5", Display(session, "A3"));
    }

    [AvaloniaFact]
    public void EditingExistingCell_RecalculatesDependents()
    {
        var (window, grid, session) = CreateWorkbook();
        var sheet = session.Document.GetOrCreateActiveSheet();

        sheet.SetValue("A1", 10.0);
        sheet.SetValue("A2", "=A1*2");
        session.Recalculate();
        Assert.Equal("20", Display(session, "A2"));

        grid.SelectCell(1, 1);
        Type(window, "25");
        window.KeyPress(Key.Enter, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);

        Assert.Equal("50", Display(session, "A2"));
    }
}
