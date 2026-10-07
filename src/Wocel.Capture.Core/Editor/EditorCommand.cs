namespace Wocel.Capture.Editor;

public interface IEditorCommand
{
    void Apply(EditorDocument document);
    void Revert(EditorDocument document);
}

public sealed class AddLayerCommand(EditorLayer layer) : IEditorCommand
{
    private int _index;

    public void Apply(EditorDocument document)
    {
        _index = document.LayerCount;
        document.InsertLayer(_index, layer);
    }

    public void Revert(EditorDocument document) => document.RemoveLayer(layer.Id);
}

public sealed class DeleteLayerCommand(Guid layerId) : IEditorCommand
{
    private EditorLayer? _removed;
    private int _index;

    public void Apply(EditorDocument document)
    {
        (_index, _removed) = document.RemoveLayer(layerId);
    }

    public void Revert(EditorDocument document)
    {
        document.InsertLayer(_index, _removed ?? throw new InvalidOperationException("Command was not applied."));
    }
}

public sealed class MoveLayerCommand(Guid layerId, int deltaX, int deltaY) : IEditorCommand
{
    public void Apply(EditorDocument document) => document.ReplaceLayer(layerId, layer => layer.Translate(deltaX, deltaY));
    public void Revert(EditorDocument document) => document.ReplaceLayer(layerId, layer => layer.Translate(-deltaX, -deltaY));
}

internal sealed class ClearLayersCommand : IEditorCommand
{
    private IReadOnlyList<EditorLayer> _removed = [];

    public void Apply(EditorDocument document)
    {
        _removed = document.SnapshotLayers();
        document.ClearLayers();
    }

    public void Revert(EditorDocument document)
    {
        foreach (var layer in _removed)
        {
            document.InsertLayer(document.LayerCount, layer);
        }
    }
}
