using Wocel.Capture.Models;

namespace Wocel.Capture.Export;

public static class CaptureFileNamer
{
    public static string NextAvailable(
        string directory,
        DateTimeOffset timestamp,
        CaptureImageFormat format,
        Func<string, bool>? exists = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        exists ??= File.Exists;
        var extension = format switch
        {
            CaptureImageFormat.Png => ".png",
            CaptureImageFormat.Jpeg => ".jpg",
            CaptureImageFormat.WebP => ".webp",
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        var stem = $"Screenshot_{timestamp:yyyy-MM-dd_HH-mm-ss}";
        var candidate = Path.Combine(directory, stem + extension);
        for (var suffix = 2; exists(candidate); suffix++)
        {
            candidate = Path.Combine(directory, $"{stem}-{suffix}{extension}");
        }

        return candidate;
    }
}
