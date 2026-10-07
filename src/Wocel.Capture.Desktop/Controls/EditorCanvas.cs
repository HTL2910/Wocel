using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Wocel.Capture.Desktop.Rendering;
using Wocel.Capture.Desktop.ViewModels;
using Wocel.Capture.Platform.Capture;
using Wocel.Capture.Presentation;
using Wocel.Capture.Rendering;
using CapturePixelPoint = Wocel.Capture.Models.PixelPoint;
using CapturePixelRect = Wocel.Capture.Models.PixelRect;
using CapturePixelSize = Wocel.Capture.Models.PixelSize;

namespace Wocel.Capture.Desktop.Controls;

public sealed class EditorCanvas : Control
{
    private readonly SkiaEditorRenderer _renderer = new();
    private readonly List<CapturePixelPoint> _stroke = [];
    private CapturePixelPoint? _start;
    private CapturePixelPoint? _currentPointer;
    private WriteableBitmap? _cachedBitmap;
    private bool _isDirty = true;
    private EditorWindowViewModel? _editor;

    public CapturedFrame? Source { get; set; }
    public CapturePixelRect? PendingCrop { get; set; }
    public event EventHandler? CropPendingChanged;
    public event Action<CapturePixelRect>? TextInputRequested;

    public EditorWindowViewModel? Editor
    {
        get => _editor;
        set
        {
            if (_editor != null)
            {
                _editor.DocumentChanged -= OnDocumentChanged;
            }
            _editor = value;
            if (_editor != null)
            {
                _editor.DocumentChanged += OnDocumentChanged;
            }
            MarkDirty();
        }
    }

