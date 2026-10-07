using System.Globalization;
using System.Text;

namespace Wocel.Core.Pdf;

/// <summary>
/// Đọc lại content stream của một trang và sửa đúng các toán tử hiển thị chữ
/// (Tj, TJ, ' và ") — dùng cho chức năng thay thế văn bản và bôi đen (redact).
/// Phần nội dung không liên quan được giữ nguyên từng byte.
/// </summary>
public static class PdfContentRewriter
{
    private sealed class Piece
    {
        public required uint Code;
        public required string Text;
        public required int ArrayIndex;   // vị trí trong mảng TJ, 0 nếu là Tj
        public double Width;              // 1/1000 em
        public bool Removed;
    }

    /// <summary>Xoá hẳn các chuỗi khớp khỏi nội dung trang. Trả về số lần xoá.</summary>
    public static int RemoveText(PdfDocument document, PdfPage page, IReadOnlyList<string> terms, bool caseSensitive)
        => Rewrite(document, page, terms, replacement: null, caseSensitive, fallbackFonts: null);

    /// <summary>
    /// Thay chuỗi tìm được bằng chuỗi mới. Nếu font gốc thiếu ký tự của chuỗi mới,
    /// đoạn thay thế được vẽ bằng font dự phòng nhúng từ <paramref name="fallbackFonts"/>.
    /// </summary>
    public static int ReplaceText(PdfDocument document, PdfPage page, string search, string replacement,
        bool caseSensitive, PdfFontLibrary? fallbackFonts = null)
        => Rewrite(document, page, new[] { search }, replacement, caseSensitive, fallbackFonts);

    private static int Rewrite(PdfDocument document, PdfPage page, IReadOnlyList<string> terms,
        string? replacement, bool caseSensitive, PdfFontLibrary? fallbackFonts)
    {
        byte[] content;
        try { content = page.GetContentBytes(); }
        catch { return 0; }
        if (content.Length == 0) return 0;

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var parser = new PdfParser(content);
        var output = new MemoryStream();
        int copied = 0;
        int changes = 0;

        var operands = new List<(int start, int end, PdfObject value)>();
        PdfFontInfo? font = null;
        string fontResourceName = string.Empty;
        double fontSize = 12;
        bool usedFallbackFont = false;

        while (!parser.AtEnd)
        {
            parser.SkipWhitespace();
            if (parser.AtEnd) break;

            byte b = parser.PeekByte();
            bool isOperand = b is (byte)'/' or (byte)'(' or (byte)'[' or (byte)'<' or (byte)'+' or (byte)'-' or (byte)'.'
                             || (b >= '0' && b <= '9');

            if (isOperand)
            {
                int start = parser.Position;
                var value = parser.ParseObject();
                if (parser.Position == start) parser.Position++;
                operands.Add((start, parser.Position, value));
                continue;
            }

            int operatorStart = parser.Position;
            var op = parser.ReadToken();
            if (op.Length == 0) { parser.Position++; continue; }
            int operatorEnd = parser.Position;

            if (op == "Tf")
            {
                if (operands.Count >= 2 && operands[^2].value is PdfName name)
                {
                    fontResourceName = name.Value;
                    font = LoadFont(document, page, name.Value);
                }
                if (operands.Count >= 1 && operands[^1].value is PdfNumber size)
                    fontSize = size.Value;
            }
            else if (op is "Tj" or "TJ" or "'" or "\"")
            {
                var replacementText = BuildReplacement(op, operands, font, terms, replacement, comparison,
                    fallbackFonts, fontResourceName, fontSize, out int applied, out bool usedFallback);

                if (replacementText != null && applied > 0)
                {
                    int regionStart = ShowOperandStart(op, operands);
                    if (regionStart >= copied)
                    {
                        output.Write(content, copied, regionStart - copied);
                        var bytes = Encoding.Latin1.GetBytes(replacementText);
                        output.Write(bytes, 0, bytes.Length);
                        copied = operatorEnd;
                        changes += applied;
                        usedFallbackFont |= usedFallback;
                    }
                }
            }

            operands.Clear();
        }

        if (changes == 0) return 0;

        output.Write(content, copied, content.Length - copied);
        page.SetContent(output.ToArray());

        if (usedFallbackFont) fallbackFonts?.ApplyTo(page);
        return changes;
    }

