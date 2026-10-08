using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Wocel.Core.Models;
using Wocel.Shell.Controls;
using Xunit;

namespace Wocel.Tests;

/// <summary>Gợi ý công thức khi gõ trong ô của lưới — đúng thao tác người dùng làm.</summary>
public class FormulaSuggestionTests
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

    private static void Type(Window window, string text)
    {
        foreach (char c in text) window.KeyTextInput(c.ToString());
    }

    [AvaloniaFact]
    public void TypingEqualsSu_ShowsSumSuggestion()
    {
        var (window, grid) = CreateGrid();

        Type(window, "=SU");

        Assert.True(grid.IsEditing);
        Assert.True(grid.Suggestions.IsOpen, "Gõ =SU phải hiện danh sách gợi ý.");
        Assert.Equal("SUM", grid.Suggestions.SelectedFunction?.Name);
        Assert.Contains(grid.Suggestions.Suggestions, f => f.Name == "SUMIF");
    }

    [AvaloniaFact]
    public void PressingTab_InsertsSelectedFunction()
    {
        var (window, grid) = CreateGrid();

        Type(window, "=SU");
        window.KeyPress(Key.Tab, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);

        Assert.Equal("=SUM(", grid.EditorText);
        Assert.False(grid.Suggestions.IsOpen);
        Assert.True(grid.IsEditing, "Tab để chọn hàm thì không được thoát khỏi ô đang nhập.");
    }

    [AvaloniaFact]
    public void ArrowDownThenEnter_PicksSecondSuggestion()
    {
        var (window, grid) = CreateGrid();

        Type(window, "=SU");
        var second = grid.Suggestions.Suggestions[1].Name;

        window.KeyPress(Key.Down, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
        window.KeyPress(Key.Enter, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);

        Assert.Equal($"={second}(", grid.EditorText);
    }

    [AvaloniaFact]
    public void Escape_ClosesSuggestionsButKeepsEditing()
    {
        var (window, grid) = CreateGrid();

        Type(window, "=SU");
        window.KeyPress(Key.Escape, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);

        Assert.False(grid.Suggestions.IsOpen);
        Assert.True(grid.IsEditing, "Esc lần đầu chỉ đóng gợi ý, chưa huỷ ô đang nhập.");

        window.KeyPress(Key.Escape, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
        Assert.False(grid.IsEditing, "Esc lần hai mới huỷ ô đang nhập.");
    }

    [AvaloniaFact]
    public void PlainText_DoesNotTriggerSuggestions()
    {
        var (window, grid) = CreateGrid();

        Type(window, "SUM");

        Assert.True(grid.IsEditing);
        Assert.False(grid.Suggestions.IsOpen, "Chưa có dấu = thì không phải công thức, không gợi ý.");
    }

    [AvaloniaFact]
    public void SuggestionsWork_ForNestedArgument()
    {
        var (window, grid) = CreateGrid();

        Type(window, "=IF(A1>1,AV");

        Assert.True(grid.Suggestions.IsOpen);
        Assert.Equal("AVERAGE", grid.Suggestions.SelectedFunction?.Name);

        window.KeyPress(Key.Tab, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
        Assert.Equal("=IF(A1>1,AVERAGE(", grid.EditorText);
    }

    [AvaloniaFact]
    public void CompletedFormula_CommitsAndCalculates()
    {
        var (window, grid) = CreateGrid();
        grid.Worksheet!.SetValue("A1", 10.0);
        grid.Worksheet!.SetValue("A2", 32.0);

        string? committed = null;
        grid.CellValueCommitted += (_, value) => committed = value;

        window.MouseMove(new Avalonia.Point(200, 200));
        window.MouseDown(new Avalonia.Point(200, 200), MouseButton.Left);
        window.MouseUp(new Avalonia.Point(200, 200), MouseButton.Left);

        Type(window, "=SU");
        window.KeyPress(Key.Tab, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
        Type(window, "A1:A2)");
        window.KeyPress(Key.Enter, RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);

        Assert.Equal("=SUM(A1:A2)", committed);
    }
}
