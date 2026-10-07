using Wocel.Capture.Cloud;
using Wocel.Capture.Desktop.Services;
using Wocel.Capture.Logging;
using Wocel.Capture.Models;
using Wocel.Capture.Persistence;
using Xunit;

namespace Wocel.Capture.Desktop.Tests;

public sealed class ExportWorkflowTests
{
    [Theory]
    [InlineData(ExportDestination.Save)]
    [InlineData(ExportDestination.Clipboard)]
    public async Task Destination_error_retains_editor_and_does_not_create_history(ExportDestination destination)
    {
        using var context = new ExportContext();
        context.Storage.Error = new IOException("disk unavailable");
        context.Clipboard.Error = new InvalidOperationException("clipboard busy");
        var intent = new ExportIntent(destination, Path.Combine(context.TempPath, "chosen.png"));

        var result = await context.Workflow.CommitAsync(Snapshot(), intent, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.False(result.ShouldCloseEditor);
        Assert.Empty(await context.History.GetAllAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Automatic_upload_detaches_only_after_durable_local_commit()
    {
        using var context = new ExportContext();
        var releaseUpload = new TaskCompletionSource<DriveUploadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Drive.Handler = async (request, _) =>
        {
            Assert.True(File.Exists(request.LocalPath));
            Assert.NotNull(await context.History.GetAsync(request.CaptureId, TestContext.Current.CancellationToken));
            return await releaseUpload.Task;
        };

        var result = await context.Workflow.CommitAsync(
            Snapshot(),
            new ExportIntent(ExportDestination.Save, Path.Combine(context.TempPath, "saved.png"), AutoUpload: true, CreatePublicLink: true),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.True(result.ShouldCloseEditor);
        Assert.NotNull(result.UploadTask);
        Assert.False(result.UploadTask!.IsCompleted);
        var saved = await context.History.GetAsync(result.CaptureId!.Value, TestContext.Current.CancellationToken);
        Assert.NotNull(saved);
        Assert.True(File.Exists(saved.LocalPath));
        releaseUpload.SetResult(DriveUploadResult.Shared("drive-1", new Uri("https://drive.google.com/file/d/drive-1/view")));
        await result.UploadTask;
    }

    [Fact]
    public async Task Repeated_exports_receive_unique_capture_ids()
    {
        using var context = new ExportContext();

        var first = await context.Workflow.CommitAsync(Snapshot(), new ExportIntent(ExportDestination.Save, Path.Combine(context.TempPath, "one.png")), TestContext.Current.CancellationToken);
        var second = await context.Workflow.CommitAsync(Snapshot(), new ExportIntent(ExportDestination.Save, Path.Combine(context.TempPath, "two.png")), TestContext.Current.CancellationToken);

        Assert.NotEqual(first.CaptureId, second.CaptureId);
        Assert.Equal(2, (await context.History.GetAllAsync(TestContext.Current.CancellationToken)).Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_sharing_choice_reaches_drive_request(bool createPublicLink)
    {
        using var context = new ExportContext();
        DriveUploadRequest? observed = null;
        context.Drive.Handler = (request, _) =>
        {
            observed = request;
            return Task.FromResult(DriveUploadResult.Private("drive-private", null));
        };

        var result = await context.Workflow.CommitAsync(
            Snapshot(),
            new ExportIntent(ExportDestination.Save, Path.Combine(context.TempPath, "share.png"), AutoUpload: true, CreatePublicLink: createPublicLink),
            TestContext.Current.CancellationToken);
        await result.UploadTask!;

        Assert.Equal(createPublicLink, observed!.CreatePublicLink);
    }

    [Fact]
    public async Task Logging_failure_cannot_rollback_successful_export()
    {
        using var context = new ExportContext(throwingLog: true);

        var result = await context.Workflow.CommitAsync(Snapshot(), new ExportIntent(ExportDestination.Save, Path.Combine(context.TempPath, "safe.png")), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(Path.Combine(context.TempPath, "safe.png")));
        Assert.NotNull(await context.History.GetAsync(result.CaptureId!.Value, TestContext.Current.CancellationToken));
    }

    private static EditorSnapshot Snapshot() => new(new byte[] { 1, 2, 3, 4 }, new PixelSize(1, 1), CaptureImageFormat.Png);

    private sealed class ExportContext : IDisposable
    {
        public ExportContext(bool throwingLog = false)
        {
            TempPath = Path.Combine(Path.GetTempPath(), "wocel-export-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempPath);
            History = new JsonHistoryRepository(Path.Combine(TempPath, "history.json"));
            Drive = new FakeDriveClient();
            Queue = new UploadQueue(History, Drive, new RecordingLog());
            Storage = new FakeStorage();
            Clipboard = new FakeClipboard();
            Workflow = new ExportWorkflow(History, Queue, throwingLog ? new ThrowingLog() : new RecordingLog(), Storage, Clipboard, TempPath);
        }

        public string TempPath { get; }
        public JsonHistoryRepository History { get; }
        public FakeDriveClient Drive { get; }
        public UploadQueue Queue { get; }
        public FakeStorage Storage { get; }
        public FakeClipboard Clipboard { get; }
        public ExportWorkflow Workflow { get; }

        public void Dispose() => Directory.Delete(TempPath, true);
    }

    private sealed class FakeStorage : IExportStorage
    {
        public Exception? Error { get; set; }

        public async Task WriteAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        {
            if (Error is not null) throw Error;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, content.ToArray(), cancellationToken);
        }
    }

    private sealed class FakeClipboard : IImageClipboard
    {
        public Exception? Error { get; set; }

        public Task WriteAsync(ReadOnlyMemory<byte> encodedImage, CaptureImageFormat format, CancellationToken cancellationToken)
        {
            if (Error is not null) throw Error;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDriveClient : IDriveClient
    {
        public Func<DriveUploadRequest, CancellationToken, Task<DriveUploadResult>> Handler { get; set; } =
            (_, _) => Task.FromResult(DriveUploadResult.Private("private", null));

        public Task<DriveUploadResult> UploadAndShareAsync(DriveUploadRequest request, CancellationToken cancellationToken) => Handler(request, cancellationToken);
    }

    private sealed class RecordingLog : IActivityLog
    {
        public Task WriteAsync(ActivityEvent activityEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PruneAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ThrowingLog : IActivityLog
    {
        public Task WriteAsync(ActivityEvent activityEvent, CancellationToken cancellationToken = default) => throw new IOException("log unavailable");
        public Task PruneAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
