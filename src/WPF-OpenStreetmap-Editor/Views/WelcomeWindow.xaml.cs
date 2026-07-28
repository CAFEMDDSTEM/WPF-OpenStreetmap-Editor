using System.ComponentModel;
using System.Reflection;
using System.Windows;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public enum WelcomeAction {
    EditNow,
    Tutorial
}

public partial class WelcomeWindow : Window {
    private readonly AppSettings _settings;

    public WelcomeWindow(AppSettings settings) {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        InitializeComponent();
        VersionTextBlock.Text = LocalizationService.Instance.Format("Startup.Version", HelpContentService.GetVersionText(Assembly.GetExecutingAssembly()));
        ThirdPartyIconsCheckBox.IsChecked = settings.ShowThirdPartyIcons;
        UpdateThirdPartyIconVisibility();
        ThemeService.ApplyWindowTheme(this);
    }

    public WelcomeAction SelectedAction { get; private set; } = WelcomeAction.EditNow;

    protected override void OnClosing(CancelEventArgs e) {
        _settings.ShowThirdPartyIcons = ThirdPartyIconsCheckBox.IsChecked == true;
        if (!AppSettingsService.Save(_settings, out var error)) {
            PersistenceErrorPresenter.Show(this, error);
        }
        base.OnClosing(e);
    }

    private void Close_Click(object sender, RoutedEventArgs e) {
        DialogResult = true;
    }

    private void Tutorial_Click(object sender, RoutedEventArgs e) {
        SelectedAction = WelcomeAction.Tutorial;
        DialogResult = true;
    }

    private void EditNow_Click(object sender, RoutedEventArgs e) {
        SelectedAction = WelcomeAction.EditNow;
        DialogResult = true;
    }

    private void ThirdPartyIconsCheckBox_Changed(object sender, RoutedEventArgs e) {
        UpdateThirdPartyIconVisibility();
    }

    private void UpdateThirdPartyIconVisibility() {
        var visibility = ThirdPartyIconsCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TutorialIcon.Visibility = visibility;
        EditNowIcon.Visibility = visibility;
    }
}
