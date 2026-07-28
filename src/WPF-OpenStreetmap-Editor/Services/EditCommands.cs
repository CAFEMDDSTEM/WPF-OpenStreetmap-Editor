using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed class AddFeatureCommand : IEditCommand {
    private readonly MapFeature _feature;
    private int? _index;
    private bool _wasDirty;

    public AddFeatureCommand(MapFeature feature, int? index = null) {
        _feature = feature;
        _index = index;
    }

    public string Description => "Add feature";

    public bool Execute(MapEditDataset dataset) {
        var document = dataset.EnsureDocument();
        _wasDirty = document.IsDirty;
        return dataset.AddFeature(_feature, _index);
    }

    public void Undo(MapEditDataset dataset) {
        _index = dataset.IndexOf(_feature);
        dataset.RemoveFeature(_feature);
        dataset.RestoreDirty(_wasDirty);
    }
}

public sealed class AddFeaturesCommand : IEditCommand {
    private readonly IReadOnlyList<MapFeature> _features;
    private readonly int? _index;
    private IReadOnlyList<FeaturePlacement> _placements = [];
    private bool _wasDirty;

    public AddFeaturesCommand(IEnumerable<MapFeature> features, int? index = null) {
        _features = features.ToList();
        _index = index;
    }

    public string Description => "Add features";

    public bool Execute(MapEditDataset dataset) {
        var document = dataset.EnsureDocument();
        _wasDirty = document.IsDirty;

        var placements = _placements.Count == _features.Count
            ? _placements
            : CreatePlacements(document.Features.Count);
        var added = new List<FeaturePlacement>();
        foreach (var placement in placements.OrderBy(static placement => placement.Index)) {
            if (!dataset.AddFeature(placement.Feature, placement.Index)) continue;
            added.Add(new FeaturePlacement(placement.Feature, dataset.IndexOf(placement.Feature)));
        }

        _placements = added;
        if (_placements.Count > 0) return true;

        dataset.RestoreDirty(_wasDirty);
        return false;
    }

    public void Undo(MapEditDataset dataset) {
        dataset.RemoveFeatures(_placements.Select(static placement => placement.Feature));
        dataset.RestoreDirty(_wasDirty);
    }

    private IReadOnlyList<FeaturePlacement> CreatePlacements(int documentFeatureCount) {
        var startIndex = _index.HasValue
            ? Math.Clamp(_index.Value, 0, documentFeatureCount)
            : documentFeatureCount;
        return _features
            .Select((feature, offset) => new FeaturePlacement(feature, startIndex + offset))
            .ToList();
    }
}

public sealed class RemoveFeaturesCommand : IEditCommand {
    private readonly IReadOnlyList<MapFeature> _features;
    private IReadOnlyList<FeaturePlacement> _placements = [];
    private bool _wasDirty;

    public RemoveFeaturesCommand(IEnumerable<MapFeature> features) {
        _features = features.ToList();
    }

    public string Description => "Remove features";

    public bool Execute(MapEditDataset dataset) {
        if (dataset.Document is null) return false;

        _wasDirty = dataset.Document.IsDirty;
        _placements = dataset.RemoveFeatures(_features);
        return _placements.Count > 0;
    }

    public void Undo(MapEditDataset dataset) {
        dataset.RestoreFeatures(_placements);
        dataset.RestoreDirty(_wasDirty);
    }
}

public sealed class SetFeatureHiddenCommand : IEditCommand {
    private readonly IReadOnlyList<MapFeature> _features;
    private readonly bool _isHidden;
    private IReadOnlyList<FeatureHiddenState> _previousStates = [];
    private bool _wasDirty;

    public SetFeatureHiddenCommand(IEnumerable<MapFeature> features, bool isHidden) {
        _features = features.ToList();
        _isHidden = isHidden;
    }

    public string Description => _isHidden ? "Hide features" : "Show features";

    public bool Execute(MapEditDataset dataset) {
        if (dataset.Document is null) return false;

        _wasDirty = dataset.Document.IsDirty;
        _previousStates = _features
            .Distinct()
            .Where(feature => dataset.Contains(feature) && feature.IsHidden != _isHidden)
            .Select(static feature => new FeatureHiddenState(feature, feature.IsHidden))
            .ToList();
        if (_previousStates.Count == 0) return false;

        foreach (var state in _previousStates) {
            dataset.SetFeatureHidden(state.Feature, _isHidden);
        }
        dataset.RestoreDirty(_wasDirty);
        return true;
    }

    public void Undo(MapEditDataset dataset) {
        foreach (var state in _previousStates) {
            dataset.SetFeatureHidden(state.Feature, state.IsHidden);
        }
        dataset.RestoreDirty(_wasDirty);
    }
}

public sealed class SetFeatureAttributesCommand : IEditCommand {
    private readonly MapFeature _feature;
    private readonly Dictionary<string, string?> _attributes;
    private readonly bool _replaceAll;
    private Dictionary<string, string>? _previousAttributes;
    private bool _wasDirty;

