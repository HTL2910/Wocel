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

/// <summary>
/// Kiểm tra thao tác bàn phím / chuột trên lưới Excel — đúng những gì người dùng
/// làm hằng ngày: bấm ô, gõ chữ, di chuyển bằng phím mũi tên, xoá ô.
/// </summary>
public class ExcelGridInteractionTests
{
    private static (Window window, ExcelCanvasGrid grid) CreateGrid()
    {
        var grid = new ExcelCanvasGrid
        {
            Worksheet = new SpreadsheetWorksheet { Name = "Sheet1" },
            TotalRows = 100,
            TotalCols = 26
        };

        var window = new Window { Content = grid, Width = 1000, Height = 600 };
        window.Show();

        // Bố cục phải hoàn tất thì việc bắt sự kiện chuột mới đúng toạ độ.
        Dispatcher.UIThread.RunJobs();
        grid.Focus();
        return (window, grid);
    }

    /// <summary>Tâm của ô (row, col) tính theo toạ độ pixel trong lưới.</summary>
    private static Point CellCenter(int row, int col) => new(
        ExcelCanvasGrid.HeaderWidth + (col - 1) * ExcelCanvasGrid.DefaultColWidth + ExcelCanvasGrid.DefaultColWidth / 2,
        ExcelCanvasGrid.HeaderHeight + (row - 1) * ExcelCanvasGrid.DefaultRowHeight + ExcelCanvasGrid.DefaultRowHeight / 2);

    [AvaloniaFact]
    public void ClickingCell_SelectsIt()
    {
        var (window, grid) = CreateGrid();

        window.MouseMove(CellCenter(3, 2));
        window.MouseDown(CellCenter(3, 2), MouseButton.Left);
        window.MouseUp(CellCenter(3, 2), MouseButton.Left);

        Assert.Equal(3, grid.SelectedRow);
        Assert.Equal(2, grid.SelectedCol);
    }

    [AvaloniaFact]
    public void ArrowKeys_MoveSelection()
    {
        var (window, grid) = CreateGrid();

        window.MouseMove(CellCenter(3, 3));
        window.MouseDown(CellCenter(3, 3), MouseButton.Left);
        window.MouseUp(CellCenter(3, 3), MouseButton.Left);

        window.KeyPress(Key.Down, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
        window.KeyPress(Key.Right, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);

        Assert.Equal(4, grid.SelectedRow);
        Assert.Equal(4, grid.SelectedCol);

        window.KeyPress(Key.Up, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
        window.KeyPress(Key.Left, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);

        Assert.Equal(3, grid.SelectedRow);
        Assert.Equal(3, grid.SelectedCol);
    }

    [AvaloniaFact]
    public void TypingCharacter_StartsEditingAndCommitsOnEnter()
    {
        var (window, grid) = CreateGrid();
        string? committedAddress = null;
        string? committedValue = null;
        grid.CellValueCommitted += (a1, value) => { committedAddress = a1; committedValue = value; };

        window.MouseMove(CellCenter(2, 1));
        window.MouseDown(CellCenter(2, 1), MouseButton.Left);
        window.MouseUp(CellCenter(2, 1), MouseButton.Left);

        window.KeyTextInput("1");
        window.KeyTextInput("2");
        window.KeyTextInput("3");

        Assert.True(grid.IsEditing, "Gõ ký tự phải mở ô nhập liệu như Excel.");

        window.KeyPress(Key.Enter, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);

        Assert.Equal("A2", committedAddress);
        Assert.Equal("123", committedValue);
        Assert.Equal(3, grid.SelectedRow);   // Enter nhảy xuống dòng dưới
    }

    [AvaloniaFact]
    public void F2_OpensEditorWithExistingValue()
    {
        var (window, grid) = CreateGrid();
        grid.Worksheet!.SetValue("B2", "xin chào");

        window.MouseMove(CellCenter(2, 2));
        window.MouseDown(CellCenter(2, 2), MouseButton.Left);
        window.MouseUp(CellCenter(2, 2), MouseButton.Left);
        window.KeyPress(Key.F2, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);

        Assert.True(grid.IsEditing);
        Assert.Equal("xin chào", grid.EditorText);
    }

    [AvaloniaFact]
    public void Delete_ClearsSelectedCell()
    {
        var (window, grid) = CreateGrid();
        grid.Worksheet!.SetValue("C3", "xoá tôi đi");

        string? committedValue = "chưa gọi";
        grid.CellValueCommitted += (_, value) => committedValue = value;

        window.MouseMove(CellCenter(3, 3));
        window.MouseDown(CellCenter(3, 3), MouseButton.Left);
        window.MouseUp(CellCenter(3, 3), MouseButton.Left);
        window.KeyPress(Key.Delete, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);

        Assert.Equal(string.Empty, committedValue);
    }

    [AvaloniaFact]
    public void Escape_CancelsEditWithoutCommitting()
    {
        var (window, grid) = CreateGrid();
        bool committed = false;
        grid.CellValueCommitted += (_, _) => committed = true;

        window.MouseMove(CellCenter(1, 1));
        window.MouseDown(CellCenter(1, 1), MouseButton.Left);
        window.MouseUp(CellCenter(1, 1), MouseButton.Left);
        window.KeyTextInput("abc");
        window.KeyPress(Key.Escape, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);

        Assert.False(grid.IsEditing);
        Assert.False(committed, "Nhấn Esc thì không được ghi giá trị.");
    }

    [AvaloniaFact]
    public void Tab_MovesRightAndCommits()
    {
        var (window, grid) = CreateGrid();
        string? committedAddress = null;
        grid.CellValueCommitted += (a1, _) => committedAddress = a1;

        window.MouseMove(CellCenter(5, 2));
        window.MouseDown(CellCenter(5, 2), MouseButton.Left);
        window.MouseUp(CellCenter(5, 2), MouseButton.Left);
        window.KeyTextInput("x");
        window.KeyPress(Key.Tab, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);

        Assert.Equal("B5", committedAddress);
        Assert.Equal(3, grid.SelectedCol);
    }
}
