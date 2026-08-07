using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using MahApps.Metro.IconPacks;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public partial class PresetToolbarSetupWindow : Window {
    private static LocalizationService L => LocalizationService.Instance;

    private readonly List<PresetToolbarButton> _buttons;
    private readonly PresetService _presets;

    public List<PresetToolbarButton> Result { get; private set; } = [];
    public bool Changed { get; private set; }

    public PresetToolbarSetupWindow(IReadOnlyList<PresetToolbarButton> current, PresetService presets) {
        _presets = presets;
        _buttons = current.Select(static button => button.Clone()).ToList();
        InitializeComponent();
        ThemeService.ApplyWindowTheme(this);
        BuildTree();
        RefreshPinnedList();
    }

    private sealed class PresetTreeNode {
        public required string Display { get; init; }
        public TagPresetGroup? Group { get; init; }
        public TagPreset? Preset { get; init; }
        public List<PresetTreeNode> Children { get; } = [];
    }

    private sealed class PinnedEntry {
        public required string Display { get; init; }
        public required PresetToolbarButton Button { get; init; }
    }

    private void BuildTree() {
        foreach (var group in _presets.RootGroups) {
            PresetTree.Items.Add(ToTreeViewItem(CreateGroupNode(group)));
        }
        PresetTree.ExpandAllNodes();
    }

    private PresetTreeNode CreateGroupNode(TagPresetGroup group) {
        var node = new PresetTreeNode {
            Display = group.DisplayName,
            Group = group
        };
        foreach (var item in group.Items) {
            node.Children.Add(new PresetTreeNode {
                Display = item.DisplayName,
                Preset = item
            });
        }
        foreach (var child in group.Groups) {
            node.Children.Add(CreateGroupNode(child));
        }
        return node;
    }

    private static TreeViewItem ToTreeViewItem(PresetTreeNode node) {
        var item = new TreeViewItem { Tag = node };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        var iconKind = PresetIconCatalog.Resolve(
            node.Group?.Icon ?? node.Preset?.Icon,
            node.Group?.Name ?? node.Preset?.Name);
        if (iconKind is not null && Enum.TryParse<PackIconLucideKind>(iconKind, out var kind)) {
            header.Children.Add(new PackIconLucide {
                Kind = kind,
                Width = 15,
                Height = 15,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (System.Windows.Media.Brush)Application.Current.TryFindResource("Theme.MutedTextBrush")
            });
        }
        header.Children.Add(new TextBlock { Text = node.Display, VerticalAlignment = VerticalAlignment.Center });
        item.Header = header;
        foreach (var child in node.Children) {
            item.Items.Add(ToTreeViewItem(child));
        }
        return item;
    }

    private void PresetTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) {
        var node = (e.NewValue as TreeViewItem)?.Tag as PresetTreeNode;
        PinGroupButton.IsEnabled = node?.Group is not null;
        PinItemButton.IsEnabled = node?.Preset is not null;
    }

    private void PinGroup_Click(object sender, RoutedEventArgs e) {
        var group = (PresetTree.SelectedItem as TreeViewItem)?.Tag is PresetTreeNode { Group: not null } node
            ? node.Group
            : null;
        if (group is null) return;

        var hasItems = PresetService.FlattenItems(group).Count > 0;
        if (!hasItems) return;

        if (!_buttons.Any(button => button.GroupKey == group.Key)) {
            _buttons.Add(new PresetToolbarButton {
                GroupKey = group.Key,
                Label = group.Name,
                Icon = group.Icon
            });
        }
        RefreshPinnedList();
    }

    private void PinItem_Click(object sender, RoutedEventArgs e) {
        var preset = (PresetTree.SelectedItem as TreeViewItem)?.Tag is PresetTreeNode { Preset: not null } node
            ? node.Preset
            : null;
        if (preset is null) return;

        if (!_buttons.Any(button => button.PresetId == preset.Id)) {
            _buttons.Add(new PresetToolbarButton {
                PresetId = preset.Id,
                Label = preset.Name,
                Icon = preset.Icon
            });
        }
        RefreshPinnedList();
    }

    private void Remove_Click(object sender, RoutedEventArgs e) {
        if (PinnedListBox.SelectedItem is not PinnedEntry entry) return;
        _buttons.Remove(entry.Button);
        RefreshPinnedList();
    }

    private void PinnedListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        RemoveButton.IsEnabled = PinnedListBox.SelectedItem is not null;
    }

    private void RefreshPinnedList() {
        var entries = _buttons.Select(button => {
            var isGroup = !string.IsNullOrEmpty(button.GroupKey);
            var display = !string.IsNullOrEmpty(button.PresetId)
                ? _presets.FindPreset(button.PresetId)?.DisplayName ?? button.Label
                : !string.IsNullOrEmpty(button.GroupKey)
                    ? _presets.FindGroup(button.GroupKey)?.DisplayName ?? button.Label
                    : button.Label;
            return new PinnedEntry {
                Display = $"{display}  [{(isGroup ? L.GetString("PresetToolbar.Group") : L.GetString("PresetToolbar.Item"))}]",
                Button = button
            };
        }).ToList();
        PinnedListBox.ItemsSource = entries;
        RemoveButton.IsEnabled = PinnedListBox.SelectedItem is not null;
    }

    private void Save_Click(object sender, RoutedEventArgs e) {
        Result = _buttons;
        Changed = true;
        DialogResult = true;
    }
}

internal static class TreeViewExtensions {
    public static void ExpandAllNodes(this ItemsControl itemsControl) {
        foreach (var item in itemsControl.Items) {
            if (item is TreeViewItem treeViewItem) {
                ExpandTreeItem(treeViewItem);
            }
        }
    }

    private static void ExpandTreeItem(TreeViewItem item) {
        item.IsExpanded = true;
        foreach (var child in item.Items) {
            if (child is TreeViewItem treeViewItem) {
                ExpandTreeItem(treeViewItem);
            }
        }
    }
}
