using System.IO;
using System.Xml.Linq;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed record TagPresetSet(
    IReadOnlyList<TagPresetGroup> RootGroups,
    IReadOnlyList<TagPreset> Presets);

public static class TagPresetXmlParser {
    private const string JosmNamespace = "http://josm.openstreetmap.de/tagging-preset-1.0";

    public static TagPresetSet Parse(string xml) {
        ArgumentNullException.ThrowIfNull(xml);
        return Parse(XDocument.Parse(xml));
    }

    public static TagPresetSet Parse(Stream stream) {
        ArgumentNullException.ThrowIfNull(stream);
        return Parse(XDocument.Load(stream));
    }

    public static TagPresetSet Parse(XDocument document) {
        ArgumentNullException.ThrowIfNull(document);
        var root = document.Root;
        if (root is null) return new TagPresetSet([], []);

        var chunks = root.Elements()
            .Where(IsName("chunk"))
            .ToDictionary(static element => (string?)element.Attribute("id") ?? "", StringComparer.Ordinal);
        chunks.Remove("");

        var context = new ParseContext(chunks);
        var presets = new List<TagPreset>();
        var roots = root.Elements()
            .Where(element => IsName("group")(element) || IsName("item")(element))
            .Select(element => context.ParseNode(element, "", presets))
            .Where(static group => group is not null)
            .Cast<TagPresetGroup>()
            .ToList();

        return new TagPresetSet(roots, presets);
    }

    private sealed class ParseContext {
        private readonly IReadOnlyDictionary<string, XElement> _chunks;
        private readonly HashSet<string> _usedIds = new(StringComparer.Ordinal);

        public ParseContext(IReadOnlyDictionary<string, XElement> chunks) {
            _chunks = chunks;
        }

        public TagPresetGroup? ParseNode(XElement element, string groupPath, List<TagPreset> presets) {
            if (IsName("group")(element)) {
                return ParseGroup(element, groupPath, presets);
            }
            if (IsName("item")(element)) {
                var preset = ParseItem(element, groupPath);
                if (preset is null) return null;
                presets.Add(preset);
                return null;
            }
            return null;
        }

        private TagPresetGroup ParseGroup(XElement group, string groupPath, List<TagPreset> presets) {
            var name = (string?)group.Attribute("name") ?? "Group";
            var key = groupPath.Length == 0 ? name : $"{groupPath}/{name}";
            var icon = (string?)group.Attribute("icon");
            var nameContext = (string?)group.Attribute("name_context");
            var groups = new List<TagPresetGroup>();
            var items = new List<TagPreset>();

            foreach (var child in GetEffectiveChildren(group)) {
                if (IsName("group")(child)) {
                    groups.Add(ParseGroup(child, key, presets));
                } else if (IsName("item")(child)) {
                    var preset = ParseItem(child, key);
                    if (preset is null) continue;
                    items.Add(preset);
                    presets.Add(preset);
                }
            }

            return new TagPresetGroup(key, name, icon, groups, items, nameContext);
        }

        private TagPreset? ParseItem(XElement item, string groupPath) {
            var name = (string?)item.Attribute("name") ?? "Untitled preset";
            var icon = (string?)item.Attribute("icon");
            var nameContext = (string?)item.Attribute("name_context");
            var geometries = ParseGeometries((string?)item.Attribute("type"));

            var tags = new Dictionary<string, string>(StringComparer.Ordinal);
            var fields = new List<TagPresetField>();
            foreach (var child in GetEffectiveChildren(item)) {
                var localName = child.Name.LocalName;
                if (localName == "key" || localName == "tag") {
                    var key = (string?)child.Attribute("key") ?? (string?)child.Attribute("k");
                    var value = (string?)child.Attribute("value") ?? (string?)child.Attribute("v");
                    if (key is not null && value is not null) {
                        tags.TryAdd(key, value);
                    }
                } else {
                    var field = ParseField(child);
                    if (field is not null) fields.Add(field);
                }
            }

            var category = Categorize(tags, groupPath);
            var searchTerms = BuildSearchTerms(name, tags, fields);
            return new TagPreset(
                CreateUniqueId(groupPath, name),
                name,
                category,
                geometries,
                tags,
                fields,
                searchTerms,
                icon,
                nameContext);
        }

        private string CreateUniqueId(string groupPath, string name) {
            var baseId = groupPath.Length == 0 ? $"xml:{name}" : $"xml:{groupPath}/{name}";
            var id = baseId;
            var suffix = 2;
            while (!_usedIds.Add(id)) {
                id = $"{baseId}#{suffix++}";
            }
            return id;
        }

        private IEnumerable<XElement> GetEffectiveChildren(XElement container, HashSet<string>? visited = null) {
            visited ??= new HashSet<string>(StringComparer.Ordinal);
            foreach (var child in container.Elements()) {
                var localName = child.Name.LocalName;
                if (localName == "reference") {
                    var reference = (string?)child.Attribute("ref");
                    if (reference is not null && visited.Add(reference) && _chunks.TryGetValue(reference, out var chunk)) {
                        foreach (var expanded in GetEffectiveChildren(chunk, visited)) {
                            yield return expanded;
                        }
                    }
                } else if (localName is "optional" or "checkgroup" or "multi_select" or "multiselect" or "conditional" or "set") {
                    foreach (var expanded in GetEffectiveChildren(child, visited)) {
                        yield return expanded;
                    }
                } else if (localName is "space" or "separator" or "link" or "label" or "preset_link" or "role" or "comment" or "deprecated") {
                    continue;
                } else {
                    yield return child;
                }
            }
        }

