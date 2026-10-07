using System.Text.Json.Serialization;

namespace Wocel.Core.Models;

public enum BlockType
{
    Paragraph,
    Heading1,
    Heading2,
    Heading3,
    BulletList,
    NumberedList,
    Table,
    EmbeddedSheet,
    PageBreak
}

public class TextRun
{
    public string Text { get; set; } = string.Empty;
    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }
    public bool IsUnderline { get; set; }
    public string? FontColor { get; set; }
    public string? FontFamily { get; set; }
    public int? FontSize { get; set; }
}

public class DocBlock
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public BlockType Type { get; set; } = BlockType.Paragraph;
    public List<TextRun> Inlines { get; set; } = new();
    
    // For Tables
    public List<List<string>>? TableData { get; set; }

    // For Embedded Excel Live Blocks
    public string? EmbeddedSheetId { get; set; }
    public string? EmbeddedRange { get; set; }
    public bool IsLiveSynced { get; set; }
}

public class DocumentDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Untitled Document";
    public List<DocBlock> Blocks { get; set; } = new();
    public DocumentPageSettings PageSettings { get; set; } = new();

    public string ToPlainText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var block in Blocks)
        {
            if (block.Type == BlockType.PageBreak)
            {
                sb.AppendLine("\n--- PAGE BREAK ---\n");
                continue;
            }

            foreach (var run in block.Inlines)
            {
                sb.Append(run.Text);
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}

public class DocumentPageSettings
{
    public string PaperSize { get; set; } = "A4"; // A4, Letter
    public double MarginLeftMm { get; set; } = 25.4; // 1 inch
    public double MarginRightMm { get; set; } = 25.4;
    public double MarginTopMm { get; set; } = 25.4;
    public double MarginBottomMm { get; set; } = 25.4;
    public bool IsLandscape { get; set; } = false;
}
