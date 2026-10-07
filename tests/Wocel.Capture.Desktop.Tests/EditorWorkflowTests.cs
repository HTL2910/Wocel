using Wocel.Capture.Desktop.ViewModels;
using Wocel.Capture.Editor;
using Wocel.Capture.Models;
using Wocel.Capture.Presentation;
using Xunit;

namespace Wocel.Capture.Desktop.Tests;

public sealed class EditorWorkflowTests
{
    [Theory]
    [InlineData(EditorTool.Pen)]
    [InlineData(EditorTool.Highlight)]
    [InlineData(EditorTool.Text)]
    [InlineData(EditorTool.Blur)]
    [InlineData(EditorTool.Crop)]
    [InlineData(EditorTool.Line)]
    [InlineData(EditorTool.Arrow)]
    [InlineData(EditorTool.Rectangle)]
    [InlineData(EditorTool.Ellipse)]
    [InlineData(EditorTool.Triangle)]
    public void Tool_selection_is_exposed_by_shared_editor(EditorTool tool)
    {
        var viewModel = Create();

        viewModel.SelectTool(tool);

        Assert.Equal(tool, viewModel.SelectedTool);
    }

    [Fact]
    public void Editor_creates_every_layer_and_preserves_reverse_arrow_endpoints()
    {
        var viewModel = Create();
        viewModel.AddStroke(EditorTool.Pen, [new(1, 1), new(5, 5)]);
        viewModel.AddStroke(EditorTool.Highlight, [new(1, 8), new(5, 8)]);
        viewModel.AddText(new PixelRect(2, 10, 30, 12), "note");
        viewModel.AddBlur(new PixelRect(10, 10, 20, 20), 4);
        foreach (var tool in new[] { EditorTool.Line, EditorTool.Rectangle, EditorTool.Ellipse, EditorTool.Triangle })
            viewModel.AddShape(tool, new PixelPoint(4, 4), new PixelPoint(20, 20));
        viewModel.AddShape(EditorTool.Arrow, new PixelPoint(30, 30), new PixelPoint(3, 2));
        viewModel.ApplyCrop(new PixelRect(1, 1, 80, 60));
        viewModel.ApplyResize(new PixelSize(160, 120));

        Assert.Equal(11, viewModel.Document.Layers.Count);
        var arrow = Assert.IsType<ShapeLayer>(viewModel.Document.Layers[8]);
        Assert.Equal(new PixelPoint(30, 30), arrow.Start);
        Assert.Equal(new PixelPoint(3, 2), arrow.End);
    }

    [Fact]
    public void Select_move_delete_undo_redo_revision_and_reset_remain_consistent()
    {
        var viewModel = Create();
        viewModel.AddShape(EditorTool.Rectangle, new PixelPoint(10, 10), new PixelPoint(30, 30));
        var layer = Assert.Single(viewModel.Document.Layers);
        viewModel.SelectLayer(layer.Id);
        viewModel.MoveSelected(5, -2);
        viewModel.DeleteSelected();

        Assert.Empty(viewModel.Document.Layers);
        Assert.True(viewModel.Undo());
        Assert.True(viewModel.Undo());
        Assert.Equal(new PixelRect(10, 10, 20, 20), Assert.Single(viewModel.Document.Layers).Bounds);
        Assert.True(viewModel.Redo());
        Assert.True(viewModel.HasUnexportedChanges);
        Assert.True(viewModel.Revision > 0);

        viewModel.Reset();
        Assert.Null(viewModel.SelectedLayerId);
        Assert.Empty(viewModel.Document.Layers);
    }

    [Fact]
    public void Export_busy_state_and_exported_revision_are_explicit()
    {
        var viewModel = Create();
        viewModel.AddShape(EditorTool.Ellipse, new PixelPoint(1, 1), new PixelPoint(20, 20));

        viewModel.BeginExport();
        Assert.True(viewModel.IsExportBusy);
        viewModel.FinishExport(succeeded: true);

        Assert.False(viewModel.IsExportBusy);
        Assert.False(viewModel.HasUnexportedChanges);
        Assert.Equal(viewModel.Revision, viewModel.ExportedRevision);
    }

    [Fact]
    public void Unified_color_updates_new_shapes_strokes_and_text()
    {
        var viewModel = Create();
        viewModel.CurrentColorHex = "#10B981";

        viewModel.AddShape(EditorTool.Rectangle, new PixelPoint(5, 5), new PixelPoint(25, 25));
        viewModel.AddStroke(EditorTool.Pen, [new(2, 2), new(10, 10)]);
        viewModel.AddText(new PixelRect(10, 10, 50, 20), "Hello", fontSize: 20);

        var rectLayer = Assert.IsType<ShapeLayer>(viewModel.Document.Layers[0]);
        var penLayer = Assert.IsType<FreehandLayer>(viewModel.Document.Layers[1]);
        var textLayer = Assert.IsType<TextLayer>(viewModel.Document.Layers[2]);

        Assert.Equal(EditorColor.FromHex("#10B981"), rectLayer.Style.Stroke);
        Assert.Equal(EditorColor.FromHex("#10B981"), penLayer.Style.Stroke);
        Assert.Equal(EditorColor.FromHex("#10B981"), textLayer.Style.Stroke);
        Assert.Equal(20, textLayer.FontSize);
    }

    private static EditorWindowViewModel Create() => new(new EditorDocument(new PixelSize(100, 80)), autoUploadDefault: false);
}
