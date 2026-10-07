# Wocel Capture — Product and Technical Design

**Status:** Approved for implementation by delegated self-review  
**Date:** 2026-10-05  
**Platform:** Windows 10/11 x64  
**Distribution:** Self-contained installer `.exe`

## 1. Purpose

Wocel Capture is a standalone Windows screenshot application. It stays available in the system tray, captures a user-selected region through a configurable global shortcut, provides a non-destructive annotation editor, and can save or copy the result locally or upload it to the user's Google Drive and copy a shareable link.

The first release is successful when a user can install the app, sign in to Google once, capture and annotate a region across mixed-DPI displays, retrieve earlier captures from History, inspect safe operational logs, and recover cleanly from network or authentication failures.

## 2. Scope

### Included

- Single-instance Windows desktop application.
- System tray and configurable global shortcut; default is `Print Screen` when available.
- Region capture across multiple monitors and mixed DPI settings.
- Frozen-screen selection overlay with pixel dimensions, keyboard adjustment, confirm, and cancel.
- Non-destructive editor with crop, pen, highlight, text, blur, resize, and 2D shapes.
- 2D shapes: line, arrow, rectangle, ellipse, and triangle.
- Per-tool color, stroke width, fill color, opacity, and text size where applicable.
- At least 100 undo/redo operations during the active edit session.
- Export to clipboard or local PNG, JPG, and WebP.
- Google OAuth sign-in and Google Drive upload.
- Configurable automatic upload, overridable for each capture.
- Local History with Drive state and retry controls.
- Local activity log with filtering and safe error details.
- Start Menu entry, uninstall support, and optional launch at sign-in.

### Excluded from the first release

- Scrolling capture, video/GIF recording, OCR, and browser extensions.
- Team workspaces or a Wocel-hosted cloud backend.
- Full editable-project persistence after the editor closes.
- Automatic application updates.
- macOS and Linux packages.

## 3. Primary User Flow

1. On first launch, the app explains its local and Google Drive behavior.
2. The user authorizes the app in their system browser.
3. The app stores the refresh token protected for the current Windows user.
4. The user chooses the global shortcut, local save folder, automatic upload preference, and launch-at-sign-in preference.
5. The app remains in the tray.
6. A shortcut or tray command freezes the virtual desktop and opens the selection overlay.
7. The user selects and confirms a region.
8. The editor opens with the original bitmap and vector/effect layers.
9. The user copies, saves, or finishes with optional Drive upload.
10. The app records History and activity-log entries and returns to the tray.

## 4. Architecture

The solution uses .NET 10 and separates portable domain code from Windows-specific WPF code.

### `Wocel.Capture.Core`

A `net10.0` class library containing:

- Capture and history records, upload state, settings, and log events.
- Editor document, layers, shape definitions, and command-based undo/redo.
- Render-independent geometry and validation.
- History repository and activity logger contracts.
- Drive client, token protector, clock, file store, and upload queue contracts.
- Retry policy and deterministic file-name generation.

The core library must not reference WPF or Windows-only assemblies.

### `Wocel.Capture.Windows`

A `net10.0-windows` WPF application containing:

- App lifecycle, single-instance activation, tray icon, settings, and first-run flow.
- Win32 global-hotkey registration.
- DPI-aware virtual-desktop capture and selection overlay.
- Editor canvas and layer rendering.
- Windows clipboard, DPAPI token storage, startup registration, and notifications.
- Google OAuth browser launch, local loopback callback, and Drive REST adapter.
- JSON-backed local repositories for the first release, with atomic file replacement.

JSON persistence is selected instead of SQLite for the MVP to avoid a native database dependency in the installer. Repository interfaces preserve a future migration path without changing the UI or domain model.

### `Wocel.Capture.Tests`

A cross-platform xUnit project covering the core library. Windows-only UI and native integration checks run on a Windows CI or release machine.

## 5. Capture and Selection

- The process declares per-monitor V2 DPI awareness before creating UI.
- Capture coordinates use physical pixels. UI coordinates are converted per monitor.
- The capture service takes a snapshot of the complete virtual desktop before showing the overlay, preventing the overlay from appearing in the result.
- The overlay spans all monitors, darkens the frozen image, and reveals the selected region.
- Drag creates the selection. Arrow keys move by one pixel, `Shift` + arrow moves by ten pixels, `Enter` confirms, and `Esc` cancels.
- A selection smaller than 2×2 pixels is rejected without opening the editor.
- If the default shortcut is occupied, the app remains usable and asks the user to choose another shortcut.

## 6. Editor Model

An editor document owns an immutable source bitmap, ordered layers, the current selection, and undo/redo stacks. Each user action is a command with `Apply` and `Revert` behavior. Rendering composites the source and visible layers only when previewing or exporting.

Layer types:

- Freehand stroke.
- Highlighter stroke with bounded opacity.
- Text.
- Blur region.
- Line, arrow, rectangle, ellipse, and triangle.
- Crop and output-resize operations.

Shapes support selection, movement, resizing, deletion, stroke color and width, optional fill, and opacity. Highlight is represented as a dedicated layer rather than drawing directly on the bitmap. Undo/redo retains at least 100 commands and discards the redo branch after a new edit.

The editor exports at source-pixel resolution regardless of preview zoom. Reset returns to the original capture through one confirmable command.

## 7. Export and Google Drive

### Local export

- PNG is the default and lossless format.
- JPG supports a user-configurable quality setting.
- WebP is available when the Windows encoder supports it; otherwise the UI explains that it is unavailable and keeps PNG selected.
- Default name: `Screenshot_yyyy-MM-dd_HH-mm-ss.png`; collisions receive `-2`, `-3`, and so on.
- Clipboard export provides a bitmap and PNG data when the Windows clipboard API permits both.

