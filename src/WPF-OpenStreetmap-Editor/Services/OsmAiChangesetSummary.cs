using System.Text.Json.Serialization;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed class OsmAiChangesetSummary {
    [JsonPropertyName("actual_changes")]
    public IReadOnlyList<OsmAiActualChange> ActualChanges { get; init; } = [];

    [JsonPropertyName("closed_features")]
    public IReadOnlyList<OsmAiNamedFeature> ClosedFeatures { get; init; } = [];

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Counts { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal);

    [JsonPropertyName("feature_actions")]
    public IReadOnlyList<OsmAiFeatureActionCount> FeatureActions { get; init; } = [];

    public IReadOnlyList<OsmAiFeatureCount> Features { get; init; } = [];

    [JsonPropertyName("meaningful_counts")]
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> MeaningfulCounts { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal);

    [JsonPropertyName("named_features")]
    public IReadOnlyList<OsmAiNamedFeature> NamedFeatures { get; init; } = [];

    public IReadOnlyList<string> Places { get; init; } = [];

    [JsonPropertyName("supporting_geometry_nodes")]
    public IReadOnlyDictionary<string, int> SupportingGeometryNodes { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    [JsonPropertyName("surrounding_named_areas")]
    public IReadOnlyList<OsmAiSurroundingArea> SurroundingNamedAreas { get; init; } = [];

    [JsonPropertyName("tag_keys")]
    public IReadOnlyList<string> TagKeys { get; init; } = [];

    public int Total { get; init; }
}

public sealed record OsmAiFeatureCount(
    string Feature,
    int Count);

public sealed record OsmAiFeatureActionCount(
    string Feature,
    int Created,
    int Modified,
    int Deleted);

public sealed class OsmAiActualChange {
    public string Action { get; init; } = "";

    [JsonPropertyName("feature_after")]
    public string? FeatureAfter { get; init; }

    [JsonPropertyName("feature_before")]
    public string? FeatureBefore { get; init; }

    public string Geometry { get; init; } = "";

    [JsonPropertyName("geometry_changed")]
    public bool GeometryChanged { get; init; }

    [JsonPropertyName("name_after")]
    public string? NameAfter { get; init; }

    [JsonPropertyName("name_before")]
    public string? NameBefore { get; init; }

    [JsonPropertyName("tag_changes")]
    public OsmAiTagChanges TagChanges { get; init; } = new();
}

public sealed class OsmAiTagChanges {
    public IReadOnlyList<OsmAiAddedTag> Added { get; init; } = [];

    public IReadOnlyList<OsmAiChangedTag> Changed { get; init; } = [];

    public IReadOnlyList<OsmAiAddedTag> Removed { get; init; } = [];
}

public sealed record OsmAiAddedTag(string Key, string Value);

public sealed record OsmAiChangedTag(string Key, string Before, string After);

public sealed record OsmAiNamedFeature(
    string Action,
    string Feature,
    string Geometry,
    string Name);

public sealed record OsmAiSurroundingArea(
    string Feature,
    string Name);

public static class OsmAiChangesetSummaryBuilder {
    private static readonly string[] Actions = ["created", "modified", "deleted"];
    private static readonly string[] PrimitiveTypes = ["node", "way", "relation"];
    private static readonly string[] GeometryTypes = ["point", "line", "area", "relation"];
    private static readonly HashSet<string> FeatureKeys = new(StringComparer.Ordinal) {
        "aerialway", "aeroway", "amenity", "barrier", "boundary", "building",
        "craft", "highway", "historic", "landuse", "leisure", "man_made",
        "natural", "office", "place", "power", "public_transport", "railway",
        "route", "shop", "tourism", "waterway"
    };
    private static readonly string[] NameKeys = ["name", "official_name", "short_name", "brand", "operator", "ref"];
    private static readonly string[] PlaceKeys = ["addr:city", "addr:district", "addr:place", "addr:street", "is_in", "place"];

    public static OsmAiChangesetSummary Build(MapDocument document, OsmChangeBuildResult preview) {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(preview);

        var changeItems = GetFeatureChanges(document);
        var counts = CountRawPrimitives(preview);
        var featureCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var featureActionCounts = new Dictionary<string, ActionCounts>(StringComparer.Ordinal);
        var meaningfulCounts = CreateNestedCounts(GeometryTypes);
        var meaningfulNodeCounts = Actions.ToDictionary(static action => action, static _ => 0, StringComparer.Ordinal);
        var namedFeatures = new List<OsmAiNamedFeature>();
        var closedFeatures = new List<OsmAiNamedFeature>();
        var tagKeys = new SortedSet<string>(StringComparer.Ordinal);
        var places = new SortedSet<string>(StringComparer.Ordinal);
        var actualChanges = new List<OsmAiActualChange>();

        foreach (var item in changeItems) {
            var tags = item.After?.Attributes ?? item.Before?.Attributes ?? new Dictionary<string, string>(StringComparer.Ordinal);
            var featureName = FeatureFor(tags) ?? GeometryFor(item.After ?? item.Before!);
            Increment(featureCounts, featureName);
            IncrementAction(featureActionCounts, featureName, item.Action);

            var geometry = GeometryFor(item.After ?? item.Before!);
            if (meaningfulCounts.TryGetValue(item.Action, out var actionCounts)) {
                Increment(actionCounts, geometry);
            }
            if ((item.After ?? item.Before)?.GeometryType == MapGeometryType.Point) {
                meaningfulNodeCounts[item.Action] += 1;
            }

            foreach (var key in tags.Keys) {
                if (!key.StartsWith('_') && key != "name" && !key.StartsWith("name:", StringComparison.Ordinal)) {
                    tagKeys.Add(key);
                }
            }

            foreach (var key in PlaceKeys) {
                if (tags.TryGetValue(key, out var place) && !string.IsNullOrWhiteSpace(place)) {
                    places.Add(place);
                }
            }

            var name = PreferredName(tags);
            if (!string.IsNullOrWhiteSpace(name) &&
                !namedFeatures.Any(existing => existing.Action == item.Action && existing.Name == name)) {
                namedFeatures.Add(new OsmAiNamedFeature(item.Action, featureName, geometry, name));
            }

            if (IsClosedFeature(item.After ?? item.Before!) &&
                !closedFeatures.Any(existing => existing.Name == name && existing.Feature == featureName)) {
                closedFeatures.Add(new OsmAiNamedFeature(item.Action, featureName, geometry, name ?? ""));
            }

            actualChanges.Add(CreateActualChange(item, geometry));
        }

        return new OsmAiChangesetSummary {
            ActualChanges = actualChanges.Take(20).ToList(),
            ClosedFeatures = closedFeatures.Take(12).ToList(),
            Counts = counts,
            FeatureActions = featureActionCounts
                .Select(static item => new OsmAiFeatureActionCount(
                    item.Key,
                    item.Value.Created,
                    item.Value.Modified,
                    item.Value.Deleted))
                .OrderByDescending(static item => item.Created + item.Modified + item.Deleted)
                .ThenBy(static item => item.Feature, StringComparer.Ordinal)
                .Take(12)
                .ToList(),
            Features = featureCounts
                .Select(static item => new OsmAiFeatureCount(item.Key, item.Value))
                .OrderByDescending(static item => item.Count)
                .ThenBy(static item => item.Feature, StringComparer.Ordinal)
                .Take(12)
                .ToList(),
            MeaningfulCounts = FreezeNestedCounts(meaningfulCounts),
            NamedFeatures = namedFeatures.Take(12).ToList(),
            Places = places.Take(12).ToList(),
            SupportingGeometryNodes = Actions.ToDictionary(
                static action => action,
                action => Math.Max(0, counts[action]["node"] - meaningfulNodeCounts[action]),
                StringComparer.Ordinal),
            SurroundingNamedAreas = [],
            TagKeys = tagKeys.Take(30).ToList(),
            Total = preview.TotalCount
        };
    }

    private static IReadOnlyList<FeatureChange> GetFeatureChanges(MapDocument document) {
        var changes = new List<FeatureChange>();
        foreach (var feature in document.Features) {
            if (!document.OriginalFeatures.TryGetValue(feature.Id, out var original)) {
                changes.Add(new FeatureChange("created", null, feature));
                continue;
            }

            original = ResolveOriginalFeature(document, original);
            if (!FeatureEquivalent(original, feature)) {
                changes.Add(new FeatureChange("modified", original, feature));
            }
        }

        foreach (var deleted in document.GetDeletedOriginalFeatures()) {
            changes.Add(new FeatureChange("deleted", ResolveOriginalFeature(document, deleted), null));
        }

        return changes;
    }

    private static MapFeature ResolveOriginalFeature(MapDocument document, MapFeature original) {
        if (!IsCompactOriginalFeature(original) || document.OriginalOsm is not { } dataset || original.Osm is null) {
            return original;
        }

        return original.Osm.PrimitiveType switch {
            OsmPrimitiveType.Node when dataset.Nodes.TryGetValue(original.Osm.Id, out var node) =>
                OsmDocumentSync.CreateNodeFeature(node),
            OsmPrimitiveType.Way when dataset.Ways.TryGetValue(original.Osm.Id, out var way) =>
                OsmDocumentSync.CreateWayFeature(dataset, way) ?? original,
            OsmPrimitiveType.Relation when dataset.Relations.TryGetValue(original.Osm.Id, out var relation) =>
                OsmDocumentSync.CreateRelationFeature(dataset, relation) ?? original,
            _ => original
        };
    }

    private static bool IsCompactOriginalFeature(MapFeature feature) {
        return feature.Osm is not null && feature.Parts.Count == 0 && feature.Attributes.Count == 0;
    }

    private static bool FeatureEquivalent(MapFeature left, MapFeature right) {
        return left.GeometryType == right.GeometryType &&
            PartsEqual(left.Parts, right.Parts) &&
            TagsEqual(left.Attributes, right.Attributes);
    }

    private static bool PartsEqual(
        IReadOnlyList<List<GeoPoint>> left,
        IReadOnlyList<List<GeoPoint>> right) {
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++) {
            if (!left[i].SequenceEqual(right[i])) return false;
        }

        return true;
    }

    private static bool TagsEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right) {
        return left.Count == right.Count &&
            left.OrderBy(static item => item.Key, StringComparer.Ordinal)
                .SequenceEqual(right.OrderBy(static item => item.Key, StringComparer.Ordinal));
    }

    private static Dictionary<string, IReadOnlyDictionary<string, int>> CountRawPrimitives(OsmChangeBuildResult preview) {
        var counts = CreateNestedCounts(PrimitiveTypes);
        var document = System.Xml.Linq.XDocument.Parse(preview.Xml);
        foreach (var section in document.Root?.Elements() ?? Enumerable.Empty<System.Xml.Linq.XElement>()) {
            var action = section.Name.LocalName switch {
                "create" => "created",
                "modify" => "modified",
                "delete" => "deleted",
                _ => null
            };
            if (action is null) continue;

            foreach (var element in section.Elements()) {
                if (counts.TryGetValue(action, out var actionCounts)) {
                    Increment(actionCounts, element.Name.LocalName);
                }
            }
        }

        return FreezeNestedCounts(counts);
    }

    private static Dictionary<string, Dictionary<string, int>> CreateNestedCounts(IReadOnlyList<string> keys) {
        return Actions.ToDictionary(
            static action => action,
            _ => keys.ToDictionary(static key => key, static _ => 0, StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    private static Dictionary<string, IReadOnlyDictionary<string, int>> FreezeNestedCounts(
        Dictionary<string, Dictionary<string, int>> counts) {
        return counts.ToDictionary(
            static item => item.Key,
            static item => (IReadOnlyDictionary<string, int>)new Dictionary<string, int>(item.Value, StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    private static OsmAiActualChange CreateActualChange(FeatureChange item, string geometry) {
        var beforeTags = item.Before?.Attributes ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var afterTags = item.After?.Attributes ?? new Dictionary<string, string>(StringComparer.Ordinal);
        return new OsmAiActualChange {
            Action = item.Action,
            FeatureAfter = FeatureFor(afterTags),
            FeatureBefore = FeatureFor(beforeTags),
            Geometry = geometry,
            GeometryChanged = item.Action == "modified" && !PartsEqual(item.Before!.Parts, item.After!.Parts),
            NameAfter = PreferredName(afterTags),
            NameBefore = PreferredName(beforeTags),
            TagChanges = GetTagChanges(beforeTags, afterTags)
        };
    }

    private static OsmAiTagChanges GetTagChanges(
        IReadOnlyDictionary<string, string> beforeTags,
        IReadOnlyDictionary<string, string> afterTags) {
        var added = new List<OsmAiAddedTag>();
        var changed = new List<OsmAiChangedTag>();
        var removed = new List<OsmAiAddedTag>();
        foreach (var key in beforeTags.Keys.Concat(afterTags.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)) {
            var hadBefore = beforeTags.TryGetValue(key, out var before);
            var hasAfter = afterTags.TryGetValue(key, out var after);
            if (hadBefore && hasAfter && before == after) continue;

            if (!hadBefore && hasAfter) {
                added.Add(new OsmAiAddedTag(key, after!));
            } else if (hadBefore && !hasAfter) {
                removed.Add(new OsmAiAddedTag(key, before!));
            } else if (hadBefore && hasAfter) {
                changed.Add(new OsmAiChangedTag(key, before!, after!));
            }
        }

        return new OsmAiTagChanges {
            Added = added.Take(20).ToList(),
            Changed = changed.Take(20).ToList(),
            Removed = removed.Take(20).ToList()
        };
    }

    private static string? FeatureFor(IReadOnlyDictionary<string, string> tags) {
        foreach (var key in FeatureKeys) {
            if (tags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)) {
                return $"{key}={value}";
            }
        }

        return null;
    }

    private static string? PreferredName(IReadOnlyDictionary<string, string> tags) {
        foreach (var key in NameKeys) {
            if (tags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)) {
                return value;
            }
        }

        return null;
    }

    private static string GeometryFor(MapFeature feature) {
        if (feature.Osm?.PrimitiveType == OsmPrimitiveType.Relation) return "relation";
        return feature.GeometryType switch {
            MapGeometryType.Point => "point",
            MapGeometryType.LineString => "line",
            MapGeometryType.Polygon => "area",
            _ => "feature"
        };
    }

    private static bool IsClosedFeature(MapFeature feature) {
        return feature.GeometryType == MapGeometryType.Polygon ||
            feature.Parts.Any(static part => part.Count > 2 && part[0] == part[^1]);
    }

    private static void Increment(Dictionary<string, int> counts, string key) {
        if (!counts.ContainsKey(key)) counts[key] = 0;
        counts[key] += 1;
    }

    private static void IncrementAction(Dictionary<string, ActionCounts> counts, string feature, string action) {
        if (!counts.TryGetValue(feature, out var value)) value = new ActionCounts();
        switch (action) {
            case "created":
                value.Created += 1;
                break;
            case "modified":
                value.Modified += 1;
                break;
            case "deleted":
                value.Deleted += 1;
                break;
        }

        counts[feature] = value;
    }

    private sealed record FeatureChange(string Action, MapFeature? Before, MapFeature? After);

    private sealed class ActionCounts {
        public int Created { get; set; }
        public int Modified { get; set; }
        public int Deleted { get; set; }
    }
}
