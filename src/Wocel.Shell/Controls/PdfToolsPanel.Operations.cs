using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wocel.Core.Diagnostics;
using Wocel.Core.Pdf;
using Wocel.Core.Services;

namespace Wocel.Shell.Views;

/// <summary>Phần dựng bảng tuỳ chọn và thực thi cho từng công cụ.</summary>
public partial class PdfToolsPanel
{
    private static readonly string[] PositionLabels =
    {
        "Trên · trái", "Trên · giữa", "Trên · phải",
        "Dưới · trái", "Dưới · giữa", "Dưới · phải"
    };

    private static PdfBoxPosition PositionFrom(int index) => index switch
    {
        0 => PdfBoxPosition.TopLeft,
        1 => PdfBoxPosition.TopCenter,
        2 => PdfBoxPosition.TopRight,
        3 => PdfBoxPosition.BottomLeft,
        5 => PdfBoxPosition.BottomRight,
        _ => PdfBoxPosition.BottomCenter
    };

    // ─────────────────────────────────────────────────────────────────────
    //  DỰNG BẢNG TUỲ CHỌN
    // ─────────────────────────────────────────────────────────────────────
    private void BuildWorkPanel(ToolDef tool)
    {
        _workContent.Children.Clear();
        _statusCard.IsVisible = false;
        _outputViewer.IsVisible = false;
        _outputBox.Text = string.Empty;

        _workContent.Children.Add(SectionTitle($"{tool.Icon}  {tool.Name}"));
        _workContent.Children.Add(Hint(tool.Desc));

        switch (tool.Id)
        {
            case "merge": BuildMerge(); break;
            case "split": BuildSplit(); break;
            case "pages": BuildPageSelect(); break;
            case "reorder": BuildReorder(); break;
            case "rotate": BuildRotate(); break;
            case "page_setup": BuildPageSetup(); break;

            case "pdf_to_text": BuildPdfToText(); break;
            case "pdf_to_csv": BuildPdfToCsv(); break;
            case "pdf_to_images": BuildPdfToImages(); break;
            case "images_to_pdf": BuildImagesToPdf(); break;
            case "to_pdf": BuildToPdf(); break;


            case "logs": BuildLogs(); break;
        }
    }


    // ── Nhật ký hoạt động ────────────────────────────────────────────────
    private void BuildLogs()
    {
        var status = new TextBlock { FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap };
        Add(status);

        void Refresh()
        {
            var entries = ActivityLog.ReadRecent(300);
            int pending = ActivityLog.PendingCount();

            var lines = new StringBuilder();
            lines.AppendLine($"Phiên hiện tại : {ActivityLog.SessionId}");
            lines.AppendLine($"Phiên bản      : {ActivityLog.AppVersion} ({ActivityLog.EnvironmentName})");
            lines.AppendLine($"Thư mục log    : {ActivityLog.LogDirectory}");
            lines.AppendLine();
            foreach (var entry in entries) lines.AppendLine(entry.ToString());

            ShowPreview(lines.ToString());

            status.Text = ActivityLog.RemoteEnabled
                ? $"Đồng bộ Supabase: BẬT · đã gửi {ActivityLog.UploadedCount} dòng · còn chờ {pending} dòng"
                  + (ActivityLog.LastUploadAt != null ? $" · lần cuối {ActivityLog.LastUploadAt:HH:mm:ss}" : string.Empty)
                  + (ActivityLog.LastRemoteError != null ? $"\n⚠ Lỗi gửi gần nhất: {ActivityLog.LastRemoteError}" : string.Empty)
                : "Đồng bộ Supabase: TẮT — nhật ký chỉ lưu trên máy. "
                  + "Điền SUPABASE_URL và SUPABASE_ANON_KEY trong tệp .env để bật.";
        }

        var refresh = SecondaryButton("🔄  Tải lại nhật ký");
        refresh.Click += (s, e) => Refresh();

        var openFolder = SecondaryButton("📂  Mở thư mục nhật ký");
        openFolder.Click += (s, e) => OpenFolder(ActivityLog.LogDirectory);

        var push = SecondaryButton("☁  Gửi ngay lên Supabase");
        push.Click += async (s, e) =>
        {
            int sent = await ActivityLog.FlushAsync();
            Refresh();
            ShowSuccess(ActivityLog.RemoteEnabled
                ? $"Đã gửi {sent} dòng nhật ký lên Supabase."
                : "Chưa cấu hình Supabase nên không gửi được — nhật ký vẫn lưu đầy đủ trên máy.", null);
        };

        Add(Row(refresh, openFolder, push));
        Add(Hint("Nhật ký chỉ chứa thông tin thao tác (công cụ, dung lượng tệp, thời gian, lỗi). "
                 + "Nội dung tài liệu của bạn không bao giờ được gửi đi."));

        Refresh();
    }


