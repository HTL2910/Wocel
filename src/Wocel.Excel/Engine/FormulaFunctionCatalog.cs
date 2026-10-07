namespace Wocel.Excel.Engine;

/// <summary>Mô tả một hàm bảng tính để hiển thị trong danh sách gợi ý.</summary>
public sealed record FormulaFunctionInfo(
    string Name,
    string Signature,
    string Description,
    string Category)
{
    /// <summary>Số tham số tối thiểu, dùng để nhắc người dùng khi gõ.</summary>
    public string Insert => Name + "(";
}

/// <summary>
/// Danh mục hàm mà bộ tính toán của Wocel thực sự chạy được.
/// Đây cũng là nguồn dữ liệu cho ô gợi ý công thức — có test bảo đảm mọi hàm
/// trong danh mục đều được <see cref="FormulaEngine"/> hỗ trợ, không để lệch nhau.
/// </summary>
public static class FormulaFunctionCatalog
{
    public const string CategoryMath = "Toán học";
    public const string CategoryStatistics = "Thống kê";
    public const string CategoryLogic = "Luận lý";
    public const string CategoryText = "Văn bản";
    public const string CategoryDate = "Ngày tháng";
    public const string CategoryLookup = "Tra cứu";

    public static readonly IReadOnlyList<FormulaFunctionInfo> All = new List<FormulaFunctionInfo>
    {
        // ── Toán học ─────────────────────────────────────────────────────
        new("SUM",       "SUM(số1; số2; …)",              "Cộng tất cả các số trong vùng",                    CategoryMath),
        new("PRODUCT",   "PRODUCT(số1; số2; …)",          "Nhân tất cả các số với nhau",                      CategoryMath),
        new("ABS",       "ABS(số)",                       "Trị tuyệt đối",                                    CategoryMath),
        new("ROUND",     "ROUND(số; chữ_số)",             "Làm tròn tới số chữ số thập phân",                 CategoryMath),
        new("ROUNDUP",   "ROUNDUP(số; chữ_số)",           "Làm tròn lên",                                     CategoryMath),
        new("ROUNDDOWN", "ROUNDDOWN(số; chữ_số)",         "Làm tròn xuống",                                   CategoryMath),
        new("INT",       "INT(số)",                       "Lấy phần nguyên (làm tròn xuống)",                 CategoryMath),
        new("MOD",       "MOD(số; số_chia)",              "Số dư của phép chia",                              CategoryMath),
        new("POWER",     "POWER(số; luỹ_thừa)",           "Luỹ thừa",                                         CategoryMath),
        new("SQRT",      "SQRT(số)",                      "Căn bậc hai",                                      CategoryMath),
        new("CEILING",   "CEILING(số; bội_số)",           "Làm tròn lên tới bội số gần nhất",                 CategoryMath),
        new("FLOOR",     "FLOOR(số; bội_số)",             "Làm tròn xuống tới bội số gần nhất",               CategoryMath),

        // ── Thống kê ─────────────────────────────────────────────────────
        new("AVERAGE",   "AVERAGE(số1; số2; …)",          "Giá trị trung bình",                               CategoryStatistics),
        new("COUNT",     "COUNT(vùng)",                   "Đếm số ô chứa số",                                 CategoryStatistics),
        new("COUNTA",    "COUNTA(vùng)",                  "Đếm số ô không rỗng",                              CategoryStatistics),
        new("COUNTBLANK","COUNTBLANK(vùng)",              "Đếm số ô rỗng",                                    CategoryStatistics),
        new("COUNTIF",   "COUNTIF(vùng; điều_kiện)",      "Đếm ô thoả điều kiện, ví dụ \">10\"",              CategoryStatistics),
        new("SUMIF",     "SUMIF(vùng; điều_kiện; [vùng_tính])", "Cộng các ô thoả điều kiện",                  CategoryStatistics),
        new("MIN",       "MIN(số1; số2; …)",              "Giá trị nhỏ nhất",                                 CategoryStatistics),
        new("MAX",       "MAX(số1; số2; …)",              "Giá trị lớn nhất",                                 CategoryStatistics),
        new("MEDIAN",    "MEDIAN(số1; số2; …)",           "Giá trị trung vị",                                 CategoryStatistics),
        new("STDEV",     "STDEV(số1; số2; …)",            "Độ lệch chuẩn (mẫu)",                              CategoryStatistics),

        // ── Luận lý ──────────────────────────────────────────────────────
        new("IF",        "IF(điều_kiện; giá_trị_đúng; giá_trị_sai)", "Trả về giá trị tuỳ theo điều kiện",     CategoryLogic),
        new("AND",       "AND(đk1; đk2; …)",              "Đúng khi mọi điều kiện đều đúng",                  CategoryLogic),
        new("OR",        "OR(đk1; đk2; …)",               "Đúng khi có ít nhất một điều kiện đúng",           CategoryLogic),
        new("NOT",       "NOT(điều_kiện)",                "Đảo ngược đúng/sai",                               CategoryLogic),
        new("IFERROR",   "IFERROR(biểu_thức; giá_trị_nếu_lỗi)", "Thay lỗi bằng giá trị khác",                 CategoryLogic),
        new("TRUE",      "TRUE()",                        "Giá trị đúng",                                     CategoryLogic),
        new("FALSE",     "FALSE()",                       "Giá trị sai",                                      CategoryLogic),

        // ── Văn bản ──────────────────────────────────────────────────────
        new("CONCAT",     "CONCAT(chuỗi1; chuỗi2; …)",    "Nối các chuỗi lại với nhau",                       CategoryText),
        new("LEFT",       "LEFT(chuỗi; số_ký_tự)",        "Lấy ký tự từ bên trái",                            CategoryText),
        new("RIGHT",      "RIGHT(chuỗi; số_ký_tự)",       "Lấy ký tự từ bên phải",                            CategoryText),
        new("MID",        "MID(chuỗi; vị_trí; số_ký_tự)", "Lấy ký tự từ giữa chuỗi",                          CategoryText),
        new("LEN",        "LEN(chuỗi)",                   "Đếm số ký tự",                                     CategoryText),
        new("UPPER",      "UPPER(chuỗi)",                 "Đổi thành CHỮ HOA",                                CategoryText),
        new("LOWER",      "LOWER(chuỗi)",                 "Đổi thành chữ thường",                             CategoryText),
        new("PROPER",     "PROPER(chuỗi)",                "Viết Hoa Chữ Đầu Mỗi Từ",                          CategoryText),
        new("TRIM",       "TRIM(chuỗi)",                  "Bỏ khoảng trắng thừa",                             CategoryText),
        new("SUBSTITUTE", "SUBSTITUTE(chuỗi; cũ; mới)",   "Thay thế đoạn văn bản",                            CategoryText),
        new("FIND",       "FIND(cần_tìm; chuỗi)",         "Vị trí xuất hiện đầu tiên",                        CategoryText),
        new("REPT",       "REPT(chuỗi; số_lần)",          "Lặp lại chuỗi nhiều lần",                          CategoryText),
        new("TEXTJOIN",   "TEXTJOIN(dấu_nối; vùng)",      "Nối các ô bằng dấu ngăn cách",                     CategoryText),
        new("VALUE",      "VALUE(chuỗi)",                 "Đổi chuỗi thành số",                               CategoryText),

        // ── Ngày tháng ───────────────────────────────────────────────────
        new("TODAY",   "TODAY()",                  "Ngày hôm nay",                                            CategoryDate),
        new("NOW",     "NOW()",                    "Ngày giờ hiện tại",                                       CategoryDate),
        new("DATE",    "DATE(năm; tháng; ngày)",   "Tạo một ngày",                                            CategoryDate),
        new("YEAR",    "YEAR(ngày)",               "Lấy năm",                                                 CategoryDate),
        new("MONTH",   "MONTH(ngày)",              "Lấy tháng",                                               CategoryDate),
        new("DAY",     "DAY(ngày)",                "Lấy ngày",                                                CategoryDate),
        new("WEEKDAY", "WEEKDAY(ngày)",            "Thứ trong tuần (1 = Chủ nhật)",                           CategoryDate),

        // ── Tra cứu ──────────────────────────────────────────────────────
        new("VLOOKUP", "VLOOKUP(giá_trị; vùng; cột)", "Dò tìm theo cột đầu tiên của vùng",                    CategoryLookup),
        new("HLOOKUP", "HLOOKUP(giá_trị; vùng; dòng)", "Dò tìm theo dòng đầu tiên của vùng",                  CategoryLookup),
        new("INDEX",   "INDEX(vùng; dòng; [cột])",     "Lấy ô theo vị trí dòng/cột trong vùng",               CategoryLookup),
        new("MATCH",   "MATCH(giá_trị; vùng)",         "Vị trí của giá trị trong vùng",                       CategoryLookup)
    };

