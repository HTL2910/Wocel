using System.Globalization;
using System.Text.RegularExpressions;
using Wocel.Core.Models;

namespace Wocel.Excel.Engine;

public class FormulaEngine
{
    private readonly SpreadsheetWorksheet _sheet;
    private readonly HashSet<string> _evaluatingCells = new(StringComparer.OrdinalIgnoreCase);

    public FormulaEngine(SpreadsheetWorksheet sheet)
    {
        _sheet = sheet;
    }

    public void RecalculateAll()
    {
        _evaluatingCells.Clear();
        foreach (var kvp in _sheet.Cells.ToList())
        {
            if (kvp.Value.DataType == CellDataType.Formula && !string.IsNullOrWhiteSpace(kvp.Value.Formula))
            {
                kvp.Value.EvaluatedValue = EvaluateFormula(kvp.Key, kvp.Value.Formula);
            }
        }
    }

    public object? EvaluateFormula(string currentCellAddress, string formula)
    {
        if (string.IsNullOrWhiteSpace(formula)) return null;

        var cleanFormula = formula.Trim();
        if (cleanFormula.StartsWith('=')) cleanFormula = cleanFormula.Substring(1).Trim();

        var normalizedAddr = currentCellAddress.Trim().ToUpperInvariant();

        // Check for Circular Dependency
        if (_evaluatingCells.Contains(normalizedAddr))
        {
            return "#CIRCULAR!";
        }

        try
        {
            _evaluatingCells.Add(normalizedAddr);
            return EvaluateExpression(cleanFormula);
        }
        catch (Exception ex)
        {
            return $"#ERROR: {ex.Message}";
        }
        finally
        {
            _evaluatingCells.Remove(normalizedAddr);
        }
    }

    private object? EvaluateExpression(string expr)
    {
        expr = expr.Trim();
        if (string.IsNullOrEmpty(expr)) return null;

        // Check Function call: e.g. SUM(A1:A5), IF(A1 > 10, "Yes", "No")
        var funcMatch = Regex.Match(expr, @"^([A-Za-z]+)\((.*)\)$", RegexOptions.Singleline);
        if (funcMatch.Success)
        {
            var funcName = funcMatch.Groups[1].Value.ToUpperInvariant();
            var innerArgs = SplitFunctionArguments(funcMatch.Groups[2].Value);
            return ExecuteFunction(funcName, innerArgs);
        }

        // Basic Math Expression Parser (+, -, *, /)
        return EvaluateArithmetic(expr);
    }

