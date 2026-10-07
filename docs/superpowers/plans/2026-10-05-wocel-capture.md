# Wocel Capture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Windows screenshot application that captures a region, annotates it with highlight and basic 2D shapes, keeps History and safe logs, and uploads shareable images to Google Drive.

**Architecture:** A portable `Wocel.Capture.Core` library owns editor/history/log/upload behavior behind narrow interfaces. A `net10.0-windows` WPF executable provides Win32 capture, global shortcut, tray, rendering, OAuth, Google Drive, and installer integration; durable local state uses atomically replaced JSON.

**Tech Stack:** C# 14, .NET 10, WPF, Win32/GDI P/Invoke, System.Text.Json, HttpClient, xUnit, Inno Setup

**Spec:** `docs/superpowers/specs/2026-10-05-wocel-capture-design.md`

## Global Constraints

- Support Windows 10/11 x64 and publish self-contained.
- Keep `Wocel.Capture.Core` on `net10.0` without Windows-only references.
- Use OAuth Authorization Code + PKCE in the system browser and request only `https://www.googleapis.com/auth/drive.file`.
- Never log bitmap data, OAuth codes, tokens, authorization headers, or raw credential-bearing responses.
- Keep the immutable source image and represent editor changes as layers with at least 100 undo operations.
- Do not initialize or commit to git: the supplied workspace has no usable git repository metadata.

## Review Focus

- Mixed-DPI/negative virtual-screen coordinates must map selection pixels correctly; cover conversion math in Task 5 and Windows verification.
- Corrupt or concurrently interrupted JSON must preserve the last valid History state; cover atomic replacement and recovery in Task 3.
- OAuth callbacks with wrong/expired state must fail without exchanging a code; cover in Task 7.
- Workspace accounts that reject `anyone` permissions must retain an uploaded private image; cover in Task 7.
- Logs receiving secrets inside nested exception/HTTP text must redact them; cover in Task 3.

---

### Task 1: Solution Skeleton and Domain Models

**Files:**
- Create: `src/Wocel.Capture.Core/Wocel.Capture.Core.csproj`
- Create: `src/Wocel.Capture.Core/Models/CaptureRecord.cs`
- Create: `src/Wocel.Capture.Core/Models/CaptureSettings.cs`
- Create: `src/Wocel.Capture.Core/Models/Geometry.cs`
- Create: `tests/Wocel.Capture.Tests/Wocel.Capture.Tests.csproj`
- Create: `tests/Wocel.Capture.Tests/ModelTests.cs`
- Modify: `Wocel.sln`

**Interfaces:**
- Produces: `CaptureRecord`, `CaptureSettings`, `PixelPoint`, `PixelSize`, `PixelRect`, `UploadState`, and `ImageFormat`.

- [ ] Write failing tests for invalid dimensions, rectangle normalization, default PNG/PrintScreen settings, and all upload-state values.
- [ ] Run `dotnet test tests/Wocel.Capture.Tests/Wocel.Capture.Tests.csproj` and verify failure because model types do not exist.
- [ ] Implement immutable validated model records and settings defaults.
- [ ] Run the model tests and verify they pass.

### Task 2: Non-Destructive Editor and Undo/Redo

**Files:**
- Create: `src/Wocel.Capture.Core/Editor/EditorLayer.cs`
- Create: `src/Wocel.Capture.Core/Editor/EditorDocument.cs`
- Create: `src/Wocel.Capture.Core/Editor/EditorCommand.cs`
- Create: `tests/Wocel.Capture.Tests/EditorDocumentTests.cs`

**Interfaces:**
- Consumes: `PixelPoint`, `PixelRect`, and `PixelSize` from Task 1.
- Produces: `EditorDocument.Execute(IEditorCommand)`, `Undo()`, `Redo()`, `Reset()`, layer records for pen/highlight/text/blur/line/arrow/rectangle/ellipse/triangle, and `ShapeStyle`.

- [ ] Write failing tests for each layer kind, highlight opacity clamping, add/move/delete commands, undo/redo, redo invalidation, reset, and the 100-command retention limit.
- [ ] Run the editor tests and verify failure because editor types do not exist.
- [ ] Implement immutable layers plus reversible commands and a bounded command history.
- [ ] Run editor tests and verify they pass.

### Task 3: Durable History, Settings, and Safe Activity Logs

