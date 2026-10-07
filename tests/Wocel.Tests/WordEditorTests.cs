using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Wocel.Core.Models;
using Wocel.Shell.Controls;
using Wocel.Word.IO;
using Xunit;

namespace Wocel.Tests;

/// <summary>
/// Bôi đen rồi bấm Đậm / đổi cỡ chữ phải chỉ tác động đúng phần được chọn,
/// và định dạng phải còn nguyên khi lưu ra .docx rồi mở lại.
/// </summary>
public class WordEditorTests
{
    private static (Window window, WordCanvasEditor editor, DocumentDocument doc) CreateEditor(string text = "")
    {
        var document = new DocumentDocument();
        var editor = new WordCanvasEditor { Document = document };

        var window = new Window { Content = editor, Width = 900, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        editor.Focus();

        if (text.Length > 0) editor.SetPlainText(text);
        return (window, editor, document);
    }

    private static void Type(Window window, string text)
    {
        foreach (char c in text) window.KeyTextInput(c.ToString());
    }

    private static List<TextRun> Runs(DocumentDocument doc, int block = 0) => doc.Blocks[block].Inlines;

    // ── Gõ chữ ───────────────────────────────────────────────────────────
    [AvaloniaFact]
    public void Typing_AddsTextToDocument()
    {
        var (window, editor, doc) = CreateEditor();

        Type(window, "Xin chào");

        Assert.Equal("Xin chào", doc.Blocks[0].Inlines[0].Text);
        Assert.Contains("Xin chào", editor.PlainText);
    }

    [AvaloniaFact]
    public void Enter_SplitsIntoParagraphs()
    {
        var (window, _, doc) = CreateEditor();

        Type(window, "Dòng một");
        window.KeyPress(Key.Enter, RawInputModifiers.None);
        Type(window, "Dòng hai");

        Assert.Equal(2, doc.Blocks.Count);
        Assert.Equal("Dòng một", doc.Blocks[0].Inlines[0].Text);
        Assert.Equal("Dòng hai", doc.Blocks[1].Inlines[0].Text);
    }

    [AvaloniaFact]
    public void Backspace_RemovesCharacter()
    {
        var (window, _, doc) = CreateEditor();

        Type(window, "abcd");
        window.KeyPress(Key.Back, RawInputModifiers.None);

        Assert.Equal("abc", doc.Blocks[0].Inlines[0].Text);
    }

    // ── Bôi đậm phần được chọn ───────────────────────────────────────────
    [AvaloniaFact]
    public void Bold_AppliesOnlyToSelection()
    {
        var (_, editor, doc) = CreateEditor("Chào các bạn");

        // Bôi đen chữ "Chào" (ký tự 0..4)
        editor.SetCaret(0, 0, extend: false);
        editor.SetCaret(0, 4, extend: true);

        int changed = editor.ApplyFormatting(run => run.IsBold = true);

        Assert.True(changed > 0);
        var runs = Runs(doc);
        Assert.True(runs.Count >= 2, "Đoạn phải được cắt thành phần đậm và phần thường.");
        Assert.Equal("Chào", runs[0].Text);
        Assert.True(runs[0].IsBold);
        Assert.False(runs[1].IsBold);
        Assert.Equal(" các bạn", runs[1].Text);
    }

    [AvaloniaFact]
    public void FontSize_AppliesOnlyToSelection()
    {
        var (_, editor, doc) = CreateEditor("nhỏ và LỚN");

        editor.SetCaret(0, 7, extend: false);
        editor.SetCaret(0, 10, extend: true);

        editor.ApplyFormatting(run => run.FontSize = 28);

        var big = Runs(doc).First(r => r.FontSize == 28);
        Assert.Equal("LỚN", big.Text);
        Assert.All(Runs(doc).Where(r => r.Text != "LỚN"), r => Assert.Null(r.FontSize));
    }

    [AvaloniaFact]
    public void Formatting_WithoutSelection_AppliesToTextTypedNext()
    {
        var (window, editor, doc) = CreateEditor();

        // Đúng thao tác Word: chọn cỡ chữ trước, gõ sau.
        editor.ApplyFormatting(run => run.FontSize = 24);
        Type(window, "to");

        Assert.Equal(24, doc.Blocks[0].Inlines.First(r => r.Text == "to").FontSize);
    }

    [AvaloniaFact]
    public void PendingFormat_DoesNotChangeExistingText()
    {
        var (window, editor, doc) = CreateEditor("chữ cũ ");

        editor.SetCaret(0, 7, extend: false);
        editor.ApplyFormatting(run => run.IsBold = true);
        Type(window, "chữ mới");

        var oldRun = doc.Blocks[0].Inlines.First(r => r.Text.StartsWith("chữ cũ"));
        var newRun = doc.Blocks[0].Inlines.First(r => r.Text == "chữ mới");

        Assert.False(oldRun.IsBold, "Chữ đã gõ trước đó không được đổi theo.");
        Assert.True(newRun.IsBold);
    }

    [AvaloniaFact]
    public void MovingCaret_ForgetsPendingFormat()
    {
        var (window, editor, doc) = CreateEditor("abc");

        editor.ApplyFormatting(run => run.FontSize = 36);
        editor.SetCaret(0, 1, extend: false);   // di chuyển con trỏ
        Type(window, "X");

        Assert.All(doc.Blocks[0].Inlines, r => Assert.NotEqual(36, r.FontSize));
    }

    [AvaloniaFact]
    public void FontSizeThroughToolbar_AppliesToTypedText()
    {
        var registry = new Wocel.Shell.Services.ModuleRegistry();
        registry.RegisterModule(new Wocel.Word.WordModule());
        registry.RegisterModule(new Wocel.Excel.ExcelModule());

        var viewModel = new Wocel.Shell.ViewModels.ShellWorkspaceViewModel(registry, new Wocel.Core.Events.EventBus());
        var window = new Wocel.Shell.Views.MainWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.FindControl<Button>("BtnNewWordDoc")!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var editor = window.FindControl<WordCanvasEditor>("WordDocumentEditor")!;
        editor.Focus();

        // Chọn cỡ 24 trên thanh công cụ rồi gõ — đúng thao tác người dùng phàn nàn.
        var sizeBox = window.FindControl<ComboBox>("CboFontSize")!;
        sizeBox.SelectedItem = sizeBox.Items.OfType<ComboBoxItem>().First(i => i.Content?.ToString() == "24");
        Dispatcher.UIThread.RunJobs();

        foreach (char c in "lớn") ((Window)window).KeyTextInput(c.ToString());
        Dispatcher.UIThread.RunJobs();

        var session = (Wocel.Word.Sessions.WordDocumentSession)viewModel.ActiveTab!.Session;
        var typed = session.Document.Blocks[0].Inlines.First(r => r.Text.Length > 0);

        Assert.Equal(24, typed.FontSize);
    }

    [AvaloniaFact]
    public void StepButtons_ReachSizesOutsideTheList()
    {
        var registry = new Wocel.Shell.Services.ModuleRegistry();
        registry.RegisterModule(new Wocel.Word.WordModule());
        registry.RegisterModule(new Wocel.Excel.ExcelModule());

        var viewModel = new Wocel.Shell.ViewModels.ShellWorkspaceViewModel(registry, new Wocel.Core.Events.EventBus());
        var window = new Wocel.Shell.Views.MainWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.FindControl<Button>("BtnNewWordDoc")!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var editor = window.FindControl<WordCanvasEditor>("WordDocumentEditor")!;
        editor.SetPlainText("cỡ lạ");
        editor.SelectAll();

        // Cỡ 33 không có trong danh sách — phải với tới được bằng nút A▴.
        var stepUp = window.FindControl<Button>("BtnFontSizeUp")!;
        for (int i = 0; i < 19; i++)   // 14 mặc định + 19 bước = 33
        {
            editor.SelectAll();
            stepUp.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        var session = (Wocel.Word.Sessions.WordDocumentSession)viewModel.ActiveTab!.Session;
        Assert.All(session.Document.Blocks[0].Inlines.Where(r => r.Text.Length > 0),
            r => Assert.Equal(33, r.FontSize));
    }

    [AvaloniaFact]
    public void FontSizeStepButtons_GrowAndShrink()
    {
        var registry = new Wocel.Shell.Services.ModuleRegistry();
        registry.RegisterModule(new Wocel.Word.WordModule());
        registry.RegisterModule(new Wocel.Excel.ExcelModule());

        var viewModel = new Wocel.Shell.ViewModels.ShellWorkspaceViewModel(registry, new Wocel.Core.Events.EventBus());
        var window = new Wocel.Shell.Views.MainWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.FindControl<Button>("BtnNewWordDoc")!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var editor = window.FindControl<WordCanvasEditor>("WordDocumentEditor")!;
        editor.SetPlainText("thử");
        editor.SelectAll();
        editor.ApplyFormatting(run => run.FontSize = 20);

        editor.SelectAll();
        window.FindControl<Button>("BtnFontSizeUp")!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var session = (Wocel.Word.Sessions.WordDocumentSession)viewModel.ActiveTab!.Session;
        Assert.Equal(21, session.Document.Blocks[0].Inlines.First(r => r.Text.Length > 0).FontSize);
    }

    [AvaloniaFact]
    public void Bold_CanBeToggledOffAgain()
    {
        var (_, editor, doc) = CreateEditor("đảo qua đảo lại");

        editor.SelectAll();
        editor.ApplyFormatting(run => run.IsBold = true);
        Assert.All(Runs(doc).Where(r => r.Text.Length > 0), r => Assert.True(r.IsBold));

        editor.ApplyFormatting(run => run.IsBold = false);
        Assert.All(Runs(doc).Where(r => r.Text.Length > 0), r => Assert.False(r.IsBold));
    }

    [AvaloniaFact]
    public void AdjacentRunsWithSameFormat_AreMergedBack()
    {
        var (_, editor, doc) = CreateEditor("một hai ba");

        editor.SetCaret(0, 0, extend: false);
        editor.SetCaret(0, 3, extend: true);
        editor.ApplyFormatting(run => run.IsBold = true);

        editor.SelectAll();
        editor.ApplyFormatting(run => run.IsBold = false);

        // Cùng định dạng thì phải gộp lại làm một, không để vụn.
        Assert.Single(Runs(doc));
        Assert.Equal("một hai ba", Runs(doc)[0].Text);
    }

    // ── Bôi đen bằng phím ────────────────────────────────────────────────
    [AvaloniaFact]
    public void ShiftArrows_ExtendSelection()
    {
        var (window, editor, _) = CreateEditor("abcdef");

        editor.SetCaret(0, 0, extend: false);
        window.KeyPress(Key.Right, RawInputModifiers.Shift);
        window.KeyPress(Key.Right, RawInputModifiers.Shift);
        window.KeyPress(Key.Right, RawInputModifiers.Shift);

        Assert.True(editor.HasSelection);
        Assert.Equal("abc", editor.SelectedText);
    }

    [AvaloniaFact]
    public void CtrlA_SelectsEverything()
    {
        var (window, editor, _) = CreateEditor("dòng 1\ndòng 2");

        window.KeyPress(Key.A, RawInputModifiers.Control);

        Assert.True(editor.HasSelection);
        Assert.Contains("dòng 1", editor.SelectedText);
        Assert.Contains("dòng 2", editor.SelectedText);
    }

    [AvaloniaFact]
    public void TypingOverSelection_ReplacesIt()
    {
        var (window, editor, doc) = CreateEditor("xoá cái này");

        editor.SetCaret(0, 0, extend: false);
        editor.SetCaret(0, 4, extend: true);
        Type(window, "giữ ");

        Assert.Equal("giữ cái này", doc.Blocks[0].Inlines[0].Text);
    }

    // ── Lưu ra .docx rồi mở lại ──────────────────────────────────────────
    [Fact]
    public void Formatting_SurvivesDocxRoundTrip()
    {
        var document = new DocumentDocument();
        var block = new DocBlock { Type = BlockType.Paragraph };
        block.Inlines.Add(new TextRun { Text = "Tiêu đề ", IsBold = true, FontSize = 20, FontFamily = "Arial" });
        block.Inlines.Add(new TextRun { Text = "và phần thường", IsItalic = true });
        document.Blocks.Add(block);

        using var buffer = new MemoryStream();
        DocxWriter.Write(document, buffer);
        buffer.Position = 0;

        var reloaded = DocxReader.Read(buffer);
        var runs = reloaded.Blocks[0].Inlines;

        Assert.Equal(2, runs.Count);
        Assert.True(runs[0].IsBold);
        Assert.Equal(20, runs[0].FontSize);
        Assert.Equal("Arial", runs[0].FontFamily);
        Assert.True(runs[1].IsItalic);
        Assert.False(runs[1].IsBold);
    }

    // ── Chuột ────────────────────────────────────────────────────────────
    [AvaloniaFact]
    public void ClickingText_MovesCaretThere()
    {
        var (_, editor, _) = CreateEditor("một dòng văn bản để bấm vào");

        // Bấm ở khoảng giữa dòng đầu tiên.
        var (block, offset) = editor.HitTest(new Point(WordCanvasEditor.PageMargin + 40,
                                                       WordCanvasEditor.PageMargin + 6));

        Assert.Equal(0, block);
        Assert.True(offset > 0, "Bấm vào giữa dòng thì con trỏ phải nằm sau ký tự đầu.");
    }

    // ── Đổi thẻ không được làm mất định dạng ─────────────────────────────
    [AvaloniaFact]
    public void SwitchingTabs_KeepsFormatting()
    {
        var registry = new Wocel.Shell.Services.ModuleRegistry();
        registry.RegisterModule(new Wocel.Word.WordModule());
        registry.RegisterModule(new Wocel.Excel.ExcelModule());

        var viewModel = new Wocel.Shell.ViewModels.ShellWorkspaceViewModel(registry, new Wocel.Core.Events.EventBus());
        var window = new Wocel.Shell.Views.MainWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.FindControl<Button>("BtnNewWordDoc")!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var editor = window.FindControl<WordCanvasEditor>("WordDocumentEditor")!;
        editor.SetPlainText("chữ cần in đậm");
        editor.SelectAll();
        editor.ApplyFormatting(run => run.IsBold = true);

        var session = (Wocel.Word.Sessions.WordDocumentSession)viewModel.ActiveTab!.Session;
        Assert.All(session.Document.Blocks[0].Inlines.Where(r => r.Text.Length > 0), r => Assert.True(r.IsBold));

        // Mở thêm sổ tính rồi quay lại — định dạng phải còn nguyên.
        window.FindControl<Button>("BtnAddExcelTab")!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        viewModel.ActiveTab = viewModel.Tabs.First(t => t.Session is Wocel.Word.Sessions.WordDocumentSession);
        Dispatcher.UIThread.RunJobs();

        Assert.All(session.Document.Blocks[0].Inlines.Where(r => r.Text.Length > 0), r => Assert.True(r.IsBold));
    }
}
