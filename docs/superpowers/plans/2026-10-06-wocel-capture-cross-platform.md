# Wocel Capture Cross-Platform Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Windows-only presentation layer with one Avalonia desktop application and ship native Windows and macOS packages while preserving Wocel Capture’s editor, History, logs, and Google Drive behavior.

**Architecture:** `Wocel.Capture.Core` remains portable and gains platform-neutral capture/hotkey contracts. `Wocel.Capture.Desktop` owns the shared Avalonia UI and SkiaSharp renderer; Windows and macOS adapter projects implement native capture, credentials, hotkeys, startup, paths, and single-instance behavior. The WPF project remains intact until the Avalonia release passes both operating-system acceptance gates.

**Tech Stack:** C# 14, .NET 10, Avalonia 12.1.2, Avalonia.Headless.XUnit 12.1.2, SkiaSharp 4.153.1, Win32/GDI, macOS ScreenCaptureKit/CoreGraphics/Security frameworks, xUnit, Inno Setup, shell/PowerShell packaging scripts

**Spec:** `docs/superpowers/specs/2026-10-06-wocel-capture-cross-platform-design.md`

## Global Constraints

- Support Windows 10/11 x64 and macOS 13+ on Apple Silicon and Intel.
- Publish `win-x64`, `osx-arm64`, and `osx-x64`; macOS arm64 and x64 packages remain separate.
- Keep `Wocel.Capture.Core` and platform contracts free of WPF, Avalonia, GDI, and CoreGraphics types.
- Use physical pixels for capture/editor geometry; negative virtual-desktop origins are valid.
- Preserve OAuth Authorization Code + PKCE, `drive.file`, explicit public-link consent, durable Drive file identity, and safe-log rules.
- Store refresh tokens only with current-user DPAPI on Windows or Keychain service `com.wocel.capture` on macOS.
- Do not remove the WPF project until the Avalonia Windows acceptance matrix passes.
- The workspace has no usable git metadata; replace per-task commits with entries in `.superpowers/sdd/2026-10-06-wocel-capture-cross-platform/progress.md`.

## Review Focus

- A Retina display beside a non-Retina display, including a negative origin, must select the same physical pixels shown in the frozen overlay; Task 4 owns the cross-display tests.
- A denied or stale macOS Screen Recording grant must produce one actionable recovery flow without repeated prompts or capture attempts; Task 7 owns the permission-state tests.
- A copied/saved export with automatic upload must freeze document mutation only until the durable local snapshot exists, then allow network work to continue safely; Task 5 owns the workflow tests.
- A previously uploaded Drive file whose sharing call times out must retry permission creation against the same file ID on either OS; Task 5 re-runs the protocol/queue regression tests.
- macOS packaging must not reset Screen Recording permission by changing application identity between signed builds; Task 8 verifies bundle ID, signing identity, entitlements, and `Info.plist` stability.

---

### Task 1: Portable Platform Contracts and Runtime Composition

**Files:**
- Create: `src/Wocel.Capture.Platform/Wocel.Capture.Platform.csproj`
- Create: `src/Wocel.Capture.Platform/Capture/CapturedFrame.cs`
- Create: `src/Wocel.Capture.Platform/Capture/ScreenCaptureContracts.cs`
- Create: `src/Wocel.Capture.Platform/Hotkeys/HotkeyGesture.cs`
- Create: `src/Wocel.Capture.Platform/Services/PlatformContracts.cs`
- Create: `src/Wocel.Capture.Platform/PlatformServiceSet.cs`
- Create: `tests/Wocel.Capture.Platform.Tests/Wocel.Capture.Platform.Tests.csproj`
- Create: `tests/Wocel.Capture.Platform.Tests/PlatformContractTests.cs`
- Modify: `src/Wocel.Capture.Core/Models/GlobalHotkey.cs`
- Modify: `Wocel.sln`

**Interfaces:**
- Consumes: `PixelPoint`, `PixelRect`, and `PixelSize` from Core.
- Produces: `CapturedFrame`, `DisplayGeometry`, `CapturePermissionStatus`, `IScreenCaptureService`, `IGlobalHotkeyService`, `IProtectedTokenStore`, `IStartupService`, `ISingleInstanceService`, `IPlatformPaths`, and the validated `PlatformServiceSet` aggregate.

