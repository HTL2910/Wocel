using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Wocel.Capture.Platform.Capture;
using Wocel.Capture.Rendering;

namespace Wocel.Capture.Desktop.Rendering;

public static class AvaloniaBitmapAdapter
{
    public static WriteableBitmap Copy(RenderedImage source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var bitmap = new WriteableBitmap(
            new PixelSize(source.Width, source.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        using var framebuffer = bitmap.Lock();
        unsafe
        {
            var destination = (byte*)framebuffer.Address;
            var srcSpan = source.GetPixelSpan();
            var lineBytes = source.Width * 4;
            for (var y = 0; y < source.Height; y++)
            {
                var srcOffset = y * source.RowBytes;
                var dstOffset = y * framebuffer.RowBytes;
                srcSpan.Slice(srcOffset, lineBytes).CopyTo(new Span<byte>(destination + dstOffset, lineBytes));
            }
        }
        return bitmap;
    }

    public static WriteableBitmap FromFrame(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var bitmap = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        using var framebuffer = bitmap.Lock();
        unsafe
        {
            var destination = (byte*)framebuffer.Address;
            var srcSpan = frame.Pixels.Span;
            var lineBytes = frame.Width * 4;
            for (var y = 0; y < frame.Height; y++)
            {
                var srcOffset = y * frame.Stride;
                var dstOffset = y * framebuffer.RowBytes;
                srcSpan.Slice(srcOffset, lineBytes).CopyTo(new Span<byte>(destination + dstOffset, lineBytes));
            }
        }
        return bitmap;
    }
}
