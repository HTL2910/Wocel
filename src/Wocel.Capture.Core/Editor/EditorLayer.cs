using Wocel.Capture.Models;

namespace Wocel.Capture.Editor;

public enum EditorLayerKind
{
    Pen,
    Highlight,
    Text,
    Blur,
    Line,
    Arrow,
    Rectangle,
    Ellipse,
    Triangle,
    Crop,
    Resize
}

public readonly record struct EditorColor(byte A, byte R, byte G, byte B)
{
    public static EditorColor FromHex(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var hex = value.TrimStart('#');
        if (hex.Length != 6 || !uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            throw new FormatException("Color must use #RRGGBB format.");
        }

        return new EditorColor(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}

public sealed record ShapeStyle
{
    public ShapeStyle(EditorColor stroke, double strokeWidth, EditorColor? fill, double opacity)
    {
        if (!double.IsFinite(strokeWidth) || strokeWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(strokeWidth));
        }

        Stroke = stroke;
        StrokeWidth = strokeWidth;
        Fill = fill;
        Opacity = Math.Clamp(opacity, 0, 1);
    }

    public EditorColor Stroke { get; }
    public double StrokeWidth { get; }
    public EditorColor? Fill { get; }
    public double Opacity { get; }

    public ShapeStyle WithOpacity(double opacity) => new(Stroke, StrokeWidth, Fill, opacity);
}

public abstract record EditorLayer(Guid Id, EditorLayerKind Kind)
{
    public abstract PixelRect Bounds { get; }
    public abstract EditorLayer Translate(int deltaX, int deltaY);
}

public sealed record FreehandLayer(Guid LayerId, IReadOnlyList<PixelPoint> Points, ShapeStyle Style)
    : EditorLayer(LayerId, EditorLayerKind.Pen)
{
    public override PixelRect Bounds => BoundsFor(Points);

    public override EditorLayer Translate(int deltaX, int deltaY) =>
        this with { Points = Points.Select(point => new PixelPoint(point.X + deltaX, point.Y + deltaY)).ToArray() };

    internal static PixelRect BoundsFor(IReadOnlyList<PixelPoint> points)
    {
        if (points.Count == 0)
        {
            return new PixelRect(0, 0, 0, 0);
        }

        var left = points.Min(point => point.X);
        var top = points.Min(point => point.Y);
        var right = points.Max(point => point.X);
        var bottom = points.Max(point => point.Y);
        return new PixelRect(left, top, right - left, bottom - top);
    }
}

public sealed record HighlightLayer(Guid LayerId, IReadOnlyList<PixelPoint> Points, ShapeStyle Style)
    : EditorLayer(LayerId, EditorLayerKind.Highlight)
{
    public override PixelRect Bounds => FreehandLayer.BoundsFor(Points);

    public static HighlightLayer Create(IReadOnlyList<PixelPoint> points, ShapeStyle style) =>
        new(Guid.NewGuid(), points, style.WithOpacity(Math.Clamp(style.Opacity, 0, 0.65)));

    public override EditorLayer Translate(int deltaX, int deltaY) =>
        this with { Points = Points.Select(point => new PixelPoint(point.X + deltaX, point.Y + deltaY)).ToArray() };
}

public sealed record TextLayer(
    Guid LayerId,
    PixelRect TextBounds,
    string Text,
    ShapeStyle Style,
    double FontSize) : EditorLayer(LayerId, EditorLayerKind.Text)
{
    public override PixelRect Bounds => TextBounds;
    public override EditorLayer Translate(int deltaX, int deltaY) =>
        this with { TextBounds = new PixelRect(Bounds.X + deltaX, Bounds.Y + deltaY, Bounds.Width, Bounds.Height) };
}

public sealed record BlurLayer(Guid LayerId, PixelRect BlurBounds, double Radius)
    : EditorLayer(LayerId, EditorLayerKind.Blur)
{
    public override PixelRect Bounds => BlurBounds;
    public override EditorLayer Translate(int deltaX, int deltaY) =>
        this with { BlurBounds = new PixelRect(Bounds.X + deltaX, Bounds.Y + deltaY, Bounds.Width, Bounds.Height) };
}

public sealed record ShapeLayer(
    Guid LayerId,
    EditorLayerKind ShapeKind,
    PixelRect ShapeBounds,
    ShapeStyle Style,
    PixelPoint? Start = null,
    PixelPoint? End = null)
    : EditorLayer(LayerId, ValidateShapeKind(ShapeKind))
{
    public override PixelRect Bounds => ShapeBounds;
    public override EditorLayer Translate(int deltaX, int deltaY) =>
        this with
        {
            ShapeBounds = new PixelRect(Bounds.X + deltaX, Bounds.Y + deltaY, Bounds.Width, Bounds.Height),
            Start = Start is { } start ? new PixelPoint(start.X + deltaX, start.Y + deltaY) : null,
            End = End is { } end ? new PixelPoint(end.X + deltaX, end.Y + deltaY) : null
        };

    private static EditorLayerKind ValidateShapeKind(EditorLayerKind kind)
    {
        if (kind is < EditorLayerKind.Line or > EditorLayerKind.Triangle)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Layer kind is not a 2D shape.");
        }

        return kind;
    }
}

public sealed record CropLayer(Guid LayerId, PixelRect CropBounds)
    : EditorLayer(LayerId, EditorLayerKind.Crop)
{
    public override PixelRect Bounds => CropBounds;
    public override EditorLayer Translate(int deltaX, int deltaY) =>
        this with { CropBounds = new PixelRect(Bounds.X + deltaX, Bounds.Y + deltaY, Bounds.Width, Bounds.Height) };
}

public sealed record ResizeLayer(Guid LayerId, PixelSize OutputSize)
    : EditorLayer(LayerId, EditorLayerKind.Resize)
{
    public override PixelRect Bounds => new(0, 0, OutputSize.Width, OutputSize.Height);
    public override EditorLayer Translate(int deltaX, int deltaY) => this;
}
