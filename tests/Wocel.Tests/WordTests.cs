using Wocel.Core.Models;
using Wocel.Word.IO;
using Wocel.Word.Sessions;
using Xunit;

namespace Wocel.Tests;

public class WordTests
{
    [Fact]
    public async Task Docx_WriteAndRead_Roundtrip_Succeeds()
    {
        var originalDoc = new DocumentDocument { Title = "Test Document" };
        
        var h1 = new DocBlock { Type = BlockType.Heading1 };
        h1.Inlines.Add(new TextRun { Text = "Báo cáo tài chính quý 1" });
        originalDoc.Blocks.Add(h1);

        var p = new DocBlock { Type = BlockType.Paragraph };
        p.Inlines.Add(new TextRun { Text = "Doanh thu tăng trưởng ", IsBold = true });
        p.Inlines.Add(new TextRun { Text = "vượt bậc trong kỳ.", IsItalic = true });
        originalDoc.Blocks.Add(p);

        var table = new DocBlock
        {
            Type = BlockType.Table,
            TableData = new List<List<string>>
            {
                new() { "Khoản mục", "Số tiền (VNĐ)" },
                new() { "Doanh thu", "1,500,000,000" },
                new() { "Chi phí", "800,000,000" }
            }
        };
        originalDoc.Blocks.Add(table);

        using var ms = new MemoryStream();
        DocxWriter.Write(originalDoc, ms);
        ms.Position = 0;

        var loadedDoc = DocxReader.Read(ms);

        Assert.Equal(3, loadedDoc.Blocks.Count);
        Assert.Equal(BlockType.Heading1, loadedDoc.Blocks[0].Type);
        Assert.Equal("Báo cáo tài chính quý 1", loadedDoc.Blocks[0].Inlines[0].Text);

        Assert.Equal(BlockType.Paragraph, loadedDoc.Blocks[1].Type);
        Assert.True(loadedDoc.Blocks[1].Inlines[0].IsBold);
        Assert.True(loadedDoc.Blocks[1].Inlines[1].IsItalic);

        Assert.Equal(BlockType.Table, loadedDoc.Blocks[2].Type);
        Assert.Equal(3, loadedDoc.Blocks[2].TableData!.Count);
        Assert.Equal("Doanh thu", loadedDoc.Blocks[2].TableData[1][0]);
    }

    [Fact]
    public void Rtf_WriteAndRead_Roundtrip_Succeeds()
    {
        var doc = new DocumentDocument();
        var block = new DocBlock { Type = BlockType.Paragraph };
        block.Inlines.Add(new TextRun { Text = "Hello Wocel RTF", IsBold = true });
        doc.Blocks.Add(block);

        using var ms = new MemoryStream();
        RtfHandler.WriteRtf(doc, ms);
        ms.Position = 0;

        var loaded = RtfHandler.ReadRtf(ms);
        Assert.Single(loaded.Blocks);
        Assert.Contains("Hello Wocel RTF", loaded.Blocks[0].Inlines[0].Text);
    }

    [Fact]
    public void Pdf_Export_GeneratesValidPdfHeader()
    {
        var doc = new DocumentDocument();
        var block = new DocBlock { Type = BlockType.Paragraph };
        block.Inlines.Add(new TextRun { Text = "Wocel PDF Content" });
        doc.Blocks.Add(block);

        using var ms = new MemoryStream();
        PdfExporter.ExportToPdf(doc, ms);
        ms.Position = 0;

        using var reader = new StreamReader(ms);
        var pdfContent = reader.ReadToEnd();
        Assert.StartsWith("%PDF-1.4", pdfContent);
        Assert.EndsWith("%%EOF\n", pdfContent);
    }
}
