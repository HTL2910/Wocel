using Wocel.Capture.Models;
using Wocel.Capture.Platform.Capture;
using Wocel.Capture.Rendering;
using Xunit;

namespace Wocel.Capture.Rendering.Tests;

public sealed class ImageCodecTests
{
    [Fact]
    public void Png_encoder_writes_png_signature()
    {
        using var image = new SkiaEditorRenderer().Render(RendererGeometryTests.WhiteFrame(4, 4), new Wocel.Capture.Editor.EditorDocument(new PixelSize(4, 4)));

        var bytes = ImageCodec.Encode(image, CaptureImageFormat.Png, 92);

        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Jpeg_encoder_rejects_quality_outside_one_to_one_hundred(int quality)
    {
        using var image = new SkiaEditorRenderer().Render(RendererGeometryTests.WhiteFrame(4, 4), new Wocel.Capture.Editor.EditorDocument(new PixelSize(4, 4)));

        Assert.Throws<ArgumentOutOfRangeException>(() => ImageCodec.Encode(image, CaptureImageFormat.Jpeg, quality));
    }

    [Fact]
    public void WebP_is_explicitly_unavailable_in_first_cross_platform_release()
    {
        using var image = new SkiaEditorRenderer().Render(RendererGeometryTests.WhiteFrame(4, 4), new Wocel.Capture.Editor.EditorDocument(new PixelSize(4, 4)));

        Assert.Throws<NotSupportedException>(() => ImageCodec.Encode(image, CaptureImageFormat.WebP, 92));
    }

    [Fact]
    public void Png_round_trip_preserves_alpha()
    {
        var frame = new CapturedFrame(
            2, 2, 8, CapturePixelFormat.Bgra8888, new PixelRect(0, 0, 2, 2),
            new byte[] { 15, 10, 5, 64, 15, 10, 5, 64, 15, 10, 5, 64, 15, 10, 5, 64 });
        using var image = new SkiaEditorRenderer().Render(frame, new Wocel.Capture.Editor.EditorDocument(new PixelSize(2, 2)));

        var bytes = ImageCodec.Encode(image, CaptureImageFormat.Png, 92);
        using var decoded = SkiaSharp.SKBitmap.Decode(bytes);

        Assert.Equal(64, decoded.GetPixel(0, 0).Alpha);
    }
}
