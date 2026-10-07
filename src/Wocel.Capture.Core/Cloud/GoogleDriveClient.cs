using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Wocel.Capture.Cloud;

public interface IAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
    Task InvalidateAsync(CancellationToken cancellationToken);
}

public static class GoogleDriveQuery
{
    public static string EscapeLiteral(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
}

public sealed class GoogleDriveClient(HttpClient httpClient, IAccessTokenProvider tokenProvider) : IDriveClient
{
    private const string ApiRoot = "https://www.googleapis.com";
    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private const string FolderName = "Wocel Capture";

    public async Task<DriveUploadResult> UploadAndShareAsync(DriveUploadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!File.Exists(request.LocalPath))
        {
            throw new DriveOperationException("LOCAL_FILE_MISSING", false);
        }

        var uploaded = request.ExistingFileId is { Length: > 0 } existingId
            ? (FileId: existingId, WebLink: request.ExistingWebLink)
            : await UploadNewFileAsync(request, cancellationToken).ConfigureAwait(false);
        if (!request.CreatePublicLink)
        {
            return new DriveUploadResult(uploaded.FileId, uploaded.WebLink, false, null);
        }

        string? sharingError;
        try
        {
            sharingError = await ShareAsync(uploaded.FileId, cancellationToken).ConfigureAwait(false);
        }
        catch (DriveOperationException exception)
        {
            throw new DriveOperationException(exception.ErrorCode, exception.RequiresAuthentication, exception, uploaded.FileId, uploaded.WebLink);
        }
        catch (OperationCanceledException exception)
        {
            throw new DriveOperationException("SHARING_CANCELLED", false, exception, uploaded.FileId, uploaded.WebLink);
        }
        catch (HttpRequestException exception)
        {
            throw new DriveOperationException("SHARING_NETWORK", false, exception, uploaded.FileId, uploaded.WebLink);
        }
        return sharingError is null
            ? DriveUploadResult.Shared(uploaded.FileId, uploaded.WebLink ?? new Uri($"https://drive.google.com/file/d/{Uri.EscapeDataString(uploaded.FileId)}/view"))
            : DriveUploadResult.Private(uploaded.FileId, sharingError);
    }

    private async Task<(string FileId, Uri? WebLink)> UploadNewFileAsync(DriveUploadRequest request, CancellationToken cancellationToken)
    {
        var folderId = await EnsureFolderAsync(cancellationToken).ConfigureAwait(false);
        var location = await StartResumableUploadAsync(request, folderId, cancellationToken).ConfigureAwait(false);
        return await UploadBytesAsync(request, location, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> EnsureFolderAsync(CancellationToken cancellationToken)
    {
        var query = $"mimeType='{FolderMimeType}' and name='{GoogleDriveQuery.EscapeLiteral(FolderName)}' and trashed=false";
        var uri = new Uri($"{ApiRoot}/drive/v3/files?q={Uri.EscapeDataString(query)}&spaces=drive&fields=files(id,name)&pageSize=1");
        using var listResponse = await SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken).ConfigureAwait(false);
        using (var document = await ParseJsonAsync(listResponse, cancellationToken).ConfigureAwait(false))
        {
            if (document.RootElement.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array && files.GetArrayLength() > 0
                && files[0].TryGetProperty("id", out var id) && !string.IsNullOrWhiteSpace(id.GetString()))
            {
                return id.GetString()!;
            }
        }

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, $"{ApiRoot}/drive/v3/files?fields=id")
        {
            Content = JsonContent.Create(new { name = FolderName, mimeType = FolderMimeType })
        };
        using var createResponse = await SendAsync(createRequest, cancellationToken).ConfigureAwait(false);
        using var created = await ParseJsonAsync(createResponse, cancellationToken).ConfigureAwait(false);
        return RequiredString(created.RootElement, "id", "FOLDER_RESPONSE_INVALID");
    }

    private async Task<Uri> StartResumableUploadAsync(DriveUploadRequest request, string folderId, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{ApiRoot}/upload/drive/v3/files?uploadType=resumable&fields=id,webViewLink")
        {
            Content = JsonContent.Create(new { name = request.DisplayName, parents = new[] { folderId } })
        };
        message.Headers.TryAddWithoutValidation("X-Upload-Content-Type", request.ContentType);
        message.Headers.TryAddWithoutValidation("X-Upload-Content-Length", request.ByteSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var response = await SendAsync(message, cancellationToken).ConfigureAwait(false);
        if (response.Headers.Location is not { } location || !location.IsAbsoluteUri || location.Scheme != Uri.UriSchemeHttps
            || !(location.Host.Equals("googleapis.com", StringComparison.OrdinalIgnoreCase)
                || location.Host.EndsWith(".googleapis.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new DriveOperationException("UPLOAD_LOCATION_INVALID", false);
        }
        return location;
    }

    private async Task<(string FileId, Uri? WebLink)> UploadBytesAsync(DriveUploadRequest request, Uri location, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(request.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var message = new HttpRequestMessage(HttpMethod.Put, location) { Content = new StreamContent(stream) };
        message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType);
        message.Content.Headers.ContentLength = stream.Length;
        using var response = await SendAsync(message, cancellationToken).ConfigureAwait(false);
        using var document = await ParseJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var id = RequiredString(document.RootElement, "id", "UPLOAD_RESPONSE_INVALID");
        Uri? link = null;
        if (document.RootElement.TryGetProperty("webViewLink", out var linkElement)
            && Uri.TryCreate(linkElement.GetString(), UriKind.Absolute, out var parsed)
            && parsed.Scheme == Uri.UriSchemeHttps)
        {
            link = parsed;
        }
        return (id, link);
    }

    private async Task<string?> ShareAsync(string fileId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiRoot}/drive/v3/files/{Uri.EscapeDataString(fileId)}/permissions?fields=id")
        {
            Content = JsonContent.Create(new { type = "anyone", role = "reader", allowFileDiscovery = false })
        };
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await tokenProvider.InvalidateAsync(CancellationToken.None).ConfigureAwait(false);
            throw new DriveOperationException("AUTH_REQUIRED", true);
        }
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return "SHARING_POLICY";
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new DriveOperationException($"DRIVE_HTTP_{(int)response.StatusCode}", false);
        }
        return null;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false));
        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            await tokenProvider.InvalidateAsync(CancellationToken.None).ConfigureAwait(false);
            throw new DriveOperationException("AUTH_REQUIRED", true);
        }
        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            response.Dispose();
            throw new DriveOperationException($"DRIVE_HTTP_{(int)status}", false);
        }
        return response;
    }

    private static async Task<JsonDocument> ParseJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new DriveOperationException("DRIVE_RESPONSE_INVALID", false, exception);
        }
    }

    private static string RequiredString(JsonElement root, string name, string errorCode)
    {
        if (!root.TryGetProperty(name, out var element) || string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new DriveOperationException(errorCode, false);
        }
        return element.GetString()!;
    }
}
