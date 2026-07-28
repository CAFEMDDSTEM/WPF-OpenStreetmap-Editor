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

        await InstallPackageAsync(dialog.FileName);
    }

    private async void Window_Drop(object sender, DragEventArgs e) {
        e.Handled = true;
        if (!TryGetDroppedPluginSource(e.Data, out var sourcePath) || sourcePath is null) {
            return;
        }

        await InstallPackageAsync(sourcePath);
    }

    private void Window_DragOver(object sender, DragEventArgs e) {
        e.Effects = TryGetDroppedPluginSource(e.Data, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async Task InstallPackageAsync(string sourcePath) {
        try {
            var candidate = _pluginHost.Installer.Inspect(sourcePath);
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

            _pluginHost.Installer.Install(sourcePath, allowCodeExecution);
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

    private static bool TryGetDroppedPluginSource(IDataObject data, out string? sourcePath) {
        sourcePath = null;
        if (data.GetData(DataFormats.FileDrop) is not string[] files || files.Length != 1) {
            return false;
        }

        var candidate = files[0];
        if (!File.Exists(candidate) || !IsSupportedPluginSource(candidate)) {
            return false;
        }

        sourcePath = candidate;
        return true;
    }

    private static bool IsSupportedPluginSource(string sourcePath) {
        var extension = Path.GetExtension(sourcePath);
        return string.Equals(extension, ".json5", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".wosm-plugin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".jar", StringComparison.OrdinalIgnoreCase);
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