    private static int ShowOperandStart(string op, List<(int start, int end, PdfObject value)> operands)
    {
        // Với ' và " ta chỉ thay từ toán hạng chuỗi trở đi, các tham số trước giữ nguyên.
        for (int i = operands.Count - 1; i >= 0; i--)
            if (operands[i].value is PdfString or PdfArray) return operands[i].start;

        return operands.Count > 0 ? operands[^1].start : 0;
    }

    private static string? BuildReplacement(
        string op, List<(int start, int end, PdfObject value)> operands, PdfFontInfo? font,
        IReadOnlyList<string> terms, string? replacement, StringComparison comparison,
        PdfFontLibrary? fallbackFonts, string fontResourceName, double fontSize,
        out int applied, out bool usedFallback)
    {
        applied = 0;
        usedFallback = false;
        if (operands.Count == 0) return null;

        var showOperand = operands[^1].value;
        var arrayItems = new List<PdfObject>();

        if (showOperand is PdfArray array) arrayItems.AddRange(array.Items);
        else if (showOperand is PdfString) arrayItems.Add(showOperand);
        else return null;

        // Bung từng mã ký tự kèm văn bản Unicode tương ứng.
        var pieces = new List<Piece>();
        for (int index = 0; index < arrayItems.Count; index++)
        {
            if (arrayItems[index] is not PdfString str) continue;

            var codes = font != null ? font.SplitCodes(str.Bytes).ToList() : str.Bytes.Select(x => (uint)x).ToList();
            foreach (uint code in codes)
            {
                pieces.Add(new Piece
                {
                    Code = code,
                    Text = font?.Decode(code) ?? ((char)code).ToString(),
                    ArrayIndex = index,
                    Width = font?.GetWidth(code) ?? 500
                });
            }
        }

        if (pieces.Count == 0) return null;

        var combined = string.Concat(pieces.Select(p => p.Text));
        var offsets = new int[pieces.Count + 1];
        for (int i = 0; i < pieces.Count; i++) offsets[i + 1] = offsets[i] + pieces[i].Text.Length;

        var replacements = new List<(int firstPiece, int lastPiece)>();

        foreach (var term in terms)
        {
            if (string.IsNullOrEmpty(term)) continue;

            int searchFrom = 0;
            while (searchFrom <= combined.Length - term.Length)
            {
                int found = combined.IndexOf(term, searchFrom, comparison);
                if (found < 0) break;

                int firstPiece = PieceAt(offsets, found);
                int lastPiece = PieceAt(offsets, found + term.Length - 1);
                if (firstPiece >= 0 && lastPiece >= firstPiece)
                {
                    for (int i = firstPiece; i <= lastPiece; i++) pieces[i].Removed = true;
                    replacements.Add((firstPiece, lastPiece));
                    applied++;
                }

                searchFrom = found + term.Length;
            }
        }

        if (applied == 0) return null;

        // Với chức năng thay thế, ưu tiên mã hoá bằng chính font đang dùng.
        byte[]? replacementBytes = null;
        PdfFontResource? fallbackFont = null;

        if (replacement != null)
        {
            if (font == null || !font.TryEncode(replacement, out replacementBytes))
            {
                // Font gốc thiếu ký tự (thường gặp với font đã rút gọn) — dùng font dự phòng.
                if (fallbackFonts == null || fontResourceName.Length == 0)
                {
                    applied = 0;
                    return null;
                }

                replacementBytes = null;
                fallbackFont = fallbackFonts.Regular;
                usedFallback = true;
            }
        }

        return Serialize(op, arrayItems, pieces, replacements, replacementBytes, font,
            fallbackFont, replacement, fontResourceName, fontSize);
    }

    private static int PieceAt(int[] offsets, int characterIndex)
    {
        for (int i = 0; i < offsets.Length - 1; i++)
            if (characterIndex >= offsets[i] && characterIndex < offsets[i + 1]) return i;
        return -1;
    }

