using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Wocel.Core.Models;

public enum CellDataType
{
    Empty,
    String,
    Number,
    Boolean,
    Date,
    Formula,
    Error
}

public class CellAddress
{
    public int Row { get; }
    public int Column { get; }
    public string A1Notation { get; }

    public CellAddress(int row, int col)
    {
        Row = row;
        Column = col;
        A1Notation = ToA1(row, col);
    }

    public CellAddress(string a1Notation)
    {
        A1Notation = a1Notation.ToUpperInvariant().Trim();
        var (row, col) = FromA1(A1Notation);
        Row = row;
        Column = col;
    }

    public static string ToA1(int row, int col)
    {
        var colName = string.Empty;
        var tempCol = col;
        while (tempCol > 0)
        {
            var rem = (tempCol - 1) % 26;
            colName = (char)('A' + rem) + colName;
            tempCol = (tempCol - 1) / 26;
        }
        return $"{colName}{row}";
    }

    public static (int Row, int Column) FromA1(string a1)
    {
        var match = Regex.Match(a1.Trim(), @"^([A-Za-z]+)(\d+)$");
        if (!match.Success)
            throw new ArgumentException($"Invalid A1 address format: '{a1}'");

        var colLetters = match.Groups[1].Value.ToUpperInvariant();
        var rowNum = int.Parse(match.Groups[2].Value);

        var colNum = 0;
        foreach (var c in colLetters)
        {
            colNum = colNum * 26 + (c - 'A' + 1);
        }

        return (rowNum, colNum);
    }

    public override string ToString() => A1Notation;
    public override bool Equals(object? obj) => obj is CellAddress other && A1Notation == other.A1Notation;
    public override int GetHashCode() => A1Notation.GetHashCode();
}

public class CellStyle
{
    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }
    public bool IsUnderline { get; set; }
    public string? BackgroundColor { get; set; }
    public string? TextColor { get; set; }
    public string? NumberFormat { get; set; }
    public string HorizontalAlignment { get; set; } = "Left";

    /// <summary>Cỡ chữ của ô, tính theo point. Bỏ trống nghĩa là dùng cỡ mặc định.</summary>
    public int? FontSize { get; set; }

    /// <summary>Phông chữ của ô. Bỏ trống nghĩa là dùng phông mặc định của lưới.</summary>
    public string? FontFamily { get; set; }

    public bool IsDefault =>
        !IsBold && !IsItalic && !IsUnderline
        && BackgroundColor == null && TextColor == null && NumberFormat == null
        && FontSize == null && FontFamily == null
        && HorizontalAlignment == "Left";

    public CellStyle Clone() => new()
    {
        IsBold = IsBold,
        IsItalic = IsItalic,
        IsUnderline = IsUnderline,
        BackgroundColor = BackgroundColor,
        TextColor = TextColor,
        NumberFormat = NumberFormat,
        FontSize = FontSize,
        FontFamily = FontFamily,
        HorizontalAlignment = HorizontalAlignment
    };
}

public class CellValue
{
    public CellDataType DataType { get; set; } = CellDataType.Empty;
    public object? RawValue { get; set; }
    public string? Formula { get; set; }
    public object? EvaluatedValue { get; set; }
    public string? FormattedText { get; set; }
    public CellStyle? Style { get; set; }

    public double? GetNumericValue()
    {
        var val = EvaluatedValue ?? RawValue;
        if (val is null) return null;
        if (val is double d) return d;
        if (val is int i) return (double)i;
        if (val is long l) return (double)l;
        if (val is decimal dec) return (double)dec;
        if (val is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number) return je.GetDouble();
            if (double.TryParse(je.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }
        if (double.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
            return num;
        return null;
    }

    public string GetDisplayString()
    {
        // Định dạng số của ô được áp lên giá trị đã tính, nên sửa giá trị vẫn giữ định dạng.
        if (Style?.NumberFormat is { Length: > 0 } numberFormat && GetNumericValue() is { } number)
            return ApplyNumberFormat(number, numberFormat);

        if (FormattedText != null) return FormattedText;
        if (EvaluatedValue != null) return Format(EvaluatedValue);
        if (RawValue != null) return Format(RawValue);
        return string.Empty;
    }

    /// <summary>Các mã định dạng số Wocel hỗ trợ, dùng chung cho thanh công cụ và khi lưu tệp.</summary>
    public static string ApplyNumberFormat(double value, string format) => format switch
    {
        "currency" => value.ToString("#,##0 ₫", CultureInfo.GetCultureInfo("vi-VN")),
        "percent" => value.ToString("0.0%", CultureInfo.InvariantCulture),
        "thousands" => value.ToString("#,##0.##", CultureInfo.GetCultureInfo("vi-VN")),
        "integer" => value.ToString("#,##0", CultureInfo.GetCultureInfo("vi-VN")),
        "decimal2" => value.ToString("#,##0.00", CultureInfo.GetCultureInfo("vi-VN")),
        "scientific" => value.ToString("0.###E+0", CultureInfo.InvariantCulture),
        "date" => DateTime.FromOADate(value).ToString("dd/MM/yyyy"),
        _ => Format(value)
    };

    private static string Format(object value) => value switch
    {
        JsonElement json => json.ToString(),
        DateTime date => date.TimeOfDay == TimeSpan.Zero
            ? date.ToString("dd/MM/yyyy")
            : date.ToString("dd/MM/yyyy HH:mm"),
        bool flag => flag ? "TRUE" : "FALSE",
        _ => value.ToString() ?? string.Empty
    };
}

public class SpreadsheetDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Untitled Sheet";
    public List<SpreadsheetWorksheet> Sheets { get; set; } = new();

    /// <summary>Trang tính đang mở (0-based).</summary>
    public int ActiveSheetIndex { get; set; }

    public SpreadsheetWorksheet GetOrCreateActiveSheet()
    {
        if (Sheets.Count == 0) Sheets.Add(new SpreadsheetWorksheet { Name = "Sheet1" });

        ActiveSheetIndex = Math.Clamp(ActiveSheetIndex, 0, Sheets.Count - 1);
        return Sheets[ActiveSheetIndex];
    }

    /// <summary>Thêm trang tính mới, tự đặt tên không trùng, rồi chuyển sang trang đó.</summary>
    public SpreadsheetWorksheet AddSheet(string? name = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            int index = Sheets.Count + 1;
            while (Sheets.Any(s => s.Name.Equals($"Sheet{index}", StringComparison.OrdinalIgnoreCase))) index++;
            name = $"Sheet{index}";
        }

        var sheet = new SpreadsheetWorksheet { Name = name! };
        Sheets.Add(sheet);
        ActiveSheetIndex = Sheets.Count - 1;
        return sheet;
    }

