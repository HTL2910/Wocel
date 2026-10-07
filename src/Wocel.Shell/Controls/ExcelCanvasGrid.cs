using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using System;
using System.Globalization;
using System.Linq;
using Wocel.Core.Models;
using Wocel.Excel.Sessions;

namespace Wocel.Shell.Controls;

/// <summary>Một vùng ô hình chữ nhật, hai đầu là ô neo và ô đang di chuyển.</summary>
public readonly record struct CellRange(int Row1, int Col1, int Row2, int Col2)
{
    public int MinRow => Math.Min(Row1, Row2);
    public int MaxRow => Math.Max(Row1, Row2);
    public int MinCol => Math.Min(Col1, Col2);
    public int MaxCol => Math.Max(Col1, Col2);

    public int RowCount => MaxRow - MinRow + 1;
    public int ColCount => MaxCol - MinCol + 1;
    public int CellCount => RowCount * ColCount;
    public bool IsSingleCell => CellCount == 1;

    public bool Contains(int row, int col) =>
        row >= MinRow && row <= MaxRow && col >= MinCol && col <= MaxCol;

    public bool ContainsRow(int row) => row >= MinRow && row <= MaxRow;
    public bool ContainsColumn(int col) => col >= MinCol && col <= MaxCol;

    public IEnumerable<(int row, int col)> Cells()
    {
        for (int r = MinRow; r <= MaxRow; r++)
            for (int c = MinCol; c <= MaxCol; c++)
                yield return (r, c);
    }

    /// <summary>Mô tả kiểu "A1:C10" như trên thanh địa chỉ của Excel.</summary>
    public string ToRangeAddress() => IsSingleCell
        ? CellAddress.ToA1(MinRow, MinCol)
        : $"{CellAddress.ToA1(MinRow, MinCol)}:{CellAddress.ToA1(MaxRow, MaxCol)}";
}

public class ExcelCanvasGrid : Control
{
    public const double HeaderWidth = 46;
    public const double HeaderHeight = 24;
    public const double DefaultColWidth = 120;
    public const double DefaultRowHeight = 24;
    public const double DefaultFontSize = 12;

    private static readonly Typeface DefaultTypeface = new("Segoe UI, -apple-system, BlinkMacSystemFont, Roboto, sans-serif");
    private static readonly Pen GridLinePen = new(new SolidColorBrush(Color.Parse("#E2E3E5")), 1);
    private static readonly Pen HeaderBorderPen = new(new SolidColorBrush(Color.Parse("#D4D4D4")), 1);
    private static readonly Pen ActiveCellPen = new(new SolidColorBrush(Color.Parse("#107C41")), 2);

    private static readonly IBrush HeaderBgBrush = new SolidColorBrush(Color.Parse("#F4F5F7"));
    private static readonly IBrush ActiveHeaderBgBrush = new SolidColorBrush(Color.Parse("#D8E8DD"));
    private static readonly IBrush ActiveHeaderTextBrush = new SolidColorBrush(Color.Parse("#107C41"));
    private static readonly IBrush HeaderTextBrush = new SolidColorBrush(Color.Parse("#444444"));
    private static readonly IBrush CellTextBrush = new SolidColorBrush(Color.Parse("#1A1A1A"));
    private static readonly IBrush ActiveCellBgBrush = new SolidColorBrush(Color.Parse("#EBF9F1"));
    private static readonly IBrush SelectionBgBrush = new SolidColorBrush(Color.Parse("#DCEFE4"));
    private static readonly Pen SelectionBorderPen = new(new SolidColorBrush(Color.Parse("#107C41")), 1.5);

    /// <summary>Bề rộng cột hiện tại (đổi được khi dùng "Vừa cột theo nội dung").</summary>
    public double ColumnWidth { get; set; } = DefaultColWidth;

    /// <summary>Chiều cao dòng hiện tại.</summary>
    public double RowHeight { get; set; } = DefaultRowHeight;

    public int TotalRows { get; set; } = 100;
    public int TotalCols { get; set; } = 26; // A .. Z
    public int SelectedRow { get; private set; } = 1;
    public int SelectedCol { get; private set; } = 1;

    public SpreadsheetWorksheet? Worksheet { get; set; }

    /// <summary>Đang mở ô nhập liệu tại chỗ hay không.</summary>
    public bool IsEditing => _inPlaceEditor.IsVisible;

    /// <summary>Nội dung đang gõ trong ô nhập liệu.</summary>
    public string EditorText => _inPlaceEditor.Text ?? string.Empty;

