using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using WPF_OpenStreetmap_Editor.Plugins;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public partial class PluginsWindow : Window {
    private readonly PluginHost _pluginHost;
    private static LocalizationService L => LocalizationService.Instance;

    public PluginsWindow(PluginHost pluginHost) {
        InitializeComponent();
        ThemeService.ApplyWindowTheme(this);
        _pluginHost = pluginHost;
        RefreshList();
    }

    private async void Install_Click(object sender, RoutedEventArgs e) {
        var dialog = new OpenFileDialog {
            Title = L.GetString("Plugins.SelectPackageTitle"),
            Filter = L.GetString("Plugins.PackageFilter")
        };
        if (dialog.ShowDialog(this) != true) return;

        try {
            var candidate = _pluginHost.Installer.Inspect(dialog.FileName);
            var allowCodeExecution = false;
            if (candidate.RequiresCodeExecutionConsent) {
                var answer = MessageBox.Show(
                    L.Format("Plugins.NativeRiskMessage", candidate.Manifest.Name),
                    L.GetString("Plugins.NativeRiskTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes) return;
                allowCodeExecution = true;
            }

            _pluginHost.Installer.Install(dialog.FileName, allowCodeExecution);
            await _pluginHost.ReloadAsync();
            RefreshList();
        } catch (Exception ex) {
            MessageBox.Show(ex.Message, L.GetString("Plugins.InstallErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Trust_Click(object sender, RoutedEventArgs e) {
        if (PluginsDataGrid.SelectedItem is not PluginDescriptor {
            Status: PluginLoadStatus.Untrusted,
            Manifest: not null
        } descriptor) return;

        var answer = MessageBox.Show(
            L.Format("Plugins.NativeTrustMessage", descriptor.Name),
            L.GetString("Plugins.NativeTrustTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        try {
            await _pluginHost.TrustAndReloadAsync(descriptor.Id);
            RefreshList();
        } catch (Exception ex) {
            MessageBox.Show(ex.Message, L.GetString("Plugins.LoadErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Reload_Click(object sender, RoutedEventArgs e) {
        try {
            await _pluginHost.ReloadAsync();
            RefreshList();
        } catch (Exception ex) {
            MessageBox.Show(ex.Message, L.GetString("Plugins.ScanErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PluginsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        TrustButton.IsEnabled = PluginsDataGrid.SelectedItem is PluginDescriptor {
            Status: PluginLoadStatus.Untrusted,
            Manifest: not null
        };
        UpdatePluginDetails(PluginsDataGrid.SelectedItem as PluginDescriptor);
    }

    private void RefreshList() {
        PluginsDataGrid.ItemsSource = null;
        PluginsDataGrid.ItemsSource = _pluginHost.Plugins;
        TrustButton.IsEnabled = false;
        UpdatePluginDetails(null);
    }

    private void UpdatePluginDetails(PluginDescriptor? descriptor) {
        PluginIconImage.Source = null;
        if (descriptor is null) {
            PluginDetailsPanel.Visibility = Visibility.Collapsed;
            PluginDetailsTitle.Text = "";
            PluginDetailsDescription.Text = "";
            return;
        }

        PluginDetailsPanel.Visibility = Visibility.Visible;
        PluginDetailsTitle.Text = string.IsNullOrWhiteSpace(descriptor.Version)
            ? descriptor.Name
            : $"{descriptor.Name} {descriptor.Version}";
        PluginDetailsDescription.Text = string.IsNullOrWhiteSpace(descriptor.Error)
            ? descriptor.Description
            : string.IsNullOrWhiteSpace(descriptor.Description)
                ? descriptor.Error
                : $"{descriptor.Description}{Environment.NewLine}{Environment.NewLine}{descriptor.Error}";
        if (descriptor.Manifest is null || string.IsNullOrWhiteSpace(descriptor.IconPath)) return;

        try {
            using var stream = new FileStream(
                descriptor.IconPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            var icon = new BitmapImage();
            icon.BeginInit();
            icon.CacheOption = BitmapCacheOption.OnLoad;
            icon.DecodePixelWidth = 64;
            icon.StreamSource = stream;
            icon.EndInit();
            icon.Freeze();
            PluginIconImage.Source = icon;
        } catch (Exception ex) {
            Logger.Error($"Failed to display icon for plugin '{descriptor.Id}'", ex);
        }
    }
}