    private object? ExecuteFunction(string funcName, List<string> args)
    {
        switch (funcName)
        {
            case "SUM":
                return SumRange(args);
            case "AVERAGE":
            case "AVG":
                var (sum, count) = GetSumAndCount(args);
                return count == 0 ? 0.0 : sum / count;
            case "COUNT":
                return (double)GetValuesFromArgs(args).Count(v => v is double or int or float or decimal);
            case "MIN":
                var minVals = GetNumericValues(args);
                return minVals.Count == 0 ? 0.0 : minVals.Min();
            case "MAX":
                var maxVals = GetNumericValues(args);
                return maxVals.Count == 0 ? 0.0 : maxVals.Max();
            case "IF":
                if (args.Count < 2) return "#VALUE!";
                var conditionResult = EvaluateCondition(args[0]);
                if (conditionResult)
                {
                    return EvaluateExpression(args[1]);
                }
                else
                {
                    return args.Count >= 3 ? EvaluateExpression(args[2]) : false;
                }
            case "CONCAT":
            case "CONCATENATE":
                return string.Join("", GetValuesFromArgs(args).Select(FormatValue));

            // ── Toán học ─────────────────────────────────────────────────
            case "PRODUCT":
            {
                var values = GetNumericValues(args);
                return values.Count == 0 ? 0.0 : values.Aggregate(1.0, (a, b) => a * b);
            }
            case "ABS": return Math.Abs(Num(args, 0));
            case "INT": return Math.Floor(Num(args, 0));
            case "SQRT":
            {
                double value = Num(args, 0);
                return value < 0 ? "#NUM!" : Math.Sqrt(value);
            }
            case "POWER": return Math.Pow(Num(args, 0), Num(args, 1, 1));
            case "MOD":
            {
                double divisor = Num(args, 1);
                return divisor == 0 ? "#DIV/0!" : Num(args, 0) % divisor;
            }
            case "ROUND": return Math.Round(Num(args, 0), DigitCount(args), MidpointRounding.AwayFromZero);
            case "ROUNDUP":
            {
                double factor = Math.Pow(10, DigitCount(args));
                return Math.Ceiling(Num(args, 0) * factor) / factor;
            }
            case "ROUNDDOWN":
            {
                double factor = Math.Pow(10, DigitCount(args));
                return Math.Floor(Num(args, 0) * factor) / factor;
            }
            case "CEILING":
            {
                double step = Num(args, 1, 1);
                return step == 0 ? 0.0 : Math.Ceiling(Num(args, 0) / step) * step;
            }
            case "FLOOR":
            {
                double step = Num(args, 1, 1);
                return step == 0 ? 0.0 : Math.Floor(Num(args, 0) / step) * step;
            }

            // ── Thống kê ─────────────────────────────────────────────────
            case "COUNTA":
                return (double)GetValuesFromArgs(args)
                    .Count(v => v != null && !string.IsNullOrEmpty(v.ToString()));
            case "COUNTBLANK":
                return (double)GetValuesFromArgs(args)
                    .Count(v => v == null || string.IsNullOrEmpty(v.ToString()));
            case "COUNTIF":
            {
                if (args.Count < 2) return "#VALUE!";
                var criteria = TextOf(args[1]);
                return (double)GetValuesFromArgs(new List<string> { args[0] })
                    .Count(v => MatchesCriteria(v, criteria));
            }
            case "SUMIF":
            {
                if (args.Count < 2) return "#VALUE!";
                var criteria = TextOf(args[1]);
                var checkValues = GetValuesFromArgs(new List<string> { args[0] });
                var sumValues = args.Count >= 3
                    ? GetValuesFromArgs(new List<string> { args[2] })
                    : checkValues;

                double total = 0;
                for (int i = 0; i < checkValues.Count; i++)
                {
                    if (!MatchesCriteria(checkValues[i], criteria)) continue;
                    if (i < sumValues.Count) total += ConvertToDouble(sumValues[i]) ?? 0;
                }
                return total;
            }
            case "MEDIAN":
            {
                var values = GetNumericValues(args).OrderBy(v => v).ToList();
                if (values.Count == 0) return 0.0;
                int middle = values.Count / 2;
                return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
            }
            case "STDEV":
            {
                var values = GetNumericValues(args);
                if (values.Count < 2) return "#DIV/0!";
                double mean = values.Average();
                return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
            }

            // ── Luận lý ──────────────────────────────────────────────────
            case "AND": return args.Count > 0 && args.All(EvaluateCondition);
            case "OR": return args.Count > 0 && args.Any(EvaluateCondition);
            case "NOT": return args.Count > 0 && !EvaluateCondition(args[0]);
            case "TRUE": return true;
            case "FALSE": return false;
            case "IFERROR":
            {
                if (args.Count == 0) return "#VALUE!";
                try
                {
                    var value = EvaluateExpression(args[0]);
                    bool isError = value is string text && text.StartsWith('#');
                    if (!isError) return value;
                }
                catch
                {
                    // rơi xuống giá trị thay thế
                }
                return args.Count >= 2 ? EvaluateExpression(args[1]) : "#N/A";
            }

            // ── Văn bản ──────────────────────────────────────────────────
            case "LEN": return (double)TextOf(args, 0).Length;
            case "UPPER": return TextOf(args, 0).ToUpperInvariant();
            case "LOWER": return TextOf(args, 0).ToLowerInvariant();
            case "TRIM": return System.Text.RegularExpressions.Regex.Replace(TextOf(args, 0).Trim(), @"\s+", " ");
            case "PROPER":
                return System.Globalization.CultureInfo.CurrentCulture.TextInfo
                    .ToTitleCase(TextOf(args, 0).ToLowerInvariant());
            case "LEFT":
            {
                var text = TextOf(args, 0);
                int take = Math.Clamp((int)Num(args, 1, 1), 0, text.Length);
                return text[..take];
            }
            case "RIGHT":
            {
                var text = TextOf(args, 0);
                int take = Math.Clamp((int)Num(args, 1, 1), 0, text.Length);
                return text[^take..];
            }
            case "MID":
            {
                var text = TextOf(args, 0);
                int start = Math.Max(1, (int)Num(args, 1, 1));
                if (start > text.Length) return string.Empty;
                int take = Math.Clamp((int)Num(args, 2, 0), 0, text.Length - start + 1);
                return text.Substring(start - 1, take);
            }
            case "SUBSTITUTE":
            {
                if (args.Count < 3) return TextOf(args, 0);
                var oldText = TextOf(args, 1);
                return oldText.Length == 0
                    ? TextOf(args, 0)
                    : TextOf(args, 0).Replace(oldText, TextOf(args, 2));
            }
            case "FIND":
            {
                if (args.Count < 2) return "#VALUE!";
                int position = TextOf(args, 1).IndexOf(TextOf(args, 0), StringComparison.Ordinal);
                return position < 0 ? "#VALUE!" : (double)(position + 1);
            }
            case "REPT":
            {
                int times = Math.Clamp((int)Num(args, 1, 0), 0, 10000);
                return string.Concat(Enumerable.Repeat(TextOf(args, 0), times));
            }
            case "TEXTJOIN":
            {
                if (args.Count < 2) return string.Empty;
                var separator = TextOf(args, 0);
                var parts = GetValuesFromArgs(args.Skip(1).ToList())
                    .Select(FormatValue)
                    .Where(v => v.Length > 0);
                return string.Join(separator, parts);
            }
            case "VALUE":
            {
                var parsed = ConvertToDouble(TextOf(args, 0));
                return parsed ?? (object)"#VALUE!";
            }

            // ── Ngày tháng ───────────────────────────────────────────────
            case "TODAY": return DateTime.Today;
            case "NOW": return DateTime.Now;
            case "DATE":
            {
                try
                {
                    return new DateTime((int)Num(args, 0, 1), (int)Num(args, 1, 1), (int)Num(args, 2, 1));
                }
                catch
                {
                    return "#VALUE!";
                }
            }
            case "YEAR": return DateOf(args) is { } y ? (double)y.Year : "#VALUE!";
            case "MONTH": return DateOf(args) is { } m ? (double)m.Month : "#VALUE!";
            case "DAY": return DateOf(args) is { } d2 ? (double)d2.Day : "#VALUE!";
            case "WEEKDAY": return DateOf(args) is { } w ? (double)((int)w.DayOfWeek + 1) : "#VALUE!";

            // ── Tra cứu ──────────────────────────────────────────────────
            case "VLOOKUP": return Lookup(args, byColumn: true);
            case "HLOOKUP": return Lookup(args, byColumn: false);
            case "INDEX":
            {
                if (args.Count < 2) return "#VALUE!";
                var cells = GetRangeCells(args[0]);
                if (cells == null || cells.Count == 0) return "#REF!";

                int rowCount = cells.Max(c => c.row) - cells.Min(c => c.row) + 1;
                int colCount = cells.Max(c => c.col) - cells.Min(c => c.col) + 1;
                int wantedRow = (int)Num(args, 1, 1);
                int wantedCol = args.Count >= 3 ? (int)Num(args, 2, 1) : 1;

                if (wantedRow < 1 || wantedRow > rowCount || wantedCol < 1 || wantedCol > colCount)
                    return "#REF!";

                int baseRow = cells.Min(c => c.row);
                int baseCol = cells.Min(c => c.col);
                var found = cells.FirstOrDefault(c => c.row == baseRow + wantedRow - 1 && c.col == baseCol + wantedCol - 1);
                return found.value;
            }
            case "MATCH":
            {
                if (args.Count < 2) return "#N/A";
                var needle = FormatValue(EvaluateExpression(args[0]));
                var values = GetValuesFromArgs(new List<string> { args[1] });

                for (int i = 0; i < values.Count; i++)
                    if (string.Equals(FormatValue(values[i]), needle, StringComparison.OrdinalIgnoreCase))
                        return (double)(i + 1);

                return "#N/A";
            }

            default:
                throw new NotSupportedException($"Chưa hỗ trợ hàm '{funcName}'");
        }
    }

