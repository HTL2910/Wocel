using Wocel.Capture.Editor;
using Wocel.Capture.Models;

namespace Wocel.Capture.Presentation;

public static class EditorCanvasMapper
{
    public static PixelPoint ToSource(EditorDocument document, PixelPoint canvasPoint, PixelSize renderedSize)
    {
        ArgumentNullException.ThrowIfNull(document);

        var sourceBounds = new PixelRect(0, 0, document.SourceSize.Width, document.SourceSize.Height);
        var requestedCrop = document.Layers.OfType<CropLayer>().LastOrDefault()?.CropBounds ?? sourceBounds;
        var crop = Intersect(requestedCrop, sourceBounds);
        if (!crop.IsUsableSelection)
        {
            throw new InvalidOperationException("Crop region is empty.");
        }

        var canvasX = Math.Clamp(canvasPoint.X, 0, renderedSize.Width - 1);
        var canvasY = Math.Clamp(canvasPoint.Y, 0, renderedSize.Height - 1);
        var sourceX = crop.X + (int)Math.Round(canvasX * crop.Width / (double)renderedSize.Width);
        var sourceY = crop.Y + (int)Math.Round(canvasY * crop.Height / (double)renderedSize.Height);

        return new PixelPoint(
            Math.Clamp(sourceX, crop.X, crop.Right - 1),
            Math.Clamp(sourceY, crop.Y, crop.Bottom - 1));
    }

    private static PixelRect Intersect(PixelRect left, PixelRect right)
    {
        var x = Math.Max(left.X, right.X);
        var y = Math.Max(left.Y, right.Y);
        var rightEdge = Math.Min(left.Right, right.Right);
        var bottom = Math.Min(left.Bottom, right.Bottom);
        return new PixelRect(x, y, Math.Max(0, rightEdge - x), Math.Max(0, bottom - y));
    }
}
