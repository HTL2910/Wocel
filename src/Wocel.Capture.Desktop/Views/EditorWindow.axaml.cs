using Wocel.Capture.Cloud;
using Wocel.Capture.Desktop.Services;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Wocel.Capture.Desktop.ViewModels;
using Wocel.Capture.Editor;
using Wocel.Capture.Models;
using Wocel.Capture.Platform.Capture;
using Wocel.Capture.Presentation;
using Wocel.Capture.Rendering;

namespace Wocel.Capture.Desktop.Views;

public sealed partial class EditorWindow : Window
{
    private PixelRect _pendingTextRect;
    private string _selectedTextColor = "#06B6D4";
    private double _selectedFontSize = 18;

    public EditorWindow() : this(EmptyFrame()) { }

    public EditorWindow(CapturedFrame frame, bool autoUploadDefault = false)
    {
        InitializeComponent();
        var viewModel = new EditorWindowViewModel(new EditorDocument(new PixelSize(frame.Width, frame.Height)), autoUploadDefault);
        DataContext = viewModel;
        Canvas.Source = frame;
        Canvas.Editor = viewModel;
        viewModel.DocumentChanged += (_, _) => Canvas.InvalidateVisual();

        Canvas.CropPendingChanged += (_, _) =>
        {
            CropActionBar.IsVisible = Canvas.PendingCrop.HasValue;
        };

        Canvas.TextInputRequested += OnTextInputRequested;

        var ikSettings = ImageKitSettingsService.Load();
        ImageKitUrlEndpointBox.Text = ikSettings.UrlEndpoint;
        ImageKitPrivateKeyBox.Text = ikSettings.PrivateApiKey;
        ImageKitFolderBox.Text = string.IsNullOrWhiteSpace(ikSettings.Folder) ? "/wocel-captures" : ikSettings.Folder;
        if (ikSettings.AutoUpload && ikSettings.IsConfigured)
        {
            viewModel.AutoUpload = true;
        }
    }

