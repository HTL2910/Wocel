using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Wocel.Capture.Editor;
using Wocel.Capture.Logging;
using Wocel.Capture.Models;
using Wocel.Capture.Presentation;
using Wocel.Capture.Windows.Rendering;
using Wocel.Capture.Windows.Services;
using Point = System.Windows.Point;

namespace Wocel.Capture.Windows.Views;

public sealed record EditorExportResult(BitmapSource Image, string? Path, CaptureImageFormat Format, bool UploadRequested);

public partial class EditorWindow : Window
{
    private readonly BitmapSource _source;
    private readonly ImageExportService _export;
    private readonly Func<EditorExportResult, Task> _commitExport;
    private readonly EditorViewModel _viewModel;
    private readonly List<PixelPoint> _points = [];
    private PixelPoint _dragStart;
    private bool _dragging;
    private bool _allowClose;
    private bool _exportInProgress;
    private Guid? _selectedLayerId;

    public EditorWindow(BitmapSource source, bool autoUpload, ImageExportService export, Func<EditorExportResult, Task> commitExport)
    {
        InitializeComponent();
        _source = source;
        _export = export;
        _commitExport = commitExport ?? throw new ArgumentNullException(nameof(commitExport));
        _viewModel = new EditorViewModel(new EditorDocument(new PixelSize(source.PixelWidth, source.PixelHeight)), autoUpload);
        UploadCheck.IsChecked = autoUpload;
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, (_, _) => { if (_viewModel.Document.Undo()) _viewModel.NotifyDocumentChanged(); RefreshPreview(); }, (_, args) => args.CanExecute = _viewModel.Document.CanUndo));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, (_, _) => { if (_viewModel.Document.Redo()) _viewModel.NotifyDocumentChanged(); RefreshPreview(); }, (_, args) => args.CanExecute = _viewModel.Document.CanRedo));
        RefreshPreview();
    }

    private ShapeStyle CurrentStyle(bool highlight = false)
    {
        var item = (ComboBoxItem)ColorPicker.SelectedItem;
        var opacity = highlight ? Math.Min(0.45, OpacitySlider.Value) : OpacitySlider.Value;
        return new ShapeStyle(EditorColor.FromHex((string)item.Tag), StrokeSlider.Value, null, opacity);
    }

    private void OnToolClick(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectedTool = Enum.Parse<EditorTool>((string)((Button)sender).Tag);
        EditorStatus.Text = $"Công cụ: {((Button)sender).Content}";
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _points.Clear();
        _dragStart = ToPixel(e.GetPosition(EditorImage));
        if (_viewModel.SelectedTool == EditorTool.Select)
        {
            _selectedLayerId = HitTestLayer(_dragStart)?.Id;
            EditorStatus.Text = _selectedLayerId is null ? "Không có chú thích tại vị trí này." : "Đã chọn layer. Kéo để di chuyển hoặc bấm Xóa layer.";
            EditorImage.CaptureMouse();
            return;
        }
        _points.Add(_dragStart);
        EditorImage.CaptureMouse();
    }

    private void OnCanvasMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }
        var point = ToPixel(e.GetPosition(EditorImage));
        if (_viewModel.SelectedTool == EditorTool.Select)
        {
            if (_selectedLayerId is not null)
            {
                EditorStatus.Text = $"Di chuyển {point.X - _dragStart.X}, {point.Y - _dragStart.Y} px";
            }
            return;
        }
        if (_viewModel.SelectedTool is EditorTool.Pen or EditorTool.Highlight)
        {
            _points.Add(point);
        }
        EditorStatus.Text = $"{Math.Abs(point.X - _dragStart.X)} × {Math.Abs(point.Y - _dragStart.Y)} px";
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }
        _dragging = false;
        EditorImage.ReleaseMouseCapture();
        var end = ToPixel(e.GetPosition(EditorImage));
        if (_viewModel.SelectedTool == EditorTool.Select)
        {
            if (_selectedLayerId is { } selected && end != _dragStart)
            {
                _viewModel.Document.Execute(new MoveLayerCommand(selected, end.X - _dragStart.X, end.Y - _dragStart.Y));
                _viewModel.NotifyDocumentChanged();
                RefreshPreview();
            }
            return;
        }
        _points.Add(end);
        var bounds = PixelRect.FromPoints(_dragStart, end);
        EditorLayer? layer = _viewModel.SelectedTool switch
        {
            EditorTool.Pen when _points.Count > 1 => new FreehandLayer(Guid.NewGuid(), [.. _points], CurrentStyle()),
            EditorTool.Highlight when _points.Count > 1 => HighlightLayer.Create([.. _points], CurrentStyle(true)),
            EditorTool.Blur when bounds.IsUsableSelection => new BlurLayer(Guid.NewGuid(), bounds, Math.Max(8, StrokeSlider.Value * 3)),
            EditorTool.Crop when bounds.IsUsableSelection => new CropLayer(Guid.NewGuid(), bounds),
            EditorTool.Text => CreateTextLayer(bounds),
            EditorTool.Line when _dragStart != end => Shape(EditorLayerKind.Line, bounds, _dragStart, end),
            EditorTool.Arrow when _dragStart != end => Shape(EditorLayerKind.Arrow, bounds, _dragStart, end),
            EditorTool.Rectangle when bounds.IsUsableSelection => Shape(EditorLayerKind.Rectangle, bounds),
            EditorTool.Ellipse when bounds.IsUsableSelection => Shape(EditorLayerKind.Ellipse, bounds),
            EditorTool.Triangle when bounds.IsUsableSelection => Shape(EditorLayerKind.Triangle, bounds),
            _ => null
        };
        if (layer is not null)
        {
            _viewModel.Document.Execute(new AddLayerCommand(layer));
            _viewModel.NotifyDocumentChanged();
            RefreshPreview();
        }
    }

    private EditorLayer? CreateTextLayer(PixelRect bounds)
    {
        var text = TextPrompt.Show(this, "Nhập nội dung", "Thêm chữ");
        return string.IsNullOrWhiteSpace(text) ? null : new TextLayer(Guid.NewGuid(), bounds.IsUsableSelection ? bounds : new PixelRect(_dragStart.X, _dragStart.Y, 240, 60), text, CurrentStyle(), Math.Max(14, StrokeSlider.Value * 4));
    }

    private ShapeLayer Shape(EditorLayerKind kind, PixelRect bounds, PixelPoint? start = null, PixelPoint? end = null) =>
        new(Guid.NewGuid(), kind, bounds, CurrentStyle(), start, end);

    private EditorLayer? HitTestLayer(PixelPoint point)
    {
        const int tolerance = 8;
        return _viewModel.Document.Layers
            .Where(layer => layer is not CropLayer and not ResizeLayer)
            .Reverse()
            .FirstOrDefault(layer => point.X >= layer.Bounds.X - tolerance
                && point.X <= layer.Bounds.Right + tolerance
                && point.Y >= layer.Bounds.Y - tolerance
                && point.Y <= layer.Bounds.Bottom + tolerance);
    }

    private void OnDeleteLayerClick(object sender, RoutedEventArgs e)
    {
        if (_selectedLayerId is not { } selected || !_viewModel.Document.Layers.Any(layer => layer.Id == selected))
        {
            _selectedLayerId = null;
            EditorStatus.Text = "Chọn một chú thích trước khi xóa.";
            return;
        }

        _viewModel.Document.Execute(new DeleteLayerCommand(selected));
        _selectedLayerId = null;
        _viewModel.NotifyDocumentChanged();
        RefreshPreview();
        EditorStatus.Text = "Đã xóa layer; có thể hoàn tác bằng Ctrl+Z.";
    }

    private void OnResizeClick(object sender, RoutedEventArgs e)
    {
        var current = EditorRenderer.Render(_source, _viewModel.Document);
        _viewModel.Document.Execute(new AddLayerCommand(new ResizeLayer(Guid.NewGuid(), new PixelSize(Math.Max(2, current.PixelWidth * 3 / 4), Math.Max(2, current.PixelHeight * 3 / 4)))));
        _viewModel.NotifyDocumentChanged();
        RefreshPreview();
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Bỏ toàn bộ chỉnh sửa?", "Reset ảnh", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        _viewModel.Document.Reset();
        _viewModel.NotifyDocumentChanged();
        RefreshPreview();
    }

    private async void OnCopyClick(object sender, RoutedEventArgs e)
    {
        await RunExportAsync(async () =>
        {
            var image = EditorRenderer.Render(_source, _viewModel.Document);
            _export.CopyToClipboard(image);
            await CommitExportAsync(new EditorExportResult(image, null, CaptureImageFormat.Png, UploadCheck.IsChecked == true));
            EditorStatus.Text = "Đã copy ảnh vào clipboard.";
        });
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg", DefaultExt = ".png", FileName = $"Screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        var format = Path.GetExtension(dialog.FileName).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ? CaptureImageFormat.Jpeg : CaptureImageFormat.Png;
        await RunExportAsync(async () =>
        {
            var image = EditorRenderer.Render(_source, _viewModel.Document);
            await _export.SaveAsync(image, dialog.FileName, format);
            await CommitExportAsync(new EditorExportResult(image, dialog.FileName, format, UploadCheck.IsChecked == true));
            EditorStatus.Text = $"Đã lưu {dialog.FileName}";
        });
    }

    private async void OnUploadClick(object sender, RoutedEventArgs e)
    {
        await RunExportAsync(async () =>
        {
            var image = EditorRenderer.Render(_source, _viewModel.Document);
            await CommitExportAsync(new EditorExportResult(image, null, CaptureImageFormat.Png, true));
            _allowClose = true;
            DialogResult = true;
        });
    }

    private async Task CommitExportAsync(EditorExportResult result)
    {
        await _commitExport(result);
        _viewModel.MarkExported();
    }

    private async Task RunExportAsync(Func<Task> operation)
    {
        if (_exportInProgress)
        {
            return;
        }

        _exportInProgress = true;
        IsEnabled = false;
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            EditorStatus.Text = $"Không thể xuất ảnh: {SensitiveDataRedactor.Redact(exception.Message)}";
            MessageBox.Show(this, "Không thể xuất ảnh. Ảnh và các chỉnh sửa vẫn được giữ để bạn thử lại.", "Wocel Capture", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
            _exportInProgress = false;
        }
    }

    private void RefreshPreview()
    {
        if (_selectedLayerId is { } selected && !_viewModel.Document.Layers.Any(layer => layer.Id == selected))
        {
            _selectedLayerId = null;
        }
        var image = EditorRenderer.Render(_source, _viewModel.Document);
        EditorImage.Source = image;
        EditorImage.Width = image.PixelWidth;
        EditorImage.Height = image.PixelHeight;
        CommandManager.InvalidateRequerySuggested();
    }

    private PixelPoint ToPixel(Point point)
    {
        var rendered = (BitmapSource)EditorImage.Source;
        var canvasPoint = new PixelPoint(
            (int)Math.Clamp(Math.Round(point.X), 0, int.MaxValue),
            (int)Math.Clamp(Math.Round(point.Y), 0, int.MaxValue));
        return EditorCanvasMapper.ToSource(
            _viewModel.Document,
            canvasPoint,
            new PixelSize(rendered.PixelWidth, rendered.PixelHeight));
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_exportInProgress && !_allowClose)
        {
            e.Cancel = true;
            return;
        }
        if (_allowClose || !_viewModel.HasUnexportedChanges)
        {
            return;
        }
        if (MessageBox.Show(this, "Đóng và bỏ các thay đổi chưa xuất?", "Wocel Capture", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
        }
        else
        {
            _allowClose = true;
        }
    }
}

internal static class TextPrompt
{
    public static string? Show(Window owner, string label, string title)
    {
        var input = new TextBox { MinWidth = 280, Margin = new Thickness(0, 8, 0, 12) };
        var ok = new Button { Content = "Thêm", IsDefault = true, MinWidth = 80 };
        var cancel = new Button { Content = "Hủy", IsCancel = true, MinWidth = 80 };
        var dialog = new Window { Owner = owner, Title = title, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        ok.Click += (_, _) => dialog.DialogResult = true;
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children = { new TextBlock { Text = label }, input, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } } }
        };
        input.Focus();
        return dialog.ShowDialog() == true ? input.Text : null;
    }
}
