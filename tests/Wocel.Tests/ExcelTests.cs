using Wocel.Core.Models;
using Wocel.Excel.Engine;
using Wocel.Excel.IO;
using Xunit;

namespace Wocel.Tests;

public class ExcelTests
{
    [Theory]
    [InlineData(1, 1, "A1")]
    [InlineData(10, 2, "B10")]
    [InlineData(100, 26, "Z100")]
    [InlineData(1, 27, "AA1")]
    public void CellAddress_Conversion_IsAccurate(int row, int col, string expectedA1)
    {
        var addr = new CellAddress(row, col);
        Assert.Equal(expectedA1, addr.A1Notation);

        var (parsedRow, parsedCol) = CellAddress.FromA1(expectedA1);
        Assert.Equal(row, parsedRow);
        Assert.Equal(col, parsedCol);
    }

    [Fact]
    public void FormulaEngine_Calculates_Sum_Average_If()
    {
        var sheet = new SpreadsheetWorksheet();
        sheet.SetValue("A1", 10);
        sheet.SetValue("A2", 20);
        sheet.SetValue("A3", 30);
        sheet.SetValue("A4", "=SUM(A1:A3)");
        sheet.SetValue("A5", "=AVERAGE(A1:A3)");
        sheet.SetValue("B1", "=IF(A4 > 50, \"High\", \"Low\")");
        sheet.SetValue("B2", "=(A1 + A2) * 2");

        var engine = new FormulaEngine(sheet);
        engine.RecalculateAll();

        Assert.Equal(60.0, sheet.GetCell("A4").EvaluatedValue);
        Assert.Equal(20.0, sheet.GetCell("A5").EvaluatedValue);
        Assert.Equal("High", sheet.GetCell("B1").EvaluatedValue);
        Assert.Equal(60.0, sheet.GetCell("B2").EvaluatedValue);
    }

    [Fact]
    public void FormulaEngine_Detects_Circular_Dependency()
    {
        var sheet = new SpreadsheetWorksheet();
        sheet.SetValue("A1", "=B1 + 1");
        sheet.SetValue("B1", "=A1 + 1");

        var engine = new FormulaEngine(sheet);
        engine.RecalculateAll();

        Assert.Equal("#CIRCULAR!", sheet.GetCell("A1").EvaluatedValue);
    }

    [Fact]
    public void Xlsx_WriteAndRead_Roundtrip_Succeeds()
    {
        var originalDoc = new SpreadsheetDocument();
        var ws = originalDoc.GetOrCreateActiveSheet();
        ws.SetValue("A1", "Sản phẩm");
        ws.SetValue("B1", "Đơn giá");
        ws.SetValue("A2", "Laptop Dell XPS");
        ws.SetValue("B2", 25000000);
        ws.SetValue("A3", "Chuột Logitech");
        ws.SetValue("B3", 500000);
        ws.SetValue("B4", "=SUM(B2:B3)");

        using var ms = new MemoryStream();
        XlsxWriter.Write(originalDoc, ms);
        ms.Position = 0;

        var loadedDoc = XlsxReader.Read(ms);
        var loadedWs = loadedDoc.GetOrCreateActiveSheet();

        Assert.Equal("Laptop Dell XPS", loadedWs.GetCell("A2").GetDisplayString());
        Assert.Equal(25000000.0, (double)loadedWs.GetCell("B2").RawValue!);
        Assert.Equal("=SUM(B2:B3)", loadedWs.GetCell("B4").Formula);
    }

    [Fact]
    public void Csv_WriteAndRead_Roundtrip_Succeeds()
    {
        var originalDoc = new SpreadsheetDocument();
        var ws = originalDoc.GetOrCreateActiveSheet();
        ws.SetValue("A1", "Item, with comma");
        ws.SetValue("B1", 100);

        using var ms = new MemoryStream();
        CsvHandler.WriteCsv(originalDoc, ms);
        ms.Position = 0;

        var loaded = CsvHandler.ReadCsv(ms);
        var loadedWs = loaded.GetOrCreateActiveSheet();

        Assert.Equal("Item, with comma", loadedWs.GetCell("A1").GetDisplayString());
        Assert.Equal(100.0, (double)loadedWs.GetCell("B1").RawValue!);
    }
}
