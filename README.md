# Wocel Office 1.0

Bộ ứng dụng văn phòng chạy trên máy tính (Avalonia + .NET 10): **Excel**, **Word**, **trình xem PDF**
và **bộ công cụ xử lý tệp 29 chức năng** theo phong cách Smallpdf.

Toàn bộ phần xử lý PDF do Wocel tự viết trong `Wocel.Core` — **không dùng thư viện PDF bên ngoài**,
và **không tệp nào của bạn được gửi lên internet**.

---

## Bộ công cụ xử lý tệp

Mở từ màn hình chào (ô **“Bộ công cụ xử lý tệp”**) hoặc nút **🛠 Công cụ tệp** trên thanh tiêu đề.

| Nhóm | Công cụ |
|---|---|
| **Tổ chức trang** | Gộp PDF · Tách PDF · Giữ/xoá trang · Sắp xếp lại trang · Xoay trang · Khổ giấy & cắt lề |
| **Chuyển đổi** | PDF → Văn bản · PDF → Excel (CSV) · Tách ảnh khỏi PDF · Ảnh → PDF · Văn bản/Bảng → PDF |
| **Hệ thống** | Nhật ký hoạt động |

Danh sách rút từ 29 xuống 12: gộp các công cụ trùng việc, rồi bỏ nhóm chỉnh sửa
(watermark, đầu-chân trang, tìm thay thế, bôi đen) và nhóm bảo mật/tài liệu.
Phần lõi trong `PdfToolkit` vẫn còn đủ, bật lại chỉ cần thêm một dòng vào danh sách công cụ.

Vài điểm đáng chú ý:

- **Tiếng Việt đủ dấu.** Khi sinh PDF mới (văn bản, bảng, watermark, số trang…), Wocel nhúng một phông
  TrueType Unicode có sẵn trên máy và **tự rút gọn** phông chỉ còn các glyph thực dùng, nên tệp vẫn nhỏ.
- **Bôi đen là bôi đen thật.** Chuỗi khớp bị **xoá khỏi content stream** rồi mới phủ hộp đen —
  không thể bôi đen rồi copy lại ra được. Bề rộng bị mất được bù bằng toán tử `TJ` nên chữ còn lại không xê dịch.
- **Mật khẩu.** Wocel chỉ giải mã khi **bạn cung cấp đúng mật khẩu**; công cụ không dò tìm mật khẩu.
- **Sửa tệp hỏng.** Khi bảng `xref` sai, Wocel quét lại toàn bộ tệp để dựng lại danh sách đối tượng và cây trang.

## Nhân xử lý PDF (`Wocel.Core/Pdf`)

| Tệp | Vai trò |
|---|---|
| `PdfObjects.cs` | Mô hình đối tượng COS (name, string, array, dictionary, stream, tham chiếu) |
| `PdfParser.cs` | Phân tích cú pháp trực tiếp trên byte, không làm hỏng dữ liệu nhị phân |
| `PdfFilters.cs` | Flate, LZW, ASCIIHex, ASCII85, RunLength + predictor PNG/TIFF |
| `PdfDocument.cs` | Bảng xref cổ điển **và** xref stream, object stream, tự sửa tệp hỏng, phẳng hoá cây trang |
| `PdfWriter.cs` | Ghi tệp: dọn đối tượng thừa, gộp đối tượng trùng, object stream, mã hoá |
| `PdfEncryption.cs` | Trình bảo mật tiêu chuẩn: RC4 40/128, AES-128 (AESV2), AES-256 (AESV3/R6) |
| `PdfTextExtractor.cs` | Đọc content stream, áp ma trận văn bản để lấy đúng toạ độ và thứ tự chữ |
| `PdfFontInfo.cs` / `PdfGlyphList.cs` | ToUnicode CMap, WinAnsi/MacRoman, tên glyph (kể cả tên glyph tiếng Việt) |
| `PdfTrueTypeFont.cs` | Đọc `.ttf` và tạo bản rút gọn để nhúng |
| `PdfImageCodec.cs` | JPEG (nhúng thẳng), PNG (giải mã + mã hoá), BMP |
| `PdfContentRewriter.cs` | Sửa toán tử hiển thị chữ — nền tảng cho thay thế và bôi đen |

API cấp cao nằm ở `Wocel.Core/Services/PdfToolkit*.cs`; mọi thao tác nhận `byte[]` và trả `byte[]`.

---

## Bố cục

Xếp theo đúng thứ tự quen thuộc của Excel, từ trên xuống:

