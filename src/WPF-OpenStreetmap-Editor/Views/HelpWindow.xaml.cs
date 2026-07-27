using System.Windows;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public partial class HelpWindow : Window {
    public HelpWindow() {
        InitializeComponent();
        ThemeService.ApplyWindowTheme(this);
        DataContext = HelpContentService.Create();
    }
}
