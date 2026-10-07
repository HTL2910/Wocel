using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Wocel.Capture.Models;
using Wocel.Capture.Windows.Native;

namespace Wocel.Capture.Windows.Capture;

public sealed record CapturedDesktop(BitmapSource Bitmap, PixelRect PhysicalBounds);

public interface IScreenCaptureService
{
    CapturedDesktop CaptureVirtualDesktop();
    BitmapSource Crop(CapturedDesktop desktop, PixelRect physicalSelection);
}

public sealed class ScreenCaptureService : IScreenCaptureService
{
    public CapturedDesktop CaptureVirtualDesktop()
    {
        var bounds = new PixelRect(
            NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen),
            NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen),
            NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen),
            NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen));
        if (!bounds.IsUsableSelection)
        {
            throw new InvalidOperationException("Windows did not report a usable virtual desktop.");
        }

        var screenDc = NativeMethods.GetDC(0);
        var memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
        var bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, bounds.Width, bounds.Height);
        var previous = NativeMethods.SelectObject(memoryDc, bitmap);
        try
        {
            if (!NativeMethods.BitBlt(memoryDc, 0, 0, bounds.Width, bounds.Height, screenDc, bounds.X, bounds.Y, NativeMethods.Srccopy))
            {
                throw new Win32Exception("Unable to copy the virtual desktop.");
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return new CapturedDesktop(source, bounds);
        }
        finally
        {
            NativeMethods.SelectObject(memoryDc, previous);
            NativeMethods.DeleteObject(bitmap);
            NativeMethods.DeleteDC(memoryDc);
            NativeMethods.ReleaseDC(0, screenDc);
        }
    }

    public BitmapSource Crop(CapturedDesktop desktop, PixelRect physicalSelection)
    {
        if (!physicalSelection.IsUsableSelection)
        {
            throw new ArgumentOutOfRangeException(nameof(physicalSelection));
        }

        var x = physicalSelection.X - desktop.PhysicalBounds.X;
        var y = physicalSelection.Y - desktop.PhysicalBounds.Y;
        if (x < 0 || y < 0 || x + physicalSelection.Width > desktop.Bitmap.PixelWidth || y + physicalSelection.Height > desktop.Bitmap.PixelHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(physicalSelection), "Selection lies outside the captured desktop.");
        }

        var cropped = new CroppedBitmap(desktop.Bitmap, new Int32Rect(x, y, physicalSelection.Width, physicalSelection.Height));
        cropped.Freeze();
        return cropped;
    }
}
