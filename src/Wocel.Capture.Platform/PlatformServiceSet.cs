using Wocel.Capture.Platform.Capture;
using Wocel.Capture.Platform.Hotkeys;
using Wocel.Capture.Platform.Services;

namespace Wocel.Capture.Platform;

public sealed record PlatformServiceSet
{
    public PlatformServiceSet(
        IScreenCaptureService screenCapture,
        IGlobalHotkeyService globalHotkey,
        IProtectedTokenStore tokenStore,
        IStartupService startup,
        ISingleInstanceService singleInstance,
        IPlatformPaths paths,
        IBrowserLauncher browser)
    {
        ScreenCapture = screenCapture ?? throw new ArgumentNullException(nameof(screenCapture));
        GlobalHotkey = globalHotkey ?? throw new ArgumentNullException(nameof(globalHotkey));
        TokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        Startup = startup ?? throw new ArgumentNullException(nameof(startup));
        SingleInstance = singleInstance ?? throw new ArgumentNullException(nameof(singleInstance));
        Paths = paths ?? throw new ArgumentNullException(nameof(paths));
        Browser = browser ?? throw new ArgumentNullException(nameof(browser));
    }

    public IScreenCaptureService ScreenCapture { get; }
    public IGlobalHotkeyService GlobalHotkey { get; }
    public IProtectedTokenStore TokenStore { get; }
    public IStartupService Startup { get; }
    public ISingleInstanceService SingleInstance { get; }
    public IPlatformPaths Paths { get; }
    public IBrowserLauncher Browser { get; }
}
