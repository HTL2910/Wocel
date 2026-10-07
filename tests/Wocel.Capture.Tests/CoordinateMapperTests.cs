using Wocel.Capture.Capture;
using Wocel.Capture.Models;
using Xunit;

namespace Wocel.Capture.Tests;

public sealed class CoordinateMapperTests
{
    [Theory]
    [InlineData(1.0, 100, 50, 100, 50)]
    [InlineData(1.5, 100, 50, 150, 75)]
    [InlineData(3.0, 100, 50, 300, 150)]
    public void Local_dips_map_to_physical_pixels(double scale, double dipX, double dipY, int pixelX, int pixelY)
    {
        var monitor = new MonitorGeometry(new PixelRect(0, 0, 3840, 2160), scale, scale);

        Assert.Equal(new PixelPoint(pixelX, pixelY), CoordinateMapper.ToPhysical(monitor, dipX, dipY));
    }

    [Fact]
    public void Mapping_preserves_negative_monitor_origin()
    {
        var monitor = new MonitorGeometry(new PixelRect(-1920, -200, 1920, 1080), 1.5, 1.5);

        var point = CoordinateMapper.ToPhysical(monitor, 100, 50);

        Assert.Equal(new PixelPoint(-1770, -125), point);
        Assert.Equal((100d, 50d), CoordinateMapper.ToLocalDips(monitor, point));
    }
}
