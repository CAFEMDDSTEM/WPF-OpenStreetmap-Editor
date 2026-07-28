using System.Text;
using System.Windows;
using System.Windows.Input;
using WPF_OpenStreetmap_Editor.Plugins;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public partial class PythonTerminalWindow : Window {
    private readonly PluginHost _pluginHost;
    private readonly string _pluginId;
    private readonly string _pluginName;
    private readonly string _commandId;
    private readonly StringBuilder _output = new();

    public PythonTerminalWindow(
        PluginHost pluginHost,
        string pluginId,
        string pluginName,
        string commandId,
        string title,
        string intro) {
        InitializeComponent();
        ThemeService.ApplyWindowTheme(this);
        _pluginHost = pluginHost;
        _pluginId = pluginId;
        _pluginName = pluginName;
        _commandId = commandId;
        Title = title;
        IntroTextBlock.Text = intro;
        AppendOutput(">>> ");
    }

    private async void Execute_Click(object sender, RoutedEventArgs e) {
        await ExecuteCommandAsync();
    }

    private async void CommandTextBox_KeyDown(object sender, KeyEventArgs e) {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await ExecuteCommandAsync();
    }

    private async Task ExecuteCommandAsync() {
        var code = CommandTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(code)) return;

        CommandTextBox.Clear();
        AppendOutput($">>> {code}");
        try {
            var result = await _pluginHost.ExecuteCommandAsync(
                _pluginId,
                _commandId,
                new { code });
            foreach (var action in result.Actions) {
                if (action.Type == PluginActionTypes.ShowMessage) {
                    AppendOutput(GetPluginArgument(action, "message") ?? "");
                }
            }
        } catch (Exception ex) {
            AppendOutput($"{_pluginName}: {ex.Message}");
        }
    }

    private void AppendOutput(string value) {
        if (_output.Length > 0) {
            _output.AppendLine();
        }
        _output.Append(value);
        OutputTextBox.Text = _output.ToString();
        OutputTextBox.CaretIndex = OutputTextBox.Text.Length;
        OutputTextBox.ScrollToEnd();
    }

    private static string? GetPluginArgument(PluginActionManifest action, string name) {
        if (action.Arguments.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !action.Arguments.TryGetProperty(name, out var value) ||
            value.ValueKind != System.Text.Json.JsonValueKind.String) {
            return null;
        }
        return value.GetString();
    }
}
