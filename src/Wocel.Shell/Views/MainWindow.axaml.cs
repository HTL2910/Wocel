using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Input.Platform;
using System;
using System.IO;
using System.Linq;
using Wocel.Core.Contracts;
using Wocel.Core.Diagnostics;
using Wocel.Core.Models;
using Wocel.Core.Services;
using Wocel.Excel.Engine;
using Wocel.Excel.Sessions;
using Wocel.Shell.Controls;
using Wocel.Shell.Services;
using Wocel.Shell.ViewModels;
using Wocel.Word.Sessions;

namespace Wocel.Shell.Views;

public partial class MainWindow : Window
{
    private ShellWorkspaceViewModel? ViewModel => DataContext as ShellWorkspaceViewModel;

    private int _currentRowCount = 100;
    private int _currentColCount = 26;

    private FormulaSuggestionPopup? _formulaSuggestions;
    private int _zoomLevel = 100;

    private UndoHistory<Dictionary<string, CellValue>>? _sheetHistory;
    private UndoHistory<List<DocBlock>>? _documentHistory;
    private int _searchIndex;
    private string _lastSearchTerm = string.Empty;
    private PdfToolsPanel? _pdfToolsPanel;
    private bool _pdfToolsVisible;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        SetupEvents();
        SetupWindowControls();
        SetupTooltips();
        RefreshContextTooltips();
        RenderRecentFiles();
        UpdateEditorVisibility();
    }

    // ─────────────────────────────────────────
    //  EVENT WIRING
    // ─────────────────────────────────────────
    // ─────────────────────────────────────────
    //  EVENT WIRING
    // ─────────────────────────────────────────
    private void SetupEvents()
    {
        // ─── Màn hình chào ───────────────────
        BtnSelectExcelHub.Click += (s, e) => { ViewModel!.SelectedHomeTab = 1; RenderRecentFiles(); };
        BtnSelectWordHub.Click  += (s, e) => { ViewModel!.SelectedHomeTab = 0; RenderRecentFiles(); };

        // ─── Tạo tài liệu mới ────────────────
        BtnNewExcelBook.Click += (s, e) => { ViewModel?.CreateNewExcelWorkbook(); RefreshWorkspace(); AttachExcelCanvasGrid(); };
        BtnNewWordDoc.Click   += (s, e) => { ViewModel?.CreateNewWordDocument();  RefreshWorkspace(); };

        // ─── Mở / lưu tệp ────────────────────
        BtnBrowseFileFromDisk.Click += async (s, e) => await SafeRunAsync("Mở tệp", OpenFileDialogAsync);
        BtnQuickOpen.Click          += async (s, e) => await SafeRunAsync("Mở tệp", OpenFileDialogAsync);
        BtnQuickSave.Click          += async (s, e) => await SafeRunAsync("Lưu", SaveFileDialogAsync);

        // ─── Bộ công cụ xử lý tệp ────────────
        BtnOpenPdfToolsHub.Click += (s, e) =>
        {
            ViewModel?.EnsureWorkspaceVisible();
            ShowPdfTools(true);
        };
        BtnTogglePdfTools.Click += (s, e) => ShowPdfTools(!_pdfToolsVisible);
        BtnOpenCapture.Click += (s, e) => OpenCapture();
        BtnOpenCaptureHub.Click += (s, e) => OpenCapture();

        // ─── Về trang chủ ────────────────────
        BtnBackToHome.Click += (s, e) => { ViewModel?.ShowWelcomeScreen(); ShowPdfTools(false); RenderRecentFiles(); };

        // ─── Thêm thẻ tài liệu ───────────────
        BtnAddExcelTab.Click += (s, e) => { ViewModel?.CreateNewExcelWorkbook(); RefreshWorkspace(); AttachExcelCanvasGrid(); };
        BtnAddWordTab.Click  += (s, e) => { ViewModel?.CreateNewWordDocument();  RefreshWorkspace(); };

        // ─── Mở rộng lưới ────────────────────
        BtnExpand100RowsTop.Click += (s, e) => ExpandRows(100);
        BtnExpandColsTop.Click    += (s, e) => ExpandCols(6);

        // ─── Thanh công thức ─────────────────
        BtnApplyFormulaCheck.Click += (s, e) => ApplyCurrentFormula();
        TxtFormulaInput.KeyDown    += (s, e) => { if (e.Key == Key.Enter) ApplyCurrentFormula(); };

        // Gợi ý tên hàm ngay trên thanh công thức.
        _formulaSuggestions = FormulaSuggestionPopup.Attach(TxtFormulaInput);

        // ─── Bảng tạm ────────────────────────
        BtnCopy.Click  += (s, e) => CopySelection();
        BtnCut.Click   += (s, e) => CutSelection();
        BtnPaste.Click += (s, e) => PasteSelection();

        // ─── Định dạng chữ ───────────────────
        BtnBold.Click      += (s, e) => ToggleCellStyle(BtnBold,      st => st.IsBold = !st.IsBold,           () => ViewModel?.ToggleBold());
        BtnItalic.Click    += (s, e) => ToggleCellStyle(BtnItalic,    st => st.IsItalic = !st.IsItalic,       () => ViewModel?.ToggleItalic());
        BtnUnderline.Click += (s, e) => ToggleCellStyle(BtnUnderline, st => st.IsUnderline = !st.IsUnderline, () => ViewModel?.ToggleUnderline());

        CboFontFamily.SelectionChanged += (s, e) =>
        {
            if (CboFontFamily.SelectedItem is not ComboBoxItem item || item.Content is not string font) return;

            if (ViewModel?.ActiveTab?.Session is WordDocumentSession wordSession)
            {
                RecordUndoPoint();
                bool hadSelection = WordDocumentEditor.HasSelection;
                int changed = WordDocumentEditor.ApplyFormatting(run => run.FontFamily = font);

                wordSession.MarkDirty();
                WordDocumentEditor.Focus();

                ShowStatusMsg(hadSelection
                    ? $"Đã đổi phông {changed} đoạn chữ sang {font}."
                    : $"Phông {font} sẽ áp cho phần chữ bạn gõ tiếp theo.");
                return;
            }

            if (ActiveExcel is { } sheetSession)
            {
                RecordUndoPoint();
                int changed = MainExcelCanvasGrid.ApplyStyleToSelection(style => style.FontFamily = font);

                sheetSession.MarkDirty();
                RefreshExcelCanvas();
                MainExcelCanvasGrid.Focus();

                ShowStatusMsg($"Đã đổi phông {changed} ô sang {font}.");
                return;
            }

            ViewModel?.SetFontFamily(font);
        };

        CboFontSize.SelectionChanged += (s, e) =>
        {
            if (CboFontSize.SelectedItem is ComboBoxItem item
                && int.TryParse(item.Content?.ToString(), out int size))
                ApplyFontSize(size);
        };

        BtnFontSizeUp.Click   += (s, e) => StepFontSize(+1);
        BtnFontSizeDown.Click += (s, e) => StepFontSize(-1);

        // ─── Căn lề ──────────────────────────
        BtnAlignLeft.Click   += (s, e) => ApplyAlignment("Left");
        BtnAlignCenter.Click += (s, e) => ApplyAlignment("Center");
        BtnAlignRight.Click  += (s, e) => ApplyAlignment("Right");

        // ─── Gộp ô ───────────────────────────
        BtnMergeCenter.Click += (s, e) => ToggleMergeCells();

        // ─── Định dạng số ────────────────────
        CboNumberFormat.SelectionChanged += (s, e) => ApplyNumberFormat();
        BtnFormatCurrency.Click += (s, e) => ApplyCellNumberFormat("currency");
        BtnFormatPercent.Click  += (s, e) => ApplyCellNumberFormat("percent");

        // ─── Công cụ dữ liệu ─────────────────
        BtnAutoSum.Click  += (s, e) => SafeRun("AutoSum", () => { ViewModel?.AutoSum(); RefreshExcelCanvas(); });
        BtnSortAsc.Click  += (s, e) => SafeRun("Sắp xếp", () => SortActiveColumn(true));
        BtnSortDesc.Click += (s, e) => SafeRun("Sắp xếp", () => SortActiveColumn(false));

        // ─── Điều hướng PDF ──────────────────
        BtnPdfPrevPage.Click  += (s, e) => NavigatePdf(-1);
        BtnPdfNextPage.Click  += (s, e) => NavigatePdf(+1);
        BtnPdfPrevPage2.Click += (s, e) => NavigatePdf(-1);
        BtnPdfNextPage2.Click += (s, e) => NavigatePdf(+1);

        BtnExportPdfToWord.Click += (s, e) => ExportPdfTextToWordTab();

        // ─── Thẻ ribbon ──────────────────────
        BtnTabFile.Click    += (s, e) => SelectRibbonTab("file");
        BtnTabHome.Click    += (s, e) => SelectRibbonTab("home");
        BtnTabInsert.Click  += (s, e) => SelectRibbonTab("insert");
        BtnTabLayout.Click  += (s, e) => SelectRibbonTab("layout");
        BtnTabFormula.Click += (s, e) => SelectRibbonTab("formula");
        BtnTabData.Click    += (s, e) => SelectRibbonTab("data");
        BtnTabView.Click    += (s, e) => SelectRibbonTab("view");

        // ─── Thẻ Tệp ─────────────────────────
        BtnFileNewExcel.Click += (s, e) => { ViewModel?.CreateNewExcelWorkbook(); RefreshWorkspace(); AttachExcelCanvasGrid(); };
        BtnFileNewWord.Click  += (s, e) => { ViewModel?.CreateNewWordDocument(); RefreshWorkspace(); };
        BtnFileOpen.Click     += async (s, e) => await SafeRunAsync("Mở tệp", OpenFileDialogAsync);
        BtnFileSave.Click     += async (s, e) => await SafeRunAsync("Lưu", SaveFileDialogAsync);
        BtnFileSaveAs.Click   += async (s, e) => await SafeRunAsync("Lưu thành", SaveFileAsDialogAsync);
        BtnFileTools.Click    += (s, e) => ShowPdfTools(true);
        BtnFileCloseTab.Click += (s, e) =>
        {
            if (ViewModel?.ActiveTab is { } tab)
            {
                ViewModel.CloseTab(tab);
                RefreshWorkspace();
            }
        };

        // ─── Thẻ Chèn ────────────────────────
        BtnInsertRow.Click    += (s, e) => SafeRun("Chèn dòng", InsertRows);
        BtnInsertColumn.Click += (s, e) => SafeRun("Chèn cột", InsertColumns);
        BtnDeleteRow.Click    += (s, e) => SafeRun("Xoá dòng", DeleteRows);
        BtnDeleteColumn.Click += (s, e) => SafeRun("Xoá cột", DeleteColumns);
        BtnInsertToday.Click  += (s, e) => WriteToActiveCell(DateTime.Today.ToString("dd/MM/yyyy"));
        BtnInsertSheet.Click  += (s, e) => AddSheet();

        // ─── Thẻ Bố trí trang ────────────────
        BtnExportSheetToPdf.Click += async (s, e) => await SafeRunAsync("Xuất PDF", ExportToPdfAsync);
        BtnExportSheetToCsv.Click += async (s, e) => await SafeRunAsync("Xuất CSV", ExportSheetToCsvAsync);

        // ─── Thẻ Công thức ───────────────────
        BtnFormulaAutoSum.Click += (s, e) => InsertAggregateFormula("SUM");
        BtnFormulaAverage.Click += (s, e) => InsertAggregateFormula("AVERAGE");
        BtnFormulaCount.Click   += (s, e) => InsertAggregateFormula("COUNT");
        BtnFormulaMax.Click     += (s, e) => InsertAggregateFormula("MAX");
        BtnFormulaMin.Click     += (s, e) => InsertAggregateFormula("MIN");
        BtnFormulaList.Click    += (s, e) => SafeRun("Danh mục hàm", ShowFunctionCatalog);
        BtnRecalculate.Click    += (s, e) =>
        {
            if (ViewModel?.ActiveTab?.Session is ExcelDocumentSession session)
            {
                session.Recalculate();
                RefreshExcelCanvas();
                ShowStatusMsg("Đã tính lại toàn bộ công thức.");
            }
        };

        // ─── Thẻ Dữ liệu ─────────────────────
        BtnDataSortAsc.Click     += (s, e) => SafeRun("Sắp xếp", () => SortActiveColumn(true));
        BtnDataSortDesc.Click    += (s, e) => SafeRun("Sắp xếp", () => SortActiveColumn(false));
        BtnRemoveDuplicates.Click += (s, e) => SafeRun("Xoá dòng trùng", RemoveDuplicateRows);
        BtnTrimSpaces.Click      += (s, e) => SafeRun("Dọn khoảng trắng", TrimSelectionSpaces);
        BtnClearContents.Click   += (s, e) => { MainExcelCanvasGrid.ClearSelectedCells(); RefreshExcelCanvas(); };

        // ─── Thẻ Xem ─────────────────────────
        BtnZoomIn.Click    += (s, e) => ApplyZoom(_zoomLevel + 10);
        BtnZoomOut.Click   += (s, e) => ApplyZoom(_zoomLevel - 10);
        BtnZoomReset.Click += (s, e) => ApplyZoom(100);
        BtnGoToCell.Click  += (s, e) => GoToCellFromFormulaBar();
        BtnFitColumns.Click += (s, e) => SafeRun("Vừa cột", FitColumnsToContent);

        // ─── Huỷ công thức đang gõ ───────────
        BtnCancelFormula.Click += (s, e) =>
        {
            _formulaSuggestions?.Close();
            var address = ViewModel?.CurrentCellAddress ?? "A1";
            if (ViewModel?.ActiveTab?.Session is ExcelDocumentSession session)
            {
                var cell = session.Document.GetOrCreateActiveSheet().GetCell(address);
                ViewModel!.CurrentFormula = cell.Formula ?? cell.RawValue?.ToString() ?? string.Empty;
            }
            MainExcelCanvasGrid.Focus();
            ShowStatusMsg($"Đã huỷ chỉnh sửa ô {address}.");
        };

        // ─── Thanh trang tính ────────────────
        BtnAddSheet.Click  += (s, e) => AddSheet();
        BtnPrevSheet.Click += (s, e) => SwitchSheet(-1);
        BtnNextSheet.Click += (s, e) => SwitchSheet(+1);

        // ─── Ô tìm kiếm ──────────────────────
        TxtSearchBox.KeyDown += (s, e) =>
        {
            if (e.Key != Key.Enter) return;
            FindNext(TxtSearchBox.Text ?? string.Empty);
            e.Handled = true;
        };

        // Thanh trượt thu phóng dưới thanh trạng thái, đồng bộ hai chiều với nút ở thẻ Xem.
        SliderZoom.PropertyChanged += (s, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            int percent = (int)Math.Round(SliderZoom.Value);
            if (percent != _zoomLevel) ApplyZoom(percent);
        };

        SelectRibbonTab("home");
    }






    // ─────────────────────────────────────────
    //  ĐIỀU KHIỂN CỬA SỔ
    // ─────────────────────────────────────────
    /// <summary>
    /// Ứng dụng tự vẽ khung cửa sổ nên phải tự lo thu nhỏ, phóng to, đóng
    /// và cho kéo cửa sổ bằng thanh tiêu đề.
    /// </summary>
    private void SetupWindowControls()
    {
        BtnWindowMinimize.Click += (s, e) => WindowState = Avalonia.Controls.WindowState.Minimized;
        BtnWindowMaximize.Click += (s, e) => ToggleMaximize();
        BtnWindowClose.Click    += (s, e) => Close();

        // Kéo thanh tiêu đề để di chuyển; bấm đúp để phóng to hoặc thu về.
        TitleBar.PointerPressed += (s, e) =>
        {
            if (!e.GetCurrentPoint(TitleBar).Properties.IsLeftButtonPressed) return;

            if (e.ClickCount >= 2)
            {
                ToggleMaximize();
                e.Handled = true;
                return;
            }

            BeginMoveDrag(e);
        };

        // Biểu tượng nút giữa đổi theo trạng thái, đúng như Windows.
        PropertyChanged += (s, e) =>
        {
            if (e.Property == WindowStateProperty) UpdateMaximizeButton();
        };

        UpdateMaximizeButton();
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == Avalonia.Controls.WindowState.Maximized
            ? Avalonia.Controls.WindowState.Normal
            : Avalonia.Controls.WindowState.Maximized;

    private void UpdateMaximizeButton()
    {
        bool maximized = WindowState == Avalonia.Controls.WindowState.Maximized;

        BtnWindowMaximize.Content = maximized ? "❐" : "☐";
        Tip(BtnWindowMaximize,
            maximized ? "Khôi phục" : "Phóng to",
            maximized ? "Đưa cửa sổ về kích thước trước đó." : "Mở rộng cửa sổ ra toàn màn hình.");
    }

    // ─────────────────────────────────────────
    //  CHẠY LỆNH AN TOÀN
    // ─────────────────────────────────────────
    /// <summary>
    /// Bọc mọi lệnh trên ribbon. Không có lớp này, một ngoại lệ trong handler
    /// async void sẽ biến mất không dấu vết và người dùng tưởng nút bị hỏng.
    /// </summary>
    private void SafeRun(string command, Action action)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            ReportCommandError(command, error);
        }
    }

    private async Task SafeRunAsync(string command, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception error)
        {
            ReportCommandError(command, error);
        }
    }

    private void ReportCommandError(string command, Exception error)
    {
        var message = error is PdfToolException or NotSupportedException or InvalidDataException
            ? error.Message
            : $"{command} gặp lỗi: {error.Message}";

        ShowStatusMsg("❌ " + message);
        ActivityLog.Error("ui", command, error);
    }


    // ─────────────────────────────────────────
    //  HOÀN TÁC / LÀM LẠI
    // ─────────────────────────────────────────
    /// <summary>Ghi lại trạng thái trước khi sửa, để Ctrl+Z quay về được.</summary>
    private void RecordUndoPoint()
    {
        if (ViewModel?.ActiveTab?.Session is ExcelDocumentSession) _sheetHistory?.Record();
        else if (ViewModel?.ActiveTab?.Session is WordDocumentSession) _documentHistory?.Record();
    }

    private void UndoLastChange()
    {
        bool done = ViewModel?.ActiveTab?.Session switch
        {
            ExcelDocumentSession => _sheetHistory?.Undo() ?? false,
            WordDocumentSession => _documentHistory?.Undo() ?? false,
            _ => false
        };

        ShowStatusMsg(done ? "Đã hoàn tác." : "Không còn thao tác nào để hoàn tác.");
    }

    private void RedoLastChange()
    {
        bool done = ViewModel?.ActiveTab?.Session switch
        {
            ExcelDocumentSession => _sheetHistory?.Redo() ?? false,
            WordDocumentSession => _documentHistory?.Redo() ?? false,
            _ => false
        };

        ShowStatusMsg(done ? "Đã làm lại." : "Không còn thao tác nào để làm lại.");
    }

    // ─────────────────────────────────────────
    //  PHÍM TẮT TOÀN CỬA SỔ
    // ─────────────────────────────────────────
    /// <summary>
    /// Các phím tắt chung. Lưới và trình soạn thảo tự xử lý phím của riêng chúng
    /// (và đánh dấu đã xử lý), nên ở đây chỉ nhận phần còn lại.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;

        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (!control) return;

        switch (e.Key)
        {
            case Key.S:
                _ = SaveFileDialogAsync();
                break;

            case Key.O:
                _ = OpenFileDialogAsync();
                break;

            case Key.N:
                ViewModel?.CreateNewExcelWorkbook();
                RefreshWorkspace();
                AttachExcelCanvasGrid();
                break;

            case Key.F:
                TxtSearchBox.Focus();
                TxtSearchBox.SelectAll();
                break;

            case Key.Z:
                UndoLastChange();
                break;

            case Key.Y:
                RedoLastChange();
                break;

            case Key.C:
                CopySelection();
                break;

            case Key.X:
                CutSelection();
                break;

            case Key.V:
                PasteSelection();
                break;

            case Key.W:
                if (ViewModel?.ActiveTab is { } closing)
                {
                    ViewModel.CloseTab(closing);
                    RefreshWorkspace();
                }
                break;

            case Key.B:
                ToggleCellStyle(BtnBold, st => st.IsBold = !st.IsBold, () => ViewModel?.ToggleBold());
                break;

            case Key.I:
                ToggleCellStyle(BtnItalic, st => st.IsItalic = !st.IsItalic, () => ViewModel?.ToggleItalic());
                break;

            case Key.U:
                ToggleCellStyle(BtnUnderline, st => st.IsUnderline = !st.IsUnderline, () => ViewModel?.ToggleUnderline());
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    // ─────────────────────────────────────────
    //  TOOLTIP
    // ─────────────────────────────────────────
    /// <summary>
    /// Gắn chú thích cho từng lệnh: dòng tên đậm, dòng mô tả xám, kèm phím tắt —
    /// cùng kiểu với Word và Excel. Phím tắt ghi ở đây đều là phím thật đã nối.
    /// </summary>
    private void Tip(Control? control, string title, string description, string? shortcut = null)
    {
        if (control == null) return;

        // Phải dùng chuỗi, KHÔNG dùng đối tượng Control làm nội dung tooltip:
        // Avalonia đưa control đó vào popup khi hiện, đóng lại thì lần sau
        // không dựng lại được nữa và tooltip im luôn.
        var heading = shortcut is { Length: > 0 } ? $"{title}   ·   {shortcut}" : title;

        ToolTip.SetTip(control, $"{heading}\n{description}");
        ToolTip.SetShowDelay(control, 350);
    }


    /// <summary>
    /// Chú thích cho những lệnh làm việc khác nhau giữa bảng tính và văn bản.
    /// Gọi lại mỗi khi đổi thẻ tài liệu để chữ nghĩa luôn khớp thứ đang mở.
    /// </summary>
    private void RefreshContextTooltips()
    {
        string ctrl = OperatingSystem.IsMacOS() ? "⌘" : "Ctrl+";
        bool sheet = ViewModel?.ActiveTab?.Session is ExcelDocumentSession;

        if (sheet)
        {
            Tip(BtnBold, "Đậm", "In đậm chữ trong các ô đang chọn.", $"{ctrl}B");
            Tip(BtnItalic, "Nghiêng", "In nghiêng chữ trong các ô đang chọn.", $"{ctrl}I");
            Tip(BtnUnderline, "Gạch chân", "Gạch chân chữ trong các ô đang chọn.", $"{ctrl}U");

            Tip(CboFontFamily, "Phông chữ", "Đổi phông cho các ô đang chọn.");
            Tip(CboFontSize, "Cỡ chữ", "Đổi cỡ chữ cho các ô đang chọn. Cần cỡ không có trong danh sách thì dùng A▴ A▾.");
            Tip(BtnFontSizeUp, "Tăng cỡ chữ", "Tăng 1 đơn vị cho các ô đang chọn.");
            Tip(BtnFontSizeDown, "Giảm cỡ chữ", "Giảm 1 đơn vị cho các ô đang chọn.");

            Tip(BtnAlignLeft, "Căn trái", "Canh nội dung về mép trái ô.");
            Tip(BtnAlignCenter, "Căn giữa", "Canh nội dung vào giữa ô.");
            Tip(BtnAlignRight, "Căn phải", "Canh nội dung về mép phải ô.");
            Tip(BtnMergeCenter, "Gộp và căn giữa", "Gộp vùng ô đang chọn thành một ô rồi canh giữa. Bấm lần nữa để bỏ gộp.");

            Tip(BtnCopy, "Sao chép", "Sao chép vùng ô đang chọn. Dán được thẳng sang Excel hay Google Sheets.", $"{ctrl}C");
            Tip(BtnCut, "Cắt", "Sao chép vùng ô rồi xoá nội dung gốc.", $"{ctrl}X");
            Tip(BtnPaste, "Dán", "Dán vào từ ô đang chọn. Dữ liệu nhiều cột cách nhau bằng Tab sẽ trải ra đúng vùng.", $"{ctrl}V");

            Tip(TxtSearchBox, "Tìm trong trang tính", "Gõ nội dung rồi Enter; mỗi lần Enter nhảy tới ô khớp tiếp theo.", $"{ctrl}F");
            Tip(BtnClearContents, "Xoá nội dung", "Xoá dữ liệu trong vùng ô đang chọn, giữ nguyên định dạng.", "Delete");
            return;
        }

        // Tài liệu văn bản
        Tip(BtnBold, "Đậm", "In đậm phần chữ bôi đen. Chưa bôi đen thì áp cho chữ bạn gõ tiếp theo.", $"{ctrl}B");
        Tip(BtnItalic, "Nghiêng", "In nghiêng phần chữ bôi đen, hoặc chữ bạn gõ tiếp theo.", $"{ctrl}I");
        Tip(BtnUnderline, "Gạch chân", "Gạch chân phần chữ bôi đen, hoặc chữ bạn gõ tiếp theo.", $"{ctrl}U");

        Tip(CboFontFamily, "Phông chữ", "Đổi phông cho phần chữ bôi đen, hoặc cho chữ sắp gõ.");
        Tip(CboFontSize, "Cỡ chữ", "Đổi cỡ cho phần chữ bôi đen, hoặc cho chữ sắp gõ. Cỡ khác thì dùng A▴ A▾.");
        Tip(BtnFontSizeUp, "Tăng cỡ chữ", "Tăng 1 đơn vị cho phần chữ bôi đen.");
        Tip(BtnFontSizeDown, "Giảm cỡ chữ", "Giảm 1 đơn vị cho phần chữ bôi đen.");

        Tip(BtnAlignLeft, "Căn trái", "Canh đoạn văn về lề trái.");
        Tip(BtnAlignCenter, "Căn giữa", "Canh đoạn văn vào giữa trang.");
        Tip(BtnAlignRight, "Căn phải", "Canh đoạn văn về lề phải.");

        Tip(BtnCopy, "Sao chép", "Sao chép phần chữ đang bôi đen.", $"{ctrl}C");
        Tip(BtnCut, "Cắt", "Xoá phần chữ bôi đen và đưa vào bảng tạm.", $"{ctrl}X");
        Tip(BtnPaste, "Dán", "Dán nội dung từ bảng tạm vào vị trí con trỏ.", $"{ctrl}V");

        Tip(TxtSearchBox, "Tìm trong tài liệu", "Gõ nội dung rồi Enter; mỗi lần Enter nhảy tới chỗ khớp tiếp theo.", $"{ctrl}F");
        Tip(BtnClearContents, "Xoá nội dung", "Xoá phần chữ đang bôi đen.", "Delete");
    }

    private void SetupTooltips()
    {
        string ctrl = OperatingSystem.IsMacOS() ? "⌘" : "Ctrl+";

        // ── Màn hình chào ────────────────────
        Tip(BtnSelectExcelHub, "Wocel Excel", "Chuyển màn hình chào sang chế độ bảng tính.");
        Tip(BtnSelectWordHub, "Wocel Word", "Chuyển màn hình chào sang chế độ soạn thảo văn bản.");
        Tip(BtnNewExcelBook, "Sổ tính mới", "Tạo bảng tính trống 100 dòng × 26 cột.", $"{ctrl}N");
        Tip(BtnNewWordDoc, "Tài liệu mới", "Tạo tài liệu văn bản trống khổ A4.");
        Tip(BtnBrowseFileFromDisk, "Mở tệp", "Mở .xlsx, .csv, .docx, .rtf, .txt, .pdf hoặc .wocel.", $"{ctrl}O");
        Tip(BtnOpenPdfToolsHub, "Bộ công cụ xử lý tệp", "29 công cụ PDF, ảnh và bảng tính, chạy ngoại tuyến trên máy bạn.");
        Tip(BtnOpenCaptureHub, "Wocel Capture", "Mở công cụ chụp ảnh màn hình (chạy nền ở khay hệ thống).");

        // ── Thanh lệnh nhanh ─────────────────
        Tip(BtnBackToHome, "Trang chủ", "Quay về màn hình chào. Các thẻ đang mở vẫn được giữ.");
        Tip(BtnWindowMinimize, "Thu nhỏ", "Thu cửa sổ xuống thanh tác vụ.");
        Tip(BtnWindowClose, "Đóng", "Thoát Wocel Office. Nhớ lưu tài liệu trước.");
        Tip(BtnQuickSave, "Lưu", "Ghi tài liệu hiện tại xuống tệp.", $"{ctrl}S");
        Tip(BtnQuickOpen, "Mở tệp", "Chọn tệp trên máy để mở trong thẻ mới.", $"{ctrl}O");
        Tip(BtnAddExcelTab, "Thêm bảng tính", "Mở thêm một sổ tính trong thẻ mới.");
        Tip(BtnAddWordTab, "Thêm tài liệu", "Mở thêm một tài liệu văn bản trong thẻ mới.");
        Tip(BtnTogglePdfTools, "Bộ công cụ tệp", "Bật/tắt bảng công cụ xử lý PDF, ảnh và bảng tính.");
        Tip(BtnOpenCapture, "Wocel Capture", "Mở công cụ chụp ảnh màn hình (chạy nền ở khay hệ thống).");

        // ── Thẻ ribbon ───────────────────────
        Tip(BtnTabFile, "Tệp", "Tạo mới, mở, lưu, đóng thẻ và mở bộ công cụ tệp.");
        Tip(BtnTabHome, "Trang đầu", "Bảng tạm, phông chữ, căn lề, định dạng số và các lệnh hay dùng.");
        Tip(BtnTabInsert, "Chèn", "Chèn hoặc xoá dòng, cột, ngày tháng và trang tính.");
        Tip(BtnTabLayout, "Bố trí trang", "Khổ giấy, hướng giấy và xuất trang tính ra PDF hoặc CSV.");
        Tip(BtnTabFormula, "Công thức", "Hàm tổng hợp nhanh, danh mục 53 hàm và tính lại toàn bộ.");
        Tip(BtnTabData, "Dữ liệu", "Sắp xếp, xoá dòng trùng, dọn khoảng trắng, xoá nội dung vùng chọn.");
        Tip(BtnTabView, "Xem", "Thu phóng, nhảy tới ô và điều chỉnh bề rộng cột.");

        // ── Bảng tạm ─────────────────────────
        Tip(BtnFileCloseTab, "Đóng thẻ", "Đóng tài liệu đang mở. Nhớ lưu trước nếu còn thay đổi.", $"{ctrl}W");

        // ── Phông chữ ────────────────────────

        // ── Căn lề & gộp ô ───────────────────

        // ── Định dạng số ─────────────────────
        Tip(CboNumberFormat, "Định dạng số", "Chung, số nguyên, thập phân, nghìn, tiền tệ, phần trăm, ngày tháng hoặc khoa học.");
        Tip(BtnFormatCurrency, "Tiền tệ", "Hiển thị dạng 1.500.000 ₫. Giá trị gốc không đổi.");
        Tip(BtnFormatPercent, "Phần trăm", "Hiển thị dạng 12,5%. Giá trị gốc không đổi.");

        // ── Lệnh nhanh trên thẻ Trang đầu ────
        Tip(BtnAutoSum, "AutoSum", "Cộng dãy số phía trên ô hiện tại và đặt kết quả vào ô đó.");
        Tip(BtnSortAsc, "Sắp xếp A→Z", "Sắp xếp cột đang chọn theo thứ tự tăng dần.");
        Tip(BtnSortDesc, "Sắp xếp Z→A", "Sắp xếp cột đang chọn theo thứ tự giảm dần.");

        // ── Điều hướng PDF ───────────────────
        Tip(BtnPdfPrevPage, "Trang trước", "Lùi một trang trong tệp PDF đang xem.");
        Tip(BtnPdfNextPage, "Trang tiếp", "Tiến một trang trong tệp PDF đang xem.");
        Tip(BtnPdfPrevPage2, "Trang trước", "Lùi một trang trong tệp PDF đang xem.");
        Tip(BtnPdfNextPage2, "Trang tiếp", "Tiến một trang trong tệp PDF đang xem.");
        Tip(BtnExportPdfToWord, "Xuất ra Word", "Tạo tài liệu Word mới chứa toàn bộ văn bản trích từ PDF.");

        // ── Thẻ Tệp ──────────────────────────
        Tip(BtnFileNewExcel, "Sổ tính mới", "Tạo bảng tính trống trong thẻ mới.");
        Tip(BtnFileNewWord, "Tài liệu mới", "Tạo tài liệu văn bản trống trong thẻ mới.");
        Tip(BtnFileOpen, "Mở tệp", "Chọn tệp trên máy để mở.", $"{ctrl}O");
        Tip(BtnFileSave, "Lưu", "Ghi đè lên tệp hiện tại.", $"{ctrl}S");
        Tip(BtnFileSaveAs, "Lưu thành", "Chọn vị trí và tên tệp mới để lưu bản sao.");
        Tip(BtnFileTools, "Bộ công cụ tệp", "Mở bảng 29 công cụ xử lý PDF, ảnh và bảng tính.");

        // ── Thẻ Chèn ─────────────────────────
        Tip(BtnInsertRow, "Chèn dòng", "Chèn số dòng bằng đúng số dòng đang chọn, ngay phía trên.");
        Tip(BtnInsertColumn, "Chèn cột", "Chèn số cột bằng đúng số cột đang chọn, ngay bên trái.");
        Tip(BtnDeleteRow, "Xoá dòng", "Xoá các dòng đang chọn và dồn dữ liệu bên dưới lên.");
        Tip(BtnDeleteColumn, "Xoá cột", "Xoá các cột đang chọn và dồn dữ liệu bên phải sang.");
        Tip(BtnInsertToday, "Chèn ngày hôm nay", "Ghi ngày hiện tại vào ô đang chọn.");
        Tip(BtnInsertSheet, "Trang tính mới", "Thêm một trang tính vào sổ tính hiện tại.");
        Tip(BtnExpand100RowsTop, "Thêm 100 dòng", "Mở rộng lưới thêm 100 dòng.");
        Tip(BtnExpandColsTop, "Thêm 6 cột", "Mở rộng lưới thêm 6 cột.");

        // ── Thẻ Bố trí trang ─────────────────
        Tip(CboExportPageSize, "Khổ giấy", "Khổ giấy dùng khi xuất trang tính ra PDF.");
        Tip(ChkExportLandscape, "In ngang giấy", "Xoay ngang trang PDF — hợp với bảng nhiều cột.");
        Tip(BtnExportSheetToPdf, "Xuất ra PDF", "Bảng tính được vẽ thành bảng có khung; tài liệu văn bản giữ nguyên đậm, nghiêng, gạch chân và cỡ chữ.");
        Tip(BtnExportSheetToCsv, "Xuất ra CSV", "Lưu dữ liệu dạng văn bản ngăn bằng dấu chấm phẩy, mở được bằng Excel.");

        // ── Thẻ Công thức ────────────────────
        Tip(BtnFormulaAutoSum, "Tổng", "Chèn =SUM cho vùng đang chọn, kết quả đặt ngay dưới vùng.");
        Tip(BtnFormulaAverage, "Trung bình", "Chèn =AVERAGE cho vùng đang chọn.");
        Tip(BtnFormulaCount, "Đếm", "Chèn =COUNT đếm số ô chứa số trong vùng chọn.");
        Tip(BtnFormulaMax, "Lớn nhất", "Chèn =MAX tìm giá trị lớn nhất trong vùng chọn.");
        Tip(BtnFormulaMin, "Nhỏ nhất", "Chèn =MIN tìm giá trị nhỏ nhất trong vùng chọn.");
        Tip(BtnFormulaList, "Danh mục hàm", "Mở bảng tra cứu toàn bộ 53 hàm kèm cú pháp và mô tả.");
        Tip(BtnRecalculate, "Tính lại", "Tính lại mọi công thức trong trang tính hiện tại.");

        // ── Thẻ Dữ liệu ──────────────────────
        Tip(BtnDataSortAsc, "Sắp xếp A→Z", "Sắp xếp cột đang chọn theo thứ tự tăng dần.");
        Tip(BtnDataSortDesc, "Sắp xếp Z→A", "Sắp xếp cột đang chọn theo thứ tự giảm dần.");
        Tip(BtnRemoveDuplicates, "Xoá dòng trùng", "Trong vùng chọn, giữ lại dòng đầu tiên của mỗi nhóm giống nhau.");
        Tip(BtnTrimSpaces, "Bỏ khoảng trắng thừa", "Cắt khoảng trắng đầu cuối và gộp khoảng trắng liên tiếp thành một.");

        // ── Thẻ Xem ──────────────────────────
        Tip(BtnZoomOut, "Thu nhỏ", "Giảm 10% mỗi lần bấm, nhỏ nhất 50%.");
        Tip(BtnZoomIn, "Phóng to", "Tăng 10% mỗi lần bấm, lớn nhất 200%.");
        Tip(BtnZoomReset, "Về 100%", "Đưa mức thu phóng về kích thước thật.");
        Tip(BtnGoToCell, "Nhảy tới ô", "Gõ địa chỉ vào ô bên trái thanh công thức rồi bấm nút này.");
        Tip(BtnFitColumns, "Vừa cột theo nội dung", "Đặt bề rộng cột theo ô có nội dung dài nhất.");
        Tip(SliderZoom, "Thu phóng", "Kéo để phóng to hoặc thu nhỏ lưới, từ 50% đến 200%.");

        // ── Thanh công thức ──────────────────
        Tip(TxtSelectionRange, "Ô hoặc vùng đang chọn", "Hiện B2, B2:D5 hoặc số vùng khi chọn nhiều mảng rời.");
        Tip(BtnCancelFormula, "Huỷ", "Bỏ nội dung đang gõ và khôi phục giá trị cũ của ô.", "Esc");
        Tip(BtnApplyFormulaCheck, "Xác nhận", "Ghi nội dung đang gõ vào ô đang chọn.", "Enter");
        Tip(TxtFormulaInput, "Thanh công thức", "Nhập giá trị hoặc công thức. Gõ dấu = để hiện danh sách gợi ý hàm.");

        // ── Dải trang tính ───────────────────
        Tip(BtnPrevSheet, "Trang tính trước", "Chuyển sang trang tính bên trái.");
        Tip(BtnNextSheet, "Trang tính sau", "Chuyển sang trang tính bên phải.");
        Tip(BtnAddSheet, "Thêm trang tính", "Tạo trang tính mới. Bấm chuột phải vào thẻ để xoá.");
    }

    // ─────────────────────────────────────────
    //  RIBBON
    // ─────────────────────────────────────────
    private string _activeRibbonTab = "home";

    private void SelectRibbonTab(string tab)
    {
        _activeRibbonTab = tab;

        RibbonFile.IsVisible    = tab == "file";
        RibbonHome.IsVisible    = tab == "home";
        RibbonInsert.IsVisible  = tab == "insert";
        RibbonLayout.IsVisible  = tab == "layout";
        RibbonFormula.IsVisible = tab == "formula";
        RibbonData.IsVisible    = tab == "data";
        RibbonView.IsVisible    = tab == "view";

        HighlightRibbonTab(BtnTabFile, tab == "file");
        HighlightRibbonTab(BtnTabHome, tab == "home");
        HighlightRibbonTab(BtnTabInsert, tab == "insert");
        HighlightRibbonTab(BtnTabLayout, tab == "layout");
        HighlightRibbonTab(BtnTabFormula, tab == "formula");
        HighlightRibbonTab(BtnTabData, tab == "data");
        HighlightRibbonTab(BtnTabView, tab == "view");
    }

    private static void HighlightRibbonTab(Button button, bool active)
    {
        button.Background = active ? Brushes.White : Brushes.Transparent;
        button.Foreground = active
            ? new SolidColorBrush(Color.Parse("#107C41"))
            : new SolidColorBrush(Color.Parse("#333333"));
        button.FontWeight = active ? FontWeight.Bold : FontWeight.Normal;
    }


    /// <summary>Áp định dạng số cho toàn bộ vùng ô đang chọn. Truyền null để trả về mặc định.</summary>
    private void ApplyCellNumberFormat(string? format)
    {
        if (!RequireExcel(out var session, out _)) return;
        RecordUndoPoint();

        int changed = MainExcelCanvasGrid.ApplyStyleToSelection(style => style.NumberFormat = format);

        session.MarkDirty();
        RefreshExcelCanvas();
        ShowStatusMsg(format == null
            ? $"Đã trả {changed} ô về định dạng chung."
            : $"Đã áp định dạng cho {changed} ô.");
    }


    /// <summary>Bật/tắt một định dạng: áp cho ô Excel đang chọn, hoặc cho văn bản Word.</summary>
    private void ToggleCellStyle(Button button, Action<CellStyle> change, Action wordFallback)
    {
        if (ActiveExcel is { } session)
        {
            RecordUndoPoint();
            int changed = MainExcelCanvasGrid.ApplyStyleToSelection(change);
            session.MarkDirty();
            RefreshExcelCanvas();

            // Trạng thái nút lấy theo ô hiện hành để phản ánh đúng thực tế.
            var style = MainExcelCanvasGrid.ActiveCellStyle;
            bool active = style != null && (
                (ReferenceEquals(button, BtnBold) && style.IsBold) ||
                (ReferenceEquals(button, BtnItalic) && style.IsItalic) ||
                (ReferenceEquals(button, BtnUnderline) && style.IsUnderline));

            button.Background = active ? Brushes.LightBlue : Brushes.White;
            ShowStatusMsg($"Đã đổi định dạng {changed} ô.");
            return;
        }

        if (ViewModel?.ActiveTab?.Session is WordDocumentSession wordSession)
        {
            RecordUndoPoint();
            bool hadSelection = WordDocumentEditor.HasSelection;
            int changed = WordDocumentEditor.ApplyFormatting(run =>
            {
                if (ReferenceEquals(button, BtnBold)) run.IsBold = !run.IsBold;
                else if (ReferenceEquals(button, BtnItalic)) run.IsItalic = !run.IsItalic;
                else if (ReferenceEquals(button, BtnUnderline)) run.IsUnderline = !run.IsUnderline;
            });

            wordSession.MarkDirty();
            WordDocumentEditor.Focus();
            UpdateWordFormatButtons();

            _ = hadSelection;
            ShowStatusMsg(changed > 0
                ? $"Đã đổi định dạng {changed} đoạn chữ."
                : "Định dạng sẽ áp cho phần chữ bạn gõ tiếp theo.");
            return;
        }

        ToggleFormatButton(button, wordFallback);
    }

    /// <summary>
    /// Chọn đúng mục trong danh sách cỡ chữ. Cỡ không có sẵn (ví dụ 33 do bấm A▴)
    /// thì bỏ chọn để không hiển thị sai.
    /// </summary>
    private void SelectFontSizeInBox(int size)
    {
        var match = CboFontSize.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Content?.ToString() == size.ToString());

        if (!ReferenceEquals(CboFontSize.SelectedItem, match)) CboFontSize.SelectedItem = match;
    }

    /// <summary>Đổi cỡ chữ cho phần bôi đen, hoặc ghi nhớ cho phần sắp gõ.</summary>
    private void ApplyFontSize(int size)
    {
        if (ViewModel?.ActiveTab?.Session is WordDocumentSession wordSession)
        {
            RecordUndoPoint();
            bool hadSelection = WordDocumentEditor.HasSelection;
            int changed = WordDocumentEditor.ApplyFormatting(run => run.FontSize = size);

            wordSession.MarkDirty();
            WordDocumentEditor.Focus();

            ShowStatusMsg(hadSelection
                ? $"Đã đổi cỡ chữ {changed} đoạn sang {size}."
                : $"Cỡ chữ {size} sẽ áp cho phần chữ bạn gõ tiếp theo.");
            return;
        }

        if (ActiveExcel is { } session)
        {
            RecordUndoPoint();
            int changed = MainExcelCanvasGrid.ApplyStyleToSelection(style => style.FontSize = size);

            session.MarkDirty();
            MainExcelCanvasGrid.FitRowHeightToContent();
            RefreshExcelCanvas();
            MainExcelCanvasGrid.Focus();

            ShowStatusMsg($"Đã đổi cỡ chữ {changed} ô sang {size}.");
            return;
        }

        ViewModel?.SetFontSize(size);
    }

    /// <summary>Tăng/giảm cỡ chữ theo bước, lấy mốc từ định dạng hiện hành.</summary>
    private void StepFontSize(int delta)
    {
        int current = ActiveExcel != null
            ? MainExcelCanvasGrid.ActiveCellStyle?.FontSize ?? (int)ExcelCanvasGrid.DefaultFontSize
            : WordDocumentEditor.EffectiveFormat.FontSize ?? 14;

        ApplyFontSize(Math.Clamp(current + delta, 6, 400));
    }

    /// <summary>Cập nhật trạng thái nút Đậm/Nghiêng/Gạch chân theo chữ tại con trỏ.</summary>
    private void UpdateWordFormatButtons()
    {
        // Bảng tính: lấy định dạng của ô hiện hành.
        if (ActiveExcel != null)
        {
            var cellStyle = MainExcelCanvasGrid.ActiveCellStyle;

            BtnBold.Background      = cellStyle is { IsBold: true } ? Brushes.LightBlue : Brushes.White;
            BtnItalic.Background    = cellStyle is { IsItalic: true } ? Brushes.LightBlue : Brushes.White;
            BtnUnderline.Background = cellStyle is { IsUnderline: true } ? Brushes.LightBlue : Brushes.White;

            SelectFontSizeInBox(cellStyle?.FontSize ?? (int)ExcelCanvasGrid.DefaultFontSize);

            if (cellStyle?.FontFamily is { Length: > 0 } cellFamily)
            {
                foreach (var item in CboFontFamily.Items.OfType<ComboBoxItem>())
                    if (item.Content?.ToString() == cellFamily) { CboFontFamily.SelectedItem = item; break; }
            }

            return;
        }

        var format = WordDocumentEditor.EffectiveFormat;

        BtnBold.Background      = format.IsBold ? Brushes.LightBlue : Brushes.White;
        BtnItalic.Background    = format.IsItalic ? Brushes.LightBlue : Brushes.White;
        BtnUnderline.Background = format.IsUnderline ? Brushes.LightBlue : Brushes.White;

        SelectFontSizeInBox(format.FontSize ?? 14);

        if (format.FontFamily is { Length: > 0 } family)
        {
            foreach (var item in CboFontFamily.Items.OfType<ComboBoxItem>())
                if (item.Content?.ToString() == family) { CboFontFamily.SelectedItem = item; break; }
        }
    }

    private void ApplyAlignment(string alignment)
    {
        if (ActiveExcel is { } session)
        {
            RecordUndoPoint();
            int changed = MainExcelCanvasGrid.ApplyStyleToSelection(style => style.HorizontalAlignment = alignment);
            session.MarkDirty();
            RefreshExcelCanvas();
            ShowStatusMsg($"Đã căn {alignment.ToLowerInvariant()} cho {changed} ô.");
            return;
        }

        ApplyWordAlignment(alignment.ToLowerInvariant());
    }

    /// <summary>Gộp vùng đang chọn, hoặc bỏ gộp nếu vùng đó đã gộp sẵn.</summary>
    private void ToggleMergeCells()
    {
        if (!RequireExcel(out var session, out _)) return;
        RecordUndoPoint();

        int unmerged = MainExcelCanvasGrid.UnmergeSelection();
        if (unmerged > 0)
        {
            session.MarkDirty();
            RefreshExcelCanvas();
            ShowStatusMsg($"Đã bỏ gộp {unmerged} vùng.");
            return;
        }

        if (!MainExcelCanvasGrid.MergeSelection())
        {
            ShowStatusMsg("Hãy chọn từ 2 ô trở lên rồi bấm Gộp.");
            return;
        }

        // Ô gộp thường dùng làm tiêu đề nên canh giữa luôn cho tiện.
        MainExcelCanvasGrid.ApplyStyleToSelection(style => style.HorizontalAlignment = "Center");
        session.MarkDirty();
        RefreshExcelCanvas();
        ShowStatusMsg($"Đã gộp vùng {MainExcelCanvasGrid.ActiveRange.ToRangeAddress()} và căn giữa.");
    }

    private async void CopySelection()
    {
        if (ViewModel?.ActiveTab?.ModuleType == OfficeModuleType.Word)
        {
            await Clipboard!.SetTextAsync(WordDocumentEditor.SelectedText);
            ShowStatusMsg("Đã sao chép phần bôi đen.");
            return;
        }

        if (ViewModel?.ActiveTab?.Session is not ExcelDocumentSession session) return;

        var sheet = session.Document.GetOrCreateActiveSheet();
        var range = MainExcelCanvasGrid.ActiveRange;
        var text = BuildTabSeparatedText(sheet, range);

        await Clipboard!.SetTextAsync(text);
        ShowStatusMsg(range.IsSingleCell
            ? $"Đã sao chép ô {range.ToRangeAddress()}"
            : $"Đã sao chép {range.CellCount} ô ({range.ToRangeAddress()})");
    }

    /// <summary>Xuất vùng chọn ra dạng cột cách nhau bằng Tab — dán được thẳng vào Excel.</summary>
    private static string BuildTabSeparatedText(SpreadsheetWorksheet sheet, CellRange range)
    {
        var builder = new System.Text.StringBuilder();

        for (int row = range.MinRow; row <= range.MaxRow; row++)
        {
            if (row > range.MinRow) builder.Append('\n');

            for (int col = range.MinCol; col <= range.MaxCol; col++)
            {
                if (col > range.MinCol) builder.Append('\t');
                builder.Append(sheet.GetCell(CellAddress.ToA1(row, col)).GetDisplayString());
            }
        }

        return builder.ToString();
    }

    private void CutSelection()
    {
        if (ViewModel?.ActiveTab?.ModuleType == OfficeModuleType.Word)
        {
            _ = Clipboard!.SetTextAsync(WordDocumentEditor.SelectedText);
            WordDocumentEditor.DeleteSelection();
            return;
        }

        if (ViewModel?.ActiveTab?.Session is not ExcelDocumentSession session) return;

        CopySelection();
        RecordUndoPoint();

        var sheet = session.Document.GetOrCreateActiveSheet();
        foreach (var address in MainExcelCanvasGrid.SelectedAddresses)
            sheet.SetValue(address, string.Empty);

        session.Recalculate();
        session.MarkDirty();
        RefreshExcelCanvas();
    }

    private async void PasteSelection()
    {
        if (ViewModel?.ActiveTab?.ModuleType == OfficeModuleType.Word)
        {
            var clip = await Clipboard!.TryGetTextAsync() ?? string.Empty;
            if (clip.Length > 0) WordDocumentEditor.InsertText(clip);
            return;
        }

        if (ViewModel?.ActiveTab?.Session is not ExcelDocumentSession session) return;

        var text = await Clipboard!.TryGetTextAsync() ?? string.Empty;
        if (text.Length == 0) return;

        RecordUndoPoint();

        var sheet = session.Document.GetOrCreateActiveSheet();
        int baseRow = MainExcelCanvasGrid.SelectedRow;
        int baseCol = MainExcelCanvasGrid.SelectedCol;

        var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        int written = 0;

        for (int r = 0; r < lines.Length; r++)
        {
            var columns = lines[r].Split('\t');
            for (int c = 0; c < columns.Length; c++)
            {
                sheet.SetValue(CellAddress.ToA1(baseRow + r, baseCol + c), columns[c]);
                written++;
            }
        }

        session.Recalculate();
        session.MarkDirty();
        RefreshExcelCanvas();
        ShowStatusMsg($"Đã dán {written} ô bắt đầu từ {CellAddress.ToA1(baseRow, baseCol)}");
    }


    // ─────────────────────────────────────────
    //  THAO TÁC BẢNG TÍNH
    // ─────────────────────────────────────────
    private ExcelDocumentSession? ActiveExcel => ViewModel?.ActiveTab?.Session as ExcelDocumentSession;

    private bool RequireExcel(out ExcelDocumentSession session, out SpreadsheetWorksheet sheet)
    {
        session = ActiveExcel!;
        sheet = null!;

        if (session == null)
        {
            ShowStatusMsg("Hãy mở một sổ tính trước.");
            return false;
        }

        sheet = session.Document.GetOrCreateActiveSheet();
        return true;
    }

    private void AfterSheetChanged(ExcelDocumentSession session)
    {
        session.Recalculate();
        session.MarkDirty();
        RefreshExcelCanvas();
    }

    /// <summary>Dời toàn bộ ô theo dòng/cột — nền chung cho chèn và xoá dòng/cột.</summary>
    private static void ShiftCells(SpreadsheetWorksheet sheet, int fromRow, int rowDelta, int fromCol, int colDelta)
    {
        var moved = new Dictionary<string, CellValue>(StringComparer.OrdinalIgnoreCase);

        foreach (var (address, cell) in sheet.Cells)
        {
            CellAddress parsed;
            try { parsed = new CellAddress(address); }
            catch { continue; }

            int row = parsed.Row, col = parsed.Column;

            if (rowDelta != 0 && row >= fromRow) row += rowDelta;
            if (colDelta != 0 && col >= fromCol) col += colDelta;

            if (row < 1 || col < 1) continue; // ô bị đẩy ra ngoài bảng thì bỏ
            moved[CellAddress.ToA1(row, col)] = cell;
        }

        sheet.Cells = moved;
    }

    private void InsertRows()
    {
        if (!RequireExcel(out var session, out var sheet)) return;
        RecordUndoPoint();

        int count = MainExcelCanvasGrid.ActiveRange.RowCount;
        int at = MainExcelCanvasGrid.ActiveRange.MinRow;

        ShiftCells(sheet, at, count, 0, 0);
        AfterSheetChanged(session);
        ShowStatusMsg($"Đã chèn {count} dòng tại dòng {at}.");
    }

    private void InsertColumns()
    {
        if (!RequireExcel(out var session, out var sheet)) return;
        RecordUndoPoint();

        int count = MainExcelCanvasGrid.ActiveRange.ColCount;
        int at = MainExcelCanvasGrid.ActiveRange.MinCol;

        ShiftCells(sheet, 0, 0, at, count);
        AfterSheetChanged(session);
        ShowStatusMsg($"Đã chèn {count} cột tại cột {at}.");
    }

    private void DeleteRows()
    {
        if (!RequireExcel(out var session, out var sheet)) return;
        RecordUndoPoint();

        var range = MainExcelCanvasGrid.ActiveRange;
        for (int row = range.MinRow; row <= range.MaxRow; row++)
            foreach (var address in sheet.Cells.Keys.Where(k => SafeRow(k) == row).ToList())
                sheet.Cells.Remove(address);

        ShiftCells(sheet, range.MaxRow + 1, -range.RowCount, 0, 0);
        AfterSheetChanged(session);
        ShowStatusMsg($"Đã xoá {range.RowCount} dòng.");
    }

    private void DeleteColumns()
    {
        if (!RequireExcel(out var session, out var sheet)) return;
        RecordUndoPoint();

        var range = MainExcelCanvasGrid.ActiveRange;
        for (int col = range.MinCol; col <= range.MaxCol; col++)
            foreach (var address in sheet.Cells.Keys.Where(k => SafeColumn(k) == col).ToList())
                sheet.Cells.Remove(address);

        ShiftCells(sheet, 0, 0, range.MaxCol + 1, -range.ColCount);
        AfterSheetChanged(session);
        ShowStatusMsg($"Đã xoá {range.ColCount} cột.");
    }

    private static int SafeRow(string address)
    {
        try { return new CellAddress(address).Row; } catch { return -1; }
    }

    private static int SafeColumn(string address)
    {
        try { return new CellAddress(address).Column; } catch { return -1; }
    }

    private void WriteToActiveCell(string value)
    {
        if (!RequireExcel(out var session, out var sheet)) return;
        RecordUndoPoint();

        var address = CellAddress.ToA1(MainExcelCanvasGrid.SelectedRow, MainExcelCanvasGrid.SelectedCol);
        sheet.SetValue(address, value);
        AfterSheetChanged(session);
        ShowStatusMsg($"Đã ghi \"{value}\" vào ô {address}.");
    }

    /// <summary>Chèn hàm tổng hợp cho vùng đang chọn, đặt kết quả ngay dưới hoặc bên phải vùng.</summary>
    private void InsertAggregateFormula(string function)
    {
        if (!RequireExcel(out var session, out var sheet)) return;
        RecordUndoPoint();

        var range = MainExcelCanvasGrid.ActiveRange;

        if (range.IsSingleCell)
        {
            // Không chọn vùng: lấy toàn bộ ô phía trên trong cùng cột.
            if (range.MinRow <= 1)
            {
                ShowStatusMsg("Hãy chọn vùng số cần tính, hoặc đặt con trỏ dưới dãy số.");
                return;
            }

            var from = CellAddress.ToA1(1, range.MinCol);
            var to = CellAddress.ToA1(range.MinRow - 1, range.MinCol);
            var target = CellAddress.ToA1(range.MinRow, range.MinCol);

            sheet.SetValue(target, $"={function}({from}:{to})");
            AfterSheetChanged(session);
            ShowStatusMsg($"{target} = {function}({from}:{to})");
            return;
        }

        // Có vùng chọn: đặt kết quả ngay dưới vùng.
        var rangeText = range.ToRangeAddress();
        var resultCell = CellAddress.ToA1(range.MaxRow + 1, range.MinCol);

        sheet.SetValue(resultCell, $"={function}({rangeText})");
        AfterSheetChanged(session);
        ShowStatusMsg($"{resultCell} = {function}({rangeText})");
    }

    private void RemoveDuplicateRows()
    {
        if (!RequireExcel(out var session, out var sheet)) return;
        RecordUndoPoint();

        var range = MainExcelCanvasGrid.ActiveRange;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateRows = new List<int>();

        for (int row = range.MinRow; row <= range.MaxRow; row++)
        {
            var key = string.Join("\u0001", Enumerable.Range(range.MinCol, range.ColCount)
                .Select(col => sheet.GetCell(CellAddress.ToA1(row, col)).GetDisplayString()));

            if (!seen.Add(key)) duplicateRows.Add(row);
        }

        foreach (int row in duplicateRows.OrderByDescending(r => r))
            for (int col = range.MinCol; col <= range.MaxCol; col++)
                sheet.Cells.Remove(CellAddress.ToA1(row, col));

        AfterSheetChanged(session);
        ShowStatusMsg(duplicateRows.Count > 0
            ? $"Đã xoá {duplicateRows.Count} dòng trùng trong vùng {range.ToRangeAddress()}."
            : "Không có dòng nào trùng trong vùng đã chọn.");
    }

    private void TrimSelectionSpaces()
    {
        if (!RequireExcel(out var session, out var sheet)) return;
        RecordUndoPoint();

        int changed = 0;
        foreach (var address in MainExcelCanvasGrid.SelectedAddresses)
        {
            var cell = sheet.GetCell(address);
            if (cell.DataType != CellDataType.String) continue;

            var text = cell.GetDisplayString();
            var trimmed = System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s+", " ");
            if (trimmed == text) continue;

            sheet.SetValue(address, trimmed);
            changed++;
        }

        AfterSheetChanged(session);
        ShowStatusMsg($"Đã dọn khoảng trắng ở {changed} ô.");
    }

    // ─────────────────────────────────────────
    //  TRANG TÍNH
    // ─────────────────────────────────────────
    private void AddSheet()
    {
        if (!RequireExcel(out var session, out _)) return;
        RecordUndoPoint();

        var sheet = session.Document.AddSheet();
        session.MarkDirty();
        AttachExcelCanvasGrid();
        RenderSheetTabs();
        ShowStatusMsg($"Đã thêm trang tính {sheet.Name}.");
    }

    private void SwitchSheet(int delta)
    {
        if (!RequireExcel(out var session, out _)) return;
        RecordUndoPoint();

        var document = session.Document;
        int target = document.ActiveSheetIndex + delta;
        if (target < 0 || target >= document.Sheets.Count) return;

        ActivateSheet(target);
    }

    private void ActivateSheet(int index)
    {
        if (!RequireExcel(out var session, out _)) return;
        RecordUndoPoint();

        session.Document.ActiveSheetIndex = index;
        session.Recalculate();
        AttachExcelCanvasGrid();
        MainExcelCanvasGrid.SelectCell(1, 1);
        RenderSheetTabs();
        ShowStatusMsg($"Trang tính: {session.Document.GetOrCreateActiveSheet().Name}");
    }

    /// <summary>Vẽ lại dải thẻ trang tính theo đúng danh sách trong sổ tính.</summary>
    private void RenderSheetTabs()
    {
        SheetTabsContainer.Children.Clear();

        var session = ActiveExcel;
        if (session == null) return;

        var document = session.Document;
        document.GetOrCreateActiveSheet();

        for (int i = 0; i < document.Sheets.Count; i++)
        {
            int index = i;
            bool active = i == document.ActiveSheetIndex;

            var label = new TextBlock
            {
                Text = document.Sheets[i].Name,
                FontSize = 11,
                FontWeight = active ? FontWeight.Bold : FontWeight.Normal,
                Foreground = new SolidColorBrush(Color.Parse(active ? "#107C41" : "#555555")),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            var tab = new Border
            {
                Background = active ? Brushes.White : Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Color.Parse("#107C41")),
                BorderThickness = new Thickness(0, 0, 0, active ? 3 : 0),
                Padding = new Thickness(14, 4),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = label
            };

            tab.PointerPressed += (s, e) =>
            {
                var properties = e.GetCurrentPoint(tab).Properties;

                // Chuột phải: xoá trang tính (còn nhiều hơn một trang)
                if (properties.IsRightButtonPressed)
                {
                    if (session.Document.RemoveSheet(index))
                    {
                        session.MarkDirty();
                        ActivateSheet(session.Document.ActiveSheetIndex);
                        ShowStatusMsg("Đã xoá trang tính. (Bấm chuột phải vào thẻ để xoá)");
                    }
                    else
                    {
                        ShowStatusMsg("Sổ tính phải còn ít nhất một trang.");
                    }
                    return;
                }

                ActivateSheet(index);
            };

            SheetTabsContainer.Children.Add(tab);
        }
    }

    // ─────────────────────────────────────────
    //  XEM
    // ─────────────────────────────────────────
    private void ApplyZoom(int percent)
    {
        _zoomLevel = Math.Clamp(percent, 50, 200);
        TxtZoomLevel.Text = $"{_zoomLevel}%";
        TxtZoomStatus.Text = $"{_zoomLevel}%";
        if (Math.Abs(SliderZoom.Value - _zoomLevel) > 0.5) SliderZoom.Value = _zoomLevel;

        MainExcelCanvasGrid.RenderTransform = new ScaleTransform(_zoomLevel / 100.0, _zoomLevel / 100.0);
        MainExcelCanvasGrid.RenderTransformOrigin = RelativePoint.TopLeft;
        ShowStatusMsg($"Thu phóng {_zoomLevel}%");
    }

    private void GoToCellFromFormulaBar()
    {
        var address = (TxtSelectionRange.Text ?? string.Empty).Trim();
        if (address.Length == 0) return;

        try
        {
            var parsed = new CellAddress(address.Split(':')[0]);
            MainExcelCanvasGrid.SelectCell(parsed.Row, parsed.Column);
            MainExcelCanvasGrid.Focus();
            ShowStatusMsg($"Đã nhảy tới ô {parsed.A1Notation}.");
        }
        catch
        {
            ShowStatusMsg($"Địa chỉ ô không hợp lệ: {address}");
        }
    }

    private void FitColumnsToContent()
    {
        if (!RequireExcel(out _, out var sheet)) return;

        // Ước lượng bề rộng theo ô dài nhất, giới hạn cho khỏi tràn màn hình.
        int longest = sheet.Cells.Values
            .Select(c => c.GetDisplayString().Length)
            .DefaultIfEmpty(8)
            .Max();

        double width = Math.Clamp(longest * 7.5 + 16, 60, 320);
        MainExcelCanvasGrid.ColumnWidth = width;
        RefreshExcelCanvas();
        ShowStatusMsg($"Đã đặt bề rộng cột theo nội dung dài nhất ({longest} ký tự).");
    }

    /// <summary>Tìm chuỗi trong trang tính, mỗi lần Enter nhảy tới kết quả tiếp theo.</summary>
    private void FindNext(string term)
    {
        term = term.Trim();
        if (term.Length == 0) return;

        // Tài liệu văn bản có cách tìm riêng.
        if (ViewModel?.ActiveTab?.Session is WordDocumentSession)
        {
            bool found = WordDocumentEditor.FindNext(term);
            WordDocumentEditor.Focus();
            ShowStatusMsg(found
                ? $"Đã nhảy tới chỗ khớp “{term}”."
                : $"Không tìm thấy “{term}” trong tài liệu.");
            return;
        }

        if (!RequireExcel(out _, out var sheet)) return;

        if (!term.Equals(_lastSearchTerm, StringComparison.OrdinalIgnoreCase))
        {
            _lastSearchTerm = term;
            _searchIndex = 0;
        }

        var matches = sheet.Cells
            .Where(kv => kv.Value.GetDisplayString().Contains(term, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .OrderBy(SafeRow)
            .ThenBy(SafeColumn)
            .ToList();

        if (matches.Count == 0)
        {
            ShowStatusMsg($"Không tìm thấy \"{term}\" trong trang tính.");
            return;
        }

        _searchIndex %= matches.Count;
        var address = matches[_searchIndex];
        _searchIndex++;

        try
        {
            var parsed = new CellAddress(address);
            MainExcelCanvasGrid.SelectCell(parsed.Row, parsed.Column);
            MainExcelCanvasGrid.Focus();
            ShowStatusMsg($"Kết quả {_searchIndex}/{matches.Count}: ô {address}");
        }
        catch
        {
            ShowStatusMsg("Địa chỉ ô trong kết quả tìm kiếm không hợp lệ.");
        }
    }

    // ─────────────────────────────────────────
    //  XUẤT TỆP
    // ─────────────────────────────────────────
    private double[] SelectedExportPageSize()
    {
        var name = (CboExportPageSize.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "A4";
        var size = PdfPageSizes.ByName(name);
        return ChkExportLandscape.IsChecked == true ? PdfPageSizes.Landscape(size) : PdfPageSizes.Portrait(size);
    }

    private async Task ExportToPdfAsync()
    {
        // Tài liệu văn bản: xuất nguyên định dạng chữ.
        if (ViewModel?.ActiveTab?.Session is WordDocumentSession wordSession)
        {
            var documentFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Xuất tài liệu ra PDF",
                DefaultExtension = "pdf",
                SuggestedFileName = SafeFileName(Path.GetFileNameWithoutExtension(ViewModel.ActiveTab.Title)) + ".pdf"
            });
            if (documentFile == null) return;

            var documentPath = EnsureExtension(documentFile.Path.LocalPath, ".pdf");
            var title = Path.GetFileNameWithoutExtension(ViewModel.ActiveTab.Title);

            var bytes = await Task.Run(() => PdfToolkit.DocumentToPdf(wordSession.Document, new PdfTextToPdfOptions
            {
                PageSize = SelectedExportPageSize(),
                Title = title
            }));

            await File.WriteAllBytesAsync(documentPath, bytes);
            ActivityLog.Info("file", "export-pdf", fileName: Path.GetFileName(documentPath), fileSize: bytes.LongLength);
            ShowStatusMsg($"Đã xuất tài liệu ra {documentPath}");
            return;
        }

        if (!RequireExcel(out _, out var sheet)) return;

        // Kiểm tra dữ liệu TRƯỚC khi hỏi nơi lưu, khỏi bắt người dùng chọn xong mới báo trống.
        var rows = BuildSheetRows(sheet);
        if (rows.Count == 0)
        {
            ShowStatusMsg("Trang tính chưa có dữ liệu để xuất.");
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Xuất trang tính ra PDF",
            DefaultExtension = "pdf",
            SuggestedFileName = SafeFileName(sheet.Name) + ".pdf"
        });
        if (file == null) return;

        var path = EnsureExtension(file.Path.LocalPath, ".pdf");
        await ExportRowsToPdfAsync(rows, path, SelectedExportPageSize(), sheet.Name);

        ShowStatusMsg($"Đã xuất {rows.Count:N0} dòng ra {path}");
    }

    private async Task ExportSheetToCsvAsync()
    {
        if (!RequireExcel(out _, out var sheet)) return;

        var rows = BuildSheetRows(sheet);
        if (rows.Count == 0)
        {
            ShowStatusMsg("Trang tính chưa có dữ liệu để xuất.");
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Xuất trang tính ra CSV",
            DefaultExtension = "csv",
            SuggestedFileName = SafeFileName(sheet.Name) + ".csv"
        });
        if (file == null) return;

        var path = EnsureExtension(file.Path.LocalPath, ".csv");
        await File.WriteAllTextAsync(path, PdfToolkit.ToCsv(rows, ';'), new System.Text.UTF8Encoding(true));

        ShowStatusMsg($"Đã xuất {rows.Count:N0} dòng ra {path}");
    }


    /// <summary>
    /// Phần xuất PDF tách riêng khỏi hộp thoại chọn tệp để kiểm thử được tự động.
    /// </summary>
    internal static async Task ExportRowsToPdfAsync(
        IReadOnlyList<IReadOnlyList<string>> rows, string path, double[] pageSize, string title)
    {
        var pdf = await Task.Run(() => PdfToolkit.TableToPdf(rows, new PdfTableToPdfOptions
        {
            PageSize = pageSize,
            Title = title,
            FirstRowIsHeader = true,
            RepeatHeader = true
        }));

        await File.WriteAllBytesAsync(path, pdf);
        ActivityLog.Info("file", "export-pdf", fileName: Path.GetFileName(path), fileSize: pdf.LongLength);
    }

    /// <summary>Bảo đảm tên tệp có đúng phần mở rộng, kể cả khi người dùng gõ thiếu.</summary>
    internal static string EnsureExtension(string path, string extension) =>
        path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? path : path + extension;

    /// <summary>Bỏ ký tự không hợp lệ khỏi tên trang tính khi dùng làm tên tệp.</summary>
    internal static string SafeFileName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(name) ? "trang-tinh" : name;
    }

    /// <summary>Đổi trang tính thành ma trận chuỗi để xuất ra PDF hoặc CSV.</summary>
    /// <summary>Dùng cho kiểm thử: dựng ma trận dòng đúng như khi xuất tệp.</summary>
    internal static List<IReadOnlyList<string>> BuildSheetRowsForTesting(SpreadsheetWorksheet sheet) => BuildSheetRows(sheet);

    private static List<IReadOnlyList<string>> BuildSheetRows(SpreadsheetWorksheet sheet)
    {
        var rows = new List<IReadOnlyList<string>>();
        if (sheet.Cells.Count == 0) return rows;

        int maxRow = sheet.MaxRow, maxCol = sheet.MaxCol;

        for (int row = 1; row <= maxRow; row++)
        {
            var cells = new List<string>(maxCol);
            for (int col = 1; col <= maxCol; col++)
                cells.Add(sheet.GetCell(CellAddress.ToA1(row, col)).GetDisplayString());

            if (cells.Any(c => c.Length > 0)) rows.Add(cells);
        }

        return rows;
    }

    private async Task SaveFileAsDialogAsync()
    {
        if (ViewModel?.ActiveTab == null) return;

        var suggested = ViewModel.ActiveTab.Title;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Lưu thành tệp mới",
            DefaultExtension = Path.GetExtension(suggested).TrimStart('.'),
            SuggestedFileName = suggested
        });
        if (file == null) return;

        var path = file.Path.LocalPath;
        await using (var stream = File.Create(path))
        {
            await ViewModel.ActiveTab.Session.SaveAsync(stream, Path.GetExtension(path));
        }

        ViewModel.ActiveTab.Session.FilePath = path;
        ViewModel.ActiveTab.Title = Path.GetFileName(path);
        RenderWorkspaceTabs();
        ActivityLog.Info("file", "save-as", fileName: Path.GetFileName(path));
        ShowStatusMsg($"Đã lưu thành {path}");
    }

    /// <summary>Bảng tra cứu nhanh toàn bộ hàm mà Wocel tính được.</summary>
    private void ShowFunctionCatalog()
    {
        var text = new System.Text.StringBuilder();

        foreach (var group in FormulaFunctionCatalog.All.GroupBy(f => f.Category))
        {
            text.AppendLine($"── {group.Key} ──");
            foreach (var function in group)
                text.AppendLine($"  {function.Signature,-42} {function.Description}");
            text.AppendLine();
        }

        var window = new Window
        {
            Title = $"Danh mục hàm ({FormulaFunctionCatalog.All.Count} hàm)",
            Width = 720,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer
            {
                Padding = new Thickness(16),
                Content = new SelectableTextBlock
                {
                    Text = text.ToString(),
                    FontFamily = new FontFamily("Menlo, Consolas, Courier New, monospace"),
                    FontSize = 12
                }
            }
        };

        window.ShowDialog(this);
    }

    // ─────────────────────────────────────────
    //  FORMATTING HELPERS
    // ─────────────────────────────────────────
    private void ToggleFormatButton(Button btn, Action action)
    {
        action();
        // Visual active state toggle
        bool isActive = btn.Background?.ToString() == Brushes.LightBlue.ToString();
        btn.Background = isActive ? Brushes.White : Brushes.LightBlue;
    }

    private void ApplyWordAlignment(string align)
    {
        if (ViewModel?.ActiveTab?.ModuleType == OfficeModuleType.Word)
        {
            ShowStatusMsg($"Căn chỉnh: {align} (áp dụng cho đoạn văn hiện tại)");
        }
    }

    private void ApplyNumberFormat()
    {
        if (CboNumberFormat.SelectedItem is not ComboBoxItem item) return;

        // Nhãn hiển thị trên ComboBox → mã định dạng dùng trong CellStyle.
        string? format = item.Content?.ToString()?.ToLowerInvariant() switch
        {
            var t when t == null => null,
            var t when t.Contains("chung") || t.Contains("general") => null,
            var t when t.Contains("tiền") || t.Contains("currency") || t.Contains("₫") => "currency",
            var t when t.Contains("phần trăm") || t.Contains("percent") || t.Contains("%") => "percent",
            var t when t.Contains("nghìn") || t.Contains("thousand") => "thousands",
            var t when t.Contains("ngày") || t.Contains("date") => "date",
            var t when t.Contains("khoa học") || t.Contains("scientific") => "scientific",
            var t when t.Contains("số nguyên") || t.Contains("integer") => "integer",
            var t when t.Contains("thập phân") || t.Contains("decimal") => "decimal2",
            _ => null
        };

        ApplyCellNumberFormat(format);
    }

    private void SortActiveColumn(bool ascending)
    {
        if (ViewModel?.ActiveTab?.Session is not ExcelDocumentSession es) return;
        var sheet = es.Document.GetOrCreateActiveSheet();
        var col = new CellAddress(ViewModel.CurrentCellAddress).Column;

        // Collect row values for this column
        var rows = sheet.Cells
            .Where(kv => { try { return new CellAddress(kv.Key).Column == col; } catch { return false; } })
            .Select(kv => (addr: kv.Key, row: new CellAddress(kv.Key).Row, val: kv.Value.GetDisplayString()))
            .OrderBy(x => ascending ? x.val : "")
            .ThenByDescending(x => ascending ? "" : x.val)
            .ToList();

        int newRow = 1;
        foreach (var item in rows)
        {
            sheet.SetValue(CellAddress.ToA1(newRow++, col), item.val);
        }
        es.Recalculate();
        es.MarkDirty();
        RefreshExcelCanvas();
        ShowStatusMsg($"Đã sắp xếp cột {CellAddress.ToA1(1, col).TrimEnd('1')} {(ascending ? "A→Z" : "Z→A")}");
    }

    // ─────────────────────────────────────────
    //  PDF VIEWER
    // ─────────────────────────────────────────
    private void NavigatePdf(int delta)
    {
        if (ViewModel?.ActiveTab?.Session is not PdfDocumentSession pdfSession) return;

        pdfSession.CurrentPage = Math.Clamp(pdfSession.CurrentPage + delta, 1, pdfSession.TotalPages);
        RenderPdfPage(pdfSession);
    }

    private void RenderPdfPage(PdfDocumentSession pdfSession)
    {
        var idx = pdfSession.CurrentPage - 1;
        var pageText = idx >= 0 && idx < pdfSession.Pages.Count ? pdfSession.Pages[idx] : "";

        PdfPageViewerControl.PageText = pageText;
        PdfPageViewerControl.CurrentPage = pdfSession.CurrentPage;
        PdfPageViewerControl.TotalPages = pdfSession.TotalPages;
        PdfPageViewerControl.InvalidateVisual();

        var label = $"Trang {pdfSession.CurrentPage} / {pdfSession.TotalPages}";
        TxtPdfPageBar.Text = label;
        TxtPdfPageIndicator.Text = label;
        TxtPdfFileName.Text = pdfSession.Title;
    }

    private void ExportPdfTextToWordTab()
    {
        if (ViewModel?.ActiveTab?.Session is not PdfDocumentSession pdfSession) return;

        ViewModel.CreateNewWordDocument($"{Path.GetFileNameWithoutExtension(pdfSession.Title)}_extracted.docx");
        ViewModel.LoadPlainTextIntoWordDocument(pdfSession.ExtractedText);
        RefreshWorkspace();
        ShowStatusMsg("Đã xuất nội dung PDF sang tab Word mới.");
    }

    // ─────────────────────────────────────────
    //  EXCEL CANVAS GRID WIRING
    // ─────────────────────────────────────────
    private void AttachExcelCanvasGrid()
    {
        if (ViewModel?.ActiveTab?.Session is not ExcelDocumentSession excelSession) return;

        MainExcelCanvasGrid.Worksheet = excelSession.Document.GetOrCreateActiveSheet();
        MainExcelCanvasGrid.TotalRows = _currentRowCount;
        MainExcelCanvasGrid.TotalCols = _currentColCount;

        MainExcelCanvasGrid.CellSelected -= OnCanvasCellSelected;
        MainExcelCanvasGrid.CellSelected += OnCanvasCellSelected;

        MainExcelCanvasGrid.CellValueCommitted -= OnCanvasCellValueCommitted;
        MainExcelCanvasGrid.CellValueCommitted += OnCanvasCellValueCommitted;

        MainExcelCanvasGrid.SelectionChanged -= OnCanvasSelectionChanged;
        MainExcelCanvasGrid.SelectionChanged += OnCanvasSelectionChanged;

        MainExcelCanvasGrid.CopyRequested -= CopySelection;
        MainExcelCanvasGrid.CopyRequested += CopySelection;
        MainExcelCanvasGrid.CutRequested -= CutSelection;
        MainExcelCanvasGrid.CutRequested += CutSelection;
        MainExcelCanvasGrid.PasteRequested -= PasteSelection;
        MainExcelCanvasGrid.PasteRequested += PasteSelection;

        MainExcelCanvasGrid.UndoRequested -= UndoLastChange;
        MainExcelCanvasGrid.UndoRequested += UndoLastChange;
        MainExcelCanvasGrid.RedoRequested -= RedoLastChange;
        MainExcelCanvasGrid.RedoRequested += RedoLastChange;

        // Lịch sử hoàn tác gắn với đúng trang tính đang mở.
        var sheet = excelSession.Document.GetOrCreateActiveSheet();
        _sheetHistory = new UndoHistory<Dictionary<string, CellValue>>(
            () => SheetSnapshot.Capture(sheet),
            snapshot =>
            {
                SheetSnapshot.Restore(sheet, snapshot);
                excelSession.Recalculate();
                RefreshExcelCanvas();
            });

        MainExcelCanvasGrid.InvalidateVisual();
        RenderSheetTabs();
        UpdateCalculationStats();
    }

    private void OnCanvasCellSelected(string a1, string formulaOrRaw)
    {
        if (ViewModel != null)
        {
            ViewModel.CurrentCellAddress = a1;
            ViewModel.CurrentFormula = formulaOrRaw;
        }
        UpdateCalculationStats();
    }

    /// <summary>Nối tài liệu Word đang mở vào trình soạn thảo.</summary>
    private void AttachWordEditor()
    {
        if (ViewModel?.ActiveTab?.Session is not WordDocumentSession session) return;

        if (!ReferenceEquals(WordDocumentEditor.Document, session.Document))
            WordDocumentEditor.Document = session.Document;

        WordDocumentEditor.DocumentChanged -= OnWordDocumentChanged;
        WordDocumentEditor.DocumentChanged += OnWordDocumentChanged;

        WordDocumentEditor.DocumentChanging -= OnWordDocumentChanging;
        WordDocumentEditor.DocumentChanging += OnWordDocumentChanging;

        WordDocumentEditor.SelectionChanged -= UpdateWordFormatButtons;
        WordDocumentEditor.SelectionChanged += UpdateWordFormatButtons;

        WordDocumentEditor.CopyRequested -= CopySelection;
        WordDocumentEditor.CopyRequested += CopySelection;
        WordDocumentEditor.CutRequested -= CutSelection;
        WordDocumentEditor.CutRequested += CutSelection;
        WordDocumentEditor.PasteRequested -= PasteSelection;
        WordDocumentEditor.PasteRequested += PasteSelection;
        WordDocumentEditor.UndoRequested -= UndoLastChange;
        WordDocumentEditor.UndoRequested += UndoLastChange;
        WordDocumentEditor.RedoRequested -= RedoLastChange;
        WordDocumentEditor.RedoRequested += RedoLastChange;

        _documentHistory = new UndoHistory<List<DocBlock>>(
            () => DocumentSnapshot.Capture(session.Document),
            snapshot =>
            {
                DocumentSnapshot.Restore(session.Document, snapshot);
                WordDocumentEditor.ReloadDocument();
            });

        Dispatcher.UIThread.Post(() => WordDocumentEditor.Focus(), DispatcherPriority.Loaded);
    }

    private void OnWordDocumentChanged()
    {
        if (ViewModel?.ActiveTab?.Session is WordDocumentSession session) session.MarkDirty();
    }

    /// <summary>Ghi điểm hoàn tác trước mỗi lần trình soạn thảo sắp đổi nội dung.</summary>
    private void OnWordDocumentChanging() => RecordUndoPoint();

    private void OnCanvasSelectionChanged()
    {
        UpdateWordFormatButtons();
        // Ô địa chỉ hiện "A1" hoặc "A1:C10" hoặc "3 vùng · 25 ô" như Excel.
        TxtSelectionRange.Text = MainExcelCanvasGrid.SelectionLabel;
        UpdateCalculationStats();
    }

    private void OnCanvasCellValueCommitted(string a1, string value)
    {
        if (ViewModel?.ActiveTab?.Session is ExcelDocumentSession session)
        {
            RecordUndoPoint();
            var sheet = session.Document.GetOrCreateActiveSheet();
            sheet.SetValue(a1, value);
            session.Recalculate();
            session.MarkDirty();
            MainExcelCanvasGrid.InvalidateVisual();
            UpdateCalculationStats();
        }
    }

    private void ApplyCurrentFormula()
    {
        if (ViewModel == null) return;

        _formulaSuggestions?.Close();
        OnCanvasCellValueCommitted(ViewModel.CurrentCellAddress, TxtFormulaInput.Text ?? "");
        MainExcelCanvasGrid.Focus();
    }

    private void RefreshExcelCanvas()
    {
        MainExcelCanvasGrid.InvalidateVisual();
        UpdateCalculationStats();
    }

    private void ExpandRows(int extra = 100)
    {
        _currentRowCount += extra;
        MainExcelCanvasGrid.TotalRows = _currentRowCount;
        MainExcelCanvasGrid.InvalidateMeasure();
        MainExcelCanvasGrid.InvalidateVisual();
        ShowStatusMsg($"Mở rộng lên {_currentRowCount} dòng");
    }

    private void ExpandCols(int extra = 6)
    {
        _currentColCount += extra;
        MainExcelCanvasGrid.TotalCols = _currentColCount;
        MainExcelCanvasGrid.InvalidateMeasure();
        MainExcelCanvasGrid.InvalidateVisual();
        ShowStatusMsg($"Mở rộng lên {_currentColCount} cột");
    }

    private void UpdateCalculationStats()
    {
        if (ViewModel?.ActiveTab?.Session is not ExcelDocumentSession excelSession) return;
        var sheet = excelSession.Document.GetOrCreateActiveSheet();

        // Có chọn nhiều ô thì tính trên vùng chọn (giống Excel);
        // chỉ một ô thì thống kê cả trang cho dễ nhìn tổng quan.
        bool useSelection = MainExcelCanvasGrid.HasMultiSelection;

        var values = (useSelection
                ? MainExcelCanvasGrid.SelectedAddresses.Select(a => sheet.GetCell(a))
                : sheet.Cells.Values)
            .Select(c => c.GetNumericValue())
            .Where(n => n.HasValue)
            .Select(n => n!.Value)
            .ToList();

        string scope = useSelection ? $" ({MainExcelCanvasGrid.SelectedCellCount} ô đã chọn)" : string.Empty;

        if (values.Count > 0)
        {
            TxtAverageStat.Text = $"AVERAGE: {values.Average():N2}";
            TxtCountStat.Text   = $"COUNT: {values.Count}{scope}";
            TxtSumStat.Text     = $"SUM: {values.Sum():N2}";
        }
        else
        {
            TxtAverageStat.Text = "AVERAGE: 0";
            TxtCountStat.Text   = $"COUNT: 0{scope}";
            TxtSumStat.Text     = "SUM: 0";
        }
    }

    // ─────────────────────────────────────────
    //  EDITOR VISIBILITY
    // ─────────────────────────────────────────
    private void UpdateEditorVisibility()
    {
        ExcelEditorContainer.IsVisible = false;
        WordEditorContainer.IsVisible  = false;
        PdfViewerContainer.IsVisible   = false;
        PdfNavToolGroup.IsVisible      = false;
        PdfToolsContainer.IsVisible    = _pdfToolsVisible;

        // Thanh công thức và dải trang tính chỉ thuộc về bảng tính.
        bool isSpreadsheet = !_pdfToolsVisible && ViewModel?.ActiveTab?.Session is ExcelDocumentSession;
        FormulaBarContainer.IsVisible = isSpreadsheet;
        SheetTabBar.IsVisible = isSpreadsheet;

        // Định dạng số, AutoSum và sắp xếp không có nghĩa với tài liệu văn bản.
        GroupNumber.IsVisible = isSpreadsheet;
        GroupEditing.IsVisible = isSpreadsheet;
        SeparatorNumber.IsVisible = isSpreadsheet;
        SeparatorEditing.IsVisible = isSpreadsheet;

        // Thẻ ribbon dành riêng cho bảng tính cũng ẩn theo.
        BtnTabFormula.IsVisible = isSpreadsheet;
        BtnTabData.IsVisible = isSpreadsheet;

        // Thẻ Bố trí trang dùng chung, chỉ đổi nhãn cho khớp loại tài liệu.
        bool isDocument = !_pdfToolsVisible && ViewModel?.ActiveTab?.Session is WordDocumentSession;
        BtnExportSheetToPdf.Content = isDocument ? "📕 Xuất tài liệu ra PDF" : "📕 Xuất trang tính ra PDF";
        BtnExportSheetToCsv.IsVisible = isSpreadsheet;
        BtnMergeCenter.IsVisible = isSpreadsheet;   // gộp ô không có nghĩa với văn bản

        RefreshContextTooltips();
        if (!isSpreadsheet && _activeRibbonTab is "formula" or "data") SelectRibbonTab("home");

        if (_pdfToolsVisible) return;
        if (ViewModel?.ActiveTab == null) return;

        if (ViewModel.ActiveTab.Session is PdfDocumentSession pdfSession)
        {
            PdfViewerContainer.IsVisible = true;
            PdfNavToolGroup.IsVisible    = true;
            RenderPdfPage(pdfSession);
        }
        else if (ViewModel.ActiveTab.ModuleType == OfficeModuleType.Word)
        {
            WordEditorContainer.IsVisible = true;
            AttachWordEditor();
        }
        else
        {
            ExcelEditorContainer.IsVisible = true;
            AttachExcelCanvasGrid();

            // Đưa focus vào lưới để gõ được ngay, không phải bấm chuột trước.
            Dispatcher.UIThread.Post(() => MainExcelCanvasGrid.Focus(), DispatcherPriority.Loaded);
        }
    }

    /// <summary>Hiện hoặc ẩn bộ công cụ xử lý tệp trong vùng nội dung chính.</summary>
    private void ShowPdfTools(bool visible)
    {
        _pdfToolsVisible = visible;

        if (visible && _pdfToolsPanel == null)
        {
            _pdfToolsPanel = new PdfToolsPanel();
            _pdfToolsPanel.LogMessage += message => TxtStatusBar.Text = message;
            PdfToolsContainer.Child = _pdfToolsPanel;

            // Mở sẵn tệp PDF đang xem để người dùng không phải chọn lại.
            if (ViewModel?.ActiveTab?.Session is PdfDocumentSession { FilePath: { Length: > 0 } path })
                _pdfToolsPanel.AddFile(path);

            ActivityLog.Info("ui", "open-pdf-tools");
        }

        BtnTogglePdfTools.Content = visible ? "📄 Quay lại tài liệu" : "🛠 Công cụ tệp";
        UpdateEditorVisibility();
    }

    private void RefreshWorkspace()
    {
        UpdateEditorVisibility();
        RenderWorkspaceTabs();
    }

    // ─────────────────────────────────────────
    //  WORKSPACE TABS
    // ─────────────────────────────────────────
    private void RenderWorkspaceTabs()
    {
        WorkspaceTabsHeader.Children.Clear();
        if (ViewModel == null) return;

        foreach (var tab in ViewModel.Tabs)
        {
            bool isCurrent = tab == ViewModel.ActiveTab;

            // Determine badge color + letter
            string badgeLetter;
            IBrush badgeBrush;
            if (tab.Session is PdfDocumentSession)
            {
                badgeLetter = "P"; badgeBrush = new SolidColorBrush(Color.Parse("#C00000"));
            }
            else if (tab.ModuleType == OfficeModuleType.Word)
            {
                badgeLetter = "W"; badgeBrush = Brushes.Navy;
            }
            else
            {
                badgeLetter = "X"; badgeBrush = Brushes.DarkGreen;
            }

            var border = new Border
            {
                Background = isCurrent ? Brushes.White : Brushes.LightGray,
                CornerRadius = new CornerRadius(4, 4, 0, 0),
                Padding = new Thickness(10, 5),
                BorderBrush = Brushes.Gainsboro,
                BorderThickness = new Thickness(1, 1, 1, 0)
            };

            var stack = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
            var badge = new Border { CornerRadius = new CornerRadius(3), Width = 16, Height = 16, Background = badgeBrush };
            badge.Child = new TextBlock { Text = badgeLetter, Foreground = Brushes.White, FontSize = 10, FontWeight = Avalonia.Media.FontWeight.Bold, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };

            stack.Children.Add(badge);
            stack.Children.Add(new TextBlock { Text = tab.Title, FontWeight = isCurrent ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal, FontSize = 12, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });

            var closeBtn = new Button { Content = "✕", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(2, 0), FontSize = 10 };
            closeBtn.Click += (s, e) => { ViewModel.CloseTab(tab); RefreshWorkspace(); };

            stack.Children.Add(closeBtn);
            border.Child = stack;
            border.PointerPressed += (s, e) => { ViewModel.ActiveTab = tab; RefreshWorkspace(); };

            WorkspaceTabsHeader.Children.Add(border);
        }
    }

    // ─────────────────────────────────────────
    //  RECENT FILES
    // ─────────────────────────────────────────
    private void RenderRecentFiles()
    {
        RecentFilesContainer.Children.Clear();
        if (ViewModel == null) return;

        var items = ViewModel.SelectedHomeTab == 0 ? ViewModel.RecentWordFiles : ViewModel.RecentExcelFiles;
        if (items.Count == 0)
        {
            RecentFilesContainer.Children.Add(new TextBlock
            {
                Text = "Chưa có lịch sử. Hãy tạo mới hoặc mở tệp từ máy tính.",
                FontSize = 13, Foreground = Brushes.Gray, Margin = new Thickness(0, 10, 0, 0)
            });
            return;
        }

        foreach (var item in items)
        {
            var btn = new Button
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Padding = new Thickness(14, 10), Margin = new Thickness(0, 0, 0, 6),
                Background = Brushes.White, BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6)
            };

            var stack = new StackPanel { Spacing = 3 };
            var top = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
            top.Children.Add(new TextBlock { Text = item.FileName, FontWeight = Avalonia.Media.FontWeight.SemiBold, FontSize = 13, Foreground = item.ModuleType == OfficeModuleType.Word ? Brushes.Navy : Brushes.DarkGreen });
            top.Children.Add(new TextBlock { Text = item.FormattedDate, FontSize = 11, Foreground = Brushes.Gray });
            stack.Children.Add(top);
            stack.Children.Add(new TextBlock { Text = item.FilePath, FontSize = 11, Foreground = Brushes.Gray });
            btn.Content = stack;

            btn.Click += async (s, e) =>
            {
                if (File.Exists(item.FilePath))
                {
                    await ViewModel.OpenFileByPathAsync(item.FilePath);
                    RefreshWorkspace();
                }
            };
            RecentFilesContainer.Children.Add(btn);
        }
    }

    // ─────────────────────────────────────────
    //  FILE DIALOGS
    // ─────────────────────────────────────────
    private async Task OpenFileDialogAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Mở tài liệu Office / PDF",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Tất cả tệp hỗ trợ")
                {
                    Patterns = new[] { "*.xlsx", "*.docx", "*.csv", "*.rtf", "*.txt", "*.pdf", "*.wocel" }
                },
                new FilePickerFileType("PDF (*.pdf)") { Patterns = new[] { "*.pdf" } },
                new FilePickerFileType("Excel (*.xlsx, *.csv)") { Patterns = new[] { "*.xlsx", "*.csv" } },
                new FilePickerFileType("Word (*.docx, *.rtf, *.txt)") { Patterns = new[] { "*.docx", "*.rtf", "*.txt" } }
            }
        });

        if (files.Count > 0 && ViewModel != null)
        {
            var path = files[0].Path.LocalPath;
            long size = 0;
            try { size = new FileInfo(path).Length; } catch { /* không lấy được kích thước */ }

            try
            {
                await ViewModel.OpenFileByPathAsync(path);
                RefreshWorkspace();
                ActivityLog.Info("file", "open", fileName: Path.GetFileName(path), fileSize: size);
            }
            catch (Exception error)
            {
                ActivityLog.Error("file", "open", error, fileName: Path.GetFileName(path));
                ShowStatusMsg($"Không mở được tệp: {error.Message}");
            }
        }
    }

    private async Task SaveFileDialogAsync()
    {
        if (ViewModel?.ActiveTab == null) return;

        var defaultName = ViewModel.ActiveTab.Title;
        var ext = Path.GetExtension(defaultName).TrimStart('.');

        if (string.IsNullOrEmpty(ViewModel.ActiveTab.FilePath))
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Lưu tệp", DefaultExtension = ext, SuggestedFileName = defaultName
            });
            if (file != null)
            {
                var path = file.Path.LocalPath;
                using var stream = File.Create(path);
                await ViewModel.ActiveTab.Session.SaveAsync(stream, Path.GetExtension(path));
                ViewModel.ActiveTab.Session.FilePath = path;
                ViewModel.ActiveTab.Title = Path.GetFileName(path);
                RenderWorkspaceTabs();
            }
        }
        else
        {
            using var stream = File.Create(ViewModel.ActiveTab.FilePath);
            await ViewModel.ActiveTab.Session.SaveAsync(stream, Path.GetExtension(ViewModel.ActiveTab.FilePath));
        }

        ShowStatusMsg("Đã lưu tệp thành công.");
        ActivityLog.Info("file", "save", fileName: ViewModel.ActiveTab.Title);
    }

    // ─────────────────────────────────────────
    //  STATUS BAR
    // ─────────────────────────────────────────
    private void ShowStatusMsg(string msg)
    {
        TxtStatusBar.Text = msg;
    }

    /// <summary>Mở Wocel Capture: chạy lại chính exe này ở chế độ chụp màn hình (process riêng).</summary>
    private void OpenCapture()
    {
        try
        {
            var exe = Environment.ProcessPath
                ?? throw new InvalidOperationException("Không xác định được đường dẫn ứng dụng.");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, Program.CaptureArgument)
            {
                UseShellExecute = false
            });
            ActivityLog.Info("ui", "open-capture");
            ShowStatusMsg("Đã mở Wocel Capture.");
        }
        catch (Exception error)
        {
            ActivityLog.Error("ui", "open-capture", error);
            ShowStatusMsg($"Không mở được Wocel Capture: {error.Message}");
        }
    }
}
