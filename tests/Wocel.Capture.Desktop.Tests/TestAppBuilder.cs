using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Wocel.Capture.Desktop;

[assembly: AvaloniaTestApplication(typeof(Wocel.Capture.Desktop.Tests.TestAppBuilder))]

namespace Wocel.Capture.Desktop.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