    public EditorCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
    }

    public void MarkDirty()
    {
        _isDirty = true;
        InvalidateVisual();
    }

    private void OnDocumentChanged(object? sender, EventArgs e) => MarkDirty();

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#070E1C")), Bounds);
        if (Source is null || Editor is null || Bounds.Width < 1 || Bounds.Height < 1) return;

        if (_isDirty || _cachedBitmap is null)
        {
            _cachedBitmap?.Dispose();
            using var rendered = _renderer.Render(Source, Editor.Document);
            _cachedBitmap = AvaloniaBitmapAdapter.Copy(rendered);
            _isDirty = false;
        }

        var fitted = GetImageFittedBounds();
        context.DrawImage(_cachedBitmap, fitted);

        // 1. Live Real-time Preview during mouse drag
        if (_start.HasValue && _currentPointer.HasValue)
        {
            var dragBox = CapturePixelRect.FromPoints(_start.Value, _currentPointer.Value);
            var canvasRect = ToCanvasRect(dragBox);
            var activeColor = Color.Parse(Editor.CurrentColorHex);
            var activeBrush = new SolidColorBrush(activeColor);
            var strokePen = new Pen(activeBrush, 3, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            var shapePen = new Pen(activeBrush, 2.5);

            switch (Editor.SelectedTool)
            {
                case EditorTool.Pen:
                    if (_stroke.Count > 1)
                    {
                        for (int i = 0; i < _stroke.Count - 1; i++)
                        {
                            context.DrawLine(strokePen, ToCanvasPoint(_stroke[i]), ToCanvasPoint(_stroke[i + 1]));
                        }
                    }
                    break;

                case EditorTool.Highlight:
                    if (_stroke.Count > 1)
                    {
                        var hlPen = new Pen(new SolidColorBrush(Color.FromArgb(130, 253, 224, 71)), 14, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                        for (int i = 0; i < _stroke.Count - 1; i++)
                        {
                            context.DrawLine(hlPen, ToCanvasPoint(_stroke[i]), ToCanvasPoint(_stroke[i + 1]));
                        }
                    }
                    break;

                case EditorTool.Arrow:
                    var ap1 = ToCanvasPoint(_start.Value);
                    var ap2 = ToCanvasPoint(_currentPointer.Value);
                    DrawLiveArrow(context, ap1, ap2, strokePen);
                    break;

                case EditorTool.Line:
                    var lp1 = ToCanvasPoint(_start.Value);
                    var lp2 = ToCanvasPoint(_currentPointer.Value);
                    context.DrawLine(strokePen, lp1, lp2);
                    break;

                case EditorTool.Rectangle:
                    context.DrawRectangle(shapePen, canvasRect);
                    break;

                case EditorTool.Ellipse:
                    context.DrawEllipse(null, shapePen,
                        canvasRect.Center, canvasRect.Width / 2, canvasRect.Height / 2);
                    break;

                case EditorTool.Blur:
                    context.FillRectangle(new SolidColorBrush(Color.FromArgb(70, 6, 182, 212)), canvasRect);
                    context.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#06B6D4")), 1.5, DashStyle.Dash), canvasRect);
                    break;

                case EditorTool.Crop:
                    DrawCropScrim(context, fitted, canvasRect);
                    context.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#EA580C")), 2), canvasRect);
                    break;

                case EditorTool.Text:
                    context.FillRectangle(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), canvasRect);
                    context.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#06B6D4")), 1.5, DashStyle.Dash), canvasRect);
                    break;
            }
        }

        // 2. Pending Crop Region (Staging mode)
        if (PendingCrop.HasValue)
        {
            var cropCanvasRect = ToCanvasRect(PendingCrop.Value);
            DrawCropScrim(context, fitted, cropCanvasRect);
            context.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#EA580C")), 2.5), cropCanvasRect);
            DrawResizeHandles(context, cropCanvasRect, Color.Parse("#EA580C"));
        }
    }

    private static void DrawLiveArrow(DrawingContext context, Point start, Point end, IPen pen)
    {
        context.DrawLine(pen, start, end);
        var dx = start.X - end.X;
        var dy = start.Y - end.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 2) return;
        var ux = dx / length;
        var uy = dy / length;
        var nx = -uy;
        var ny = ux;
        var head = Math.Max(12.0, pen.Thickness * 4.0);
        var arrowPoint1 = new Point(end.X + ux * head + nx * head * 0.45, end.Y + uy * head + ny * head * 0.45);
        var arrowPoint2 = new Point(end.X + ux * head - nx * head * 0.45, end.Y + uy * head - ny * head * 0.45);
        context.DrawLine(pen, end, arrowPoint1);
        context.DrawLine(pen, end, arrowPoint2);
    }

    public CapturePixelPoint ToSourcePoint(Point point)
    {
        if (Editor is null) throw new InvalidOperationException("Editor is not assigned.");
        var fitted = GetImageFittedBounds();
        var local = new CapturePixelPoint(
            (int)Math.Round(Math.Clamp(point.X - fitted.X, 0, Math.Max(0, fitted.Width - 1))),
            (int)Math.Round(Math.Clamp(point.Y - fitted.Y, 0, Math.Max(0, fitted.Height - 1))));
        return EditorCanvasMapper.ToSource(Editor.Document, local, new CapturePixelSize((int)Math.Max(1, fitted.Width), (int)Math.Max(1, fitted.Height)));
    }

    public Rect ToCanvasRect(CapturePixelRect sourceRect)
    {
        if (Editor is null) return default;
        var fitted = GetImageFittedBounds();
        var sourceBounds = new CapturePixelRect(0, 0, Editor.Document.SourceSize.Width, Editor.Document.SourceSize.Height);
        var crop = Editor.Document.Layers.OfType<Wocel.Capture.Editor.CropLayer>().LastOrDefault()?.CropBounds ?? sourceBounds;

        if (crop.Width <= 0 || crop.Height <= 0) return default;

        var relX = (sourceRect.X - crop.X) / (double)crop.Width;
        var relY = (sourceRect.Y - crop.Y) / (double)crop.Height;
        var relW = sourceRect.Width / (double)crop.Width;
        var relH = sourceRect.Height / (double)crop.Height;

        return new Rect(
            fitted.X + relX * fitted.Width,
            fitted.Y + relY * fitted.Height,
            Math.Max(1, relW * fitted.Width),
            Math.Max(1, relH * fitted.Height));
    }

    public Point ToCanvasPoint(CapturePixelPoint sourcePoint)
    {
        if (Editor is null) return default;
        var fitted = GetImageFittedBounds();
        var sourceBounds = new CapturePixelRect(0, 0, Editor.Document.SourceSize.Width, Editor.Document.SourceSize.Height);
        var crop = Editor.Document.Layers.OfType<Wocel.Capture.Editor.CropLayer>().LastOrDefault()?.CropBounds ?? sourceBounds;

        if (crop.Width <= 0 || crop.Height <= 0) return default;

        var relX = (sourcePoint.X - crop.X) / (double)crop.Width;
        var relY = (sourcePoint.Y - crop.Y) / (double)crop.Height;

        return new Point(
            fitted.X + relX * fitted.Width,
            fitted.Y + relY * fitted.Height);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Editor is null || Source is null) return;
        Focus();
        _start = ToSourcePoint(e.GetPosition(this));
        _currentPointer = _start;
        _stroke.Clear();
        _stroke.Add(_start.Value);
        e.Pointer.Capture(this);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_start is null || Editor is null) return;
        _currentPointer = ToSourcePoint(e.GetPosition(this));

        if (Editor.SelectedTool is EditorTool.Pen or EditorTool.Highlight)
        {
            if (_stroke.Count == 0 || _stroke[^1] != _currentPointer.Value)
                _stroke.Add(_currentPointer.Value);
        }

        InvalidateVisual();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_start is not { } start || Editor is null) return;
        var end = ToSourcePoint(e.GetPosition(this));

        switch (Editor.SelectedTool)
        {
            case EditorTool.Pen:
            case EditorTool.Highlight:
                if (_stroke.Count == 1) _stroke.Add(end);
                Editor.AddStroke(Editor.SelectedTool, _stroke);
                break;
            case EditorTool.Text:
                var textRect = CapturePixelRect.FromPoints(start, end);
                if (textRect.Width < 20 || textRect.Height < 14)
                    textRect = new CapturePixelRect(start.X, start.Y, 180, 40);
                TextInputRequested?.Invoke(textRect);
                break;
            case EditorTool.Blur:
                var blurRect = CapturePixelRect.FromPoints(start, end);
                if (blurRect.Width >= 4 && blurRect.Height >= 4)
                    Editor.AddBlur(blurRect, 12);
                break;
            case EditorTool.Crop:
                var cropBox = CapturePixelRect.FromPoints(start, end);
                if (cropBox.Width >= 16 && cropBox.Height >= 16)
                {
                    PendingCrop = cropBox;
                    CropPendingChanged?.Invoke(this, EventArgs.Empty);
                }
                break;
            case EditorTool.Line:
            case EditorTool.Arrow:
            case EditorTool.Rectangle:
            case EditorTool.Ellipse:
            case EditorTool.Triangle:
                Editor.AddShape(Editor.SelectedTool, start, end);
                break;
        }

        _start = null;
        _currentPointer = null;
        _stroke.Clear();
        e.Pointer.Capture(null);
        MarkDirty();
    }

    public void ConfirmCrop()
    {
        if (PendingCrop.HasValue && Editor != null)
        {
            Editor.ApplyCrop(PendingCrop.Value);
            PendingCrop = null;
            CropPendingChanged?.Invoke(this, EventArgs.Empty);
            Editor.SelectTool(EditorTool.Arrow);
            MarkDirty();
        }
    }

    public void CancelCrop()
    {
        if (PendingCrop.HasValue)
        {
            PendingCrop = null;
            CropPendingChanged?.Invoke(this, EventArgs.Empty);
            Editor?.SelectTool(EditorTool.Arrow);
            MarkDirty();
        }
    }

    private static void DrawResizeHandles(DrawingContext context, Rect rect, Color handleColor)
    {
        var points = new[]
        {
            rect.TopLeft,
            new Point(rect.X + rect.Width / 2, rect.Y),
            rect.TopRight,
            new Point(rect.Right, rect.Y + rect.Height / 2),
            rect.BottomRight,
            new Point(rect.X + rect.Width / 2, rect.Bottom),
            rect.BottomLeft,
            new Point(rect.X, rect.Y + rect.Height / 2)
        };

        var handleBrush = new SolidColorBrush(Colors.White);
        var borderPen = new Pen(new SolidColorBrush(handleColor), 1.5);
        const double size = 7.0;

        foreach (var pt in points)
        {
            var hRect = new Rect(pt.X - size / 2, pt.Y - size / 2, size, size);
            context.FillRectangle(handleBrush, hRect);
            context.DrawRectangle(borderPen, hRect);
        }
    }

    private static void DrawCropScrim(DrawingContext context, Rect fitted, Rect cropRect)
    {
        var scrimBrush = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0));
        if (cropRect.Y > fitted.Y)
            context.FillRectangle(scrimBrush, new Rect(fitted.X, fitted.Y, fitted.Width, Math.Max(0, cropRect.Y - fitted.Y)));
        if (fitted.Bottom > cropRect.Bottom)
            context.FillRectangle(scrimBrush, new Rect(fitted.X, cropRect.Bottom, fitted.Width, Math.Max(0, fitted.Bottom - cropRect.Bottom)));
        if (cropRect.X > fitted.X)
            context.FillRectangle(scrimBrush, new Rect(fitted.X, cropRect.Y, Math.Max(0, cropRect.X - fitted.X), cropRect.Height));
        if (fitted.Right > cropRect.Right)
            context.FillRectangle(scrimBrush, new Rect(cropRect.Right, cropRect.Y, Math.Max(0, fitted.Right - cropRect.Right), cropRect.Height));
    }

    private CapturePixelSize RenderedSize()
    {
        if (Editor is null) return default;
        if (Editor.Document.Layers.OfType<Wocel.Capture.Editor.ResizeLayer>().LastOrDefault()?.OutputSize is { } resize)
            return resize;
        var sourceBounds = new CapturePixelRect(0, 0, Editor.Document.SourceSize.Width, Editor.Document.SourceSize.Height);
        var crop = Editor.Document.Layers.OfType<Wocel.Capture.Editor.CropLayer>().LastOrDefault()?.CropBounds ?? sourceBounds;
        return new CapturePixelSize(Math.Max(1, crop.Width), Math.Max(1, crop.Height));
    }

    private Rect GetImageFittedBounds()
    {
        var size = RenderedSize();
        return Fit(size.Width, size.Height);
    }

    private Rect Fit(int width, int height)
    {
        var scale = Math.Min(Bounds.Width / width, Bounds.Height / height);
        var fittedWidth = Math.Max(1, width * scale);
        var fittedHeight = Math.Max(1, height * scale);
        return new Rect((Bounds.Width - fittedWidth) / 2, (Bounds.Height - fittedHeight) / 2, fittedWidth, fittedHeight);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_editor != null)
        {
            _editor.DocumentChanged -= OnDocumentChanged;
        }
        _cachedBitmap?.Dispose();
        _cachedBitmap = null;
    }
}
