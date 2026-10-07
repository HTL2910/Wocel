using System.Globalization;

namespace Wocel.Core.Services;

/// <summary>
/// Phân tích chuỗi chọn trang kiểu "1, 3, 5-7, 10-" (n = trang cuối).
/// Số trang tính từ 1 như người dùng vẫn quen.
/// </summary>
public static class PdfPageRange
{
    /// <summary>Trả về danh sách chỉ số trang (1-based) đã sắp xếp, loại trùng và nằm trong [1..pageCount].</summary>
    public static List<int> Parse(string? expression, int pageCount)
    {
        var pages = new SortedSet<int>();
        if (string.IsNullOrWhiteSpace(expression)) return pages.ToList();

        var text = expression.Trim().ToLowerInvariant();

        if (text is "all" or "tất cả" or "tat ca" or "*")
            return Enumerable.Range(1, pageCount).ToList();

        if (text is "even" or "chẵn" or "chan")
            return Enumerable.Range(1, pageCount).Where(p => p % 2 == 0).ToList();

        if (text is "odd" or "lẻ" or "le")
            return Enumerable.Range(1, pageCount).Where(p => p % 2 == 1).ToList();

        foreach (var part in text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = part.Trim();
            if (token.Length == 0) continue;

            int dash = token.IndexOf('-', 1);
            if (dash > 0)
            {
                var fromText = token[..dash].Trim();
                var toText = token[(dash + 1)..].Trim();

                int from = ParseNumber(fromText, pageCount, 1);
                int to = toText.Length == 0 ? pageCount : ParseNumber(toText, pageCount, pageCount);

                if (from > to) (from, to) = (to, from);
                for (int p = Math.Max(1, from); p <= Math.Min(pageCount, to); p++) pages.Add(p);
            }
            else
            {
                int page = ParseNumber(token, pageCount, -1);
                if (page >= 1 && page <= pageCount) pages.Add(page);
            }
        }

        return pages.ToList();
    }

    private static int ParseNumber(string token, int pageCount, int fallback)
    {
        if (token is "n" or "cuối" or "cuoi" or "last") return pageCount;
        return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
    }

    /// <summary>Rút gọn danh sách trang thành chuỗi dễ đọc: 1-3, 7, 10-12.</summary>
    public static string Describe(IEnumerable<int> pages)
    {
        var ordered = pages.Distinct().OrderBy(p => p).ToList();
        if (ordered.Count == 0) return "(không có trang nào)";

        var parts = new List<string>();
        int start = ordered[0], previous = ordered[0];

        foreach (int page in ordered.Skip(1))
        {
            if (page == previous + 1) { previous = page; continue; }
            parts.Add(start == previous ? start.ToString() : $"{start}-{previous}");
            start = previous = page;
        }

        parts.Add(start == previous ? start.ToString() : $"{start}-{previous}");
        return string.Join(", ", parts);
    }
}
