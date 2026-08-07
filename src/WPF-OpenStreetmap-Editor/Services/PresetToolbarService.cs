using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public enum PresetToolbarActionKind {
    Single,
    Group
}

public sealed class PresetToolbarAction {
    public required string ButtonId { get; init; }
    public required string Label { get; init; }
    public string? Icon { get; init; }
    public required PresetToolbarActionKind Kind { get; init; }
    public TagPreset? Preset { get; init; }
    public TagPresetGroup? Group { get; init; }
    public IReadOnlyList<TagPreset> GroupPresets { get; init; } = [];
}

public static class PresetToolbarService {
    public static IReadOnlyList<PresetToolbarAction> Resolve(
        IReadOnlyList<PresetToolbarButton> buttons,
        PresetService service) {
        ArgumentNullException.ThrowIfNull(buttons);
        ArgumentNullException.ThrowIfNull(service);

        var actions = new List<PresetToolbarAction>(buttons.Count);
        foreach (var button in buttons) {
            if (!string.IsNullOrEmpty(button.PresetId)) {
                var preset = service.FindPreset(button.PresetId);
                if (preset is null) continue;

                actions.Add(new PresetToolbarAction {
                    ButtonId = button.Id,
                    Label = string.IsNullOrWhiteSpace(button.Label) ? preset.Name : button.Label,
                    Icon = button.Icon ?? preset.Icon,
                    Kind = PresetToolbarActionKind.Single,
                    Preset = preset
                });
            } else if (!string.IsNullOrEmpty(button.GroupKey)) {
                var group = service.FindGroup(button.GroupKey);
                if (group is null) continue;

                var items = PresetService.FlattenItems(group);
                if (items.Count == 0) continue;

                actions.Add(new PresetToolbarAction {
                    ButtonId = button.Id,
                    Label = string.IsNullOrWhiteSpace(button.Label) ? group.Name : button.Label,
                    Icon = button.Icon ?? group.Icon,
                    Kind = PresetToolbarActionKind.Group,
                    Group = group,
                    GroupPresets = items
                });
            }
        }
        return actions;
    }

    public static bool RemoveUnresolvable(
        List<PresetToolbarButton> buttons,
        PresetService service) {
        var changed = false;
        for (var index = buttons.Count - 1; index >= 0; index--) {
            var button = buttons[index];
            var resolvable = !string.IsNullOrEmpty(button.PresetId) && service.FindPreset(button.PresetId) is not null ||
                !string.IsNullOrEmpty(button.GroupKey) && service.FindGroup(button.GroupKey) is not null;
            if (resolvable) continue;

            buttons.RemoveAt(index);
            changed = true;
        }
        return changed;
    }
}
