using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using WPF_OpenStreetmap_Editor.Plugins;
using WPF_OpenStreetmap_Editor.Services;
using WPF_OpenStreetmap_Editor.Views;

namespace WPF_OpenStreetmap_Editor;

public partial class App : Application {
    private static readonly TimeSpan MinimumSplashDuration = TimeSpan.FromMilliseconds(1200);
    private readonly PluginHost _pluginHost = new();

    protected override async void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);

        var settings = AppSettingsService.Load();
        ThemeService.Initialize(settings.ThemeId);

        var stopwatch = Stopwatch.StartNew();
        var splash = new StartupWindow();
        MainWindow = splash;
        splash.Show();

        AppUpdateCheckResult? startupUpdateCheck = null;
        try {
            using var diagnostics = new StartupDiagnosticsService();
            var progress = new Progress<StartupProgressUpdate>(splash.ApplyProgress);
            await diagnostics.RunAsync(progress);
            startupUpdateCheck = diagnostics.LastUpdateCheckResult;
        } catch (Exception ex) {
            Logger.Startup($"启动诊断异常：{ex.GetType().Name}: {ex.Message}");
            splash.ApplyProgress(new StartupProgressUpdate(
                "diagnostics-error",
                "启动诊断",
                "启动检查遇到异常，已写入日志并继续打开主界面",
                StartupCheckState.Warning,
                0.96));
        }

        var remaining = MinimumSplashDuration - stopwatch.Elapsed;
        if (remaining > TimeSpan.Zero) {
            await Task.Delay(remaining);
        }

        IReadOnlyList<PluginActionRequest> startupPluginActions = [];
        try {
            await _pluginHost.ReloadAsync();
            startupPluginActions = await _pluginHost.PublishAsync(PluginHooks.ApplicationStarted);
        } catch (Exception ex) {
            Logger.Error("Failed to initialize plugins", ex);
        }

        var mainWindow = new MainWindow(_pluginHost, startupPluginActions, startupUpdateCheck);
        MainWindow = mainWindow;
        mainWindow.Show();
        splash.Close();
    }

    protected override void OnExit(ExitEventArgs e) {
        ThemeService.Shutdown();

        try {
            _pluginHost.PublishAsync(PluginHooks.ApplicationStopping).GetAwaiter().GetResult();
            _pluginHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
        } catch (Exception ex) {
            Logger.Error("Failed to stop plugins", ex);
        }

        base.OnExit(e);
    }
}
