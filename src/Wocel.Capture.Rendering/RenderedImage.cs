using SkiaSharp;

namespace Wocel.Capture.Rendering;

public readonly record struct RenderedPixel(byte Blue, byte Green, byte Red, byte Alpha);

public sealed class RenderedImage : IDisposable
{
    internal RenderedImage(SKBitmap bitmap) => Bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));

    internal SKBitmap Bitmap { get; }
    public int Width => Bitmap.Width;
    public int Height => Bitmap.Height;
    public int RowBytes => Bitmap.RowBytes;

    public ReadOnlySpan<byte> GetPixelSpan() => Bitmap.GetPixelSpan();

    public RenderedPixel GetPixel(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        if (x >= Width || y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }
        var color = Bitmap.GetPixel(x, y);
        return new RenderedPixel(color.Blue, color.Green, color.Red, color.Alpha);
    }

    public void Dispose() => Bitmap.Dispose();
}