- [ ] Write failing tests named `CapturedFrame_rejects_invalid_stride_or_buffer_length`, `DisplayGeometry_maps_logical_points_to_negative_physical_coordinates`, `HotkeyGesture_parses_platform_neutral_modifiers`, and `Platform_service_set_rejects_missing_contracts`.
- [ ] Run `dotnet test tests/Wocel.Capture.Platform.Tests/Wocel.Capture.Platform.Tests.csproj` and verify compile failure because the contract project/types do not exist.
- [ ] Implement `CapturedFrame(int width, int height, int stride, PixelFormat pixelFormat, PixelRect physicalBounds, ReadOnlyMemory<byte> pixels)`, `DisplayGeometry`, and the service interfaces without UI/native references.
- [ ] Implement `HotkeyGesture.TryParse(string, out HotkeyGesture)` with logical `Control`, `Shift`, `Alt`, and `Meta` modifiers; keep a compatibility converter for the existing Windows `GlobalHotkey` tests.
- [ ] Implement `PlatformServiceSet` validation only; runtime selection belongs to Desktop composition after both adapter projects exist, avoiding a dependency cycle.
- [ ] Add both projects to `Wocel.sln`, run the platform tests, and record the passing count in the SDD ledger.

### Task 2: Cross-Platform Raster Renderer and Export Codec

**Files:**
- Create: `src/Wocel.Capture.Rendering/Wocel.Capture.Rendering.csproj`
- Create: `src/Wocel.Capture.Rendering/SkiaEditorRenderer.cs`
- Create: `src/Wocel.Capture.Rendering/ImageCodec.cs`
- Create: `tests/Wocel.Capture.Rendering.Tests/Wocel.Capture.Rendering.Tests.csproj`
- Create: `tests/Wocel.Capture.Rendering.Tests/RendererGeometryTests.cs`
- Create: `tests/Wocel.Capture.Rendering.Tests/ImageCodecTests.cs`
- Create: `tests/Wocel.Capture.Rendering.Tests/Golden/README.md`
- Modify: `Wocel.sln`

**Interfaces:**
- Consumes: `CapturedFrame`, `EditorDocument`, and every Core `EditorLayer` subtype.
- Produces: `RenderedImage Render(CapturedFrame, EditorDocument)` and `byte[] Encode(RenderedImage, CaptureImageFormat, int quality)` with no UI-framework types.

- [ ] Write failing geometry tests for pen, highlight opacity, text, blur, all five shapes, horizontal/reverse arrows, crop, resize, and coordinates after crop+resize. Assert dimensions and selected stable pixels rather than a whole-file hash.
- [ ] Write failing codec tests for PNG round-trip, JPEG quality bounds, unsupported WebP fallback, and alpha preservation.
- [ ] Run both rendering test projects and verify failure because the renderer does not exist.
- [ ] Add explicit `SkiaSharp` 4.153.1 native assets for Windows and macOS and implement the renderer without referencing Avalonia until the adapter boundary.
- [ ] Keep the rendering project free of Avalonia references; Task 3 owns the small UI bitmap adapter.
- [ ] Run rendering tests on the host architecture and add the projects to `Wocel.sln`.

### Task 3: Avalonia Application Shell and Design System

**Files:**
- Create: `src/Wocel.Capture.Desktop/Wocel.Capture.Desktop.csproj`
- Create: `src/Wocel.Capture.Desktop/Program.cs`
- Create: `src/Wocel.Capture.Desktop/App.axaml`
- Create: `src/Wocel.Capture.Desktop/App.axaml.cs`
- Create: `src/Wocel.Capture.Desktop/Composition/AppComposition.cs`
- Create: `src/Wocel.Capture.Desktop/Rendering/AvaloniaBitmapAdapter.cs`
- Create: `src/Wocel.Capture.Desktop/Styles/Colors.axaml`
- Create: `src/Wocel.Capture.Desktop/Styles/Controls.axaml`
- Create: `src/Wocel.Capture.Desktop/Views/MainWindow.axaml`
- Create: `src/Wocel.Capture.Desktop/Views/MainWindow.axaml.cs`
- Create: `src/Wocel.Capture.Desktop/ViewModels/MainWindowViewModel.cs`
- Create: `src/Wocel.Capture.Desktop/Assets/app-icon.ico`
- Create: `src/Wocel.Capture.Desktop/Assets/app-icon.png`
- Create: `tests/Wocel.Capture.Desktop.Tests/Wocel.Capture.Desktop.Tests.csproj`
- Create: `tests/Wocel.Capture.Desktop.Tests/AppShellTests.cs`
- Modify: `Wocel.sln`

**Interfaces:**
- Consumes: Core repositories/queue/logging, `PlatformServices`, and rendering services.
- Produces: an Avalonia 12.1.2 application with explicit-shutdown lifetime, tray/native menu, and `MainWindowViewModel` commands for capture, History, Log, Settings, sign-in, and exit.

