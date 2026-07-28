using System.IO;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public static class MapDataLayerSelectionService {
    public static MapDataLayer? SelectPrimaryDataLayer(
        MapDocument document,
        IEnumerable<MapImageLayer> mapLayers) {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(mapLayers);

        var layerList = mapLayers.ToList();
        var primaryLayer = layerList.FirstOrDefault(static layer =>
            layer.Kind == MapLayerKind.Data && layer.IsPrimary) ??
            layerList.FirstOrDefault(static layer => layer.Kind == MapLayerKind.Data);

        var dataLayer = primaryLayer is null
            ? document.DataLayers.FirstOrDefault()
            : FindBySourcePath(document.DataLayers, primaryLayer.DataPath) ??
                FindByName(document.DataLayers, primaryLayer.Name) ??
                document.DataLayers.FirstOrDefault();
        if (dataLayer is null) return null;

        document.ActiveDataLayer = dataLayer;
        return dataLayer;
    }

    public static MapImageLayer? FindMapLayer(
        MapDataLayer dataLayer,
        IEnumerable<MapImageLayer> mapLayers) {
        ArgumentNullException.ThrowIfNull(dataLayer);
        ArgumentNullException.ThrowIfNull(mapLayers);

        var layerList = mapLayers.Where(static layer => layer.Kind == MapLayerKind.Data).ToList();
        return layerList.FirstOrDefault(layer => PathsEqual(dataLayer.SourcePath, layer.DataPath)) ??
            layerList.FirstOrDefault(layer => NamesEqual(dataLayer.Name, layer.Name)) ??
            layerList.FirstOrDefault();
    }

    private static MapDataLayer? FindBySourcePath(
        IEnumerable<MapDataLayer> dataLayers,
        string sourcePath) {
        if (string.IsNullOrWhiteSpace(sourcePath)) return null;

        return dataLayers.FirstOrDefault(layer => PathsEqual(layer.SourcePath, sourcePath));
    }

    private static MapDataLayer? FindByName(
        IEnumerable<MapDataLayer> dataLayers,
        string name) {
        if (string.IsNullOrWhiteSpace(name)) return null;

        return dataLayers.FirstOrDefault(layer => NamesEqual(layer.Name, name));
    }

    private static bool NamesEqual(string? left, string? right) {
        return !string.IsNullOrWhiteSpace(left) &&
            !string.IsNullOrWhiteSpace(right) &&
            string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string? left, string? right) {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;

        try {
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
        } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
            return false;
        }
    }
}
