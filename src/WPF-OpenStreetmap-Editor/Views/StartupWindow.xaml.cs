using System.Reflection;
using System.Windows;
using System.Windows.Media;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public partial class StartupWindow : Window {
    public StartupWindow() {
        InitializeComponent();

        VersionTextBlock.Text = $"Version {GetVersionText()}";
        ApplyTheme(SystemThemeService.GetCurrentTheme());
    }

    public void ApplyProgress(StartupProgressUpdate update) {
        if (!Dispatcher.CheckAccess()) {
            Dispatcher.Invoke(() => ApplyProgress(update));
            return;
        }

        var percent = Math.Clamp(update.Progress * 100, 0, 100);
        StartupProgressBar.Value = percent;
        PercentTextBlock.Text = $"{percent:0}%";
        StatusTitleTextBlock.Text = update.Title;
        StatusTextBlock.Text = update.Detail;
    }

    private void ApplyTheme(SystemThemeMode theme) {
        if (theme == SystemThemeMode.HighContrast) {
            Resources["SplashWindowBackgroundBrush"] = SystemColors.WindowBrush;
            Resources["SplashTextBrush"] = SystemColors.WindowTextBrush;
            Resources["SplashMutedTextBrush"] = SystemColors.GrayTextBrush;
            Resources["SplashBorderBrush"] = SystemColors.ActiveBorderBrush;
            Resources["SplashProgressTrackBrush"] = SystemColors.InactiveBorderBrush;
            Resources["SplashProgressBrush"] = SystemColors.HighlightBrush;
            return;
        }

        if (theme == SystemThemeMode.Dark) {
            SetBrush("SplashWindowBackgroundBrush", "#101010");
            SetBrush("SplashTextBrush", "#FFFFFF");
            SetBrush("SplashMutedTextBrush", "#B7B7B7");
            SetBrush("SplashBorderBrush", "#505050");
            SetBrush("SplashProgressTrackBrush", "#303030");
            SetBrush("SplashProgressBrush", "#F5F5F5");
        }
    }

    private void SetBrush(string key, string color) {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        Resources[key] = brush;
    }

    private static string GetVersionText() {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version is null) return "0.1.0";

        return $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }
}