- [ ] Write Avalonia headless tests proving the main window exposes History/Log/Settings tabs, capture and Google buttons have accessible names, public-link copy is explicit, and status does not rely only on color.
- [ ] Run `dotnet test tests/Wocel.Capture.Desktop.Tests/Wocel.Capture.Desktop.Tests.csproj` and verify failure because the Avalonia app is absent.
- [ ] Implement the shared shell using the existing teal/light design and dark editor canvas from `design-system/wocel-capture/MASTER.md`, overriding its unsuitable web/mobile typography with platform system fonts.
- [ ] Add Avalonia `TrayIcon`/`NativeMenu` commands for Show, Capture, and Exit; keep first launch visible and later closes tray-resident.
- [ ] Implement composition without service locators in views: construct interfaces in `AppComposition`, inject view models, and expose cancellation for shutdown.
- [ ] Run headless tests, build the Desktop project on macOS, and record the results.

### Task 4: Multi-Display Selection Overlay and Avalonia Editor

**Files:**
- Create: `src/Wocel.Capture.Desktop/Capture/SelectionController.cs`
- Create: `src/Wocel.Capture.Desktop/Views/SelectionOverlayWindow.axaml`
- Create: `src/Wocel.Capture.Desktop/Views/SelectionOverlayWindow.axaml.cs`
- Create: `src/Wocel.Capture.Desktop/ViewModels/SelectionOverlayViewModel.cs`
- Create: `src/Wocel.Capture.Desktop/Views/EditorWindow.axaml`
- Create: `src/Wocel.Capture.Desktop/Views/EditorWindow.axaml.cs`
- Create: `src/Wocel.Capture.Desktop/ViewModels/EditorWindowViewModel.cs`
- Create: `src/Wocel.Capture.Desktop/Controls/EditorCanvas.cs`
- Create: `tests/Wocel.Capture.Desktop.Tests/SelectionOverlayTests.cs`
- Create: `tests/Wocel.Capture.Desktop.Tests/EditorWorkflowTests.cs`

**Interfaces:**
- Consumes: `CapturedFrame`, `DisplayGeometry`, Core editor commands, `SkiaEditorRenderer`, and `EditorCanvasMapper`.
- Produces: `SelectionController.Begin(IReadOnlyList<DisplayGeometry>, CapturedFrame)`, physical `SelectedRegion`, and a shared editor that executes reversible Core commands.

- [ ] Write failing tests for 1×/1.5×/2×/3× displays, negative origins, Retina beside non-Retina, cross-display reverse drag, one/ten physical-pixel keyboard movement, bounds clamping, and 2×2 rejection.
- [ ] Write failing editor tests for tool selection, pen/highlight/text/blur/crop/resize/shapes, horizontal/reverse arrows, select/move/delete, undo/redo dirty revision, export busy state, and stale selection after reset.
- [ ] Run the focused headless tests and verify the expected failures.
- [ ] Implement one overlay window per display backed by one shared physical selection controller; never derive physical pixels from a single window’s scale across all displays.
- [ ] Implement `EditorCanvas` drawing through the shared renderer and route pointer coordinates through Core mapping before executing commands.
- [ ] Implement accessible toolbar controls and platform-appropriate shortcut labels (`Ctrl` on Windows, `⌘` on macOS).
- [ ] Run all Desktop tests and manually preview the Avalonia windows on the current macOS host.

### Task 5: Shared History, Logs, Settings, OAuth, and Export Workflow

**Files:**
- Create: `src/Wocel.Capture.Desktop/Services/CaptureWorkflow.cs`
- Create: `src/Wocel.Capture.Desktop/Services/ExportWorkflow.cs`
- Create: `src/Wocel.Capture.Desktop/Services/GoogleOAuthService.cs`
- Create: `src/Wocel.Capture.Desktop/ViewModels/HistoryViewModel.cs`
- Create: `src/Wocel.Capture.Desktop/ViewModels/ActivityLogViewModel.cs`
- Create: `src/Wocel.Capture.Desktop/ViewModels/SettingsViewModel.cs`
- Create: `src/Wocel.Capture.Desktop/Views/FirstRunWindow.axaml`
- Create: `tests/Wocel.Capture.Desktop.Tests/ExportWorkflowTests.cs`
- Create: `tests/Wocel.Capture.Desktop.Tests/SettingsAndHistoryTests.cs`
- Modify: `src/Wocel.Capture.Core/Models/CaptureSettings.cs`
- Modify: `src/Wocel.Capture.Core/Cloud/UploadQueue.cs`

