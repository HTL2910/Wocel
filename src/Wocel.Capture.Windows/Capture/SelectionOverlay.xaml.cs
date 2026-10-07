using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Wocel.Capture.Models;

namespace Wocel.Capture.Windows.Capture;

public partial class SelectionOverlay : Window
{
    private readonly CapturedDesktop _desktop;
    private System.Windows.Point _start;
    private Rect _selection;
    private bool _dragging;

    public SelectionOverlay(CapturedDesktop desktop)
    {
        InitializeComponent();
        _desktop = desktop;
        FrozenImage.Source = desktop.Bitmap;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Loaded += (_, _) => Focus();
    }

    public PixelRect? SelectedRegion { get; private set; }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _start = e.GetPosition(this);
        CaptureMouse();
        UpdateSelection(_start);
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragging)
        {
            UpdateSelection(e.GetPosition(this));
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();
        UpdateSelection(e.GetPosition(this));
    }

    private void UpdateSelection(System.Windows.Point current)
    {
        _selection = new Rect(_start, current);
        UpdateSelectionVisual();
    }

    private void UpdateSelectionVisual()
    {
        SelectionBox.Visibility = Visibility.Visible;
        SelectionBox.Width = _selection.Width;
        SelectionBox.Height = _selection.Height;
        System.Windows.Controls.Canvas.SetLeft(SelectionBox, _selection.Left);
        System.Windows.Controls.Canvas.SetTop(SelectionBox, _selection.Top);
        SizeBadge.Visibility = Visibility.Visible;
        var physicalTopLeft = PointToScreen(_selection.TopLeft);
        var physicalBottomRight = PointToScreen(_selection.BottomRight);
        SizeText.Text = $"{Math.Round(Math.Abs(physicalBottomRight.X - physicalTopLeft.X))} × {Math.Round(Math.Abs(physicalBottomRight.Y - physicalTopLeft.Y))}";
        System.Windows.Controls.Canvas.SetLeft(SizeBadge, _selection.Left);
        System.Windows.Controls.Canvas.SetTop(SizeBadge, Math.Max(0, _selection.Top - 30));
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            return;
        }

        if (e.Key == Key.Enter)
        {
            ConfirmSelection();
            return;
        }

        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down && !_selection.IsEmpty)
        {
            var pixels = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            var physical = new Vector(
                e.Key == Key.Left ? -pixels : e.Key == Key.Right ? pixels : 0,
                e.Key == Key.Up ? -pixels : e.Key == Key.Down ? pixels : 0);
            var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var dip = transform.Transform(physical);
            var offsetX = Math.Clamp(dip.X, -_selection.Left, ActualWidth - _selection.Right);
            var offsetY = Math.Clamp(dip.Y, -_selection.Top, ActualHeight - _selection.Bottom);
            _selection.Offset(offsetX, offsetY);
            _start = _selection.TopLeft;
            UpdateSelectionVisual();
            e.Handled = true;
        }
    }

    private void ConfirmSelection()
    {
        if (_selection.Width < 2 || _selection.Height < 2)
        {
            return;
        }

        var topLeft = PointToScreen(_selection.TopLeft);
        var bottomRight = PointToScreen(_selection.BottomRight);
        SelectedRegion = PixelRect.FromPoints(
            new PixelPoint((int)Math.Round(topLeft.X), (int)Math.Round(topLeft.Y)),
            new PixelPoint((int)Math.Round(bottomRight.X), (int)Math.Round(bottomRight.Y)));
        DialogResult = true;
    }

    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        ConfirmSelection();
    }
}
