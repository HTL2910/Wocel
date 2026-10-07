using Wocel.Capture.Models;

namespace Wocel.Capture.Platform.Capture;

public enum CapturePixelFormat
{
    Bgra8888
}

public sealed class CapturedFrame
{
    private readonly byte[] _pixels;

    public CapturedFrame(
        int width,
        int height,
        int stride,
        CapturePixelFormat pixelFormat,
        PixelRect physicalBounds,
        ReadOnlyMemory<byte> pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (stride < checked(width * 4))
        {
            throw new ArgumentException("Stride is smaller than one BGRA scanline.", nameof(stride));
        }
        if (pixels.Length != checked(stride * height))
        {
            throw new ArgumentException("Pixel buffer length does not match stride and height.", nameof(pixels));
        }
        if (physicalBounds.Width != width || physicalBounds.Height != height)
        {
            throw new ArgumentException("Physical bounds must match frame dimensions.", nameof(physicalBounds));
        }

        Width = width;
        Height = height;
        Stride = stride;
        PixelFormat = pixelFormat;
        PhysicalBounds = physicalBounds;
        _pixels = pixels.ToArray();
    }

    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public CapturePixelFormat PixelFormat { get; }
    public PixelRect PhysicalBounds { get; }
    public ReadOnlyMemory<byte> Pixels => _pixels;
}
