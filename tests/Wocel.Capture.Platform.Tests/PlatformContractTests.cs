using Wocel.Capture.Models;
using Wocel.Capture.Platform;
using Wocel.Capture.Platform.Capture;
using Wocel.Capture.Platform.Hotkeys;
using Wocel.Capture.Platform.Services;
using Xunit;

namespace Wocel.Capture.Platform.Tests;

public sealed class PlatformContractTests
{
    [Theory]
    [InlineData(10, 10, 39, 400)]
    [InlineData(10, 10, 40, 399)]
    public void CapturedFrame_rejects_invalid_stride_or_buffer_length(int width, int height, int stride, int bytes)
    {
        Assert.Throws<ArgumentException>(() => new CapturedFrame(
            width,
            height,
            stride,
            CapturePixelFormat.Bgra8888,
            new PixelRect(0, 0, width, height),
            new byte[bytes]));
    }

    [Fact]
    public void DisplayGeometry_maps_logical_points_to_negative_physical_coordinates()
    {
        var display = new DisplayGeometry(
            "external",
            new PixelRect(-3000, -200, 3000, 2000),
            new LogicalRect(-2000, -133.333333, 2000, 1333.333333),
            1.5,
            isPrimary: false);

        Assert.Equal(new PixelPoint(-2850, -125), display.ToPhysical(-1900, -83.333333));
    }

    [Theory]
    [InlineData("Control+Shift+4", HotkeyModifiers.Control | HotkeyModifiers.Shift, "4")]
    [InlineData("Meta+Alt+F12", HotkeyModifiers.Meta | HotkeyModifiers.Alt, "F12")]
    [InlineData("PrintScreen", HotkeyModifiers.None, "PrintScreen")]
    public void HotkeyGesture_parses_platform_neutral_modifiers(string text, HotkeyModifiers modifiers, string key)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(modifiers, gesture.Modifiers);
        Assert.Equal(key, gesture.Key);
    }

    [Fact]
    public void Platform_service_set_rejects_missing_contracts()
    {
        Assert.Throws<ArgumentNullException>(() => new PlatformServiceSet(
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!));
    }
}
