using Wocel.Capture.Models;
using Wocel.Capture.Persistence;
using Xunit;

namespace Wocel.Capture.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task History_round_trips_capture_records()
    {
        using var temp = new TempDirectory();
        var repository = new JsonHistoryRepository(Path.Combine(temp.Path, "history.json"));
        var capture = Capture("one.png");

        await repository.UpsertAsync(capture);
        var reloaded = new JsonHistoryRepository(Path.Combine(temp.Path, "history.json"));

        Assert.Equal(capture, Assert.Single(await reloaded.GetAllAsync()));
    }

    [Fact]
    public async Task History_recovers_previous_valid_file_when_primary_is_corrupt()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "history.json");
        var repository = new JsonHistoryRepository(path);
        var first = Capture("first.png");
        await repository.UpsertAsync(first);
        await repository.UpsertAsync(Capture("second.png"));
        await File.WriteAllTextAsync(path, "{ definitely-not-json");

        var recovered = await new JsonHistoryRepository(path).GetAllAsync();

        Assert.Single(recovered);
        Assert.Equal(first.Id, recovered[0].Id);
    }

    [Fact]
    public async Task Saving_after_recovery_does_not_replace_valid_backup_with_corrupt_primary()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "history.json");
        var repository = new JsonHistoryRepository(path);
        var first = Capture("first.png");
        await repository.UpsertAsync(first);
        await repository.UpsertAsync(Capture("second.png"));
        await File.WriteAllTextAsync(path, "{ corrupt");

        var recoveredRepository = new JsonHistoryRepository(path);
        Assert.Single(await recoveredRepository.GetAllAsync());
        await recoveredRepository.UpsertAsync(Capture("third.png"));
        await File.WriteAllTextAsync(path, "{ interrupted-after-save");

        var recoveredAgain = await new JsonHistoryRepository(path).GetAllAsync();
        Assert.Single(recoveredAgain);
        Assert.Equal(first.Id, recoveredAgain[0].Id);
    }

    [Fact]
    public async Task History_serializes_concurrent_updates_without_losing_records()
    {
        using var temp = new TempDirectory();
        var repository = new JsonHistoryRepository(Path.Combine(temp.Path, "history.json"));

        await Task.WhenAll(Enumerable.Range(0, 25).Select(index => repository.UpsertAsync(Capture($"{index}.png"))));

        Assert.Equal(25, (await repository.GetAllAsync()).Count);
    }

    private static CaptureRecord Capture(string name) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = name,
        CreatedAt = DateTimeOffset.Parse("2026-10-05T08:00:00+07:00"),
        Size = new PixelSize(1920, 1080),
        Format = CaptureImageFormat.Png,
        ByteSize = 1234,
        UploadState = UploadState.LocalOnly
    };
}

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wocel-capture-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, true);
        }
    }
}
