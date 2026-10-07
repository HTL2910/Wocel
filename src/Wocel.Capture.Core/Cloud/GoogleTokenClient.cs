using System.Net.Http.Json;
using System.Text.Json;

namespace Wocel.Capture.Cloud;

public sealed record GoogleTokenResult(string AccessToken, int ExpiresInSeconds, string? RefreshToken, string TokenType);

public sealed class GoogleTokenClient(HttpClient httpClient)
{
    private static readonly Uri TokenEndpoint = new("https://oauth2.googleapis.com/token");

    public Task<GoogleTokenResult> RefreshAsync(string clientId, string refreshToken, CancellationToken cancellationToken) =>
        RequestAsync(new Dictionary<string, string>
        {
            ["client_id"] = Required(clientId, nameof(clientId)),
            ["refresh_token"] = Required(refreshToken, nameof(refreshToken)),
            ["grant_type"] = "refresh_token"
        }, cancellationToken);

    public Task<GoogleTokenResult> ExchangeCodeAsync(string clientId, string code, string verifier, Uri redirectUri, CancellationToken cancellationToken) =>
        RequestAsync(new Dictionary<string, string>
        {
            ["client_id"] = Required(clientId, nameof(clientId)),
            ["code"] = Required(code, nameof(code)),
            ["code_verifier"] = Required(verifier, nameof(verifier)),
            ["redirect_uri"] = redirectUri.AbsoluteUri,
            ["grant_type"] = "authorization_code"
        }, cancellationToken);

    private async Task<GoogleTokenResult> RequestAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint) { Content = new FormUrlEncodedContent(form) };
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new DriveOperationException("TOKEN_EXCHANGE_FAILED", true);
        }

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        if (!root.TryGetProperty("access_token", out var accessTokenElement)
            || string.IsNullOrWhiteSpace(accessTokenElement.GetString())
            || !root.TryGetProperty("expires_in", out var expiresElement)
            || !expiresElement.TryGetInt32(out var expiresIn)
            || expiresIn <= 0)
        {
            throw new DriveOperationException("TOKEN_RESPONSE_INVALID", true);
        }

        var tokenType = root.TryGetProperty("token_type", out var typeElement) ? typeElement.GetString() : null;
        var refreshToken = root.TryGetProperty("refresh_token", out var refreshElement) ? refreshElement.GetString() : null;
        return new GoogleTokenResult(accessTokenElement.GetString()!, expiresIn, refreshToken, tokenType ?? "Bearer");
    }

    private static string Required(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value;
    }
}
