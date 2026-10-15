using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Controls;

public sealed record RelationMembershipItem(long RelationId, string Title, string Role);

/// <summary>
/// JOSM-style persistent tag editor. The panel never mutates features itself: every edit is raised through
/// <see cref="CommandRequested"/> and the host refreshes the panel after executing the command.
/// </summary>
public partial class TagEditorPanel : UserControl {
    private static readonly string[] RelationSummaryKeys = ["type", "name"];
    private static LocalizationService L => LocalizationService.Instance;
    private readonly ObservableCollection<TagEditorRow> _rows = [];
    private readonly ObservableCollection<RelationMembershipItem> _memberships = [];
    private IReadOnlyList<MapFeature> _features = [];
    private MapDocument? _document;
    private TagAutocompleteService? _autocomplete;
    private string? _pendingSelectedKey;

    public TagEditorPanel() {
        InitializeComponent();
        TagsDataGrid.ItemsSource = _rows;
        MembershipsListBox.ItemsSource = _memberships;
        NewKeyTextBox.SuggestionProvider = prefix => Autocomplete.SuggestKeys(prefix, DocumentFeatures);
        NewValueTextBox.SuggestionProvider = prefix => Autocomplete.SuggestValues(NewKeyTextBox.Text.Trim(), prefix, DocumentFeatures);
        Loaded += (_, _) => L.PropertyChanged += Localization_PropertyChanged;
        Unloaded += (_, _) => L.PropertyChanged -= Localization_PropertyChanged;
        UpdateSummary();
        UpdateMemberships();
        AddTagPanel.IsEnabled = false;
    }

    /// <summary>Raised with an undoable command the host should execute; the host then calls <see cref="Refresh"/>.</summary>
    public event EventHandler<IEditCommand>? CommandRequested;

    /// <summary>Raised when the user activates a relation membership whose relation exists as a feature.</summary>
    public event EventHandler<MapFeature>? NavigateRequested;

    private TagAutocompleteService Autocomplete => _autocomplete ??= new TagAutocompleteService(PresetService.Instance.Presets);

    private IEnumerable<MapFeature> DocumentFeatures => _document?.Features ?? Enumerable.Empty<MapFeature>();

    public void SetSelection(IReadOnlyList<MapFeature> features, MapDocument? document) {
        ArgumentNullException.ThrowIfNull(features);

        var featuresChanged = !_features.SequenceEqual(features);
        _features = features.ToList();
        _document = document;
        Update(featuresChanged);
    }

    public void Refresh() {
        Update(featuresChanged: false);
    }

    public void FocusNewTagKey() {
        NewKeyTextBox.Focus();
        NewKeyTextBox.SelectAll();
    }

    private void Update(bool featuresChanged) {
        var rows = TagEditorModel.BuildRows(_features);
        if (featuresChanged || !TagEditorModel.RowsEqual(rows, _rows)) {
            ReplaceRows(rows, featuresChanged);
        }

        UpdateSummary();
        UpdateMemberships();
        AddTagPanel.IsEnabled = _features.Count > 0;
    }

    private void ReplaceRows(IReadOnlyList<TagEditorRow> rows, bool featuresChanged) {
        var selectedKey = featuresChanged
            ? null
            : _pendingSelectedKey ?? (TagsDataGrid.SelectedItem as TagEditorRow)?.Key;
        _pendingSelectedKey = null;

        TagsDataGrid.CancelEdit(DataGridEditingUnit.Row);
        _rows.Clear();
        foreach (var row in rows) {
            _rows.Add(row);
        }

        var selected = selectedKey is null ? null : rows.FirstOrDefault(row => row.Key == selectedKey);
        if (selected is not null) {
            TagsDataGrid.SelectedItem = selected;
            TagsDataGrid.CurrentItem = selected;
        }
    }

    private void UpdateSummary() {
        SummaryTextBlock.Text = _features.Count switch {
            0 => L.GetString("TagEditor.NoSelection"),
            1 => DescribeFeature(_features[0]),
            _ => L.Format("TagEditor.Summary.Features", _features.Count)
        };
    }

