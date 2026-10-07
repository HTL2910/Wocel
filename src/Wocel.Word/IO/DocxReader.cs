using System.IO.Compression;
using System.Xml.Linq;
using Wocel.Core.Models;

namespace Wocel.Word.IO;

public static class DocxReader
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static DocumentDocument Read(Stream stream)
    {
        var doc = new DocumentDocument();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        var docEntry = archive.GetEntry("word/document.xml");
        if (docEntry == null)
            throw new InvalidDataException("Invalid .docx file: missing word/document.xml");

        using var entryStream = docEntry.Open();
        var xDoc = XDocument.Load(entryStream);
        var body = xDoc.Root?.Element(W + "body");
        if (body == null) return doc;

        foreach (var element in body.Elements())
        {
            if (element.Name == W + "p") // Paragraph
            {
                var block = new DocBlock { Type = BlockType.Paragraph };
                
                // Check paragraph style (Headings)
                var pStyle = element.Element(W + "pPr")?.Element(W + "pStyle")?.Attribute(W + "val")?.Value;
                if (pStyle != null)
                {
                    if (pStyle.Contains("Heading1", StringComparison.OrdinalIgnoreCase)) block.Type = BlockType.Heading1;
                    else if (pStyle.Contains("Heading2", StringComparison.OrdinalIgnoreCase)) block.Type = BlockType.Heading2;
                    else if (pStyle.Contains("Heading3", StringComparison.OrdinalIgnoreCase)) block.Type = BlockType.Heading3;
                }

                foreach (var runElem in element.Elements(W + "r"))
                {
                    var run = new TextRun();
                    var rPr = runElem.Element(W + "rPr");
                    if (rPr != null)
                    {
                        run.IsBold = rPr.Element(W + "b") != null;
                        run.IsItalic = rPr.Element(W + "i") != null;
                        run.IsUnderline = rPr.Element(W + "u") != null;

                        var fonts = rPr.Element(W + "rFonts")?.Attribute(W + "ascii")?.Value;
                        if (!string.IsNullOrWhiteSpace(fonts)) run.FontFamily = fonts;

                        // w:sz tính theo nửa point.
                        var halfPoints = rPr.Element(W + "sz")?.Attribute(W + "val")?.Value;
                        if (int.TryParse(halfPoints, out int halves) && halves > 0) run.FontSize = halves / 2;

                        var runColor = rPr.Element(W + "color")?.Attribute(W + "val")?.Value;
                        if (!string.IsNullOrWhiteSpace(runColor) && runColor != "auto") run.FontColor = "#" + runColor;
                    }

                    var textElem = runElem.Element(W + "t");
                    if (textElem != null)
                    {
                        run.Text = textElem.Value;
                    }

                    if (!string.IsNullOrEmpty(run.Text))
                    {
                        block.Inlines.Add(run);
                    }
                }

                doc.Blocks.Add(block);
            }
            else if (element.Name == W + "tbl") // Table
            {
                var tableBlock = new DocBlock
                {
                    Type = BlockType.Table,
                    TableData = new List<List<string>>()
                };

                foreach (var rowElem in element.Elements(W + "tr"))
                {
                    var rowData = new List<string>();
                    foreach (var cellElem in rowElem.Elements(W + "tc"))
                    {
                        var cellText = string.Join(" ", cellElem.Elements(W + "p")
                            .SelectMany(p => p.Elements(W + "r"))
                            .Select(r => r.Element(W + "t")?.Value ?? "")
                            .Where(s => !string.IsNullOrEmpty(s)));
                        rowData.Add(cellText);
                    }
                    tableBlock.TableData.Add(rowData);
                }

                doc.Blocks.Add(tableBlock);
            }
        }

        return doc;
    }
}
