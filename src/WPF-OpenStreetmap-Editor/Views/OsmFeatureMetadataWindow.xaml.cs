using System.Globalization;
using System.IO;
using System.Windows;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Views;

public partial class OsmFeatureMetadataWindow : Window {
    private static readonly IReadOnlyList<OsmPrimitiveType> PrimitiveTypes = [
        OsmPrimitiveType.Node,
        OsmPrimitiveType.Way,
        OsmPrimitiveType.Relation
    ];

    private readonly MapFeature _feature;

    public OsmFeatureMetadataWindow(MapFeature feature) {
        InitializeComponent();
        _feature = feature;
        Metadata = feature.Osm?.Clone();
        PrimitiveTypeComboBox.ItemsSource = PrimitiveTypes;
        FeatureTextBlock.Text = $"要素：{feature.Id}    几何：{feature.GeometryType}";
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
                throw new InvalidDataException("OSM ID 必须是正整数。");
            }
            if (!int.TryParse(VersionTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) || version <= 0) {
                throw new InvalidDataException("版本号必须是正整数。");
            }

            Metadata = new OsmFeatureMetadata {
                PrimitiveType = primitiveType,
                Id = id,
                Version = version,
                NodeReferences = primitiveType == OsmPrimitiveType.Way ? ParseNodeReferences() : []
            };
            DialogResult = true;
        } catch (Exception ex) {
            MessageBox.Show(ex.Message, "编辑原始 OSM 元数据", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            throw new InvalidDataException($"节点引用数量必须与当前几何点数量一致（当前 {points.Count:N0} 个点）。");
        }

        var references = new List<OsmNodeReference>(lines.Length);
        for (var i = 0; i < lines.Length; i++) {
            var parts = lines[i].Split(':', StringSplitOptions.TrimEntries);
            if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var nodeId) || nodeId <= 0) {
                throw new InvalidDataException($"第 {i + 1:N0} 行节点 ID 无效。");
            }
            var version = 1;
            if (parts.Length > 1 &&
                (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out version) || version <= 0)) {
                throw new InvalidDataException($"第 {i + 1:N0} 行节点版本号无效。");
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
