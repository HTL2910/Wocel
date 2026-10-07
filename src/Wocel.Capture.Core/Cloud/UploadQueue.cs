using Wocel.Capture.Logging;
using Wocel.Capture.Models;
using Wocel.Capture.Persistence;

namespace Wocel.Capture.Cloud;

public sealed class UploadQueue(IHistoryRepository history, IDriveClient drive, IActivityLog activityLog)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task EnqueueAsync(Guid captureId, CancellationToken cancellationToken = default)
    {
        // Enqueue is durable: once requested, persist Pending even if the caller is already cancelled.
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            var capture = await RequiredCaptureAsync(captureId, CancellationToken.None).ConfigureAwait(false);
            await history.UpsertAsync(capture with
            {
                UploadState = UploadState.Pending,
                LastErrorCode = null
            }, CancellationToken.None).ConfigureAwait(false);
            await ProcessAsync(captureId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ProcessPendingAsync(Guid captureId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var capture = await RequiredCaptureAsync(captureId, cancellationToken).ConfigureAwait(false);
            if (capture.UploadState != UploadState.Pending)
            {
                throw new InvalidOperationException($"Capture {captureId} is not pending upload.");
            }

            await ProcessAsync(captureId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RetryAsync(Guid captureId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var capture = await RequiredCaptureAsync(captureId, cancellationToken).ConfigureAwait(false);
            await history.UpsertAsync(capture with
            {
                UploadState = UploadState.Pending,
                RetryCount = checked(capture.RetryCount + 1),
                LastErrorCode = null
            }, cancellationToken).ConfigureAwait(false);
            await ProcessAsync(captureId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> RecoverInterruptedAsync(CancellationToken cancellationToken = default)
    {
        var captures = await history.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var interrupted = captures.Where(capture => capture.UploadState == UploadState.Uploading).ToArray();
        foreach (var capture in interrupted)
        {
            await history.UpsertAsync(capture with { UploadState = UploadState.Pending }, cancellationToken).ConfigureAwait(false);
        }

        return interrupted.Length;
    }

    private async Task ProcessAsync(Guid captureId, CancellationToken cancellationToken)
    {
        var capture = await RequiredCaptureAsync(captureId, CancellationToken.None).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(capture.LocalPath) || !File.Exists(capture.LocalPath))
        {
            await FailAsync(capture, "LOCAL_FILE_MISSING", CancellationToken.None).ConfigureAwait(false);
            return;
        }

        capture = capture with { UploadState = UploadState.Uploading, LastErrorCode = null };
        await history.UpsertAsync(capture, CancellationToken.None).ConfigureAwait(false);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var result = await drive.UploadAndShareAsync(
                new DriveUploadRequest(capture.Id, capture.LocalPath, capture.DisplayName, DriveContentTypes.For(capture.Format), capture.ByteSize, capture.DriveFileId, capture.DriveWebLink, capture.CreatePublicLink),
                cancellationToken).ConfigureAwait(false);
            var completed = capture with
            {
                UploadState = result.IsShared ? UploadState.UploadedShared : UploadState.UploadedPrivate,
                DriveFileId = result.FileId,
                DriveWebLink = result.WebLink,
                LastErrorCode = result.SharingErrorCode
            };
            await history.UpsertAsync(completed, CancellationToken.None).ConfigureAwait(false);
            await TryLogAsync(capture, "upload_completed", true, stopwatch.ElapsedMilliseconds, result.SharingErrorCode).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await history.UpsertAsync(capture with { UploadState = UploadState.Pending }, CancellationToken.None).ConfigureAwait(false);
            await TryLogAsync(capture, "upload_cancelled", false, stopwatch.ElapsedMilliseconds, "CANCELLED").ConfigureAwait(false);
            throw;
        }
        catch (DriveOperationException exception) when (exception.InnerException is OperationCanceledException cancellation)
        {
            await history.UpsertAsync(capture with
            {
                UploadState = UploadState.Pending,
                LastErrorCode = "CANCELLED",
                DriveFileId = exception.UploadedFileId ?? capture.DriveFileId,
                DriveWebLink = exception.UploadedWebLink ?? capture.DriveWebLink
            }, CancellationToken.None).ConfigureAwait(false);
            await TryLogAsync(capture, "upload_cancelled", false, stopwatch.ElapsedMilliseconds, "CANCELLED").ConfigureAwait(false);
            throw cancellation;
        }
        catch (DriveOperationException exception)
        {
            var state = exception.RequiresAuthentication ? UploadState.AuthenticationRequired : UploadState.Failed;
            await history.UpsertAsync(capture with
            {
                UploadState = state,
                LastErrorCode = exception.ErrorCode,
                DriveFileId = exception.UploadedFileId ?? capture.DriveFileId,
                DriveWebLink = exception.UploadedWebLink ?? capture.DriveWebLink
            }, CancellationToken.None).ConfigureAwait(false);
            await TryLogAsync(capture, "upload_failed", false, stopwatch.ElapsedMilliseconds, exception.ErrorCode).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await FailAsync(capture, "UNEXPECTED", CancellationToken.None).ConfigureAwait(false);
            await TryLogAsync(capture, "upload_failed", false, stopwatch.ElapsedMilliseconds, "UNEXPECTED").ConfigureAwait(false);
        }
    }

    private async Task<CaptureRecord> RequiredCaptureAsync(Guid captureId, CancellationToken cancellationToken) =>
        await history.GetAsync(captureId, cancellationToken).ConfigureAwait(false)
        ?? throw new KeyNotFoundException($"Capture {captureId} was not found.");

    private Task FailAsync(CaptureRecord capture, string code, CancellationToken cancellationToken) =>
        history.UpsertAsync(capture with { UploadState = UploadState.Failed, LastErrorCode = code }, cancellationToken);

    private async Task TryLogAsync(CaptureRecord capture, string eventName, bool succeeded, long durationMs, string? errorCode)
    {
        try
        {
            await activityLog.WriteAsync(new ActivityEvent(
                DateTimeOffset.UtcNow,
                capture.Id,
                eventName,
                succeeded ? ActivityLevel.Information : ActivityLevel.Warning,
                durationMs,
                succeeded,
                errorCode,
                Width: capture.Size.Width,
                Height: capture.Size.Height,
                ByteSize: capture.ByteSize), CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Diagnostics must never roll back durable upload state.
        }
    }
}
