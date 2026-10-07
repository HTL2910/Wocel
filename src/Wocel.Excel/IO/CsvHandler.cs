using System.Text;
using Wocel.Core.Models;

namespace Wocel.Excel.IO;

public static class CsvHandler
{
    public static SpreadsheetDocument ReadCsv(Stream stream, char delimiter = ',')
    {
        var doc = new SpreadsheetDocument();
        var ws = doc.GetOrCreateActiveSheet();

        using var reader = new StreamReader(stream, Encoding.UTF8);
        int row = 1;

        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (line == null) break;

            var cols = ParseCsvLine(line, delimiter);
            for (int col = 1; col <= cols.Count; col++)
            {
                var val = cols[col - 1];
                var a1 = CellAddress.ToA1(row, col);
                ws.SetValue(a1, val);
            }
            row++;
        }

        return doc;
    }

    public static void WriteCsv(SpreadsheetDocument doc, Stream outputStream, char delimiter = ',')
    {
        using var writer = new StreamWriter(outputStream, new UTF8Encoding(true), leaveOpen: true);
        var ws = doc.GetOrCreateActiveSheet();

        int maxRow = ws.MaxRow;
        int maxCol = ws.MaxCol;

        for (int r = 1; r <= maxRow; r++)
        {
            var rowVals = new List<string>();
            for (int c = 1; c <= maxCol; c++)
            {
                var a1 = CellAddress.ToA1(r, c);
                var cell = ws.GetCell(a1);
                var text = cell.GetDisplayString();
                rowVals.Add(EscapeCsvField(text, delimiter));
            }
            writer.WriteLine(string.Join(delimiter.ToString(), rowVals));
        }
    }

    private static List<string> ParseCsvLine(string line, char delimiter)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                result.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        result.Add(sb.ToString().Trim());
        return result;
    }

    private static string EscapeCsvField(string field, char delimiter)
    {
        if (field.Contains(delimiter) || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }
}
