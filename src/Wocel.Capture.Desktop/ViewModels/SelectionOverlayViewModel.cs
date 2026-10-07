using System.ComponentModel;
using Wocel.Capture.Desktop.Capture;
using Wocel.Capture.Models;

namespace Wocel.Capture.Desktop.ViewModels;

public sealed class SelectionOverlayViewModel : INotifyPropertyChanged
{
    public SelectionOverlayViewModel(string displayId, SelectionController controller)
    {
        DisplayId = displayId;
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        Controller.SelectionChanged += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionText)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string DisplayId { get; }
    public SelectionController Controller { get; }
    public PixelRect Selection => Controller.Selection;
    public string SelectionText => Selection.IsUsableSelection ? $"{Selection.Width} × {Selection.Height} px" : "Drag to select";
}
