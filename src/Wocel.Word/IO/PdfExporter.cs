using System.Text;
using Wocel.Core.Models;

namespace Wocel.Word.IO;

public static class PdfExporter
{
    /// <summary>
    /// Generates a valid standard PDF 1.4 stream containing document text and formatting.
    /// </summary>
    public static void ExportToPdf(DocumentDocument doc, Stream outputStream)
    {
        // PDF xref offsets below assume 1-byte line endings; never use Environment.NewLine ("\r\n" on Windows).
        using var writer = new StreamWriter(outputStream, Encoding.ASCII, leaveOpen: true) { NewLine = "\n" };
        var objects = new List<string>();

        // Object 1: Catalog
        objects.Add("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj");

        // We will build content stream (stream object)
        var contentBuilder = new StringBuilder();
        contentBuilder.AppendLine("BT");
        contentBuilder.AppendLine("/F1 12 Tf");
        contentBuilder.AppendLine("50 750 Td");
        contentBuilder.AppendLine("16 TL"); // Line spacing

        foreach (var block in doc.Blocks)
        {
            if (block.Type == BlockType.Heading1)
            {
                contentBuilder.AppendLine("/F1 20 Tf");
            }
            else if (block.Type == BlockType.Heading2)
            {
                contentBuilder.AppendLine("/F1 16 Tf");
            }
            else
            {
                contentBuilder.AppendLine("/F1 12 Tf");
            }

            var lineText = string.Join("", block.Inlines.Select(i => i.Text));
            var sanitizedText = lineText.Replace("(", "\\(").Replace(")", "\\)");
            contentBuilder.AppendLine($"({sanitizedText}) '");
        }

        contentBuilder.AppendLine("ET");
        var contentBytes = Encoding.ASCII.GetBytes(contentBuilder.ToString());

        // Object 4: Content Stream
        var contentObj = $"4 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n{contentBuilder}endstream\nendobj";

        // Object 2: Pages
        objects.Add("2 0 obj\n<< /Type /Pages /Kids [ 3 0 R ] /Count 1 >>\nendobj");

        // Object 3: Page
        objects.Add("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [ 0 0 595 842 ] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj");

        objects.Add(contentObj);

        // Object 5: Font
        objects.Add("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj");

        // Header
        writer.WriteLine("%PDF-1.4");
        var offsets = new List<long>();

        long currentOffset = 9; // %PDF-1.4 + newline
        foreach (var obj in objects)
        {
            offsets.Add(currentOffset);
            writer.WriteLine(obj);
            currentOffset += Encoding.ASCII.GetByteCount(obj) + 1; // +1 for newline
        }

        var xrefOffset = currentOffset;
        writer.WriteLine("xref");
        writer.WriteLine($"0 {objects.Count + 1}");
        writer.WriteLine("0000000000 65535 f ");
        foreach (var offset in offsets)
        {
            writer.WriteLine($"{offset:D10} 00000 n ");
        }

        writer.WriteLine("trailer");
        writer.WriteLine($"<< /Size {objects.Count + 1} /Root 1 0 R >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefOffset);
        writer.WriteLine("%%EOF");
    }
}