**Interfaces:**
- Consumes: platform services, Core repositories/logger/Drive client, renderer/codec, and Avalonia clipboard/storage-provider abstractions.
- Produces: durable `CaptureWorkflow`, `ExportWorkflow.CommitAsync(EditorSnapshot, ExportIntent, CancellationToken)`, first-run state, and shared browser OAuth.

- [ ] Write failing workflow tests for clipboard/save errors retaining the editor, auto-upload detaching only after durable local commit, repeated exports receiving unique IDs, public/private disclosure, share timeout reusing Drive ID, and log failure preserving success.
- [ ] Write failing UI-state tests for search/state filters, per-item retry, authentication-required reauthorization, first-run Google prompt, permission status, log copy/redaction, and 30-day prune on startup.
- [ ] Run the focused Core and Desktop tests and verify the new assertions fail first.
- [ ] Implement the workflows and view models; keep browser OAuth and Drive REST portable, injecting only `IProtectedTokenStore` and browser launch.
- [ ] Extend settings with per-platform default hotkeys while preserving backward-compatible JSON deserialization.
- [ ] Run all Capture Core, Platform, Rendering, and Desktop tests.

### Task 6: Windows Native Adapter and Parity Build

**Files:**
- Create: `src/Wocel.Capture.Platform.Windows/Wocel.Capture.Platform.Windows.csproj`
- Create: `src/Wocel.Capture.Platform.Windows/WindowsScreenCaptureService.cs`
- Create: `src/Wocel.Capture.Platform.Windows/WindowsHotkeyService.cs`
- Create: `src/Wocel.Capture.Platform.Windows/WindowsTokenStore.cs`
- Create: `src/Wocel.Capture.Platform.Windows/WindowsStartupService.cs`
- Create: `src/Wocel.Capture.Platform.Windows/WindowsSingleInstanceService.cs`
- Create: `src/Wocel.Capture.Platform.Windows/WindowsPlatformPaths.cs`
- Create: `tests/Wocel.Capture.Platform.Tests/WindowsAdapterContractTests.cs`
- Modify: `src/Wocel.Capture.Desktop/Composition/AppComposition.cs`
- Modify: `Wocel.sln`

**Interfaces:**
- Consumes: Task 1 contracts; behavior may be migrated from `Wocel.Capture.Windows` but no WPF types.
- Produces: `WindowsPlatformServices.Create()` implementing every platform contract.

- [ ] Write failing contract tests around negative virtual bounds, BGRA stride, hotkey flag conversion, DPAPI error mapping, quoted startup path, activation IPC, and expected Windows app-data paths.
- [ ] Migrate GDI capture with strict native-handle cleanup and validate every Win32 return value.
- [ ] Migrate `RegisterHotKey`, DPAPI, Run-key startup, and named-pipe/mutex single instance behind the portable interfaces.
- [ ] Compose the adapter when `OperatingSystem.IsWindows()` and ensure background launch creates a hotkey-capable native window before hiding.
- [ ] Run Windows adapter contract tests on the host fakes, then cross-publish `win-x64` self-contained and verify the PE artifact.

### Task 7: macOS Native Adapter, Capture Permission, and Keychain

**Files:**
- Create: `src/Wocel.Capture.Platform.Mac/Wocel.Capture.Platform.Mac.csproj`
- Create: `src/Wocel.Capture.Platform.Mac/MacScreenCaptureService.cs`
- Create: `src/Wocel.Capture.Platform.Mac/MacCapturePermissionService.cs`
- Create: `src/Wocel.Capture.Platform.Mac/MacHotkeyService.cs`
- Create: `src/Wocel.Capture.Platform.Mac/MacKeychainTokenStore.cs`
- Create: `src/Wocel.Capture.Platform.Mac/MacStartupService.cs`
- Create: `src/Wocel.Capture.Platform.Mac/MacSingleInstanceService.cs`
- Create: `src/Wocel.Capture.Platform.Mac/MacPlatformPaths.cs`
- Create: `src/Wocel.Capture.Platform.Mac/Native/MacNativeMethods.cs`
- Create: `native/macos/WocelCaptureNative/WocelCaptureNative.h`
- Create: `native/macos/WocelCaptureNative/WocelCaptureNative.m`
- Create: `native/macos/build-native.sh`
- Create: `tests/Wocel.Capture.Platform.Tests/MacAdapterContractTests.cs`
- Modify: `src/Wocel.Capture.Desktop/Composition/AppComposition.cs`
- Modify: `Wocel.sln`

**Interfaces:**
- Consumes: Task 1 contracts and the stable C ABI exported by `libWocelCaptureNative.dylib`.
- Produces: `MacPlatformServices.Create()` implementing capture, permission, hotkey, Keychain, startup, paths, and single instance.

