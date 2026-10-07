# Wocel Capture Cross-Platform Design

**Status:** Proposed for user review  
**Date:** 2026-10-06  
**Platforms:** Windows 10/11 x64; macOS 13+ on Apple Silicon and Intel  
**Distribution:** Windows portable `.exe` and installer; macOS `.app` and architecture-specific `.dmg`

## 1. Purpose

Wocel Capture will provide the same capture, annotation, History, safe logging, Google sign-in, and Drive-sharing workflow on Windows and macOS. The product remains a standalone tray/menu-bar application. Platform differences are limited to native operating-system integration; product behavior and data contracts remain shared.

Success means that a user can install the native package, grant the required capture permission, sign in to Google, capture a region on one or more displays, annotate it, save or copy it, upload it to Drive, and recover History or failed uploads after restarting the application.

## 2. Scope

### Included

- One Avalonia desktop UI shared by Windows and macOS.
- Region capture across multiple displays and mixed scale factors.
- Frozen-screen selection overlay with cancel, confirm, physical-pixel size, and keyboard adjustment.
- Pen, highlight, text, blur, crop, output resize, line, arrow, rectangle, ellipse, and triangle tools.
- Layer selection, movement, deletion, and undo/redo of at least 100 operations.
- Clipboard and PNG/JPEG export.
- Local History, state filters, retry, and safe operational logs.
- Google OAuth Authorization Code with PKCE and Google Drive upload/share.
- Tray or menu-bar operation, configurable global hotkey, single instance, and optional launch at sign-in.
- Windows x64, macOS arm64, and macOS x64 release artifacts.
- macOS Screen Recording permission onboarding and recovery instructions.

### Deferred

- Linux distribution.
- One universal macOS binary; arm64 and x64 packages are released separately.
- Video/GIF, OCR, scrolling capture, browser extensions, and automatic updater.
- Editable project persistence after the editor closes.
- Mac App Store sandbox distribution.
- Remote Drive deletion/download and bulk retry; existing local History and per-item retry remain in scope.

## 3. Migration Strategy

The existing `Wocel.Capture.Core` remains the source of truth for domain models, editor commands, persistence, logging, OAuth helpers, Drive REST, and upload state. The current WPF project remains buildable during migration until the Avalonia application reaches parity. Release documentation will switch to Avalonia only after both Windows and macOS acceptance checks pass.

New components:

- `Wocel.Capture.Desktop`: Avalonia application, views, cross-platform rendering, clipboard, dialogs, and orchestration.
- `Wocel.Capture.Platform`: platform-neutral contracts and runtime selection.
- `Wocel.Capture.Platform.Windows`: Windows capture, hotkey, credential protection, startup, and native coordinate services.
- `Wocel.Capture.Platform.Mac`: macOS capture permission, capture, hotkey, Keychain, startup, and native coordinate services.

The WPF implementation is not used as a shared UI library. Behavior is migrated feature by feature behind tested interfaces, preventing WPF types from entering Core or platform contracts.

## 4. Architecture

```text
Wocel.Capture.Desktop (Avalonia)
  ├── Wocel.Capture.Core
  └── Wocel.Capture.Platform
        ├── Windows adapter
        └── macOS adapter
```

### Portable contracts

Platform services expose only portable records and byte buffers:

- `IScreenCaptureService`: permission status, request permission, enumerate displays, capture a frozen virtual desktop.
- `IGlobalHotkeyService`: validate, register, unregister, and report conflicts.
- `IProtectedTokenStore`: save, load, and delete the Google refresh token.
- `IStartupService`: read and update launch-at-sign-in state.
- `ISingleInstanceService`: activate the existing process.
- `IPlatformPaths`: app data, cache, log, and Pictures defaults.

`CapturedFrame` contains width, height, stride, pixel format, physical virtual-desktop bounds, and immutable pixel bytes. It does not reference WPF, Avalonia, CoreGraphics, or GDI types.

### Rendering

The editor continues to store source pixels and immutable logical layers in Core. A cross-platform raster renderer converts the source plus layers into an Avalonia bitmap for preview and encoded PNG/JPEG bytes for export. Preview scale is independent from source coordinates. Crop and output resize remain reversible layers.

Rendering must be deterministic across Windows and macOS for geometry, opacity, and crop/resize mapping. Platform font rasterization may differ, but text bounds and export dimensions must remain stable.

## 5. Platform Integration

### Windows

- Capture the physical virtual desktop using the existing GDI implementation.
- Register the hotkey with `RegisterHotKey`; default is `PrintScreen`.
- Protect refresh tokens with current-user DPAPI.
- Configure startup through the current-user Run key.
- Keep the Per-Monitor-V2 manifest and physical-coordinate rules.
- Use Avalonia `TrayIcon` and native menu instead of WinForms `NotifyIcon`.

### macOS

- Minimum supported version is macOS 13 Ventura.
- Use native screen-capture APIs with ScreenCaptureKit as the preferred implementation. A small signed native shim may expose the required still-frame operations to .NET without adopting the `net10.0-macos` workload.
- Declare `NSScreenCaptureUsageDescription` in `Info.plist`.
- Check permission before capture. When missing, request it, explain that macOS may require System Settings and an application restart, and provide an “Open Screen Recording Settings” action.
- Do not repeatedly trigger the system prompt after denial.
- Register a global hotkey through native macOS APIs; default is `Control+Shift+4`, avoiding the system `Command+Shift+4` shortcut.
- Store the refresh token as a generic password in the user’s Keychain, scoped to service `com.wocel.capture`.
- Implement launch at sign-in with a per-user LaunchAgent for the non-App-Store package.
- Use Avalonia tray/menu-bar support and native application menu conventions.

