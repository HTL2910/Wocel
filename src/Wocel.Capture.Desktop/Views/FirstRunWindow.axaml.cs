using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Wocel.Capture.Desktop.Views;

public sealed partial class FirstRunWindow : Window
{
    public FirstRunWindow() => InitializeComponent();

    private void Continue(object? sender, RoutedEventArgs e) => Close(true);
}