    public SetFeatureAttributesCommand(MapFeature feature, IReadOnlyDictionary<string, string> attributes)
        : this(
            feature,
            attributes.ToDictionary(
                static item => item.Key,
                static item => (string?)item.Value,
                StringComparer.Ordinal),
            replaceAll: true) {
    }

    private SetFeatureAttributesCommand(
        MapFeature feature,
        Dictionary<string, string?> attributes,
        bool replaceAll) {
        _feature = feature;
        _attributes = attributes;
        _replaceAll = replaceAll;
    }

    public static SetFeatureAttributesCommand CreatePatch(
        MapFeature feature,
        IEnumerable<KeyValuePair<string, string?>> attributes) {
        return new SetFeatureAttributesCommand(
            feature,
            new Dictionary<string, string?>(attributes, StringComparer.Ordinal),
            replaceAll: false);
    }

    public string Description => "Update feature tags";

    public bool Execute(MapEditDataset dataset) {
        if (dataset.Document is null || !dataset.Contains(_feature) || !HasChanges()) {
            return false;
        }

        _wasDirty = dataset.Document.IsDirty;
        _previousAttributes = new Dictionary<string, string>(_feature.Attributes, StringComparer.Ordinal);
        ApplyAttributes(_feature, _attributes, _replaceAll);
        dataset.MarkContentChanged();
        return true;
    }

    public void Undo(MapEditDataset dataset) {
        if (_previousAttributes is null) return;

        ReplaceAttributes(_feature, _previousAttributes);
        dataset.MarkContentChanged();
        dataset.RestoreDirty(_wasDirty);
    }

    private bool HasChanges() {
        if (_replaceAll) {
            return !TagsEqual(featureTags: _feature.Attributes, replacement: _attributes);
        }

        foreach (var attribute in _attributes) {
            if (string.IsNullOrWhiteSpace(attribute.Key)) continue;

            var hasCurrent = _feature.Attributes.TryGetValue(attribute.Key, out var current);
            if (attribute.Value is null) {
                if (hasCurrent) return true;
            } else if (!hasCurrent || current != attribute.Value) {
                return true;
            }
        }

        return false;
    }

    private static void ApplyAttributes(
        MapFeature feature,
        IReadOnlyDictionary<string, string?> attributes,
        bool replaceAll) {
        if (replaceAll) feature.Attributes.Clear();
        foreach (var attribute in attributes) {
            if (string.IsNullOrWhiteSpace(attribute.Key)) continue;

            if (attribute.Value is null) {
                feature.Attributes.Remove(attribute.Key);
            } else {
                feature.Attributes[attribute.Key] = attribute.Value;
            }
        }
    }

    private static void ReplaceAttributes(MapFeature feature, IReadOnlyDictionary<string, string> attributes) {
        feature.Attributes.Clear();
        foreach (var attribute in attributes) feature.Attributes[attribute.Key] = attribute.Value;
    }

    private static bool TagsEqual(
        IReadOnlyDictionary<string, string> featureTags,
        IReadOnlyDictionary<string, string?> replacement) {
        var normalizedReplacement = replacement
            .Where(static item => item.Value is not null && !string.IsNullOrWhiteSpace(item.Key))
            .OrderBy(static item => item.Key, StringComparer.Ordinal)
            .Select(static item => KeyValuePair.Create(item.Key, item.Value!));
        return featureTags.Count == replacement.Count(static item => item.Value is not null && !string.IsNullOrWhiteSpace(item.Key)) &&
            featureTags.OrderBy(static item => item.Key, StringComparer.Ordinal)
                .SequenceEqual(normalizedReplacement);
    }
}

public sealed class SetFeatureOsmMetadataCommand : IEditCommand {
    private readonly MapFeature _feature;
    private readonly OsmFeatureMetadata? _metadata;
    private OsmFeatureMetadata? _previousMetadata;
    private bool _wasDirty;

    public SetFeatureOsmMetadataCommand(MapFeature feature, OsmFeatureMetadata? metadata) {
        _feature = feature;
        _metadata = metadata?.Clone();
    }

    public string Description => "Update OSM metadata";

    public bool Execute(MapEditDataset dataset) {
        if (dataset.Document is null || !dataset.Contains(_feature) || MetadataEqual(_feature.Osm, _metadata)) {
            return false;
        }

        _wasDirty = dataset.Document.IsDirty;
        _previousMetadata = _feature.Osm?.Clone();
        _feature.Osm = _metadata?.Clone();
        dataset.MarkContentChanged();
        return true;
    }

    public void Undo(MapEditDataset dataset) {
        _feature.Osm = _previousMetadata?.Clone();
        dataset.MarkContentChanged();
        dataset.RestoreDirty(_wasDirty);
    }