    /// <summary>Ô gợi ý công thức đang gắn với ô nhập liệu.</summary>
    public FormulaSuggestionPopup Suggestions => _suggestions;

    public event Action<string, string>? CellSelected;
    public event Action<string, string>? CellValueCommitted;

    /// <summary>Báo mỗi khi vùng chọn thay đổi, để thanh trạng thái tính lại tổng/đếm.</summary>
    public event Action? SelectionChanged;

    public event Action? CopyRequested;
    public event Action? CutRequested;
    public event Action? PasteRequested;
    public event Action? UndoRequested;
    public event Action? RedoRequested;

    /// <summary>Vùng đang kéo chọn: từ ô neo tới ô hiện hành.</summary>
    public CellRange ActiveRange => new(_anchorRow, _anchorCol, SelectedRow, SelectedCol);

    /// <summary>Mọi vùng đang chọn, kể cả các vùng rời thêm bằng Ctrl (Cmd trên macOS).</summary>
    public IReadOnlyList<CellRange> SelectedRanges
    {
        get
        {
            var all = new List<CellRange>(_extraRanges) { ActiveRange };
            return all;
        }
    }

    /// <summary>Danh sách địa chỉ ô đang chọn, đã loại trùng giữa các vùng.</summary>
    public IReadOnlyList<string> SelectedAddresses
    {
        get
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();

            foreach (var range in SelectedRanges)
                foreach (var (row, col) in range.Cells())
                {
                    var a1 = CellAddress.ToA1(row, col);
                    if (seen.Add(a1)) result.Add(a1);
                }

            return result;
        }
    }

    public int SelectedCellCount => SelectedAddresses.Count;
    public bool HasMultiSelection => SelectedCellCount > 1;

    /// <summary>Nhãn hiển thị trên ô địa chỉ: "B2" hoặc "A1:C10" hoặc "3 vùng".</summary>
    public string SelectionLabel => _extraRanges.Count > 0
        ? $"{_extraRanges.Count + 1} vùng · {SelectedCellCount} ô"
        : ActiveRange.ToRangeAddress();

    private readonly TextBox _inPlaceEditor;
    private readonly FormulaSuggestionPopup _suggestions;

    private readonly List<CellRange> _extraRanges = new();
    private int _anchorRow = 1;
    private int _anchorCol = 1;
    private bool _isDragging;

    public ExcelCanvasGrid()
    {
        ClipToBounds = true;
        Focusable = true;

        _inPlaceEditor = new TextBox
        {
            IsVisible = false,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.Parse("#107C41")),
            BorderThickness = new Thickness(2),
            Padding = new Thickness(4, 2),
            FontSize = 12,
            FontFamily = new FontFamily("Segoe UI, -apple-system, BlinkMacSystemFont, Roboto, sans-serif")
        };

        _inPlaceEditor.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitEditorValue();
                MoveSelection(1, 0); // Move down on enter like Excel
                e.Handled = true;
            }
            else if (e.Key == Key.Tab)
            {
                CommitEditorValue();
                MoveSelection(0, 1); // Move right on tab like Excel
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelEditor();
                e.Handled = true;
            }
        };

        _inPlaceEditor.LostFocus += (s, e) => CommitEditorValue();

        // Gõ "=SU" thì hiện gợi ý SUM, SUMIF… như Excel.
        _suggestions = FormulaSuggestionPopup.Attach(_inPlaceEditor);

        VisualChildren.Add(_inPlaceEditor);
        LogicalChildren.Add(_inPlaceEditor);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = HeaderWidth + TotalCols * ColumnWidth;
        var height = HeaderHeight + TotalRows * RowHeight;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_inPlaceEditor.IsVisible)
        {
            PositionInPlaceEditor();
        }
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var totalW = HeaderWidth + TotalCols * ColumnWidth;
        var totalH = HeaderHeight + TotalRows * RowHeight;

        // 0. Nền trắng phủ kín lưới.
        //    Bắt buộc phải có: Avalonia bắt sự kiện chuột theo hình đã vẽ, nếu chỉ
        //    kẻ đường lưới thì bấm vào ô trống sẽ không nhận được cú click nào.
        context.FillRectangle(Brushes.White, new Rect(0, 0, totalW, totalH));

        // 1. Draw Grid Lines (Horizontal & Vertical)
        for (int r = 0; r <= TotalRows; r++)
        {
            var y = HeaderHeight + r * RowHeight;
            context.DrawLine(GridLinePen, new Point(0, y), new Point(totalW, y));
        }

        for (int c = 0; c <= TotalCols; c++)
        {
            var x = HeaderWidth + c * ColumnWidth;
            context.DrawLine(GridLinePen, new Point(x, 0), new Point(x, totalH));
        }

        // 2. Draw Column Headers (A, B, C...)
        for (int c = 1; c <= TotalCols; c++)
        {
            var x = HeaderWidth + (c - 1) * ColumnWidth;
            var rect = new Rect(x, 0, ColumnWidth, HeaderHeight);
            bool isActiveCol = SelectedRanges.Any(r => r.ContainsColumn(c));

            context.FillRectangle(isActiveCol ? ActiveHeaderBgBrush : HeaderBgBrush, rect);
            context.DrawRectangle(null, HeaderBorderPen, rect);

            var colName = CellAddress.ToA1(1, c).TrimEnd('1');
            var ft = new FormattedText(
                colName,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                DefaultTypeface,
                11,
                isActiveCol ? ActiveHeaderTextBrush : HeaderTextBrush);

            var textX = x + (ColumnWidth - ft.Width) / 2;
            var textY = (HeaderHeight - ft.Height) / 2;
            context.DrawText(ft, new Point(textX, textY));
        }

        // 3. Draw Row Headers (1, 2, 3...)
        for (int r = 1; r <= TotalRows; r++)
        {
            var y = HeaderHeight + (r - 1) * RowHeight;
            var rect = new Rect(0, y, HeaderWidth, RowHeight);
            bool isActiveRow = SelectedRanges.Any(range => range.ContainsRow(r));

            context.FillRectangle(isActiveRow ? ActiveHeaderBgBrush : HeaderBgBrush, rect);
            context.DrawRectangle(null, HeaderBorderPen, rect);

            var rowNum = r.ToString();
            var ft = new FormattedText(
                rowNum,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                DefaultTypeface,
                11,
                isActiveRow ? ActiveHeaderTextBrush : HeaderTextBrush);

            var textX = (HeaderWidth - ft.Width) / 2;
            var textY = y + (RowHeight - ft.Height) / 2;
            context.DrawText(ft, new Point(textX, textY));
        }

        // 4. Top-Left Select All Box
        var cornerRect = new Rect(0, 0, HeaderWidth, HeaderHeight);
        context.FillRectangle(HeaderBgBrush, cornerRect);
        context.DrawRectangle(null, HeaderBorderPen, cornerRect);

        // 5. Vẽ nội dung các ô
        if (Worksheet != null)
        {
            var merges = MergedRanges();

            // Vẽ khung và nền cho các vùng gộp trước.
            foreach (var merge in merges)
            {
                var mergeRect = new Rect(
                    HeaderWidth + (merge.MinCol - 1) * ColumnWidth,
                    HeaderHeight + (merge.MinRow - 1) * RowHeight,
                    merge.ColCount * ColumnWidth,
                    merge.RowCount * RowHeight);

                context.FillRectangle(Brushes.White, mergeRect);
                context.DrawRectangle(null, GridLinePen, mergeRect);
            }

            foreach (var kvp in Worksheet.Cells)
            {
                try
                {
                    var (row, col) = CellAddress.FromA1(kvp.Key);
                    if (row < 1 || row > TotalRows || col < 1 || col > TotalCols) continue;

                    // Ô đang được sửa thì để ô nhập liệu hiển thị, không vẽ đè.
                    if (_inPlaceEditor.IsVisible && row == SelectedRow && col == SelectedCol) continue;

                    // Ô bị vùng gộp che (không phải góc trên-trái) thì bỏ qua.
                    var merge = merges.FirstOrDefault(m => m.Contains(row, col));
                    int columnSpan = 1;
                    if (merge != default && merge.Contains(row, col))
                    {
                        if (row != merge.MinRow || col != merge.MinCol) continue;
                        columnSpan = merge.ColCount;
                    }

                    DrawCellBackground(context, row, col, kvp.Value.Style, columnSpan,
                        columnSpan > 1 ? merge.RowCount : 1);

                    var display = kvp.Value.GetDisplayString();
                    if (string.IsNullOrEmpty(display)) continue;

                    DrawCellText(context, display, row, col,
                        kvp.Value.GetNumericValue().HasValue, kvp.Value.Style, columnSpan);
                }
                catch
                {
                    // Địa chỉ ô hỏng thì bỏ qua, phần còn lại vẫn vẽ được.
                }
            }
        }

        // 6. Tô vùng chọn, viền quanh từng vùng, và làm nổi ô hiện hành
        if (!_inPlaceEditor.IsVisible)
        {
            var ranges = SelectedRanges;

            foreach (var range in ranges)
            {
                var rangeRect = new Rect(
                    HeaderWidth + (range.MinCol - 1) * ColumnWidth,
                    HeaderHeight + (range.MinRow - 1) * RowHeight,
                    range.ColCount * ColumnWidth,
                    range.RowCount * RowHeight);

                // Vùng nhiều ô mới tô nền; một ô lẻ chỉ cần viền cho gọn.
                if (!range.IsSingleCell) context.FillRectangle(SelectionBgBrush, rangeRect);
                context.DrawRectangle(null, SelectionBorderPen, rangeRect);
            }

            // Vẽ lại chữ trong vùng đã tô nền để không bị nền che mất.
            if (Worksheet != null && ranges.Any(r => !r.IsSingleCell))
            {
                foreach (var range in ranges)
                {
                    if (range.IsSingleCell) continue;

                    foreach (var (row, col) in range.Cells())
                    {
                        var a1 = CellAddress.ToA1(row, col);
                        if (!Worksheet.Cells.TryGetValue(a1, out var cell)) continue;

                        var display = cell.GetDisplayString();
                        if (string.IsNullOrEmpty(display)) continue;

                        DrawCellText(context, display, row, col, cell.GetNumericValue().HasValue, cell.Style);
                    }
                }
            }

            // Ô hiện hành: nền trắng + viền đậm như Excel
            if (SelectedRow >= 1 && SelectedRow <= TotalRows && SelectedCol >= 1 && SelectedCol <= TotalCols)
            {
                var activeX = HeaderWidth + (SelectedCol - 1) * ColumnWidth;
                var activeY = HeaderHeight + (SelectedRow - 1) * RowHeight;
                var activeRect = new Rect(activeX, activeY, ColumnWidth, RowHeight);

                context.FillRectangle(HasMultiSelection ? Brushes.White : ActiveCellBgBrush, activeRect);
                context.DrawRectangle(null, ActiveCellPen, activeRect);

                var activeAddress = CellAddress.ToA1(SelectedRow, SelectedCol);
                if (Worksheet != null && Worksheet.Cells.TryGetValue(activeAddress, out var activeCell))
                {
                    var display = activeCell.GetDisplayString();
                    if (!string.IsNullOrEmpty(display))
                        DrawCellText(context, display, SelectedRow, SelectedCol,
                            activeCell.GetNumericValue().HasValue, activeCell.Style);
                }

                // Nút kéo điền dữ liệu ở góc dưới-phải của vùng chọn
                var fillRange = ActiveRange;
                var handleX = HeaderWidth + fillRange.MaxCol * ColumnWidth - 4;
                var handleY = HeaderHeight + fillRange.MaxRow * RowHeight - 4;
                context.FillRectangle(new SolidColorBrush(Color.Parse("#107C41")), new Rect(handleX, handleY, 5, 5));
            }
        }
    }

    /// <summary>Vẽ nội dung một ô: áp đậm/nghiêng/gạch chân, màu chữ và cách canh lề.</summary>
    private void DrawCellText(DrawingContext context, string text, int row, int col, bool isNumeric,
        CellStyle? style = null, int columnSpan = 1)
    {
        var family = style?.FontFamily is { Length: > 0 } name
            ? new FontFamily(name + ", Segoe UI, -apple-system, sans-serif")
            : DefaultTypeface.FontFamily;

        var typeface = new Typeface(family,
            style is { IsItalic: true } ? FontStyle.Italic : FontStyle.Normal,
            style is { IsBold: true } ? FontWeight.Bold : FontWeight.Normal);

        double fontSize = style?.FontSize is > 0 ? style.FontSize!.Value : DefaultFontSize;

        var brush = style?.TextColor is { Length: > 0 } color
            ? new SolidColorBrush(Color.Parse(color))
            : CellTextBrush;

        var formatted = new FormattedText(
            text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fontSize, brush);

        double cellLeft = HeaderWidth + (col - 1) * ColumnWidth;
        double cellWidth = ColumnWidth * columnSpan;
        double top = HeaderHeight + (row - 1) * RowHeight + Math.Max(1, (RowHeight - formatted.Height) / 2);

        // Canh lề: theo style nếu có, mặc định số canh phải và chữ canh trái.
        string alignment = style?.HorizontalAlignment ?? (isNumeric ? "Right" : "Left");
        double left = alignment switch
        {
            "Right" => cellLeft + cellWidth - formatted.Width - 6,
            "Center" => cellLeft + (cellWidth - formatted.Width) / 2,
            _ => cellLeft + 6
        };
        left = Math.Max(cellLeft + 2, left);

        context.DrawText(formatted, new Point(left, top));

        if (style is { IsUnderline: true })
        {
            double underlineY = top + formatted.Height - 1;
            context.DrawLine(
                new Pen(brush, 1),
                new Point(left, underlineY),
                new Point(left + formatted.Width, underlineY));
        }
    }

    /// <summary>Tô nền ô theo định dạng người dùng đặt.</summary>
    private void DrawCellBackground(DrawingContext context, int row, int col, CellStyle? style, int columnSpan = 1, int rowSpan = 1)
    {
        if (style?.BackgroundColor is not { Length: > 0 } color) return;

        var rect = new Rect(
            HeaderWidth + (col - 1) * ColumnWidth,
            HeaderHeight + (row - 1) * RowHeight,
            ColumnWidth * columnSpan,
            RowHeight * rowSpan);

        context.FillRectangle(new SolidColorBrush(Color.Parse(color)), rect);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // Không lấy focus thì mọi phím gõ sau đó đều rơi vào hư không.
        Focus();

        var pt = e.GetPosition(this);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool multi = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        CommitEditorValue();

        // Ô góc trên-trái: chọn toàn bộ bảng
        if (pt.X <= HeaderWidth && pt.Y <= HeaderHeight)
        {
            SelectAll();
            return;
        }

        // Tiêu đề dòng: chọn cả dòng
        if (pt.X <= HeaderWidth)
        {
            int row = (int)((pt.Y - HeaderHeight) / RowHeight) + 1;
            if (row < 1 || row > TotalRows) return;

            if (shift) ExtendSelectionTo(row, TotalCols);
            else if (multi) AddRange(row, 1, row, TotalCols);
            else SelectRows(row, row);

            _isDragging = true;
            e.Pointer.Capture(this);
            return;
        }

        // Tiêu đề cột: chọn cả cột
        if (pt.Y <= HeaderHeight)
        {
            int col = (int)((pt.X - HeaderWidth) / ColumnWidth) + 1;
            if (col < 1 || col > TotalCols) return;

            if (shift) ExtendSelectionTo(TotalRows, col);
            else if (multi) AddRange(1, col, TotalRows, col);
            else SelectColumns(col, col);

            _isDragging = true;
            e.Pointer.Capture(this);
            return;
        }

        // Vùng ô dữ liệu
        var (targetRow, targetCol) = CellAt(pt);
        if (targetRow < 1 || targetCol < 1) return;

        if (shift)
        {
            ExtendSelectionTo(targetRow, targetCol);
        }
        else if (multi)
        {
            AddRange(targetRow, targetCol, targetRow, targetCol);
        }
        else
        {
            // Bấm vào ô nằm trong vùng gộp thì chọn cả vùng đó.
            var merge = MergeAt(targetRow, targetCol);
            if (merge is { } mergedRange)
            {
                SelectCell(mergedRange.MinRow, mergedRange.MinCol);
                ExtendSelectionTo(mergedRange.MaxRow, mergedRange.MaxCol);
            }
            else
            {
                SelectCell(targetRow, targetCol);
            }

            if (e.ClickCount >= 2) StartInPlaceEditing();
        }

        _isDragging = e.ClickCount < 2;
        if (_isDragging) e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isDragging || IsEditing) return;

        var (row, col) = CellAt(e.GetPosition(this));
        if (row < 1 || col < 1) return;
        if (row == SelectedRow && col == SelectedCol) return;

        ExtendSelectionTo(row, col);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!_isDragging) return;
        _isDragging = false;
        e.Pointer.Capture(null);
    }

    /// <summary>Các vùng ô đã gộp trên trang tính hiện tại.</summary>
    public List<CellRange> MergedRanges()
    {
        var result = new List<CellRange>();
        if (Worksheet == null) return result;

        foreach (var text in Worksheet.MergedRanges)
        {
            var parts = text.Split(':');
            if (parts.Length != 2) continue;

            try
            {
                var start = new CellAddress(parts[0]);
                var end = new CellAddress(parts[1]);
                result.Add(new CellRange(start.Row, start.Column, end.Row, end.Column));
            }
            catch
            {
                // Vùng ghi sai định dạng thì bỏ qua.
            }
        }

        return result;
    }

    /// <summary>Vùng gộp chứa ô này, nếu có.</summary>
    public CellRange? MergeAt(int row, int col)
    {
        foreach (var range in MergedRanges())
            if (range.Contains(row, col)) return range;
        return null;
    }

    /// <summary>Gộp vùng đang chọn thành một ô. Nội dung ô góc trên-trái được giữ lại.</summary>
    public bool MergeSelection()
    {
        if (Worksheet == null) return false;

        var range = ActiveRange;
        if (range.IsSingleCell) return false;

        // Bỏ các vùng gộp cũ nằm chồng lên vùng mới.
        Worksheet.MergedRanges.RemoveAll(text =>
        {
            var parts = text.Split(':');
            if (parts.Length != 2) return false;
            try
            {
                var start = new CellAddress(parts[0]);
                var end = new CellAddress(parts[1]);
                var existing = new CellRange(start.Row, start.Column, end.Row, end.Column);
                return existing.MinRow <= range.MaxRow && existing.MaxRow >= range.MinRow
                    && existing.MinCol <= range.MaxCol && existing.MaxCol >= range.MinCol;
            }
            catch { return false; }
        });

        Worksheet.MergedRanges.Add(range.ToRangeAddress());
        InvalidateVisual();
        return true;
    }

    /// <summary>Bỏ gộp mọi vùng chạm tới vùng đang chọn.</summary>
    public int UnmergeSelection()
    {
        if (Worksheet == null) return 0;

        var selection = SelectedRanges;
        int removed = Worksheet.MergedRanges.RemoveAll(text =>
        {
            var parts = text.Split(':');
            if (parts.Length != 2) return false;
            try
            {
                var start = new CellAddress(parts[0]);
                var end = new CellAddress(parts[1]);
                var existing = new CellRange(start.Row, start.Column, end.Row, end.Column);
                return selection.Any(sel => existing.MinRow <= sel.MaxRow && existing.MaxRow >= sel.MinRow
                                            && existing.MinCol <= sel.MaxCol && existing.MaxCol >= sel.MinCol);
            }
            catch { return false; }
        });

        if (removed > 0) InvalidateVisual();
        return removed;
    }

    /// <summary>
    /// Cho chiều cao dòng vừa với cỡ chữ lớn nhất đang dùng, nếu không chữ cỡ lớn
    /// sẽ bị cắt mất phần trên dưới.
    /// </summary>
    public void FitRowHeightToContent()
    {
        double largest = DefaultFontSize;

        if (Worksheet != null)
        {
            foreach (var cell in Worksheet.Cells.Values)
                if (cell.Style?.FontSize is > 0 && cell.Style.FontSize.Value > largest)
                    largest = cell.Style.FontSize.Value;
        }

        double needed = Math.Max(DefaultRowHeight, largest * 1.6);
        if (Math.Abs(needed - RowHeight) < 0.5) return;

        RowHeight = needed;
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Áp một thay đổi định dạng lên mọi ô đang chọn.</summary>
    public int ApplyStyleToSelection(Action<CellStyle> change)
    {
        if (Worksheet == null) return 0;

        int count = 0;
        foreach (var address in SelectedAddresses)
        {
            change(Worksheet.GetOrCreateStyle(address));
            count++;
        }

        InvalidateVisual();
        return count;
    }

    /// <summary>Định dạng của ô hiện hành, để thanh công cụ hiển thị đúng trạng thái nút.</summary>
    public CellStyle? ActiveCellStyle =>
        Worksheet != null && Worksheet.Cells.TryGetValue(CellAddress.ToA1(SelectedRow, SelectedCol), out var cell)
            ? cell.Style
            : null;

    /// <summary>Đổi toạ độ pixel thành chỉ số ô. Trả (0,0) nếu nằm ngoài vùng dữ liệu.</summary>
    private (int row, int col) CellAt(Point point)
    {
        if (point.X <= HeaderWidth || point.Y <= HeaderHeight) return (0, 0);

        int col = (int)((point.X - HeaderWidth) / ColumnWidth) + 1;
        int row = (int)((point.Y - HeaderHeight) / RowHeight) + 1;

        if (row < 1 || row > TotalRows || col < 1 || col > TotalCols) return (0, 0);
        return (row, col);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;

        // Khi đang gõ trong ô, để ô nhập liệu tự xử lý (nó lo Enter/Tab/Esc).
        if (IsEditing) return;

        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (control)
        {
            switch (e.Key)
            {
                case Key.A: SelectAll(); e.Handled = true; return;
                case Key.C: CopyRequested?.Invoke(); e.Handled = true; return;
                case Key.X: CutRequested?.Invoke(); e.Handled = true; return;
                case Key.V: PasteRequested?.Invoke(); e.Handled = true; return;
                case Key.Z: UndoRequested?.Invoke(); e.Handled = true; return;
                case Key.Y: RedoRequested?.Invoke(); e.Handled = true; return;
            }
        }

        // Shift + phím di chuyển = mở rộng vùng chọn từ ô neo.
        if (shift && e.Key is Key.Up or Key.Down or Key.Left or Key.Right
                             or Key.Home or Key.End or Key.PageUp or Key.PageDown)
        {
            var (row, col) = (SelectedRow, SelectedCol);

            switch (e.Key)
            {
                case Key.Up: row--; break;
                case Key.Down: row++; break;
                case Key.Left: col--; break;
                case Key.Right: col++; break;
                case Key.Home: col = 1; if (control) row = 1; break;
                case Key.End: col = TotalCols; if (control) row = TotalRows; break;
                case Key.PageUp: row -= 20; break;
                case Key.PageDown: row += 20; break;
            }

            ExtendSelectionTo(row, col);
            BringSelectionIntoView();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Up:    MoveSelection(-1, 0); break;
            case Key.Down:  MoveSelection(1, 0); break;
            case Key.Left:  MoveSelection(0, -1); break;
            case Key.Right: MoveSelection(0, 1); break;

            case Key.Enter: MoveSelection(1, 0); break;
            case Key.Tab:   MoveSelection(0, 1); break;

            case Key.Home:
                if (control) SelectCell(1, 1);
                else SelectCell(SelectedRow, 1);
                break;
            case Key.End:
                if (control) SelectCell(TotalRows, TotalCols);
                else SelectCell(SelectedRow, TotalCols);
                break;

            case Key.PageDown: MoveSelection(20, 0); break;
            case Key.PageUp:   MoveSelection(-20, 0); break;

            case Key.F2:
                StartInPlaceEditing();
                break;

            case Key.Delete:
            case Key.Back:
                ClearSelectedCells();
                break;

            default:
                return; // phím khác để nơi khác xử lý
        }

        BringSelectionIntoView();
        e.Handled = true;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || IsEditing) return;
        if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;

        // Gõ thẳng một ký tự là bắt đầu nhập đè lên ô — đúng thói quen Excel.
        StartInPlaceEditing(e.Text);
        e.Handled = true;
    }

    /// <summary>Cuộn để ô đang chọn luôn nằm trong tầm nhìn khi di chuyển bằng bàn phím.</summary>
    private void BringSelectionIntoView()
    {
        var scroll = this.FindAncestorOfType<ScrollViewer>();
        if (scroll == null) return;

        double cellLeft = HeaderWidth + (SelectedCol - 1) * ColumnWidth;
        double cellTop = HeaderHeight + (SelectedRow - 1) * RowHeight;
        double cellRight = cellLeft + ColumnWidth;
        double cellBottom = cellTop + RowHeight;

        var offset = scroll.Offset;
        var viewport = scroll.Viewport;
        double x = offset.X, y = offset.Y;

        // Chừa chỗ cho dải tiêu đề hàng/cột để ô không bị nấp sau nó.
        if (cellLeft - HeaderWidth < x) x = Math.Max(0, cellLeft - HeaderWidth);
        else if (cellRight > x + viewport.Width) x = cellRight - viewport.Width;

        if (cellTop - HeaderHeight < y) y = Math.Max(0, cellTop - HeaderHeight);
        else if (cellBottom > y + viewport.Height) y = cellBottom - viewport.Height;

        double maxX = Math.Max(0, scroll.Extent.Width - viewport.Width);
        double maxY = Math.Max(0, scroll.Extent.Height - viewport.Height);

        scroll.Offset = new Vector(Math.Clamp(x, 0, maxX), Math.Clamp(y, 0, maxY));
    }

    /// <summary>Chọn đúng một ô, bỏ mọi vùng đang chọn trước đó.</summary>
    public void SelectCell(int row, int col)
    {
        _extraRanges.Clear();

        SelectedRow = Math.Clamp(row, 1, TotalRows);
        SelectedCol = Math.Clamp(col, 1, TotalCols);
        _anchorRow = SelectedRow;
        _anchorCol = SelectedCol;

        RaiseSelectionChanged();
    }

    /// <summary>Kéo dài vùng chọn từ ô neo tới ô chỉ định (Shift + chuột hoặc Shift + phím).</summary>
    public void ExtendSelectionTo(int row, int col)
    {
        SelectedRow = Math.Clamp(row, 1, TotalRows);
        SelectedCol = Math.Clamp(col, 1, TotalCols);
        RaiseSelectionChanged();
    }

    /// <summary>Thêm một vùng rời (Ctrl trên Windows, Cmd trên macOS).</summary>
    public void AddRange(int row1, int col1, int row2, int col2)
    {
        _extraRanges.Add(ActiveRange);

        _anchorRow = Math.Clamp(row1, 1, TotalRows);
        _anchorCol = Math.Clamp(col1, 1, TotalCols);
        SelectedRow = Math.Clamp(row2, 1, TotalRows);
        SelectedCol = Math.Clamp(col2, 1, TotalCols);

        RaiseSelectionChanged();
    }

    public void SelectAll() => SelectBlock(1, 1, TotalRows, TotalCols);

    public void SelectRows(int fromRow, int toRow) => SelectBlock(fromRow, 1, toRow, TotalCols);

    public void SelectColumns(int fromCol, int toCol) => SelectBlock(1, fromCol, TotalRows, toCol);

    private void SelectBlock(int row1, int col1, int row2, int col2)
    {
        _extraRanges.Clear();

        _anchorRow = Math.Clamp(row1, 1, TotalRows);
        _anchorCol = Math.Clamp(col1, 1, TotalCols);
        SelectedRow = Math.Clamp(row2, 1, TotalRows);
        SelectedCol = Math.Clamp(col2, 1, TotalCols);

        RaiseSelectionChanged();
    }

    private void RaiseSelectionChanged()
    {
        var a1 = CellAddress.ToA1(_anchorRow, _anchorCol);
        var rawOrFormula = string.Empty;

        if (Worksheet != null && Worksheet.Cells.TryGetValue(a1, out var cell))
            rawOrFormula = cell.Formula ?? cell.RawValue?.ToString() ?? string.Empty;

        CellSelected?.Invoke(a1, rawOrFormula);
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    public void StartInPlaceEditing(string? initialText = null)
    {
        var a1 = CellAddress.ToA1(SelectedRow, SelectedCol);
        var currentText = initialText ?? (Worksheet?.Cells.TryGetValue(a1, out var cell) == true ? (cell.Formula ?? cell.RawValue?.ToString() ?? "") : "");

        _inPlaceEditor.Text = currentText;
        _inPlaceEditor.IsVisible = true;
        PositionInPlaceEditor();
        _inPlaceEditor.Focus();
        _inPlaceEditor.CaretIndex = _inPlaceEditor.Text?.Length ?? 0;
        InvalidateArrange();
        InvalidateVisual();
    }

    private void PositionInPlaceEditor()
    {
        var selX = HeaderWidth + (SelectedCol - 1) * ColumnWidth;
        var selY = HeaderHeight + (SelectedRow - 1) * RowHeight;
        var rect = new Rect(selX, selY, ColumnWidth, RowHeight);

        _inPlaceEditor.Width = ColumnWidth;
        _inPlaceEditor.Height = RowHeight;

        // Avalonia bắt buộc Measure trước Arrange; thiếu bước này ô nhập liệu
        // có kích thước 0 và người dùng không gõ được gì.
        _inPlaceEditor.Measure(rect.Size);
        _inPlaceEditor.Arrange(rect);
    }

    public void CommitEditorValue()
    {
        if (!_inPlaceEditor.IsVisible) return;

        _suggestions.Close();

        var a1 = CellAddress.ToA1(SelectedRow, SelectedCol);
        var text = _inPlaceEditor.Text ?? "";
        _inPlaceEditor.IsVisible = false;

        // Trả focus về lưới, nếu không thì phím gõ tiếp theo rơi vào ô đã ẩn.
        Focus();

        CellValueCommitted?.Invoke(a1, text);
        InvalidateVisual();
    }

    public void CancelEditor()
    {
        if (!_inPlaceEditor.IsVisible) return;

        _suggestions.Close();
        _inPlaceEditor.IsVisible = false;
        Focus();
        InvalidateVisual();
    }

    /// <summary>Xoá nội dung toàn bộ vùng đang chọn (phím Delete / Backspace).</summary>
    public void ClearSelectedCells()
    {
        CancelEditor();

        foreach (var address in SelectedAddresses)
            CellValueCommitted?.Invoke(address, string.Empty);

        InvalidateVisual();
    }

    public void MoveSelection(int rowDelta, int colDelta)
    {
        SelectCell(SelectedRow + rowDelta, SelectedCol + colDelta);
    }

    public void UpdateCellFromExternal(string a1, string value)
    {
        CellValueCommitted?.Invoke(a1, value);
        InvalidateVisual();
    }
}
