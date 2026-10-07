namespace Wocel.Capture.Models;

public readonly record struct PixelPoint(int X, int Y);

public readonly record struct PixelSize
{
    [System.Text.Json.Serialization.JsonConstructor]
    public PixelSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }
}

public readonly record struct PixelRect
{
    public PixelRect(int x, int y, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public int Right => checked(X + Width);
    public int Bottom => checked(Y + Height);
    public bool IsUsableSelection => Width >= 2 && Height >= 2;

    public static PixelRect FromPoints(PixelPoint first, PixelPoint second)
    {
        var left = Math.Min(first.X, second.X);
        var top = Math.Min(first.Y, second.Y);
        return new PixelRect(left, top, Math.Abs(second.X - first.X), Math.Abs(second.Y - first.Y));
    }
}
