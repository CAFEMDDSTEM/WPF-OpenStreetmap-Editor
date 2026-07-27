using System.Reflection;
using System.Windows;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public partial class StartupWindow : Window {
    public StartupWindow() {
        InitializeComponent();

        VersionTextBlock.Text = LocalizationService.Instance.Format("Startup.Version", GetVersionText());
        ThemeService.ApplyWindowTheme(this);
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

    private static string GetVersionText() {
        return HelpContentService.GetVersionText(Assembly.GetExecutingAssembly());
    }
}
