using Wocel.Capture.Export;
using Wocel.Capture.Models;
using Xunit;

namespace Wocel.Capture.Tests;

public sealed class CaptureFileNamerTests
{
    [Fact]
    public void Namer_adds_incrementing_suffix_for_collisions()
    {
        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "/captures/Screenshot_2026-10-05_08-09-10.png",
            "/captures/Screenshot_2026-10-05_08-09-10-2.png"
        };

        var result = CaptureFileNamer.NextAvailable(
            "/captures",
            DateTimeOffset.Parse("2026-10-05T08:09:10+07:00"),
            CaptureImageFormat.Png,
            occupied.Contains);

        Assert.Equal("/captures/Screenshot_2026-10-05_08-09-10-3.png", result);
    }
}