    // ─────────────────────────────────────────────────────────────────────
    //  CHẠY CÔNG CỤ TRỰC TIẾP (dùng cho kiểm thử tự động)
    // ─────────────────────────────────────────────────────────────────────
    /// <summary>Kết quả chạy một công cụ.</summary>
    public enum ToolRunOutcome
    {
        /// <summary>Chạy xong, đã tạo kết quả.</summary>
        Succeeded,
        /// <summary>Dừng có kiểm soát vì thiếu dữ liệu người dùng phải nhập.</summary>
        NeedsInput,
        /// <summary>Sự cố ngoài dự tính — đây mới là lỗi thật.</summary>
        Failed,
        /// <summary>Công cụ này không có lệnh nào để chạy.</summary>
        NoAction
    }

    /// <summary>Chạy lệnh chính của công cụ đang chọn và trả về kết quả kèm thông báo.</summary>
    public async Task<(ToolRunOutcome Outcome, string Message)> RunSelectedToolAsync()
    {
        if (_primaryAction == null) return (ToolRunOutcome.NoAction, "Công cụ này không có lệnh nào.");

        try
        {
            await _primaryAction();
            return (ToolRunOutcome.Succeeded, _lastStatusMessage);
        }
        catch (PdfToolException expected)
        {
            return (ToolRunOutcome.NeedsInput, expected.Message);
        }
        catch (Exception error)
        {
            return (ToolRunOutcome.Failed, $"{error.GetType().Name}: {error.Message}");
        }
    }


    /// <summary>
    /// Chạy đoạn cập nhật giao diện trên đúng luồng UI. Các thao tác công cụ có
    /// await nên phần chạy tiếp sau đó không chắc còn ở luồng UI.
    /// </summary>
    private static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    private void Add(Control control) => _workContent.Children.Add(control);

    private void AddRun(string label, Func<Task> action)
    {
        var button = PrimaryButton(label);
        var toolId = _selectedTool?.Id ?? "unknown";

        // Hành động đầu tiên của mỗi công cụ được ghi lại để chạy trực tiếp,
        // phục vụ kiểm thử tự động toàn bộ danh sách công cụ.
        _primaryAction ??= action;

        button.Click += async (s, e) =>
        {
            button.IsEnabled = false;
            var original = button.Content;
            button.Content = "⏳ Đang xử lý…";

            var watch = Stopwatch.StartNew();
            var firstFile = _files.FirstOrDefault();
            long? inputSize = null;
            try { if (firstFile != null) inputSize = new FileInfo(firstFile).Length; }
            catch { /* không lấy được kích thước thì thôi */ }

            try
            {
                await action();
                watch.Stop();

                ActivityLog.Write(new ActivityLogEntry
                {
                    Category = "pdf-tool",
                    Action = "run",
                    ToolId = toolId,
                    FileName = firstFile == null ? null : Path.GetFileName(firstFile),
                    FileExtension = firstFile == null ? null : Path.GetExtension(firstFile).ToLowerInvariant(),
                    FileSizeBytes = inputSize,
                    ItemCount = _files.Count,
                    DurationMs = watch.ElapsedMilliseconds,
                    Success = true,
                    Message = label
                });
            }
            catch (Exception error)
            {
                watch.Stop();
                bool expected = error is PdfToolException;

                ShowError(expected ? error.Message : $"Xử lý thất bại: {error.Message}");

                ActivityLog.Write(new ActivityLogEntry
                {
                    // Lỗi nghiệp vụ (người dùng nhập thiếu) chỉ là cảnh báo; lỗi khác mới là sự cố thật.
                    Level = expected ? "warning" : "error",
                    Category = "pdf-tool",
                    Action = "run",
                    ToolId = toolId,
                    FileName = firstFile == null ? null : Path.GetFileName(firstFile),
                    FileExtension = firstFile == null ? null : Path.GetExtension(firstFile).ToLowerInvariant(),
                    FileSizeBytes = inputSize,
                    ItemCount = _files.Count,
                    DurationMs = watch.ElapsedMilliseconds,
                    Success = false,
                    Message = error.Message,
                    ErrorType = error.GetType().FullName,
                    ErrorDetail = expected ? null : error.ToString()
                });
            }
            finally
            {
                button.IsEnabled = true;
                button.Content = original;
            }
        };

        Add(button);
    }

