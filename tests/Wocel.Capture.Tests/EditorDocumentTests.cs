using Wocel.Capture.Editor;
using Wocel.Capture.Models;
using Xunit;

namespace Wocel.Capture.Tests;

public sealed class EditorDocumentTests
{
    private static readonly ShapeStyle RedStyle = new(EditorColor.FromHex("#EF4444"), 3, null, 1);

    [Theory]
    [InlineData(EditorLayerKind.Pen)]
    [InlineData(EditorLayerKind.Highlight)]
    [InlineData(EditorLayerKind.Text)]
    [InlineData(EditorLayerKind.Blur)]
    [InlineData(EditorLayerKind.Line)]
    [InlineData(EditorLayerKind.Arrow)]
    [InlineData(EditorLayerKind.Rectangle)]
    [InlineData(EditorLayerKind.Ellipse)]
    [InlineData(EditorLayerKind.Triangle)]
    public void Editor_accepts_every_required_layer_kind(EditorLayerKind kind)
    {
        var document = new EditorDocument(new PixelSize(800, 600));
        var layer = TestLayer(kind);

        document.Execute(new AddLayerCommand(layer));

        Assert.Same(layer, Assert.Single(document.Layers));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0.4, 0.4)]
    [InlineData(1, 0.65)]
    public void Highlight_opacity_is_limited_to_safe_range(double input, double expected)
    {
        var layer = HighlightLayer.Create(
            [new PixelPoint(1, 1), new PixelPoint(2, 2)],
            new ShapeStyle(EditorColor.FromHex("#FDE047"), 12, null, input));

        Assert.Equal(expected, layer.Style.Opacity, 2);
    }

    [Fact]
    public void Add_move_delete_commands_are_reversible()
    {
        var document = new EditorDocument(new PixelSize(800, 600));
        var layer = TestLayer(EditorLayerKind.Rectangle);
        document.Execute(new AddLayerCommand(layer));
        document.Execute(new MoveLayerCommand(layer.Id, 15, -5));
        document.Execute(new DeleteLayerCommand(layer.Id));

        Assert.Empty(document.Layers);
        Assert.True(document.Undo());
        Assert.Equal(new PixelRect(25, 5, 100, 80), Assert.Single(document.Layers).Bounds);
        Assert.True(document.Undo());
        Assert.Equal(new PixelRect(10, 10, 100, 80), Assert.Single(document.Layers).Bounds);
        Assert.True(document.Redo());
        Assert.Equal(new PixelRect(25, 5, 100, 80), Assert.Single(document.Layers).Bounds);
    }

    [Fact]
    public void New_edit_discards_redo_branch()
    {
        var document = new EditorDocument(new PixelSize(800, 600));
        document.Execute(new AddLayerCommand(TestLayer(EditorLayerKind.Line)));
        Assert.True(document.Undo());

        document.Execute(new AddLayerCommand(TestLayer(EditorLayerKind.Ellipse)));

        Assert.False(document.Redo());
    }

    [Fact]
    public void Directional_shapes_preserve_reverse_drag_endpoints()
    {
        var start = new PixelPoint(110, 90);
        var end = new PixelPoint(10, 10);

        var layer = new ShapeLayer(Guid.NewGuid(), EditorLayerKind.Arrow, PixelRect.FromPoints(start, end), RedStyle, start, end);

        Assert.Equal(start, layer.Start);
        Assert.Equal(end, layer.End);
        Assert.Equal(new PixelRect(10, 10, 100, 80), layer.Bounds);
    }

    [Fact]
    public void Reset_is_one_undoable_command()
    {
        var document = new EditorDocument(new PixelSize(800, 600));
        document.Execute(new AddLayerCommand(TestLayer(EditorLayerKind.Line)));
        document.Execute(new AddLayerCommand(TestLayer(EditorLayerKind.Text)));

        document.Reset();

        Assert.Empty(document.Layers);
        Assert.True(document.Undo());
        Assert.Equal(2, document.Layers.Count);
    }

    [Fact]
    public void Undo_history_keeps_latest_one_hundred_commands()
    {
        var document = new EditorDocument(new PixelSize(800, 600));
        for (var index = 0; index < 101; index++)
        {
            document.Execute(new AddLayerCommand(TestLayer(EditorLayerKind.Rectangle)));
        }

        var undoCount = 0;
        while (document.Undo())
        {
            undoCount++;
        }

        Assert.Equal(100, undoCount);
        Assert.Single(document.Layers);
    }

    private static EditorLayer TestLayer(EditorLayerKind kind) => kind switch
    {
        EditorLayerKind.Pen => new FreehandLayer(Guid.NewGuid(), [new(10, 10), new(20, 20)], RedStyle),
        EditorLayerKind.Highlight => HighlightLayer.Create([new(10, 10), new(20, 20)], RedStyle),
        EditorLayerKind.Text => new TextLayer(Guid.NewGuid(), new PixelRect(10, 10, 100, 80), "Ghi chú", RedStyle, 16),
        EditorLayerKind.Blur => new BlurLayer(Guid.NewGuid(), new PixelRect(10, 10, 100, 80), 12),
        _ => new ShapeLayer(Guid.NewGuid(), kind, new PixelRect(10, 10, 100, 80), RedStyle)
    };
}
