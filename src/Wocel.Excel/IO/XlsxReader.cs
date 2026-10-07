using System.IO.Compression;
using System.Xml.Linq;
using Wocel.Core.Models;

namespace Wocel.Excel.IO;

public static class XlsxReader
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static SpreadsheetDocument Read(Stream stream)
    {
        var doc = new SpreadsheetDocument();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        // 1. Read Shared Strings (if exists)
        var sharedStrings = new List<string>();
        var sstEntry = archive.GetEntry("xl/sharedStrings.xml");
        if (sstEntry != null)
        {
            using var sstStream = sstEntry.Open();
            var xSst = XDocument.Load(sstStream);
            foreach (var si in xSst.Descendants(S + "si"))
            {
                var text = si.Element(S + "t")?.Value ?? string.Join("", si.Descendants(S + "t").Select(t => t.Value));
                sharedStrings.Add(text);
            }
        }

        // 1b. Bảng định dạng (nếu tệp có)
        var cellStyles = new List<CellStyle?>();
        var stylesEntry = archive.GetEntry("xl/styles.xml");
        if (stylesEntry != null)
        {
            try
            {
                using var stylesStream = stylesEntry.Open();
                cellStyles = XlsxStyleTable.Parse(XDocument.Load(stylesStream));
            }
            catch
            {
                // Bảng định dạng hỏng thì vẫn đọc được dữ liệu.
            }
        }

        // 2. Tên các trang tính nằm ở workbook.xml, theo đúng thứ tự tệp sheetN.xml
        var sheetNames = new List<string>();
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry != null)
        {
            using var workbookStream = workbookEntry.Open();
            var xWorkbook = XDocument.Load(workbookStream);
            foreach (var sheetElem in xWorkbook.Descendants(S + "sheet"))
                sheetNames.Add(sheetElem.Attribute("name")?.Value ?? $"Sheet{sheetNames.Count + 1}");
        }

        // 3. Đọc lần lượt từng trang tính
        for (int index = 1; ; index++)
        {
            var sheetEntry = archive.GetEntry($"xl/worksheets/sheet{index}.xml");
            if (sheetEntry == null) break;

            var worksheet = new SpreadsheetWorksheet
            {
                Name = index - 1 < sheetNames.Count ? sheetNames[index - 1] : $"Sheet{index}"
            };

            using (var sheetStream = sheetEntry.Open())
            {
                var xSheet = XDocument.Load(sheetStream);

                var sheetData = xSheet.Descendants(S + "sheetData").FirstOrDefault();
                if (sheetData != null)
                {
                    foreach (var rowElem in sheetData.Elements(S + "row"))
                        foreach (var cElem in rowElem.Elements(S + "c"))
                            ReadCell(cElem, worksheet, sharedStrings, cellStyles);
                }

                foreach (var mergeElem in xSheet.Descendants(S + "mergeCell"))
                {
                    var reference = mergeElem.Attribute("ref")?.Value;
                    if (!string.IsNullOrWhiteSpace(reference)) worksheet.MergedRanges.Add(reference);
                }
            }

            doc.Sheets.Add(worksheet);
        }

        if (doc.Sheets.Count == 0)
            throw new InvalidDataException("Tệp .xlsx không hợp lệ: không tìm thấy trang tính nào.");

        return doc;
    }

    private static void ReadCell(XElement cElem, SpreadsheetWorksheet worksheet, List<string> sharedStrings,
        List<CellStyle?> cellStyles)
    {
        var cellRef = cElem.Attribute("r")?.Value;
        if (string.IsNullOrEmpty(cellRef)) return;

        var cellType = cElem.Attribute("t")?.Value;
        var formula = cElem.Element(S + "f")?.Value;
        var rawVal = cElem.Element(S + "v")?.Value;
        var inlineStr = cElem.Element(S + "is")?.Element(S + "t")?.Value;

        var cell = new CellValue();
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var anyNumber = System.Globalization.NumberStyles.Any;

        if (!string.IsNullOrEmpty(formula))
        {
            cell.DataType = CellDataType.Formula;
            cell.Formula = "=" + formula;
            cell.RawValue = cell.Formula;
            if (double.TryParse(rawVal, anyNumber, invariant, out var formulaValue))
                cell.EvaluatedValue = formulaValue;
        }
        else if (cellType == "inlineStr" || !string.IsNullOrEmpty(inlineStr))
        {
            cell.DataType = CellDataType.String;
            cell.RawValue = inlineStr ?? string.Empty;
            cell.EvaluatedValue = inlineStr ?? string.Empty;
        }
        else if (cellType == "s" && int.TryParse(rawVal, out var sharedIndex) && sharedIndex < sharedStrings.Count)
        {
            cell.DataType = CellDataType.String;
            cell.RawValue = sharedStrings[sharedIndex];
            cell.EvaluatedValue = sharedStrings[sharedIndex];
        }
        else if (double.TryParse(rawVal, anyNumber, invariant, out var number))
        {
            cell.DataType = CellDataType.Number;
            cell.RawValue = number;
            cell.EvaluatedValue = number;
        }
        else
        {
            cell.DataType = CellDataType.String;
            cell.RawValue = rawVal ?? string.Empty;
            cell.EvaluatedValue = rawVal ?? string.Empty;
        }

        if (int.TryParse(cElem.Attribute("s")?.Value, out int styleIndex)
            && styleIndex > 0 && styleIndex < cellStyles.Count)
            cell.Style = cellStyles[styleIndex]?.Clone();

        worksheet.SetCell(cellRef, cell);

    }
}
