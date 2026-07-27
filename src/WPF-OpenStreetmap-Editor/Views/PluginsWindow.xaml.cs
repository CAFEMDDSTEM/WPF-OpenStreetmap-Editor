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

    public PluginsWindow(PluginHost pluginHost) {
        InitializeComponent();
        ThemeService.ApplyWindowTheme(this);
        _pluginHost = pluginHost;
        RefreshList();
    }

    private async void Install_Click(object sender, RoutedEventArgs e) {
        var dialog = new OpenFileDialog {
            Title = "选择插件包",
            Filter = "WOSM 插件|plugin.json5;*.wosm-plugin;*.zip|插件声明|plugin.json5|插件包|*.wosm-plugin;*.zip|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        try {
            var candidate = _pluginHost.Installer.Inspect(dialog.FileName);
            var allowCodeExecution = false;
            if (candidate.RequiresCodeExecutionConsent) {
                var answer = MessageBox.Show(
                    $"“{candidate.Manifest.Name}”包含在主进程内运行的原生 DLL。\n\n" +
                    "它可以读取或修改当前用户可访问的文件、访问网络、控制主程序并启动其他程序。" +
                    "只有在你信任来源时才应继续。\n\n是否安装并允许运行？",
                    "原生 DLL 插件风险",
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
            MessageBox.Show(ex.Message, "无法安装插件", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Trust_Click(object sender, RoutedEventArgs e) {
        if (PluginsDataGrid.SelectedItem is not PluginDescriptor {
            Status: PluginLoadStatus.Untrusted,
            Manifest: not null
        } descriptor) return;

        var answer = MessageBox.Show(
            $"“{descriptor.Name}”是手动放入插件目录或内容已发生变化的原生 DLL 插件。\n\n" +
            "确认后它将以当前用户权限运行。是否信任当前文件并加载？",
            "确认原生 DLL 插件",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        try {
            await _pluginHost.TrustAndReloadAsync(descriptor.Id);
            RefreshList();
        } catch (Exception ex) {
            MessageBox.Show(ex.Message, "无法加载插件", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Reload_Click(object sender, RoutedEventArgs e) {
        try {
            await _pluginHost.ReloadAsync();
            RefreshList();
        } catch (Exception ex) {
            MessageBox.Show(ex.Message, "无法扫描插件", MessageBoxButton.OK, MessageBoxImage.Error);
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