### Authorization

- OAuth 2.0 Authorization Code with PKCE is completed in the system browser.
- A random loopback listener on `127.0.0.1` receives the callback.
- The app validates `state`, uses an S256 PKCE challenge, and never treats a desktop client secret as confidential.
- Scope is limited to `https://www.googleapis.com/auth/drive.file`.
- Refresh tokens are encrypted with Windows DPAPI for the current user. Access tokens remain in memory and are never logged.
- Signing out revokes the grant when possible and always removes the local protected token. It does not delete Drive files.

### Upload and sharing

- The app creates and reuses a `Wocel Capture` folder that it created in the user's My Drive.
- Upload uses resumable upload for robustness.
- After upload, the app attempts to create an `anyone` / `reader` permission and retrieves `webViewLink`.
- If an organization forbids public links, upload remains successful and private; the UI reports that link sharing is blocked by the account policy.
- A successful public link is copied to the clipboard and stored in History.
- Automatic upload is a setting and can be overridden on every capture.

## 8. History and Activity Log

### History

Each capture record includes:

- Local capture ID, display name, created time, dimensions, format, and byte size.
- Optional local path and thumbnail-cache path.
- Drive file ID, Drive web link, and upload state: `LocalOnly`, `Pending`, `Uploading`, `UploadedPrivate`, `UploadedShared`, or `Failed`.
- Sanitized last error code and retry count.

History supports search by name/date, state filters, open, copy link, download, retry, and delete. Deletion asks whether to remove only the local History entry or also delete the Drive file. Drive deletion is never implied.

The source of truth for app-created metadata is the local history repository. On startup and manual refresh, the app reconciles pending items and Drive IDs known to the app; it does not scan unrelated Drive content.

### Activity log

Events include app start/stop, sign-in/out, capture, copy, save, upload, sharing, download, delete, retry, and failures. Each entry contains timestamp, correlation ID, operation, duration, result, image dimensions/size when relevant, and a sanitized error code/message.

Logs must never contain bitmap content, local OAuth callback codes, access tokens, refresh tokens, authorization headers, or raw Google responses that can contain credentials. Logs rotate by date and retain 30 days by default. The app provides filters, copy-safe diagnostic details, and a clear-log action.

## 9. Failure and Recovery Behavior

- Pending and failed uploads are durable and can be retried individually or in bulk.
- The app refreshes expired access tokens. Revoked grants transition to `AuthenticationRequired` and prompt sign-in without deleting local data.
- An interrupted upload returns to `Pending` on the next launch.
- Repository writes use a temporary file followed by atomic replacement to prevent partial JSON files.
- Clipboard or local-save failure leaves the editor open.
- Capture failure displays an actionable message and records only safe diagnostics.
- Closing an editor with unexported changes asks for confirmation.
- No uncertain Drive response triggers an automatic delete or overwrite.

## 10. Security and Privacy

- Least-privilege `drive.file` scope.
- System browser only; no embedded login view.
- PKCE S256, unpredictable `state`, fixed loopback address, random port, and a short callback timeout.
- DPAPI protection for refresh tokens and restrictive local file access under the current profile.
- HTTPS-only Google endpoints.
- No analytics or Wocel-hosted upload in the first release.
- Public sharing is explicit in onboarding and settings; each capture exposes its pending sharing choice before finish.
- User-provided text and file names are normalized and never concatenated into shell commands.

## 11. Performance and Accessibility

- Overlay target: visible within 300 ms of shortcut activation on a typical Windows 11 desktop.
- Editor target: responsive interaction with a 4K capture and 100 layers.
- Upload does not block capture or editing.
- All primary editor actions have keyboard access and tooltips.
- Controls expose accessible names; status is not communicated by color alone.
- Default colors meet WCAG AA contrast where applicable.

## 12. Packaging

- The Windows project publishes self-contained for `win-x64`.
- An Inno Setup script produces a signed-ready installer `.exe` with Start Menu entry and uninstall support.
- Launch at sign-in is opt-in and can be changed in Settings.
- The installer and executable are designed for Authenticode signing, though signing credentials are a release-time concern.
- OAuth client ID configuration is injected at build time and is not considered a secret.

## 13. Verification and Acceptance Criteria

Automated core tests cover:

- Shape bounds and editor validation.
- Highlight opacity constraints.
- Command apply/revert, 100-operation history, redo invalidation, and reset.
- File naming and collision handling.
- Capture-history state transitions.
- Safe activity-log redaction and retention.
- Queue persistence, retry/backoff, cancellation, and crash recovery.
- OAuth state/PKCE helpers and Drive error mapping using fake HTTP handlers.

Windows verification covers:

- Single and mixed-DPI multi-monitor selection.
- Global shortcut registration and conflict behavior.
- All editor tools, keyboard shortcuts, copy, and file formats.
- Real Google authorization, upload, public link, Workspace public-link restriction, logout, and revoked access.
- Installer, Start Menu, optional startup, uninstall, and self-contained launch on a clean Windows 10/11 VM.

Release acceptance requires no secrets in logs or published output, no loss of the active edit on recoverable export errors, and a traceable History state for every upload attempt.

## 14. Implementation Order

1. Core models, editor commands, repositories, logging, and tests.
2. WPF shell, lifecycle, tray, settings, and History views.
3. Native capture, overlay, clipboard, and local export.
4. Editor canvas and tools.
5. OAuth, Drive upload/share, durable retry, and reconciliation.
6. Installer and Windows release verification.

