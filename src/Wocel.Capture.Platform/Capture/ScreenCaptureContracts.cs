using Wocel.Capture.Models;

namespace Wocel.Capture.Platform.Capture;

public readonly record struct LogicalRect
{
    public LogicalRect(double x, double y, double width, double height)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height)
            || width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Logical display bounds must be finite and positive.");
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }
}

public sealed record DisplayGeometry
{
    public DisplayGeometry(string id, PixelRect physicalBounds, LogicalRect logicalBounds, double scaleFactor, bool isPrimary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!physicalBounds.IsUsableSelection)
        {
            throw new ArgumentOutOfRangeException(nameof(physicalBounds));
        }
        if (!double.IsFinite(scaleFactor) || scaleFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scaleFactor));
        }

        Id = id;
        PhysicalBounds = physicalBounds;
        LogicalBounds = logicalBounds;
        ScaleFactor = scaleFactor;
        IsPrimary = isPrimary;
    }

    public string Id { get; }
    public PixelRect PhysicalBounds { get; }
    public LogicalRect LogicalBounds { get; }
    public double ScaleFactor { get; }
    public bool IsPrimary { get; }

    public PixelPoint ToPhysical(double logicalX, double logicalY)
    {
        if (!double.IsFinite(logicalX) || !double.IsFinite(logicalY))
        {
            throw new ArgumentOutOfRangeException(nameof(logicalX));
        }

        return new PixelPoint(
            PhysicalBounds.X + checked((int)Math.Round((logicalX - LogicalBounds.X) * ScaleFactor, MidpointRounding.AwayFromZero)),
            PhysicalBounds.Y + checked((int)Math.Round((logicalY - LogicalBounds.Y) * ScaleFactor, MidpointRounding.AwayFromZero)));
    }
}

public enum CapturePermissionStatus
{
    NotRequired,
    Unknown,
    Denied,
    Granted,
    RestartRequired
}

public interface IScreenCaptureService
{
    Task<CapturePermissionStatus> GetPermissionStatusAsync(CancellationToken cancellationToken = default);
    Task<CapturePermissionStatus> RequestPermissionAsync(CancellationToken cancellationToken = default);
    Task OpenPermissionSettingsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DisplayGeometry>> GetDisplaysAsync(CancellationToken cancellationToken = default);
    Task<CapturedFrame> CaptureVirtualDesktopAsync(CancellationToken cancellationToken = default);
}
