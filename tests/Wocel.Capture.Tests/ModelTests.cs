using Wocel.Capture.Models;
using Xunit;

namespace Wocel.Capture.Tests;

public sealed class ModelTests
{
    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-1, 10)]
    [InlineData(10, -1)]
    public void PixelSize_rejects_non_positive_dimensions(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelSize(width, height));
    }

    [Fact]
    public void PixelRect_from_points_normalizes_reverse_drag()
    {
        var rect = PixelRect.FromPoints(new PixelPoint(50, 75), new PixelPoint(10, 20));

        Assert.Equal(new PixelRect(10, 20, 40, 55), rect);
    }

    [Fact]
    public void CaptureSettings_defaults_to_png_and_print_screen()
    {
        var settings = CaptureSettings.CreateDefault();

        Assert.Equal(CaptureImageFormat.Png, settings.DefaultFormat);
        Assert.Equal("PrintScreen", settings.Hotkey);
        Assert.False(settings.AutoUpload);
        Assert.False(settings.LaunchAtSignIn);
    }

    [Theory]
    [InlineData("PrintScreen", 0u, 0x2Cu)]
    [InlineData("Ctrl+Shift+S", 0x0006u, 0x53u)]
    [InlineData("Alt+F12", 0x0001u, 0x7Bu)]
    public void Configurable_hotkeys_parse_to_win32_values(string text, uint modifiers, uint virtualKey)
    {
        Assert.True(GlobalHotkey.TryParse(text, out var hotkey));
        Assert.Equal(modifiers, hotkey.Modifiers);
        Assert.Equal(virtualKey, hotkey.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+NoSuchKey")]
    [InlineData("Ctrl+Ctrl+S")]
    public void Invalid_hotkeys_are_rejected(string text) => Assert.False(GlobalHotkey.TryParse(text, out _));

    [Fact]
    public void UploadState_exposes_every_history_state()
    {
        var names = Enum.GetNames<UploadState>();

        Assert.Equal(
            ["LocalOnly", "Pending", "Uploading", "UploadedPrivate", "UploadedShared", "Failed", "AuthenticationRequired"],
            names);
    }
}
