using System;
using System.Collections.Generic;
using System.Linq;
using Wocel.Core.Models;

namespace Wocel.Shell.Services;

/// <summary>
/// Lịch sử hoàn tác dựa trên ảnh chụp trạng thái. Tài liệu văn phòng thường nhỏ
/// nên chụp toàn bộ đơn giản và chắc chắn hơn ghi lại từng thao tác.
/// </summary>
public sealed class UndoHistory<T>
{
    private readonly List<T> _past = new();
    private readonly List<T> _future = new();
    private readonly Func<T> _capture;
    private readonly Action<T> _restore;
    private readonly int _limit;

    private bool _restoring;

    public UndoHistory(Func<T> capture, Action<T> restore, int limit = 60)
    {
        _capture = capture;
        _restore = restore;
        _limit = Math.Max(1, limit);
    }

    public bool CanUndo => _past.Count > 0;
    public bool CanRedo => _future.Count > 0;

    /// <summary>Ghi lại trạng thái TRƯỚC khi thay đổi. Gọi ngay trước mỗi thao tác sửa.</summary>
    public void Record()
    {
        if (_restoring) return;

        _past.Add(_capture());
        if (_past.Count > _limit) _past.RemoveAt(0);

        // Có thao tác mới thì nhánh "làm lại" cũ không còn ý nghĩa.
        _future.Clear();
    }

    public bool Undo()
    {
        if (!CanUndo) return false;

        var current = _capture();
        var previous = _past[^1];
        _past.RemoveAt(_past.Count - 1);
        _future.Add(current);

        Apply(previous);
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo) return false;

        var current = _capture();
        var next = _future[^1];
        _future.RemoveAt(_future.Count - 1);
        _past.Add(current);

        Apply(next);
        return true;
    }

    private void Apply(T snapshot)
    {
        _restoring = true;
        try { _restore(snapshot); }
        finally { _restoring = false; }
    }

    public void Clear()
    {
        _past.Clear();
        _future.Clear();
    }
}

/// <summary>Chụp và khôi phục nội dung một trang tính.</summary>
public static class SheetSnapshot
{
    public static Dictionary<string, CellValue> Capture(SpreadsheetWorksheet sheet)
    {
        var copy = new Dictionary<string, CellValue>(StringComparer.OrdinalIgnoreCase);

        foreach (var (address, cell) in sheet.Cells)
        {
            copy[address] = new CellValue
            {
                DataType = cell.DataType,
                RawValue = cell.RawValue,
                Formula = cell.Formula,
                EvaluatedValue = cell.EvaluatedValue,
                FormattedText = cell.FormattedText,
                Style = cell.Style?.Clone()
            };
        }

        return copy;
    }

    public static void Restore(SpreadsheetWorksheet sheet, Dictionary<string, CellValue> snapshot)
    {
        sheet.Cells.Clear();
        foreach (var (address, cell) in snapshot) sheet.Cells[address] = cell;
    }
}

/// <summary>Chụp và khôi phục nội dung một tài liệu văn bản.</summary>
public static class DocumentSnapshot
{
    public static List<DocBlock> Capture(DocumentDocument document) =>
        document.Blocks.Select(block => new DocBlock
        {
            Id = block.Id,
            Type = block.Type,
            TableData = block.TableData,
            EmbeddedSheetId = block.EmbeddedSheetId,
            EmbeddedRange = block.EmbeddedRange,
            IsLiveSynced = block.IsLiveSynced,
            Inlines = block.Inlines.Select(run => new TextRun
            {
                Text = run.Text,
                IsBold = run.IsBold,
                IsItalic = run.IsItalic,
                IsUnderline = run.IsUnderline,
                FontColor = run.FontColor,
                FontFamily = run.FontFamily,
                FontSize = run.FontSize
            }).ToList()
        }).ToList();

    public static void Restore(DocumentDocument document, List<DocBlock> snapshot)
    {
        document.Blocks.Clear();
        document.Blocks.AddRange(snapshot);
    }
}