    private static string DescribeFeature(MapFeature feature) {
        var parts = new List<string>(3);
        if (feature.Osm is { } osm) {
            parts.Add(L.Format("TagEditor.Summary.OsmObject", L.GetString(PrimitiveKey(osm.PrimitiveType)), osm.Id));
            parts.Add(L.Format("TagEditor.Summary.Version", osm.Version));
        } else {
            parts.Add(L.GetString(GeometryKey(feature.GeometryType)));
        }
        if (feature.GeometryType != MapGeometryType.Point) {
            parts.Add(L.Format("TagEditor.Summary.Vertices", feature.CoordinateCount));
        }

        return string.Join(" · ", parts);
    }

    private void UpdateMemberships() {
        _memberships.Clear();
        var osm = _features.Count == 1 ? _features[0].Osm : null;
        var memberType = osm is null ? null : RelationEditService.ToMemberType(osm.PrimitiveType);
        var relations = _document?.Osm?.Relations;
        if (osm is null || memberType is null || relations is null) {
            MembershipsExpander.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var relation in relations.Values.OrderBy(static relation => relation.Id)) {
            foreach (var member in relation.Members) {
                if (member.Type != memberType || member.Id != osm.Id) continue;

                _memberships.Add(new RelationMembershipItem(relation.Id, DescribeRelation(relation), member.Role));
            }
        }

        MembershipsExpander.Header = L.Format("TagEditor.MembershipsFormat", _memberships.Count);
        MembershipsExpander.Visibility = Visibility.Visible;
    }

    private static string DescribeRelation(OsmRelation relation) {
        var text = L.Format("TagEditor.Summary.OsmObject", L.GetString("TagEditor.Primitive.Relation"), relation.Id);
        foreach (var key in RelationSummaryKeys) {
            if (relation.Tags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)) {
                text += $" · {key}={value}";
            }
        }

