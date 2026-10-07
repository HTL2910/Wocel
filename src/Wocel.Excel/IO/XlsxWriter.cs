using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Wocel.Core.Models;

namespace Wocel.Excel.IO;

public static class XlsxWriter
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PKG = "http://schemas.openxmlformats.org/package/2006/content-types";

    public static void Write(SpreadsheetDocument doc, Stream outputStream)
    {
        using var archive = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: true);

        // Bảo đảm sổ tính luôn có ít nhất một trang rồi ghi toàn bộ.
        doc.GetOrCreateActiveSheet();
        var sheets = doc.Sheets;

        // Gom mọi định dạng ô lại trước, để ghi vào xl/styles.xml.
        var styles = new XlsxStyleTable();
        var styleIndex = new Dictionary<CellValue, int>();
        foreach (var sheet in sheets)
            foreach (var cell in sheet.Cells.Values)
                if (cell.Style is { IsDefault: false }) styleIndex[cell] = styles.Register(cell.Style);

        // 1. [Content_Types].xml
        var contentTypesEntry = archive.CreateEntry("[Content_Types].xml");
        using (var writer = new StreamWriter(contentTypesEntry.Open(), Encoding.UTF8))
        {
            var contentTypesXml = new XDocument(
                new XElement(PKG + "Types",
                    new XElement(PKG + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                    new XElement(PKG + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                    new XElement(PKG + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                    new XElement(PKG + "Override", new XAttribute("PartName", "/xl/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")),
                    sheets.Select((_, i) => new XElement(PKG + "Override",
                        new XAttribute("PartName", $"/xl/worksheets/sheet{i + 1}.xml"),
                        new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))
                )
            );
            contentTypesXml.Save(writer);
        }

        // 2. _rels/.rels
        var rootRelsEntry = archive.CreateEntry("_rels/.rels");
        using (var writer = new StreamWriter(rootRelsEntry.Open(), Encoding.UTF8))
        {
            var rootRelsXml = new XDocument(
                new XElement(R + "Relationships",
                    new XElement(R + "Relationship",
                        new XAttribute("Id", "rId1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                        new XAttribute("Target", "xl/workbook.xml"))
                )
            );
            rootRelsXml.Save(writer);
        }

        // 3. xl/_rels/workbook.xml.rels
        var wbRelsEntry = archive.CreateEntry("xl/_rels/workbook.xml.rels");
        using (var writer = new StreamWriter(wbRelsEntry.Open(), Encoding.UTF8))
        {
            var wbRelsXml = new XDocument(
                new XElement(R + "Relationships",
                    sheets.Select((_, i) => new XElement(R + "Relationship",
                        new XAttribute("Id", $"rId{i + 1}"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                        new XAttribute("Target", $"worksheets/sheet{i + 1}.xml"))),
                    new XElement(R + "Relationship",
                        new XAttribute("Id", "rIdStyles"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                        new XAttribute("Target", "styles.xml"))
                )
            );
            wbRelsXml.Save(writer);
        }

        // 4. xl/workbook.xml
        var wbEntry = archive.CreateEntry("xl/workbook.xml");
        using (var writer = new StreamWriter(wbEntry.Open(), Encoding.UTF8))
        {
            var wbXml = new XDocument(
                new XElement(S + "workbook",
                    new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                    new XElement(S + "sheets",
                        sheets.Select((sheet, i) => new XElement(S + "sheet",
                            new XAttribute("name", sheet.Name),
                            new XAttribute("sheetId", (i + 1).ToString()),
                            new XAttribute(R + "id", $"rId{i + 1}")))
                    )
                )
            );
            wbXml.Save(writer);
        }

        // 4b. xl/styles.xml — phông, cỡ chữ, màu nền, định dạng số
        var stylesEntry = archive.CreateEntry("xl/styles.xml");
        using (var writer = new StreamWriter(stylesEntry.Open(), Encoding.UTF8))
        {
            styles.BuildDocument().Save(writer);
        }

        // 5. xl/worksheets/sheetN.xml — mỗi trang tính một tệp
        for (int index = 0; index < sheets.Count; index++)
        {
            var worksheet = sheets[index];
            var sheetDataElem = new XElement(S + "sheetData");

            var cellsByRow = worksheet.Cells
                .Select(kvp => new { Addr = new CellAddress(kvp.Key), Cell = kvp.Value })
                .GroupBy(x => x.Addr.Row)
                .OrderBy(g => g.Key);

            foreach (var rowGroup in cellsByRow)
            {
                var rowElem = new XElement(S + "row", new XAttribute("r", rowGroup.Key));

                foreach (var item in rowGroup.OrderBy(i => i.Addr.Column))
                {
                    var cElem = new XElement(S + "c", new XAttribute("r", item.Addr.A1Notation));

                    if (styleIndex.TryGetValue(item.Cell, out int style) && style > 0)
                        cElem.Add(new XAttribute("s", style));

                    if (item.Cell.DataType == CellDataType.Formula && !string.IsNullOrEmpty(item.Cell.Formula))
                    {
                        cElem.Add(new XElement(S + "f", item.Cell.Formula.TrimStart('=')));
                        if (item.Cell.EvaluatedValue != null)
                            cElem.Add(new XElement(S + "v", FormatForXml(item.Cell.EvaluatedValue)));
                    }
                    else if (item.Cell.DataType == CellDataType.Number && item.Cell.RawValue is double or int)
                    {
                        cElem.Add(new XElement(S + "v", FormatForXml(item.Cell.RawValue)));
                    }
                    else
                    {
                        var text = item.Cell.GetDisplayString();
                        if (text.Length == 0 && item.Cell.Style == null) continue; // ô rỗng không cần ghi

                        cElem.Add(new XAttribute("t", "inlineStr"),
                            new XElement(S + "is", new XElement(S + "t", text)));
                    }

                    rowElem.Add(cElem);
                }

                if (rowElem.HasElements) sheetDataElem.Add(rowElem);
            }

            var worksheetElem = new XElement(S + "worksheet", sheetDataElem);

            // Vùng gộp ô
            var merged = worksheet.MergedRanges.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
            if (merged.Count > 0)
            {
                worksheetElem.Add(new XElement(S + "mergeCells",
                    new XAttribute("count", merged.Count),
                    merged.Select(r => new XElement(S + "mergeCell", new XAttribute("ref", r)))));
            }

            var sheetEntry = archive.CreateEntry($"xl/worksheets/sheet{index + 1}.xml");
            using var sheetWriter = new StreamWriter(sheetEntry.Open(), Encoding.UTF8);
            new XDocument(worksheetElem).Save(sheetWriter);
        }
    }

    /// <summary>Số phải ghi theo chuẩn bất biến, nếu không Excel đọc sai dấu thập phân.</summary>
    private static string FormatForXml(object value) => value switch
    {
        double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
        int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}
