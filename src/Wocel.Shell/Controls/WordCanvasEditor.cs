using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia.Interactivity;
using Wocel.Core.Models;

namespace Wocel.Shell.Controls;

/// <summary>
/// Trình soạn thảo văn bản có định dạng: mỗi đoạn chữ giữ riêng kiểu đậm, nghiêng,
/// gạch chân, phông và cỡ chữ. Vẽ trực tiếp lên canvas nên bôi đậm hay đổi cỡ chữ
/// chỉ tác động lên phần đang bôi đen, đúng như Word.
/// </summary>
public class WordCanvasEditor : Control
{
    public const double PageWidth = 680;
    public const double PageMargin = 56;

    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#1A1A1A"));
    private static readonly IBrush CaretBrush = new SolidColorBrush(Color.Parse("#107C41"));
    private static readonly IBrush SelectionBrush = new SolidColorBrush(Color.Parse("#B7DFC7"));

    /// <summary>Một mẩu chữ đã đo xong, gắn với đúng đoạn định dạng sinh ra nó.</summary>
    private sealed class LayoutPiece
    {
        public required string Text;
        public required TextRun Run;
        public required int BlockIndex;
        public required int StartOffset;    // vị trí trong đoạn văn
        public double X, Y, Width, Height;
        public int LineIndex;
        public int Length => Text.Length;
    }

    private readonly List<LayoutPiece> _pieces = new();
    private readonly List<double> _lineTops = new();
    private readonly List<double> _lineHeights = new();

    private DocumentDocument? _document;
    private double _contentHeight = 400;

    private int _caretBlock, _caretOffset;
    private int _anchorBlock, _anchorOffset;
    private bool _dragging;

    /// <summary>
    /// Định dạng đang chờ áp cho phần chữ sắp gõ. Word hoạt động đúng như vậy:
    /// chọn cỡ chữ khi chưa bôi đen thì chữ gõ tiếp theo mới mang cỡ đó.
    /// </summary>
    private TextRun? _pendingFormat;

