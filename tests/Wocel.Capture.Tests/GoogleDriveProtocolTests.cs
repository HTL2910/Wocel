using System.Net;
using System.Text;
using Wocel.Capture.Cloud;
using Xunit;

namespace Wocel.Capture.Tests;

public sealed class GoogleDriveProtocolTests
{
    [Fact]
    public async Task Upload_creates_folder_uses_resumable_session_and_shares_file()
    {
        var requests = new List<(HttpMethod Method, Uri Uri, string Body, string? Authorization)>();
        var handler = new DelegateHandler(async request =>
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync();
            requests.Add((request.Method, request.RequestUri!, body, request.Headers.Authorization?.ToString()));
            return (request.Method.Method, request.RequestUri!.AbsolutePath) switch
            {
                ("GET", "/drive/v3/files") => Json("{\"files\":[]}"),
                ("POST", "/drive/v3/files") => Json("{\"id\":\"folder-1\"}"),
                ("POST", "/upload/drive/v3/files") => Response(HttpStatusCode.OK, "", ("Location", "https://www.googleapis.com/upload/session-1")),
                ("PUT", "/upload/session-1") => Json("{\"id\":\"file-1\",\"webViewLink\":\"https://drive.google.com/file/d/file-1/view\"}"),
                ("POST", "/drive/v3/files/file-1/permissions") => Json("{\"id\":\"anyone\"}"),
                _ => throw new InvalidOperationException($"Unexpected request {request.Method} {request.RequestUri}")
            };
        });
        var client = new GoogleDriveClient(new HttpClient(handler), new FixedTokenProvider("access-token"));
        var file = Path.GetTempFileName();
        await File.WriteAllBytesAsync(file, [1, 2, 3, 4]);
        try
        {
            var result = await client.UploadAndShareAsync(new DriveUploadRequest(Guid.NewGuid(), file, "ảnh.png", "image/png", 4), CancellationToken.None);

            Assert.True(result.IsShared);
            Assert.Equal("file-1", result.FileId);
            Assert.Equal("https://drive.google.com/file/d/file-1/view", result.WebLink!.AbsoluteUri);
            Assert.All(requests, request => Assert.Equal("Bearer access-token", request.Authorization));
            Assert.Contains(requests, request => request.Method == HttpMethod.Put && request.Uri.AbsoluteUri == "https://www.googleapis.com/upload/session-1");
            Assert.Contains(requests, request => request.Body.Contains("\"type\":\"anyone\"") && request.Body.Contains("\"role\":\"reader\""));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Sharing_policy_rejection_returns_private_upload()
    {
        var handler = new DelegateHandler(request => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/drive/v3/files" when request.Method == HttpMethod.Get => Json("{\"files\":[{\"id\":\"folder-1\"}]}"),
            "/upload/drive/v3/files" => Response(HttpStatusCode.OK, "", ("Location", "https://www.googleapis.com/upload/session-2")),
            "/upload/session-2" => Json("{\"id\":\"file-2\",\"webViewLink\":\"https://drive.google.com/file/d/file-2/view\"}"),
            "/drive/v3/files/file-2/permissions" => Response(HttpStatusCode.Forbidden, "{\"error\":{\"errors\":[{\"reason\":\"domainPolicy\"}]}}"),
            _ => throw new InvalidOperationException(request.RequestUri.AbsoluteUri)
        }));
        var client = new GoogleDriveClient(new HttpClient(handler), new FixedTokenProvider("token"));
        var file = Path.GetTempFileName();
        try
        {
            var result = await client.UploadAndShareAsync(new DriveUploadRequest(Guid.NewGuid(), file, "capture.png", "image/png", 0), CancellationToken.None);

            Assert.False(result.IsShared);
            Assert.Equal("file-2", result.FileId);
            Assert.Equal("SHARING_POLICY", result.SharingErrorCode);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Private_upload_never_creates_anyone_permission()
    {
        var requestedPaths = new List<string>();
        var handler = new DelegateHandler(request =>
        {
            requestedPaths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(request.RequestUri.AbsolutePath switch
            {
                "/drive/v3/files" when request.Method == HttpMethod.Get => Json("{\"files\":[{\"id\":\"folder-1\"}]}"),
                "/upload/drive/v3/files" => Response(HttpStatusCode.OK, "", ("Location", "https://www.googleapis.com/upload/session-private")),
                "/upload/session-private" => Json("{\"id\":\"file-private\",\"webViewLink\":\"https://drive.google.com/file/d/file-private/view\"}"),
                _ => throw new InvalidOperationException($"Private upload made unexpected request {request.RequestUri}")
            });
        });
        var client = new GoogleDriveClient(new HttpClient(handler), new FixedTokenProvider("token"));
        var file = Path.GetTempFileName();
        try
        {
            var request = new DriveUploadRequest(Guid.NewGuid(), file, "private.png", "image/png", 0, CreatePublicLink: false);

            var result = await client.UploadAndShareAsync(request, CancellationToken.None);

            Assert.False(result.IsShared);
            Assert.Null(result.SharingErrorCode);
            Assert.DoesNotContain(requestedPaths, path => path.Contains("permissions", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Sharing_network_failure_preserves_completed_upload_identity()
    {
        var handler = new DelegateHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/drive/v3/files" when request.Method == HttpMethod.Get => Task.FromResult(Json("{\"files\":[{\"id\":\"folder-1\"}]}")),
            "/upload/drive/v3/files" => Task.FromResult(Response(HttpStatusCode.OK, "", ("Location", "https://www.googleapis.com/upload/session-network"))),
            "/upload/session-network" => Task.FromResult(Json("{\"id\":\"file-network\",\"webViewLink\":\"https://drive.google.com/file/d/file-network/view\"}")),
            "/drive/v3/files/file-network/permissions" => Task.FromException<HttpResponseMessage>(new HttpRequestException("offline")),
            _ => throw new InvalidOperationException(request.RequestUri.AbsoluteUri)
        });
        var client = new GoogleDriveClient(new HttpClient(handler), new FixedTokenProvider("token"));
        var file = Path.GetTempFileName();
        try
        {
            var exception = await Assert.ThrowsAsync<DriveOperationException>(() => client.UploadAndShareAsync(new DriveUploadRequest(Guid.NewGuid(), file, "capture.png", "image/png", 0), CancellationToken.None));

            Assert.Equal("SHARING_NETWORK", exception.ErrorCode);
            Assert.Equal("file-network", exception.UploadedFileId);
            Assert.Equal("https://drive.google.com/file/d/file-network/view", exception.UploadedWebLink!.AbsoluteUri);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Unauthorized_response_maps_to_authentication_required()
    {
        var handler = new DelegateHandler(_ => Task.FromResult(Response(HttpStatusCode.Unauthorized, "{}")));
        var tokenProvider = new FixedTokenProvider("expired");
        var client = new GoogleDriveClient(new HttpClient(handler), tokenProvider);
        var file = Path.GetTempFileName();
        try
        {
            var exception = await Assert.ThrowsAsync<DriveOperationException>(() => client.UploadAndShareAsync(new DriveUploadRequest(Guid.NewGuid(), file, "capture.png", "image/png", 0), CancellationToken.None));

            Assert.True(exception.RequiresAuthentication);
            Assert.Equal("AUTH_REQUIRED", exception.ErrorCode);
            Assert.True(tokenProvider.Invalidated);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Drive_query_literal_escapes_quotes_and_backslashes()
    {
        Assert.Equal("a\\'b\\\\c", GoogleDriveQuery.EscapeLiteral("a'b\\c"));
    }

    [Fact]
    public async Task Token_client_refreshes_and_validates_response()
    {
        var handler = new DelegateHandler(request =>
        {
            Assert.Equal(new Uri("https://oauth2.googleapis.com/token"), request.RequestUri);
            return Task.FromResult(Json("{\"access_token\":\"new-access\",\"expires_in\":3600,\"token_type\":\"Bearer\"}"));
        });

        var token = await new GoogleTokenClient(new HttpClient(handler)).RefreshAsync("client-id", "refresh-token", CancellationToken.None);

        Assert.Equal("new-access", token.AccessToken);
        Assert.Equal(3600, token.ExpiresInSeconds);
    }

    private static HttpResponseMessage Json(string body) => Response(HttpStatusCode.OK, body);

    private static HttpResponseMessage Response(HttpStatusCode status, string body, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }
        return response;
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }

    private sealed class FixedTokenProvider(string token) : IAccessTokenProvider
    {
        public bool Invalidated { get; private set; }
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult(token);
        public Task InvalidateAsync(CancellationToken cancellationToken) { Invalidated = true; return Task.CompletedTask; }
    }
}