    /// <summary>Xoá một trang tính. Không cho xoá trang cuối cùng.</summary>
    public bool RemoveSheet(int index)
    {
        if (Sheets.Count <= 1 || index < 0 || index >= Sheets.Count) return false;

        Sheets.RemoveAt(index);
        ActiveSheetIndex = Math.Clamp(ActiveSheetIndex, 0, Sheets.Count - 1);
        return true;
    }

    /// <summary>Đổi tên trang tính, từ chối nếu trùng tên trang khác.</summary>
    public bool RenameSheet(int index, string newName)
    {
        if (index < 0 || index >= Sheets.Count || string.IsNullOrWhiteSpace(newName)) return false;

        var trimmed = newName.Trim();
        if (Sheets.Where((_, i) => i != index).Any(s => s.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
            return false;

        Sheets[index].Name = trimmed;
        return true;
    }
}

public class SpreadsheetWorksheet
{
    public string Name { get; set; } = "Sheet1";
    public Dictionary<string, CellValue> Cells { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Các vùng ô đã gộp, mỗi vùng ghi dạng "A1:C2". Ô góc trên-trái giữ nội dung,
    /// các ô còn lại bị che khi hiển thị.
    /// </summary>
    public List<string> MergedRanges { get; set; } = new();

    /// <summary>Lấy style của ô, tự tạo nếu chưa có (dùng khi áp định dạng).</summary>
    public CellStyle GetOrCreateStyle(string a1)
    {
        var address = a1.Trim().ToUpperInvariant();

        if (!Cells.TryGetValue(address, out var cell))
        {
            cell = new CellValue { DataType = CellDataType.Empty };
            Cells[address] = cell;
        }

        return cell.Style ??= new CellStyle();
    }

    public int MaxRow => Cells.Keys.Select(k => new CellAddress(k).Row).DefaultIfEmpty(1).Max();
    public int MaxCol => Cells.Keys.Select(k => new CellAddress(k).Column).DefaultIfEmpty(1).Max();

    public CellValue GetCell(string a1)
    {
        if (Cells.TryGetValue(a1.Trim(), out var val))
            return val;
        return new CellValue { DataType = CellDataType.Empty };
    }

    public void SetCell(string a1, CellValue val)
    {
        Cells[a1.Trim().ToUpperInvariant()] = val;
    }

    public void SetValue(string a1, object? value)
    {
        var address = a1.Trim().ToUpperInvariant();

        // Định dạng thuộc về ô, không thuộc về giá trị — gõ đè giá trị mới
        // không được làm mất đậm/nghiêng/cỡ chữ người dùng đã đặt.
        var keepStyle = Cells.TryGetValue(address, out var existing) ? existing.Style : null;

        if (value == null || (value is string s && string.IsNullOrWhiteSpace(s)))
        {
            // Xoá nội dung nhưng giữ định dạng, giống phím Delete của Excel.
            if (keepStyle is { IsDefault: false })
                Cells[address] = new CellValue { DataType = CellDataType.Empty, Style = keepStyle };
            else
                Cells.Remove(address);
            return;
        }

        Cells[address] = BuildCell(value) is { } cell
            ? Apply(cell, keepStyle)
            : new CellValue { DataType = CellDataType.Empty, Style = keepStyle };
    }

    private static CellValue Apply(CellValue cell, CellStyle? style)
    {
        cell.Style = style;
        return cell;
    }

    /// <summary>Đoán kiểu dữ liệu từ giá trị người dùng nhập.</summary>
    private static CellValue BuildCell(object value)
    {
        if (value is string formula && formula.StartsWith('='))
            return new CellValue { DataType = CellDataType.Formula, Formula = formula, RawValue = formula };

        double? number = value switch
        {
            double d => d,
            int i => i,
            long l => l,
            decimal dec => (double)dec,
            float f => f,
            _ => double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null
        };

        if (number.HasValue)
            return new CellValue { DataType = CellDataType.Number, RawValue = number.Value, EvaluatedValue = number.Value };

        return new CellValue
        {
            DataType = CellDataType.String,
            RawValue = value.ToString(),
            EvaluatedValue = value.ToString()
        };
    }
}
