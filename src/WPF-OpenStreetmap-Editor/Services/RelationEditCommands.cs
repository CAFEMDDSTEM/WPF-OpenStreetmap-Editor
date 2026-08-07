using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public static class RelationEditService {
    public static OsmRelationMemberType? ToMemberType(OsmPrimitiveType type) {
        return type switch {
            OsmPrimitiveType.Node => OsmRelationMemberType.Node,
            OsmPrimitiveType.Way => OsmRelationMemberType.Way,
            OsmPrimitiveType.Relation => OsmRelationMemberType.Relation,
            _ => null
        };
    }

    public static OsmPrimitiveType ToPrimitiveType(OsmRelationMemberType type) {
        return type switch {
            OsmRelationMemberType.Node => OsmPrimitiveType.Node,
            OsmRelationMemberType.Way => OsmPrimitiveType.Way,
            OsmRelationMemberType.Relation => OsmPrimitiveType.Relation,
            _ => OsmPrimitiveType.Node
        };
    }

    public static bool MembersEqual(
        IReadOnlyList<OsmRelationMember> left,
        IReadOnlyList<OsmRelationMember> right) {
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++) {
            if (left[i] != right[i]) return false;
        }

        return true;
    }

    public static void SyncRelationFeatureGeometry(
        OsmDataset dataset,
        OsmRelation relation,
        MapFeature feature) {
        var relationFeature = OsmDocumentSync.CreateRelationFeature(dataset, relation);
        feature.Parts.Clear();
        if (relationFeature is not null) {
            foreach (var part in relationFeature.Parts) feature.Parts.Add(part.ToList());
            feature.GeometryType = MapGeometryType.Polygon;
        }

        feature.InvalidateGeometry();
    }
}

public sealed class SetRelationMembersCommand : IEditCommand {
    private readonly MapFeature _feature;
    private readonly IReadOnlyList<OsmRelationMember> _members;
    private IReadOnlyList<OsmRelationMember> _previousMembers = [];
    private IReadOnlyList<List<GeoPoint>> _previousParts = [];
    private MapGeometryType _previousGeometryType;
    private MapDirtyState? _dirtyState;

    public SetRelationMembersCommand(MapFeature feature, IEnumerable<OsmRelationMember> members) {
        _feature = feature;
        _members = members.ToList();
    }

    public string Description => "Edit relation members";

    public bool Execute(MapEditDataset dataset) {
        var document = dataset.EnsureDocument();
        if (document.Osm is null ||
            _feature.Osm is null ||
            !document.Osm.Relations.TryGetValue(_feature.Osm.Id, out var relation)) {
            return false;
        }

        if (RelationEditService.MembersEqual(relation.Members, _members)) return false;

        _dirtyState = dataset.CaptureDirtyState();
        _previousMembers = relation.Members.ToList();
        _previousParts = _feature.Parts.Select(static part => part.ToList()).ToList();
        _previousGeometryType = _feature.GeometryType;

        relation.Members = _members.ToList();
        RelationEditService.SyncRelationFeatureGeometry(document.Osm, relation, _feature);
        dataset.MarkContentChanged(_feature);
        return true;
    }

    public void Undo(MapEditDataset dataset) {
        var document = dataset.Document;
        if (document?.Osm is null || _feature.Osm is null) return;

        if (document.Osm.Relations.TryGetValue(_feature.Osm.Id, out var relation)) {
            relation.Members = _previousMembers.ToList();
        }

        _feature.Parts.Clear();
        foreach (var part in _previousParts) _feature.Parts.Add(part.ToList());
        _feature.GeometryType = _previousGeometryType;
        _feature.InvalidateGeometry();
        dataset.MarkContentChanged(_feature, markDirty: false);
        dataset.RestoreDirty(_dirtyState);
    }
}

public sealed class CreateRelationCommand : IEditCommand {
    private readonly IReadOnlyList<OsmRelationMember> _members;
    private readonly Dictionary<string, string> _tags;
    private MapFeature? _createdFeature;
    private long _relationId;
    private MapDirtyState? _dirtyState;

    public CreateRelationCommand(
        IEnumerable<OsmRelationMember> members,
        IReadOnlyDictionary<string, string> tags) {
        _members = members.ToList();
        _tags = OsmDataset.CopyTags(tags);
    }

    public MapFeature? CreatedFeature => _createdFeature;

    public string Description => "Create relation";

    public bool Execute(MapEditDataset dataset) {
        var document = dataset.EnsureDocument();
        if (document.Osm is null) return false;

        _dirtyState = dataset.CaptureDirtyState();
        _relationId = document.Osm.CreateRelation(_members, _tags);
        var relation = document.Osm.Relations[_relationId];

        var feature = OsmDocumentSync.CreateRelationFeature(document.Osm, relation);
        if (feature is not null) {
            dataset.AddFeature(feature);
            _createdFeature = feature;
        }

        return true;
    }

    public void Undo(MapEditDataset dataset) {
        var document = dataset.Document;
        if (document?.Osm is null) return;

        if (_createdFeature is not null) dataset.RemoveFeature(_createdFeature, markDirty: false);
        document.Osm.Relations.Remove(_relationId);
        dataset.RestoreDirty(_dirtyState);
    }
}