```
Thanh tiêu đề     🏠 · 💾 Lưu · 📂 Mở · + Excel · + Word · 🛠 Công cụ  │ tên tài liệu │ 🔍 tìm kiếm
Thẻ ribbon        Tệp · Trang đầu · Chèn · Bố trí trang · Công thức · Dữ liệu · Xem
Thanh công cụ     các nhóm lệnh, tên nhóm nằm dưới như Excel
Thẻ tài liệu      mở nhiều tệp cùng lúc
Thanh công thức   [ô địa chỉ] ✕ ✓ fx [nhập công thức]
Lưới              tiêu đề cột A B C · tiêu đề dòng 1 2 3
Dải trang tính    ◀ ▶ Sheet1 Sheet2 +
Thanh trạng thái  SẴN SÀNG │ thông báo │ AVERAGE · COUNT · SUM │ − ▭▭▭ + 100%
```

## Bảng tính Excel

- **Thao tác bàn phím đầy đủ**: phím mũi tên, Enter, Tab/Shift+Tab, F2, Delete, Home/End,
  Ctrl+Home/End, PageUp/PageDown; gõ thẳng ký tự là vào chế độ nhập như Excel.
- **Chọn nhiều ô**: Shift + chuột hoặc Shift + mũi tên để quét vùng, kéo chuột để chọn,
  Ctrl (Cmd trên macOS) + chuột để cộng thêm vùng rời, Ctrl+A chọn tất cả,
  bấm tiêu đề dòng/cột để chọn cả dòng/cột, ô góc trên-trái chọn toàn bảng.
  Thanh trạng thái tính SUM · AVERAGE · COUNT **trên vùng đang chọn**, ô địa chỉ hiện `B2:D5`.
- **Bảng tạm theo vùng**: Ctrl+C / Ctrl+X / Ctrl+V sao chép cả khối ô ở dạng cách nhau bằng Tab —
  dán thẳng sang Excel hay Google Sheets được, và ngược lại. Delete xoá sạch vùng chọn.
- **Gợi ý công thức**: gõ `=SU` là hiện ngay SUM, SUMIF… kèm cú pháp và mô tả tiếng Việt.
  Dùng ↑ ↓ để chọn, Tab hoặc Enter để chèn, Esc để bỏ qua. Có ở cả ô trong lưới lẫn thanh công thức.
- **53 hàm** chia theo nhóm Toán học · Thống kê · Luận lý · Văn bản · Ngày tháng · Tra cứu —
  xem [`FormulaFunctionCatalog.cs`](src/Wocel.Excel/Engine/FormulaFunctionCatalog.cs).
  Có test bảo đảm mọi hàm trong danh mục gợi ý đều thực sự tính được, không để danh sách nói dối.
- Dấu ngăn tham số dùng được cả `,` lẫn `;` theo thói quen Excel bản Việt.
- **Ribbon 7 thẻ có việc thật**: Tệp · Trang đầu · Chèn · Bố trí trang · Công thức · Dữ liệu · Xem —
  chèn/xoá dòng cột, xuất PDF & CSV, hàm nhanh, xoá dòng trùng, thu phóng, nhảy tới ô, tìm kiếm.
- **Nhiều trang tính**: thêm, chuyển, xoá (chuột phải vào thẻ); đọc/ghi đủ mọi trang trong tệp `.xlsx`.
- **Định dạng ô**: đậm · nghiêng · gạch chân · **cỡ chữ · phông chữ** · căn lề · gộp ô ·
  8 kiểu định dạng số (tiền tệ ₫, phần trăm, nghìn, ngày tháng…) — áp cho cả vùng chọn,
  hiển thị đúng trên lưới, dòng tự cao lên cho vừa cỡ chữ lớn.
- Định dạng thuộc về **ô**, không thuộc về giá trị: gõ đè nội dung mới hay xoá bằng Delete
  đều giữ nguyên định dạng, giống Excel.
- Toàn bộ định dạng **được lưu vào tệp .xlsx** (xl/styles.xml) và đọc lại đúng.

## Trình soạn thảo Word

`TextBox` chỉ có một phông cho toàn bộ nội dung, nên bôi đậm hay đổi cỡ chữ theo vùng chọn
là bất khả thi. Wocel thay bằng [`WordCanvasEditor`](src/Wocel.Shell/Controls/WordCanvasEditor.cs) —
vẽ trực tiếp lên canvas, mỗi đoạn chữ giữ riêng định dạng:

