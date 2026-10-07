using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Wocel.Capture.Desktop.Rendering;
using Wocel.Capture.Desktop.ViewModels;
using Wocel.Capture.Platform.Capture;

namespace Wocel.Capture.Desktop.Views;

public sealed partial class SelectionOverlayWindow : Window
{
    private DisplayGeometry? _display;
    private bool _isDragging;

    public SelectionOverlayWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        PointerPressed += PointerPressedOnOverlay;
        PointerMoved += PointerMovedOnOverlay;
        PointerReleased += PointerReleasedOnOverlay;
        DoubleTapped += (_, _) => Confirmed?.Invoke(this, EventArgs.Empty);
    }

    public SelectionOverlayWindow(DisplayGeometry display, SelectionOverlayViewModel viewModel) : this(display, viewModel, null)
    {
    }

    public SelectionOverlayWindow(DisplayGeometry display, SelectionOverlayViewModel viewModel, CapturedFrame? frame) : this()
    {
        _display = display;
        DataContext = viewModel;
        Position = new Avalonia.PixelPoint((int)Math.Round(display.LogicalBounds.X), (int)Math.Round(display.LogicalBounds.Y));
        Width = display.LogicalBounds.Width;
        Height = display.LogicalBounds.Height;

        OverlayCanvas.Display = display;
        OverlayCanvas.ViewModel = viewModel;
        if (frame != null)
        {
            BackgroundImage.Source = AvaloniaBitmapAdapter.FromFrame(frame);
        }

        viewModel.Controller.SelectionChanged += (_, _) =>
        {
            OverlayCanvas.InvalidateVisual();
        };
    }

    public event EventHandler? Confirmed;
    public event EventHandler? Cancelled;

    private void PointerPressedOnOverlay(object? sender, PointerPressedEventArgs e)
    {
        if (_display is null || DataContext is not SelectionOverlayViewModel viewModel) return;
        _isDragging = true;
        var point = CanvasToLogical(e.GetPosition(this));
        viewModel.Controller.BeginDrag(_display.Id, point.X, point.Y);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void PointerMovedOnOverlay(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _display is null || DataContext is not SelectionOverlayViewModel viewModel) return;
        var point = CanvasToLogical(e.GetPosition(this));
        viewModel.Controller.UpdateDrag(_display.Id, point.X, point.Y);
        e.Handled = true;
    }

    private void PointerReleasedOnOverlay(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;

        if (DataContext is SelectionOverlayViewModel viewModel)
        {
            if (viewModel.Selection.IsUsableSelection && viewModel.Selection.Width >= 8 && viewModel.Selection.Height >= 8)
            {
                Confirmed?.Invoke(this, EventArgs.Empty);
                return;
            }
        }
        OverlayCanvas.InvalidateVisual();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not SelectionOverlayViewModel viewModel) return;
        var coarse = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var handled = true;
        switch (e.Key)
        {
            case Key.Left: viewModel.Controller.Nudge(-1, 0, coarse); break;
            case Key.Right: viewModel.Controller.Nudge(1, 0, coarse); break;
            case Key.Up: viewModel.Controller.Nudge(0, -1, coarse); break;
            case Key.Down: viewModel.Controller.Nudge(0, 1, coarse); break;
            case Key.Enter: Confirmed?.Invoke(this, EventArgs.Empty); break;
            case Key.Escape: Cancelled?.Invoke(this, EventArgs.Empty); break;
            default: handled = false; break;
        }
        e.Handled = handled;
    }

    private Avalonia.Point CanvasToLogical(Avalonia.Point local)
    {
        if (_display is null || Bounds.Width <= 0 || Bounds.Height <= 0) return local;

        var normX = Math.Clamp(local.X / Bounds.Width, 0.0, 1.0);
        var normY = Math.Clamp(local.Y / Bounds.Height, 0.0, 1.0);

        return new Avalonia.Point(
            _display.LogicalBounds.X + normX * _display.LogicalBounds.Width,
            _display.LogicalBounds.Y + normY * _display.LogicalBounds.Height);
    }
}
