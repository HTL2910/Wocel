namespace Wocel.Capture.Models;

public readonly record struct GlobalHotkey(uint Modifiers, uint VirtualKey)
{
    private const uint Alt = 0x0001;
    private const uint Control = 0x0002;
    private const uint Shift = 0x0004;
    private const uint Windows = 0x0008;

    public static bool TryParse(string? value, out GlobalHotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        uint modifiers = 0;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            var modifier = parts[index].ToUpperInvariant() switch
            {
                "ALT" => Alt,
                "CTRL" or "CONTROL" => Control,
                "SHIFT" => Shift,
                "WIN" or "WINDOWS" => Windows,
                _ => 0u
            };
            if (modifier == 0 || (modifiers & modifier) != 0)
            {
                return false;
            }
            modifiers |= modifier;
        }

        if (!TryParseKey(parts[^1], out var virtualKey))
        {
            return false;
        }

        hotkey = new GlobalHotkey(modifiers, virtualKey);
        return true;
    }

    private static bool TryParseKey(string value, out uint virtualKey)
    {
        var key = value.Trim().ToUpperInvariant();
        if (key is "PRINTSCREEN" or "PRTSC" or "SNAPSHOT")
        {
            virtualKey = 0x2C;
            return true;
        }
        if (key.Length == 1 && (key[0] is >= 'A' and <= 'Z' or >= '0' and <= '9'))
        {
            virtualKey = key[0];
            return true;
        }
        if (key.Length is 2 or 3 && key[0] == 'F' && int.TryParse(key[1..], out var functionKey) && functionKey is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + functionKey - 1);
            return true;
        }

        virtualKey = 0;
        return false;
    }
}
