using Wocel.Core.Models;
using Wocel.Excel.Engine;
using Xunit;

namespace Wocel.Tests;

public class FormulaFunctionTests
{
    private static SpreadsheetWorksheet SheetWithSampleData()
    {
        var sheet = new SpreadsheetWorksheet { Name = "Sheet1" };

        // Bảng hàng hoá nhỏ để thử SUMIF/COUNTIF/VLOOKUP
        sheet.SetValue("A1", "SP001"); sheet.SetValue("B1", "Bàn phím"); sheet.SetValue("C1", 12.0);
        sheet.SetValue("A2", "SP002"); sheet.SetValue("B2", "Chuột");    sheet.SetValue("C2", 34.0);
        sheet.SetValue("A3", "SP003"); sheet.SetValue("B3", "Màn hình"); sheet.SetValue("C3", 7.0);
        sheet.SetValue("A4", "SP004"); sheet.SetValue("B4", "Loa");      sheet.SetValue("C4", 50.0);

        return sheet;
    }

    private static object? Eval(SpreadsheetWorksheet sheet, string formula)
    {
        sheet.SetValue("Z100", formula);
        return new FormulaEngine(sheet).EvaluateFormula("Z100", formula);
    }

    private static string Text(SpreadsheetWorksheet sheet, string formula) =>
        Eval(sheet, formula)?.ToString() ?? string.Empty;

    private static double Number(SpreadsheetWorksheet sheet, string formula) =>
        Convert.ToDouble(Eval(sheet, formula));

    // ── Danh mục và engine phải khớp nhau ────────────────────────────────
    [Fact]
    public void EveryCatalogFunction_IsActuallySupported()
    {
        var sheet = SheetWithSampleData();
        var engine = new FormulaEngine(sheet);
        var unsupported = new List<string>();

        foreach (var function in FormulaFunctionCatalog.All)
        {
            // Gọi với đủ tham số giả để chắc chắn hàm được nhận diện.
            var call = $"={function.Name}(C1:C4,1,1)";
            try
            {
                engine.EvaluateFormula("Z100", call);
            }
            catch (NotSupportedException)
            {
                unsupported.Add(function.Name);
            }
            catch
            {
                // Sai kiểu tham số thì không sao — miễn là hàm có tồn tại.
            }
        }

        Assert.True(unsupported.Count == 0,
            "Danh mục gợi ý có hàm mà engine chưa chạy được: " + string.Join(", ", unsupported));
    }