- **Bôi đen rồi định dạng**: đậm · nghiêng · gạch chân · cỡ chữ · phông · màu, chỉ tác động
  đúng phần được chọn.
- **Chọn định dạng trước rồi gõ** cũng chạy đúng như Word: chưa bôi đen thì định dạng được
  ghi nhớ và áp cho phần chữ gõ tiếp theo, không đụng tới chữ đã có. Di chuyển con trỏ thì bỏ ghi nhớ.
- **Cỡ chữ 8–72** chọn trong danh sách thả xuống; cỡ ngoài danh sách dùng hai nút A▴ A▾ (bước 1).
- Tự cắt và gộp lại các đoạn định dạng nên không sinh vụn khi định dạng qua lại nhiều lần.
- Soạn thảo đầy đủ: gõ chữ, Enter tách đoạn, Backspace/Delete, mũi tên, Home/End,
  Shift + mũi tên để bôi đen, Ctrl+A, nhấp đúp chọn từ, kéo chuột chọn vùng, sao chép/cắt/dán.
- Định dạng còn nguyên khi lưu ra `.docx` và mở lại — kể cả **cỡ chữ, phông và màu**,
  vốn trước đây không được ghi vào tệp.

## Biểu tượng trên thanh công cụ

- Biểu tượng căn lề vẽ bằng **vector** (`StreamGeometry`), không dùng ký tự Unicode —
  `⫷` `⫸` thiếu trong nhiều phông và bị hệ thống thay bằng ký tự lạ.
- Nút biểu tượng dùng lớp `icon` để bỏ padding mặc định của theme Fluent. Không bỏ thì
  nút 26px sẽ **cắt cụt hai bên chữ**: `B` trông thành `E`, `U` thành `L`.
- Ribbon đổi theo loại tài liệu: mở văn bản thì nhóm **Số**, **Chỉnh sửa** và hai thẻ
  **Công thức**, **Dữ liệu** tự ẩn vì không dùng được cho Word.

## Chú thích và phím tắt

Mọi lệnh đều có chú thích kiểu Office khi rê chuột: **tên lệnh** in đậm, mô tả ngắn bên dưới,
kèm phím tắt trong khung xám. Phím tắt hiện đúng theo hệ điều hành (`⌘S` trên macOS, `Ctrl+S` trên Windows).

| Phím tắt | Việc |
|---|---|
| `Ctrl/⌘ + S` · `O` · `N` | Lưu · Mở tệp · Sổ tính mới |
| `Ctrl/⌘ + F` | Nhảy vào ô tìm kiếm |
| `Ctrl/⌘ + B` · `I` · `U` | Đậm · Nghiêng · Gạch chân |
| `Ctrl/⌘ + C` · `X` · `V` · `A` | Sao chép · Cắt · Dán · Chọn tất cả |
| `Ctrl/⌘ + Z` · `Y` | Hoàn tác · Làm lại |
| `Ctrl/⌘ + W` | Đóng thẻ đang mở |
| `F2` · `Delete` · `Esc` | Sửa ô · Xoá nội dung · Huỷ |

Hoàn tác chạy trong **cả bảng tính lẫn tài liệu văn bản**, quay lại được cả nội dung
lẫn định dạng, giữ 60 bước gần nhất.

Chú thích **đổi theo loại tài liệu**: cùng nút Đậm, bên bảng tính ghi "chữ trong các ô đang chọn",
bên văn bản ghi "phần chữ bôi đen, hoặc chữ bạn gõ tiếp theo".

Có test bắt buộc: mọi nút phải có chú thích; phím tắt nào được nhắc thì phải thực sự đã nối;
và chú thích phải là **chuỗi** chứ không phải đối tượng Control — gắn bằng Control thì
Avalonia chỉ hiện được đúng một lần rồi thôi.

## Xuất ra PDF

- **Bảng tính → PDF**: vẽ thành bảng có khung, tự lặp dòng tiêu đề mỗi trang, ô công thức
  xuất ra *kết quả* chứ không phải chuỗi `=SUM(...)`.
- **Tài liệu văn bản → PDF**: giữ nguyên đậm, nghiêng, gạch chân và cỡ chữ của từng đoạn.
- Nút tự đổi nhãn theo loại tài liệu đang mở; kiểm tra dữ liệu **trước** khi hỏi nơi lưu.
- Mọi lệnh trên ribbon được bọc bắt lỗi — hỏng thì hiện thông báo ở thanh trạng thái và ghi
  vào nhật ký, thay vì im lặng như trước.

