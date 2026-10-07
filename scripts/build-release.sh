#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
#  Wocel Office — build bản phát hành
#  Cách dùng:  ./scripts/build-release.sh [osx-arm64|osx-x64|win-x64|linux-x64]
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

# Không truyền tham số thì tự nhận kiến trúc của máy đang chạy.
detect_rid() {
    case "$(uname -s)" in
        Darwin) [ "$(uname -m)" = "arm64" ] && echo "osx-arm64" || echo "osx-x64" ;;
        Linux)  [ "$(uname -m)" = "aarch64" ] && echo "linux-arm64" || echo "linux-x64" ;;
        *)      echo "win-x64" ;;
    esac
}

RID="${1:-$(detect_rid)}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="$(grep -o '<Version>[^<]*' "$ROOT/Directory.Build.props" | head -1 | cut -d'>' -f2)"
OUT="$ROOT/dist/$RID"

echo "▶ Wocel Office $VERSION  →  $RID"

echo "▶ Chạy kiểm thử…"
dotnet test "$ROOT/tests/Wocel.Tests/Wocel.Tests.csproj" -c Release --nologo -v q

echo "▶ Publish…"
rm -rf "$OUT"

# Windows: gói tất cả vào MỘT tệp .exe cho gọn, chép đi đâu cũng chạy.
SINGLE_FILE_ARGS=()
if [[ "$RID" == win-* ]]; then
    SINGLE_FILE_ARGS=(
        -p:PublishSingleFile=true
        -p:IncludeNativeLibrariesForSelfExtract=true
        -p:EnableCompressionInSingleFile=true
    )
fi

dotnet publish "$ROOT/src/Wocel.Shell/Wocel.Shell.csproj" \
    -c Release -r "$RID" --self-contained true \
    -p:DebugType=none -p:DebugSymbols=false \
    ${SINGLE_FILE_ARGS[@]+"${SINGLE_FILE_ARGS[@]}"} \
    -o "$OUT" --nologo -v q

cp "$ROOT/.env.example" "$OUT/.env.example"

# Đặt tên tệp chạy cho dễ nhận ra trên máy người dùng.
if [[ "$RID" == win-* && -f "$OUT/Wocel.Shell.exe" ]]; then
    mv "$OUT/Wocel.Shell.exe" "$OUT/Wocel Office.exe"
fi

# macOS: đóng gói thành .app cho dễ chạy
if [[ "$RID" == osx-* ]]; then
    APP="$ROOT/dist/Wocel Office.app"
    rm -rf "$APP"
    mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

    cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>              <string>Wocel Office</string>
    <key>CFBundleDisplayName</key>       <string>Wocel Office</string>
    <key>CFBundleIdentifier</key>        <string>vn.wocel.office</string>
    <key>CFBundleVersion</key>           <string>$VERSION</string>
    <key>CFBundleShortVersionString</key><string>$VERSION</string>
    <key>CFBundleExecutable</key>        <string>Wocel.Shell</string>
    <key>CFBundlePackageType</key>       <string>APPL</string>
    <key>LSMinimumSystemVersion</key>    <string>11.0</string>
    <key>NSHighResolutionCapable</key>   <true/>
    <key>CFBundleDocumentTypes</key>
    <array>
        <dict>
            <key>CFBundleTypeName</key><string>PDF</string>
            <key>CFBundleTypeRole</key><string>Editor</string>
            <key>LSItemContentTypes</key><array><string>com.adobe.pdf</string></array>
        </dict>
    </array>
</dict>
</plist>
PLIST

    cp -R "$OUT/." "$APP/Contents/MacOS/"
    chmod +x "$APP/Contents/MacOS/Wocel.Shell"

    # macOS bắt buộc ứng dụng phải có chữ ký; ký ad-hoc để chạy được ngay trên máy này.
    codesign --force --deep --sign - "$APP" >/dev/null 2>&1 || echo "⚠ Không ký ad-hoc được — hãy mở bằng chuột phải → Open."
    xattr -dr com.apple.quarantine "$APP" 2>/dev/null || true

    echo "▶ Đã đóng gói: $APP"
fi

SIZE="$(du -sh "$OUT" | cut -f1)"
FILES="$(find "$OUT" -type f | wc -l | tr -d ' ')"
echo "✅ Xong. Thư mục: $OUT ($SIZE, $FILES tệp)"

if [[ "$RID" == win-* ]]; then
    EXE_SIZE="$(du -h "$OUT/Wocel Office.exe" | cut -f1)"
    echo "   Tệp chạy duy nhất: $OUT/Wocel Office.exe ($EXE_SIZE)"
fi
