namespace Wocel.Capture.Desktop.Composition;

public sealed record AppLaunchOptions(bool StartHidden)
{
    public static AppLaunchOptions Parse(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return new AppLaunchOptions(arguments.Any(argument =>
            string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase)));
    }
}
