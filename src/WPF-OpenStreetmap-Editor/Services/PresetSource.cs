using System.IO;
using System.Reflection;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public interface ITagPresetSource {
    string Id { get; }
    string Name { get; }
    IReadOnlyList<TagPresetGroup> RootGroups { get; }
    IReadOnlyList<TagPreset> Presets { get; }
    TagPreset? FindPreset(string presetId);
    TagPresetGroup? FindGroup(string groupKey);
}

public sealed class BuiltInPresetSource : ITagPresetSource {
    public static BuiltInPresetSource Instance { get; } = new();

    public string Id => "builtin";
    public string Name => "Built-in";
    public IReadOnlyList<TagPresetGroup> RootGroups { get; }
    public IReadOnlyList<TagPreset> Presets { get; }

    private BuiltInPresetSource() {
        Presets = TagPresetCatalog.All;
        RootGroups = [
            new TagPresetGroup("builtin", "Built-in", null, [], Presets)
        ];
    }

    public TagPreset? FindPreset(string presetId) {
        return Presets.FirstOrDefault(preset => preset.Id == presetId);
    }

    public TagPresetGroup? FindGroup(string groupKey) {
        return RootGroups.FirstOrDefault(group => group.Key == groupKey);
    }
}

public sealed class XmlTagPresetSource : ITagPresetSource {
    private const string BundledResourceName = "WPF_OpenStreetmap_Editor.Assets.defaultpresets.xml";

    public static XmlTagPresetSource CreateBundled() {
        var assembly = typeof(XmlTagPresetSource).Assembly;
        using var stream = assembly.GetManifestResourceStream(BundledResourceName) ??
            throw new InvalidOperationException($"Embedded resource '{BundledResourceName}' was not found.");

        var xml = ReadAll(stream);
        var userFile = Path.Combine(AppPaths.DataDirectory, "presets.xml");
        if (File.Exists(userFile)) {
            try {
                var userXml = File.ReadAllText(userFile);
                xml = userXml;
            } catch (Exception ex) {
                Logger.Error("Failed to read user preset file", ex);
            }
        }

        return new XmlTagPresetSource(TagPresetXmlParser.Parse(xml));
    }

    public static XmlTagPresetSource FromXml(string xml) {
        return new XmlTagPresetSource(TagPresetXmlParser.Parse(xml));
    }

    private readonly IReadOnlyDictionary<string, TagPreset> _presetsById;
    private readonly Dictionary<string, TagPresetGroup> _groupsByKey = new(StringComparer.Ordinal);

    private XmlTagPresetSource(TagPresetSet set) {
        Set = set;
        _presetsById = set.Presets.ToDictionary(static preset => preset.Id, StringComparer.Ordinal);
        _groupsByKey = new Dictionary<string, TagPresetGroup>(StringComparer.Ordinal);
        foreach (var group in set.RootGroups) {
            CollectGroups(group);
        }
    }

    public string Id => "xml";
    public string Name => "JOSM XML presets";
    public TagPresetSet Set { get; }
    public IReadOnlyList<TagPresetGroup> RootGroups => Set.RootGroups;
    public IReadOnlyList<TagPreset> Presets => Set.Presets;

    public TagPreset? FindPreset(string presetId) {
        return _presetsById.TryGetValue(presetId, out var preset) ? preset : null;
    }

    public TagPresetGroup? FindGroup(string groupKey) {
        return _groupsByKey.TryGetValue(groupKey, out var group) ? group : null;
    }

    private void CollectGroups(TagPresetGroup group) {
        _groupsByKey.TryAdd(group.Key, group);
        foreach (var child in group.Groups) {
            CollectGroups(child);
        }
    }

    private static string ReadAll(Stream stream) {
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

public sealed class PresetService {
    public static PresetService Instance { get; } = CreateDefault();

    private readonly IReadOnlyList<ITagPresetSource> _sources;
    private readonly IReadOnlyList<TagPresetGroup> _groups;
    private readonly IReadOnlyList<TagPreset> _presets;
    private readonly IReadOnlyDictionary<string, TagPreset> _presetsById;
    private readonly Dictionary<string, TagPresetGroup> _groupsByKey = new(StringComparer.Ordinal);

    public PresetService(IEnumerable<ITagPresetSource> sources) {
        _sources = sources.ToList();
        _groups = _sources.SelectMany(static source => source.RootGroups).ToList();
        _presets = _sources.SelectMany(static source => source.Presets).ToList();
        _presetsById = _presets
            .DistinctBy(static preset => preset.Id)
            .ToDictionary(static preset => preset.Id, StringComparer.Ordinal);
        _groupsByKey = new Dictionary<string, TagPresetGroup>(StringComparer.Ordinal);
        foreach (var group in _groups) {
            CollectGroups(group);
        }
    }

    public IReadOnlyList<ITagPresetSource> Sources => _sources;
    public IReadOnlyList<TagPresetGroup> RootGroups => _groups;
    public IReadOnlyList<TagPreset> Presets => _presets;

    public TagPreset? FindPreset(string? presetId) {
        if (presetId is null) return null;
        return _presetsById.TryGetValue(presetId, out var preset) ? preset : null;
    }

    public TagPresetGroup? FindGroup(string? groupKey) {
        if (groupKey is null) return null;
        return _groupsByKey.TryGetValue(groupKey, out var group) ? group : null;
    }

    public static IReadOnlyList<TagPreset> FlattenItems(TagPresetGroup group) {
        var items = new List<TagPreset>(group.Items);
        foreach (var child in group.Groups) {
            items.AddRange(FlattenItems(child));
        }
        return items;
    }

    private void CollectGroups(TagPresetGroup group) {
        _groupsByKey.TryAdd(group.Key, group);
        foreach (var child in group.Groups) {
            CollectGroups(child);
        }
    }

    private static PresetService CreateDefault() {
        return new PresetService([
            BuiltInPresetSource.Instance,
            XmlTagPresetSource.CreateBundled()
        ]);
    }
}
