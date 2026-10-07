using System.Runtime.InteropServices;
using Wocel.Capture.Platform.Hotkeys;

namespace Wocel.Capture.Platform.Windows;

public readonly record struct WindowsHotkey(uint Modifiers, uint VirtualKey);

public sealed partial class WindowsHotkeyService : IGlobalHotkeyService
{
    private const int HotkeyId = 0x5743;
    private const uint NoRepeat = 0x4000;
    private const uint WmHotkey = 0x0312;
    private const uint WmQuit = 0x0012;
    private Thread? _thread;
    private uint _threadId;

    public event EventHandler? Pressed;

    public static WindowsHotkey ToNative(HotkeyGesture gesture)
    {
        var modifiers = NoRepeat;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Alt)) modifiers |= 0x0001;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Control)) modifiers |= 0x0002;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Shift)) modifiers |= 0x0004;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Meta)) modifiers |= 0x0008;
        var key = gesture.Key switch
        {
            "PrintScreen" => 0x2Cu,
            { Length: 1 } value when char.IsAsciiLetterOrDigit(value[0]) => char.ToUpperInvariant(value[0]),
            _ when gesture.Key.StartsWith('F') && int.TryParse(gesture.Key[1..], out var number) && number is >= 1 and <= 24 => (uint)(0x70 + number - 1),
            _ => 0u
        };
        if (key == 0) throw new ArgumentException("Unsupported Windows hotkey.", nameof(gesture));
        return new WindowsHotkey(modifiers, key);
    }

    public HotkeyRegistrationResult Register(HotkeyGesture gesture)
    {
        Unregister();
        if (!OperatingSystem.IsWindows()) return new HotkeyRegistrationResult(false, "PLATFORM_UNSUPPORTED");
        var native = ToNative(gesture);
        var ready = new TaskCompletionSource<HotkeyRegistrationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() => RunMessageLoop(native, ready)) { IsBackground = true, Name = "Wocel Windows hotkey" };
        _thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    }

    public void Unregister()
    {
        var thread = _thread;
        if (thread is null) return;
        if (_threadId != 0) PostThreadMessage(_threadId, WmQuit, 0, 0);
        thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _threadId = 0;
    }

    public void Dispose() => Unregister();

    private void RunMessageLoop(WindowsHotkey hotkey, TaskCompletionSource<HotkeyRegistrationResult> ready)
    {
        _threadId = GetCurrentThreadId();
        if (!RegisterHotKey(0, HotkeyId, hotkey.Modifiers, hotkey.VirtualKey))
        {
            ready.TrySetResult(new HotkeyRegistrationResult(false, "HOTKEY_CONFLICT"));
            return;
        }
        ready.TrySetResult(new HotkeyRegistrationResult(true));
        try
        {
            while (GetMessage(out var message, 0, 0, 0) > 0)
            {
                if (message.Message == WmHotkey && message.WParam == HotkeyId) Pressed?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            UnregisterHotKey(0, HotkeyId);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint HWnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint window, int id);
    [LibraryImport("user32.dll")]
    private static partial int GetMessage(out NativeMessage message, nint window, uint minimum, uint maximum);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);
    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}
