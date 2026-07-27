namespace WPF_OpenStreetmap_Editor.Models;

public enum OsmRelationMemberType {
    Node,
    Way,
    Relation
}

public sealed class OsmDataset {
    public Dictionary<long, OsmNode> Nodes { get; } = [];
    public Dictionary<long, OsmWay> Ways { get; } = [];
    public Dictionary<long, OsmRelation> Relations { get; } = [];
    public long NextNodeId { get; set; } = -1;
    public long NextWayId { get; set; } = -1;
    public long NextRelationId { get; set; } = -1;

    public OsmDataset Clone() {
        var clone = new OsmDataset {
            NextNodeId = NextNodeId,
            NextWayId = NextWayId,
            NextRelationId = NextRelationId
        };
        foreach (var (id, node) in Nodes) clone.Nodes[id] = node.Clone();
        foreach (var (id, way) in Ways) clone.Ways[id] = way.Clone();
        foreach (var (id, relation) in Relations) clone.Relations[id] = relation.Clone();
        return clone;
    }

    public long CreateNode(GeoPoint point, IReadOnlyDictionary<string, string>? tags = null, int version = 1) {
        var id = NextNodeId--;
        Nodes[id] = new OsmNode {
            Id = id,
            Version = version,
            Point = point,
            Tags = CopyTags(tags)
        };
        return id;
    }

    public long CreateWay(IReadOnlyList<long> nodeIds, IReadOnlyDictionary<string, string>? tags = null, int version = 1) {
        var id = NextWayId--;
        Ways[id] = new OsmWay {
            Id = id,
            Version = version,
            NodeIds = nodeIds.ToList(),
            Tags = CopyTags(tags)
        };
        return id;
    }

    public long CreateRelation(
        IReadOnlyList<OsmRelationMember> members,
        IReadOnlyDictionary<string, string>? tags = null,
        int version = 1) {
        var id = NextRelationId--;
        Relations[id] = new OsmRelation {
            Id = id,
            Version = version,
            Members = members.ToList(),
            Tags = CopyTags(tags)
        };
        return id;
    }

    public void NormalizeTemporaryIds() {
        NextNodeId = Math.Min(-1, Nodes.Keys.Where(static id => id < 0).DefaultIfEmpty(0).Min() - 1);
        NextWayId = Math.Min(-1, Ways.Keys.Where(static id => id < 0).DefaultIfEmpty(0).Min() - 1);
        NextRelationId = Math.Min(-1, Relations.Keys.Where(static id => id < 0).DefaultIfEmpty(0).Min() - 1);
    }

    public static Dictionary<string, string> CopyTags(IReadOnlyDictionary<string, string>? tags) {
        return tags is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(tags, StringComparer.Ordinal);
    }
}

public sealed class OsmNode {
    public long Id { get; set; }
    public int Version { get; set; } = 1;
    public GeoPoint Point { get; set; }
    public Dictionary<string, string> Tags { get; set; } = new(StringComparer.Ordinal);

    public OsmNode Clone() {
        return new OsmNode {
            Id = Id,
            Version = Version,
            Point = Point,
            Tags = OsmDataset.CopyTags(Tags)
        };
    }
}

public sealed class OsmWay {
    public long Id { get; set; }
    public int Version { get; set; } = 1;
    public List<long> NodeIds { get; set; } = [];
    public Dictionary<string, string> Tags { get; set; } = new(StringComparer.Ordinal);

    public OsmWay Clone() {
        return new OsmWay {
            Id = Id,
            Version = Version,
            NodeIds = NodeIds.ToList(),
            Tags = OsmDataset.CopyTags(Tags)
        };
    }
}

public sealed record OsmRelationMember(OsmRelationMemberType Type, long Id, string Role);

public sealed class OsmRelation {
    public long Id { get; set; }
    public int Version { get; set; } = 1;
    public List<OsmRelationMember> Members { get; set; } = [];
    public Dictionary<string, string> Tags { get; set; } = new(StringComparer.Ordinal);

    public OsmRelation Clone() {
        return new OsmRelation {
            Id = Id,
            Version = Version,
            Members = Members.ToList(),
            Tags = OsmDataset.CopyTags(Tags)
        };
    }
}
