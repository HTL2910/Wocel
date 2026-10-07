using Wocel.Capture.Models;
using Wocel.Capture.Platform.Capture;

namespace Wocel.Capture.Desktop.Capture;

public sealed class SelectionController
{
    private readonly IReadOnlyDictionary<string, DisplayGeometry> _displays;
    private readonly PixelRect _virtualBounds;
    private PixelPoint? _anchor;

    public SelectionController(IReadOnlyList<DisplayGeometry> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        if (displays.Count == 0) throw new ArgumentException("At least one display is required.", nameof(displays));
        _displays = displays.ToDictionary(display => display.Id, StringComparer.Ordinal);
        var left = displays.Min(display => display.PhysicalBounds.X);
        var top = displays.Min(display => display.PhysicalBounds.Y);
        var right = displays.Max(display => display.PhysicalBounds.Right);
        var bottom = displays.Max(display => display.PhysicalBounds.Bottom);
        _virtualBounds = new PixelRect(left, top, right - left, bottom - top);
    }

    public PixelPoint? Anchor => _anchor;
    public PixelRect Selection { get; private set; }
    public PixelRect VirtualBounds => _virtualBounds;
    public event EventHandler? SelectionChanged;

    public static SelectionController Begin(IReadOnlyList<DisplayGeometry> displays, CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var controller = new SelectionController(displays);
        if (frame.PhysicalBounds != controller.VirtualBounds)
            throw new ArgumentException("Captured frame must match the physical virtual desktop.", nameof(frame));
        return controller;
    }

    public void BeginDrag(string displayId, double logicalX, double logicalY)
    {
        _anchor = Map(displayId, logicalX, logicalY);
        Selection = new PixelRect(_anchor.Value.X, _anchor.Value.Y, 0, 0);
        OnSelectionChanged();
    }

    public void UpdateDrag(string displayId, double logicalX, double logicalY)
    {
        if (_anchor is not { } anchor) return;
        var current = Map(displayId, logicalX, logicalY);
        Selection = PixelRect.FromPoints(ClampPoint(anchor), ClampPoint(current));
        OnSelectionChanged();
    }

    public void SetSelection(PixelRect selection)
    {
        var width = Math.Min(selection.Width, _virtualBounds.Width);
        var height = Math.Min(selection.Height, _virtualBounds.Height);
        var x = Math.Clamp(selection.X, _virtualBounds.X, _virtualBounds.Right - width);
        var y = Math.Clamp(selection.Y, _virtualBounds.Y, _virtualBounds.Bottom - height);
        Selection = new PixelRect(x, y, width, height);
        _anchor = new PixelPoint(x, y);
        OnSelectionChanged();
    }

    public void Nudge(int directionX, int directionY, bool coarse)
    {
        if (Selection.Width == 0 || Selection.Height == 0) return;
        var step = coarse ? 10 : 1;
        SetSelection(new PixelRect(
            Selection.X + Math.Sign(directionX) * step,
            Selection.Y + Math.Sign(directionY) * step,
            Selection.Width,
            Selection.Height));
    }

    public bool TryComplete(out PixelRect selection)
    {
        selection = Selection;
        return selection.IsUsableSelection;
    }

    private PixelPoint Map(string displayId, double x, double y)
    {
        if (!_displays.TryGetValue(displayId, out var display))
            throw new KeyNotFoundException($"Display '{displayId}' was not found.");
        return ClampPoint(display.ToPhysical(x, y));
    }

    private PixelPoint ClampPoint(PixelPoint point) => new(
        Math.Clamp(point.X, _virtualBounds.X, _virtualBounds.Right),
        Math.Clamp(point.Y, _virtualBounds.Y, _virtualBounds.Bottom));

    private void OnSelectionChanged() => SelectionChanged?.Invoke(this, EventArgs.Empty);
}
