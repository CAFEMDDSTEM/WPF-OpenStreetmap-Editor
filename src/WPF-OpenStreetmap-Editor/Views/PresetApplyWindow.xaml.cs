using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public partial class PresetApplyWindow : Window {
    private static LocalizationService L => LocalizationService.Instance;

    private readonly TagPreset _preset;
    private readonly IReadOnlyList<MapFeature> _features;
    private readonly List<(TagPresetField Field, FrameworkElement Editor)> _editors = [];

    public PresetApplyWindow(TagPreset preset, IReadOnlyList<MapFeature> features) {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(features);
        _preset = preset;
        _features = features;

        InitializeComponent();
        ThemeService.ApplyWindowTheme(this);
        PresetNameTextBlock.Text = preset.DisplayName;
        FixedTagsTextBlock.Visibility = preset.Tags.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        FixedTagsTextBlock.Text = preset.Tags.Count == 0
            ? ""
            : L.Format(
                "PresetFields.FixedTagsFormat",
                string.Join("   ", preset.Tags.Select(static tag => $"{tag.Key}={tag.Value}")));
        BuildFields();
    }

    public IReadOnlyDictionary<string, string?> FieldValues { get; private set; } =
        new Dictionary<string, string?>(StringComparer.Ordinal);

    private void BuildFields() {
        foreach (var field in _preset.Fields) {
            var row = new Grid { Margin = new Thickness(0, 0, 8, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock {
                Text = GetFieldLabel(field),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 12, 0),
                Visibility = field.Kind == TagPresetFieldKind.Checkbox ? Visibility.Collapsed : Visibility.Visible
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            var editor = CreateEditor(field);
            editor.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(editor, 1);
            row.Children.Add(editor);
            FieldsPanel.Children.Add(row);
            _editors.Add((field, editor));
        }

        if (_preset.Fields.Any(static field => field.Importance == TagPresetFieldImportance.Recommended)) {
            FieldsPanel.Children.Add(new TextBlock {
                Text = L.GetString("PresetFields.RecommendedHint"),
                Foreground = TryFindResource("Theme.MutedTextBrush") as Brush ?? SystemColors.GrayTextBrush,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            });
        }
    }

    private FrameworkElement CreateEditor(TagPresetField field) {
        var initialValue = GetInitialValue(field.Key);
        switch (field.Kind) {
            case TagPresetFieldKind.Choice:
                var combo = new ComboBox {
                    IsEditable = true,
                    IsTextSearchEnabled = true,
                    MinHeight = 28,
                    DisplayMemberPath = nameof(TagPresetChoice.Label),
                    SelectedValuePath = nameof(TagPresetChoice.Value)
                };
                combo.Items.Add(new TagPresetChoice("", ""));
                foreach (var choice in field.Choices ?? []) {
                    combo.Items.Add(choice);
                }
                if (initialValue is not null) {
                    combo.SelectedValue = initialValue;
                    if (combo.SelectedItem is null) combo.Text = initialValue;
                }
                return combo;

            case TagPresetFieldKind.Checkbox:
                var checkBox = new CheckBox {
                    Content = GetFieldLabel(field),
                    IsThreeState = true,
                    IsChecked = initialValue is null
                        ? null
                        : !IsNegativeValue(initialValue),
                    Tag = initialValue,
                    VerticalAlignment = VerticalAlignment.Center
                };
                return checkBox;

            default:
                return new TextBox {
                    MinHeight = 28,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Text = initialValue ?? ""
                };
        }
    }

    private string? GetInitialValue(string key) {
        var currentValues = _features
            .Select(feature => feature.Attributes.TryGetValue(key, out var value) ? value : null)
            .ToList();
        if (currentValues.Count > 0 && currentValues.All(value => value == currentValues[0])) {
            if (!string.IsNullOrWhiteSpace(currentValues[0])) return currentValues[0];
            if (currentValues[0] is null && _preset.Tags.TryGetValue(key, out var presetValue)) return presetValue;
        }

        return null;
    }

    private static bool IsNegativeValue(string value) {
        return value.Trim().ToLowerInvariant() is "no" or "false" or "0";
    }

    private static string? GetCheckBoxValue(CheckBox checkBox) {
        if (checkBox.IsChecked is not bool isChecked) return null;
        if (checkBox.Tag is string existingValue && IsNegativeValue(existingValue) == !isChecked) {
            return existingValue;
        }

        return isChecked ? "yes" : "no";
    }

    private static string GetFieldLabel(TagPresetField field) {
        var localizationKey = field.Key.ToLowerInvariant() switch {
            "name" => "PresetFields.Field.Name",
            "maxspeed" => "PresetFields.Field.SpeedLimit",
            "lanes" => "PresetFields.Field.Lanes",
            "lanes:forward" => "PresetFields.Field.LanesForward",
            "lanes:backward" => "PresetFields.Field.LanesBackward",
            "ref" => "PresetFields.Field.Reference",
            "bridge" => "PresetFields.Field.Bridge",
            "tunnel" => "PresetFields.Field.Tunnel",
            "ford" => "PresetFields.Field.Ford",
            "embankment" => "PresetFields.Field.Embankment",
            "cutting" => "PresetFields.Field.Cutting",
            "layer" => "PresetFields.Field.Layer",
            "surface" => "PresetFields.Field.Surface",
            "access" => "PresetFields.Field.Access",
            "oneway" => "PresetFields.Field.OneWay",
            "width" => "PresetFields.Field.Width",
            "height" => "PresetFields.Field.Height",
            "building:levels" => "PresetFields.Field.BuildingLevels",
            _ => ""
        };
        var localized = localizationKey.Length == 0 ? "" : L.GetString(localizationKey);
        var text = localized.Length == 0 || localized == localizationKey ? field.Label : localized;
        return field.Importance == TagPresetFieldImportance.Recommended ? $"{text} *" : text;
    }

    private void Apply_Click(object sender, RoutedEventArgs e) {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (field, editor) in _editors) {
            values[field.Key] = editor switch {
                CheckBox checkBox => GetCheckBoxValue(checkBox),
                ComboBox { SelectedItem: TagPresetChoice choice } combo when combo.Text == choice.Label =>
                    choice.Value.Length > 0 ? choice.Value : null,
                ComboBox combo => string.IsNullOrWhiteSpace(combo.Text) ? null : combo.Text.Trim(),
                TextBox textBox => string.IsNullOrWhiteSpace(textBox.Text) ? null : textBox.Text.Trim(),
                _ => null
            };
        }

        FieldValues = values;
        DialogResult = true;
    }
}
