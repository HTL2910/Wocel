using Wocel.Capture.Cloud;
using Wocel.Capture.Desktop.Services;
using Wocel.Capture.Desktop.ViewModels;
using Wocel.Capture.Logging;
using Wocel.Capture.Models;
using Wocel.Capture.Persistence;
using Wocel.Capture.Platform.Capture;
using Wocel.Capture.Platform.Services;
using Wocel.Capture.Presentation;
using Xunit;

namespace Wocel.Capture.Desktop.Tests;

public sealed class SettingsAndHistoryTests
{
    [Fact]
    public async Task History_search_state_filter_and_per_item_retry_are_shared_ui_state()
    {
        var history = new MemoryHistory(
            Capture("alpha.png", UploadState.UploadedPrivate),
            Capture("beta.png", UploadState.Failed));
        var drive = new FakeDriveClient();
        var queue = new UploadQueue(history, drive, new RecordingLog());
        var viewModel = new HistoryViewModel(history, queue);
        await viewModel.RefreshAsync(TestContext.Current.CancellationToken);

        viewModel.SearchText = "beta";
        viewModel.StateFilter = HistoryStateFilter.Failed;
        Assert.Equal("beta.png", Assert.Single(viewModel.Items).DisplayName);

        var retryId = viewModel.Items[0].Id;
        await viewModel.RetryAsync(viewModel.Items[0], TestContext.Current.CancellationToken);
        Assert.Equal(UploadState.UploadedPrivate, (await history.GetAsync(retryId, TestContext.Current.CancellationToken))!.UploadState);
    }

    [Fact]
    public async Task Authentication_required_retry_reauthorizes_before_upload()
    {
        var capture = Capture("reauth.png", UploadState.AuthenticationRequired);
        var history = new MemoryHistory(capture);
        var calls = new List<string>();
        var queue = new UploadQueue(history, new FakeDriveClient { BeforeUpload = () => calls.Add("upload") }, new RecordingLog());
        var viewModel = new HistoryViewModel(history, queue, _ => { calls.Add("authorize"); return Task.CompletedTask; });

        await viewModel.RetryAsync(capture, TestContext.Current.CancellationToken);

        Assert.Equal(["authorize", "upload"], calls);
    }

    [Fact]
    public async Task Settings_exposes_first_run_google_prompt_and_capture_permission()
    {
        var settings = new MemorySettings();
        var capture = new FakeCaptureService(CapturePermissionStatus.Denied);
        var startup = new FakeStartupService();
        var viewModel = new SettingsViewModel(settings, capture, startup, isFirstRun: true, hasGoogleCredential: _ => Task.FromResult(false));

        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.ShouldShowGooglePrompt);
        Assert.Equal(CapturePermissionStatus.Denied, viewModel.PermissionStatus);
        Assert.Contains("System Settings", viewModel.PermissionStatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Activity_log_copy_redacts_credentials()
    {
        var clipboard = new RecordingTextClipboard();
        var reader = new MemoryLogReader(new ActivityEvent(
            DateTimeOffset.UtcNow, Guid.NewGuid(), "oauth_failed", ActivityLevel.Error, 4, false,
            Message: "Authorization: Bearer secret-token refresh_token=very-secret"));
        var viewModel = new ActivityLogViewModel(reader, clipboard);
        await viewModel.RefreshAsync(cancellationToken: TestContext.Current.CancellationToken);

        await viewModel.CopyAsync(viewModel.Items[0], TestContext.Current.CancellationToken);

        Assert.DoesNotContain("secret-token", clipboard.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("very-secret", clipboard.Text, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", clipboard.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_prunes_thirty_day_logs_and_recovers_interrupted_uploads()
    {
        var history = new MemoryHistory(Capture("pending.png", UploadState.Uploading));
        var log = new RecordingLog();
        var workflow = new CaptureWorkflow(new UploadQueue(history, new FakeDriveClient(), log), log, TimeProvider.System);

        var recovered = await workflow.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, recovered);
        Assert.Single(log.PruneCalls);
        Assert.Equal(UploadState.Pending, (await history.GetAllAsync(TestContext.Current.CancellationToken)).Single().UploadState);
    }

    [Fact]
    public async Task Legacy_settings_without_hotkey_still_deserialize_and_platform_defaults_differ()
    {
        var windows = CaptureSettings.CreateDefault(CapturePlatform.Windows);
        var mac = CaptureSettings.CreateDefault(CapturePlatform.MacOS);

        Assert.Equal("PrintScreen", windows.Hotkey);
        Assert.Equal("Control+Shift+4", mac.Hotkey);

        var path = Path.Combine(Path.GetTempPath(), $"wocel-settings-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, "{\"autoUpload\":true}", TestContext.Current.CancellationToken);
            var legacy = await new JsonSettingsRepository(path).GetAsync(TestContext.Current.CancellationToken);
            Assert.Equal("PrintScreen", legacy.Hotkey);
            Assert.True(legacy.AutoUpload);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Missing_oauth_client_id_never_creates_a_demo_credential()
    {
        var tokenStore = new RecordingTokenStore();
        var browser = new RecordingBrowserLauncher();
        var service = new GoogleOAuthService(null, new HttpClient(), tokenStore, browser);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SignInAsync(TestContext.Current.CancellationToken));

        Assert.Null(tokenStore.Token);
        Assert.Empty(browser.Opened);
    }

    private static CaptureRecord Capture(string name, UploadState state)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, [1]);
        return new CaptureRecord
        {
            Id = Guid.NewGuid(), DisplayName = name, CreatedAt = DateTimeOffset.UtcNow,
            Size = new PixelSize(1, 1), Format = CaptureImageFormat.Png, ByteSize = 1,
            LocalPath = path, UploadState = state
        };
    }

    private sealed class MemoryHistory(params CaptureRecord[] records) : IHistoryRepository
    {
        private readonly Dictionary<Guid, CaptureRecord> _records = records.ToDictionary(item => item.Id);
        public Task<IReadOnlyList<CaptureRecord>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CaptureRecord>>(_records.Values.ToArray());
        public Task<CaptureRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_records.GetValueOrDefault(id));
        public Task UpsertAsync(CaptureRecord capture, CancellationToken cancellationToken = default) { _records[capture.Id] = capture; return Task.CompletedTask; }
        public Task RemoveAsync(Guid id, CancellationToken cancellationToken = default) { _records.Remove(id); return Task.CompletedTask; }
    }

    private sealed class MemorySettings : ISettingsRepository
    {
        public CaptureSettings Value { get; private set; } = CaptureSettings.CreateDefault();
        public Task<CaptureSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);
        public Task SaveAsync(CaptureSettings settings, CancellationToken cancellationToken = default) { Value = settings; return Task.CompletedTask; }
    }

