# Cấu hình Google Drive cho Wocel Capture

Wocel Capture dùng OAuth 2.0 dành cho **Desktop app** và chỉ yêu cầu scope `drive.file`. Scope này cho phép ứng dụng tạo và quản lý các ảnh do chính ứng dụng tạo; ứng dụng không đọc toàn bộ Google Drive.

## Tạo OAuth client

1. Mở [Google Cloud Console](https://console.cloud.google.com/) và tạo hoặc chọn một project.
2. Trong **APIs & Services → Library**, bật **Google Drive API**.
3. Cấu hình **OAuth consent screen**. Thêm scope `https://www.googleapis.com/auth/drive.file`.
4. Vào **Credentials → Create credentials → OAuth client ID**.
5. Chọn application type **Desktop app**, đặt tên `Wocel Capture`, rồi copy Client ID.
6. Với app dùng ngoài nhóm test, chuyển consent screen sang Production và hoàn tất các yêu cầu xác minh Google hiển thị.

Tài liệu chính thức: [OAuth cho desktop app](https://developers.google.com/identity/protocols/oauth2/native-app), [Drive scopes](https://developers.google.com/workspace/drive/api/guides/api-specific-auth), [chia sẻ file Drive](https://developers.google.com/workspace/drive/api/guides/manage-sharing).

## Đưa Client ID vào bản build

Client ID của desktop app là cấu hình công khai, không phải client secret. Không tạo hoặc nhúng client secret.

PowerShell:

```powershell
$env:WOCEL_GOOGLE_CLIENT_ID = "YOUR_CLIENT_ID.apps.googleusercontent.com"
./scripts/build-capture-release.ps1
```

Script ghi Client ID vào `oauth-client-id.txt` cạnh file thực thi. Khi phát triển, có thể đặt biến môi trường `WOCEL_GOOGLE_CLIENT_ID` trước khi chạy app.

## Kiểm tra trước phát hành

1. Cài app và bấm **Đăng nhập Google**.
2. Xác nhận trình duyệt hệ thống mở trang Google và consent screen chỉ xin quyền đối với file do app tạo.
3. Chụp một ảnh, bật upload và bấm **Upload & Copy Link**.
4. Kiểm tra thư mục `Wocel Capture` xuất hiện trong My Drive.
5. Mở link vừa copy trong cửa sổ trình duyệt riêng tư.
6. Thu hồi quyền app trong Google Account, rồi xác nhận lần upload kế tiếp chuyển sang trạng thái yêu cầu đăng nhập.
7. Với tài khoản Workspace cấm public link, xác nhận ảnh vẫn upload nhưng History ghi trạng thái `UploadedPrivate`.

Refresh token được mã hóa bằng Windows DPAPI cho đúng tài khoản Windows hiện tại. Đăng xuất xóa token cục bộ và cố gắng thu hồi grant; ảnh đã upload không bị xóa.
