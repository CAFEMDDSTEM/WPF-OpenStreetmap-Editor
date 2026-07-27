using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class MapEditServiceTests {
    [Fact]
    public void CreateNewCopies_AssignsLocalIdentityAndOffsetsGeometry() {
        var source = new MapFeature {
            Id = "osm-node-1",
            GeometryType = MapGeometryType.Point,
            Parts = [[new GeoPoint(114.1, 30.2)]],
            Attributes = new Dictionary<string, string> { ["amenity"] = "cafe" },
            Osm = new OsmFeatureMetadata { PrimitiveType = OsmPrimitiveType.Node, Id = 1, Version = 3 },
            IsHidden = true,
            IsSelected = true
        };

        var copy = Assert.Single(MapEditService.CreateNewCopies([source], [source.Id], 0.01, -0.02));

        Assert.NotEqual(source.Id, copy.Id);
        Assert.Null(copy.Osm);
        Assert.False(copy.IsHidden);
        Assert.False(copy.IsSelected);
        Assert.Equal(114.11, copy.Parts[0][0].Longitude, 8);
        Assert.Equal(30.18, copy.Parts[0][0].Latitude, 8);
        Assert.Equal(source.Attributes, copy.Attributes);
        Assert.NotSame(source.Attributes, copy.Attributes);
    }

    [Fact]
    public void AddFeaturesCommand_UndoAndRedoTreatsBatchAsSingleEdit() {
        var document = new MapDocument();
        var existing = new MapFeature {
            Id = "existing",
            GeometryType = MapGeometryType.Point,
            Parts = [[new GeoPoint(1, 1)]]
        };
        var firstCopy = new MapFeature {
            Id = "copy-1",
            GeometryType = MapGeometryType.Point,
            Parts = [[new GeoPoint(2, 2)]]
        };
        var secondCopy = new MapFeature {
            Id = "copy-2",
            GeometryType = MapGeometryType.LineString,
            Parts = [[new GeoPoint(3, 3), new GeoPoint(4, 4)]]
        };
        document.Features.Add(existing);
        document.MarkClean();
        var editor = new EditorSession();
        editor.ReplaceDocument(document);

        Assert.True(editor.Execute(new AddFeaturesCommand([firstCopy, secondCopy])));
        Assert.True(document.IsDirty);
        Assert.Equal(["existing", "copy-1", "copy-2"], document.Features.Select(static feature => feature.Id));

        Assert.True(editor.Undo());
        Assert.False(document.IsDirty);
        Assert.Equal(["existing"], document.Features.Select(static feature => feature.Id));

        Assert.True(editor.Redo());
        Assert.True(document.IsDirty);
        Assert.Equal(["existing", "copy-1", "copy-2"], document.Features.Select(static feature => feature.Id));
    }

    [Fact]
    public void RemoveFeaturesCommand_UndoRestoresOriginalOrder() {
        var first = CreatePoint("first", 1);
        var second = CreatePoint("second", 2);
        var third = CreatePoint("third", 3);
        var document = new MapDocument();
        document.Features.AddRange([first, second, third]);
        document.MarkClean();
        var editor = new EditorSession();
        editor.ReplaceDocument(document);

        Assert.True(editor.Execute(new RemoveFeaturesCommand([first, third])));
        Assert.Equal(["second"], document.Features.Select(static feature => feature.Id));
        Assert.True(document.IsDirty);

        Assert.True(editor.Undo());
        Assert.Equal(["first", "second", "third"], document.Features.Select(static feature => feature.Id));
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void SetFeatureHiddenCommand_UndoRestoresVisibilityWithoutDirtyingDocument() {
        var feature = CreatePoint("feature", 1);
        var document = new MapDocument();
        document.Features.Add(feature);
        document.MarkClean();
        var editor = new EditorSession();
        editor.ReplaceDocument(document);

        Assert.True(editor.Execute(new SetFeatureHiddenCommand([feature], isHidden: true)));
        Assert.True(feature.IsHidden);
        Assert.False(document.IsDirty);

        Assert.True(editor.Undo());
        Assert.False(feature.IsHidden);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void FinishDraftLine_CommitsUndoableLineFeature() {
        var document = new MapDocument();
        document.MarkClean();
        var editor = new EditorSession();
        editor.ReplaceDocument(document);

        Assert.True(editor.AddDraftLinePoint(new GeoPoint(1, 1)));
        Assert.True(editor.AddDraftLinePoint(new GeoPoint(2, 2)));
        Assert.False(document.IsDirty);

        var line = editor.FinishDraftLine();

        Assert.NotNull(line);
        Assert.True(document.IsDirty);
        Assert.Single(document.Features);

        Assert.True(editor.Undo());
        Assert.Empty(document.Features);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void RotateParts_RotatesAroundCenter() {
        var feature = new MapFeature {
            GeometryType = MapGeometryType.LineString,
            Parts = [[new GeoPoint(1, 0), new GeoPoint(0, 1)]]
        };

        var rotated = MapEditService.RotateParts(feature, new GeoPoint(0, 0), 90);

        Assert.Equal(0, rotated[0][0].Longitude, 8);
        Assert.Equal(1, rotated[0][0].Latitude, 8);
        Assert.Equal(-1, rotated[0][1].Longitude, 8);
        Assert.Equal(0, rotated[0][1].Latitude, 8);
    }

    [Fact]
    public void MoveParts_OffsetsGeometry() {
        var feature = new MapFeature {
            GeometryType = MapGeometryType.LineString,
            Parts = [[new GeoPoint(1, 2), new GeoPoint(3, 4)]]
        };

        var moved = MapEditService.MoveParts(feature.Parts, 0.5, -1.5);

        Assert.Equal(1.5, moved[0][0].Longitude, 8);
        Assert.Equal(0.5, moved[0][0].Latitude, 8);
        Assert.Equal(3.5, moved[0][1].Longitude, 8);
        Assert.Equal(2.5, moved[0][1].Latitude, 8);
    }

    [Fact]
    public void OrthogonalizeParts_MakesSkewedPolygonCornersRightAngles() {
        var feature = new MapFeature {
            GeometryType = MapGeometryType.Polygon,
            Parts = [[
                new GeoPoint(0, 0),
                new GeoPoint(4, 0.3),
                new GeoPoint(3.6, 2),
                new GeoPoint(-0.2, 1.7),
                new GeoPoint(0, 0)
            ]]
        };

        var orthogonalized = MapEditService.OrthogonalizeParts(feature.Parts);
        var ring = orthogonalized[0];

        Assert.Equal(ring[0], ring[^1]);
        Assert.False(feature.Parts[0].SequenceEqual(ring));
        for (var i = 0; i < ring.Count - 1; i++) {
            AssertCornerIsRightAngle(ring, i);
        }
    }

    [Fact]
    public void SetFeaturePartsCommand_UndoRestoresOriginalGeometry() {
        var feature = new MapFeature {
            Id = "line",
            GeometryType = MapGeometryType.LineString,
            Parts = [[new GeoPoint(1, 0), new GeoPoint(0, 1)]]
        };
        var document = new MapDocument();
        document.Features.Add(feature);
        document.MarkClean();
        var before = CaptureFeatureParts(feature);
        var after = new FeaturePartsSnapshot(feature, MapEditService.RotateParts(feature, new GeoPoint(0, 0), 90));
        var editor = new EditorSession();
        editor.ReplaceDocument(document);

        Assert.True(editor.Execute(new SetFeaturePartsCommand([before], [after])));
        Assert.True(document.IsDirty);
        Assert.Equal(0, feature.Parts[0][0].Longitude, 8);

        Assert.True(editor.Undo());
        Assert.False(document.IsDirty);
        Assert.Equal(1, feature.Parts[0][0].Longitude, 8);
        Assert.Equal(0, feature.Parts[0][0].Latitude, 8);
    }

    private static MapFeature CreatePoint(string id, double coordinate) {
        return new MapFeature {
            Id = id,
            GeometryType = MapGeometryType.Point,
            Parts = [[new GeoPoint(coordinate, coordinate)]]
        };
    }

    private static FeaturePartsSnapshot CaptureFeatureParts(MapFeature feature) {
        return new FeaturePartsSnapshot(
            feature,
            feature.Parts.Select(static part => (IReadOnlyList<GeoPoint>)part.ToList()).ToList());
    }

    private static void AssertCornerIsRightAngle(IReadOnlyList<GeoPoint> ring, int pointIndex) {
        var vertexCount = ring.Count - 1;
        var previous = ring[(pointIndex + vertexCount - 1) % vertexCount];
        var current = ring[pointIndex];
        var next = ring[(pointIndex + 1) % vertexCount];
        var firstX = previous.Longitude - current.Longitude;
        var firstY = previous.Latitude - current.Latitude;
        var secondX = next.Longitude - current.Longitude;
        var secondY = next.Latitude - current.Latitude;
        var dotProduct = firstX * secondX + firstY * secondY;

        Assert.Equal(0, dotProduct, 10);
    }
}
