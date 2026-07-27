using System.Windows;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class VectorMapInteractionTests {
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
}