    private static readonly Dictionary<string, FormulaFunctionInfo> ByName =
        All.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);

    public static FormulaFunctionInfo? Find(string name) =>
        ByName.TryGetValue(name, out var info) ? info : null;

    /// <summary>
    /// Tìm các hàm khớp với phần người dùng đang gõ. Khớp từ đầu tên được xếp trước,
    /// sau đó mới tới khớp ở giữa tên hoặc trong phần mô tả.
    /// </summary>
    public static List<FormulaFunctionInfo> Search(string prefix, int limit = 12)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return All.OrderBy(f => f.Name, StringComparer.Ordinal).Take(limit).ToList();

        var term = prefix.Trim();

        var startsWith = All
            .Where(f => f.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Name.Length)
            .ThenBy(f => f.Name, StringComparer.Ordinal);

        var contains = All
            .Where(f => !f.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase)
                        && (f.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                            || f.Description.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(f => f.Name, StringComparer.Ordinal);

        return startsWith.Concat(contains).Take(limit).ToList();
    }

    /// <summary>
    /// Lấy phần tên hàm đang được gõ dở ngay trước con trỏ.
    /// Trả về null nếu con trỏ không nằm ở chỗ có thể gợi ý (chưa có dấu =, đang trong chuỗi…).
    /// </summary>
    public static string? GetPrefixAtCaret(string? text, int caretIndex)
    {
        if (string.IsNullOrEmpty(text)) return null;
        if (!text.TrimStart().StartsWith('=')) return null;

        caretIndex = Math.Clamp(caretIndex, 0, text.Length);

        // Không gợi ý khi con trỏ đang nằm trong một chuỗi trích dẫn.
        int quotes = 0;
        for (int i = 0; i < caretIndex; i++)
            if (text[i] == '"') quotes++;
        if (quotes % 2 == 1) return null;

        int start = caretIndex;
        while (start > 0 && char.IsLetter(text[start - 1])) start--;

        // Ngay trước phần chữ phải là dấu =, dấu ngăn cách hoặc toán tử — không phải chữ/số khác.
        if (start > 0)
        {
            char before = text[start - 1];
            if (!"=+-*/(,;: <>&".Contains(before)) return null;
        }

        return text[start..caretIndex];
    }
}