## Nhật ký hoạt động (truy vết lỗi)

Mỗi thao tác đều được ghi lại: công cụ nào, tệp bao nhiêu byte, chạy mất bao lâu, thành công hay lỗi gì.

- **Luôn ghi tại máy:** `~/Library/Application Support/Wocel/logs/activity-YYYYMMDD.ndjson`
  (Windows: `%APPDATA%\Wocel\logs`).
- **Đẩy lên Supabase** (tuỳ chọn) để xem tập trung và truy lỗi từ xa.

> Nhật ký **chỉ chứa thông tin về thao tác**. Nội dung tài liệu không bao giờ rời khỏi máy bạn.

### Bật ghi log lên Supabase

1. Mở Supabase → **SQL Editor**, chạy toàn bộ [`docs/supabase-activity-log.sql`](docs/supabase-activity-log.sql)
   (tạo bảng `wocel_activity_logs`, chỉ mục và policy chỉ-cho-ghi cho khoá `anon`).
2. Điền `SUPABASE_URL` và `SUPABASE_ANON_KEY` vào tệp `.env` (xem `.env.example`).
3. Mở app → **🛠 Công cụ tệp** → **Nhật ký hoạt động** để kiểm tra trạng thái đồng bộ.

Khi mất mạng, các dòng chờ được giữ trong `pending.ndjson` và tự gửi lại ở lần sau — không mất dữ liệu.

Các truy vấn SQL hay dùng (thống kê công cụ hay lỗi, thao tác chậm, dựng lại một phiên làm việc)
nằm sẵn ở cuối tệp `docs/supabase-activity-log.sql`.

---

## Chạy và build

```bash
# Chạy thử
dotnet run --project src/Wocel.Shell

# Kiểm thử (224 test)
dotnet test tests/Wocel.Tests/Wocel.Tests.csproj

# Build bản phát hành — tự nhận kiến trúc máy, macOS thì đóng gói .app luôn
./scripts/build-release.sh
./scripts/build-release.sh win-x64      # hoặc chỉ định rõ nền tảng
```

**Windows** ra đúng **một tệp** `dist/win-x64/Wocel Office.exe` (~46 MB) — mọi thư viện
đã gói sẵn bên trong, chép đi đâu cũng chạy, không cần cài .NET.
**macOS** ra `dist/Wocel Office.app` (đã ký ad-hoc).

Muốn app đọc được cấu hình Supabase, đặt tệp `.env` cạnh tệp thực thi
(macOS: `Wocel Office.app/Contents/MacOS/.env`).

## Cấu trúc dự án

```
src/Wocel.Core     Mô hình dữ liệu, nhân PDF, PdfToolkit, nhật ký hoạt động
src/Wocel.Word     Đọc/ghi .docx, .rtf
src/Wocel.Excel    Đọc/ghi .xlsx, .csv, công thức
src/Wocel.Shell    Giao diện Avalonia (Excel grid, Word editor, PDF viewer, bộ công cụ tệp)
tests/Wocel.Tests  224 test: nhân PDF, các công cụ, và dựng giao diện không màn hình
```

---

## Wocel Capture

Repo có thêm ứng dụng Windows độc lập **Wocel Capture**:

- Chụp vùng bằng `Print Screen` hoặc nút trong app; chạy nền ở system tray.
- Editor không phá hủy ảnh gốc: bút, highlight, chữ, làm mờ, crop, resize, đường thẳng, mũi tên, chữ nhật, ellipse và tam giác.
- Undo/redo 100 thao tác, copy clipboard, lưu PNG/JPG.
- History, nhật ký hoạt động đã lọc secret và hàng đợi upload bền vững.
- Đăng nhập Google bằng OAuth 2.0 + PKCE; upload resumable vào thư mục `Wocel Capture`, tạo link xem và copy link.

```powershell
# Test phần core trên mọi hệ điều hành
dotnet test tests/Wocel.Capture.Tests/Wocel.Capture.Tests.csproj

# Build WPF (có thể cross-compile; chạy app cần Windows 10/11 x64)
dotnet build src/Wocel.Capture.Windows/Wocel.Capture.Windows.csproj

# Trên Windows: publish self-contained và tạo installer nếu có Inno Setup 6
./scripts/build-capture-release.ps1
```

Thiết lập Google Cloud xem tại [`docs/wocel-capture-google-setup.md`](docs/wocel-capture-google-setup.md). Spec và implementation plan nằm trong `docs/superpowers/`.