    private bool EvaluateCondition(string cond)
    {
        var match = Regex.Match(cond, @"^(.+?)\s*(>=|<=|<>|!=|>|<|=)\s*(.+)$");
        if (!match.Success)
        {
            var val = EvaluateExpression(cond);
            if (val is bool b) return b;
            if (val is double d) return d != 0;
            return val != null;
        }

        var left = EvaluateExpression(match.Groups[1].Value);
        var op = match.Groups[2].Value;
        var right = EvaluateExpression(match.Groups[3].Value);

        var leftNum = ConvertToDouble(left);
        var rightNum = ConvertToDouble(right);

        if (leftNum.HasValue && rightNum.HasValue)
        {
            return op switch
            {
                "=" or "==" => Math.Abs(leftNum.Value - rightNum.Value) < 0.0000001,
                "<>" or "!=" => Math.Abs(leftNum.Value - rightNum.Value) > 0.0000001,
                ">" => leftNum.Value > rightNum.Value,
                ">=" => leftNum.Value >= rightNum.Value,
                "<" => leftNum.Value < rightNum.Value,
                "<=" => leftNum.Value <= rightNum.Value,
                _ => false
            };
        }

        var leftStr = left?.ToString() ?? "";
        var rightStr = right?.ToString()?.Trim('"', '\'') ?? "";
        return op switch
        {
            "=" or "==" => string.Equals(leftStr, rightStr, StringComparison.OrdinalIgnoreCase),
            "<>" or "!=" => !string.Equals(leftStr, rightStr, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private double SumRange(List<string> args)
    {
        return GetNumericValues(args).Sum();
    }

    private (double sum, int count) GetSumAndCount(List<string> args)
    {
        var nums = GetNumericValues(args);
        return (nums.Sum(), nums.Count);
    }

    private List<double> GetNumericValues(List<string> args)
    {
        var list = new List<double>();
        foreach (var val in GetValuesFromArgs(args))
        {
            var d = ConvertToDouble(val);
            if (d.HasValue) list.Add(d.Value);
        }
        return list;
    }

    private List<object?> GetValuesFromArgs(List<string> args)
    {
        var results = new List<object?>();
        foreach (var arg in args)
        {
            var trimmed = arg.Trim();
            // Check if Range like A1:B10
            var rangeMatch = Regex.Match(trimmed, @"^([A-Za-z]+\d+):([A-Za-z]+\d+)$");
            if (rangeMatch.Success)
            {
                var start = new CellAddress(rangeMatch.Groups[1].Value);
                var end = new CellAddress(rangeMatch.Groups[2].Value);

                var minRow = Math.Min(start.Row, end.Row);
                var maxRow = Math.Max(start.Row, end.Row);
                var minCol = Math.Min(start.Column, end.Column);
                var maxCol = Math.Max(start.Column, end.Column);

                for (int r = minRow; r <= maxRow; r++)
                {
                    for (int c = minCol; c <= maxCol; c++)
                    {
                        var cellAddr = CellAddress.ToA1(r, c);
                        results.Add(GetCellCalculatedValue(cellAddr));
                    }
                }
            }
            else
            {
                // Single cell or literal expression
                results.Add(EvaluateExpression(trimmed));
            }
        }
        return results;
    }

    private object? GetCellCalculatedValue(string a1)
    {
        var cell = _sheet.GetCell(a1);
        if (cell.DataType == CellDataType.Formula && !string.IsNullOrWhiteSpace(cell.Formula))
        {
            return EvaluateFormula(a1, cell.Formula);
        }
        if (cell.EvaluatedValue != null) return cell.EvaluatedValue;
        return cell.RawValue;
    }

    private object? EvaluateArithmetic(string expr)
    {
        // Simple string literal
        if (expr.StartsWith('"') && expr.EndsWith('"') && expr.Length >= 2)
        {
            return expr.Substring(1, expr.Length - 2);
        }

        // Hằng luận lý viết trần: TRUE / FALSE
        if (expr.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) return true;
        if (expr.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) return false;

        // Single Cell Reference like A1
        if (Regex.IsMatch(expr, @"^[A-Za-z]+\d+$"))
        {
            return GetCellCalculatedValue(expr);
        }

        // Handle parentheses and arithmetic
        var tokens = TokenizeArithmetic(expr);
        if (tokens.Count == 1 && tokens[0] == expr)
        {
            // Check if stripped parentheses like (A1 + B1)
            if (expr.StartsWith('(') && expr.EndsWith(')'))
            {
                return EvaluateExpression(expr.Substring(1, expr.Length - 2));
            }

            if (double.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
                return num;
            return expr;
        }

        return EvaluateTokens(tokens);
    }

    private List<string> TokenizeArithmetic(string expr)
    {
        var tokens = new List<string>();
        int depth = 0;
        int lastPos = 0;

        for (int i = 0; i < expr.Length; i++)
        {
            char c = expr[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (depth == 0 && (c == '+' || c == '-' || c == '*' || c == '/'))
            {
                tokens.Add(expr.Substring(lastPos, i - lastPos).Trim());
                tokens.Add(c.ToString());
                lastPos = i + 1;
            }
        }
        tokens.Add(expr.Substring(lastPos).Trim());
        return tokens.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
    }

    private object? EvaluateTokens(List<string> tokens)
    {
        if (tokens.Count == 0) return 0.0;

        // Process * and / first
        var simplified = new List<string>();
        int idx = 0;
        while (idx < tokens.Count)
        {
            var token = tokens[idx];
            if (token == "*" || token == "/")
            {
                var leftVal = EvaluateExpression(simplified[^1]);
                if (leftVal is string leftStr && leftStr.StartsWith('#')) return leftStr;

                simplified.RemoveAt(simplified.Count - 1);
                var rightVal = EvaluateExpression(tokens[idx + 1]);
                if (rightVal is string rightStr && rightStr.StartsWith('#')) return rightStr;

                var prevNum = ConvertToDouble(leftVal) ?? 0;
                var nextNum = ConvertToDouble(rightVal) ?? 1;
                var res = token == "*" ? prevNum * nextNum : (nextNum == 0 ? double.PositiveInfinity : prevNum / nextNum);
                simplified.Add(res.ToString(CultureInfo.InvariantCulture));
                idx += 2;
            }
            else
            {
                simplified.Add(token);
                idx++;
            }
        }

        // Process + and -
        var firstVal = EvaluateExpression(simplified[0]);
        if (firstVal is string firstStr && firstStr.StartsWith('#')) return firstStr;

        double total = ConvertToDouble(firstVal) ?? 0;
        int sIdx = 1;
        while (sIdx < simplified.Count)
        {
            var op = simplified[sIdx];
            var nextVal = EvaluateExpression(simplified[sIdx + 1]);
            if (nextVal is string nextStr && nextStr.StartsWith('#')) return nextStr;

            var nextNum = ConvertToDouble(nextVal) ?? 0;
            if (op == "+") total += nextNum;
            else if (op == "-") total -= nextNum;
            sIdx += 2;
        }

        return total;
    }

    private double? ConvertToDouble(object? val)
    {
        if (val == null) return null;
        if (val is double d) return d;
        if (val is int i) return (double)i;
        if (val is long l) return (double)l;
        if (val is decimal dec) return (double)dec;
        if (val is float f) return (double)f;
        if (val is bool bl) return bl ? 1d : 0d;
        if (val is DateTime dt) return dt.ToOADate();
        if (double.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }


    // ─────────────────────────────────────────────────────────────────────
    //  HÀM PHỤ TRỢ CHO CÁC HÀM BẢNG TÍNH
    // ─────────────────────────────────────────────────────────────────────
    private double Num(List<string> args, int index, double fallback = 0)
    {
        if (index >= args.Count) return fallback;
        return ConvertToDouble(EvaluateExpression(args[index])) ?? fallback;
    }

    private int DigitCount(List<string> args) => Math.Clamp((int)Num(args, 1, 0), 0, 15);

    private string TextOf(List<string> args, int index)
        => index < args.Count ? TextOf(args[index]) : string.Empty;

    private string TextOf(string arg)
    {
        var trimmed = arg.Trim();
        if (trimmed.Length >= 2 && trimmed.StartsWith('"') && trimmed.EndsWith('"'))
            return trimmed[1..^1];

        return FormatValue(EvaluateExpression(trimmed));
    }

    /// <summary>Đưa giá trị về chuỗi hiển thị nhất quán (ngày theo định dạng Việt Nam).</summary>
    private static string FormatValue(object? value) => value switch
    {
        null => string.Empty,
        DateTime date => date.TimeOfDay == TimeSpan.Zero
            ? date.ToString("dd/MM/yyyy")
            : date.ToString("dd/MM/yyyy HH:mm"),
        double d => d.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "TRUE" : "FALSE",
        _ => value.ToString() ?? string.Empty
    };

    private DateTime? DateOf(List<string> args)
    {
        if (args.Count == 0) return null;

        var value = EvaluateExpression(args[0]);
        if (value is DateTime date) return date;

        var text = FormatValue(value);
        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed)) return parsed;
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) return parsed;

        // Số sê-ri kiểu Excel
        var serial = ConvertToDouble(value);
        if (serial.HasValue)
        {
            try { return DateTime.FromOADate(serial.Value); }
            catch { return null; }
        }

        return null;
    }

    /// <summary>Bung một vùng "A1:C10" thành danh sách ô kèm toạ độ. Trả null nếu không phải vùng.</summary>
    private List<(int row, int col, object? value)>? GetRangeCells(string arg)
    {
        var match = Regex.Match(arg.Trim(), @"^([A-Za-z]+\d+):([A-Za-z]+\d+)$");
        if (!match.Success) return null;

        var start = new CellAddress(match.Groups[1].Value);
        var end = new CellAddress(match.Groups[2].Value);

        int minRow = Math.Min(start.Row, end.Row), maxRow = Math.Max(start.Row, end.Row);
        int minCol = Math.Min(start.Column, end.Column), maxCol = Math.Max(start.Column, end.Column);

        var cells = new List<(int row, int col, object? value)>();
        for (int r = minRow; r <= maxRow; r++)
            for (int c = minCol; c <= maxCol; c++)
                cells.Add((r, c, GetCellCalculatedValue(CellAddress.ToA1(r, c))));

        return cells;
    }

    /// <summary>So khớp điều kiện kiểu Excel: "&gt;10", "&lt;=5", "&lt;&gt;0" hoặc so bằng chuỗi.</summary>
    private bool MatchesCriteria(object? value, string criteria)
    {
        criteria = criteria.Trim();
        if (criteria.Length == 0) return false;

        var match = Regex.Match(criteria, @"^(>=|<=|<>|!=|>|<|=)\s*(.*)$");
        if (match.Success)
        {
            var op = match.Groups[1].Value;
            var target = match.Groups[2].Value.Trim().Trim('"');

            var left = ConvertToDouble(value);
            var right = ConvertToDouble(target);

            if (left.HasValue && right.HasValue)
            {
                return op switch
                {
                    ">" => left.Value > right.Value,
                    ">=" => left.Value >= right.Value,
                    "<" => left.Value < right.Value,
                    "<=" => left.Value <= right.Value,
                    "<>" or "!=" => Math.Abs(left.Value - right.Value) > 0.0000001,
                    _ => Math.Abs(left.Value - right.Value) < 0.0000001
                };
            }

            bool equal = string.Equals(FormatValue(value), target, StringComparison.OrdinalIgnoreCase);
            return op is "<>" or "!=" ? !equal : equal;
        }

        return string.Equals(FormatValue(value), criteria.Trim('"'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Nền chung cho VLOOKUP (dò theo cột) và HLOOKUP (dò theo dòng).</summary>
    private object? Lookup(List<string> args, bool byColumn)
    {
        if (args.Count < 3) return "#VALUE!";

        var cells = GetRangeCells(args[1]);
        if (cells == null || cells.Count == 0) return "#REF!";

        var needle = FormatValue(EvaluateExpression(args[0]));
        int offset = (int)Num(args, 2, 1);
        if (offset < 1) return "#VALUE!";

        int baseRow = cells.Min(c => c.row);
        int baseCol = cells.Min(c => c.col);

        // Dò trên cột đầu tiên (VLOOKUP) hoặc dòng đầu tiên (HLOOKUP).
        var searchLine = byColumn
            ? cells.Where(c => c.col == baseCol).OrderBy(c => c.row)
            : cells.Where(c => c.row == baseRow).OrderBy(c => c.col);

        foreach (var cell in searchLine)
        {
            if (!string.Equals(FormatValue(cell.value), needle, StringComparison.OrdinalIgnoreCase)) continue;

            int targetRow = byColumn ? cell.row : baseRow + offset - 1;
            int targetCol = byColumn ? baseCol + offset - 1 : cell.col;

            var found = cells.FirstOrDefault(c => c.row == targetRow && c.col == targetCol);
            if (found == default && !cells.Any(c => c.row == targetRow && c.col == targetCol)) return "#REF!";
            return found.value;
        }

        return "#N/A";
    }

    private List<string> SplitFunctionArguments(string argsStr)
    {
        var list = new List<string>();
        int depth = 0;
        int lastIndex = 0;

        for (int i = 0; i < argsStr.Length; i++)
        {
            char c = argsStr[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if ((c == ',' || c == ';') && depth == 0)
            {
                list.Add(argsStr.Substring(lastIndex, i - lastIndex).Trim());
                lastIndex = i + 1;
            }
        }
        if (lastIndex < argsStr.Length)
        {
            list.Add(argsStr.Substring(lastIndex).Trim());
        }

        return list;
    }
}
