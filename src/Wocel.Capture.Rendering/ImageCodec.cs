using SkiaSharp;
using Wocel.Capture.Models;

namespace Wocel.Capture.Rendering;

public static class ImageCodec
{
    public static byte[] Encode(RenderedImage image, CaptureImageFormat format, int quality)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (format == CaptureImageFormat.WebP)
        {
            throw new NotSupportedException("WebP is not available in the first cross-platform release.");
        }
        if (format == CaptureImageFormat.Jpeg && quality is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(quality));
        }

        using var skImage = SKImage.FromBitmap(image.Bitmap);
        using var data = skImage.Encode(
            format == CaptureImageFormat.Png ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg,
            format == CaptureImageFormat.Png ? 100 : quality);
        return data?.ToArray() ?? throw new InvalidOperationException("Image encoding failed.");
    }
}
