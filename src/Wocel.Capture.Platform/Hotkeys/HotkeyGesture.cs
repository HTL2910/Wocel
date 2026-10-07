namespace Wocel.Capture.Platform.Hotkeys;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Meta = 8
}

public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    public static bool TryParse(string? value, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            var parsed = parts[index].ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => HotkeyModifiers.Control,
                "SHIFT" => HotkeyModifiers.Shift,
                "ALT" or "OPTION" => HotkeyModifiers.Alt,
                "META" or "WIN" or "WINDOWS" or "CMD" or "COMMAND" => HotkeyModifiers.Meta,
                _ => HotkeyModifiers.None
            };
            if (parsed == HotkeyModifiers.None || modifiers.HasFlag(parsed))
            {
                return false;
            }
            modifiers |= parsed;
        }

        if (!TryNormalizeKey(parts[^1], out var key))
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    private static bool TryNormalizeKey(string value, out string key)
    {
        var candidate = value.Trim().ToUpperInvariant();
        if (candidate is "PRINTSCREEN" or "PRTSC" or "SNAPSHOT")
        {
            key = "PrintScreen";
            return true;
        }
        if (candidate.Length == 1 && (char.IsAsciiLetter(candidate[0]) || char.IsAsciiDigit(candidate[0])))
        {
            key = candidate;
            return true;
        }
        if (candidate.Length is 2 or 3 && candidate[0] == 'F'
            && int.TryParse(candidate[1..], out var functionKey) && functionKey is >= 1 and <= 24)
        {
            key = $"F{functionKey}";
            return true;
        }

        key = string.Empty;
        return false;
    }
}

public sealed record HotkeyRegistrationResult(bool Succeeded, string? ErrorCode = null);

public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler? Pressed;
    HotkeyRegistrationResult Register(HotkeyGesture gesture);
    void Unregister();
}
