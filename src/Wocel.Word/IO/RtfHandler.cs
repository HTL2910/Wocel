using System.Text;
using Wocel.Core.Models;

namespace Wocel.Word.IO;

public static class RtfHandler
{
    public static DocumentDocument ReadRtf(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var rtf = reader.ReadToEnd();

        var doc = new DocumentDocument();
        var extractedText = ExtractTextFromRtf(rtf);
        var paragraphs = extractedText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var para in paragraphs)
        {
            var trimmed = para.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            var block = new DocBlock { Type = BlockType.Paragraph };
            block.Inlines.Add(new TextRun { Text = trimmed });
            doc.Blocks.Add(block);
        }

        if (doc.Blocks.Count == 0)
        {
            doc.Blocks.Add(new DocBlock { Type = BlockType.Paragraph });
        }

        return doc;
    }

    public static void WriteRtf(DocumentDocument doc, Stream outputStream)
    {
        using var writer = new StreamWriter(outputStream, Encoding.ASCII, leaveOpen: true);
        writer.WriteLine(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Calibri;}}");
        writer.WriteLine(@"\pard\f0\fs24");

        for (int i = 0; i < doc.Blocks.Count; i++)
        {
            var block = doc.Blocks[i];
            if (block.Type == BlockType.Heading1) writer.Write(@"\fs36\b ");
            else if (block.Type == BlockType.Heading2) writer.Write(@"\fs30\b ");
            else if (block.Type == BlockType.Heading3) writer.Write(@"\fs26\b ");

            foreach (var run in block.Inlines)
            {
                if (run.IsBold) writer.Write(@"\b ");
                if (run.IsItalic) writer.Write(@"\i ");
                if (run.IsUnderline) writer.Write(@"\ul ");

                writer.Write(EscapeRtf(run.Text));

                if (run.IsUnderline) writer.Write(@"\ul0 ");
                if (run.IsItalic) writer.Write(@"\i0 ");
                if (run.IsBold) writer.Write(@"\b0 ");
            }

            if (block.Type is BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3)
            {
                writer.Write(@"\b0\fs24");
            }

            if (i < doc.Blocks.Count - 1)
            {
                writer.WriteLine(@"\par");
            }
        }

        writer.WriteLine("}");
    }

    private static string EscapeRtf(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            if (c == '\\' || c == '{' || c == '}') sb.Append('\\').Append(c);
            else if (c > 127) sb.Append($@"\u{(int)c}?");
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static string ExtractTextFromRtf(string rtf)
    {
        var sb = new StringBuilder();
        var groupDepth = 0;
        var inControl = false;
        var inFontTable = false;

        for (int i = 0; i < rtf.Length; i++)
        {
            char c = rtf[i];

            if (c == '{')
            {
                groupDepth++;
                // Check if group is header/fonttbl
                if (i + 8 < rtf.Length && rtf.Substring(i, 8).StartsWith("{\\fonttb"))
                {
                    inFontTable = true;
                }
                continue;
            }

            if (c == '}')
            {
                groupDepth--;
                if (inFontTable) inFontTable = false;
                continue;
            }

            if (inFontTable) continue;

            if (c == '\\')
            {
                // check if \par
                if (i + 4 <= rtf.Length && (rtf.Substring(i, 4) == "\\par" || rtf.Substring(i, 4) == "\\par\r" || rtf.Substring(i, 4) == "\\par\n" || rtf.Substring(i, 4) == "\\par "))
                {
                    sb.AppendLine();
                    i += 3;
                    continue;
                }
                inControl = true;
                continue;
            }

            if (inControl)
            {
                if (char.IsWhiteSpace(c))
                {
                    inControl = false;
                }
                else if (c == '\\' || c == '{' || c == '}')
                {
                    inControl = false;
                    i--; // re-process
                }
                continue;
            }

            if (c != '\r' && c != '\n')
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Trim();
    }
}