    private static string Serialize(
        string op, List<PdfObject> arrayItems, List<Piece> pieces,
        List<(int firstPiece, int lastPiece)> replacements, byte[]? replacementBytes, PdfFontInfo? font,
        PdfFontResource? fallbackFont, string? replacementText, string fontResourceName, double fontSize)
    {
        var sb = new StringBuilder();

        // ' và " vẫn phải xuống dòng như cũ trước khi hiển thị chữ.
        if (op is "'" or "\"") sb.Append("T* ");

        var replacementStart = replacements.ToDictionary(r => r.firstPiece, r => r);
        var current = new List<byte>();
        double pendingWidth = 0;
        bool arrayOpen = false;
        int pieceIndex = 0;

        void FlushCodes()
        {
            if (current.Count == 0) return;
            sb.Append('<').Append(Convert.ToHexString(current.ToArray())).Append('>');
            current.Clear();
        }

        void OpenArray()
        {
            if (arrayOpen) return;
            sb.Append('[');
            arrayOpen = true;
        }

        void CloseArray()
        {
            if (!arrayOpen) return;
            FlushCodes();
            sb.Append("] TJ ");
            arrayOpen = false;
            pendingWidth = 0;
        }

        void FlushWidth()
        {
            if (Math.Abs(pendingWidth) < 0.001) return;
            OpenArray();
            FlushCodes();
            sb.Append(' ').Append((-pendingWidth).ToString("0.##", CultureInfo.InvariantCulture)).Append(' ');
            pendingWidth = 0;
        }

        void AppendCode(uint code)
        {
            OpenArray();
            if (font is { IsTwoByte: true })
            {
                current.Add((byte)(code >> 8));
                current.Add((byte)code);
            }
            else
            {
                current.Add((byte)code);
            }
        }

        string Size() => fontSize.ToString("0.####", CultureInfo.InvariantCulture);

        for (int index = 0; index < arrayItems.Count; index++)
        {
            if (arrayItems[index] is PdfNumber number)
            {
                OpenArray();
                FlushCodes();
                sb.Append(' ').Append(number).Append(' ');
                continue;
            }

            if (arrayItems[index] is not PdfString) continue;

            while (pieceIndex < pieces.Count && pieces[pieceIndex].ArrayIndex == index)
            {
                var piece = pieces[pieceIndex];

                if (piece.Removed)
                {
                    bool startsMatch = replacementStart.ContainsKey(pieceIndex);

                    if (replacementBytes != null && startsMatch)
                    {
                        // Chuỗi mới mã hoá được bằng font gốc.
                        FlushWidth();
                        OpenArray();
                        FlushCodes();
                        sb.Append('<').Append(Convert.ToHexString(replacementBytes)).Append('>');
                    }
                    else if (fallbackFont != null && replacementText != null && startsMatch)
                    {
                        // Đổi tạm sang font dự phòng để vẽ chuỗi mới, rồi trả lại font cũ.
                        CloseArray();
                        sb.Append('/').Append(fallbackFont.ResourceName).Append(' ').Append(Size()).Append(" Tf ")
                          .Append(fallbackFont.EncodeForShow(replacementText)).Append(" Tj ")
                          .Append('/').Append(fontResourceName).Append(' ').Append(Size()).Append(" Tf ");
                    }
                    else if (replacementBytes == null && fallbackFont == null)
                    {
                        // Xoá hẳn: bù lại đúng bề rộng đã mất để chữ còn lại không bị xê dịch.
                        pendingWidth += piece.Width;
                    }
                }
                else
                {
                    FlushWidth();
                    AppendCode(piece.Code);
                }

                pieceIndex++;
            }
        }

        FlushWidth();
        CloseArray();
        return sb.ToString().TrimEnd();
    }

    private static PdfFontInfo? LoadFont(PdfDocument document, PdfPage page, string name)
    {
        var fonts = page.Resources.GetDictionary("Font", document);
        if (fonts?.Get(name, document) is not PdfDictionary dict) return null;

        try { return PdfFontInfo.Load(dict, document); }
        catch { return null; }
    }
}
