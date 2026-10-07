using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Wocel.Capture.Cloud;

public sealed record ImageKitConfig(
    string UrlEndpoint,
    string PrivateApiKey,
    string Folder = "/wocel-captures");

public sealed class ImageKitDriveClient(HttpClient httpClient, ImageKitConfig config) : IDriveClient
{
    private const string UploadApiUrl = "https://upload.imagekit.io/api/v1/files/upload";

    public async Task<DriveUploadResult> UploadAndShareAsync(DriveUploadRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.UrlEndpoint) || string.IsNullOrWhiteSpace(config.PrivateApiKey))
        {
            throw new DriveOperationException("IMAGEKIT_NOT_CONFIGURED", true);
        }

        await using var stream = new FileStream(request.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var form = new MultipartFormDataContent();

        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType);
        form.Add(fileContent, "file", request.DisplayName);
        form.Add(new StringContent(request.DisplayName), "fileName");

        var folder = string.IsNullOrWhiteSpace(config.Folder) ? "/wocel-captures" : config.Folder;
        if (!folder.StartsWith('/')) folder = "/" + folder;
        form.Add(new StringContent(folder), "folder");
        form.Add(new StringContent("true"), "useUniqueFileName");

        using var message = new HttpRequestMessage(HttpMethod.Post, UploadApiUrl)
        {
            Content = form
        };

        var authBytes = Encoding.ASCII.GetBytes($"{config.PrivateApiKey}:");
        message.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

        using var response = await httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new DriveOperationException($"IMAGEKIT_{(int)response.StatusCode}", false);
        }

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
        var fileId = document.RootElement.GetProperty("fileId").GetString()!;
        var urlString = document.RootElement.GetProperty("url").GetString()!;
        var link = new Uri(urlString);

        return DriveUploadResult.Shared(fileId, link);
    }
}
