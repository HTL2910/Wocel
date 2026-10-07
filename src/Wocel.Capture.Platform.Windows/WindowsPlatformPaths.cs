using Wocel.Capture.Platform.Services;

namespace Wocel.Capture.Platform.Windows;

public sealed class WindowsPlatformPaths : IPlatformPaths
{
    public WindowsPlatformPaths(string? localApplicationData = null, string? pictures = null)
    {
        localApplicationData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        pictures ??= Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationData);
        ArgumentException.ThrowIfNullOrWhiteSpace(pictures);
        AppDataDirectory = Path.Combine(localApplicationData, "Wocel", "Capture");
        CacheDirectory = Path.Combine(AppDataDirectory, "Cache");
        LogDirectory = Path.Combine(AppDataDirectory, "Logs");
        PicturesDirectory = Path.Combine(pictures, "Wocel Capture");
    }

    public string AppDataDirectory { get; }
    public string CacheDirectory { get; }
    public string LogDirectory { get; }
    public string PicturesDirectory { get; }
}
