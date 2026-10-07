using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Wocel.Capture.Desktop.Services;

public sealed class ImageKitSettings
{
    public string UrlEndpoint { get; set; } = string.Empty;
    public string PrivateApiKey { get; set; } = string.Empty;
    public string Folder { get; set; } = "/wocel-captures";
    public bool AutoUpload { get; set; } = false;
    public string LocalSaveDirectory { get; set; } = string.Empty;

    public string GetEffectiveLocalSaveDirectory()
    {
        if (!string.IsNullOrWhiteSpace(LocalSaveDirectory) && Directory.Exists(LocalSaveDirectory))
        {
            return LocalSaveDirectory;
        }
        var defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures", "WocelCaptures");
        Directory.CreateDirectory(defaultDir);
        return defaultDir;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(UrlEndpoint) &&
        !string.IsNullOrWhiteSpace(PrivateApiKey);

    // Security: Never output plaintext secret key in string logs/representations
    public override string ToString() =>
        $"ImageKitSettings [Endpoint={UrlEndpoint}, Folder={Folder}, AutoUpload={AutoUpload}, KeyConfigured={!string.IsNullOrWhiteSpace(PrivateApiKey)}]";
}

public static class ImageKitSettingsService
{
    public const string ApiKeysUrl = "https://imagekit.io/dashboard/developer/api-keys";
    public const string RegistrationUrl = "https://imagekit.io/registration";

    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".wocel");

    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "imagekit.json");

    public static ImageKitSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<ImageKitSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch
        {
            // Fallback to default on read failure
        }
        return new ImageKitSettings();
    }

    public static void Save(ImageKitSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);

            // Security: Enforce User Read/Write only (0600) on Unix/macOS to protect secrets
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(SettingsFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch
                {
                    // Best effort permission hardening
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save ImageKit settings: {ex.Message}");
        }
    }

    public static void OpenBrowserUrl(string url)
    {
        // Security: Strict URL validation to prevent command/protocol injection (e.g. file://, smb://, javascript:)
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        try
        {
            if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    ArgumentList = { uri.AbsoluteUri },
                    UseShellExecute = false
                });
            }
            else if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    ArgumentList = { uri.AbsoluteUri },
                    UseShellExecute = false
                });
            }
        }
        catch
        {
            // Best effort browser launch
        }
    }

    public static async Task<(bool Succeeded, string Message)> TestConnectionAsync(string urlEndpoint, string privateKey)
    {
        if (string.IsNullOrWhiteSpace(urlEndpoint))
            return (false, "Vui lòng nhập URL-endpoint");

        if (string.IsNullOrWhiteSpace(privateKey))
            return (false, "Vui lòng nhập Private API Key");

        if (!Uri.TryCreate(urlEndpoint, UriKind.Absolute, out var endpointUri) || endpointUri.Scheme != Uri.UriSchemeHttps)
            return (false, "URL-endpoint phải bắt đầu bằng https:// để bảo mật");

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.imagekit.io/v1/files?limit=1");
            var authBytes = Encoding.ASCII.GetBytes($"{privateKey.Trim()}:");
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

            using var resp = await http.SendAsync(req);
            if (resp.IsSuccessStatusCode)
            {
                return (true, "Kết nối ImageKit thành công! Sẵn sàng upload.");
            }
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return (false, "Private API Key không đúng (401 Unauthorized).");
            }
            return (false, $"Lỗi từ ImageKit: {(int)resp.StatusCode} {resp.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi kết nối: {ex.Message}");
        }
    }
}