    private void SelectTool(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name } && Enum.TryParse<EditorTool>(name, out var tool) && DataContext is EditorWindowViewModel viewModel)
        {
            viewModel.SelectTool(tool);
            if (tool != EditorTool.Crop) Canvas.CancelCrop();
        }
    }

    private void SelectDrawTool(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string name } && Enum.TryParse<EditorTool>(name, out var tool) && DataContext is EditorWindowViewModel viewModel)
        {
            viewModel.SelectTool(tool);
            Canvas.CancelCrop();
            UpdateDrawMenuHeader(name);
        }
    }

    private void UpdateDrawMenuHeader(string name)
    {
        ActiveDrawLabel.Text = name switch
        {
            "Pen" => "Bút vẽ",
            "Highlight" => "Dạ quang",
            "Arrow" => "Mũi tên",
            "Rectangle" => "Chữ nhật",
            "Ellipse" => "Elip",
            "Line" => "Đường thẳng",
            _ => "Vẽ & Khối"
        };
    }

    private void SelectTextColor(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
        {
            _selectedTextColor = hex;
            var brush = new SolidColorBrush(Color.Parse(hex));
            TextInputBox.Foreground = brush;
            ActiveColorIndicator.Background = brush;
        }
    }

    private void SelectFontSize(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string sizeStr } && double.TryParse(sizeStr, out var size))
        {
            _selectedFontSize = size;
            TextInputBox.FontSize = Math.Max(12, size);
        }
    }

    private void ConfirmCropClick(object? sender, RoutedEventArgs e) => Canvas.ConfirmCrop();

    private void CancelCropClick(object? sender, RoutedEventArgs e) => Canvas.CancelCrop();

    private void OnTextInputRequested(PixelRect rect)
    {
        _pendingTextRect = rect;
        TextInputBox.Text = string.Empty;
        var brush = new SolidColorBrush(Color.Parse(_selectedTextColor));
        TextInputBox.Foreground = brush;
        TextInputBox.FontSize = Math.Max(12, _selectedFontSize);
        ActiveColorIndicator.Background = brush;
        TextInputCard.IsVisible = true;
        TextInputBox.Focus();
    }

    private void ConfirmTextInputClick(object? sender, RoutedEventArgs e)
    {
        var text = TextInputBox.Text?.Trim();
        if (!string.IsNullOrEmpty(text) && DataContext is EditorWindowViewModel viewModel)
        {
            viewModel.AddText(_pendingTextRect, text, _selectedTextColor, _selectedFontSize);
        }
        TextInputCard.IsVisible = false;
        Canvas.Focus();
    }

    private void CancelTextInputClick(object? sender, RoutedEventArgs e)
    {
        TextInputCard.IsVisible = false;
        Canvas.Focus();
    }

    private void TextInputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            ConfirmTextInputClick(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelTextInputClick(sender, e);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (TextInputCard.IsVisible) return;

        if (Canvas.PendingCrop.HasValue)
        {
            if (e.Key == Key.Enter)
            {
                Canvas.ConfirmCrop();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                Canvas.CancelCrop();
                e.Handled = true;
                return;
            }
        }

        if (DataContext is EditorWindowViewModel viewModel)
        {
            if (e.Key == Key.C) { viewModel.SelectTool(EditorTool.Crop); e.Handled = true; }
            else if (e.Key == Key.T) { viewModel.SelectTool(EditorTool.Text); e.Handled = true; }
            else if (e.Key == Key.B) { viewModel.SelectTool(EditorTool.Blur); e.Handled = true; }
            else if (e.Key == Key.A) { viewModel.SelectTool(EditorTool.Arrow); UpdateDrawMenuHeader("Arrow"); e.Handled = true; }
            else if (e.Key == Key.P) { viewModel.SelectTool(EditorTool.Pen); UpdateDrawMenuHeader("Pen"); e.Handled = true; }
            else if (e.Key == Key.R) { viewModel.SelectTool(EditorTool.Rectangle); UpdateDrawMenuHeader("Rectangle"); e.Handled = true; }
            else if (e.Key == Key.Delete || e.Key == Key.Back) { viewModel.DeleteSelected(); e.Handled = true; }
        }
    }

    private void ImageKitCheckClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorWindowViewModel viewModel && viewModel.AutoUpload)
        {
            var settings = ImageKitSettingsService.Load();
            if (!settings.IsConfigured)
            {
                OpenImageKitSettingsClick(sender, e);
                ImageKitStatusLabel.Text = "⚠️ Vui lòng cấu hình API Key để bật tự động tải lên ImageKit.";
                ImageKitStatusLabel.Foreground = Brushes.Orange;
            }
        }
    }

    private void OpenImageKitSettingsClick(object? sender, RoutedEventArgs e)
    {
        var settings = ImageKitSettingsService.Load();
        ImageKitUrlEndpointBox.Text = settings.UrlEndpoint;
        ImageKitPrivateKeyBox.Text = settings.PrivateApiKey;
        ImageKitFolderBox.Text = string.IsNullOrWhiteSpace(settings.Folder) ? "/wocel-captures" : settings.Folder;
        ImageKitStatusLabel.Text = settings.IsConfigured
            ? "✅ Đã cấu hình ImageKit Cloud."
            : "Chưa cấu hình. Vui lòng lấy API Key theo hướng dẫn trên.";
        ImageKitStatusLabel.Foreground = settings.IsConfigured ? Brushes.LightGreen : Brushes.Gray;
        ImageKitModalOverlay.IsVisible = true;
    }

    private void CloseImageKitSettingsClick(object? sender, RoutedEventArgs e)
    {
        ImageKitModalOverlay.IsVisible = false;
    }

    private void OpenImageKitApiKeysPage(object? sender, RoutedEventArgs e)
    {
        ImageKitSettingsService.OpenBrowserUrl(ImageKitSettingsService.ApiKeysUrl);
    }

    private void ToggleApiKeyVisibilityClick(object? sender, RoutedEventArgs e)
    {
        ImageKitPrivateKeyBox.PasswordChar = ImageKitPrivateKeyBox.PasswordChar == '•' ? ' ' : '•';
    }

    private async void TestImageKitConnectionClick(object? sender, RoutedEventArgs e)
    {
        var url = ImageKitUrlEndpointBox.Text?.Trim() ?? string.Empty;
        var key = ImageKitPrivateKeyBox.Text?.Trim() ?? string.Empty;

        ImageKitStatusLabel.Text = "⏳ Đang kiểm tra kết nối ImageKit...";
        ImageKitStatusLabel.Foreground = Brushes.Cyan;

        var (succeeded, msg) = await ImageKitSettingsService.TestConnectionAsync(url, key);
        ImageKitStatusLabel.Text = msg;
        ImageKitStatusLabel.Foreground = succeeded ? Brushes.LightGreen : Brushes.Red;
    }

    private void SaveImageKitSettingsClick(object? sender, RoutedEventArgs e)
    {
        var url = ImageKitUrlEndpointBox.Text?.Trim() ?? string.Empty;
        var key = ImageKitPrivateKeyBox.Text?.Trim() ?? string.Empty;
        var folder = ImageKitFolderBox.Text?.Trim() ?? "/wocel-captures";

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
        {
            ImageKitStatusLabel.Text = "❌ Vui lòng nhập đầy đủ URL-endpoint và Private API Key.";
            ImageKitStatusLabel.Foreground = Brushes.Red;
            return;
        }

        var settings = new ImageKitSettings
        {
            UrlEndpoint = url,
            PrivateApiKey = key,
            Folder = folder,
            AutoUpload = true
        };
        ImageKitSettingsService.Save(settings);

        if (DataContext is EditorWindowViewModel viewModel)
        {
            viewModel.AutoUpload = true;
        }

        ImageKitModalOverlay.IsVisible = false;
    }

    private async void ExportImage(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorWindowViewModel viewModel || Canvas.Source is null) return;
        try
        {
            viewModel.BeginExport();
            var renderer = new SkiaEditorRenderer();
            using var rendered = renderer.Render(Canvas.Source, viewModel.Document);
            var pngBytes = ImageCodec.Encode(rendered, CaptureImageFormat.Png, 100);

            var picturesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures", "WocelCaptures");
            Directory.CreateDirectory(picturesDir);
            var fileName = $"Wocel_Capture_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var filePath = Path.Combine(picturesDir, fileName);
            await File.WriteAllBytesAsync(filePath, pngBytes);

            var ikSettings = ImageKitSettingsService.Load();
            bool uploaded = false;

            if (viewModel.AutoUpload && ikSettings.IsConfigured)
            {
                try
                {
                    var client = new ImageKitDriveClient(new HttpClient(), new ImageKitConfig(ikSettings.UrlEndpoint, ikSettings.PrivateApiKey, ikSettings.Folder));
                    var uploadReq = new DriveUploadRequest(
                        Guid.NewGuid(), filePath, fileName, "image/png", pngBytes.Length);
                    var uploadRes = await client.UploadAndShareAsync(uploadReq, CancellationToken.None);

                    if (uploadRes.WebLink != null)
                    {
                        var linkStr = uploadRes.WebLink.ToString();
                        uploaded = true;
                        CopyToMacClipboard(linkStr, "Đã sao chép link ảnh ImageKit vào Clipboard!");
                    }
                }
                catch
                {
                    // Fallback to local copy if cloud fails
                }
            }

            if (!uploaded)
            {
                CopyPngToMacClipboard(filePath);
            }

            viewModel.FinishExport(succeeded: true);
            Close();
        }
        catch (Exception)
        {
            viewModel.FinishExport(succeeded: false);
        }
    }

    private static void CopyToMacClipboard(string text, string? notification = null)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "pbcopy",
                UseShellExecute = false,
                RedirectStandardInput = true
            });
            if (p != null)
            {
                p.StandardInput.Write(text);
                p.StandardInput.Close();
                p.WaitForExit();
            }
            if (!string.IsNullOrEmpty(notification))
            {
                using var np = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "osascript",
                    ArgumentList = { "-e", $"display notification '{notification}' with title 'Wocel Capture'" },
                    UseShellExecute = false
                });
                np?.WaitForExit();
            }
        }
        catch { /* ignore */ }
    }

    private static void CopyPngToMacClipboard(string filePath)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "osascript",
                ArgumentList = { "-e", $"set the clipboard to (read (POSIX file '{filePath}') as «class PNGf»)" },
                UseShellExecute = false
            });
            p?.WaitForExit();
        }
        catch { /* ignore */ }
    }

    private static CapturedFrame EmptyFrame() => new(
        2, 2, 8, CapturePixelFormat.Bgra8888, new PixelRect(0, 0, 2, 2),
        new byte[] { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 });
}
