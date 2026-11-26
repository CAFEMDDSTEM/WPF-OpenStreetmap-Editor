using System.Windows;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public partial class OsmUploadErrorWindow : Window {
    public OsmUploadErrorWindow(string message) {
        InitializeComponent();
        ThemeService.ApplyWindowTheme(this);
        MessageTextBlock.Text = message;
    }
}
