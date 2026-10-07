using Wocel.Capture.Models;

namespace Wocel.Capture.Capture;

public sealed record MonitorGeometry
{
    public MonitorGeometry(PixelRect physicalBounds, double scaleX, double scaleY)
    {
        if (!double.IsFinite(scaleX) || scaleX <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scaleX));
        }

        if (!double.IsFinite(scaleY) || scaleY <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scaleY));
        }

        PhysicalBounds = physicalBounds;
        ScaleX = scaleX;
        ScaleY = scaleY;
    }

    public PixelRect PhysicalBounds { get; }
    public double ScaleX { get; }
    public double ScaleY { get; }
}

public static class CoordinateMapper
{
    public static PixelPoint ToPhysical(MonitorGeometry monitor, double localDipX, double localDipY)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        if (!double.IsFinite(localDipX) || !double.IsFinite(localDipY))
        {
            throw new ArgumentOutOfRangeException(nameof(localDipX), "Coordinates must be finite.");
        }

        return new PixelPoint(
            monitor.PhysicalBounds.X + checked((int)Math.Round(localDipX * monitor.ScaleX, MidpointRounding.AwayFromZero)),
            monitor.PhysicalBounds.Y + checked((int)Math.Round(localDipY * monitor.ScaleY, MidpointRounding.AwayFromZero)));
    }

    public static (double X, double Y) ToLocalDips(MonitorGeometry monitor, PixelPoint point)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        return (
            (point.X - monitor.PhysicalBounds.X) / monitor.ScaleX,
            (point.Y - monitor.PhysicalBounds.Y) / monitor.ScaleY);
    }
}
