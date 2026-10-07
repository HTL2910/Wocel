using Microsoft.Win32;
using Wocel.Capture.Platform.Services;

namespace Wocel.Capture.Platform.Windows;

public sealed class WindowsStartupService(string? executablePath = null) : IStartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WocelCapture";
    private readonly string _executablePath = executablePath ?? Environment.ProcessPath
        ?? throw new InvalidOperationException("The current executable path is unavailable.");

    public bool IsEnabled
    {
        get
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows startup registration is available only on Windows.");
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return string.Equals(key?.GetValue(ValueName) as string, BuildCommand(_executablePath), StringComparison.Ordinal);
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows startup registration is available only on Windows.");
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("Windows startup settings are unavailable.");
        if (enabled) key.SetValue(ValueName, BuildCommand(_executablePath), RegistryValueKind.String);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public static string BuildCommand(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (executablePath.Contains('"')) throw new ArgumentException("Executable path cannot contain a quote.", nameof(executablePath));
        return $"\"{executablePath}\" --background";
    }
}
