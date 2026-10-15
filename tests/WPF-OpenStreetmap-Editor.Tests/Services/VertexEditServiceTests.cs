using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class VertexEditServiceTests {
    private static readonly GeoPoint A = new(0, 0);
    private static readonly GeoPoint B = new(1, 0);
    private static readonly GeoPoint C = new(1, 1);
    private static readonly GeoPoint D = new(0, 1);
    private static readonly GeoPoint Inserted = new(0.5, 0.5);

    [Fact]
    public void InsertVertex_InsertsAfterSegmentStartInOpenLine() {
        var parts = CreateParts([A, B, C]);

        var result = VertexEditService.InsertVertex(parts, 0, 1, Inserted);

        Assert.Equal([A, B, Inserted, C], Assert.Single(result));
    }

    [Fact]
    public void InsertVertex_InsertsBeforeClosingVertexOfRing() {
        var parts = CreateParts([A, B, C, D, A]);

        var result = VertexEditService.InsertVertex(parts, 0, 3, Inserted);

        Assert.Equal([A, B, C, D, Inserted, A], Assert.Single(result));
    }

    [Fact]
    public void InsertVertex_InvalidIndexesReturnUnchangedCopy() {
        var parts = CreateParts([A, B, C]);

        var invalidSegment = VertexEditService.InsertVertex(parts, 0, 2, Inserted);
        var invalidPart = VertexEditService.InsertVertex(parts, 1, 0, Inserted);

        Assert.Equal([A, B, C], Assert.Single(invalidSegment));
        Assert.Equal([A, B, C], Assert.Single(invalidPart));
        Assert.NotSame(parts[0], invalidSegment[0]);
    }

    [Fact]
    public void RemoveVertex_RemovesInteriorVertex() {
        var parts = CreateParts([A, B, C, D, A]);

        var result = VertexEditService.RemoveVertex(parts, 0, 2);

        Assert.NotNull(result);
        Assert.Equal([A, B, D, A], Assert.Single(result));
    }

    [Fact]
    public void RemoveVertex_RemovesInteriorVertexOfOpenLine() {
        var parts = CreateParts([A, B, C]);

        var result = VertexEditService.RemoveVertex(parts, 0, 1);

        Assert.NotNull(result);
        Assert.Equal([A, C], Assert.Single(result));
    }

    [Fact]
    public void RemoveVertex_RemovingFirstVertexReclosesRingWithNewFirstVertex() {
        var parts = CreateParts([A, B, C, D, A]);

        var result = VertexEditService.RemoveVertex(parts, 0, 0);

        Assert.NotNull(result);
        Assert.Equal([B, C, D, B], Assert.Single(result));
    }

    [Fact]
    public void RemoveVertex_RemovingClosingVertexBehavesLikeFirstVertex() {
        var parts = CreateParts([A, B, C, D, A]);

        var result = VertexEditService.RemoveVertex(parts, 0, 4);

        Assert.NotNull(result);
        Assert.Equal([B, C, D, B], Assert.Single(result));
    }

    [Fact]
    public void RemoveVertex_ReturnsNullWhenLineWouldDegenerate() {
        Assert.Null(VertexEditService.RemoveVertex(CreateParts([A, B]), 0, 0));
        Assert.Null(VertexEditService.RemoveVertex(CreateParts([A, B]), 0, 1));
    }

    [Fact]
    public void RemoveVertex_ReturnsNullWhenRingWouldDegenerate() {
        Assert.Null(VertexEditService.RemoveVertex(CreateParts([A, B, C, A]), 0, 1));
        Assert.Null(VertexEditService.RemoveVertex(CreateParts([A, B, C, A]), 0, 0));
    }

    [Fact]
    public void RemoveVertex_ReturnsNullForInvalidIndexes() {
        var parts = CreateParts([A, B, C]);

        Assert.Null(VertexEditService.RemoveVertex(parts, 0, 3));
        Assert.Null(VertexEditService.RemoveVertex(parts, 0, -1));
        Assert.Null(VertexEditService.RemoveVertex(parts, 1, 0));
    }

    [Fact]
    public void CloseRing_AppendsFirstVertexToOpenPart() {
        var parts = CreateParts([A, B, C]);

        var result = VertexEditService.CloseRing(parts, 0);

        Assert.Equal([A, B, C, A], Assert.Single(result));
    }

    [Fact]
    public void CloseRing_LeavesClosedRingUnchanged() {
        var parts = CreateParts([A, B, C, A]);

        var result = VertexEditService.CloseRing(parts, 0);

        Assert.Equal([A, B, C, A], Assert.Single(result));
    }

    [Fact]
    public void CloseRing_LeavesShortPartUnchanged() {
        var parts = CreateParts([A, B]);

        var result = VertexEditService.CloseRing(parts, 0);

        Assert.Equal([A, B], Assert.Single(result));
    }

    [Fact]
    public void IsClosedRing_RequiresMatchingEndpointsAndAtLeastThreePoints() {
        Assert.True(VertexEditService.IsClosedRing([A, B, C, A]));
        Assert.False(VertexEditService.IsClosedRing([A, B, C]));
        Assert.False(VertexEditService.IsClosedRing([A, A]));
        Assert.False(VertexEditService.IsClosedRing([]));
    }

    [Fact]
    public void Operations_DoNotMutateInputParts() {
        var parts = CreateParts([A, B, C, D, A]);
        parts.Add([B, C]);

        var inserted = VertexEditService.InsertVertex(parts, 0, 0, Inserted);
        var removed = VertexEditService.RemoveVertex(parts, 0, 0);
        var closed = VertexEditService.CloseRing(parts, 1);

        Assert.Equal([A, B, C, D, A], parts[0]);
        Assert.Equal([B, C], parts[1]);
        Assert.NotNull(removed);
        Assert.NotSame(parts[0], inserted[0]);
        Assert.NotSame(parts[1], inserted[1]);
        Assert.NotSame(parts[0], removed[0]);
        Assert.NotSame(parts[1], closed[1]);
    }

    private static List<List<GeoPoint>> CreateParts(List<GeoPoint> part) {
        return [part];
    }
}
