using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Wocel.Capture.Cloud;
using Wocel.Capture.Windows.Security;

namespace Wocel.Capture.Windows.Cloud;

public sealed class GoogleOAuthService : IAccessTokenProvider
{
    public const string DriveFileScope = "https://www.googleapis.com/auth/drive.file";
    private readonly string? _clientId;
    private readonly HttpClient _httpClient;
    private readonly GoogleTokenClient _tokenClient;
    private readonly IProtectedTokenStore _tokenStore;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public GoogleOAuthService(string? clientId, HttpClient httpClient, IProtectedTokenStore tokenStore)
    {
        _clientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim();
        _httpClient = httpClient;
        _tokenClient = new GoogleTokenClient(httpClient);
        _tokenStore = tokenStore;
    }

    public bool IsConfigured => _clientId is not null;

    public async Task<bool> HasStoredCredentialAsync(CancellationToken cancellationToken = default) =>
        !string.IsNullOrWhiteSpace(await _tokenStore.LoadAsync(cancellationToken).ConfigureAwait(false));

    public async Task SignInAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var port = ReserveLoopbackPort();
        var redirectUri = new Uri($"http://127.0.0.1:{port}/");
        var verifier = Pkce.GenerateVerifier();
        var state = Pkce.GenerateState();
        var validator = new OAuthStateValidator(state, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(3));
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri.AbsoluteUri);
        listener.Start();

        var authorization = BuildAuthorizationUri(_clientId!, redirectUri, verifier, state);
        Process.Start(new ProcessStartInfo(authorization.AbsoluteUri) { UseShellExecute = true });
        HttpListenerContext context;
        try
        {
            context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromMinutes(3), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new DriveOperationException("OAUTH_TIMEOUT", true, exception);
        }

        var query = context.Request.QueryString;
        var receivedState = query["state"];
        var code = query["code"];
        var error = query["error"];
        var accepted = error is null && code is not null && validator.IsValid(receivedState, DateTimeOffset.UtcNow);
        await WriteBrowserResponseAsync(context.Response, accepted).ConfigureAwait(false);
        if (!accepted)
        {
            throw new DriveOperationException(error == "access_denied" ? "OAUTH_CANCELLED" : "OAUTH_STATE_INVALID", true);
        }

        var token = await _tokenClient.ExchangeCodeAsync(_clientId!, code!, verifier, redirectUri, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            throw new DriveOperationException("REFRESH_TOKEN_MISSING", true);
        }
        await _tokenStore.SaveAsync(token.RefreshToken, cancellationToken).ConfigureAwait(false);
        SetAccessToken(token);
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            return _accessToken;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                return _accessToken;
            }
            var refreshToken = await _tokenStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                throw new DriveOperationException("AUTH_REQUIRED", true);
            }
            GoogleTokenResult token;
            try
            {
                token = await _tokenClient.RefreshAsync(_clientId!, refreshToken, cancellationToken).ConfigureAwait(false);
            }
            catch (DriveOperationException exception) when (exception.RequiresAuthentication)
            {
                await _tokenStore.DeleteAsync(CancellationToken.None).ConfigureAwait(false);
                await InvalidateAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            SetAccessToken(token);
            return _accessToken!;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task InvalidateAsync(CancellationToken cancellationToken)
    {
        _accessToken = null;
        _accessTokenExpiresAt = default;
        return Task.CompletedTask;
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var refreshToken = await _tokenStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refreshToken });
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var response = await _httpClient.PostAsync("https://oauth2.googleapis.com/revoke", content, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            // Local sign-out must still complete while offline or when revocation times out.
        }
        finally
        {
            await _tokenStore.DeleteAsync(CancellationToken.None).ConfigureAwait(false);
            await InvalidateAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static Uri BuildAuthorizationUri(string clientId, Uri redirectUri, string verifier, string state)
    {
        var values = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri.AbsoluteUri,
            ["response_type"] = "code",
            ["scope"] = DriveFileScope,
            ["code_challenge"] = Pkce.CreateChallenge(verifier),
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["access_type"] = "offline",
            ["prompt"] = "consent"
        };
        return new Uri("https://accounts.google.com/o/oauth2/v2/auth?" + string.Join("&", values.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}")));
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WriteBrowserResponseAsync(HttpListenerResponse response, bool succeeded)
    {
        var html = succeeded
            ? "<!doctype html><meta charset=utf-8><title>Wocel Capture</title><p>Đăng nhập thành công. Bạn có thể đóng tab này.</p>"
            : "<!doctype html><meta charset=utf-8><title>Wocel Capture</title><p>Không thể hoàn tất đăng nhập. Hãy quay lại ứng dụng.</p>";
        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.Close();
    }

    private void SetAccessToken(GoogleTokenResult token)
    {
        _accessToken = token.AccessToken;
        _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresInSeconds);
    }

    private void EnsureConfigured()
    {
        if (_clientId is null)
        {
            throw new InvalidOperationException("Google OAuth client ID is not configured. Set WOCEL_GOOGLE_CLIENT_ID or oauth-client-id.txt.");
        }
    }
}

public static class OAuthConfiguration
{
    public static string? FindClientId()
    {
        var environment = Environment.GetEnvironmentVariable("WOCEL_GOOGLE_CLIENT_ID");
        if (!string.IsNullOrWhiteSpace(environment))
        {
            return environment.Trim();
        }
        var path = Path.Combine(AppContext.BaseDirectory, "oauth-client-id.txt");
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }
}
