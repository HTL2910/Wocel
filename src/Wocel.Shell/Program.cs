using Avalonia;
using System;
using System.Reflection;
using Wocel.Core.Diagnostics;

namespace Wocel.Shell;

internal sealed class Program
{
    /// <summary>Phiên bản hiển thị của ứng dụng, lấy từ thông tin biên dịch.</summary>
    public static string Version =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?.Split('+')[0]
        ?? "1.0.0";

    [STAThread]
    public static void Main(string[] args)
    {
        ActivityLog.Start(Version);

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception error)
                ActivityLog.Error("app", "unhandled-exception", error);
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            ActivityLog.Error("app", "unobserved-task-exception", e.Exception);
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception error)
        {
            ActivityLog.Error("app", "fatal", error);
            throw;
        }
        finally
        {
            ActivityLog.Stop();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
