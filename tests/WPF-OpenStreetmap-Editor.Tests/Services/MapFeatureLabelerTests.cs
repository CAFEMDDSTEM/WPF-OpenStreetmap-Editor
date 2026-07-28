using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class MapFeatureLabelerTests {
    [Fact]
    public void GetLabels_ReturnsFeatureName() {
        var feature = Feature(MapGeometryType.Point, ("name", "Central Station"));

        var labels = MapFeatureLabeler.GetLabels(feature);

        var label = Assert.Single(labels);
        Assert.Equal("Central Station", label.Text);
        Assert.Equal(MapFeatureLabelKind.Name, label.Kind);
    }

    [Fact]
    public void GetLabels_ReturnsRoadNameAndRef() {
        var feature = Feature(
            MapGeometryType.LineString,
            ("highway", "primary"),
            ("name", "People Road"),
            ("ref", "G105"));

        var labels = MapFeatureLabeler.GetLabels(feature);

        Assert.Collection(
            labels,
            label => {
                Assert.Equal("People Road", label.Text);
                Assert.Equal(MapFeatureLabelKind.Name, label.Kind);
            },
            label => {
                Assert.Equal("G105", label.Text);
                Assert.Equal(MapFeatureLabelKind.Ref, label.Kind);
            });
    }

    [Fact]
    public void GetLabels_UsesRoadRefWhenNameIsMissing() {
        var feature = Feature(
            MapGeometryType.LineString,
            ("highway", "secondary"),
            ("network", "CN:provincial"),
            ("ref", "S226"));

        var label = Assert.Single(MapFeatureLabeler.GetLabels(feature));

        Assert.Equal("S226", label.Text);
        Assert.Equal(MapFeatureLabelKind.Ref, label.Kind);
    }

    [Fact]
    public void GetLabels_MatchesTagKeysIgnoringCase() {
        var feature = Feature(MapGeometryType.Point, ("Name", "Case Safe"));

        var label = Assert.Single(MapFeatureLabeler.GetLabels(feature));

        Assert.Equal("Case Safe", label.Text);
    }

    [Fact]
    public void GetLabels_UsesLocalizedNameWhenPlainNameIsMissing() {
        var feature = Feature(MapGeometryType.Point, ("name:zh", "人民公园"));

        var label = Assert.Single(MapFeatureLabeler.GetLabels(feature));

        Assert.Equal("人民公园", label.Text);
        Assert.Equal(MapFeatureLabelKind.Name, label.Kind);
    }

    [Theory]
    [InlineData("ref:CN:national", "G105")]
    [InlineData("ref:CN:provincial", "S226")]
    [InlineData("ref:CN:county", "X001")]
    [InlineData("unsigned_ref", "Y012")]
    public void GetLabels_UsesExtendedRoadRefs(string key, string value) {
        var feature = Feature(MapGeometryType.LineString, ("highway", "secondary"), (key, value));

        var label = Assert.Single(MapFeatureLabeler.GetLabels(feature));

        Assert.Equal(value, label.Text);
        Assert.Equal(MapFeatureLabelKind.Ref, label.Kind);
    }

    private static MapFeature Feature(MapGeometryType geometryType, params (string Key, string Value)[] tags) {
        var feature = new MapFeature {
            GeometryType = geometryType,
            Parts = [[new GeoPoint(0, 0), new GeoPoint(1, 0), new GeoPoint(1, 1), new GeoPoint(0, 0)]]
        };
        foreach (var (key, value) in tags) {
            feature.Attributes[key] = value;
        }

        return feature;
    }
}
