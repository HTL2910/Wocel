using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wocel.Capture.Editor;
using Wocel.Capture.Models;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace Wocel.Capture.Windows.Rendering;

public static class EditorRenderer
{
    public static BitmapSource Render(BitmapSource source, EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(document);
        var crop = document.Layers.OfType<CropLayer>().LastOrDefault()?.CropBounds
            ?? new PixelRect(0, 0, source.PixelWidth, source.PixelHeight);
        crop = Intersect(crop, new PixelRect(0, 0, source.PixelWidth, source.PixelHeight));
        if (!crop.IsUsableSelection)
        {
            throw new InvalidOperationException("Crop region is empty.");
        }

        var output = document.Layers.OfType<ResizeLayer>().LastOrDefault()?.OutputSize
            ?? new PixelSize(crop.Width, crop.Height);
        var scaleX = output.Width / (double)crop.Width;
        var scaleY = output.Height / (double)crop.Height;
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawImage(new CroppedBitmap(source, new Int32Rect(crop.X, crop.Y, crop.Width, crop.Height)), new Rect(0, 0, output.Width, output.Height));
            drawing.PushClip(new RectangleGeometry(new Rect(0, 0, output.Width, output.Height)));
            foreach (var layer in document.Layers.Where(layer => layer is not CropLayer and not ResizeLayer))
            {
                DrawLayer(drawing, source, layer, crop, scaleX, scaleY);
            }
            drawing.Pop();
        }

