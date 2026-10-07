using System.Runtime.InteropServices;
using SkiaSharp;
using Wocel.Capture.Editor;
using Wocel.Capture.Models;
using Wocel.Capture.Platform.Capture;

namespace Wocel.Capture.Rendering;

public sealed class SkiaEditorRenderer
{
    public RenderedImage Render(CapturedFrame source, EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(document);
        if (source.Width != document.SourceSize.Width || source.Height != document.SourceSize.Height)
        {
            throw new ArgumentException("Source frame and editor document dimensions differ.", nameof(document));
        }

        using var sourceBitmap = CreateSourceBitmap(source);
        var sourceBounds = new PixelRect(0, 0, source.Width, source.Height);
        var crop = Intersect(document.Layers.OfType<CropLayer>().LastOrDefault()?.CropBounds ?? sourceBounds, sourceBounds);
        if (!crop.IsUsableSelection)
        {
            throw new InvalidOperationException("Crop region is empty.");
        }
        var output = document.Layers.OfType<ResizeLayer>().LastOrDefault()?.OutputSize ?? new PixelSize(crop.Width, crop.Height);
        var bitmap = new SKBitmap(new SKImageInfo(output.Width, output.Height, SKColorType.Bgra8888, SKAlphaType.Premul));

        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(
            sourceBitmap,
            new SKRect(crop.X, crop.Y, crop.Right, crop.Bottom),
            new SKRect(0, 0, output.Width, output.Height),
            new SKSamplingOptions(SKCubicResampler.Mitchell),
            null);
        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, output.Width, output.Height));
        canvas.Scale(output.Width / (float)crop.Width, output.Height / (float)crop.Height);
        canvas.Translate(-crop.X, -crop.Y);
        foreach (var layer in document.Layers.Where(layer => layer is not CropLayer and not ResizeLayer))
        {
            DrawLayer(canvas, sourceBitmap, layer);
        }
        canvas.Restore();
        canvas.Flush();
        return new RenderedImage(bitmap);
    }

    private static SKBitmap CreateSourceBitmap(CapturedFrame source)
    {
        if (source.PixelFormat != CapturePixelFormat.Bgra8888)
        {
            throw new NotSupportedException($"Unsupported pixel format: {source.PixelFormat}");
        }
        var bitmap = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul), source.Stride);
        Marshal.Copy(source.Pixels.ToArray(), 0, bitmap.GetPixels(), source.Pixels.Length);
        return bitmap;
    }

    private static void DrawLayer(SKCanvas canvas, SKBitmap source, EditorLayer layer)
    {
        switch (layer)
        {
            case FreehandLayer pen:
                DrawStroke(canvas, pen.Points, pen.Style);
                break;
            case HighlightLayer highlight:
                DrawStroke(canvas, highlight.Points, highlight.Style);
                break;
            case TextLayer text:
                using (var paint = StrokePaint(text.Style))
                using (var font = new SKFont(SKTypeface.Default, (float)text.FontSize))
                {
                    paint.Style = SKPaintStyle.Fill;
                    canvas.DrawText(text.Text, text.Bounds.X, text.Bounds.Y + font.Size, SKTextAlign.Left, font, paint);
                }
                break;
            case BlurLayer blur:
                using (var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur((float)blur.Radius, (float)blur.Radius) })
                {
                    var rect = Rect(blur.Bounds);
                    canvas.Save();
                    canvas.ClipRect(rect);
                    canvas.DrawBitmap(source, rect, rect, SKSamplingOptions.Default, paint);
                    canvas.Restore();
                }
                break;
            case ShapeLayer shape:
                DrawShape(canvas, shape);
                break;
        }
    }

    private static void DrawStroke(SKCanvas canvas, IReadOnlyList<PixelPoint> points, ShapeStyle style)
    {
        if (points.Count < 2)
        {
            return;
        }
        using var builder = new SKPathBuilder();
        builder.MoveTo(points[0].X, points[0].Y);
        foreach (var point in points.Skip(1))
        {
            builder.LineTo(point.X, point.Y);
        }
        using var path = builder.Detach();
        using var paint = StrokePaint(style);
        canvas.DrawPath(path, paint);
    }

    private static void DrawShape(SKCanvas canvas, ShapeLayer layer)
    {
        using var stroke = StrokePaint(layer.Style);
        using var fill = layer.Style.Fill is { } fillColor ? FillPaint(fillColor, layer.Style.Opacity) : null;
        var bounds = Rect(layer.Bounds);
        var start = layer.Start ?? new PixelPoint(layer.Bounds.X, layer.Bounds.Y);
        var end = layer.End ?? new PixelPoint(layer.Bounds.Right, layer.Bounds.Bottom);
        switch (layer.Kind)
        {
            case EditorLayerKind.Line:
                canvas.DrawLine(start.X, start.Y, end.X, end.Y, stroke);
                break;
            case EditorLayerKind.Arrow:
                DrawArrow(canvas, stroke, start, end);
                break;
            case EditorLayerKind.Rectangle:
                if (fill is not null) canvas.DrawRect(bounds, fill);
                canvas.DrawRect(bounds, stroke);
                break;
            case EditorLayerKind.Ellipse:
                if (fill is not null) canvas.DrawOval(bounds, fill);
                canvas.DrawOval(bounds, stroke);
                break;
            case EditorLayerKind.Triangle:
                using (var builder = new SKPathBuilder())
                {
                    builder.MoveTo(bounds.MidX, bounds.Top);
                    builder.LineTo(bounds.Right, bounds.Bottom);
                    builder.LineTo(bounds.Left, bounds.Bottom);
                    builder.Close();
                    using var path = builder.Detach();
                    if (fill is not null) canvas.DrawPath(path, fill);
                    canvas.DrawPath(path, stroke);
                }
                break;
        }
    }

    private static void DrawArrow(SKCanvas canvas, SKPaint paint, PixelPoint start, PixelPoint end)
    {
        canvas.DrawLine(start.X, start.Y, end.X, end.Y, paint);
        var dx = start.X - end.X;
        var dy = start.Y - end.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1) return;
        var ux = dx / length;
        var uy = dy / length;
        var nx = -uy;
        var ny = ux;
        var head = Math.Max(10, paint.StrokeWidth * 4);
        canvas.DrawLine(end.X, end.Y, (float)(end.X + ux * head + nx * head * .45), (float)(end.Y + uy * head + ny * head * .45), paint);
        canvas.DrawLine(end.X, end.Y, (float)(end.X + ux * head - nx * head * .45), (float)(end.Y + uy * head - ny * head * .45), paint);
    }

    private static SKPaint StrokePaint(ShapeStyle style) => new()
    {
        Color = Color(style.Stroke, style.Opacity),
        StrokeWidth = (float)style.StrokeWidth,
        Style = SKPaintStyle.Stroke,
        IsAntialias = true,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round
    };

    private static SKPaint FillPaint(EditorColor color, double opacity) => new()
    {
        Color = Color(color, opacity),
        Style = SKPaintStyle.Fill,
        IsAntialias = true
    };

    private static SKColor Color(EditorColor color, double opacity) =>
        new(color.R, color.G, color.B, (byte)Math.Round(color.A * Math.Clamp(opacity, 0, 1)));

    private static SKRect Rect(PixelRect rect) => new(rect.X, rect.Y, rect.Right, rect.Bottom);

    private static PixelRect Intersect(PixelRect left, PixelRect right)
    {
        var x = Math.Max(left.X, right.X);
        var y = Math.Max(left.Y, right.Y);
        var rightEdge = Math.Min(left.Right, right.Right);
        var bottom = Math.Min(left.Bottom, right.Bottom);
        return new PixelRect(x, y, Math.Max(0, rightEdge - x), Math.Max(0, bottom - y));
    }
}
