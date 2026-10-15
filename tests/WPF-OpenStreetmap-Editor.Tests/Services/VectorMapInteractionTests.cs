using System.Windows;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class VectorMapInteractionTests {
    [Fact]
    public void DisplayTransform_OffsetsScreenPositionWithoutChangingDocumentPoint() {
        var viewport = new Size(800, 600);
        var documentPoint = new GeoPoint(10, 20);
        var transform = MapDisplayTransform.Create(new MapDisplayAlignmentOptions {
            ProjectionId = ProjectionService.Wgs84Id,
            OffsetX = 1,
            OffsetY = -2
        });

        var screen = VectorMapInteraction.GeoToScreen(documentPoint, 20, 10, 8, viewport, displayTransform: transform);
        var restored = VectorMapInteraction.ScreenToGeo(screen, 20, 10, 8, viewport, displayTransform: transform);

        Assert.Equal(documentPoint.Longitude, restored.Longitude, 8);
        Assert.Equal(documentPoint.Latitude, restored.Latitude, 8);
        Assert.True(screen.X > viewport.Width / 2.0);
        Assert.True(screen.Y > viewport.Height / 2.0);
    }

    [Fact]
    public void HitTest_UsesDisplayTransform() {
        var feature = new MapFeature {
            GeometryType = MapGeometryType.Point,
            Parts = [[new GeoPoint(10, 20)]]
        };
        var transform = MapDisplayTransform.Create(new MapDisplayAlignmentOptions {
            ProjectionId = ProjectionService.Wgs84Id,
            OffsetX = 1
        });
        var viewport = new Size(800, 600);
        var screen = VectorMapInteraction.GeoToScreen(feature.Parts[0][0], 20, 10, 8, viewport, displayTransform: transform);

        var hit = VectorMapInteraction.HitTest([feature], screen, 20, 10, 8, viewport, displayTransform: transform);

        Assert.Same(feature, hit);
    }

    [Fact]
    public void HitTest_PrefersPointOverContainingPolygon() {
        var polygon = new MapFeature {
            GeometryType = MapGeometryType.Polygon,
            Parts = [[
                new GeoPoint(-0.01, -0.01),
                new GeoPoint(0.01, -0.01),
                new GeoPoint(0.01, 0.01),
                new GeoPoint(-0.01, 0.01),
                new GeoPoint(-0.01, -0.01)
            ]]
        };
        var point = new MapFeature {
            GeometryType = MapGeometryType.Point,
            Parts = [[new GeoPoint(0, 0)]]
        };
        var viewport = new Size(800, 600);
        var screen = VectorMapInteraction.GeoToScreen(point.Parts[0][0], 0, 0, 16, viewport);

        var hit = VectorMapInteraction.HitTest([polygon, point], screen, 0, 0, 16, viewport);

        Assert.Same(point, hit);
    }

    [Fact]
    public void HitTest_UsesPanOffset() {
        var feature = new MapFeature {
            GeometryType = MapGeometryType.Point,
            Parts = [[new GeoPoint(10, 20)]]
        };
        var viewport = new Size(800, 600);
        var screen = VectorMapInteraction.GeoToScreen(
            feature.Parts[0][0],
            20,
            10,
            8,
            viewport,
            panOffsetX: 36,
            panOffsetY: -24);

        var hit = VectorMapInteraction.HitTest(
            [feature],
            screen,
            20,
            10,
            8,
            viewport,
            panOffsetX: 36,
            panOffsetY: -24);

        Assert.Same(feature, hit);
    }

    [Fact]
    public void HitTestVertex_ReturnsNearestLineVertex() {
        var feature = new MapFeature {
            GeometryType = MapGeometryType.LineString,
            Parts = [[new GeoPoint(10, 20), new GeoPoint(10.1, 20.1)]]
        };
        var viewport = new Size(800, 600);
        var vertexScreen = VectorMapInteraction.GeoToScreen(feature.Parts[0][1], 20, 10, 12, viewport);

        var hit = VectorMapInteraction.HitTestVertex([feature], vertexScreen, 20, 10, 12, viewport);

        Assert.NotNull(hit);
        Assert.Same(feature, hit.Feature);
        Assert.Equal(0, hit.PartIndex);
        Assert.Equal(1, hit.PointIndex);
    }

    [Fact]
    public void HitTestSegment_ReturnsNearestLineSegment() {
        var feature = new MapFeature {
            GeometryType = MapGeometryType.LineString,
            Parts = [[new GeoPoint(10, 20), new GeoPoint(10.1, 20.1), new GeoPoint(10.2, 20.1)]]
        };
        var viewport = new Size(800, 600);
        var start = VectorMapInteraction.GeoToScreen(feature.Parts[0][1], 20, 10, 12, viewport);
        var end = VectorMapInteraction.GeoToScreen(feature.Parts[0][2], 20, 10, 12, viewport);
        var midpoint = new Point((start.X + end.X) / 2.0, (start.Y + end.Y) / 2.0);

        var hit = VectorMapInteraction.HitTestSegment([feature], midpoint, 20, 10, 12, viewport);

        Assert.NotNull(hit);
        Assert.Same(feature, hit.Feature);
        Assert.Equal(0, hit.PartIndex);
        Assert.Equal(1, hit.StartPointIndex);
        Assert.Equal(2, hit.EndPointIndex);
    }

    [Fact]
    public void FindNear_ReturnsFeatureWithVertexWithinTolerance() {
        var line = CreateHorizontalLine();
        var viewport = new Size(800, 600);
        var vertex = VectorMapInteraction.GeoToScreen(line.Parts[0][1], 20, 10, 12, viewport);

        var near = VectorMapInteraction.FindNear([line], vertex + new Vector(5, 5), 20, 10, 12, viewport);

        Assert.Equal([line], near);
    }

    [Fact]
    public void FindNear_ReturnsFeatureHitOnlyAlongSegment() {
        var line = CreateHorizontalLine();
        var viewport = new Size(800, 600);
        var start = VectorMapInteraction.GeoToScreen(line.Parts[0][0], 20, 10, 12, viewport);
        var end = VectorMapInteraction.GeoToScreen(line.Parts[0][1], 20, 10, 12, viewport);
        var pointer = new Point((start.X + end.X) / 2.0, start.Y + 6);
        Assert.True((pointer - start).Length > 100 && (pointer - end).Length > 100);

        var near = VectorMapInteraction.FindNear([line], pointer, 20, 10, 12, viewport);

        Assert.Equal([line], near);
    }

    [Fact]
    public void FindNear_ReturnsPolygonContainingPointer() {
        var polygon = CreateSquare(0.01);
        var viewport = new Size(800, 600);
        var pointer = VectorMapInteraction.GeoToScreen(new GeoPoint(0, 0), 0, 0, 16, viewport);

        var near = VectorMapInteraction.FindNear([polygon], pointer, 0, 0, 16, viewport);

        Assert.Equal([polygon], near);
    }

    [Fact]
    public void FindNear_IgnoresFeaturesBeyondTolerance() {
        var line = CreateHorizontalLine();
        var viewport = new Size(800, 600);
        var start = VectorMapInteraction.GeoToScreen(line.Parts[0][0], 20, 10, 12, viewport);
        var end = VectorMapInteraction.GeoToScreen(line.Parts[0][1], 20, 10, 12, viewport);
        var pointer = new Point((start.X + end.X) / 2.0, start.Y + 20);

        var near = VectorMapInteraction.FindNear([line], pointer, 20, 10, 12, viewport);

        Assert.Empty(near);
    }

    [Fact]
    public void FindNear_ExcludesHiddenFeatures() {
        var line = CreateHorizontalLine();
        line.IsHidden = true;
        var viewport = new Size(800, 600);
        var vertex = VectorMapInteraction.GeoToScreen(line.Parts[0][0], 20, 10, 12, viewport);

        var near = VectorMapInteraction.FindNear([line], vertex, 20, 10, 12, viewport);

        Assert.Empty(near);
    }

    [Fact]
    public void FindNear_ReturnsFeatureOnceInInputOrderWhenHitAtSeveralPlaces() {
        // The square is small enough that every ring segment and its interior are all within tolerance.
        var tinySquare = CreateSquare(0.00005);
        var line = new MapFeature {
            GeometryType = MapGeometryType.LineString,
            Parts = [[new GeoPoint(-0.01, 0), new GeoPoint(0.01, 0)]]
        };
        var viewport = new Size(800, 600);
        var pointer = VectorMapInteraction.GeoToScreen(new GeoPoint(0, 0), 0, 0, 16, viewport);

        var near = VectorMapInteraction.FindNear([tinySquare, line], pointer, 0, 0, 16, viewport);

        Assert.Equal([tinySquare, line], near);
    }

    [Fact]
    public void ScreenRectToGeoBounds_NormalizesDraggedCorners() {
        var bounds = VectorMapInteraction.ScreenRectToGeoBounds(
            new Rect(new Point(600, 450), new Point(200, 150)),
            0,
            0,
            10,
            new Size(800, 600));

        Assert.True(bounds.IsValid);
        Assert.True(bounds.MinLongitude < bounds.MaxLongitude);
        Assert.True(bounds.MinLatitude < bounds.MaxLatitude);
    }

    [Fact]
    public void GetCenterAfterPan_MovesMapOppositeToDrag() {
        var center = VectorMapInteraction.GetCenterAfterPan(
            new GeoPoint(0, 0),
            new Vector(256, 0),
            2);

        Assert.Equal(-90, center.Longitude, 8);
        Assert.Equal(0, center.Latitude, 8);
    }

    [Fact]
    public void GetCenterAfterZoom_PreservesCoordinateUnderAnchor() {
        var viewport = new Size(900, 600);
        var anchor = new Point(700, 180);
        var center = new GeoPoint(114.3, 30.6);
        var before = VectorMapInteraction.ScreenToGeo(
            anchor,
            center.Latitude,
            center.Longitude,
            10,
            viewport);

        var zoomedCenter = VectorMapInteraction.GetCenterAfterZoom(center, 10, 13, anchor, viewport);
        var after = VectorMapInteraction.ScreenToGeo(
            anchor,
            zoomedCenter.Latitude,
            zoomedCenter.Longitude,
            13,
            viewport);

        Assert.Equal(before.Longitude, after.Longitude, 8);
        Assert.Equal(before.Latitude, after.Latitude, 8);
    }

    /// <summary>A line along latitude 20 so its screen segment is exactly horizontal and about 290 px long at zoom 12.</summary>
    private static MapFeature CreateHorizontalLine() {
        return new MapFeature {
            GeometryType = MapGeometryType.LineString,
            Parts = [[new GeoPoint(10, 20), new GeoPoint(10.1, 20)]]
        };
    }

    private static MapFeature CreateSquare(double halfSize) {
        return new MapFeature {
            GeometryType = MapGeometryType.Polygon,
            Parts = [[
                new GeoPoint(-halfSize, -halfSize),
                new GeoPoint(halfSize, -halfSize),
                new GeoPoint(halfSize, halfSize),
                new GeoPoint(-halfSize, halfSize),
                new GeoPoint(-halfSize, -halfSize)
            ]]
        };
    }
}
