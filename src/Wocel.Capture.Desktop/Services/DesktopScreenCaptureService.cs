using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using SkiaSharp;
using Wocel.Capture.Models;
using Wocel.Capture.Platform.Capture;
using PixelRect = Wocel.Capture.Models.PixelRect;

namespace Wocel.Capture.Desktop.Services;

public sealed class DesktopScreenCaptureService : IScreenCaptureService
{
    public Task<CapturePermissionStatus> GetPermissionStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CapturePermissionStatus.Granted);

    public Task<CapturePermissionStatus> RequestPermissionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CapturePermissionStatus.Granted);

    public Task OpenPermissionSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsMacOS())
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture",
                    UseShellExecute = true
                });
            }
            catch
            {
                // Ignore fallback
            }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DisplayGeometry>> GetDisplaysAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<DisplayGeometry>();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.Screens is { } screens)
        {
            int index = 0;
            foreach (var screen in screens.All)
            {
                var scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
                var phys = new PixelRect(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);
                var log = new LogicalRect(
                    screen.Bounds.X / scale,
                    screen.Bounds.Y / scale,
                    Math.Max(1, screen.Bounds.Width / scale),
                    Math.Max(1, screen.Bounds.Height / scale));

                list.Add(new DisplayGeometry(
                    id: screen.DisplayName ?? $"display_{index++}",
                    physicalBounds: phys,
                    logicalBounds: log,
                    scaleFactor: scale,
                    isPrimary: screen.IsPrimary));
            }
        }

        if (list.Count == 0)
        {
            list.Add(new DisplayGeometry("default", new PixelRect(0, 0, 1920, 1080), new LogicalRect(0, 0, 1920, 1080), 1.0, isPrimary: true));
        }

        return Task.FromResult<IReadOnlyList<DisplayGeometry>>(list);
    }

    public async Task<CapturedFrame> CaptureVirtualDesktopAsync(CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsMacOS())
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"wocel_capture_{Guid.NewGuid():N}.bmp");
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "/usr/sbin/screencapture",
                    Arguments = $"-x -t bmp \"{tempFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process != null)
                {
                    await process.WaitForExitAsync(cancellationToken);
                }

                if (File.Exists(tempFile))
                {
                    using var raw = SKBitmap.Decode(tempFile);
                    if (raw != null && raw.Width > 0 && raw.Height > 0)
                    {
                        var bgra = raw.ColorType == SKColorType.Bgra8888 ? raw : raw.Copy(SKColorType.Bgra8888);
                        var pixels = bgra.GetPixelSpan().ToArray();
                        if (!ReferenceEquals(bgra, raw)) bgra.Dispose();

                        return new CapturedFrame(
                            raw.Width,
                            raw.Height,
                            raw.RowBytes,
                            CapturePixelFormat.Bgra8888,
                            new PixelRect(0, 0, raw.Width, raw.Height),
                            pixels);
                    }
                }
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { /* ignore */ }
                }
            }
        }

        const int fallbackWidth = 1920;
        const int fallbackHeight = 1080;
        const int stride = fallbackWidth * 4;
        var dummyBytes = new byte[stride * fallbackHeight];
        return new CapturedFrame(
            fallbackWidth,
            fallbackHeight,
            stride,
            CapturePixelFormat.Bgra8888,
            new PixelRect(0, 0, fallbackWidth, fallbackHeight),
            dummyBytes);
    }

    public static CapturedFrame Crop(CapturedFrame source, PixelRect crop)
    {
        var clampedX = Math.Clamp(crop.X, 0, source.Width);
        var clampedY = Math.Clamp(crop.Y, 0, source.Height);
        var clampedWidth = Math.Clamp(crop.Width, 2, source.Width - clampedX);
        var clampedHeight = Math.Clamp(crop.Height, 2, source.Height - clampedY);

        var stride = clampedWidth * 4;
        var bytes = new byte[stride * clampedHeight];
        var srcSpan = source.Pixels.Span;

        for (int y = 0; y < clampedHeight; y++)
        {
            var srcRowStart = (clampedY + y) * source.Stride + (clampedX * 4);
            var srcRow = srcSpan.Slice(srcRowStart, stride);
            var dstRow = bytes.AsSpan(y * stride, stride);
            srcRow.CopyTo(dstRow);
        }

        return new CapturedFrame(
            clampedWidth,
            clampedHeight,
            stride,
            CapturePixelFormat.Bgra8888,
            new PixelRect(0, 0, clampedWidth, clampedHeight),
            bytes);
    }
}