    private static bool MetadataEqual(OsmFeatureMetadata? left, OsmFeatureMetadata? right) {
        if (left is null || right is null) return left is null && right is null;
        return left.PrimitiveType == right.PrimitiveType &&
            left.Id == right.Id &&
            left.Version == right.Version &&
            left.NodeReferences.SequenceEqual(right.NodeReferences);
    }
}

public sealed record FeaturePartsSnapshot(MapFeature Feature, IReadOnlyList<IReadOnlyList<GeoPoint>> Parts);

public sealed class SetFeaturePartsCommand : IEditCommand {
    private readonly IReadOnlyList<FeaturePartsSnapshot> _beforeStates;
    private readonly IReadOnlyList<FeaturePartsSnapshot> _afterStates;
    private bool _wasDirty;

    public SetFeaturePartsCommand(
        IEnumerable<FeaturePartsSnapshot> beforeStates,
        IEnumerable<FeaturePartsSnapshot> afterStates) {
        _beforeStates = CloneSnapshots(beforeStates);
        _afterStates = CloneSnapshots(afterStates);
    }

    public string Description => "Update feature geometry";

    public bool Execute(MapEditDataset dataset) {
        if (dataset.Document is null || !HasGeometryChange(dataset)) return false;

        _wasDirty = dataset.Document.IsDirty;
        Apply(dataset, _afterStates);
        dataset.RestoreDirty(true);
        return true;
    }

    public void Undo(MapEditDataset dataset) {
        Apply(dataset, _beforeStates);
        dataset.RestoreDirty(_wasDirty);
    }

    private bool HasGeometryChange(MapEditDataset dataset) {
        var beforeByFeature = _beforeStates.ToDictionary(static state => state.Feature);
        foreach (var after in _afterStates) {
            if (!dataset.Contains(after.Feature) ||
                !beforeByFeature.TryGetValue(after.Feature, out var before)) {
                continue;
            }
            if (!PartsEqual(before.Parts, after.Parts)) return true;
        }

        return false;
    }

    private static void Apply(MapEditDataset dataset, IEnumerable<FeaturePartsSnapshot> states) {
        foreach (var state in states) {
            dataset.ReplaceParts(state.Feature, state.Parts.Select(static part => part.ToList()), markDirty: false);
        }
    }

    private static IReadOnlyList<FeaturePartsSnapshot> CloneSnapshots(IEnumerable<FeaturePartsSnapshot> states) {
        return states
            .Select(static state => new FeaturePartsSnapshot(state.Feature, CloneParts(state.Parts)))
            .ToList();
    }

    private static IReadOnlyList<IReadOnlyList<GeoPoint>> CloneParts(IEnumerable<IEnumerable<GeoPoint>> parts) {
        return parts.Select(static part => part.ToList()).ToList();
    }

    private static bool PartsEqual(
        IReadOnlyList<IReadOnlyList<GeoPoint>> left,
        IReadOnlyList<IReadOnlyList<GeoPoint>> right) {
        if (left.Count != right.Count) return false;

        for (var i = 0; i < left.Count; i++) {
            if (!left[i].SequenceEqual(right[i])) return false;
        }

        return true;
    }
}

public sealed class AppendFeaturePointCommand : IEditCommand {
    private readonly MapFeature _feature;
    private readonly int _partIndex;
    private readonly GeoPoint _point;
    private readonly bool _addFeatureIfMissing;
    private int? _featureIndex;
    private int? _pointIndex;
    private bool _addedFeature;
    private bool _wasDirty;

    public AppendFeaturePointCommand(
        MapFeature feature,
        int partIndex,
        GeoPoint point,
        bool addFeatureIfMissing = false,
        int? featureIndex = null) {
        _feature = feature;
        _partIndex = partIndex;
        _point = point;
        _addFeatureIfMissing = addFeatureIfMissing;
        _featureIndex = featureIndex;
    }

    public string Description => "Append feature point";

    public bool Execute(MapEditDataset dataset) {
        var document = dataset.EnsureDocument();
        _wasDirty = document.IsDirty;
        _addedFeature = false;

        var existingIndex = dataset.IndexOf(_feature);
        if (existingIndex < 0) {
            if (!_addFeatureIfMissing || !dataset.AddFeature(_feature, _featureIndex)) return false;
            _addedFeature = true;
        } else {
            _featureIndex = existingIndex;
        }

        _pointIndex = dataset.AppendPoint(_feature, _partIndex, _point);
        if (_pointIndex.HasValue) return true;

        if (_addedFeature) dataset.RemoveFeature(_feature);
        dataset.RestoreDirty(_wasDirty);
        return false;
    }

    public void Undo(MapEditDataset dataset) {
        if (_pointIndex.HasValue) {
            dataset.RemovePointAt(_feature, _partIndex, _pointIndex.Value);
        }

        if (_addedFeature) {
            _featureIndex = dataset.IndexOf(_feature);
            dataset.RemoveFeature(_feature);
        }
        dataset.RestoreDirty(_wasDirty);
    }
}
