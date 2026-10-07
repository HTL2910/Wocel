using Wocel.Capture.Editor;
using Wocel.Capture.Models;
using Wocel.Capture.Platform.Capture;
using Wocel.Capture.Rendering;
using Xunit;

namespace Wocel.Capture.Rendering.Tests;

public sealed class RendererGeometryTests
{
    private static readonly ShapeStyle Red = new(EditorColor.FromHex("#EF4444"), 2, null, 1);

    [Fact]
    public void Renderer_draws_rectangle_without_mutating_source()
    {
        var source = WhiteFrame(20, 20);
        var original = source.Pixels.ToArray();
        var document = new EditorDocument(new PixelSize(20, 20));
        document.Execute(new AddLayerCommand(new ShapeLayer(Guid.NewGuid(), EditorLayerKind.Rectangle, new PixelRect(2, 3, 10, 8), Red)));

        using var rendered = new SkiaEditorRenderer().Render(source, document);

        Assert.Equal(new RenderedPixel(68, 68, 239, 255), rendered.GetPixel(2, 3));
        Assert.Equal(original, source.Pixels.ToArray());
    }

    [Fact]
    public void Renderer_supports_horizontal_and_reverse_arrows()
    {
        var document = new EditorDocument(new PixelSize(30, 20));
        document.Execute(new AddLayerCommand(new ShapeLayer(Guid.NewGuid(), EditorLayerKind.Line, new PixelRect(2, 5, 20, 0), Red, new PixelPoint(2, 5), new PixelPoint(22, 5))));
        document.Execute(new AddLayerCommand(new ShapeLayer(Guid.NewGuid(), EditorLayerKind.Arrow, new PixelRect(4, 8, 20, 6), Red, new PixelPoint(24, 14), new PixelPoint(4, 8))));

        using var rendered = new SkiaEditorRenderer().Render(WhiteFrame(30, 20), document);

        Assert.Contains(Enumerable.Range(2, 20), x => rendered.GetPixel(x, 5).Red > 200 && rendered.GetPixel(x, 5).Green < 120);
        Assert.True(rendered.GetPixel(4, 8).Red > 200);
    }

    [Fact]
    public void Renderer_draws_pen_highlight_text_and_blur_layers()
    {
        var document = new EditorDocument(new PixelSize(48, 32));
        document.Execute(new AddLayerCommand(new FreehandLayer(Guid.NewGuid(), [new PixelPoint(2, 2), new PixelPoint(12, 2)], Red)));
        document.Execute(new AddLayerCommand(HighlightLayer.Create(
            [new PixelPoint(2, 8), new PixelPoint(12, 8)],
            new ShapeStyle(EditorColor.FromHex("#FFFF00"), 4, null, 0.5))));
        document.Execute(new AddLayerCommand(new TextLayer(Guid.NewGuid(), new PixelRect(16, 1, 28, 16), "Aa", Red, 12)));
        document.Execute(new AddLayerCommand(new BlurLayer(Guid.NewGuid(), new PixelRect(0, 16, 16, 16), 3)));

        using var rendered = new SkiaEditorRenderer().Render(CheckerFrame(48, 32), document);

        Assert.True(rendered.GetPixel(7, 2).Red > rendered.GetPixel(7, 2).Green);
        Assert.True(rendered.GetPixel(7, 8).Red > 100 && rendered.GetPixel(7, 8).Green > 100);
        Assert.Contains(Enumerable.Range(16, 28).SelectMany(x => Enumerable.Range(1, 16).Select(y => rendered.GetPixel(x, y))), pixel => pixel.Red > pixel.Green);
        Assert.NotEqual(CheckerFrame(48, 32).Pixels.Span[16 * 48 * 4], rendered.GetPixel(0, 16).Blue);
    }

    [Theory]
    [InlineData(EditorLayerKind.Line)]
    [InlineData(EditorLayerKind.Arrow)]
    [InlineData(EditorLayerKind.Rectangle)]
    [InlineData(EditorLayerKind.Ellipse)]
    [InlineData(EditorLayerKind.Triangle)]
    public void Renderer_draws_each_2d_shape(EditorLayerKind kind)
    {
        var document = new EditorDocument(new PixelSize(30, 30));
        document.Execute(new AddLayerCommand(new ShapeLayer(
            Guid.NewGuid(), kind, new PixelRect(4, 4, 20, 20), Red,
            new PixelPoint(4, 4), new PixelPoint(24, 24))));

        using var rendered = new SkiaEditorRenderer().Render(WhiteFrame(30, 30), document);

        Assert.Contains(
            Enumerable.Range(0, 30).SelectMany(x => Enumerable.Range(0, 30).Select(y => rendered.GetPixel(x, y))),
            pixel => pixel.Red > 200 && pixel.Green < 120);
    }

    [Fact]
    public void Crop_and_resize_map_source_layers_to_output_pixels()
    {
        var document = new EditorDocument(new PixelSize(40, 30));
        document.Execute(new AddLayerCommand(new ShapeLayer(Guid.NewGuid(), EditorLayerKind.Rectangle, new PixelRect(10, 10, 10, 10), Red)));
        document.Execute(new AddLayerCommand(new CropLayer(Guid.NewGuid(), new PixelRect(10, 5, 20, 20))));
        document.Execute(new AddLayerCommand(new ResizeLayer(Guid.NewGuid(), new PixelSize(40, 40))));

        using var rendered = new SkiaEditorRenderer().Render(WhiteFrame(40, 30), document);

        Assert.Equal(40, rendered.Width);
        Assert.Equal(40, rendered.Height);
        Assert.True(rendered.GetPixel(0, 10).Red > 200);
    }

    internal static CapturedFrame WhiteFrame(int width, int height)
    {
        var bytes = new byte[width * height * 4];
        for (var index = 0; index < bytes.Length; index += 4)
        {
            bytes[index] = 255;
            bytes[index + 1] = 255;
            bytes[index + 2] = 255;
            bytes[index + 3] = 255;
        }
        return new CapturedFrame(width, height, width * 4, CapturePixelFormat.Bgra8888, new PixelRect(0, 0, width, height), bytes);
    }

    internal static CapturedFrame CheckerFrame(int width, int height)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var index = (y * width + x) * 4;
            var value = (byte)(((x + y) & 1) == 0 ? 0 : 255);
            bytes[index] = value;
            bytes[index + 1] = value;
            bytes[index + 2] = value;
            bytes[index + 3] = 255;
        }
        return new CapturedFrame(width, height, width * 4, CapturePixelFormat.Bgra8888, new PixelRect(0, 0, width, height), bytes);
    }
}
