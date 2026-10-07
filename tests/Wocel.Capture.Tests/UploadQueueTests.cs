using Wocel.Capture.Cloud;
using Wocel.Capture.Logging;
using Wocel.Capture.Models;
using Wocel.Capture.Persistence;
using Xunit;

namespace Wocel.Capture.Tests;

public sealed class UploadQueueTests
{
    [Fact]
    public async Task Queue_transitions_pending_uploading_to_shared()
    {
        using var context = await QueueContext.CreateAsync();
        context.Drive.Handler = (_, _) => Task.FromResult(DriveUploadResult.Shared("drive-1", new Uri("https://drive.google.com/file/d/drive-1/view")));

        await context.Queue.EnqueueAsync(context.Capture.Id);

        var saved = await context.History.GetAsync(context.Capture.Id);
        Assert.Equal(UploadState.UploadedShared, saved!.UploadState);
        Assert.Equal("drive-1", saved.DriveFileId);
        Assert.Equal("https://drive.google.com/file/d/drive-1/view", saved.DriveWebLink!.AbsoluteUri);
        Assert.Contains(context.Log.Events, entry => entry.EventName == "upload_completed" && entry.Succeeded);
    }

    [Fact]
    public async Task Sharing_policy_rejection_keeps_uploaded_file_private()
    {
        using var context = await QueueContext.CreateAsync();
        context.Drive.Handler = (_, _) => Task.FromResult(DriveUploadResult.Private("drive-private", "SHARING_POLICY"));

        await context.Queue.EnqueueAsync(context.Capture.Id);

        var saved = await context.History.GetAsync(context.Capture.Id);
        Assert.Equal(UploadState.UploadedPrivate, saved!.UploadState);
        Assert.Equal("drive-private", saved.DriveFileId);
        Assert.Equal("SHARING_POLICY", saved.LastErrorCode);
    }

    [Fact]
    public async Task Failed_upload_can_be_retried()
    {
        using var context = await QueueContext.CreateAsync();
        var attempt = 0;
        context.Drive.Handler = (_, _) => ++attempt == 1
            ? throw new DriveOperationException("NETWORK", false)
            : Task.FromResult(DriveUploadResult.Shared("drive-2", new Uri("https://drive.google.com/file/d/drive-2/view")));

        await context.Queue.EnqueueAsync(context.Capture.Id);
        Assert.Equal(UploadState.Failed, (await context.History.GetAsync(context.Capture.Id))!.UploadState);

        await context.Queue.RetryAsync(context.Capture.Id);

        var saved = await context.History.GetAsync(context.Capture.Id);
        Assert.Equal(UploadState.UploadedShared, saved!.UploadState);
        Assert.Equal(1, saved.RetryCount);
    }

    [Fact]
    public async Task Sharing_failure_preserves_file_id_and_retry_does_not_request_a_new_upload()
    {
        using var context = await QueueContext.CreateAsync();
        var attempt = 0;
        context.Drive.Handler = (request, _) =>
        {
            attempt++;
            if (attempt == 1)
            {
                throw new DriveOperationException("DRIVE_HTTP_503", false, uploadedFileId: "drive-existing", uploadedWebLink: new Uri("https://drive.google.com/file/d/drive-existing/view"));
            }
            Assert.Equal("drive-existing", request.ExistingFileId);
            return Task.FromResult(DriveUploadResult.Shared("drive-existing", request.ExistingWebLink!));
        };

        await context.Queue.EnqueueAsync(context.Capture.Id);
        Assert.Equal("drive-existing", (await context.History.GetAsync(context.Capture.Id))!.DriveFileId);

        await context.Queue.RetryAsync(context.Capture.Id);

        Assert.Equal(2, attempt);
        Assert.Equal(UploadState.UploadedShared, (await context.History.GetAsync(context.Capture.Id))!.UploadState);
    }

    [Fact]
    public async Task Sharing_choice_is_preserved_when_queue_builds_drive_request()
    {
        using var context = await QueueContext.CreateAsync();
        DriveUploadRequest? observed = null;
        context.Drive.Handler = (request, _) =>
        {
            observed = request;
            return Task.FromResult(DriveUploadResult.Private("drive-private", null));
        };
        await context.History.UpsertAsync(context.Capture with { CreatePublicLink = false });

        await context.Queue.EnqueueAsync(context.Capture.Id);

        Assert.False(observed!.CreatePublicLink);
    }