        var rendered = new RenderTargetBitmap(output.Width, output.Height, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual);
        rendered.Freeze();
        return rendered;
    }

    private static void DrawLayer(DrawingContext drawing, BitmapSource source, EditorLayer layer, PixelRect crop, double scaleX, double scaleY)
    {
        switch (layer)
        {
            case FreehandLayer pen:
                DrawStroke(drawing, pen.Points, pen.Style, crop, scaleX, scaleY);
                break;
            case HighlightLayer highlight:
                DrawStroke(drawing, highlight.Points, highlight.Style, crop, scaleX, scaleY);
                break;
            case TextLayer text:
                DrawText(drawing, text, crop, scaleX, scaleY);
                break;
            case BlurLayer blur:
                DrawBlur(drawing, source, blur, crop, scaleX, scaleY);
                break;
            case ShapeLayer shape:
                DrawShape(drawing, shape, crop, scaleX, scaleY);
                break;
        }
    }

    private static void DrawStroke(DrawingContext drawing, IReadOnlyList<PixelPoint> points, ShapeStyle style, PixelRect crop, double scaleX, double scaleY)
    {
        if (points.Count < 2)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(Map(points[0], crop, scaleX, scaleY), false, false);
            context.PolyLineTo(points.Skip(1).Select(point => Map(point, crop, scaleX, scaleY)).ToArray(), true, true);
        }
        geometry.Freeze();
        drawing.DrawGeometry(null, Pen(style, (scaleX + scaleY) / 2), geometry);
    }

    private static void DrawText(DrawingContext drawing, TextLayer layer, PixelRect crop, double scaleX, double scaleY)
    {
        var brush = Brush(layer.Style.Stroke, layer.Style.Opacity);
        var text = new FormattedText(layer.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), layer.FontSize * scaleY, brush, 1);
        drawing.DrawText(text, Map(new PixelPoint(layer.Bounds.X, layer.Bounds.Y), crop, scaleX, scaleY));
    }

    private static void DrawBlur(DrawingContext drawing, BitmapSource source, BlurLayer layer, PixelRect crop, double scaleX, double scaleY)
    {
        var region = Intersect(layer.Bounds, new PixelRect(0, 0, source.PixelWidth, source.PixelHeight));
        if (!region.IsUsableSelection)
        {
            return;
        }

        var cropped = new CroppedBitmap(source, new Int32Rect(region.X, region.Y, region.Width, region.Height));
        var downX = Math.Max(1d / region.Width, 1d / Math.Max(4, region.Width / Math.Max(4, layer.Radius)));
        var downY = Math.Max(1d / region.Height, 1d / Math.Max(4, region.Height / Math.Max(4, layer.Radius)));
        var tiny = new TransformedBitmap(cropped, new ScaleTransform(downX, downY));
        var pixelated = new TransformedBitmap(tiny, new ScaleTransform(region.Width / (double)tiny.PixelWidth, region.Height / (double)tiny.PixelHeight));
        pixelated.Freeze();
        drawing.DrawImage(pixelated, Map(region, crop, scaleX, scaleY));
    }

    private static void DrawShape(DrawingContext drawing, ShapeLayer layer, PixelRect crop, double scaleX, double scaleY)
    {
        var bounds = Map(layer.Bounds, crop, scaleX, scaleY);
        var pen = Pen(layer.Style, (scaleX + scaleY) / 2);
        var fill = layer.Style.Fill is { } color ? Brush(color, layer.Style.Opacity) : null;
        switch (layer.Kind)
        {
            case EditorLayerKind.Line:
                drawing.DrawLine(pen, Map(layer.Start ?? new PixelPoint(layer.Bounds.X, layer.Bounds.Y), crop, scaleX, scaleY), Map(layer.End ?? new PixelPoint(layer.Bounds.Right, layer.Bounds.Bottom), crop, scaleX, scaleY));
                break;
            case EditorLayerKind.Arrow:
                DrawArrow(drawing, pen, Map(layer.Start ?? new PixelPoint(layer.Bounds.X, layer.Bounds.Y), crop, scaleX, scaleY), Map(layer.End ?? new PixelPoint(layer.Bounds.Right, layer.Bounds.Bottom), crop, scaleX, scaleY));
                break;
            case EditorLayerKind.Rectangle:
                drawing.DrawRectangle(fill, pen, bounds);
                break;
            case EditorLayerKind.Ellipse:
                drawing.DrawEllipse(fill, pen, new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2), bounds.Width / 2, bounds.Height / 2);
                break;
            case EditorLayerKind.Triangle:
                var triangle = new StreamGeometry();
                using (var context = triangle.Open())
                {
                    context.BeginFigure(new Point(bounds.X + bounds.Width / 2, bounds.Y), fill is not null, true);
                    context.LineTo(bounds.BottomRight, true, false);
                    context.LineTo(bounds.BottomLeft, true, false);
                }
                drawing.DrawGeometry(fill, pen, triangle);
                break;
        }
    }

    private static void DrawArrow(DrawingContext drawing, Pen pen, Point start, Point end)
    {
        drawing.DrawLine(pen, start, end);
        var vector = start - end;
        if (vector.Length < 1)
        {
            return;
        }
        vector.Normalize();
        var normal = new Vector(-vector.Y, vector.X);
        var length = Math.Max(10, pen.Thickness * 4);
        drawing.DrawLine(pen, end, end + vector * length + normal * length * .45);
        drawing.DrawLine(pen, end, end + vector * length - normal * length * .45);
    }

    private static Pen Pen(ShapeStyle style, double scale)
    {
        var pen = new Pen(Brush(style.Stroke, style.Opacity), Math.Max(1, style.StrokeWidth * scale))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        return pen;
    }

    private static SolidColorBrush Brush(EditorColor color, double opacity)
    {
        var brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B)) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }

    private static Point Map(PixelPoint point, PixelRect crop, double scaleX, double scaleY) =>
        new((point.X - crop.X) * scaleX, (point.Y - crop.Y) * scaleY);

    private static Rect Map(PixelRect rect, PixelRect crop, double scaleX, double scaleY) =>
        new((rect.X - crop.X) * scaleX, (rect.Y - crop.Y) * scaleY, rect.Width * scaleX, rect.Height * scaleY);

    private static PixelRect Intersect(PixelRect left, PixelRect right)
    {
        var x = Math.Max(left.X, right.X);
        var y = Math.Max(left.Y, right.Y);
        var rightEdge = Math.Min(left.Right, right.Right);
        var bottom = Math.Min(left.Bottom, right.Bottom);
        return new PixelRect(x, y, Math.Max(0, rightEdge - x), Math.Max(0, bottom - y));
    }
}
