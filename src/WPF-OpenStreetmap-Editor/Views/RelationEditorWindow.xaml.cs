using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public sealed class RelationMemberRow : INotifyPropertyChanged {
    private string _role;
    private readonly string _typeName;
    private readonly string _description;

    public RelationMemberRow(OsmRelationMemberType type, long id, string role, string description) {
        Type = type;
        Id = id;
        _role = role;
        _typeName = LocalizationService.Instance.GetString(TypeNameKey(type));
        _description = description;
    }

    public OsmRelationMemberType Type { get; }

    public long Id { get; }

    public string Role {
        get => _role;
        set {
            if (_role == value) return;
            _role = value;
            OnPropertyChanged();
        }
    }

    public string TypeName => _typeName;

    public string Description => _description;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string TypeNameKey(OsmRelationMemberType type) {
        return type switch {
            OsmRelationMemberType.Node => "Osm.Upload.Primitive.Node",
            OsmRelationMemberType.Way => "Osm.Upload.Primitive.Way",
            OsmRelationMemberType.Relation => "Osm.Upload.Primitive.Relation",
            _ => "Osm.Upload.Primitive.Node"
        };
    }
}

public sealed record RelationMemberCandidate(OsmRelationMemberType Type, long Id, string Display);

public partial class RelationEditorWindow : Window {
    private readonly MapDocument _document;
    private readonly IReadOnlyList<MapFeature> _selection;
    private static LocalizationService L => LocalizationService.Instance;
    private readonly ObservableCollection<RelationMemberRow> _members = [];

    public RelationEditorWindow(
        MapDocument document,
        OsmRelation? relation,
        IEnumerable<OsmRelationMember> initialMembers,
        IReadOnlyList<MapFeature>? selection = null) {
        InitializeComponent();
        _document = document;
        _selection = selection ?? [];
        HeaderTextBlock.Text = relation is null
            ? L.GetString("Osm.Relation.HeaderNew")
            : L.Format("Osm.Relation.HeaderFormat", relation.Id, GetRelationType(relation));
        TypeComboBox.ItemsSource = new[] {
            OsmRelationMemberType.Node,
            OsmRelationMemberType.Way,
            OsmRelationMemberType.Relation
        };
        TypeComboBox.SelectedItem = OsmRelationMemberType.Way;
        MembersDataGrid.ItemsSource = _members;
        foreach (var member in initialMembers) {
            _members.Add(CreateRow(member));
        }
        UpdateMemberCount();
        RefreshCandidates();
    }

    public IReadOnlyList<OsmRelationMember> Members { get; private set; } = [];

    private void TypeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) {
        RefreshCandidates();
    }

    private void RefreshCandidates() {
        if (TypeComboBox.SelectedItem is not OsmRelationMemberType type) {
            CandidateListBox.ItemsSource = null;
            return;
        }

        var existing = _members
            .Select(static member => (member.Type, member.Id))
            .ToHashSet();
        var candidates = _document.Features
            .Where(feature =>
                feature.Osm is not null &&
                feature.Osm.PrimitiveType == RelationEditService.ToPrimitiveType(type))
            .OrderBy(static feature => feature.Osm!.Id)
            .Select(feature => new RelationMemberCandidate(
                type,
                feature.Osm!.Id,
                Describe(feature)))
            .Where(candidate => !existing.Contains((candidate.Type, candidate.Id)))
            .ToList();
        CandidateListBox.ItemsSource = candidates;
    }

    private void AddMember_Click(object sender, RoutedEventArgs e) {
        if (CandidateListBox.SelectedItem is not RelationMemberCandidate candidate) {
            MessageBox.Show(
                L.GetString("Osm.Relation.EmptySelection"),
                L.GetString("Osm.Relation.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _members.Add(new RelationMemberRow(candidate.Type, candidate.Id, RoleTextBox.Text.Trim(), candidate.Display));
        UpdateMemberCount();
        RefreshCandidates();
    }

    private void AddSelected_Click(object sender, RoutedEventArgs e) {
        foreach (var feature in _selection) {
            if (feature.Osm is null) continue;
            var type = RelationEditService.ToMemberType(feature.Osm.PrimitiveType);
            if (type is null) continue;
            if (_members.Any(member => member.Type == type && member.Id == feature.Osm.Id)) continue;

            _members.Add(new RelationMemberRow(type.Value, feature.Osm.Id, RoleTextBox.Text.Trim(), Describe(feature)));
        }

        UpdateMemberCount();
        RefreshCandidates();
    }

    private void RemoveMember_Click(object sender, RoutedEventArgs e) {
        if (MembersDataGrid.SelectedItem is not RelationMemberRow row) return;
        _members.Remove(row);
        UpdateMemberCount();
        RefreshCandidates();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) {
        if (MembersDataGrid.SelectedItem is not RelationMemberRow row) return;
        var index = _members.IndexOf(row);
        if (index <= 0) return;
        _members.Move(index, index - 1);
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e) {
        if (MembersDataGrid.SelectedItem is not RelationMemberRow row) return;
        var index = _members.IndexOf(row);
        if (index < 0 || index >= _members.Count - 1) return;
        _members.Move(index, index + 1);
    }

    private void Save_Click(object sender, RoutedEventArgs e) {
        Members = _members
            .Select(static member => new OsmRelationMember(member.Type, member.Id, member.Role.Trim()))
            .ToList();
        DialogResult = true;
    }

    private RelationMemberRow CreateRow(OsmRelationMember member) {
        return new RelationMemberRow(member.Type, member.Id, member.Role, DescribeMember(member.Type, member.Id));
    }

    private string DescribeMember(OsmRelationMemberType type, long id) {
        var feature = _document.Features.FirstOrDefault(feature =>
            feature.Osm is not null &&
            feature.Osm.PrimitiveType == RelationEditService.ToPrimitiveType(type) &&
            feature.Osm.Id == id);
        return feature is null ? "" : Describe(feature);
    }

    private string Describe(MapFeature feature) {
        if (feature.Osm is null) return "";
        var idText = feature.Osm.Id.ToString();
        if (feature.Attributes.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name)) {
            return $"{idText} · {name}";
        }

        if (feature.Osm.PrimitiveType == OsmPrimitiveType.Way) {
            return $"{idText} · {L.Format("Osm.Relation.WayNodeCount", feature.Points.Count())}";
        }

        if (feature.Osm.PrimitiveType == OsmPrimitiveType.Relation &&
            feature.Attributes.TryGetValue("type", out var relationType) &&
            !string.IsNullOrWhiteSpace(relationType)) {
            return $"{idText} · type={relationType}";
        }

        return idText;
    }

    private void UpdateMemberCount() {
        MemberCountTextBlock.Text = L.Format("Osm.Relation.MemberCount", _members.Count);
    }

    private static string GetRelationType(OsmRelation relation) {
        return relation.Tags.TryGetValue("type", out var type) ? type : "";
    }
}
