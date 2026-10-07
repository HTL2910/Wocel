using System.Windows.Interop;
using Wocel.Capture.Windows.Native;

namespace Wocel.Capture.Windows.Services;

public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x5743;
    private const uint NoRepeat = 0x4000;
    private HwndSource? _source;

    public event EventHandler? Pressed;

    public bool Register(nint windowHandle, uint modifiers = 0, uint virtualKey = NativeMethods.VkSnapshot)
    {
        Dispose();
        _source = HwndSource.FromHwnd(windowHandle);
        _source?.AddHook(WndProc);
        if (!NativeMethods.RegisterHotKey(windowHandle, HotkeyId, modifiers | NoRepeat, virtualKey))
        {
            _source?.RemoveHook(WndProc);
            _source = null;
            return false;
        }

        return true;
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == NativeMethods.WmHotkey && wParam == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return 0;
    }

    public void Dispose()
    {
        if (_source is null)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _source.RemoveHook(WndProc);
        _source = null;
    }
}