## 6. Capture and Coordinate Model

All capture and editor geometry uses physical pixels. Each display record includes its physical bounds, logical bounds, and scale factor. Negative origins are valid on both operating systems.

The frozen desktop is represented as one physical-pixel canvas. The overlay may use one top-level transparent window per display so each window follows its display scale correctly. A shared selection controller combines pointer positions into virtual-desktop physical coordinates, allowing a selection to cross display boundaries.

Arrow keys move the normalized selection by one physical pixel; holding Shift moves it by ten. The selection is clamped to the virtual desktop and cannot be smaller than 2×2 pixels.

## 7. Shared User Experience

- First run opens setup instead of silently hiding in the tray.
- Setup explains local storage, Google Drive, and that “public link” means anyone with the link can view the image.
- macOS setup also includes Screen Recording permission status.
- The main window retains History, Activity Log, and Settings tabs.
- The editor keeps the current light toolbar and dark image canvas, with platform-native `Control`/`Command` shortcut labels.
- Export disables document mutation until its durable local commit completes. Network upload continues through the durable queue.
- Closing with edits newer than the last durable export asks for confirmation.
- Status is always communicated with text or icons, never color alone.

## 8. Google Authorization and Storage

The existing browser OAuth flow, PKCE/state validation, `drive.file` scope, folder management, upload/share behavior, and retry identity are shared unchanged.

Refresh-token storage is selected at runtime:

- Windows: DPAPI, current user.
- macOS: Keychain, current user.

OAuth configuration remains a public desktop Client ID supplied through `WOCEL_GOOGLE_CLIENT_ID` or a packaged configuration file. No client secret is embedded. Loopback callbacks bind only to `127.0.0.1`, validate state before exchange, and time out.

History and settings use the same JSON schema across operating systems. Platform-specific default paths are resolved by `IPlatformPaths`; records created by one OS remain readable if copied to another.

## 9. Failure and Recovery

- Permission denial never crashes capture; the UI shows the exact recovery action.
- Hotkey conflicts leave tray/menu capture available and identify the conflicting shortcut.
- A capture failure leaves History unchanged and writes a redacted diagnostic event.
- Clipboard/save errors retain the active editor document.
- Upload completion is persisted before public sharing; retries reuse a known Drive file ID.
- Logging failure cannot roll back successful business state.
- Interrupted uploads return to Pending and resume through bounded startup backoff.
- Keychain/DPAPI failures are reported without logging token material.
- A corrupt primary JSON file recovers from the last validated backup.

## 10. Security and Privacy

- Capture only after explicit user action through the hotkey, tray/menu command, or button.
- Use least-privilege Google `drive.file` scope.
- Store refresh tokens only in DPAPI or Keychain; access tokens remain memory-only.
- Never log pixels, clipboard contents, OAuth codes, tokens, authorization headers, or raw credential-bearing responses.
- Public sharing is explicitly labeled per capture and in automatic-upload settings.
- Only Google HTTPS upload-session hosts with an exact `googleapis.com` boundary are accepted.
- macOS signing identity must remain stable so Screen Recording permission does not reset between builds.

## 11. Packaging

### Windows

- Publish self-contained `win-x64` single-file executable.
- Produce the existing per-user Inno Setup installer with Start Menu, optional desktop shortcut, startup cleanup, and uninstall.
- Keep the package Authenticode-signing ready.

### macOS

- Publish self-contained `osx-arm64` and `osx-x64` outputs.
- Assemble a valid `Wocel Capture.app` containing executable, resources, native libraries, `Info.plist`, icon, and permission description.
- Produce separate arm64 and x64 DMGs.
- Local development builds may use a stable Apple Development identity. Public release builds require Developer ID signing, hardened runtime, notarization, and stapling.
- Build scripts fail clearly when signing/notarization credentials are absent; they may still produce an explicitly labeled unsigned local-test artifact.

## 12. Testing and Acceptance

### Automated

- Preserve all current Core tests.
- Add contract tests run against fake Windows and macOS adapters.
- Add pixel-coordinate tests for negative origins, 1×/1.5×/2×/3× scaling, and cross-display selections.
- Add renderer golden tests for all annotation types, crop, resize, and reverse-direction arrows.
- Add permission-state, hotkey validation, platform-path, token-store error mapping, startup recovery, and packaging-manifest tests.
- Add Avalonia headless tests for first run, History, Settings, tool selection, export busy state, and accessibility labels.

### Windows acceptance

- Windows 10 and 11 x64 clean machines.
- Single and mixed-DPI multi-monitor capture.
- Hotkey, tray, startup, clipboard, all editor tools, Google login, upload/share/retry, installer, and uninstall.

### macOS acceptance

- Apple Silicon and Intel machines, macOS 13 or newer.
- First permission request, denial, later grant through System Settings, and stable permission after signed rebuild.
- Retina plus external non-Retina display capture, including negative display placement and cross-display selection.
- Global hotkey, menu-bar item, launch at login, clipboard, all editor tools, Keychain persistence/logout, Google login, upload/share/retry.
- Gatekeeper launch of signed/notarized `.app`, DMG install, and clean removal.

## 13. Release Gate

The Avalonia application becomes the primary Wocel Capture release only when:

- Core and Avalonia automated suites pass.
- Both Windows and macOS packages build from clean staging directories.
- No secrets appear in source or packaged output.
- A reviewer finds no Critical or Important issue.
- The available Windows and macOS acceptance matrices pass, with any unavailable physical configuration explicitly recorded rather than inferred from unit tests.