    [Fact]
    public async Task Cancellation_returns_upload_to_pending()
    {
        using var context = await QueueContext.CreateAsync();
        context.Drive.Handler = (_, token) => Task.FromCanceled<DriveUploadResult>(token);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.Queue.EnqueueAsync(context.Capture.Id, cancellation.Token));

        Assert.Equal(UploadState.Pending, (await context.History.GetAsync(context.Capture.Id))!.UploadState);
    }

    [Fact]
    public async Task Revoked_grant_requires_authentication_without_dropping_file()
    {
        using var context = await QueueContext.CreateAsync();
        context.Drive.Handler = (_, _) => throw new DriveOperationException("AUTH_REVOKED", true);

        await context.Queue.EnqueueAsync(context.Capture.Id);

        var saved = await context.History.GetAsync(context.Capture.Id);
        Assert.Equal(UploadState.AuthenticationRequired, saved!.UploadState);
        Assert.Equal(context.Capture.LocalPath, saved.LocalPath);
    }

    [Fact]
    public async Task Startup_recovers_interrupted_uploads_to_pending()
    {
        using var context = await QueueContext.CreateAsync(UploadState.Uploading);

        var count = await context.Queue.RecoverInterruptedAsync();

        Assert.Equal(1, count);
        Assert.Equal(UploadState.Pending, (await context.History.GetAsync(context.Capture.Id))!.UploadState);
    }

    [Fact]
    public async Task Logging_failure_does_not_rollback_completed_upload()
    {
        using var context = await QueueContext.CreateAsync();
        context.Drive.Handler = (_, _) => Task.FromResult(DriveUploadResult.Shared("drive-safe", new Uri("https://drive.google.com/file/d/drive-safe/view")));
        var queue = new UploadQueue(context.History, context.Drive, new ThrowingActivityLog());

        await queue.EnqueueAsync(context.Capture.Id);

        var saved = await context.History.GetAsync(context.Capture.Id);
        Assert.Equal(UploadState.UploadedShared, saved!.UploadState);
        Assert.Equal("drive-safe", saved.DriveFileId);
    }

    private sealed class QueueContext : IDisposable
    {
        private readonly TempDirectory _temp;

        private QueueContext(TempDirectory temp, JsonHistoryRepository history, CaptureRecord capture)
        {
            _temp = temp;
            History = history;
            Capture = capture;
            Drive = new FakeDriveClient();
            Log = new RecordingActivityLog();
            Queue = new UploadQueue(history, Drive, Log);
        }

        public JsonHistoryRepository History { get; }
        public CaptureRecord Capture { get; }
        public FakeDriveClient Drive { get; }
        public RecordingActivityLog Log { get; }
        public UploadQueue Queue { get; }

        public static async Task<QueueContext> CreateAsync(UploadState state = UploadState.LocalOnly)
        {
            var temp = new TempDirectory();
            var localPath = Path.Combine(temp.Path, "capture.png");
            await File.WriteAllBytesAsync(localPath, [1, 2, 3]);
            var capture = new CaptureRecord
            {
                Id = Guid.NewGuid(),
                DisplayName = "capture.png",
                CreatedAt = DateTimeOffset.UtcNow,
                Size = new PixelSize(100, 80),
                Format = CaptureImageFormat.Png,
                ByteSize = 3,
                LocalPath = localPath,
                UploadState = state
            };
            var history = new JsonHistoryRepository(Path.Combine(temp.Path, "history.json"));
            await history.UpsertAsync(capture);
            return new QueueContext(temp, history, capture);
        }

        public void Dispose() => _temp.Dispose();
    }

    private sealed class FakeDriveClient : IDriveClient
    {
        public Func<DriveUploadRequest, CancellationToken, Task<DriveUploadResult>> Handler { get; set; } =
            (_, _) => throw new InvalidOperationException("No fake response configured.");

        public Task<DriveUploadResult> UploadAndShareAsync(DriveUploadRequest request, CancellationToken cancellationToken) =>
            Handler(request, cancellationToken);
    }

    private sealed class RecordingActivityLog : IActivityLog
    {
        public List<ActivityEvent> Events { get; } = [];
        public Task WriteAsync(ActivityEvent activityEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(activityEvent);
            return Task.CompletedTask;
        }

        public Task PruneAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ThrowingActivityLog : IActivityLog
    {
        public Task WriteAsync(ActivityEvent activityEvent, CancellationToken cancellationToken = default) =>
            throw new IOException("log unavailable");

        public Task PruneAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
