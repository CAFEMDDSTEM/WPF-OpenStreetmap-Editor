using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public readonly record struct FeaturePlacement(MapFeature Feature, int Index, MapDataLayer? Layer = null);

public readonly record struct FeatureHiddenState(MapFeature Feature, bool IsHidden);

public sealed class MapEditDataset {
    public MapEditDataset(MapDocument? document = null) {
        Document = document;
    }

    public MapDocument? Document { get; private set; }

    public void ReplaceDocument(MapDocument? document) {
        Document = document;
    }

    public MapDocument EnsureDocument() {
        Document ??= new MapDocument();
        return Document;
    }

    public bool Contains(MapFeature feature) {
        return GetLayer(feature) is not null;
    }

    public int IndexOf(MapFeature feature) {
        return GetLayer(feature)?.Features.IndexOf(feature) ?? -1;
    }

    public MapDataLayer? GetLayer(MapFeature feature) {
        return Document?.FindDataLayer(feature);
    }

    public bool AddFeature(MapFeature feature, int? index = null, bool markDirty = true) {
        var document = EnsureDocument();
        var layer = document.ActiveDataLayer;
        if (document.FindDataLayer(feature) is not null) return false;

        var insertIndex = index.HasValue
            ? Math.Clamp(index.Value, 0, layer.Features.Count)
            : layer.Features.Count;
        layer.Features.Insert(insertIndex, feature);
        MarkFeatureSetChanged(document, layer, markDirty);
        return true;
    }

    public bool RemoveFeature(MapFeature feature, bool markDirty = true) {
        if (Document is null) return false;
        var layer = Document.FindDataLayer(feature);
        if (layer is null) return false;

        var removed = layer.Features.Remove(feature);
        if (!removed) return false;

        MarkFeatureSetChanged(Document, layer, markDirty);
        return true;
    }

    public IReadOnlyList<FeaturePlacement> RemoveFeatures(IEnumerable<MapFeature> features, bool markDirty = true) {
        if (Document is null) return [];

        var placements = features
            .Distinct()
            .Select(feature => {
                var layer = Document.FindDataLayer(feature);
                return new FeaturePlacement(feature, layer?.Features.IndexOf(feature) ?? -1, layer);
            })
            .Where(static placement => placement.Index >= 0)
            .OrderBy(static placement => placement.Layer?.Id)
            .ThenBy(static placement => placement.Index)
            .ToList();
        if (placements.Count == 0) return [];

        foreach (var group in placements.GroupBy(static placement => placement.Layer)) {
            if (group.Key is null) continue;

            foreach (var placement in group.OrderByDescending(static placement => placement.Index)) {
                group.Key.Features.RemoveAt(placement.Index);
            }
            group.Key.InvalidateSpatialIndex();
        }
        MarkContentChanged(markDirty);
        return placements;
    }

    public void RestoreFeatures(IEnumerable<FeaturePlacement> placements, bool markDirty = true) {
        var document = EnsureDocument();
        var restored = false;
        foreach (var placement in placements.OrderBy(static placement => placement.Index)) {
            if (document.FindDataLayer(placement.Feature) is not null) continue;

            var layer = placement.Layer ?? document.ActiveDataLayer;
            var index = Math.Clamp(placement.Index, 0, layer.Features.Count);
            layer.Features.Insert(index, placement.Feature);
            layer.InvalidateSpatialIndex();
            restored = true;
        }
        if (restored) MarkContentChanged(markDirty);
    }

    public bool SetFeatureHidden(MapFeature feature, bool isHidden, bool markDirty = false) {
        if (!Contains(feature) || feature.IsHidden == isHidden) return false;

        feature.IsHidden = isHidden;
        MarkContentChanged(markDirty);
        return true;
    }

    public int? AppendPoint(MapFeature feature, int partIndex, GeoPoint point, bool markDirty = true) {
        if (!Contains(feature) ||
            partIndex < 0 ||
            partIndex >= feature.Parts.Count) {
            return null;
        }

        var part = feature.Parts[partIndex];
        var pointIndex = part.Count;
        part.Add(point);
        MarkGeometryChanged(feature, markDirty);
        return pointIndex;
    }

    public bool RemovePointAt(MapFeature feature, int partIndex, int pointIndex, bool markDirty = true) {
        if (!Contains(feature) ||
            partIndex < 0 ||
            partIndex >= feature.Parts.Count ||
            pointIndex < 0 ||
            pointIndex >= feature.Parts[partIndex].Count) {
            return false;
        }

        feature.Parts[partIndex].RemoveAt(pointIndex);
        MarkGeometryChanged(feature, markDirty);
        return true;
    }

    public bool ReplaceParts(MapFeature feature, IEnumerable<List<GeoPoint>> parts, bool markDirty = true) {
        if (!Contains(feature)) return false;

        feature.Parts.Clear();
        feature.Parts.AddRange(parts.Select(static part => part.ToList()));
        MarkGeometryChanged(feature, markDirty);
        return true;
    }

    public void RestoreDirty(bool isDirty) {
        if (Document is not null) Document.IsDirty = isDirty;
    }

    public void MarkContentChanged(bool markDirty = true) {
        if (Document is null) return;

        Document.MarkContentChanged();
        if (markDirty) Document.IsDirty = true;
    }

    private void MarkGeometryChanged(MapFeature feature, bool markDirty) {
        feature.InvalidateGeometry();
        if (Document is null) return;

        Document.FindDataLayer(feature)?.InvalidateSpatialIndex();
        Document.InvalidateSpatialIndex();
        Document.MarkContentChanged();
        if (markDirty) Document.IsDirty = true;
    }

    private static void MarkFeatureSetChanged(MapDocument document, MapDataLayer layer, bool markDirty) {
        layer.InvalidateSpatialIndex();
        document.InvalidateSpatialIndex();
        document.MarkContentChanged();
        if (markDirty) document.IsDirty = true;
    }
}
