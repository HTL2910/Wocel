using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Wocel.Core.Events;
using Wocel.Excel;
using Wocel.Shell.Services;
using Wocel.Shell.ViewModels;
using Wocel.Shell.Views;
using Wocel.Word;

namespace Wocel.Shell;

public partial class App : Application
{
    private AutoSaveService? _autoSaveService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var registry = new ModuleRegistry();
            registry.RegisterModule(new WordModule());
            registry.RegisterModule(new ExcelModule());

            var eventBus = new EventBus();
            var recentFilesService = new RecentFilesService();
            _autoSaveService = new AutoSaveService();
            var settingsService = new SettingsService();
            var cloudSyncService = new CloudSyncService();
            
            var workspaceVm = new ShellWorkspaceViewModel(registry, eventBus, recentFilesService, _autoSaveService, settingsService, cloudSyncService);

            desktop.MainWindow = new MainWindow
            {
                DataContext = workspaceVm
            };

            // Setup cleanup on exit
            desktop.ShutdownRequested += (s, e) =>
            {
                _autoSaveService?.DisableAllAutoSaves();
            };

            // Check for crash recovery on startup
            CheckForCrashRecovery(workspaceVm, registry);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void CheckForCrashRecovery(ShellWorkspaceViewModel viewModel, ModuleRegistry registry)
    {
        try
        {
            if (_autoSaveService == null) return;

            var autoSaves = _autoSaveService.GetAvailableAutoSaves();
            if (autoSaves.Count > 0)
            {
                // Clean old auto-saves (older than 7 days)
                _autoSaveService.CleanOldAutoSaves(TimeSpan.FromDays(7));
                
                // Show recovery dialog if there are recent auto-saves
                var recentAutoSaves = autoSaves
                    .Where(a => a.LastAutoSaveTime > DateTime.Now.AddHours(-1))
                    .ToList();

                if (recentAutoSaves.Count > 0)
                {
                    // TODO: Show recovery dialog to user
                    // For now, just log the available recoveries
                    System.Diagnostics.Debug.WriteLine($"Found {recentAutoSaves.Count} potential crash recovery files");
                }
            }
        }
        catch
        {
            // Ignore crash recovery errors
        }
    }
}
