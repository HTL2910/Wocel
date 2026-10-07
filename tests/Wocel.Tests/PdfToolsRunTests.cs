using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Wocel.Core.Pdf;
using Wocel.Core.Services;
using Wocel.Shell.Views;
using Xunit;

namespace Wocel.Tests;

/// <summary>
/// Chạy thật từng công cụ trong bảng, trên tệp thật, qua đúng đường mà nút bấm đi.
/// Không công cụ nào được phép ném lỗi ngoài dự tính.
/// </summary>
public class PdfToolsRunTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wocel-tools-" + Guid.NewGuid().ToString("N")[..8]);

    public PdfToolsRunTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* thư mục tạm */ }
    }

    private string WritePdf(string name, int paragraphs = 60)
    {
        var text = string.Join("\n", Enumerable.Range(1, paragraphs)
            .Select(i => $"{i}. Dòng thử nghiệm tiếng Việt có dấu — số tài khoản 0123456789."));

        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, PdfToolkit.TextToPdf(text, new PdfTextToPdfOptions { Title = "Thử" }));
        return path;
    }

    private string WriteCsv(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "Mã;Tên hàng;Số lượng\nSP001;Bàn phím;12\nSP002;Chuột;34\n");
        return path;
    }

    private string WriteText(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "Một đoạn văn bản tiếng Việt để chuyển thành PDF.");
        return path;
    }

    private string WritePng(string name)
    {
        var samples = new byte[32 * 24 * 3];
        for (int i = 0; i < samples.Length; i++) samples[i] = (byte)(i % 251);

        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, PdfImageCodec.EncodePng(samples, 32, 24, 3, 8));
        return path;
    }

    private static PdfToolsPanel OpenPanel()
    {
        var panel = new PdfToolsPanel();
        var window = new Window { Content = panel, Width = 1300, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return panel;
    }

    // ─────────────────────────────────────────────────────────────────────
    [AvaloniaFact]
    public void ToolList_IsTrimmedAndHasNoDuplicates()
    {
        var ids = PdfToolsPanel.ToolIds;

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.True(ids.Count <= 12, $"Danh sách công cụ nên gọn: hiện có {ids.Count}.");

        // Các công cụ đã gộp không được còn sót lại.
        // Các công cụ đã gộp hoặc đã gỡ không được còn sót lại.
        foreach (var removed in new[] { "extract", "remove", "nup", "resize", "crop",
                                        "flatten", "csv_to_pdf", "text_to_pdf",
                                        "page_numbers", "metadata", "info",
                                        "watermark", "header_footer", "replace", "redact",
                                        "protect", "unlock", "compress", "repair",
                                        "doc_info", "compare" })
            Assert.DoesNotContain(removed, ids);
    }

    [AvaloniaFact]
    public void EveryTool_BuildsItsOptionsPanel()
    {
        var panel = OpenPanel();

        foreach (var id in PdfToolsPanel.ToolIds)
        {
            Assert.True(panel.SelectToolById(id), $"Không mở được công cụ “{id}”.");
            Assert.True(panel.VisibleOptionCount > 0, $"Công cụ “{id}” không hiện tuỳ chọn nào.");
        }
    }

    /// <summary>
    /// Chạy thật mọi công cụ. Được phép dừng vì thiếu dữ liệu người dùng nhập,
    /// nhưng không được ném lỗi ngoài dự tính.
    /// </summary>
    [AvaloniaFact]
    public async Task EveryTool_RunsWithoutUnexpectedError()
    {
        var panel = OpenPanel();
        panel.AddFile(WritePdf("a.pdf"));
        panel.AddFile(WritePdf("b.pdf", paragraphs: 40));
        panel.AddFile(WriteCsv("bang.csv"));
        panel.AddFile(WriteText("vanban.txt"));
        panel.AddFile(WritePng("anh.png"));

        var failures = new List<string>();

        foreach (var id in PdfToolsPanel.ToolIds)
        {
            panel.SelectToolById(id);
            var (outcome, message) = await Dispatcher.UIThread.InvokeAsync(panel.RunSelectedToolAsync);

            if (outcome == PdfToolsPanel.ToolRunOutcome.Failed)
                failures.Add($"{id} → {message}");
        }

        Assert.True(failures.Count == 0, "Công cụ gặp lỗi ngoài dự tính:\n" + string.Join("\n", failures));
    }

    /// <summary>Các công cụ chạy được ngay với tuỳ chọn mặc định thì phải ra kết quả thật.</summary>
    [AvaloniaTheory]
    [InlineData("merge")]
    [InlineData("split")]
    [InlineData("pages")]
    [InlineData("rotate")]
    [InlineData("pdf_to_text")]
    [InlineData("pdf_to_csv")]
    [InlineData("images_to_pdf")]
    [InlineData("to_pdf")]
    public async Task ToolWithDefaults_ProducesResult(string toolId)
    {
        var panel = OpenPanel();
        panel.AddFile(WritePdf("a.pdf"));
        panel.AddFile(WritePdf("b.pdf", paragraphs: 40));
        panel.AddFile(WriteCsv("bang.csv"));
        panel.AddFile(WritePng("anh.png"));

        panel.SelectToolById(toolId);
        var (outcome, message) = await Dispatcher.UIThread.InvokeAsync(panel.RunSelectedToolAsync);

        Assert.True(outcome == PdfToolsPanel.ToolRunOutcome.Succeeded,
            $"Công cụ “{toolId}” phải chạy được với tuỳ chọn mặc định. Kết quả: {outcome} — {message}");
    }

    [AvaloniaFact]
    public async Task Merge_ActuallyWritesAFile()
    {
        var panel = OpenPanel();
        panel.AddFile(WritePdf("mot.pdf"));
        panel.AddFile(WritePdf("hai.pdf"));

        panel.SelectToolById("merge");
        var (outcome, _) = await Dispatcher.UIThread.InvokeAsync(panel.RunSelectedToolAsync);

        Assert.Equal(PdfToolsPanel.ToolRunOutcome.Succeeded, outcome);

        var merged = Directory.GetFiles(_folder, "*_gop.pdf");
        Assert.Single(merged);
        Assert.True(PdfToolkit.Open(File.ReadAllBytes(merged[0])).PageCount >= 2);
    }

    [AvaloniaFact]
    public async Task ToolWithoutFile_ExplainsInsteadOfCrashing()
    {
        var panel = OpenPanel();   // không nạp tệp nào

        panel.SelectToolById("split");
        var (outcome, message) = await Dispatcher.UIThread.InvokeAsync(panel.RunSelectedToolAsync);

        Assert.Equal(PdfToolsPanel.ToolRunOutcome.NeedsInput, outcome);
        Assert.Contains("tệp", message, StringComparison.OrdinalIgnoreCase);
    }
}
