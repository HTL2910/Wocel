using System.ComponentModel;
using Wocel.Capture.Models;
using Wocel.Capture.Platform.Hotkeys;
using Wocel.Capture.Platform.Windows;
using Xunit;

namespace Wocel.Capture.Platform.Tests;

public sealed class WindowsAdapterContractTests
{
    [Fact]
    public async Task Capture_preserves_negative_virtual_bounds_and_bgra_stride()
    {
        var bounds = new PixelRect(-1920, -120, 4, 3);
        var service = new WindowsScreenCaptureService(new FakeCaptureNative(bounds, 16, new byte[48]));

        var frame = await service.CaptureVirtualDesktopAsync();

        Assert.Equal(bounds, frame.PhysicalBounds);
        Assert.Equal(16, frame.Stride);
        Assert.Equal(48, frame.Pixels.Length);
    }

    [Fact]
    public async Task Capture_rejects_native_buffer_with_non_bgra_stride()
    {
        var service = new WindowsScreenCaptureService(new FakeCaptureNative(new PixelRect(0, 0, 4, 3), 12, new byte[36]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CaptureVirtualDesktopAsync());
    }

    [Fact]
    public async Task Display_enumeration_preserves_per_monitor_scale_and_negative_origin()
    {
        var displays = new[]
        {
            new Wocel.Capture.Platform.Capture.DisplayGeometry("left", new PixelRect(-1920, 0, 1920, 1080), new Wocel.Capture.Platform.Capture.LogicalRect(-1920, 0, 1920, 1080), 1, false),
            new Wocel.Capture.Platform.Capture.DisplayGeometry("main", new PixelRect(0, 0, 3840, 2160), new Wocel.Capture.Platform.Capture.LogicalRect(0, 0, 1920, 1080), 2, true)
        };
        var service = new WindowsScreenCaptureService(new FakeCaptureNative(new PixelRect(-1920, 0, 5760, 2160), 23040, new byte[23040 * 2160], displays));

        var actual = await service.GetDisplaysAsync();

        Assert.Equal(displays, actual);
    }

    [Fact]
    public void Hotkey_conversion_uses_win32_flags_and_virtual_keys()
    {
        var gesture = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Meta, "PrintScreen");

        var native = WindowsHotkeyService.ToNative(gesture);

        Assert.Equal(0x400Eu, native.Modifiers);
        Assert.Equal(0x2Cu, native.VirtualKey);
    }

    [Fact]
    public async Task Dpapi_failure_maps_to_sanitized_credential_error()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wocel-token-{Guid.NewGuid():N}");
        var store = new WindowsTokenStore(path, new ThrowingProtector());

        var exception = await Assert.ThrowsAsync<WindowsCredentialException>(() => store.SaveAsync("refresh-secret"));

        Assert.Equal("CREDENTIAL_PROTECT_FAILED", exception.ErrorCode);
        Assert.DoesNotContain("refresh-secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_command_quotes_executable_path_and_rejects_quotes()
    {
        Assert.Equal("\"C:\\Program Files\\Wocel\\Wocel Capture.exe\" --background", WindowsStartupService.BuildCommand("C:\\Program Files\\Wocel\\Wocel Capture.exe"));
        Assert.Throws<ArgumentException>(() => WindowsStartupService.BuildCommand("C:\\bad\"path.exe"));
    }

    [Fact]
    public async Task Secondary_instance_notifies_primary_over_activation_pipe()
    {
        var name = "wocel-test-" + Guid.NewGuid().ToString("N");
        using var primary = new WindowsSingleInstanceService(name);
        using var secondary = new WindowsSingleInstanceService(name);
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.Activated += (_, _) => activated.TrySetResult();

        secondary.NotifyPrimary();

        await activated.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(primary.IsPrimary);
        Assert.False(secondary.IsPrimary);
    }

    [Fact]
    public void Platform_paths_use_expected_per_user_directories()
    {
        var paths = new WindowsPlatformPaths("C:/Users/test/AppData/Local", "C:/Users/test/Pictures");

        Assert.EndsWith("Wocel/Capture", paths.AppDataDirectory.Replace('\\', '/'), StringComparison.Ordinal);
        Assert.EndsWith("Wocel/Capture/Cache", paths.CacheDirectory.Replace('\\', '/'), StringComparison.Ordinal);
        Assert.EndsWith("Wocel/Capture/Logs", paths.LogDirectory.Replace('\\', '/'), StringComparison.Ordinal);
        Assert.EndsWith("Wocel Capture", paths.PicturesDirectory.Replace('\\', '/'), StringComparison.Ordinal);
    }

    private sealed class FakeCaptureNative(PixelRect bounds, int stride, byte[] pixels, IReadOnlyList<Wocel.Capture.Platform.Capture.DisplayGeometry>? displays = null) : IWindowsScreenCaptureNative
    {
        public PixelRect GetVirtualDesktopBounds() => bounds;
        public WindowsCaptureBuffer Capture(PixelRect requestedBounds) => new(stride, pixels);
        public IReadOnlyList<Wocel.Capture.Platform.Capture.DisplayGeometry> GetDisplays() => displays ?? [];
    }

    private sealed class ThrowingProtector : IWindowsDataProtector
    {
        public byte[] Protect(byte[] plaintext) => throw new Win32Exception(5, "native details");
        public byte[] Unprotect(byte[] ciphertext) => throw new Win32Exception(5, "native details");
    }
}