    public WordCanvasEditor()
    {
        Focusable = true;
        ClipToBounds = false;
        Cursor = new Cursor(StandardCursorType.Ibeam);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  TÀI LIỆU
    // ─────────────────────────────────────────────────────────────────────
    public DocumentDocument? Document
    {
        get => _document;
        set
        {
            _document = value;
            EnsureAtLeastOneBlock();
            _caretBlock = _caretOffset = _anchorBlock = _anchorOffset = 0;
            Relayout();
        }
    }

    /// <summary>Báo mỗi khi nội dung đổi, để phiên làm việc đánh dấu "chưa lưu".</summary>
    public event Action? DocumentChanged;

    /// <summary>Báo TRƯỚC khi nội dung đổi, để bên ngoài kịp ghi điểm hoàn tác.</summary>
    public event Action? DocumentChanging;

    /// <summary>Báo khi con trỏ hoặc vùng bôi đen đổi, để thanh công cụ cập nhật trạng thái.</summary>
    public event Action? SelectionChanged;

    // Bảng tạm và hoàn tác cần quyền truy cập cửa sổ, nên cửa sổ chính xử lý.
    public event Action? CopyRequested;
    public event Action? CutRequested;
    public event Action? PasteRequested;
    public event Action? UndoRequested;
    public event Action? RedoRequested;

    private void EnsureAtLeastOneBlock()
    {
        if (_document == null) return;
        if (_document.Blocks.Count == 0)
            _document.Blocks.Add(new DocBlock { Type = BlockType.Paragraph });

        foreach (var block in _document.Blocks)
            if (block.Inlines.Count == 0)
                block.Inlines.Add(new TextRun { Text = string.Empty });
    }

    // ─────────────────────────────────────────────────────────────────────
    //  TRUY CẬP NỘI DUNG
    // ─────────────────────────────────────────────────────────────────────
    private static string BlockText(DocBlock block) => string.Concat(block.Inlines.Select(r => r.Text));

    private int BlockLength(int index) =>
        _document != null && index >= 0 && index < _document.Blocks.Count
            ? BlockText(_document.Blocks[index]).Length
            : 0;

    public string PlainText => _document?.ToPlainText() ?? string.Empty;

    public bool HasSelection => _caretBlock != _anchorBlock || _caretOffset != _anchorOffset;

    /// <summary>Vùng bôi đen đã chuẩn hoá theo chiều xuôi.</summary>
    private (int startBlock, int startOffset, int endBlock, int endOffset) NormalizedSelection()
    {
        if (_anchorBlock < _caretBlock || (_anchorBlock == _caretBlock && _anchorOffset <= _caretOffset))
            return (_anchorBlock, _anchorOffset, _caretBlock, _caretOffset);
        return (_caretBlock, _caretOffset, _anchorBlock, _anchorOffset);
    }

    public string SelectedText
    {
        get
        {
            if (_document == null || !HasSelection) return string.Empty;

            var (startBlock, startOffset, endBlock, endOffset) = NormalizedSelection();
            var builder = new StringBuilder();

            for (int b = startBlock; b <= endBlock; b++)
            {
                var text = BlockText(_document.Blocks[b]);
                int from = b == startBlock ? startOffset : 0;
                int to = b == endBlock ? endOffset : text.Length;
                builder.Append(text[Math.Clamp(from, 0, text.Length)..Math.Clamp(to, 0, text.Length)]);
                if (b < endBlock) builder.Append('\n');
            }

            return builder.ToString();
        }
    }

    /// <summary>Định dạng tại con trỏ, để nút Đậm/Nghiêng hiển thị đúng trạng thái.</summary>
    public TextRun? RunAtCaret
    {
        get
        {
            if (_document == null || _caretBlock >= _document.Blocks.Count) return null;
            var (run, _) = FindRun(_document.Blocks[_caretBlock], Math.Max(0, _caretOffset - 1));
            return run;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  THAO TÁC VỚI ĐOẠN ĐỊNH DẠNG
    // ─────────────────────────────────────────────────────────────────────
    private static (TextRun run, int offsetInRun) FindRun(DocBlock block, int offset)
    {
        int consumed = 0;

        foreach (var run in block.Inlines)
        {
            if (offset <= consumed + run.Text.Length) return (run, offset - consumed);
            consumed += run.Text.Length;
        }

        var last = block.Inlines[^1];
        return (last, last.Text.Length);
    }

    /// <summary>Cắt các đoạn sao cho có đúng một ranh giới tại vị trí này.</summary>
    private static void SplitAt(DocBlock block, int offset)
    {
        int consumed = 0;

        for (int i = 0; i < block.Inlines.Count; i++)
        {
            var run = block.Inlines[i];
            int end = consumed + run.Text.Length;

            if (offset > consumed && offset < end)
            {
                int cut = offset - consumed;
                var tail = CloneRun(run);
                tail.Text = run.Text[cut..];
                run.Text = run.Text[..cut];
                block.Inlines.Insert(i + 1, tail);
                return;
            }

            if (offset <= end) return;
            consumed = end;
        }
    }

    private static TextRun CloneRun(TextRun source) => new()
    {
        Text = string.Empty,
        IsBold = source.IsBold,
        IsItalic = source.IsItalic,
        IsUnderline = source.IsUnderline,
        FontColor = source.FontColor,
        FontFamily = source.FontFamily,
        FontSize = source.FontSize
    };

    private static void MergeNeighbours(DocBlock block)
    {
        for (int i = block.Inlines.Count - 1; i > 0; i--)
        {
            var current = block.Inlines[i];
            var previous = block.Inlines[i - 1];

            if (current.Text.Length == 0 && block.Inlines.Count > 1)
            {
                block.Inlines.RemoveAt(i);
                continue;
            }

            if (SameFormatting(previous, current))
            {
                previous.Text += current.Text;
                block.Inlines.RemoveAt(i);
            }
        }

        if (block.Inlines.Count == 0) block.Inlines.Add(new TextRun { Text = string.Empty });
    }

    private static bool SameFormatting(TextRun a, TextRun b) =>
        a.IsBold == b.IsBold && a.IsItalic == b.IsItalic && a.IsUnderline == b.IsUnderline
        && a.FontColor == b.FontColor && a.FontFamily == b.FontFamily && a.FontSize == b.FontSize;

    /// <summary>
    /// Áp thay đổi định dạng. Có bôi đen thì đổi đúng phần được chọn; chưa bôi đen
    /// thì ghi nhớ để áp cho phần chữ gõ tiếp theo — giống hệt Word.
    /// </summary>
    public int ApplyFormatting(Action<TextRun> change)
    {
        DocumentChanging?.Invoke();
        if (_document == null) return 0;

        // Chưa bôi đen: chỉ ghi nhớ định dạng cho lần gõ tới.
        if (!HasSelection)
        {
            _pendingFormat = CloneRun(RunAtCaret ?? new TextRun());
            change(_pendingFormat);
            SelectionChanged?.Invoke();
            return 0;
        }

        var (startBlock, startOffset, endBlock, endOffset) = NormalizedSelection();
        int changed = 0;

        for (int b = startBlock; b <= endBlock && b < _document.Blocks.Count; b++)
        {
            var block = _document.Blocks[b];
            int from = b == startBlock ? startOffset : 0;
            int to = b == endBlock ? endOffset : BlockText(block).Length;

            SplitAt(block, from);
            SplitAt(block, to);

            int consumed = 0;
            foreach (var run in block.Inlines)
            {
                int end = consumed + run.Text.Length;
                if (consumed >= from && end <= to && run.Text.Length > 0)
                {
                    change(run);
                    changed++;
                }
                consumed = end;
            }

            MergeNeighbours(block);
        }

        // Giữ luôn định dạng vừa áp cho phần gõ tiếp ngay sau vùng bôi đen.
        _pendingFormat = CloneRun(RunAtCaret ?? new TextRun());

        Relayout();
        DocumentChanged?.Invoke();
        return changed;
    }

    /// <summary>Định dạng sẽ dùng cho chữ gõ tiếp theo (đang chờ, hoặc lấy tại con trỏ).</summary>
    public TextRun EffectiveFormat => _pendingFormat ?? RunAtCaret ?? new TextRun();

    private void ClearPendingFormat() => _pendingFormat = null;

    // ─────────────────────────────────────────────────────────────────────
    //  SOẠN THẢO
    // ─────────────────────────────────────────────────────────────────────
    public void InsertText(string text)
    {
        DocumentChanging?.Invoke();
        if (_document == null || text.Length == 0) return;

        if (HasSelection) DeleteSelection();

        var block = _document.Blocks[_caretBlock];
        SplitAt(block, _caretOffset);

        if (_pendingFormat != null && !SameFormatting(_pendingFormat, RunAtCaret ?? new TextRun()))
        {
            // Chèn thành một đoạn riêng mang định dạng đang chờ.
            var inserted = CloneRun(_pendingFormat);
            inserted.Text = text;
            InsertRunAt(block, _caretOffset, inserted);
        }
        else
        {
            var (run, offsetInRun) = FindRun(block, _caretOffset);
            run.Text = run.Text.Insert(Math.Clamp(offsetInRun, 0, run.Text.Length), text);
        }

        _caretOffset += text.Length;
        _anchorBlock = _caretBlock;
        _anchorOffset = _caretOffset;

        MergeNeighbours(block);
        Relayout();
        DocumentChanged?.Invoke();
    }

    /// <summary>Chèn một đoạn định dạng mới vào đúng vị trí ký tự trong đoạn văn.</summary>
    private static void InsertRunAt(DocBlock block, int offset, TextRun run)
    {
        int consumed = 0;

        for (int i = 0; i < block.Inlines.Count; i++)
        {
            if (consumed == offset)
            {
                block.Inlines.Insert(i, run);
                return;
            }
            consumed += block.Inlines[i].Text.Length;
        }

        block.Inlines.Add(run);
    }

    public void SplitParagraph()
    {
        DocumentChanging?.Invoke();
        if (_document == null) return;
        if (HasSelection) DeleteSelection();

        var block = _document.Blocks[_caretBlock];
        SplitAt(block, _caretOffset);

        var tail = new DocBlock { Type = block.Type };
        int consumed = 0;
        var keep = new List<TextRun>();

        foreach (var run in block.Inlines)
        {
            if (consumed >= _caretOffset) tail.Inlines.Add(run);
            else keep.Add(run);
            consumed += run.Text.Length;
        }

        block.Inlines.Clear();
        block.Inlines.AddRange(keep);
        if (block.Inlines.Count == 0) block.Inlines.Add(new TextRun { Text = string.Empty });
        if (tail.Inlines.Count == 0) tail.Inlines.Add(new TextRun { Text = string.Empty });

        _document.Blocks.Insert(_caretBlock + 1, tail);
        _caretBlock++;
        _caretOffset = 0;
        _anchorBlock = _caretBlock;
        _anchorOffset = 0;

        Relayout();
        DocumentChanged?.Invoke();
    }

    public void DeleteSelection()
    {
        DocumentChanging?.Invoke();
        if (_document == null || !HasSelection) return;

        var (startBlock, startOffset, endBlock, endOffset) = NormalizedSelection();

        var first = _document.Blocks[startBlock];
        var last = _document.Blocks[endBlock];

        SplitAt(first, startOffset);
        SplitAt(last, endOffset);

        var head = TakeRuns(first, 0, startOffset);
        var tail = TakeRuns(last, endOffset, BlockText(last).Length);

        first.Inlines.Clear();
        first.Inlines.AddRange(head);
        first.Inlines.AddRange(tail);
        if (first.Inlines.Count == 0) first.Inlines.Add(new TextRun { Text = string.Empty });

        if (endBlock > startBlock)
            _document.Blocks.RemoveRange(startBlock + 1, endBlock - startBlock);

        MergeNeighbours(first);

        _caretBlock = _anchorBlock = startBlock;
        _caretOffset = _anchorOffset = startOffset;

        Relayout();
        DocumentChanged?.Invoke();
    }

    private static List<TextRun> TakeRuns(DocBlock block, int from, int to)
    {
        var result = new List<TextRun>();
        int consumed = 0;

        foreach (var run in block.Inlines)
        {
            int end = consumed + run.Text.Length;
            if (consumed >= from && end <= to && run.Text.Length > 0) result.Add(run);
            consumed = end;
        }

        return result;
    }

    public void Backspace()
    {
        if (_document == null) return;

        if (HasSelection) { DeleteSelection(); return; }

        if (_caretOffset > 0)
        {
            _anchorBlock = _caretBlock;
            _anchorOffset = _caretOffset - 1;
            DeleteSelection();
            return;
        }

        if (_caretBlock == 0) return;

        // Đầu đoạn: nối đoạn này vào cuối đoạn trước.
        int previousLength = BlockLength(_caretBlock - 1);
        var previous = _document.Blocks[_caretBlock - 1];
        var current = _document.Blocks[_caretBlock];

        foreach (var run in current.Inlines.Where(r => r.Text.Length > 0))
            previous.Inlines.Add(run);

        _document.Blocks.RemoveAt(_caretBlock);
        MergeNeighbours(previous);

        _caretBlock--;
        _caretOffset = _anchorOffset = previousLength;
        _anchorBlock = _caretBlock;

        Relayout();
        DocumentChanged?.Invoke();
    }

    public void DeleteForward()
    {
        if (_document == null) return;

        if (HasSelection) { DeleteSelection(); return; }

        if (_caretOffset < BlockLength(_caretBlock))
        {
            _anchorBlock = _caretBlock;
            _anchorOffset = _caretOffset + 1;
            DeleteSelection();
            return;
        }

        if (_caretBlock >= _document.Blocks.Count - 1) return;

        _caretBlock++;
        _caretOffset = 0;
        Backspace();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  DI CHUYỂN CON TRỎ
    // ─────────────────────────────────────────────────────────────────────
    public void MoveCaret(int blockDelta, int offsetDelta, bool extend)
    {
        if (_document == null) return;

        if (blockDelta != 0)
        {
            int target = Math.Clamp(_caretBlock + blockDelta, 0, _document.Blocks.Count - 1);
            _caretBlock = target;
            _caretOffset = Math.Clamp(_caretOffset, 0, BlockLength(target));
        }
        else if (offsetDelta != 0)
        {
            int next = _caretOffset + offsetDelta;

            if (next < 0)
            {
                if (_caretBlock > 0)
                {
                    _caretBlock--;
                    _caretOffset = BlockLength(_caretBlock);
                }
                else
                {
                    _caretOffset = 0;
                }
            }
            else if (next > BlockLength(_caretBlock))
            {
                if (_caretBlock < _document.Blocks.Count - 1)
                {
                    _caretBlock++;
                    _caretOffset = 0;
                }
            }
            else
            {
                _caretOffset = next;
            }
        }

        if (!extend)
        {
            _anchorBlock = _caretBlock;
            _anchorOffset = _caretOffset;
        }

        ClearPendingFormat();
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    public void SetCaret(int block, int offset, bool extend)
    {
        if (_document == null) return;

        _caretBlock = Math.Clamp(block, 0, _document.Blocks.Count - 1);
        _caretOffset = Math.Clamp(offset, 0, BlockLength(_caretBlock));

        if (!extend)
        {
            _anchorBlock = _caretBlock;
            _anchorOffset = _caretOffset;
        }

        ClearPendingFormat();
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    public void SelectAll()
    {
        if (_document == null) return;

        _anchorBlock = 0;
        _anchorOffset = 0;
        _caretBlock = _document.Blocks.Count - 1;
        _caretOffset = BlockLength(_caretBlock);

        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  DÀN TRANG
    // ─────────────────────────────────────────────────────────────────────
    private static Typeface TypefaceFor(TextRun run) => new(
        new FontFamily(run.FontFamily is { Length: > 0 } family
            ? family + ", Segoe UI, -apple-system, sans-serif"
            : "Segoe UI, -apple-system, BlinkMacSystemFont, Roboto, sans-serif"),
        run.IsItalic ? FontStyle.Italic : FontStyle.Normal,
        run.IsBold ? FontWeight.Bold : FontWeight.Normal);

    private static double SizeFor(TextRun run) => run.FontSize is > 0 ? run.FontSize.Value : 14;

    private static FormattedText Measure(string text, TextRun run) => new(
        text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
        TypefaceFor(run), SizeFor(run), TextBrush);

    /// <summary>Chia nội dung thành từng mẩu chữ đã đo và xếp xuống dòng theo bề rộng trang.</summary>
    private void Relayout()
    {
        _pieces.Clear();
        _lineTops.Clear();
        _lineHeights.Clear();

        if (_document == null)
        {
            _contentHeight = 200;
            InvalidateMeasure();
            InvalidateVisual();
            return;
        }

        EnsureAtLeastOneBlock();

        double usableWidth = PageWidth - PageMargin * 2;
        double y = PageMargin;
        int lineIndex = 0;

        for (int blockIndex = 0; blockIndex < _document.Blocks.Count; blockIndex++)
        {
            var block = _document.Blocks[blockIndex];
            double x = PageMargin;
            double lineHeight = 0;
            int lineStart = _pieces.Count;

            _lineTops.Add(y);
            _lineHeights.Add(0);

            int offset = 0;

            foreach (var run in block.Inlines)
            {
                foreach (var token in Tokenize(run.Text))
                {
                    var measured = Measure(token, run);
                    double width = measured.Width;
                    double height = measured.Height;

                    // Xuống dòng khi vượt quá bề rộng trang (bỏ qua khoảng trắng đầu dòng).
                    if (x > PageMargin && x + width > PageMargin + usableWidth && token.Trim().Length > 0)
                    {
                        _lineHeights[lineIndex] = Math.Max(lineHeight, 18);
                        y += _lineHeights[lineIndex];
                        lineIndex++;
                        _lineTops.Add(y);
                        _lineHeights.Add(0);

                        x = PageMargin;
                        lineHeight = 0;
                        lineStart = _pieces.Count;
                    }

                    _pieces.Add(new LayoutPiece
                    {
                        Text = token,
                        Run = run,
                        BlockIndex = blockIndex,
                        StartOffset = offset,
                        X = x,
                        Y = y,
                        Width = width,
                        Height = height,
                        LineIndex = lineIndex
                    });

                    x += width;
                    offset += token.Length;
                    lineHeight = Math.Max(lineHeight, height);
                }
            }

            _lineHeights[lineIndex] = Math.Max(lineHeight, 18);
            y += _lineHeights[lineIndex] + 6;   // khoảng cách giữa các đoạn
            lineIndex++;
            _ = lineStart;
        }

        _contentHeight = y + PageMargin;
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Tách thành từng từ, giữ khoảng trắng dính vào cuối từ để xuống dòng cho đúng.</summary>
    private static IEnumerable<string> Tokenize(string text)
    {
        if (text.Length == 0) yield break;

        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != ' ') continue;

            // gom cả cụm khoảng trắng vào từ vừa rồi
            int end = i;
            while (end < text.Length && text[end] == ' ') end++;

            yield return text[start..end];
            start = end;
            i = end - 1;
        }

        if (start < text.Length) yield return text[start..];
    }

    protected override Size MeasureOverride(Size availableSize) => new(PageWidth, Math.Max(_contentHeight, 200));

    // ─────────────────────────────────────────────────────────────────────
    //  VẼ
    // ─────────────────────────────────────────────────────────────────────
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // Nền trang giấy — cũng là vùng nhận chuột.
        context.FillRectangle(Brushes.White, new Rect(0, 0, PageWidth, Math.Max(_contentHeight, Bounds.Height)));

        if (_document == null) return;

        DrawSelection(context);

        foreach (var piece in _pieces)
        {
            if (piece.Text.Trim().Length == 0) continue;

            var brush = piece.Run.FontColor is { Length: > 0 } color
                ? new SolidColorBrush(Color.Parse(color))
                : TextBrush;

            var formatted = new FormattedText(piece.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                TypefaceFor(piece.Run), SizeFor(piece.Run), brush);

            context.DrawText(formatted, new Point(piece.X, piece.Y));

            if (piece.Run.IsUnderline)
            {
                double underlineY = piece.Y + formatted.Height - 2;
                context.DrawLine(new Pen(brush, 1),
                    new Point(piece.X, underlineY),
                    new Point(piece.X + formatted.Width, underlineY));
            }
        }

        DrawCaret(context);
    }

    private void DrawSelection(DrawingContext context)
    {
        if (!HasSelection) return;

        var (startBlock, startOffset, endBlock, endOffset) = NormalizedSelection();

        foreach (var piece in _pieces)
        {
            if (piece.BlockIndex < startBlock || piece.BlockIndex > endBlock) continue;

            int from = piece.BlockIndex == startBlock ? startOffset : 0;
            int to = piece.BlockIndex == endBlock ? endOffset : int.MaxValue;

            int pieceStart = piece.StartOffset;
            int pieceEnd = piece.StartOffset + piece.Length;

            int overlapStart = Math.Max(pieceStart, from);
            int overlapEnd = Math.Min(pieceEnd, to);
            if (overlapEnd <= overlapStart) continue;

            double left = piece.X + PrefixWidth(piece, overlapStart - pieceStart);
            double right = piece.X + PrefixWidth(piece, overlapEnd - pieceStart);

            context.FillRectangle(SelectionBrush,
                new Rect(left, piece.Y, Math.Max(2, right - left), piece.Height));
        }
    }

    private static double PrefixWidth(LayoutPiece piece, int characters)
    {
        characters = Math.Clamp(characters, 0, piece.Length);
        if (characters == 0) return 0;
        if (characters == piece.Length) return piece.Width;
        return Measure(piece.Text[..characters], piece.Run).Width;
    }

    private void DrawCaret(DrawingContext context)
    {
        if (!IsFocused && !_dragging) return;

        var (x, y, height) = CaretPosition();
        context.DrawLine(new Pen(CaretBrush, 1.6), new Point(x, y), new Point(x, y + height));
    }

    private (double x, double y, double height) CaretPosition()
    {
        var candidates = _pieces.Where(p => p.BlockIndex == _caretBlock).ToList();

        if (candidates.Count == 0)
        {
            double top = _lineTops.Count > 0 ? _lineTops[Math.Min(_caretBlock, _lineTops.Count - 1)] : PageMargin;
            return (PageMargin, top, 18);
        }

        foreach (var piece in candidates)
        {
            int start = piece.StartOffset;
            int end = start + piece.Length;

            if (_caretOffset >= start && _caretOffset <= end)
                return (piece.X + PrefixWidth(piece, _caretOffset - start), piece.Y, Math.Max(piece.Height, 16));
        }

        var last = candidates[^1];
        return (last.X + last.Width, last.Y, Math.Max(last.Height, 16));
    }

    // ─────────────────────────────────────────────────────────────────────
    //  CHUỘT
    // ─────────────────────────────────────────────────────────────────────
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var (block, offset) = HitTest(e.GetPosition(this));

        if (e.ClickCount >= 2)
        {
            SelectWordAt(block, offset);
            return;
        }

        SetCaret(block, offset, extend: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        _dragging = true;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;

        var (block, offset) = HitTest(e.GetPosition(this));
        SetCaret(block, offset, extend: true);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;

        _dragging = false;
        e.Pointer.Capture(null);
    }

    /// <summary>Đổi toạ độ chuột thành vị trí trong tài liệu.</summary>
    public (int block, int offset) HitTest(Point point)
    {
        if (_document == null || _pieces.Count == 0) return (0, 0);

        // Tìm dòng gần nhất theo trục dọc.
        var line = _pieces
            .OrderBy(p => Math.Abs(p.Y + p.Height / 2 - point.Y))
            .First().LineIndex;

        var onLine = _pieces.Where(p => p.LineIndex == line).OrderBy(p => p.X).ToList();
        if (onLine.Count == 0) return (0, 0);

        if (point.X <= onLine[0].X) return (onLine[0].BlockIndex, onLine[0].StartOffset);

        foreach (var piece in onLine)
        {
            if (point.X > piece.X + piece.Width) continue;

            // Tìm ký tự gần con trỏ nhất trong mẩu chữ này.
            double relative = point.X - piece.X;
            int best = 0;
            double bestDistance = double.MaxValue;

            for (int i = 0; i <= piece.Length; i++)
            {
                double distance = Math.Abs(PrefixWidth(piece, i) - relative);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = i;
            }

            return (piece.BlockIndex, piece.StartOffset + best);
        }

        var last = onLine[^1];
        return (last.BlockIndex, last.StartOffset + last.Length);
    }

    private void SelectWordAt(int block, int offset)
    {
        if (_document == null) return;

        var text = BlockText(_document.Blocks[block]);
        if (text.Length == 0) return;

        int start = Math.Clamp(offset, 0, text.Length - 1);
        int end = start;

        while (start > 0 && !char.IsWhiteSpace(text[start - 1])) start--;
        while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;

        _anchorBlock = _caretBlock = block;
        _anchorOffset = start;
        _caretOffset = end;

        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  BÀN PHÍM
    // ─────────────────────────────────────────────────────────────────────
    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || string.IsNullOrEmpty(e.Text)) return;
        if (e.Text.Length == 1 && char.IsControl(e.Text[0])) return;

        InsertText(e.Text);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;

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

        switch (e.Key)
        {
            case Key.Left:  MoveCaret(0, -1, shift); break;
            case Key.Right: MoveCaret(0, 1, shift); break;
            case Key.Up:    MoveCaret(-1, 0, shift); break;
            case Key.Down:  MoveCaret(1, 0, shift); break;

            case Key.Home: SetCaret(_caretBlock, 0, shift); break;
            case Key.End:  SetCaret(_caretBlock, BlockLength(_caretBlock), shift); break;

            case Key.Enter: SplitParagraph(); break;
            case Key.Back:  Backspace(); break;
            case Key.Delete: DeleteForward(); break;

            case Key.Tab: InsertText("    "); break;

            default: return;
        }

        e.Handled = true;
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateVisual();
    }

    /// <summary>
    /// Tìm chuỗi kể từ sau con trỏ, quay vòng về đầu khi hết tài liệu.
    /// Tìm thấy thì bôi đen luôn chỗ đó. Trả về false nếu không có chỗ nào khớp.
    /// </summary>
    public bool FindNext(string term, bool caseSensitive = false)
    {
        if (_document == null || string.IsNullOrEmpty(term)) return false;

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int blockCount = _document.Blocks.Count;

        // Quét từ đoạn đang đứng, đi hết rồi vòng lại chính nó.
        for (int step = 0; step <= blockCount; step++)
        {
            int blockIndex = (_caretBlock + step) % blockCount;
            var text = BlockText(_document.Blocks[blockIndex]);
            if (text.Length == 0) continue;

            int from = step == 0 ? Math.Clamp(_caretOffset, 0, text.Length) : 0;
            int found = from <= text.Length - term.Length
                ? text.IndexOf(term, from, comparison)
                : -1;

            // Vòng cuối: cho phép khớp cả phần đầu đoạn ban đầu.
            if (found < 0 && step == blockCount) found = text.IndexOf(term, comparison);
            if (found < 0) continue;

            _anchorBlock = _caretBlock = blockIndex;
            _anchorOffset = found;
            _caretOffset = found + term.Length;

            ClearPendingFormat();
            SelectionChanged?.Invoke();
            InvalidateVisual();
            return true;
        }

        return false;
    }

    /// <summary>Dựng lại bố cục sau khi tài liệu bị thay từ bên ngoài (ví dụ hoàn tác).</summary>
    public void ReloadDocument()
    {
        EnsureAtLeastOneBlock();

        _caretBlock = Math.Clamp(_caretBlock, 0, Math.Max(0, (_document?.Blocks.Count ?? 1) - 1));
        _caretOffset = Math.Clamp(_caretOffset, 0, BlockLength(_caretBlock));
        _anchorBlock = _caretBlock;
        _anchorOffset = _caretOffset;

        Relayout();
    }

    /// <summary>Nạp văn bản thuần, giữ định dạng mặc định — dùng khi mở tệp .txt.</summary>
    public void SetPlainText(string text)
    {
        if (_document == null) return;

        _document.Blocks.Clear();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var block = new DocBlock { Type = BlockType.Paragraph };
            block.Inlines.Add(new TextRun { Text = line });
            _document.Blocks.Add(block);
        }

        EnsureAtLeastOneBlock();
        _caretBlock = _caretOffset = _anchorBlock = _anchorOffset = 0;
        Relayout();
    }
}
