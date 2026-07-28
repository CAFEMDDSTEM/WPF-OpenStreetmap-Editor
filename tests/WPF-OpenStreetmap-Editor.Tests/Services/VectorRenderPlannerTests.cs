using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class VectorRenderPlannerTests {
    [Fact]
    public void Create_CullsHiddenOutsideAndOverBudgetFeatures() {
        var visible = PointFeature("visible", 1, 1);
        var hidden = PointFeature("hidden", 1, 1);
        hidden.IsHidden = true;
        var outside = PointFeature("outside", 20, 20);
        var overBudget = PointFeature("budget", 2, 2);

        var plan = VectorRenderPlanner.Create(
            [visible, hidden, outside, overBudget],
            new GeoBounds(0, 0, 10, 10),
            featureBudget: 1,
            coordinateBudget: 100);

        Assert.Same(visible, Assert.Single(plan.Features));
        Assert.Equal(1, plan.HiddenCount);
        Assert.Equal(1, plan.OutsideViewportCount);
        Assert.Equal(1, plan.BudgetOmittedCount);
    }

    [Fact]
    public void Create_FromDocumentUsesSpatialIndexForViewportFeatures() {
        var document = new MapDocument();
        var visible = PointFeature("visible", 1, 1);
        var outside = PointFeature("outside", 20, 20);
        document.Features.Add(visible);
        document.Features.Add(outside);

        var plan = VectorRenderPlanner.Create(document, new GeoBounds(0, 0, 2, 2));

        Assert.Same(visible, Assert.Single(plan.Features));
    }

    [Fact]
    public void Create_FromDocumentKeepsLargeFeaturesThatIntersectViewport() {
        var document = new MapDocument();
        var longRoad = new MapFeature {
            Id = "long-road",
            GeometryType = MapGeometryType.LineString,
            Parts = [[new GeoPoint(-10, 1), new GeoPoint(10, 1)]]
        };
        document.Features.Add(longRoad);

        var plan = VectorRenderPlanner.Create(document, new GeoBounds(0, 0, 2, 2));

        Assert.Same(longRoad, Assert.Single(plan.Features));
    }

    [Fact]
    public void Create_FromDocumentRendersFeaturesFromSeparateVisibleDataLayers() {
        var document = new MapDocument();
        var baseFeature = PointFeature("base", 1, 1);
        var overlayFeature = PointFeature("overlay", 1.5, 1.5);
        document.Features.Add(baseFeature);
        var overlay = new MapDataLayer { Name = "overlay.geojson" };
        overlay.Features.Add(overlayFeature);
        document.AddDataLayer(overlay);

        var plan = VectorRenderPlanner.Create(document, new GeoBounds(0, 0, 2, 2));

        Assert.Equal([baseFeature, overlayFeature], plan.Features);
    }

    [Fact]
    public void Create_FromDocumentSkipsHiddenDataLayers() {
        var document = new MapDocument();
        var baseFeature = PointFeature("base", 1, 1);
        var hiddenFeature = PointFeature("hidden-layer", 1.5, 1.5);
        document.Features.Add(baseFeature);
        var hiddenLayer = new MapDataLayer {
            Name = "hidden.geojson",
            IsVisible = false
        };
        hiddenLayer.Features.Add(hiddenFeature);
        document.AddDataLayer(hiddenLayer);

        var plan = VectorRenderPlanner.Create(document, new GeoBounds(0, 0, 2, 2));

        Assert.Same(baseFeature, Assert.Single(plan.Features));
    }

    [Fact]
    public void SpatialIndex_UsesFinerCellsForDenseLocalData() {
        var features = new List<MapFeature>();
        for (var y = 0; y < 64; y++) {
            for (var x = 0; x < 64; x++) {
                features.Add(PointFeature($"p-{x}-{y}", x * 0.001, y * 0.001));
            }
        }

        var index = MapFeatureSpatialIndex.Build(features);

        Assert.True(index.CellSizeDegrees < 0.05);
        Assert.True(index.CellCount > 1);
        Assert.Equal(4096, index.FeatureCount);
    }

    [Fact]
    public void SpatialIndex_QueryCullsDenseLocalDataToViewport() {
        var features = new List<MapFeature>();
        for (var y = 0; y < 100; y++) {
            for (var x = 0; x < 100; x++) {
                features.Add(PointFeature($"p-{x}-{y}", x * 0.001, y * 0.001));
            }
        }

        var index = MapFeatureSpatialIndex.Build(features);
        var visible = index.Query(new GeoBounds(0, 0, 0.0091, 0.0091)).ToList();

        Assert.Equal(100, visible.Count);
    }

    [Fact]
    public void SpatialIndex_QueryDoesNotDuplicateFeaturesThatSpanCells() {
        var feature = new MapFeature {
            Id = "multi-cell",
            GeometryType = MapGeometryType.LineString,
            Parts = [[new GeoPoint(0, 0), new GeoPoint(0.01, 0.01)]]
        };
        var index = MapFeatureSpatialIndex.Build([feature]);

        var visible = index.Query(new GeoBounds(0, 0, 0.01, 0.01)).ToList();

        Assert.Same(feature, Assert.Single(visible));
    }

    [Fact]
    public void GetFitZoom_ReturnsZoomWhoseExtentFitsViewport() {
        var bounds = new GeoBounds(103.8, 1.3, 103.9, 1.4);

        var zoom = VectorMapInteraction.GetFitZoom(bounds, new System.Windows.Size(800, 600), 20);

        Assert.InRange(zoom, 10, 14);
    }

    private static MapFeature PointFeature(string id, double longitude, double latitude) {
        return new MapFeature {
            Id = id,
            GeometryType = MapGeometryType.Point,
            Parts = [[new GeoPoint(longitude, latitude)]]
        };
    }
}
