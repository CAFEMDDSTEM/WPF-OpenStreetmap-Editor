using System.Globalization;
using System.IO;
using System.Windows;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public partial class OsmFeatureMetadataWindow : Window {
    private static readonly IReadOnlyList<OsmPrimitiveType> PrimitiveTypes = [
        OsmPrimitiveType.Node,
        OsmPrimitiveType.Way,
        OsmPrimitiveType.Relation
    ];

    private readonly MapFeature _feature;
    private static LocalizationService L => LocalizationService.Instance;

    public OsmFeatureMetadataWindow(MapFeature feature) {
        InitializeComponent();
        _feature = feature;
        Metadata = feature.Osm?.Clone();
        PrimitiveTypeComboBox.ItemsSource = PrimitiveTypes;
        FeatureTextBlock.Text = L.Format("Osm.Metadata.FeatureFormat", feature.Id, feature.GeometryType);
        PrimitiveTypeComboBox.SelectedItem = feature.Osm?.PrimitiveType ?? GetDefaultPrimitiveType(feature);
        OsmIdTextBox.Text = feature.Osm?.Id > 0 ? feature.Osm.Id.ToString(CultureInfo.InvariantCulture) : "";
        VersionTextBox.Text = feature.Osm?.Version > 0 ? feature.Osm.Version.ToString(CultureInfo.InvariantCulture) : "1";
        NodeReferencesTextBox.Text = FormatNodeReferences(feature.Osm?.NodeReferences);
    }

    public OsmFeatureMetadata? Metadata { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e) {
        try {
            var primitiveType = PrimitiveTypeComboBox.SelectedItem is OsmPrimitiveType selected
                ? selected
                : GetDefaultPrimitiveType(_feature);
            if (!long.TryParse(OsmIdTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0) {
                throw new InvalidDataException(L.GetString("Osm.Metadata.IdRequired"));
            }
            if (!int.TryParse(VersionTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) || version <= 0) {
                throw new InvalidDataException(L.GetString("Osm.Metadata.VersionRequired"));
            }

            Metadata = new OsmFeatureMetadata {
                PrimitiveType = primitiveType,
                Id = id,
                Version = version,
                NodeReferences = primitiveType == OsmPrimitiveType.Way ? ParseNodeReferences() : []
            };
            DialogResult = true;
        } catch (Exception ex) {
            MessageBox.Show(ex.Message, L.GetString("Osm.Metadata.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e) {
        Metadata = null;
        DialogResult = true;
    }

    private List<OsmNodeReference> ParseNodeReferences() {
        var lines = NodeReferencesTextBox.Text
            .Split(["\r\n", "\n"], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return [];

        var points = _feature.Parts.Count == 1 ? _feature.Parts[0] : [];
        if (points.Count != lines.Length) {
            throw new InvalidDataException(L.Format("Osm.Metadata.NodeReferenceCount", points.Count));
        }

        var references = new List<OsmNodeReference>(lines.Length);
        for (var i = 0; i < lines.Length; i++) {
            var parts = lines[i].Split(':', StringSplitOptions.TrimEntries);
            if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var nodeId) || nodeId <= 0) {
                throw new InvalidDataException(L.Format("Osm.Metadata.NodeIdInvalid", i + 1));
            }
            var version = 1;
            if (parts.Length > 1 &&
                (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out version) || version <= 0)) {
                throw new InvalidDataException(L.Format("Osm.Metadata.NodeVersionInvalid", i + 1));
            }
            references.Add(new OsmNodeReference(nodeId, version, points[i]));
        }

        return references;
    }

    private static string FormatNodeReferences(IReadOnlyList<OsmNodeReference>? references) {
        if (references is null || references.Count == 0) return "";
        return string.Join(Environment.NewLine, references.Select(static reference =>
            $"{reference.Id.ToString(CultureInfo.InvariantCulture)}:{reference.Version.ToString(CultureInfo.InvariantCulture)}"));
    }

    private static OsmPrimitiveType GetDefaultPrimitiveType(MapFeature feature) {
        return feature.GeometryType == MapGeometryType.Point ? OsmPrimitiveType.Node : OsmPrimitiveType.Way;
    }
}
