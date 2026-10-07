namespace Wocel.Core.Services;

public static class EnvLoader
{
    public static void Load(string? filePath = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            // Search in current directory, parent directories, or app base
            var currentDir = AppDomain.CurrentDomain.BaseDirectory;
            var searchDir = new DirectoryInfo(currentDir);

            while (searchDir != null)
            {
                var candidate = Path.Combine(searchDir.FullName, ".env");
                if (File.Exists(candidate))
                {
                    filePath = candidate;
                    break;
                }
                searchDir = searchDir.Parent;
            }
        }

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return;

        foreach (var line in File.ReadAllLines(filePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                continue;

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0)
                continue;

            var key = trimmed.Substring(0, separatorIndex).Trim();
            var value = trimmed.Substring(separatorIndex + 1).Trim().Trim('"', '\'');

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    public static string Get(string key, string defaultValue = "")
    {
        return Environment.GetEnvironmentVariable(key) ?? defaultValue;
    }

    public static bool GetBool(string key, bool defaultValue = false)
    {
        var val = Environment.GetEnvironmentVariable(key);
        return bool.TryParse(val, out var result) ? result : defaultValue;
    }
}
