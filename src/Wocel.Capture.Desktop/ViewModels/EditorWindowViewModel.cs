using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Wocel.Capture.Editor;
using Wocel.Capture.Models;
using Wocel.Capture.Presentation;

namespace Wocel.Capture.Desktop.ViewModels;

public sealed class EditorWindowViewModel : INotifyPropertyChanged
{
    private static readonly ShapeStyle DefaultStyle = new(EditorColor.FromHex("#EA580C"), 3, null, 1);
    private static readonly ShapeStyle HighlightStyle = new(EditorColor.FromHex("#FDE047"), 14, null, .45);
    private EditorTool _selectedTool = EditorTool.Arrow;
    private Guid? _selectedLayerId;
    private bool _isExportBusy;

    public EditorWindowViewModel(EditorDocument document, bool autoUploadDefault)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        AutoUpload = autoUploadDefault;
        UndoCommand = new DelegateCommand(() => Undo());
        RedoCommand = new DelegateCommand(() => Redo());
        DeleteCommand = new DelegateCommand(DeleteSelected);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? DocumentChanged;
    public EditorDocument Document { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand DeleteCommand { get; }
    public bool AutoUpload { get; set; }
    public int Revision { get; private set; }
    public int ExportedRevision { get; private set; }
    public bool HasUnexportedChanges => Revision != ExportedRevision;
    public bool IsExportBusy => _isExportBusy;
    public Guid? SelectedLayerId => _selectedLayerId;

    public EditorTool SelectedTool
    {
        get => _selectedTool;
        private set => Set(ref _selectedTool, value);
    }

    public string ShortcutModifier => OperatingSystem.IsMacOS() ? "⌘" : "Ctrl";

    public void SelectTool(EditorTool tool) => SelectedTool = tool;

    public void AddStroke(EditorTool tool, IReadOnlyList<PixelPoint> points)
    {
        if (points.Count < 2) return;
        EditorLayer layer = tool switch
        {
            EditorTool.Pen => new FreehandLayer(Guid.NewGuid(), [.. points], DefaultStyle),
            EditorTool.Highlight => HighlightLayer.Create([.. points], HighlightStyle),
            _ => throw new ArgumentOutOfRangeException(nameof(tool), "Tool is not a stroke tool.")
        };
        Execute(new AddLayerCommand(layer), layer.Id);
    }

    public void AddText(PixelRect bounds, string text, string? hexColor = null, double fontSize = 18)
    {
        var style = !string.IsNullOrWhiteSpace(hexColor)
            ? new ShapeStyle(EditorColor.FromHex(hexColor), 3, null, 1)
            : DefaultStyle;
        Add(new TextLayer(Guid.NewGuid(), bounds, text, style, Math.Max(10, fontSize)));
    }

    public void AddBlur(PixelRect bounds, double radius) =>
        Add(new BlurLayer(Guid.NewGuid(), bounds, radius));

    public void AddShape(EditorTool tool, PixelPoint start, PixelPoint end)
    {
        var kind = tool switch
        {
            EditorTool.Line => EditorLayerKind.Line,
            EditorTool.Arrow => EditorLayerKind.Arrow,
            EditorTool.Rectangle => EditorLayerKind.Rectangle,
            EditorTool.Ellipse => EditorLayerKind.Ellipse,
            EditorTool.Triangle => EditorLayerKind.Triangle,
            _ => throw new ArgumentOutOfRangeException(nameof(tool), "Tool is not a 2D shape.")
        };
        Add(new ShapeLayer(Guid.NewGuid(), kind, PixelRect.FromPoints(start, end), DefaultStyle, start, end));
    }

    public void ApplyCrop(PixelRect bounds) => Add(new CropLayer(Guid.NewGuid(), bounds));
    public void ApplyResize(PixelSize size) => Add(new ResizeLayer(Guid.NewGuid(), size));
    public void SelectLayer(Guid? id) => Set(ref _selectedLayerId, id, nameof(SelectedLayerId));

    public void MoveSelected(int deltaX, int deltaY)
    {
        if (_selectedLayerId is { } id) Execute(new MoveLayerCommand(id, deltaX, deltaY), id);
    }

    public void DeleteSelected()
    {
        if (_selectedLayerId is not { } id) return;
        Execute(new DeleteLayerCommand(id), null);
    }

    public bool Undo()
    {
        if (!Document.Undo()) return false;
        Changed();
        return true;
    }

    public bool Redo()
    {
        if (!Document.Redo()) return false;
        Changed();
        return true;
    }

    public void Reset()
    {
        if (Document.Layers.Count > 0)
        {
            Document.Reset();
            Changed();
        }
        SelectLayer(null);
    }

    public void BeginExport()
    {
        if (_isExportBusy) throw new InvalidOperationException("An export is already in progress.");
        _isExportBusy = true;
        OnPropertyChanged(nameof(IsExportBusy));
    }

    public void FinishExport(bool succeeded)
    {
        if (succeeded) ExportedRevision = Revision;
        _isExportBusy = false;
        OnPropertyChanged(nameof(IsExportBusy));
        OnPropertyChanged(nameof(ExportedRevision));
        OnPropertyChanged(nameof(HasUnexportedChanges));
    }

    private void Add(EditorLayer layer) => Execute(new AddLayerCommand(layer), layer.Id);

    private void Execute(IEditorCommand command, Guid? selected)
    {
        Document.Execute(command);
        SelectLayer(selected);
        Changed();
    }

    private void Changed()
    {
        Revision++;
        OnPropertyChanged(nameof(Revision));
        OnPropertyChanged(nameof(HasUnexportedChanges));
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