    // ── Tổ chức trang ────────────────────────────────────────────────────
    private void BuildMerge()
    {
        Add(Hint("Dùng nút ↑ ↓ ở danh sách tệp phía trên để chỉnh thứ tự trước khi gộp."));
        AddRun("🔗  Gộp tất cả tệp PDF", async () =>
        {
            var sources = RequireFiles(2, ".pdf");
            var password = Password();

            var buffers = new List<byte[]>();
            foreach (var path in sources) buffers.Add(await File.ReadAllBytesAsync(path));

            var result = await Task.Run(() => PdfToolkit.Merge(buffers, sources.Select(_ => password)));
            var output = await SaveResultAsync(result, "gop", sources[0]);

            ShowSuccess($"Đã gộp {sources.Count} tệp thành {PdfToolkit.Open(result).PageCount} trang.", output);
        });
    }

    private void BuildSplit()
    {
        var mode = Choice(new[] { "Mỗi trang một tệp", "Chia theo nhóm N trang", "Theo khoảng trang tự nhập" });
        var perFile = Number(2, 1, 500);
        var ranges = Field("Ví dụ: 1-3; 4-6; 7-10", width: 300);

        Add(Labeled("Cách tách", mode));
        Add(Labeled("Số trang mỗi tệp", perFile));
        Add(Labeled("Khoảng trang", ranges));
        Add(Hint("Các tệp kết quả được lưu vào một thư mục con cạnh tệp gốc."));

        AddRun("✂  Tách tệp", async () =>
        {
            var source = RequireFile(".pdf");
            var password = Password();
            var options = new PdfSplitOptions
            {
                Mode = mode.SelectedIndex switch
                {
                    1 => PdfSplitMode.EveryNPages,
                    2 => PdfSplitMode.ByRanges,
                    _ => PdfSplitMode.EveryPage
                },
                PagesPerFile = (int)(perFile.Value ?? 2),
                Ranges = ranges.Text ?? string.Empty,
                BaseName = Path.GetFileNameWithoutExtension(source)
            };

            var data = await File.ReadAllBytesAsync(source);
            var parts = await Task.Run(() => PdfToolkit.Split(data, options, password));
            var folder = await SaveManyAsync(parts, source, "tach");

            ShowSuccess($"Đã tách thành {parts.Count} tệp.", folder);
            ShowPreview(string.Join("\n", parts.Select(p => $"{p.FileName}   —   {p.Description}")));
        });
    }

    private void BuildPageSelect()
    {
        var mode = Choice(new[] { "Chỉ giữ lại các trang này", "Xoá các trang này" }, 0, 240);
        var pages = Field("Ví dụ: 1, 3, 5-8, 12-  (n = trang cuối)", "1-3", 320);

        Add(Labeled("Cách xử lý", mode));
        Add(Labeled("Trang", pages));
        Add(Hint("Nhập được cả “chẵn”, “lẻ” hoặc “tất cả”."));

        AddRun("📑  Thực hiện", async () =>
        {
            var source = RequireFile(".pdf");
            var password = Password();
            bool keep = mode.SelectedIndex == 0;
            var expression = pages.Text ?? string.Empty;

            var data = await File.ReadAllBytesAsync(source);
            var result = keep
                ? await Task.Run(() => PdfToolkit.ExtractPages(data, expression, password))
                : await Task.Run(() => PdfToolkit.RemovePages(data, expression, password));

            var output = await SaveResultAsync(result, keep ? "trich" : "da-xoa-trang", source);
            ShowSuccess($"Tệp kết quả có {PdfToolkit.Open(result).PageCount} trang.", output);
        });
    }

