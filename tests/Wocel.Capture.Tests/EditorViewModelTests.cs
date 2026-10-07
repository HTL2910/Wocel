using Wocel.Capture.Editor;
using Wocel.Capture.Logging;
using Wocel.Capture.Models;
using Wocel.Capture.Presentation;
using Xunit;

namespace Wocel.Capture.Tests;

public sealed class EditorViewModelTests
{
    [Theory]
    [InlineData(EditorTool.Pen)]
    [InlineData(EditorTool.Highlight)]
    [InlineData(EditorTool.Text)]
    [InlineData(EditorTool.Blur)]
    [InlineData(EditorTool.Line)]
    [InlineData(EditorTool.Arrow)]
    [InlineData(EditorTool.Rectangle)]
    [InlineData(EditorTool.Ellipse)]
    [InlineData(EditorTool.Triangle)]
    public void Tool_selection_is_explicit(EditorTool tool)
    {
        var viewModel = new EditorViewModel(new EditorDocument(new PixelSize(800, 600)), autoUploadDefault: true);

        viewModel.SelectedTool = tool;

        Assert.Equal(tool, viewModel.SelectedTool);
    }

    [Fact]
    public void Per_capture_auto_upload_override_wins_over_default()
    {
        var viewModel = new EditorViewModel(new EditorDocument(new PixelSize(800, 600)), autoUploadDefault: true);
        Assert.True(viewModel.ShouldUpload);

        viewModel.AutoUploadOverride = false;

        Assert.False(viewModel.ShouldUpload);
    }

    [Fact]
    public void Unsaved_close_warning_tracks_real_edits_and_export()
    {
        var document = new EditorDocument(new PixelSize(800, 600));
        var viewModel = new EditorViewModel(document, autoUploadDefault: false);
        var style = new ShapeStyle(EditorColor.FromHex("#0D9488"), 3, null, 1);
        document.Execute(new AddLayerCommand(new ShapeLayer(Guid.NewGuid(), EditorLayerKind.Rectangle, new PixelRect(1, 1, 20, 20), style)));
        viewModel.NotifyDocumentChanged();

        Assert.True(viewModel.HasUnexportedChanges);

        viewModel.MarkExported();
        Assert.False(viewModel.HasUnexportedChanges);
    }

    [Fact]
    public void Crop_and_resize_are_layers_so_they_are_undoable()
    {
        var document = new EditorDocument(new PixelSize(800, 600));
        document.Execute(new AddLayerCommand(new CropLayer(Guid.NewGuid(), new PixelRect(10, 20, 400, 300))));
        document.Execute(new AddLayerCommand(new ResizeLayer(Guid.NewGuid(), new PixelSize(1200, 900))));

        Assert.IsType<ResizeLayer>(document.Layers[^1]);
        Assert.True(document.Undo());
        Assert.IsType<CropLayer>(document.Layers[^1]);
    }

    [Fact]
    public void Canvas_coordinates_map_back_to_source_after_crop_and_resize()
    {
        var document = new EditorDocument(new PixelSize(800, 600));
        document.Execute(new AddLayerCommand(new CropLayer(Guid.NewGuid(), new PixelRect(100, 50, 400, 300))));
        document.Execute(new AddLayerCommand(new ResizeLayer(Guid.NewGuid(), new PixelSize(200, 150))));

        var point = EditorCanvasMapper.ToSource(document, new PixelPoint(100, 75), new PixelSize(200, 150));

        Assert.Equal(new PixelPoint(300, 200), point);
    }

    [Fact]
    public void History_filters_by_state_and_search_text()
    {
        var records = new[]
        {
            Capture("Meeting.png", UploadState.UploadedShared),
            Capture("Bug.png", UploadState.Failed),
            Capture("Local.png", UploadState.LocalOnly)
        };

        var result = HistoryFilter.Apply(records, "bug", HistoryStateFilter.Failed);

        Assert.Equal("Bug.png", Assert.Single(result).DisplayName);
    }

    [Theory]
    [InlineData(UploadState.Failed, true)]
    [InlineData(UploadState.Pending, true)]
    [InlineData(UploadState.AuthenticationRequired, true)]
    [InlineData(UploadState.UploadedShared, false)]
    public void Retry_availability_matches_upload_state(UploadState state, bool expected)
    {
        Assert.Equal(expected, HistoryFilter.CanRetry(Capture("capture.png", state)));
    }

    [Fact]
    public void Activity_filter_matches_event_error_and_message_without_exposing_secrets()
    {
        var events = new[]
        {
            new ActivityEvent(DateTimeOffset.UtcNow, Guid.NewGuid(), "upload_failed", ActivityLevel.Warning, 100, false, "HTTP_401", "access_token=secret"),
            new ActivityEvent(DateTimeOffset.UtcNow, Guid.NewGuid(), "capture_completed", ActivityLevel.Information, 20, true)
        };

        var result = ActivityLogFilter.Apply(events, "401");

        var entry = Assert.Single(result);
        Assert.Equal("HTTP_401", entry.ErrorCode);
        Assert.DoesNotContain("secret", ActivityLogFilter.SafeMessage(entry));
    }

    private static CaptureRecord Capture(string name, UploadState state) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = name,
        CreatedAt = DateTimeOffset.UtcNow,
        Size = new PixelSize(100, 80),
        Format = CaptureImageFormat.Png,
        UploadState = state
    };
}
