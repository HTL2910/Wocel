namespace Wocel.Capture.Models;

public enum UploadState
{
    LocalOnly,
    Pending,
    Uploading,
    UploadedPrivate,
    UploadedShared,
    Failed,
    AuthenticationRequired
}

public sealed record CaptureRecord
{
    public required Guid Id { get; init; }
    public required string DisplayName { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required PixelSize Size { get; init; }
    public required CaptureImageFormat Format { get; init; }
    public long ByteSize { get; init; }
    public string? LocalPath { get; init; }
    public string? ThumbnailPath { get; init; }
    public string? DriveFileId { get; init; }
    public Uri? DriveWebLink { get; init; }
    public bool CreatePublicLink { get; init; }
    public UploadState UploadState { get; init; } = UploadState.LocalOnly;
    public string? LastErrorCode { get; init; }
    public int RetryCount { get; init; }

    public CaptureRecord Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("Capture ID must not be empty.", nameof(Id));
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            throw new ArgumentException("Display name is required.", nameof(DisplayName));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(ByteSize);
        ArgumentOutOfRangeException.ThrowIfNegative(RetryCount);
        return this;
    }
}