    private void BuildReorder()
    {
        var order = Field("Ví dụ: 3, 1, 2, 4", width: 320);
        Add(Labeled("Thứ tự trang mới", order));
        Add(Hint("Trang không nhắc tới sẽ được giữ lại và xếp xuống cuối tài liệu."));

        AddRun("🔃  Sắp xếp lại", async () =>
        {
            var source = RequireFile(".pdf");
            var password = Password();
            var sequence = (order.Text ?? string.Empty)
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => int.TryParse(t.Trim(), out int value) ? value : -1)
                .Where(v => v > 0)
                .ToList();

            if (sequence.Count == 0) throw new PdfToolException("Hãy nhập thứ tự trang, ví dụ: 3, 1, 2");

            var data = await File.ReadAllBytesAsync(source);
            var result = await Task.Run(() => PdfToolkit.ReorderPages(data, sequence, password));
            var output = await SaveResultAsync(result, "sap-xep", source);

            ShowSuccess("Đã sắp xếp lại thứ tự trang.", output);
        });

        AddRun("↩  Đảo ngược toàn bộ trang", async () =>
        {
            var source = RequireFile(".pdf");
            var password = Password();

            var data = await File.ReadAllBytesAsync(source);
            var result = await Task.Run(() => PdfToolkit.ReversePages(data, password));
            var output = await SaveResultAsync(result, "dao-nguoc", source);

            ShowSuccess("Đã đảo ngược thứ tự trang.", output);
        });
    }

    private void BuildRotate()
    {
        var degrees = Choice(new[] { "90° sang phải", "180° lật ngược", "270° sang trái" });
        var pages = Field("Để trống = mọi trang", width: 240);

        Add(Labeled("Góc xoay", degrees));
        Add(Labeled("Trang áp dụng", pages));

        AddRun("🔄  Xoay trang", async () =>
        {
            var source = RequireFile(".pdf");
            var password = Password();
            int angle = degrees.SelectedIndex switch { 1 => 180, 2 => 270, _ => 90 };
            var range = pages.Text;

            var data = await File.ReadAllBytesAsync(source);
            var result = await Task.Run(() => PdfToolkit.RotatePages(data, angle, range, password));
            var output = await SaveResultAsync(result, $"xoay{angle}", source);

            ShowSuccess($"Đã xoay {angle}°.", output);
        });
    }

    private void BuildPageSetup()
    {
        var size = Choice(new[] { "Giữ nguyên khổ giấy" }.Concat(PdfPageSizes.Names), 0, 190);
        var keepOrientation = new CheckBox { Content = "Giữ hướng ngang/dọc của trang gốc", IsChecked = true, FontSize = 12 };

        var left = Number(0, 0, 40, 1, 84);
        var right = Number(0, 0, 40, 1, 84);
        var top = Number(0, 0, 40, 1, 84);
        var bottom = Number(0, 0, 40, 1, 84);

        Add(Labeled("Khổ giấy đích", size));
        Add(keepOrientation);
        Add(Labeled("Cắt lề trái / phải (%)", Row(left, right)));
        Add(Labeled("Cắt lề trên / dưới (%)", Row(top, bottom)));
        Add(Hint("Để 0 nếu không muốn cắt lề. Chọn “Giữ nguyên khổ giấy” nếu chỉ cần cắt."));

        AddRun("📐  Áp dụng", async () =>
        {
            var source = RequireFile(".pdf");
            var password = Password();
            var data = await File.ReadAllBytesAsync(source);

            double cropLeft = (double)(left.Value ?? 0), cropRight = (double)(right.Value ?? 0);
            double cropTop = (double)(top.Value ?? 0), cropBottom = (double)(bottom.Value ?? 0);
            bool changeSize = size.SelectedIndex > 0;
            bool crop = cropLeft + cropRight + cropTop + cropBottom > 0;

            if (!changeSize && !crop)
                throw new PdfToolException("Hãy chọn khổ giấy mới hoặc nhập phần lề cần cắt.");

            var result = data;

            if (crop)
            {
                result = await Task.Run(() => PdfToolkit.CropPages(
                    result, cropLeft, cropBottom, cropRight, cropTop, null, password));
            }

            if (changeSize)
            {
                var target = PdfPageSizes.ByName(PdfPageSizes.Names[size.SelectedIndex - 1]);
                // sau khi cắt, tệp trung gian không còn mật khẩu
                result = await Task.Run(() => PdfToolkit.ResizePages(
                    result, target, keepOrientation.IsChecked == true, crop ? null : password));
            }

            var output = await SaveResultAsync(result, "khogiay", source);
            ShowSuccess("Đã áp dụng khổ giấy và phần cắt lề.", output);
        });
    }

    // ── Chuyển đổi ───────────────────────────────────────────────────────
    private void BuildPdfToText()
    {
        var perPage = new CheckBox { Content = "Chèn dấu ngắt giữa các trang", IsChecked = true, FontSize = 12 };
        Add(perPage);

        AddRun("📄  Trích xuất văn bản", async () =>
        {
            var source = RequireFile(".pdf");
            var password = Password();
            var data = await File.ReadAllBytesAsync(source);

            var pages = await Task.Run(() => PdfToolkit.ExtractTextPerPage(data, password));
            var text = perPage.IsChecked == true
                ? string.Join("\n", pages.Select((p, i) => $"───── Trang {i + 1} ─────\n{p}\n"))
                : string.Join("\n", pages);

            var output = Path.Combine(Path.GetDirectoryName(source)!, Path.GetFileNameWithoutExtension(source) + ".txt");
            await File.WriteAllTextAsync(output, text, new UTF8Encoding(true));

            ShowPreview(text);
            ShowSuccess($"Đã trích {text.Length:N0} ký tự từ {pages.Count} trang.", output);
        });
    }

    private void BuildPdfToCsv()
    {
        var separator = Choice(new[] { "Dấu phẩy (,)", "Dấu chấm phẩy (;)", "Tab" }, 1, 190);
        Add(Labeled("Ký tự ngăn cột", separator));
        Add(Hint("Excel bản tiếng Việt thường mở tốt nhất với dấu chấm phẩy."));

        AddRun("📊  Xuất bảng ra CSV", async () =>
        {
            var source = RequireFile(".pdf");
            var password = Password();
            var data = await File.ReadAllBytesAsync(source);

            var rows = await Task.Run(() => PdfToolkit.ExtractTables(data, password));
            if (rows.Count == 0) throw new PdfToolException("Không nhận diện được dữ liệu dạng bảng trong tệp này.");

            char delimiter = separator.SelectedIndex switch { 0 => ',', 2 => '\t', _ => ';' };
            var csv = PdfToolkit.ToCsv(rows, delimiter);

            var output = Path.Combine(Path.GetDirectoryName(source)!, Path.GetFileNameWithoutExtension(source) + ".csv");
            await File.WriteAllTextAsync(output, csv, new UTF8Encoding(true));

            ShowPreview(csv);
            ShowSuccess($"Đã xuất {rows.Count:N0} dòng dữ liệu.", output);
        });
    }

    private void BuildPdfToImages()
    {
        Add(Hint("Lấy ra đúng các ảnh đã nhúng trong tệp (ảnh JPEG giữ nguyên chất lượng gốc). "
                 + "Đây không phải chức năng chụp lại toàn trang thành ảnh."));

        AddRun("🖼  Tách ảnh", async () =>
        {
            var source = RequireFile(".pdf");
            var password = Password();
            var data = await File.ReadAllBytesAsync(source);
            var name = Path.GetFileNameWithoutExtension(source);

            var images = await Task.Run(() => PdfToolkit.ExtractImages(data, name, password));

            var folder = Path.Combine(Path.GetDirectoryName(source)!, $"{name}_anh");
            Directory.CreateDirectory(folder);

            foreach (var image in images)
                await File.WriteAllBytesAsync(Path.Combine(folder, image.FileName), image.Data);

            ShowPreview(string.Join("\n", images.Select(i => $"{i.FileName}   {i.Width}×{i.Height}px   {FormatSize(i.Data.Length)}")));
            ShowSuccess($"Đã tách {images.Count} ảnh.", folder);
        });
    }

    private void BuildImagesToPdf()
    {
        var fit = Choice(new[] { "Đặt vừa trang giấy", "Trang vừa đúng cỡ ảnh" });
        var size = Choice(PdfPageSizes.Names);
        var margin = Number(28, 0, 150, 4, 100);

        Add(Labeled("Cách bố trí", fit));
        Add(Labeled("Khổ giấy", size));
        Add(Labeled("Lề (point)", margin));
        Add(Hint("Thứ tự ảnh trong danh sách tệp chính là thứ tự trang."));

        AddRun("📷  Tạo PDF từ ảnh", async () =>
        {
            var sources = RequireFiles(1, ".png", ".jpg", ".jpeg", ".bmp");
            var images = new List<(string, byte[])>();
            foreach (var path in sources) images.Add((path, await File.ReadAllBytesAsync(path)));

            var options = new PdfImageToPdfOptions
            {
                Fit = fit.SelectedIndex == 1 ? PdfImageFit.MatchImage : PdfImageFit.Fit,
                PageSize = PdfPageSizes.ByName(PdfPageSizes.Names[Math.Max(0, size.SelectedIndex)]),
                Margin = (double)(margin.Value ?? 28)
            };

            var result = await Task.Run(() => PdfToolkit.ImagesToPdf(images, options));
            var output = await SaveResultAsync(result, "anh", sources[0], forceExtension: ".pdf");

            ShowSuccess($"Đã tạo PDF {sources.Count} trang từ ảnh.", output);
        });
    }

    private void BuildToPdf()
    {
        var mode = Choice(new[] { "Tự nhận biết theo tệp", "Ép kiểu văn bản", "Ép kiểu bảng" }, 0, 200);
        var title = Field("Tiêu đề in ở đầu tài liệu (không bắt buộc)", width: 300);
        var size = Choice(PdfPageSizes.Names, 0, 120);
        var landscape = new CheckBox { Content = "In ngang giấy", FontSize = 12 };

        Add(Labeled("Kiểu nội dung", mode));
        Add(Labeled("Tiêu đề", title));
        Add(Labeled("Khổ giấy", Row(size, landscape)));
        Add(Hint("Tệp .csv/.tsv được vẽ thành bảng có khung và tự lặp dòng tiêu đề. "
                 + "Tệp .txt/.md được dàn thành văn bản thường. Phông Unicode nên tiếng Việt đủ dấu."));

        AddRun("📝  Tạo PDF", async () =>
        {
            var source = RequireFile(".txt", ".md", ".csv", ".tsv", ".log");
            var content = await File.ReadAllTextAsync(source);
            var extension = Path.GetExtension(source).ToLowerInvariant();

            bool asTable = mode.SelectedIndex switch
            {
                1 => false,
                2 => true,
                _ => extension is ".csv" or ".tsv"
            };

            var pageSize = PdfPageSizes.ByName(PdfPageSizes.Names[Math.Max(0, size.SelectedIndex)]);
            if (landscape.IsChecked == true) pageSize = PdfPageSizes.Landscape(pageSize);

            var name = string.IsNullOrWhiteSpace(title.Text)
                ? Path.GetFileNameWithoutExtension(source)
                : title.Text!;

            byte[] result;
            string summary;

            if (asTable)
            {
                var rows = PdfToolkit.ParseCsv(content, DetectSeparator(content));
                if (rows.Count == 0) throw new PdfToolException("Tệp không có dữ liệu để dựng bảng.");

                result = await Task.Run(() => PdfToolkit.TableToPdf(rows, new PdfTableToPdfOptions
                {
                    PageSize = pageSize,
                    Title = name,
                    RepeatHeader = true
                }));
                summary = $"Đã dựng bảng {rows.Count:N0} dòng";
            }
            else
            {
                result = await Task.Run(() => PdfToolkit.TextToPdf(content, new PdfTextToPdfOptions
                {
                    PageSize = pageSize,
                    Title = name
                }));
                summary = $"Đã dàn {content.Length:N0} ký tự";
            }

            var output = await SaveResultAsync(result, "pdf", source, forceExtension: ".pdf");
            ShowSuccess($"{summary} thành PDF {PdfToolkit.Open(result).PageCount} trang.", output);
        });
    }

    private static char DetectSeparator(string content)
    {
        var sample = content.Split('\n').Take(5).ToList();
        int semicolons = sample.Sum(l => l.Count(c => c == ';'));
        int commas = sample.Sum(l => l.Count(c => c == ','));
        int tabs = sample.Sum(l => l.Count(c => c == '\t'));

        if (tabs >= semicolons && tabs >= commas && tabs > 0) return '\t';
        return semicolons > commas ? ';' : ',';
    }


    // ─────────────────────────────────────────────────────────────────────
    //  TIỆN ÍCH DÙNG CHUNG
    // ─────────────────────────────────────────────────────────────────────
    private string? Password() => string.IsNullOrEmpty(_passwordBox.Text) ? null : _passwordBox.Text;

    private string RequireFile(params string[] extensions)
    {
        var matched = _files.FirstOrDefault(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
        if (matched != null) return matched;

        throw new PdfToolException(_files.Count == 0
            ? "Chưa chọn tệp nào. Hãy kéo thả tệp vào khung phía trên."
            : $"Công cụ này cần tệp {string.Join(" hoặc ", extensions)}, nhưng danh sách chưa có tệp phù hợp.");
    }

    private List<string> RequireFiles(int minimum, params string[] extensions)
    {
        var matched = _files.Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();

        if (matched.Count < minimum)
            throw new PdfToolException($"Công cụ này cần ít nhất {minimum} tệp {string.Join(" / ", extensions)}. "
                                       + $"Hiện có {matched.Count}.");

        return matched;
    }

    private async Task<string> SaveResultAsync(byte[] data, string suffix, string sourcePath, string? forceExtension = null)
    {
        var directory = Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory();
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = forceExtension ?? Path.GetExtension(sourcePath);
        if (string.IsNullOrEmpty(extension)) extension = ".pdf";

        var path = Path.Combine(directory, $"{name}_{suffix}{extension}");
        int counter = 2;
        while (File.Exists(path))
            path = Path.Combine(directory, $"{name}_{suffix}_{counter++}{extension}");

        await File.WriteAllBytesAsync(path, data);
        return path;
    }

    private async Task<string> SaveManyAsync(IReadOnlyList<PdfOutputFile> files, string sourcePath, string folderSuffix)
    {
        var directory = Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory();
        var folder = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(sourcePath)}_{folderSuffix}");
        Directory.CreateDirectory(folder);

        foreach (var file in files)
            await File.WriteAllBytesAsync(Path.Combine(folder, SafeName(file.FileName)), file.Data);

        return folder;
    }

    private static string SafeName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');
        return name;
    }

    private void ShowPreview(string text)
    {
        OnUiThread(() =>
        {
            _outputBox.Text = text.Length > 60000 ? text[..60000] + "\n… (đã cắt bớt phần hiển thị)" : text;
            _outputViewer.IsVisible = true;
        });
    }

    private void ShowSuccess(string message, string? outputPath)
    {
        _lastStatusMessage = message;
        LogMessage?.Invoke(message);
        OnUiThread(() => RenderSuccess(message, outputPath));
    }

    private void RenderSuccess(string message, string? outputPath)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock
        {
            Text = "✅  " + message,
            FontSize = 12.5,
            Foreground = Accent,
            TextWrapping = TextWrapping.Wrap
        });

        if (outputPath != null)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Đã lưu: " + outputPath,
                FontSize = 11.5,
                Foreground = Muted,
                TextWrapping = TextWrapping.Wrap
            });

            _lastOutputFolder = Directory.Exists(outputPath) ? outputPath : Path.GetDirectoryName(outputPath);

            var open = SecondaryButton("📂  Mở thư mục chứa tệp");
            open.HorizontalAlignment = HorizontalAlignment.Left;
            open.Click += (s, e) => OpenFolder(_lastOutputFolder);
            stack.Children.Add(open);
        }

        _statusCard.Child = stack;
        _statusCard.IsVisible = true;
    }

    private void ShowError(string message)
    {
        OnUiThread(() => RenderError(message));
    }

    private void RenderError(string message)
    {
        _statusCard.Child = new TextBlock
        {
            Text = "❌  " + message,
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.Parse("#B91C1C")),
            TextWrapping = TextWrapping.Wrap
        };
        _statusCard.IsVisible = true;
    }

    private static void OpenFolder(string? folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch
        {
            try
            {
                var opener = OperatingSystem.IsWindows() ? "explorer"
                    : OperatingSystem.IsMacOS() ? "open"
                    : "xdg-open";
                Process.Start(opener, folder);
            }
            catch
            {
                // Không mở được trình quản lý tệp — đường dẫn vẫn hiển thị cho người dùng tự mở.
            }
        }
    }
}