**Files:**
- Create: `src/Wocel.Capture.Core/Persistence/Repositories.cs`
- Create: `src/Wocel.Capture.Core/Persistence/JsonFileStore.cs`
- Create: `src/Wocel.Capture.Core/Logging/ActivityLog.cs`
- Create: `src/Wocel.Capture.Core/Export/CaptureFileNamer.cs`
- Create: `tests/Wocel.Capture.Tests/PersistenceTests.cs`
- Create: `tests/Wocel.Capture.Tests/ActivityLogTests.cs`
- Create: `tests/Wocel.Capture.Tests/CaptureFileNamerTests.cs`

**Interfaces:**
- Consumes: domain records from Task 1.
- Produces: `IHistoryRepository`, `ISettingsRepository`, `IActivityLog`, JSON implementations, `SensitiveDataRedactor.Redact(string)`, and `CaptureFileNamer.NextAvailable(...)`.

- [ ] Write failing tests for JSON round-trip, atomic replacement, recovery from a corrupt new file using the previous valid file, and concurrent serialization through one repository instance.
- [ ] Write failing tests that redact bearer tokens, OAuth `code`, refresh/access tokens, authorization headers, and nested secret-bearing error strings while retaining safe diagnostics.
- [ ] Write failing tests for 30-day log retention and deterministic `-2`/`-3` filename collisions.
- [ ] Implement the repositories, redactor, rotating NDJSON logger, and filename service.
- [ ] Run Task 3 tests and verify they pass.

### Task 4: Upload Queue and Drive Contracts

**Files:**
- Create: `src/Wocel.Capture.Core/Cloud/DriveContracts.cs`
- Create: `src/Wocel.Capture.Core/Cloud/UploadQueue.cs`
- Create: `tests/Wocel.Capture.Tests/UploadQueueTests.cs`

**Interfaces:**
- Consumes: `CaptureRecord`, `UploadState`, `IHistoryRepository`, and `IActivityLog`.
- Produces: `IDriveClient.UploadAndShareAsync(DriveUploadRequest, CancellationToken)`, `DriveUploadResult`, `UploadQueue.EnqueueAsync`, `RetryAsync`, and `RecoverInterruptedAsync`.

- [ ] Write failing tests for `Pending → Uploading → UploadedShared`, private upload after sharing-policy rejection, failure/retry, cancellation, and startup recovery of `Uploading` to `Pending`.
- [ ] Run upload queue tests and verify failure because contracts do not exist.
- [ ] Implement the serialized durable queue with bounded exponential retry metadata and explicit state transitions.
- [ ] Run upload queue tests and verify they pass.

### Task 5: Windows Shell, Capture Overlay, and Local Export

**Files:**
- Create: `src/Wocel.Capture.Windows/Wocel.Capture.Windows.csproj`
- Create: `src/Wocel.Capture.Windows/App.xaml`
- Create: `src/Wocel.Capture.Windows/App.xaml.cs`
- Create: `src/Wocel.Capture.Windows/Native/NativeMethods.cs`
- Create: `src/Wocel.Capture.Windows/Capture/ScreenCaptureService.cs`
- Create: `src/Wocel.Capture.Windows/Capture/SelectionOverlay.xaml`
- Create: `src/Wocel.Capture.Windows/Capture/SelectionOverlay.xaml.cs`
- Create: `src/Wocel.Capture.Windows/Services/HotkeyService.cs`
- Create: `src/Wocel.Capture.Windows/Services/SingleInstanceService.cs`
- Create: `src/Wocel.Capture.Windows/Services/ImageExportService.cs`
- Create: `tests/Wocel.Capture.Tests/CoordinateMapperTests.cs`

**Interfaces:**
- Consumes: geometry, settings, repositories, and logger from Tasks 1 and 3.
- Produces: `IScreenCaptureService.CaptureVirtualDesktop()`, `CoordinateMapper`, overlay `Selected` event, hotkey registration result, and local save/clipboard operations.

- [ ] Write failing cross-platform tests for negative-origin virtual screens and 100%, 150%, and 300% DPI conversions.
- [ ] Implement and pass `CoordinateMapper` tests.
- [ ] Implement per-monitor-V2 process setup, GDI virtual-screen snapshot, frozen-image selection overlay, keyboard adjustment, and 2×2 minimum selection.
- [ ] Implement single instance, `RegisterHotKey`, clipboard PNG/bitmap, PNG/JPG export, and graceful WebP unavailability.
- [ ] Build `src/Wocel.Capture.Windows/Wocel.Capture.Windows.csproj` with Windows targeting enabled and resolve all compile errors.

