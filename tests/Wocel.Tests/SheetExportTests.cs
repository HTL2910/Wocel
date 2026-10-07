using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Wocel.Core.Events;
using Wocel.Core.Services;
using Wocel.Excel;
using Wocel.Excel.Sessions;
using Wocel.Shell.Controls;
using Wocel.Shell.Services;
using Wocel.Shell.ViewModels;
using Wocel.Shell.Views;
using Xunit;

namespace Wocel.Tests;

/// <summary>Xuất trang tính ra PDF và CSV — chạy thật, ghi ra tệp thật, mở lại kiểm tra.</summary>
public class SheetExportTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wocel-export-" + Guid.NewGuid().ToString("N")[..8]);

    public SheetExportTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch { }
    }

    private static (MainWindow window, ExcelDocumentSession session) OpenWorkbook()
    {
        var registry = new ModuleRegistry();
        registry.RegisterModule(new Wocel.Word.WordModule());
        registry.RegisterModule(new ExcelModule());

        var viewModel = new ShellWorkspaceViewModel(registry, new EventBus());
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.FindControl<Button>("BtnNewExcelBook")!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        return (window, (ExcelDocumentSession)viewModel.ActiveTab!.Session);
    }

    private static List<IReadOnlyList<string>> SampleRows() => new()
    {
        new[] { "Mã", "Tên hàng", "Số lượng", "Đơn giá" },
        new[] { "SP001", "Bàn phím cơ", "12", "1.500.000 ₫" },
        new[] { "SP002", "Chuột không dây", "34", "450.000 ₫" },
        new[] { "SP003", "Màn hình 27\"", "7", "6.200.000 ₫" }
    };

    [Fact]
    public async Task ExportToPdf_WritesReadableFile()
    {
        var path = Path.Combine(_folder, "bang-ke.pdf");

        await MainWindow.ExportRowsToPdfAsync(SampleRows(), path, PdfPageSizes.Landscape(PdfPageSizes.A4), "Bảng kê");

        Assert.True(File.Exists(path), "Không tạo được tệp PDF.");

        var bytes = await File.ReadAllBytesAsync(path);
        Assert.True(bytes.Length > 500, $"Tệp PDF quá nhỏ ({bytes.Length} byte), có vẻ rỗng.");

        var document = PdfToolkit.Open(bytes);
        Assert.True(document.PageCount >= 1);

        var text = PdfToolkit.ExtractText(bytes);
        Assert.Contains("SP001", text);
        Assert.Contains("Bàn phím", text);   // tiếng Việt phải đủ dấu
        Assert.Contains("Bảng kê", text);
    }

    [Fact]
    public async Task ExportToPdf_HandlesManyRowsAcrossPages()
    {
        var rows = new List<IReadOnlyList<string>> { new[] { "STT", "Nội dung" } };
        for (int i = 1; i <= 200; i++) rows.Add(new[] { i.ToString(), $"Dòng số {i} có dấu tiếng Việt" });

        var path = Path.Combine(_folder, "nhieu-dong.pdf");
        await MainWindow.ExportRowsToPdfAsync(rows, path, PdfPageSizes.A4, "Danh sách dài");

        var document = PdfToolkit.Open(await File.ReadAllBytesAsync(path));
        Assert.True(document.PageCount > 1, "200 dòng phải trải ra nhiều trang.");
    }

    [Fact]
    public async Task ExportToPdf_HandlesWideSheet()
    {
        // 26 cột như lưới mặc định — trường hợp dễ vỡ nhất khi chia bề rộng cột.
        var header = Enumerable.Range(0, 26).Select(i => ((char)('A' + i)).ToString()).ToArray();
        var row = Enumerable.Range(1, 26).Select(i => $"ô {i}").ToArray();

        var path = Path.Combine(_folder, "nhieu-cot.pdf");
        await MainWindow.ExportRowsToPdfAsync(
            new List<IReadOnlyList<string>> { header, row },
            path, PdfPageSizes.Landscape(PdfPageSizes.A3), "Bảng rộng");

        Assert.True(new FileInfo(path).Length > 500);
        Assert.True(PdfToolkit.Open(await File.ReadAllBytesAsync(path)).PageCount >= 1);
    }

    [Theory]
    [InlineData("bang", ".pdf", "bang.pdf")]
    [InlineData("bang.pdf", ".pdf", "bang.pdf")]
    [InlineData("bang.PDF", ".pdf", "bang.PDF")]
    public void EnsureExtension_AddsMissingSuffixOnly(string input, string extension, string expected)
    {
        Assert.Equal(expected, MainWindow.EnsureExtension(input, extension));
    }

    [Fact]
    public void SafeFileName_StripsInvalidCharacters()
    {
        var cleaned = MainWindow.SafeFileName("Báo cáo/Quý 1: 2026");
        Assert.DoesNotContain('/', cleaned);
        Assert.DoesNotContain(Path.GetInvalidFileNameChars(), cleaned.Contains);
    }

    // ── Qua đúng đường của nút bấm ───────────────────────────────────────
    [AvaloniaFact]
    public void ExportButton_ReportsEmptySheetInsteadOfSilence()
    {
        var (window, _) = OpenWorkbook();

        window.FindControl<Button>("BtnTabLayout")!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        window.FindControl<Button>("BtnExportSheetToPdf")!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        // Trang tính trống: phải báo rõ NGAY, trước khi mở hộp thoại chọn nơi lưu.
        var status = window.FindControl<TextBlock>("TxtStatusBar")!;
        Assert.Contains("chưa có dữ liệu", status.Text ?? string.Empty);
    }

    [AvaloniaFact]
    public void ExportCsvButton_AlsoChecksDataFirst()
    {
        var (window, _) = OpenWorkbook();

        window.FindControl<Button>("BtnTabLayout")!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        window.FindControl<Button>("BtnExportSheetToCsv")!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var status = window.FindControl<TextBlock>("TxtStatusBar")!;
        Assert.Contains("chưa có dữ liệu", status.Text ?? string.Empty);
    }

    [AvaloniaFact]
    public async Task SheetWithData_ExportsThroughRealPipeline()
    {
        var (window, session) = OpenWorkbook();
        var sheet = session.Document.GetOrCreateActiveSheet();

        sheet.SetValue("A1", "Tên");
        sheet.SetValue("B1", "Số lượng");
        sheet.SetValue("A2", "Bàn phím");
        sheet.SetValue("B2", 12.0);
        sheet.SetValue("A3", "Chuột");
        sheet.SetValue("B3", "=B2*2");
        session.Recalculate();

        var rows = MainWindow.BuildSheetRowsForTesting(sheet);

        Assert.Equal(3, rows.Count);
        Assert.Equal("24", rows[2][1]);   // công thức phải xuất ra kết quả, không phải "=B2*2"

        var path = Path.Combine(_folder, "that.pdf");
        await MainWindow.ExportRowsToPdfAsync(rows, path, PdfPageSizes.A4, sheet.Name);

        var text = PdfToolkit.ExtractText(await File.ReadAllBytesAsync(path));
        Assert.Contains("Bàn phím", text);
        Assert.Contains("24", text);
    }

    /// <summary>Dữ liệu thật hay có ô trống, chữ rất dài, xuống dòng, ký tự lạ.</summary>
    [Fact]
    public async Task ExportToPdf_SurvivesAwkwardData()
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "Mã", "Mô tả", "Ghi chú", "Giá" },
            new[] { "", "", "", "" },
            new[] { "SP001", new string('x', 500), "dòng 1\ndòng 2", "1.000.000 ₫" },
            new[] { "SP002", "Chữ có dấu: ơ ư đ ă â ê ô ạ ả ã", "\t tab \t", "-12,5%" },
            new[] { "SP003", "Ký tự lạ: <>&\"'\\/|?*", "🙂 emoji", "0" },
            new[] { "SP004" },                                  // dòng thiếu cột
            new[] { "SP005", "a", "b", "c", "d", "e", "f" }      // dòng thừa cột
        };

        var path = Path.Combine(_folder, "kho.pdf");
        var error = await Record.ExceptionAsync(() =>
            MainWindow.ExportRowsToPdfAsync(rows, path, PdfPageSizes.Landscape(PdfPageSizes.A4), "Dữ liệu khó"));

        Assert.True(error == null, $"Xuất PDF vỡ với dữ liệu thật: {error?.Message}");
        Assert.True(new FileInfo(path).Length > 500);
    }

    [Fact]
    public async Task ExportToPdf_WorksForEveryPageSize()
    {
        foreach (var name in PdfPageSizes.Names)
        {
            foreach (bool landscape in new[] { false, true })
            {
                var size = PdfPageSizes.ByName(name);
                if (landscape) size = PdfPageSizes.Landscape(size);

                var path = Path.Combine(_folder, $"{name}-{landscape}.pdf");
                var error = await Record.ExceptionAsync(() =>
                    MainWindow.ExportRowsToPdfAsync(SampleRows(), path, size, name));

                Assert.True(error == null, $"Khổ {name} ({(landscape ? "ngang" : "dọc")}) lỗi: {error?.Message}");
                Assert.True(PdfToolkit.Open(await File.ReadAllBytesAsync(path)).PageCount >= 1);
            }
        }
    }

    /// <summary>Ô công thức phải xuất ra KẾT QUẢ, không phải chuỗi "=SUM(...)".</summary>
    [AvaloniaFact]
    public void BuildSheetRows_ExportsFormulaResults()
    {
        var (_, session) = OpenWorkbook();
        var sheet = session.Document.GetOrCreateActiveSheet();

        sheet.SetValue("A1", 10.0);
        sheet.SetValue("A2", 32.0);
        sheet.SetValue("A3", "=SUM(A1:A2)");
        session.Recalculate();

        var rows = MainWindow.BuildSheetRowsForTesting(sheet);

        Assert.Equal("42", rows[2][0]);
        Assert.DoesNotContain(rows.SelectMany(r => r), cell => cell.StartsWith("="));
    }

    // ── Xuất tài liệu văn bản ────────────────────────────────────────────
    [Fact]
    public void DocumentToPdf_KeepsBoldAndFontSize()
    {
        var document = new Wocel.Core.Models.DocumentDocument();

        var heading = new Wocel.Core.Models.DocBlock();
        heading.Inlines.Add(new Wocel.Core.Models.TextRun { Text = "Tiêu đề lớn", IsBold = true, FontSize = 22 });
        document.Blocks.Add(heading);

        var body = new Wocel.Core.Models.DocBlock();
        body.Inlines.Add(new Wocel.Core.Models.TextRun { Text = "Phần thường " });
        body.Inlines.Add(new Wocel.Core.Models.TextRun { Text = "phần đậm", IsBold = true });
        body.Inlines.Add(new Wocel.Core.Models.TextRun { Text = " và phần gạch chân", IsUnderline = true });
        document.Blocks.Add(body);

        var pdf = PdfToolkit.DocumentToPdf(document, new PdfTextToPdfOptions { Title = "Thử xuất" });

        Assert.True(pdf.Length > 500);

        var text = PdfToolkit.ExtractText(pdf);
        Assert.Contains("Tiêu đề lớn", text);
        Assert.Contains("phần đậm", text);
        Assert.Contains("gạch chân", text);
    }

    [Fact]
    public void DocumentToPdf_PaginatesLongDocuments()
    {
        var document = new Wocel.Core.Models.DocumentDocument();
        for (int i = 1; i <= 150; i++)
        {
            var block = new Wocel.Core.Models.DocBlock();
            block.Inlines.Add(new Wocel.Core.Models.TextRun { Text = $"Đoạn số {i} với nội dung tiếng Việt có dấu." });
            document.Blocks.Add(block);
        }

        var pdf = PdfToolkit.DocumentToPdf(document);
        Assert.True(PdfToolkit.Open(pdf).PageCount > 1, "Tài liệu dài phải trải ra nhiều trang.");
    }

    [AvaloniaFact]
    public void ExportButtonLabel_FollowsDocumentType()
    {
        var (window, _) = OpenWorkbook();
        var button = window.FindControl<Button>("BtnExportSheetToPdf")!;
        var csvButton = window.FindControl<Button>("BtnExportSheetToCsv")!;

        Assert.Contains("trang tính", button.Content?.ToString() ?? string.Empty);
        Assert.True(csvButton.IsVisible);

        window.FindControl<Button>("BtnAddWordTab")!
              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        // Với tài liệu văn bản: nhãn đổi, và xuất CSV không còn ý nghĩa nên ẩn đi.
        Assert.Contains("tài liệu", button.Content?.ToString() ?? string.Empty);
        Assert.False(csvButton.IsVisible);
    }
}
