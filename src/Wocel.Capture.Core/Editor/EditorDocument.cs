using Wocel.Capture.Models;

namespace Wocel.Capture.Editor;

public sealed class EditorDocument
{
    private const int HistoryLimit = 100;
    private readonly List<EditorLayer> _layers = [];
    private readonly List<IEditorCommand> _undo = [];
    private readonly Stack<IEditorCommand> _redo = [];

    public EditorDocument(PixelSize sourceSize)
    {
        SourceSize = sourceSize;
    }

    public PixelSize SourceSize { get; }
    public IReadOnlyList<EditorLayer> Layers => _layers.AsReadOnly();
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    internal int LayerCount => _layers.Count;

    public void Execute(IEditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Apply(this);
        _undo.Add(command);
        if (_undo.Count > HistoryLimit)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        var command = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        command.Revert(this);
        _redo.Push(command);
        return true;
    }

    public bool Redo()
    {
        if (!_redo.TryPop(out var command))
        {
            return false;
        }

        command.Apply(this);
        _undo.Add(command);
        return true;
    }

    public void Reset()
    {
        if (_layers.Count > 0)
        {
            Execute(new ClearLayersCommand());
        }
    }

    internal void InsertLayer(int index, EditorLayer layer) => _layers.Insert(index, layer);

    internal (int Index, EditorLayer Layer) RemoveLayer(Guid id)
    {
        var index = _layers.FindIndex(layer => layer.Id == id);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Layer {id} was not found.");
        }

        var layer = _layers[index];
        _layers.RemoveAt(index);
        return (index, layer);
    }

    internal void ReplaceLayer(Guid id, Func<EditorLayer, EditorLayer> replace)
    {
        var index = _layers.FindIndex(layer => layer.Id == id);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Layer {id} was not found.");
        }

        _layers[index] = replace(_layers[index]);
    }

    internal IReadOnlyList<EditorLayer> SnapshotLayers() => [.. _layers];
    internal void ClearLayers() => _layers.Clear();
}