### Task 6: WPF Editor, History, Logs, and Settings UI

**Files:**
- Create: `src/Wocel.Capture.Windows/Views/MainWindow.xaml`
- Create: `src/Wocel.Capture.Windows/Views/MainWindow.xaml.cs`
- Create: `src/Wocel.Capture.Windows/Views/EditorWindow.xaml`
- Create: `src/Wocel.Capture.Windows/Views/EditorWindow.xaml.cs`
- Create: `src/Wocel.Capture.Windows/ViewModels/MainViewModel.cs`
- Create: `src/Wocel.Capture.Windows/ViewModels/EditorViewModel.cs`
- Create: `src/Wocel.Capture.Windows/Rendering/EditorRenderer.cs`
- Create: `src/Wocel.Capture.Windows/Services/TrayService.cs`
- Create: `tests/Wocel.Capture.Tests/EditorViewModelTests.cs`

**Interfaces:**
- Consumes: editor model, History, logs, settings, queue, capture, and export services.
- Produces: capture/editor workflow and tabs for History, Activity Log, and Settings.

- [ ] Write failing view-model tests for tool selection, auto-upload override, unsaved-close state, history filters, retry command availability, and safe log filtering.
- [ ] Implement view models and pass tests without WPF dependencies in command/state logic.
- [ ] Implement the editor canvas and toolbar for crop, pen, highlight, text, blur, resize, and all five 2D shapes with style controls and keyboard shortcuts.
- [ ] Implement History, Activity Log, Settings, tray commands, and first-run UI with accessible labels and non-color status text.
- [ ] Build the Windows project and perform a static XAML/resource check.

### Task 7: Google OAuth and Drive REST Adapter

**Files:**
- Create: `src/Wocel.Capture.Core/Cloud/Pkce.cs`
- Create: `src/Wocel.Capture.Windows/Cloud/GoogleOAuthService.cs`
- Create: `src/Wocel.Capture.Windows/Cloud/GoogleDriveClient.cs`
- Create: `src/Wocel.Capture.Windows/Security/DpapiTokenStore.cs`
- Create: `tests/Wocel.Capture.Tests/PkceTests.cs`
- Create: `tests/Wocel.Capture.Tests/GoogleDriveProtocolTests.cs`

**Interfaces:**
- Consumes: `IDriveClient`, logger, queue, and protected token storage.
- Produces: browser OAuth login/logout, refresh, folder discovery/creation, resumable image upload, `anyone/reader` sharing, private-upload fallback, and `webViewLink`.

- [ ] Write failing deterministic PKCE/state validation tests, including mismatched and expired callback state.
- [ ] Write failing fake-HTTP tests for token refresh, folder query escaping, resumable upload, sharing success, Workspace sharing rejection, 401 reauthentication, and sanitized error mapping.
- [ ] Implement PKCE helpers and pass core tests.
- [ ] Implement loopback OAuth with system-browser launch and DPAPI-protected refresh-token storage.
- [ ] Implement Drive v3 REST calls and pass protocol tests without live credentials.
- [ ] Wire sign-in state and upload actions into the WPF UI.

### Task 8: Installer, Release Build, and End-to-End Verification

**Files:**
- Create: `installer/WocelCapture.iss`
- Create: `scripts/build-capture-release.ps1`
- Create: `docs/wocel-capture-google-setup.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: the finished Windows executable and build-time `WOCEL_GOOGLE_CLIENT_ID`.
- Produces: `dist/wocel-capture/WocelCaptureSetup.exe` on a Windows machine with Inno Setup.

- [ ] Add a PowerShell release script that tests core, publishes `win-x64` self-contained, verifies required files and absence of token-like strings, then invokes Inno Setup when available.
- [ ] Add an installer with per-user install, Start Menu shortcut, uninstall, and no forced startup entry.
- [ ] Document Google Cloud OAuth desktop-client creation, Drive API enablement, consent-screen publication, client-ID injection, and real-account smoke test.
- [ ] Update README with build/run/test commands and current platform limitations.
- [ ] Run all cross-platform tests and compile checks available in the current environment.
- [ ] On Windows, verify capture on mixed DPI, each editor tool, clipboard/save, real Drive upload/share, retry, installer, clean uninstall, and clean-VM self-contained launch.

