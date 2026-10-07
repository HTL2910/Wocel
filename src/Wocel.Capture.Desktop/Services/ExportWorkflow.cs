using Wocel.Capture.Cloud;
using Wocel.Capture.Logging;
using Wocel.Capture.Models;
using Wocel.Capture.Persistence;

namespace Wocel.Capture.Desktop.Services;

public enum ExportDestination
{
    Clipboard,
    Save
}

public sealed record EditorSnapshot
{
    public EditorSnapshot(ReadOnlyMemory<byte> encodedImage, PixelSize size, CaptureImageFormat format)
    {
        if (encodedImage.IsEmpty) throw new ArgumentException("Encoded image is required.", nameof(encodedImage));
        Size = size;
        EncodedImage = encodedImage.ToArray();
        Format = format;
    }

    public ReadOnlyMemory<byte> EncodedImage { get; }
    public PixelSize Size { get; }
    public CaptureImageFormat Format { get; }
}

public sealed record ExportIntent(
    ExportDestination Destination,
    string? SavePath = null,
    bool AutoUpload = false,
    bool CreatePublicLink = false);

public sealed record ExportCommitResult(
    bool Succeeded,
    bool ShouldCloseEditor,
    Guid? CaptureId = null,
    string? ErrorCode = null,
    Task? UploadTask = null);

public interface IExportStorage
{
    Task WriteAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
}

public interface IImageClipboard
{
    Task WriteAsync(ReadOnlyMemory<byte> encodedImage, CaptureImageFormat format, CancellationToken cancellationToken);
}

public sealed class FileExportStorage : IExportStorage
{
    public async Task WriteAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content.ToArray(), cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}

public sealed class ExportWorkflow(
    IHistoryRepository history,
    UploadQueue uploadQueue,
    IActivityLog activityLog,
    IExportStorage storage,
    IImageClipboard clipboard,
    string cacheDirectory,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<ExportCommitResult> CommitAsync(
        EditorSnapshot snapshot,
        ExportIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(intent);
        var id = Guid.NewGuid();
        var timestamp = _timeProvider.GetUtcNow();
        var extension = Extension(snapshot.Format);
        var localPath = intent.Destination == ExportDestination.Save
            ? intent.SavePath
            : intent.AutoUpload
                ? Path.Combine(cacheDirectory, $"capture-{id:N}{extension}")
                : null;

        try
        {
            if (intent.Destination == ExportDestination.Save)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
                await storage.WriteAsync(localPath, snapshot.EncodedImage, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await clipboard.WriteAsync(snapshot.EncodedImage, snapshot.Format, cancellationToken).ConfigureAwait(false);
                if (localPath is not null)
                {
                    await storage.WriteAsync(localPath, snapshot.EncodedImage, cancellationToken).ConfigureAwait(false);
                }
            }

            var displayName = localPath is null
                ? $"Clipboard_{timestamp:yyyy-MM-dd_HH-mm-ss}{extension}"
                : Path.GetFileName(localPath);
            var capture = new CaptureRecord
            {
                Id = id,
                DisplayName = displayName,
                CreatedAt = timestamp,
                Size = snapshot.Size,
                Format = snapshot.Format,
                ByteSize = snapshot.EncodedImage.Length,
                LocalPath = localPath,
                CreatePublicLink = intent.CreatePublicLink,
                UploadState = intent.AutoUpload ? UploadState.Pending : UploadState.LocalOnly
            };
            await history.UpsertAsync(capture, CancellationToken.None).ConfigureAwait(false);
            await TryLogAsync(capture, "export_completed", true, null).ConfigureAwait(false);

            var uploadTask = intent.AutoUpload
                ? uploadQueue.ProcessPendingAsync(id, CancellationToken.None)
                : null;
            return new ExportCommitResult(true, true, id, UploadTask: uploadTask);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await TryLogAsync(new CaptureRecord
            {
                Id = id,
                DisplayName = localPath is null ? "clipboard" : Path.GetFileName(localPath),
                CreatedAt = timestamp,
                Size = snapshot.Size,
                Format = snapshot.Format,
                ByteSize = snapshot.EncodedImage.Length
            }, "export_failed", false, ErrorCode(exception)).ConfigureAwait(false);
            return new ExportCommitResult(false, false, ErrorCode: ErrorCode(exception));
        }
    }

    private async Task TryLogAsync(CaptureRecord capture, string eventName, bool succeeded, string? errorCode)
    {
        try
        {
            await activityLog.WriteAsync(new ActivityEvent(
                _timeProvider.GetUtcNow(), capture.Id, eventName,
                succeeded ? ActivityLevel.Information : ActivityLevel.Error,
                0, succeeded, errorCode,
                Width: capture.Size.Width, Height: capture.Size.Height, ByteSize: capture.ByteSize),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Diagnostics never change the durable export result.
        }
    }

    private static string ErrorCode(Exception exception) => exception switch
    {
        IOException => "EXPORT_IO",
        UnauthorizedAccessException => "EXPORT_ACCESS_DENIED",
        _ => "EXPORT_FAILED"
    };

    private static string Extension(CaptureImageFormat format) => format switch
    {
        CaptureImageFormat.Png => ".png",
        CaptureImageFormat.Jpeg => ".jpg",
        CaptureImageFormat.WebP => ".webp",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
}