    private sealed class FakeDriveClient : IDriveClient
    {
        public Action? BeforeUpload { get; init; }
        public Task<DriveUploadResult> UploadAndShareAsync(DriveUploadRequest request, CancellationToken cancellationToken)
        {
            BeforeUpload?.Invoke();
            return Task.FromResult(DriveUploadResult.Private("drive-private", null));
        }
    }

    private sealed class RecordingLog : IActivityLog
    {
        public List<DateTimeOffset> PruneCalls { get; } = [];
        public Task WriteAsync(ActivityEvent activityEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PruneAsync(DateTimeOffset now, CancellationToken cancellationToken = default) { PruneCalls.Add(now); return Task.CompletedTask; }
    }

    private sealed class MemoryLogReader(params ActivityEvent[] entries) : IActivityLogReader
    {
        public Task<IReadOnlyList<ActivityEvent>> ReadRecentAsync(int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityEvent>>(entries.Take(limit).ToArray());
    }

    private sealed class RecordingTextClipboard : ITextClipboard
    {
        public string Text { get; private set; } = string.Empty;
        public Task WriteTextAsync(string text, CancellationToken cancellationToken = default) { Text = text; return Task.CompletedTask; }
    }

    private sealed class FakeStartupService : IStartupService
    {
        public bool IsEnabled { get; private set; }
        public void SetEnabled(bool enabled) => IsEnabled = enabled;
    }

    private sealed class RecordingTokenStore : IProtectedTokenStore
    {
        public string? Token { get; private set; }
        public Task SaveAsync(string token, CancellationToken cancellationToken = default) { Token = token; return Task.CompletedTask; }
        public Task<string?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Token);
        public Task DeleteAsync(CancellationToken cancellationToken = default) { Token = null; return Task.CompletedTask; }
    }

    private sealed class RecordingBrowserLauncher : IBrowserLauncher
    {
        public List<Uri> Opened { get; } = [];
        public Task OpenAsync(Uri uri, CancellationToken cancellationToken = default) { Opened.Add(uri); return Task.CompletedTask; }
    }

    private sealed class FakeCaptureService(CapturePermissionStatus status) : IScreenCaptureService
    {
        public Task<CapturePermissionStatus> GetPermissionStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(status);
        public Task<CapturePermissionStatus> RequestPermissionAsync(CancellationToken cancellationToken = default) => Task.FromResult(status);
        public Task OpenPermissionSettingsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DisplayGeometry>> GetDisplaysAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DisplayGeometry>>([]);
        public Task<Wocel.Capture.Platform.Capture.CapturedFrame> CaptureVirtualDesktopAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
