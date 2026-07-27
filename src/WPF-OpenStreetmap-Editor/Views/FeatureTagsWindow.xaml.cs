using System.Globalization;
using System.IO;
using System.Windows;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public partial class FeatureTagsWindow : Window {
    private readonly MapFeature _feature;
    private static LocalizationService L => LocalizationService.Instance;

    public FeatureTagsWindow(MapFeature feature) {
        InitializeComponent();
        _feature = feature;
        FeatureTextBlock.Text = L.Format("FeatureTags.FeatureFormat", feature.Id, feature.GeometryType);
        TagsTextBox.Text = FormatTags(feature.Attributes);
    }

    public IReadOnlyDictionary<string, string> Tags { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private void Save_Click(object sender, RoutedEventArgs e) {
        try {
            Tags = ParseTags(TagsTextBox.Text);
            DialogResult = true;
        } catch (Exception ex) {
            MessageBox.Show(ex.Message, L.GetString("FeatureTags.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string FormatTags(IReadOnlyDictionary<string, string> tags) {
        return string.Join(Environment.NewLine, tags
            .OrderBy(static tag => tag.Key, StringComparer.Ordinal)
            .Select(static tag => string.Create(
                CultureInfo.InvariantCulture,
                $"{tag.Key}={tag.Value}")));
    }

    private static Dictionary<string, string> ParseTags(string text) {
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.TrimEntries);
        for (var i = 0; i < lines.Length; i++) {
            var line = lines[i];
            if (line.Length == 0) continue;

            var separator = line.IndexOf('=');
            if (separator <= 0) {
                throw new InvalidDataException(L.Format("FeatureTags.LineRequiresKeyValue", i + 1));
            }

            var key = line[..separator].Trim();
            if (key.Length == 0) {
                throw new InvalidDataException(L.Format("FeatureTags.EmptyKey", i + 1));
            }

            tags[key] = line[(separator + 1)..].Trim();
        }

        return tags;
    }
}