    [Fact]
    public void Catalog_HasNoDuplicateNames()
    {
        var duplicates = FormulaFunctionCatalog.All
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    // ── Gợi ý ────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("SU", "SUM")]
    [InlineData("su", "SUM")]
    [InlineData("CO", "COUNT")]
    [InlineData("VL", "VLOOKUP")]
    [InlineData("AVER", "AVERAGE")]
    public void Search_PutsBestMatchFirst(string prefix, string expected)
    {
        var results = FormulaFunctionCatalog.Search(prefix);
        Assert.NotEmpty(results);
        Assert.Equal(expected, results[0].Name);
    }

    [Fact]
    public void Search_MatchesVietnameseDescription()
    {
        var results = FormulaFunctionCatalog.Search("trung bình");
        Assert.Contains(results, f => f.Name == "AVERAGE");
    }

    [Theory]
    [InlineData("=SU", 3, "SU")]
    [InlineData("=SUM(A1,AV", 10, "AV")]
    [InlineData("=SUM(A1:A5)", 11, null)]   // sau dấu ) thì không gợi ý nữa
    [InlineData("123", 3, null)]          // không phải công thức
    [InlineData("=\"SUM", 5, null)]       // đang ở trong chuỗi
    [InlineData("=A1+CO", 6, "CO")]
    public void GetPrefixAtCaret_ReadsWhatUserIsTyping(string text, int caret, string? expected)
    {
        Assert.Equal(expected, FormulaFunctionCatalog.GetPrefixAtCaret(text, caret));
    }

    // ── Toán học & thống kê ──────────────────────────────────────────────
    [Fact]
    public void MathFunctions_Compute()
    {
        var sheet = SheetWithSampleData();

        Assert.Equal(103, Number(sheet, "=SUM(C1:C4)"));
        Assert.Equal(25.75, Number(sheet, "=AVERAGE(C1:C4)"));
        Assert.Equal(7, Number(sheet, "=MIN(C1:C4)"));
        Assert.Equal(50, Number(sheet, "=MAX(C1:C4)"));
        Assert.Equal(23, Number(sheet, "=MEDIAN(C1:C4)"));
        Assert.Equal(5, Number(sheet, "=ABS(0-5)"));
        Assert.Equal(3.14, Number(sheet, "=ROUND(3.14159,2)"));
        Assert.Equal(4, Number(sheet, "=ROUNDUP(3.1,0)"));
        Assert.Equal(3, Number(sheet, "=ROUNDDOWN(3.9,0)"));
        Assert.Equal(1, Number(sheet, "=MOD(10,3)"));
        Assert.Equal(8, Number(sheet, "=POWER(2,3)"));
        Assert.Equal(4, Number(sheet, "=SQRT(16)"));
        Assert.Equal(2, Number(sheet, "=INT(2.9)"));
    }

    [Fact]
    public void ConditionalFunctions_Compute()
    {
        var sheet = SheetWithSampleData();

        Assert.Equal(2, Number(sheet, "=COUNTIF(C1:C4,\">20\")"));
        Assert.Equal(84, Number(sheet, "=SUMIF(C1:C4,\">20\")"));
        Assert.Equal(4, Number(sheet, "=COUNTA(A1:A4)"));
        Assert.Equal(4, Number(sheet, "=COUNT(C1:C4)"));
    }

    [Fact]
    public void LogicFunctions_Compute()
    {
        var sheet = SheetWithSampleData();

        Assert.Equal("Đạt", Text(sheet, "=IF(C4>20,\"Đạt\",\"Chưa đạt\")"));
        Assert.Equal("True", Text(sheet, "=AND(C4>20,C1>5)"));
        Assert.Equal("False", Text(sheet, "=AND(C4>20,C3>20)"));
        Assert.Equal("True", Text(sheet, "=OR(C4>20,C3>20)"));
        Assert.Equal("True", Text(sheet, "=NOT(C3>20)"));
        Assert.Equal("không có", Text(sheet, "=IFERROR(NOSUCHFUNC(1),\"không có\")"));
    }

    // ── Văn bản (phải giữ nguyên dấu tiếng Việt) ─────────────────────────
    [Fact]
    public void TextFunctions_KeepVietnameseCharacters()
    {
        var sheet = SheetWithSampleData();

        Assert.Equal("Màn hình", Text(sheet, "=B3"));
        Assert.Equal(8, Number(sheet, "=LEN(B3)"));
        Assert.Equal("MÀN HÌNH", Text(sheet, "=UPPER(B3)"));
        Assert.Equal("màn hình", Text(sheet, "=LOWER(B3)"));
        Assert.Equal("Màn", Text(sheet, "=LEFT(B3,3)"));
        Assert.Equal("hình", Text(sheet, "=RIGHT(B3,4)"));
        Assert.Equal("àn", Text(sheet, "=MID(B3,2,2)"));
        Assert.Equal("Bàn phím-Chuột", Text(sheet, "=CONCAT(B1,\"-\",B2)"));
        Assert.Equal("Bàn phím | Chuột", Text(sheet, "=TEXTJOIN(\" | \",B1:B2)"));
        Assert.Equal("Màn ảnh", Text(sheet, "=SUBSTITUTE(B3,\"hình\",\"ảnh\")"));
        Assert.Equal("xin chao", Text(sheet, "=TRIM(\"  xin   chao  \")"));
    }

    // ── Tra cứu ──────────────────────────────────────────────────────────
    [Fact]
    public void LookupFunctions_Compute()
    {
        var sheet = SheetWithSampleData();

        Assert.Equal("Chuột", Text(sheet, "=VLOOKUP(\"SP002\",A1:C4,2)"));
        Assert.Equal(50, Number(sheet, "=VLOOKUP(\"SP004\",A1:C4,3)"));
        Assert.Equal("#N/A", Text(sheet, "=VLOOKUP(\"KHONGCO\",A1:C4,2)"));
        Assert.Equal("Màn hình", Text(sheet, "=INDEX(A1:C4,3,2)"));
        Assert.Equal(2, Number(sheet, "=MATCH(\"SP002\",A1:A4)"));
    }

    // ── Ngày tháng ───────────────────────────────────────────────────────
    [Fact]
    public void DateFunctions_Compute()
    {
        var sheet = SheetWithSampleData();

        Assert.Equal(2026, Number(sheet, "=YEAR(DATE(2026,8,28))"));
        Assert.Equal(8, Number(sheet, "=MONTH(DATE(2026,8,28))"));
        Assert.Equal(28, Number(sheet, "=DAY(DATE(2026,8,28))"));
        Assert.Equal(DateTime.Today.Year, Number(sheet, "=YEAR(TODAY())"));
    }

    [Fact]
    public void DateValue_DisplaysInVietnameseFormat()
    {
        var sheet = SheetWithSampleData();
        sheet.SetValue("D1", "=DATE(2026,8,28)");
        new FormulaEngine(sheet).RecalculateAll();

        Assert.Equal("28/08/2026", sheet.GetCell("D1").GetDisplayString());
    }

    // ── Dấu ngăn cách kiểu Việt Nam ──────────────────────────────────────
    [Fact]
    public void SemicolonSeparator_WorksLikeComma()
    {
        var sheet = SheetWithSampleData();
        Assert.Equal(103, Number(sheet, "=SUM(C1:C4;0)"));
        Assert.Equal("Đạt", Text(sheet, "=IF(C4>20;\"Đạt\";\"Chưa đạt\")"));
    }
}