        return text;
    }

    private void RequestCommand(IEditCommand? command) {
        if (command is not null) CommandRequested?.Invoke(this, command);
    }

    private void RequestRemove(TagEditorRow row) {
        RequestCommand(TagEditorModel.CreateRemoveCommand(_features, row.Key));
    }

    private void AddTag() {
        var key = NewKeyTextBox.Text.Trim();
        var value = NewValueTextBox.Text.Trim();
        if (key.Length == 0) {
            FocusNewTagKey();
            return;
        }
        if (value.Length == 0) {
            NewValueTextBox.Focus();
            return;
        }

        var command = TagEditorModel.CreateSetCommand(_features, key, value);
        if (command is null) return;

        NewKeyTextBox.Clear();
        NewValueTextBox.Clear();
        FocusNewTagKey();
        RequestCommand(command);
    }

    private void TagsDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) {
        if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not TagEditorRow row) return;

        var editor = FindVisualChild<AutoCompleteTextBox>(e.EditingElement);
        if (editor is null) return;

        var text = editor.Text.Trim();
        var isKeyEdit = ReferenceEquals(e.Column, KeyColumn);
        var command = isKeyEdit ? CreateRenameCommand(row, text) : CreateValueCommand(row, text);
        if (command is null) return;

        // Raised after the DataGrid finishes its edit transaction so the host's refresh can safely rebuild the rows.
        _pendingSelectedKey = isKeyEdit ? text : row.Key;
        Dispatcher.BeginInvoke(() => RequestCommand(command), DispatcherPriority.Input);
    }

    private IEditCommand? CreateRenameCommand(TagEditorRow row, string newKey) {
        if (newKey.Length == 0 || newKey == row.Key) return null;

        return TagEditorModel.CreateRenameKeyCommand(_features, row.Key, newKey);
    }

    private IEditCommand? CreateValueCommand(TagEditorRow row, string value) {
        // Leaving the "<different>" placeholder empty keeps every feature's own value.
        if (row.IsDifferent ? value.Length == 0 : value == row.Value) return null;

        return TagEditorModel.CreateSetCommand(_features, row.Key, value);
    }

    private void TagsDataGrid_PreviewKeyDown(object sender, KeyEventArgs e) {
        // Delete removes the selected tag only while no cell editor has focus, so it never eats text editing keys.
        if (e.Key != Key.Delete || e.OriginalSource is TextBoxBase || TagsDataGrid.SelectedItem is not TagEditorRow row) return;

        RequestRemove(row);
        e.Handled = true;
    }

    private void TagsDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        // Focus and select the clicked cell up front so the DataGrid's own click handling starts editing on a single click.
        var source = e.OriginalSource as DependencyObject;
        var cell = FindVisualAncestor<DataGridCell>(source);
        if (cell is null || cell.IsEditing || cell.IsReadOnly || FindVisualAncestor<ButtonBase>(source) is not null) return;

        if (!cell.IsFocused) cell.Focus();
        if (FindVisualAncestor<DataGridRow>(cell) is { IsSelected: false } row) row.IsSelected = true;
    }

    private void KeyEditor_Loaded(object sender, RoutedEventArgs e) {
        if (sender is not AutoCompleteTextBox editor) return;

        editor.SuggestionProvider = prefix => Autocomplete.SuggestKeys(prefix, DocumentFeatures);
        FocusEditor(editor);
    }

    private void ValueEditor_Loaded(object sender, RoutedEventArgs e) {
        if (sender is not AutoCompleteTextBox editor) return;

        var key = (editor.DataContext as TagEditorRow)?.Key ?? "";
        editor.SuggestionProvider = prefix => Autocomplete.SuggestValues(key, prefix, DocumentFeatures);
        FocusEditor(editor);
    }

    private static void FocusEditor(TextBox editor) {
        editor.Focus();
        editor.SelectAll();
    }

    private void RemoveTag_Click(object sender, RoutedEventArgs e) {
        if (sender is FrameworkElement { DataContext: TagEditorRow row }) RequestRemove(row);
    }

    private void AddTag_Click(object sender, RoutedEventArgs e) {
        AddTag();
    }

    private void NewKeyTextBox_KeyDown(object sender, KeyEventArgs e) {
        if (e.Key != Key.Enter) return;

        NewValueTextBox.Focus();
        NewValueTextBox.SelectAll();
        e.Handled = true;
    }

    private void NewValueTextBox_KeyDown(object sender, KeyEventArgs e) {
        if (e.Key != Key.Enter) return;

        AddTag();
        e.Handled = true;
    }

    private void MembershipsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
        if (_document is null ||
            e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(MembershipsListBox, source) is not ListBoxItem { DataContext: RelationMembershipItem item }) {
            return;
        }

        var target = _document.Features.FirstOrDefault(feature =>
            feature.Osm is { PrimitiveType: OsmPrimitiveType.Relation } osm && osm.Id == item.RelationId);
        if (target is not null) NavigateRequested?.Invoke(this, target);
    }

    private void Localization_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        UpdateSummary();
        UpdateMemberships();
    }

    private static string PrimitiveKey(OsmPrimitiveType type) {
        return type switch {
            OsmPrimitiveType.Way => "TagEditor.Primitive.Way",
            OsmPrimitiveType.Relation => "TagEditor.Primitive.Relation",
            _ => "TagEditor.Primitive.Node"
        };
    }

    private static string GeometryKey(MapGeometryType type) {
        return type switch {
            MapGeometryType.LineString => "TagEditor.Geometry.LineString",
            MapGeometryType.Polygon => "TagEditor.Geometry.Polygon",
            _ => "TagEditor.Geometry.Point"
        };
    }

    private static T? FindVisualAncestor<T>(DependencyObject? element) where T : DependencyObject {
        while (element is not null) {
            if (element is T match) return match;
            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject? element) where T : DependencyObject {
        if (element is null) return null;

        var count = VisualTreeHelper.GetChildrenCount(element);
        for (var i = 0; i < count; i++) {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child is T match) return match;

            var nested = FindVisualChild<T>(child);
            if (nested is not null) return nested;
        }

        return null;
    }
}
