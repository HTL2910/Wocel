using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Wocel.Core.Events;
using Wocel.Excel;
using Wocel.Shell.Controls;
using Wocel.Shell.ViewModels;
using Wocel.Shell.Views;
using Xunit;
using ModuleRegistry = Wocel.Shell.Services.ModuleRegistry;

namespace Wocel.Tests;

/// <summary>
/// Cùng một nút nhưng làm việc khác nhau ở bảng tính và văn bản, nên chú thích
/// phải đổi theo loại tài liệu đang mở.
/// </summary>
public class ContextTooltipTests
{
    private static MainWindow Open()
    {
        var registry = new ModuleRegistry();
        registry.RegisterModule(new Wocel.Word.WordModule());
        registry.RegisterModule(new ExcelModule());

        var window = new MainWindow { DataContext = new ShellWorkspaceViewModel(registry, new EventBus()) };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Click(MainWindow window, string name)
    {
        window.FindControl<Button>(name)!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static string TipOf(MainWindow window, string name) =>
        ToolTip.GetTip(window.FindControl<Control>(name)!)?.ToString() ?? string.Empty;

    [AvaloniaFact]
    public void FormattingTooltips_DifferBetweenSheetAndDocument()
    {
        var window = Open();

        Click(window, "BtnNewExcelBook");
        var sheetTip = TipOf(window, "BtnBold");

        Click(window, "BtnAddWordTab");
        var wordTip = TipOf(window, "BtnBold");

        Assert.NotEqual(sheetTip, wordTip);
        Assert.Contains("ô đang chọn", sheetTip);
        Assert.Contains("bôi đen", wordTip);
    }

    [AvaloniaFact]
    public void AlignmentTooltips_TalkAboutCellsOrParagraphs()
    {
        var window = Open();

        Click(window, "BtnNewExcelBook");
        Assert.Contains("ô", TipOf(window, "BtnAlignLeft"));

        Click(window, "BtnAddWordTab");
        Assert.Contains("đoạn văn", TipOf(window, "BtnAlignLeft"));
    }

    [AvaloniaFact]
    public void ClipboardTooltips_MatchTheDocumentType()
    {
        var window = Open();

        Click(window, "BtnNewExcelBook");
        Assert.Contains("vùng ô", TipOf(window, "BtnCopy"));

        Click(window, "BtnAddWordTab");
        Assert.Contains("chữ", TipOf(window, "BtnCopy"));
    }

    [AvaloniaFact]
    public void SearchTooltip_NamesTheRightTarget()
    {
        var window = Open();

        Click(window, "BtnNewExcelBook");
        Assert.Contains("trang tính", TipOf(window, "TxtSearchBox"));

        Click(window, "BtnAddWordTab");
        Assert.Contains("tài liệu", TipOf(window, "TxtSearchBox"));
    }

    [AvaloniaFact]
    public void MergeButton_HiddenForDocuments()
    {
        var window = Open();

        Click(window, "BtnNewExcelBook");
        Assert.True(window.FindControl<Button>("BtnMergeCenter")!.IsVisible);

        Click(window, "BtnAddWordTab");
        Assert.False(window.FindControl<Button>("BtnMergeCenter")!.IsVisible);
    }

    /// <summary>Tooltip hứa "tìm trong tài liệu" thì tìm kiếm phải thật sự chạy được bên Word.</summary>
    [AvaloniaFact]
    public void SearchActuallyWorksInDocuments()
    {
        var window = Open();
        Click(window, "BtnNewWordDoc");

        var editor = window.FindControl<WordCanvasEditor>("WordDocumentEditor")!;
        editor.SetPlainText("dòng một\ncần tìm chỗ này\ndòng ba");

        Assert.True(editor.FindNext("cần tìm"), "Không tìm thấy chuỗi có trong tài liệu.");
        Assert.Equal("cần tìm", editor.SelectedText);

        Assert.False(editor.FindNext("khong-he-co"));
    }

    [AvaloniaFact]
    public void SearchInDocument_WrapsAroundToTheStart()
    {
        var window = Open();
        Click(window, "BtnNewWordDoc");

        var editor = window.FindControl<WordCanvasEditor>("WordDocumentEditor")!;
        editor.SetPlainText("alpha\nbeta\nalpha");

        Assert.True(editor.FindNext("alpha"));   // lần 1
        Assert.True(editor.FindNext("alpha"));   // lần 2 ở đoạn cuối
        Assert.True(editor.FindNext("alpha"));   // quay vòng về đầu
        Assert.Equal("alpha", editor.SelectedText);
    }
}
