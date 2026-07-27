using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using WPF_OpenStreetmap_Editor.Services;
using WPF_OpenStreetmap_Editor.Views;

namespace WPF_OpenStreetmap_Editor;

public partial class App : Application {
    private static readonly TimeSpan MinimumSplashDuration = TimeSpan.FromMilliseconds(1200);

    protected override async void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);

        var stopwatch = Stopwatch.StartNew();
        var splash = new StartupWindow();
        MainWindow = splash;
        splash.Show();

        try {
            using var diagnostics = new StartupDiagnosticsService();
            var progress = new Progress<StartupProgressUpdate>(splash.ApplyProgress);
            await diagnostics.RunAsync(progress);
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

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
        splash.Close();
    }
}
