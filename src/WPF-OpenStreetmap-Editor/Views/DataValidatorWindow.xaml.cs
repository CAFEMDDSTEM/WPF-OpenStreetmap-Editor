using System.Windows;
using System.Windows.Input;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public partial class DataValidatorWindow : Window {
    private readonly EditorSession? _editor;
    private readonly IReadOnlyCollection<MapFeature> _selection;
    private readonly Action<MapFeature>? _navigate;
    private readonly ValidationService _validationService = new();
    private static LocalizationService L => LocalizationService.Instance;

    public DataValidatorWindow(
        EditorSession? editor = null,
        IReadOnlyCollection<MapFeature>? selection = null,
        Action<MapFeature>? navigate = null) {
        InitializeComponent();
        ThemeService.ApplyWindowTheme(this);
        _editor = editor;
        _selection = selection ?? [];
        _navigate = navigate;
        RunValidation();
    }

    private MapDocument? Document => _editor?.Document;

    private void Run_Click(object sender, RoutedEventArgs e) => RunValidation();

    private void Locate_Click(object sender, RoutedEventArgs e) => LocateSelectedIssue();

    private void IssueDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => LocateSelectedIssue();

    private void Fix_Click(object sender, RoutedEventArgs e) {
        if (IssueDataGrid.SelectedItem is not ValidationIssueItem item || _editor is null) return;
        var feature = FindFeature(item.Issue.Target);
        var fix = item.Issue.SuggestedFix;
        if (feature is null || fix is null) return;

        IEditCommand? command = fix.Kind switch {
            ValidationFixKind.RemoveTag when !string.IsNullOrEmpty(fix.TagKey) =>
                SetFeatureAttributesCommand.CreatePatch(feature, new Dictionary<string, string?> { [fix.TagKey] = null }),
            ValidationFixKind.RemoveConsecutiveDuplicatePoints => CreateGeometryFix(feature, RemoveConsecutiveDuplicates),
            ValidationFixKind.ClosePolygon => CreateGeometryFix(feature, ClosePolygon),
            _ => null
        };
        if (command is null || !_editor.Execute(command)) return;

        RunValidation();
        _navigate?.Invoke(feature);
    }

    private void RunValidation() {
        var document = Document;
        if (document is null) {
            IssueDataGrid.ItemsSource = Array.Empty<ValidationIssueItem>();
            SummaryTextBlock.Text = L.GetString("DataValidator.NoDocument");
            return;
        }

        var features = _selection.Count > 0 ? _selection : document.Features;
        var issues = _validationService
            .Validate(new ValidationContext(features.ToList(), document.Osm))
            .Select(static issue => new ValidationIssueItem(issue))
            .ToList();
        IssueDataGrid.ItemsSource = issues;
        SummaryTextBlock.Text = issues.Count == 0
            ? L.GetString("DataValidator.NoIssues")
            : L.Format("DataValidator.IssueCount", issues.Count);
    }

    private void LocateSelectedIssue() {
        if (IssueDataGrid.SelectedItem is not ValidationIssueItem item) return;
        var feature = FindFeature(item.Issue.Target);
        if (feature is not null) _navigate?.Invoke(feature);
    }

    private MapFeature? FindFeature(ValidationObjectReference target) {
        if (Document is not { } document) return null;
        if (target.Type == ValidationObjectType.Feature) {
            return document.DataLayers.SelectMany(static layer => layer.Features)
                .FirstOrDefault(feature => feature.Id == target.Id);
        }

        if (!long.TryParse(target.Id, out var osmId)) return null;
        var primitiveType = target.Type switch {
            ValidationObjectType.Node => OsmPrimitiveType.Node,
            ValidationObjectType.Way => OsmPrimitiveType.Way,
            ValidationObjectType.Relation => OsmPrimitiveType.Relation,
            _ => (OsmPrimitiveType?)null
        };
        return primitiveType is null
            ? null
            : document.DataLayers.SelectMany(static layer => layer.Features)
                .FirstOrDefault(feature => feature.Osm?.PrimitiveType == primitiveType && feature.Osm.Id == osmId);
    }

    private static IEditCommand? CreateGeometryFix(
        MapFeature feature,
        Func<IReadOnlyList<IReadOnlyList<GeoPoint>>, IReadOnlyList<IReadOnlyList<GeoPoint>>> transform) {
        var before = Capture(feature);
        var afterParts = transform(before.Parts);
        if (before.Parts.Zip(afterParts).All(pair => pair.First.SequenceEqual(pair.Second))) return null;
        return new SetFeaturePartsCommand([before], [new FeaturePartsSnapshot(feature, afterParts)]);
    }

    private static FeaturePartsSnapshot Capture(MapFeature feature) => new(
        feature,
        feature.Parts.Select(static part => (IReadOnlyList<GeoPoint>)part.ToList()).ToList());

    private static IReadOnlyList<IReadOnlyList<GeoPoint>> RemoveConsecutiveDuplicates(
        IReadOnlyList<IReadOnlyList<GeoPoint>> parts) => parts
        .Select(part => (IReadOnlyList<GeoPoint>)part.Where((point, index) => index == 0 || point != part[index - 1]).ToList())
        .ToList();

    private static IReadOnlyList<IReadOnlyList<GeoPoint>> ClosePolygon(
        IReadOnlyList<IReadOnlyList<GeoPoint>> parts) => parts
        .Select(part => {
            var closed = part.ToList();
            if (closed.Count > 0 && closed[0] != closed[^1]) closed.Add(closed[0]);
            return (IReadOnlyList<GeoPoint>)closed;
        })
        .ToList();

    private sealed class ValidationIssueItem {
        public ValidationIssueItem(ValidationIssue issue) {
            Issue = issue;
            SeverityText = issue.Severity.ToString();
            TargetText = $"{issue.Target.Type} {issue.Target.Id}";
            Message = issue.Message;
            FixText = issue.SuggestedFix?.Description ?? "";
        }

        public ValidationIssue Issue { get; }
        public string SeverityText { get; }
        public string TargetText { get; }
        public string Message { get; }
        public string FixText { get; }
    }
}