- [ ] Write failing contract tests for `Unknown/Denied/Granted/RestartRequired` permission states, no repeated prompt after denial, settings deep link, two-display BGRA composition, native-buffer release, Carbon hotkey conversion, Keychain duplicate/update/delete, LaunchAgent escaping, and stale single-instance socket recovery.
- [ ] Define the native C ABI for permission preflight/request, display metadata, ScreenCaptureKit still capture, returned BGRA buffer ownership, and last-error code; document which side allocates and frees every pointer.
- [ ] Build the shim separately for arm64 and x86_64 with a stable bundle identity; do not use an ad-hoc changing identity for acceptance tests.
- [ ] Implement managed interop with `LibraryImport`, `SafeHandle`, cancellation, and sanitized error mapping.
- [ ] Implement hotkey, Keychain, LaunchAgent, paths, and Unix-domain-socket single instance; use service name `com.wocel.capture`.
- [ ] Compose the adapter when `OperatingSystem.IsMacOS()`, run contract tests, then run a real permission/capture smoke test on the current Mac.

### Task 8: Windows and macOS Packaging

**Files:**
- Create: `packaging/macos/Info.plist.template`
- Create: `packaging/macos/WocelCapture.entitlements`
- Create: `packaging/macos/create-app.sh`
- Create: `packaging/macos/create-dmg.sh`
- Create: `scripts/build-capture-cross-platform.ps1`
- Create: `scripts/build-capture-macos.sh`
- Create: `tests/Wocel.Capture.Platform.Tests/PackagingManifestTests.cs`
- Modify: `installer/WocelCapture.iss`
- Modify: `scripts/build-capture-release.ps1`
- Modify: `docs/wocel-capture-google-setup.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: the Desktop publish output plus per-platform native libraries and optional signing environment variables.
- Produces: Windows portable/installer artifacts and macOS arm64/x64 `.app`/`.dmg` artifacts from clean staging directories.

- [ ] Write failing packaging tests asserting stable bundle ID `com.wocel.capture`, `NSScreenCaptureUsageDescription`, executable/icon names, minimum macOS version, hardened-runtime entitlement, architecture labels, and absence of secret-like strings.
- [ ] Update the Windows build to package `Wocel.Capture.Desktop`, retain per-user install/startup cleanup, and fail on stale staging contents.
- [ ] Implement macOS scripts that publish `osx-arm64` and `osx-x64`, assemble `.app`, copy the matching native shim, validate Mach-O architectures, and create architecture-labeled DMGs.
- [ ] Add optional Developer ID signing/notarization/stapling. When credentials are absent, produce only an explicitly named `unsigned-local-test` package and print the exact limitation.
- [ ] Document Google Client ID injection, Screen Recording permission, Keychain behavior, signing variables, and commands for both OS releases.
- [ ] Run packaging-manifest tests, build both macOS architecture packages on the current Mac, and cross-publish the Windows portable artifact.

### Task 9: Full Verification, Review, and Release Cutover

**Files:**
- Create: `docs/testing/wocel-capture-windows-acceptance.md`
- Create: `docs/testing/wocel-capture-macos-acceptance.md`
- Modify: `.superpowers/sdd/2026-10-06-wocel-capture-cross-platform/progress.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: all test projects and release artifacts.
- Produces: an evidence-backed release decision and documented remaining physical-device checks.

- [ ] Run Capture Core, Platform, Rendering, and Avalonia headless suites sequentially; require zero failures.
- [ ] Run existing `Wocel.Tests`; record but do not conceal unrelated pre-existing dependency warnings.
- [ ] Audit new dependency graphs for known vulnerabilities and scan the exact staged artifacts for credentials.
- [ ] Verify `win-x64` is a PE GUI executable and both macOS executables have the expected Mach-O architecture; record sizes and SHA-256 hashes.
- [ ] On macOS, execute first-run permission denial/grant/restart, Retina/external-display capture, every editor tool, clipboard/save, Keychain sign-in/logout, Drive upload/retry, menu-bar hotkey, launch-at-login, `.app`, and DMG checks.
- [ ] On Windows, execute the documented clean-machine capture/editor/Drive/tray/hotkey/startup/installer/uninstall matrix when a Windows runner is available; never infer these results from cross-compilation.
- [ ] Request a fresh whole-change code review, fix every Critical/Important issue, rerun affected verification, and record the reviewer verdict.
- [ ] Make Avalonia the documented primary release only after both OS acceptance matrices pass; otherwise retain the WPF build as the Windows fallback and label the macOS build as preview.
