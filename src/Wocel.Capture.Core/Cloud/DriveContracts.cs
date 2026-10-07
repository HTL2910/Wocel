using Wocel.Capture.Models;

namespace Wocel.Capture.Cloud;

public sealed record DriveUploadRequest(
    Guid CaptureId,
    string LocalPath,
    string DisplayName,
    string ContentType,
    long ByteSize,
    string? ExistingFileId = null,
    Uri? ExistingWebLink = null,
    bool CreatePublicLink = true);

public sealed record DriveUploadResult(string FileId, Uri? WebLink, bool IsShared, string? SharingErrorCode)
{
    public static DriveUploadResult Shared(string fileId, Uri webLink) => new(fileId, webLink, true, null);
    public static DriveUploadResult Private(string fileId, string? sharingErrorCode) => new(fileId, null, false, sharingErrorCode);
}

public interface IDriveClient
{
    Task<DriveUploadResult> UploadAndShareAsync(DriveUploadRequest request, CancellationToken cancellationToken);
}

public sealed class DriveOperationException(
    string errorCode,
    bool requiresAuthentication,
    Exception? innerException = null,
    string? uploadedFileId = null,
    Uri? uploadedWebLink = null)
    : Exception(errorCode, innerException)
{
    public string ErrorCode { get; } = errorCode;
    public bool RequiresAuthentication { get; } = requiresAuthentication;
    public string? UploadedFileId { get; } = uploadedFileId;
    public Uri? UploadedWebLink { get; } = uploadedWebLink;
}

internal static class DriveContentTypes
{
    public static string For(CaptureImageFormat format) => format switch
    {
        CaptureImageFormat.Png => "image/png",
        CaptureImageFormat.Jpeg => "image/jpeg",
        CaptureImageFormat.WebP => "image/webp",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
}
