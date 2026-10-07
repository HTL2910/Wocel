using Wocel.Capture.Desktop.Capture;
using Wocel.Capture.Models;
using Wocel.Capture.Platform.Capture;
using Xunit;

namespace Wocel.Capture.Desktop.Tests;

public sealed class SelectionOverlayTests
{
    [Theory]
    [InlineData(1.0, 100, 50, 100, 50)]
    [InlineData(1.5, 100, 50, 150, 75)]
    [InlineData(2.0, 100, 50, 200, 100)]
    [InlineData(3.0, 100, 50, 300, 150)]
    public void Pointer_coordinates_map_to_each_displays_physical_scale(double scale, double x, double y, int expectedX, int expectedY)
    {
        var display = Display("main", new PixelRect(0, 0, 1200, 900), new LogicalRect(0, 0, 1200 / scale, 900 / scale), scale);
        var controller = new SelectionController([display]);

        controller.BeginDrag("main", x, y);

        Assert.Equal(new PixelPoint(expectedX, expectedY), controller.Anchor);
    }

    [Fact]
    public void Reverse_drag_crosses_retina_and_non_retina_displays_with_negative_origin()
    {
        var left = Display("left", new PixelRect(-1920, 0, 1920, 1080), new LogicalRect(-1920, 0, 1920, 1080), 1);
        var retina = Display("retina", new PixelRect(0, 0, 3024, 1964), new LogicalRect(0, 0, 1512, 982), 2, true);
        var controller = new SelectionController([left, retina]);

        controller.BeginDrag("retina", 100, 100);
        controller.UpdateDrag("left", -1800, 500);

        Assert.Equal(new PixelRect(-1800, 200, 2000, 300), controller.Selection);
        Assert.True(controller.TryComplete(out var completed));
        Assert.Equal(controller.Selection, completed);
    }

    [Fact]
    public void Keyboard_moves_one_or_ten_physical_pixels_and_clamps_to_virtual_bounds()
    {
        var controller = new SelectionController([Display("main", new PixelRect(-100, -50, 200, 100), new LogicalRect(-100, -50, 200, 100), 1)]);
        controller.SetSelection(new PixelRect(-99, -49, 20, 20));

        controller.Nudge(-1, -1, coarse: false);
        controller.Nudge(-1, -1, coarse: true);

        Assert.Equal(new PixelRect(-100, -50, 20, 20), controller.Selection);
        controller.Nudge(1, 1, coarse: true);
        Assert.Equal(new PixelRect(-90, -40, 20, 20), controller.Selection);
    }

    [Fact]
    public void Selection_smaller_than_two_by_two_is_rejected_but_two_by_two_is_valid()
    {
        var controller = new SelectionController([Display("main", new PixelRect(0, 0, 100, 100), new LogicalRect(0, 0, 100, 100), 1)]);
        controller.SetSelection(new PixelRect(1, 1, 1, 2));
        Assert.False(controller.TryComplete(out _));

        controller.SetSelection(new PixelRect(1, 1, 2, 2));
        Assert.True(controller.TryComplete(out _));
    }

    private static DisplayGeometry Display(string id, PixelRect physical, LogicalRect logical, double scale, bool primary = false) =>
        new(id, physical, logical, scale, primary);
}