        private static TagPresetField? ParseField(XElement element) {
            var key = (string?)element.Attribute("key");
            if (key is null) return null;

            var label = (string?)element.Attribute("text") ??
                (string?)element.Attribute("title") ??
                (string?)element.Attribute("description") ??
                key;

            return element.Name.LocalName switch {
                "combo" or "multiselect" or "multi_select" => new TagPresetField(
                    key, label, TagPresetFieldKind.Choice, TagPresetFieldImportance.Optional, ParseChoices(element)),
                "check" or "checkbox" => new TagPresetField(key, label, TagPresetFieldKind.Checkbox),
                _ => new TagPresetField(
                    key, label, IsNumericKey(key) ? TagPresetFieldKind.Number : TagPresetFieldKind.Text)
            };
        }

        private static IReadOnlyList<TagPresetChoice> ParseChoices(XElement element) {
            var choices = new List<TagPresetChoice>();
            foreach (var listEntry in element.Elements().Where(static child => child.Name.LocalName == "list_entry")) {
                var value = (string?)listEntry.Attribute("value");
                if (value is null) continue;
                var display = (string?)listEntry.Attribute("display_value") ??
                    (string?)listEntry.Attribute("short_description") ??
                    value;
                choices.Add(new TagPresetChoice(value, display));
            }
            if (choices.Count > 0) return choices;

            var values = (string?)element.Attribute("values");
            if (values is not null) {
                foreach (var value in values.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                    choices.Add(new TagPresetChoice(value, value));
                }
            }
            return choices;
        }
    }

    internal static TagPresetGeometry ParseGeometries(string? type) {
        if (string.IsNullOrWhiteSpace(type)) return TagPresetGeometry.Any;
        var flags = TagPresetGeometry.None;
        foreach (var token in type.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            switch (token) {
                case "node":
                    flags |= TagPresetGeometry.Point;
                    break;
                case "way":
                    flags |= TagPresetGeometry.Line;
                    break;
                case "closedway":
                    flags |= TagPresetGeometry.Line | TagPresetGeometry.Area;
                    break;
                case "area":
                case "multipolygon":
                    flags |= TagPresetGeometry.Area;
                    break;
            }
        }
        return flags;
    }

    internal static TagPresetCategory Categorize(IReadOnlyDictionary<string, string> tags, string groupPath) {
        foreach (var (key, value) in tags) {
            if (key.StartsWith("addr", StringComparison.Ordinal)) return TagPresetCategory.Address;
            if (key == "highway") {
                return value switch {
                    "footway" or "cycleway" or "path" or "steps" or "bridleway" or "track" or "pedestrian" =>
                        TagPresetCategory.Path,
                    _ => TagPresetCategory.Road
                };
            }
            switch (key) {
                case "building": return TagPresetCategory.Building;
                case "place": return TagPresetCategory.Place;
                case "amenity": return TagPresetCategory.Amenity;
                case "shop": return TagPresetCategory.Shop;
                case "landuse": return TagPresetCategory.LandUse;
                case "natural": return TagPresetCategory.Natural;
                case "public_transport":
                case "railway":
                case "aerialway":
                case "aeroway":
                case "waterway":
                case "route":
                    return TagPresetCategory.PublicTransport;
            }
        }

        return GroupCategory(groupPath);
    }

    private static TagPresetCategory GroupCategory(string groupPath) {
        var name = groupPath ?? "";
        if (name.Contains("Address", StringComparison.OrdinalIgnoreCase)) return TagPresetCategory.Address;
        if (name.Contains("Build", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("House", StringComparison.OrdinalIgnoreCase)) return TagPresetCategory.Building;
        if (name.Contains("Foot", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Path", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Bicycl", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Pedestrian", StringComparison.OrdinalIgnoreCase)) return TagPresetCategory.Path;
        if (name.Contains("Highway", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Street", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Road", StringComparison.OrdinalIgnoreCase)) return TagPresetCategory.Road;
        if (name.Contains("Transport", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Rail", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Aerial", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Airport", StringComparison.OrdinalIgnoreCase)) return TagPresetCategory.PublicTransport;
        if (name.Contains("Shop", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Store", StringComparison.OrdinalIgnoreCase)) return TagPresetCategory.Shop;
        if (name.Contains("Amenit", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Facilit", StringComparison.OrdinalIgnoreCase)) return TagPresetCategory.Amenity;
        if (name.Contains("Land", StringComparison.OrdinalIgnoreCase)) return TagPresetCategory.LandUse;
        if (name.Contains("Natural", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Water", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Forest", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Wood", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Coast", StringComparison.OrdinalIgnoreCase)) return TagPresetCategory.Natural;
        if (name.Contains("Place", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("City", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Town", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Village", StringComparison.OrdinalIgnoreCase)) return TagPresetCategory.Place;
        return TagPresetCategory.Custom;
    }

    private static List<string> BuildSearchTerms(
        string name,
        IReadOnlyDictionary<string, string> tags,
        IReadOnlyList<TagPresetField> fields) {
        var terms = new List<string>(tags.Count * 2 + fields.Count + 2) {
            name
        };
        foreach (var (key, value) in tags) {
            terms.Add(key);
            terms.Add($"{key}={value}");
            terms.Add(value);
        }
        foreach (var field in fields) {
            terms.Add(field.Key);
            terms.Add(field.Label);
        }
        return terms;
    }

    private static bool IsNumericKey(string key) {
        return key.Contains("max", StringComparison.OrdinalIgnoreCase) ||
            key.Contains(":lanes", StringComparison.OrdinalIgnoreCase) ||
            key is "lanes" or "capacity" or "levels" or "height" or "width" or "length" or "population" or "ele";
    }

    private static Func<XElement, bool> IsName(string localName) {
        return element => element.Name.LocalName == localName;
    }
}
