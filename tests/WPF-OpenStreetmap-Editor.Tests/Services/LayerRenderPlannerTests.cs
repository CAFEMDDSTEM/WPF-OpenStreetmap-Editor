using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class LayerRenderPlannerTests {
    [Fact]
    public void GetRasterCandidates_IncludesLowerOpaqueLayerForAvailabilityFallback() {
        var top = Raster("top");
        var bottom = Raster("bottom");

        var layers = LayerRenderPlanner.GetRasterCandidates([top, bottom]);

        Assert.Equal([top, bottom], layers);
    }

    [Fact]
    public void GetLayersToRender_StopsBelowOpaqueRasterLayer() {
        var top = Raster("top");
        var bottom = Raster("bottom");

        var layers = LayerRenderPlanner.GetLayersToRender([top, bottom]);

        Assert.Equal([top], layers);
    }

    [Fact]
    public void GetLayersToRender_SkipsHiddenLayerAndRendersNextLayer() {
        var hiddenTop = Raster("top");
        hiddenTop.IsVisible = false;
        var bottom = Raster("bottom");

        var layers = LayerRenderPlanner.GetLayersToRender([hiddenTop, bottom]);

        Assert.Equal([bottom], layers);
    }

    [Fact]
    public void GetLayersToRender_DataLayerAllowsLowerRasterLayer() {
        var data = new MapImageLayer {
            Name = "roads.osm",
            Kind = MapLayerKind.Data,
            IsVisible = true
        };
        var bottom = Raster("bottom");

        var layers = LayerRenderPlanner.GetLayersToRender([data, bottom]);

        Assert.Equal([bottom], layers);
    }

    [Fact]
    public void GetLayersToRender_TransparentRasterAllowsLowerLayer() {
        var overlay = Raster("overlay");
        overlay.HasTransparency = true;
        var bottom = Raster("bottom");

        var layers = LayerRenderPlanner.GetLayersToRender([overlay, bottom]);

        Assert.Equal([overlay, bottom], layers);
    }

    [Fact]
    public void GetLayersToRender_SemiTransparentRasterAllowsLowerLayer() {
        var overlay = Raster("overlay");
        overlay.Opacity = 0.5;
        var bottom = Raster("bottom");

        var layers = LayerRenderPlanner.GetLayersToRender([overlay, bottom]);

        Assert.Equal([overlay, bottom], layers);
    }

    [Fact]
    public void GetLayersToRender_SkipsFullyTransparentLayer() {
        var transparent = Raster("transparent");
        transparent.Opacity = 0;
        var bottom = Raster("bottom");

        var layers = LayerRenderPlanner.GetLayersToRender([transparent, bottom]);

        Assert.Equal([bottom], layers);
    }

    private static MapImageLayer Raster(string name) {
        return new MapImageLayer {
            Name = name,
            SourceName = name,
            Source = $"xyz:https://tiles.example.com/{name}/{{z}}/{{x}}/{{y}}.png",
            Kind = MapLayerKind.Raster,
            IsVisible = true,
            Opacity = 1.0
        };
    }
}
