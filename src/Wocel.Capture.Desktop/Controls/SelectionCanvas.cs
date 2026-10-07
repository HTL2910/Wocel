using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Wocel.Capture.Desktop.ViewModels;
using Wocel.Capture.Platform.Capture;

namespace Wocel.Capture.Desktop.Controls;

public sealed class SelectionCanvas : Control
{
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.FromArgb(125, 0, 0, 0));
    private static readonly IPen BorderPen = new Pen(new SolidColorBrush(Color.Parse("#06B6D4")), 2);

    public DisplayGeometry? Display { get; set; }
    public SelectionOverlayViewModel? ViewModel { get; set; }
    public Rect ImageBounds => Bounds;

    public SelectionCanvas()
    {
        ClipToBounds = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var sel = ViewModel?.Selection;
        if (Display != null && sel is { IsUsableSelection: true })
        {
            var physBounds = Display.PhysicalBounds;
            var relX = (double)(sel.Value.X - physBounds.X) / physBounds.Width;
            var relY = (double)(sel.Value.Y - physBounds.Y) / physBounds.Height;
            var relW = (double)sel.Value.Width / physBounds.Width;
            var relH = (double)sel.Value.Height / physBounds.Height;

            var logX = Math.Clamp(relX * bounds.Width, 0, bounds.Width);
            var logY = Math.Clamp(relY * bounds.Height, 0, bounds.Height);
            var logW = Math.Clamp(relW * bounds.Width, 0, bounds.Width - logX);
            var logH = Math.Clamp(relH * bounds.Height, 0, bounds.Height - logY);

            var right = logX + logW;
            var bottom = logY + logH;

            // 4 dimming quadrants surrounding the selection cutout
            if (logY > 0)
                context.FillRectangle(DimBrush, new Rect(0, 0, bounds.Width, logY));
            if (bounds.Height > bottom)
                context.FillRectangle(DimBrush, new Rect(0, bottom, bounds.Width, bounds.Height - bottom));
            if (logX > 0)
                context.FillRectangle(DimBrush, new Rect(0, logY, logX, logH));
            if (bounds.Width > right)
                context.FillRectangle(DimBrush, new Rect(right, logY, bounds.Width - right, logH));

            // Cyan selection border
            context.DrawRectangle(BorderPen, new Rect(logX, logY, logW, logH));
        }
        else
        {
            context.FillRectangle(DimBrush, new Rect(0, 0, bounds.Width, bounds.Height));
        }
    }
}
