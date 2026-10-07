using Wocel.Capture.Logging;
using Xunit;

namespace Wocel.Capture.Tests;

public sealed class ActivityLogTests
{
    [Theory]
    [InlineData("Authorization: Bearer abc.def.ghi", "Authorization: [REDACTED]")]
    [InlineData("access_token=secret-token&x=1", "access_token=[REDACTED]&x=1")]
    [InlineData("refresh_token: very-secret", "refresh_token: [REDACTED]")]
    [InlineData("code=oauth-code-123", "code=[REDACTED]")]
    [InlineData("outer: inner response {\"access_token\":\"secret\"}", "outer: inner response {\"access_token\":\"[REDACTED]\"}")]
    [InlineData("{\"Authorization\":\"Bearer json-secret\"}", "{\"Authorization\":\"[REDACTED]\"}")]
    [InlineData("request failed with Bearer standalone-secret", "request failed with Bearer [REDACTED]")]
    public void Redactor_removes_secrets_but_keeps_diagnostic_context(string input, string expected)
    {
        Assert.Equal(expected, SensitiveDataRedactor.Redact(input));
    }

    [Fact]
    public async Task Logger_writes_structured_event_with_correlation_id_and_sanitized_message()
    {
        using var temp = new TempDirectory();
        var logger = new NdjsonActivityLog(temp.Path);
        var entry = new ActivityEvent(
            DateTimeOffset.Parse("2026-10-05T08:00:00+07:00"),
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "upload_failed",
            ActivityLevel.Warning,
            120,
            false,
            "HTTP_401",
            "Authorization: Bearer top-secret");

        await logger.WriteAsync(entry);
        var line = Assert.Single(await File.ReadAllLinesAsync(Path.Combine(temp.Path, "activity-20261005.ndjson")));

        Assert.Contains("\"eventName\":\"upload_failed\"", line);
        Assert.Contains("11111111-1111-1111-1111-111111111111", line);
        Assert.DoesNotContain("top-secret", line);
        Assert.Contains("[REDACTED]", line);
    }

    [Fact]
    public async Task Logger_prunes_files_older_than_thirty_days()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "activity-20260801.ndjson"), "old");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "activity-20260906.ndjson"), "keep");

        await new NdjsonActivityLog(temp.Path).PruneAsync(DateTimeOffset.Parse("2026-10-05T12:00:00Z"));

        Assert.False(File.Exists(Path.Combine(temp.Path, "activity-20260801.ndjson")));
        Assert.True(File.Exists(Path.Combine(temp.Path, "activity-20260906.ndjson")));
    }

    [Fact]
    public async Task Logger_reads_recent_events_newest_first()
    {
        using var temp = new TempDirectory();
        var logger = new NdjsonActivityLog(temp.Path);
        await logger.WriteAsync(new ActivityEvent(DateTimeOffset.Parse("2026-10-05T08:00:00Z"), Guid.NewGuid(), "first", ActivityLevel.Information, 1, true));
        await logger.WriteAsync(new ActivityEvent(DateTimeOffset.Parse("2026-10-05T09:00:00Z"), Guid.NewGuid(), "second", ActivityLevel.Information, 1, true));

        var events = await logger.ReadRecentAsync(10);

        Assert.Equal(["second", "first"], events.Select(entry => entry.EventName));
    }
}
