using System.Collections.Generic;
using System.Linq;

namespace WPF_OpenStreetmap_Editor.Services;

public static class LayerRenderPlanner {
    public static IReadOnlyList<MapImageLayer> GetRasterCandidates(IEnumerable<MapImageLayer> layers) {
        return layers
            .Where(static layer => layer.IsVisible && layer.Opacity > 0 && layer.Kind == MapLayerKind.Raster)
            .ToList();
    }

    public static IReadOnlyList<MapImageLayer> GetLayersToRender(IEnumerable<MapImageLayer> layers) {
        List<MapImageLayer> renderLayers = [];
        foreach (var layer in GetRasterCandidates(layers)) {
            renderLayers.Add(layer);
            if (!AllowsLowerLayers(layer)) {
                break;
            }
        }

        return renderLayers;
    }

    public static MapImageLayer? GetTopRasterLayerToRender(IEnumerable<MapImageLayer> layers) {
        return GetLayersToRender(layers).FirstOrDefault();
    }

    public static bool AllowsLowerLayers(MapImageLayer layer) {
        return layer.Kind == MapLayerKind.Data ||
            layer.HasTransparency ||
            layer.Opacity < 1.0;
    }
}
