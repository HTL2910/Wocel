using System.IO;

namespace Wocel.Shell.Services;

public class EnvironmentService
{
    private static readonly string EnvFilePath = Path.Combine(
        Directory.GetCurrentDirectory(),
        ".env");

    private static Dictionary<string, string> _envVariables = new();

    static EnvironmentService()
    {
        LoadEnvironment();
    }

    private static void LoadEnvironment()
    {
        try
        {
            if (File.Exists(EnvFilePath))
            {
                var lines = File.ReadAllLines(EnvFilePath);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                        continue;

                    var parts = trimmed.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim();
                        var value = parts[1].Trim();
                        _envVariables[key] = value;
                    }
                }
            }
        }
        catch
        {
            // Use default values if .env file cannot be loaded
        }
    }

    public static string Get(string key, string defaultValue = "")
    {
        if (_envVariables.TryGetValue(key, out var value))
        {
            return value;
        }
        return defaultValue;
    }

    public static int GetInt(string key, int defaultValue = 0)
    {
        var value = Get(key);
        if (int.TryParse(value, out var result))
        {
            return result;
        }
        return defaultValue;
    }

    public static bool GetBool(string key, bool defaultValue = false)
    {
        var value = Get(key);
        if (bool.TryParse(value, out var result))
        {
            return result;
        }
        return defaultValue;
    }

    // Supabase Configuration
    public static string SupabaseUrl => Get("SUPABASE_URL");
    public static string SupabaseAnonKey => Get("SUPABASE_ANON_KEY");
    public static string SupabaseStorageBucket => Get("SUPABASE_STORAGE_BUCKET", "wocel-documents");

    // Cloudinary Configuration
    public static string CloudinaryCloudName => Get("CLOUDINARY_CLOUD_NAME");
    public static string CloudinaryApiKey => Get("CLOUDINARY_API_KEY");
    public static string CloudinaryApiSecret => Get("CLOUDINARY_API_SECRET");
    public static string CloudinaryUploadPreset => Get("CLOUDINARY_UPLOAD_PRESET");

    // Application Settings
    public static string Environment => Get("WOCEL_ENV", "Development");
    public static bool EnableCloudSync => GetBool("WOCEL_ENABLE_CLOUD_SYNC", false);
    public static int AutoSaveIntervalSeconds => GetInt("WOCEL_AUTO_SAVE_INTERVAL_SECONDS", 60);

    public static bool IsConfigured => !string.IsNullOrEmpty(SupabaseUrl) && !string.IsNullOrEmpty(SupabaseAnonKey);
}
