using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Wocel.Capture.Models;

namespace Wocel.Capture.Windows.Services;

public sealed class ImageExportService
{
    public bool IsWebPAvailable => false;

    public void CopyToClipboard(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);
        System.Windows.Clipboard.SetImage(image);
    }

    public async Task<long> SaveAsync(BitmapSource image, string path, CaptureImageFormat format, int jpegQuality = 92, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        BitmapEncoder encoder = format switch
        {
            CaptureImageFormat.Png => new PngBitmapEncoder(),
            CaptureImageFormat.Jpeg => new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 1, 100) },
            CaptureImageFormat.WebP => throw new NotSupportedException("WebP encoder is not available on this Windows installation."),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        encoder.Save(stream);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        return stream.Length;
    }
}
