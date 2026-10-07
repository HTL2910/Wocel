using System.ComponentModel;
using System.Runtime.InteropServices;
using Wocel.Capture.Models;
using Wocel.Capture.Platform.Capture;

namespace Wocel.Capture.Platform.Windows;

public sealed record WindowsCaptureBuffer(int Stride, byte[] Pixels);

public interface IWindowsScreenCaptureNative
{
    PixelRect GetVirtualDesktopBounds();
    IReadOnlyList<DisplayGeometry> GetDisplays();
    WindowsCaptureBuffer Capture(PixelRect requestedBounds);
}

public sealed class WindowsScreenCaptureService(IWindowsScreenCaptureNative? native = null) : IScreenCaptureService
{
    private readonly IWindowsScreenCaptureNative _native = native ?? new GdiWindowsScreenCaptureNative();

    public Task<CapturePermissionStatus> GetPermissionStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CapturePermissionStatus.NotRequired);

    public Task<CapturePermissionStatus> RequestPermissionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CapturePermissionStatus.NotRequired);

    public Task OpenPermissionSettingsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<DisplayGeometry>> GetDisplaysAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_native.GetDisplays());
    }

    public Task<CapturedFrame> CaptureVirtualDesktopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bounds = _native.GetVirtualDesktopBounds();
        if (!bounds.IsUsableSelection) throw new InvalidOperationException("Windows did not report a usable virtual desktop.");
        var buffer = _native.Capture(bounds);
        var expectedStride = checked(bounds.Width * 4);
        if (buffer.Stride != expectedStride || buffer.Pixels.Length != checked(expectedStride * bounds.Height))
        {
            throw new InvalidOperationException("Windows returned an invalid BGRA capture buffer.");
        }
        return Task.FromResult(new CapturedFrame(bounds.Width, bounds.Height, buffer.Stride,
            CapturePixelFormat.Bgra8888, bounds, buffer.Pixels));
    }
}

internal sealed partial class GdiWindowsScreenCaptureNative : IWindowsScreenCaptureNative
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const uint Srccopy = 0x00CC0020;
    private const uint DibRgbColors = 0;

    public PixelRect GetVirtualDesktopBounds()
    {
        EnsureWindows();
        return new PixelRect(GetSystemMetrics(SmXVirtualScreen), GetSystemMetrics(SmYVirtualScreen),
            GetSystemMetrics(SmCxVirtualScreen), GetSystemMetrics(SmCyVirtualScreen));
    }

    public IReadOnlyList<DisplayGeometry> GetDisplays()
    {
        EnsureWindows();
        var displays = new List<DisplayGeometry>();
        var index = 0;
        if (!EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
            {
                var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
                if (!GetMonitorInfo(monitor, ref info)) throw NativeFailure("GetMonitorInfo");
                var physical = new PixelRect(info.Monitor.Left, info.Monitor.Top,
                    info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top);
                var scale = 1d;
                try
                {
                    if (GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0) scale = dpiX / 96d;
                }
                catch (DllNotFoundException)
                {
                    scale = 1;
                }
                var logical = new LogicalRect(physical.X / scale, physical.Y / scale,
                    physical.Width / scale, physical.Height / scale);
                displays.Add(new DisplayGeometry($"windows-monitor-{index++}", physical, logical, scale, (info.Flags & 1) != 0));
                return true;
            }, 0))
            throw NativeFailure("EnumDisplayMonitors");
        if (displays.Count == 0) throw new InvalidOperationException("Windows did not report any displays.");
        return displays;
    }

    public WindowsCaptureBuffer Capture(PixelRect requestedBounds)
    {
        EnsureWindows();
        var screen = GetDC(0);
        if (screen == 0) throw NativeFailure("GetDC");
        nint memory = 0;
        nint bitmap = 0;
        nint previous = 0;
        try
        {
            memory = CreateCompatibleDC(screen);
            if (memory == 0) throw NativeFailure("CreateCompatibleDC");
            bitmap = CreateCompatibleBitmap(screen, requestedBounds.Width, requestedBounds.Height);
            if (bitmap == 0) throw NativeFailure("CreateCompatibleBitmap");
            previous = SelectObject(memory, bitmap);
            if (previous == 0 || previous == -1) throw NativeFailure("SelectObject");
            if (!BitBlt(memory, 0, 0, requestedBounds.Width, requestedBounds.Height, screen,
                    requestedBounds.X, requestedBounds.Y, Srccopy))
                throw NativeFailure("BitBlt");

            var stride = checked(requestedBounds.Width * 4);
            var pixels = new byte[checked(stride * requestedBounds.Height)];
            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = requestedBounds.Width,
                    Height = -requestedBounds.Height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                    SizeImage = (uint)pixels.Length
                }
            };
            var copied = GetDIBits(memory, bitmap, 0, (uint)requestedBounds.Height, pixels, ref info, DibRgbColors);
            if (copied != requestedBounds.Height) throw NativeFailure("GetDIBits");
            return new WindowsCaptureBuffer(stride, pixels);
        }
        finally
        {
            if (previous != 0 && previous != -1) SelectObject(memory, previous);
            if (bitmap != 0) DeleteObject(bitmap);
            if (memory != 0) DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("GDI capture is available only on Windows.");
    }

    private static Win32Exception NativeFailure(string operation) =>
        new(Marshal.GetLastWin32Error(), $"Windows screen capture failed during {operation}.");

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public uint Size; public NativeRect Monitor; public NativeRect Work; public uint Flags; }
    private delegate bool MonitorEnumProcedure(nint monitor, nint dc, nint bounds, nint data);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplayMonitors(nint dc, nint clip, MonitorEnumProcedure callback, nint data);
    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetDC(nint window);
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int ReleaseDC(nint window, nint dc);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint CreateCompatibleBitmap(nint dc, int width, int height);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint SelectObject(nint dc, nint value);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint value);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial int GetDIBits(nint dc, nint bitmap, uint start, uint lines, [Out] byte[] bits, ref BitmapInfo info, uint usage);
}
