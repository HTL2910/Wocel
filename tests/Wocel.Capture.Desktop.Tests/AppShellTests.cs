using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Wocel.Capture.Desktop.ViewModels;
using Wocel.Capture.Desktop.Views;
using Wocel.Capture.Desktop.Composition;
using Xunit;

namespace Wocel.Capture.Desktop.Tests;

public sealed class AppShellTests
{
    [AvaloniaFact]
    public void Main_window_exposes_history_log_and_settings_tabs()
    {
        var window = CreateWindow();
        var tabNames = window.GetVisualDescendants().OfType<TabItem>().Select(item => item.Header?.ToString()).ToArray();

        Assert.Contains("History", tabNames);
        Assert.Contains("Log", tabNames);
        Assert.Contains("Settings", tabNames);
    }

    [AvaloniaFact]
    public void Capture_and_google_buttons_have_accessible_names()
    {
        var window = CreateWindow();
        var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();

        Assert.Contains(buttons, button => AutomationProperties.GetName(button) == "Capture screen");
        Assert.Contains(buttons, button => AutomationProperties.GetName(button) == "Sign in with Google");
    }

    [AvaloniaFact]
    public void Public_link_copy_requires_explicit_choice_and_status_has_text()
    {
        var viewModel = new MainWindowViewModel();

        Assert.False(viewModel.CreatePublicLink);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.StatusText));
        Assert.Contains("private", viewModel.SharingDisclosure, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void First_run_explains_google_public_links_and_screen_recording()
    {
        var window = new FirstRunWindow();
        window.Show();
        var text = string.Join(" ", window.GetVisualDescendants().OfType<TextBlock>().Select(item => item.Text));

        Assert.Contains("anyone with the link", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Screen Recording", text, StringComparison.OrdinalIgnoreCase);
    }

    private static MainWindow CreateWindow()
    {
        var window = new MainWindow { DataContext = new MainWindowViewModel() };
        window.Show();
        return window;
    }

    [Theory]
    [InlineData(false, "Wocel.Capture.Desktop")]
    [InlineData(true, "Wocel.Capture.Desktop", "--background")]
    public void Launch_options_control_only_initial_window_visibility(bool expectedHidden, params string[] arguments)
    {
        Assert.Equal(expectedHidden, AppLaunchOptions.Parse(arguments).StartHidden);
    }
}
