using Wocel.Capture.Editor;

namespace Wocel.Capture.Presentation;

public enum EditorTool
{
    Select,
    Crop,
    Pen,
    Highlight,
    Text,
    Blur,
    Line,
    Arrow,
    Rectangle,
    Ellipse,
    Triangle
}

public sealed class EditorViewModel
{
    private readonly bool _autoUploadDefault;

    public EditorViewModel(EditorDocument document, bool autoUploadDefault)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        _autoUploadDefault = autoUploadDefault;
    }

    public EditorDocument Document { get; }
    public EditorTool SelectedTool { get; set; } = EditorTool.Select;
    public bool? AutoUploadOverride { get; set; }
    public bool ShouldUpload => AutoUploadOverride ?? _autoUploadDefault;
    public bool HasUnexportedChanges { get; private set; }

    public void NotifyDocumentChanged() => HasUnexportedChanges = true;
    public void MarkExported() => HasUnexportedChanges = false;
}
