namespace Wocel.Capture.Models;

public enum CaptureImageFormat
{
    Png,
    Jpeg,
    WebP
}

public enum CapturePlatform
{
    Windows,
    MacOS
}

public sealed record CaptureSettings
{
    public string Hotkey { get; init; } = "PrintScreen";
    public CaptureImageFormat DefaultFormat { get; init; } = CaptureImageFormat.Png;
    public string LocalFolder { get; init; } = string.Empty;
    public bool AutoUpload { get; init; }
    public bool LaunchAtSignIn { get; init; }
    public int JpegQuality { get; init; } = 92;

    public static CaptureSettings CreateDefault() => CreateDefault(CapturePlatform.Windows);

    public static CaptureSettings CreateCurrentPlatformDefault() => CreateDefault(
        OperatingSystem.IsMacOS() ? CapturePlatform.MacOS : CapturePlatform.Windows);

    public static CaptureSettings CreateDefault(CapturePlatform platform) => new()
    {
        Hotkey = platform == CapturePlatform.MacOS ? "Control+Shift+4" : "PrintScreen"
    };
}
