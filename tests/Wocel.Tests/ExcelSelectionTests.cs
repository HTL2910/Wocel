using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Wocel.Core.Models;
using Wocel.Shell.Controls;
using Xunit;

namespace Wocel.Tests;

/// <summary>Chọn nhiều ô bằng Shift, Ctrl/Cmd, kéo chuột và tiêu đề dòng/cột.</summary>
public class ExcelSelectionTests
{
    private static (Window window, ExcelCanvasGrid grid) CreateGrid()
    {
        var grid = new ExcelCanvasGrid { Worksheet = new SpreadsheetWorksheet { Name = "Sheet1" } };
        var window = new Window { Content = grid, Width = 1000, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        grid.Focus();
        return (window, grid);
    }

    private static Point CellCenter(int row, int col) => new(
        ExcelCanvasGrid.HeaderWidth + (col - 1) * ExcelCanvasGrid.DefaultColWidth + ExcelCanvasGrid.DefaultColWidth / 2,
        ExcelCanvasGrid.HeaderHeight + (row - 1) * ExcelCanvasGrid.DefaultRowHeight + ExcelCanvasGrid.DefaultRowHeight / 2);

    private static void Click(Window window, Point point, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.MouseMove(point, modifiers);
        window.MouseDown(point, MouseButton.Left, modifiers);
        window.MouseUp(point, MouseButton.Left, modifiers);
    }

    // ── Shift ────────────────────────────────────────────────────────────
    [AvaloniaFact]
    public void ShiftClick_SelectsRectangle()
    {
        var (window, grid) = CreateGrid();

        Click(window, CellCenter(2, 2));
        Click(window, CellCenter(5, 4), RawInputModifiers.Shift);

        var range = grid.ActiveRange;
        Assert.Equal(2, range.MinRow);
        Assert.Equal(5, range.MaxRow);
        Assert.Equal(2, range.MinCol);
        Assert.Equal(4, range.MaxCol);
        Assert.Equal(12, grid.SelectedCellCount);
        Assert.Equal("B2:D5", range.ToRangeAddress());
    }

    [AvaloniaFact]
    public void ShiftArrows_ExtendSelection()
    {
        var (window, grid) = CreateGrid();
        grid.SelectCell(3, 3);

        window.KeyPress(Key.Down, RawInputModifiers.Shift);
        window.KeyPress(Key.Down, RawInputModifiers.Shift);
        window.KeyPress(Key.Right, RawInputModifiers.Shift);

        Assert.Equal(6, grid.SelectedCellCount);      // 3 dòng × 2 cột
        Assert.Equal("C3:D5", grid.ActiveRange.ToRangeAddress());
    }

    [AvaloniaFact]
    public void ArrowWithoutShift_CollapsesBackToOneCell()
    {
        var (window, grid) = CreateGrid();
        grid.SelectCell(2, 2);

        window.KeyPress(Key.Down, RawInputModifiers.Shift);
        Assert.Equal(2, grid.SelectedCellCount);

        window.KeyPress(Key.Down, RawInputModifiers.None);
        Assert.Equal(1, grid.SelectedCellCount);
    }

    // ── Ctrl / Cmd ───────────────────────────────────────────────────────
    [AvaloniaFact]
    public void CtrlClick_AddsSeparateRange()
    {
        var (window, grid) = CreateGrid();

        Click(window, CellCenter(1, 1));
        Click(window, CellCenter(3, 3), RawInputModifiers.Control);
        Click(window, CellCenter(5, 5), RawInputModifiers.Control);

        Assert.Equal(3, grid.SelectedRanges.Count);
        Assert.Equal(3, grid.SelectedCellCount);
        Assert.Contains("A1", grid.SelectedAddresses);
        Assert.Contains("C3", grid.SelectedAddresses);
        Assert.Contains("E5", grid.SelectedAddresses);
    }

    [AvaloniaFact]
    public void MetaClick_WorksLikeCtrlOnMac()
    {
        var (window, grid) = CreateGrid();

        Click(window, CellCenter(1, 1));
        Click(window, CellCenter(4, 2), RawInputModifiers.Meta);

        Assert.Equal(2, grid.SelectedRanges.Count);
        Assert.Equal(2, grid.SelectedCellCount);
    }

    [AvaloniaFact]
    public void CtrlA_SelectsWholeSheet()
    {
        var (window, grid) = CreateGrid();
        grid.TotalRows = 20;
        grid.TotalCols = 5;

        window.KeyPress(Key.A, RawInputModifiers.Control);

        Assert.Equal(100, grid.SelectedCellCount);
    }

    // ── Kéo chuột ────────────────────────────────────────────────────────
    [AvaloniaFact]
    public void Dragging_SelectsRange()
    {
        var (window, grid) = CreateGrid();

        window.MouseMove(CellCenter(2, 2));
        window.MouseDown(CellCenter(2, 2), MouseButton.Left);
        window.MouseMove(CellCenter(4, 3));
        window.MouseUp(CellCenter(4, 3), MouseButton.Left);

        Assert.Equal("B2:C4", grid.ActiveRange.ToRangeAddress());
        Assert.Equal(6, grid.SelectedCellCount);
    }

    // ── Tiêu đề dòng / cột ───────────────────────────────────────────────
    [AvaloniaFact]
    public void ClickingRowHeader_SelectsEntireRow()
    {
        var (window, grid) = CreateGrid();
        grid.TotalCols = 10;

        Click(window, new Point(20, ExcelCanvasGrid.HeaderHeight + 2 * ExcelCanvasGrid.DefaultRowHeight + 10));

        Assert.Equal(10, grid.SelectedCellCount);
        Assert.Equal(3, grid.ActiveRange.MinRow);
        Assert.Equal(3, grid.ActiveRange.MaxRow);
    }

    [AvaloniaFact]
    public void ClickingColumnHeader_SelectsEntireColumn()
    {
        var (window, grid) = CreateGrid();
        grid.TotalRows = 30;

        Click(window, new Point(ExcelCanvasGrid.HeaderWidth + ExcelCanvasGrid.DefaultColWidth + 10, 10));

        Assert.Equal(30, grid.SelectedCellCount);
        Assert.Equal(2, grid.ActiveRange.MinCol);
    }

    [AvaloniaFact]
    public void ClickingCornerBox_SelectsEverything()
    {
        var (window, grid) = CreateGrid();
        grid.TotalRows = 12;
        grid.TotalCols = 4;

        Click(window, new Point(10, 10));

        Assert.Equal(48, grid.SelectedCellCount);
    }

    // ── Xoá cả vùng ──────────────────────────────────────────────────────
    [AvaloniaFact]
    public void Delete_ClearsEveryCellInSelection()
    {
        var (window, grid) = CreateGrid();
        var cleared = new List<string>();
        grid.CellValueCommitted += (a1, value) => { if (value.Length == 0) cleared.Add(a1); };

        Click(window, CellCenter(1, 1));
        Click(window, CellCenter(2, 2), RawInputModifiers.Shift);
        window.KeyPress(Key.Delete, RawInputModifiers.None);

        Assert.Equal(4, cleared.Count);
        Assert.Contains("A1", cleared);
        Assert.Contains("B2", cleared);
    }

    // ── Nhãn hiển thị ────────────────────────────────────────────────────
    [AvaloniaFact]
    public void SelectionLabel_DescribesWhatIsSelected()
    {
        var (window, grid) = CreateGrid();

        Click(window, CellCenter(2, 2));
        Assert.Equal("B2", grid.SelectionLabel);

        Click(window, CellCenter(4, 3), RawInputModifiers.Shift);
        Assert.Equal("B2:C4", grid.SelectionLabel);

        Click(window, CellCenter(7, 7), RawInputModifiers.Control);
        Assert.Contains("vùng", grid.SelectionLabel);
        Assert.Contains("7 ô", grid.SelectionLabel);
    }

    [AvaloniaFact]
    public void SelectionChanged_FiresForConsumers()
    {
        var (window, grid) = CreateGrid();
        int count = 0;
        grid.SelectionChanged += () => count++;

        Click(window, CellCenter(1, 1));
        Click(window, CellCenter(3, 3), RawInputModifiers.Shift);

        Assert.True(count >= 2, $"Sự kiện phải kích hoạt ít nhất 2 lần, thực tế {count}");
    }

    [AvaloniaFact]
    public void CopyShortcut_RaisesRequest()
    {
        var (window, grid) = CreateGrid();
        bool copied = false, cut = false, pasted = false;
        grid.CopyRequested += () => copied = true;
        grid.CutRequested += () => cut = true;
        grid.PasteRequested += () => pasted = true;

        window.KeyPress(Key.C, RawInputModifiers.Control);
        window.KeyPress(Key.X, RawInputModifiers.Control);
        window.KeyPress(Key.V, RawInputModifiers.Control);

        Assert.True(copied && cut && pasted);
    }
}
