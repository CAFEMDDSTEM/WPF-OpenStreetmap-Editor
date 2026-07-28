using WPF_OpenStreetmap_Editor.Controls;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Tests.Controls;

public class VectorLayerElementTests {
    [Fact]
    public void RequiresDrawingRefreshCore_TreatsSmallCoordinateChangeAsNewView() {
        var document = new MapDocument();

        var refresh = VectorLayerElement.RequiresDrawingRefreshCore(
            document,
            document,
            currentCenterLatitude: 24.8784031,
            nextCenterLatitude: 24.8785031,
            currentCenterLongitude: 113.62372195,
            nextCenterLongitude: 113.62382195,
            currentZoom: 17,
            nextZoom: 17,
            lastRenderedWidth: 1200,
            actualWidth: 1200,
            lastRenderedHeight: 800,
            actualHeight: 800,
            drawPanOffsetX: 0,
            panOffsetX: 0,
            drawPanOffsetY: 0,
            panOffsetY: 0);

        Assert.True(refresh);
    }

    [Fact]
    public void RequiresDrawingRefreshCore_AllowsCachedDrawingForSmallPan() {
        var document = new MapDocument();

        var refresh = VectorLayerElement.RequiresDrawingRefreshCore(
            document,
            document,
            currentCenterLatitude: 24.8784031,
            nextCenterLatitude: 24.8784031,
            currentCenterLongitude: 113.62372195,
            nextCenterLongitude: 113.62372195,
            currentZoom: 17,
            nextZoom: 17,
            lastRenderedWidth: 1200,
            actualWidth: 1200,
            lastRenderedHeight: 800,
            actualHeight: 800,
            drawPanOffsetX: 0,
            panOffsetX: 120,
            drawPanOffsetY: 0,
            panOffsetY: 120);

        Assert.False(refresh);
    }

    [Fact]
    public void RequiresDrawingRefreshCore_TreatsDocumentRevisionChangeAsNewContent() {
        var document = new MapDocument();

        var refresh = VectorLayerElement.RequiresDrawingRefreshCore(
            document,
            document,
            currentCenterLatitude: 24.8784031,
            nextCenterLatitude: 24.8784031,
            currentCenterLongitude: 113.62372195,
            nextCenterLongitude: 113.62372195,
            currentZoom: 17,
            nextZoom: 17,
            lastRenderedWidth: 1200,
            actualWidth: 1200,
            lastRenderedHeight: 800,
            actualHeight: 800,
            drawPanOffsetX: 0,
            panOffsetX: 0,
            drawPanOffsetY: 0,
            panOffsetY: 0,
            currentDocumentRevision: 1,
            nextDocumentRevision: 2,
            currentVisualStateRevision: 0,
            nextVisualStateRevision: 0);

        Assert.True(refresh);
    }
}
