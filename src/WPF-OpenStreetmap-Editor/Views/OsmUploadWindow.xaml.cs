using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public partial class OsmUploadWindow : Window {
    private readonly Func<OsmChangeBuildResult> _previewFactory;
    private readonly Func<MapFeature, OsmFeatureMetadata?, bool> _metadataUpdater;
    private readonly MapDocument? _document;
    private readonly BetterIdAiClient? _aiClient;
    private OsmChangeBuildResult _preview;
    private CancellationTokenSource? _aiSummaryCts;
    private bool _updatingSelection;
    private static LocalizationService L => LocalizationService.Instance;

    public OsmUploadWindow(OsmAccount account, OsmChangeBuildResult preview)
        : this(account, preview, () => preview, ApplyMetadataDirectly) {
    }

    public OsmUploadWindow(OsmAccount account, OsmChangeBuildResult preview, Func<OsmChangeBuildResult> previewFactory)
        : this(account, preview, previewFactory, ApplyMetadataDirectly) {
    }

    public OsmUploadWindow(
        OsmAccount account,
        OsmChangeBuildResult preview,
        Func<OsmChangeBuildResult> previewFactory,
        Func<MapFeature, OsmFeatureMetadata?, bool> metadataUpdater,
        MapDocument? document = null,
        BetterIdAiClient? aiClient = null) {
        InitializeComponent();
        _preview = preview;
        _previewFactory = previewFactory;
        _metadataUpdater = metadataUpdater;
        _document = document;
        _aiClient = aiClient;
        AccountTextBlock.Text = L.Format(
            "Osm.Upload.AccountFormat",
            account.ApiBaseUrl,
            account.DisplayName,
            OsmAuthenticationMethodDisplay.GetName(account.AuthenticationMethod));
        CommentComboBox.ItemsSource = new[] {
            L.GetString("Osm.Upload.DefaultComment.Update"),
            L.GetString("Osm.Upload.DefaultComment.Fix"),
            L.GetString("Osm.Upload.DefaultComment.Add")
        };
        SourceComboBox.ItemsSource = new[] {
            "survey",
            "local knowledge",
            "Bing aerial imagery",
            "OpenStreetMap"
        };
        AiSummaryButton.IsEnabled = _document is not null && _aiClient is not null;
        AiSummaryButton.ToolTip = AiSummaryButton.IsEnabled
            ? L.GetString("Osm.Upload.AiAvailable")
            : L.GetString("Osm.Upload.AiUnavailableTooltip");
        Closed += (_, _) => _aiSummaryCts?.Cancel();
        RefreshPreview();
    }

    public string Comment => CommentComboBox.Text.Trim();
    public string Source => SourceComboBox.Text.Trim();
    public bool ReviewRequested => ReviewRequestedCheckBox.IsChecked == true;
    public bool MetadataChanged { get; private set; }

    private void Upload_Click(object sender, RoutedEventArgs e) {
        if (string.IsNullOrWhiteSpace(Comment)) {
            MessageBox.Show(L.GetString("Osm.Upload.CommentRequired"), L.GetString("Osm.Upload.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_preview.TotalCount == 0) {
            MessageBox.Show(L.GetString("Osm.Upload.NoChanges"), L.GetString("Osm.Upload.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void EditMetadata_Click(object sender, RoutedEventArgs e) {
        if (GetSelectedChangeItem()?.Feature is not { } feature) {
            MessageBox.Show(L.GetString("Osm.Upload.SelectEditableFeature"), L.GetString("Osm.Upload.EditRawIdTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new OsmFeatureMetadataWindow(feature) { Owner = this };
        if (window.ShowDialog() != true) return;

        try {
            if (!_metadataUpdater(feature, window.Metadata)) return;

            MetadataChanged = true;
            _preview = _previewFactory();
            RefreshPreview();
            StatusTextBlock.Text = L.GetString("Osm.Upload.MetadataUpdated");
        } catch (Exception ex) {
            StatusTextBlock.Text = ex.Message;
            MessageBox.Show(ex.Message, L.GetString("Osm.Upload.RefreshPreviewTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AiSummary_Click(object sender, RoutedEventArgs e) {
        if (_document is null || _aiClient is null) {
            StatusTextBlock.Text = L.GetString("Osm.Upload.AiUnavailable");
            return;
        }

        _aiSummaryCts?.Cancel();
        _aiSummaryCts = new CancellationTokenSource();
        var ct = _aiSummaryCts.Token;

        try {
            AiSummaryButton.IsEnabled = false;
            StatusTextBlock.Text = L.GetString("Osm.Upload.AiGenerating");
            _preview = _previewFactory();
            var summary = OsmAiChangesetSummaryBuilder.Build(_document, _preview);
            var comment = await _aiClient.SummarizeChangesAsync(summary, ct);
            CommentComboBox.Text = comment;
            StatusTextBlock.Text = L.GetString("Osm.Upload.AiGenerated");
        } catch (OperationCanceledException) {
        } catch (Exception ex) {
            Logger.Error("Failed to generate AI changeset summary", ex);
            StatusTextBlock.Text = L.Format("Osm.Upload.AiFailed", ex.Message);
        } finally {
            if (!ct.IsCancellationRequested) {
                AiSummaryButton.IsEnabled = _document is not null && _aiClient is not null;
            }
        }
    }

    private void ChangeList_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (_updatingSelection) return;
        if (sender is not ListBox selectedList || selectedList.SelectedItem is null) {
            UpdateEditButton();
            return;
        }

        _updatingSelection = true;
        try {
            foreach (var list in new[] { CreateListBox, ModifyListBox, DeleteListBox }) {
                if (!ReferenceEquals(list, selectedList)) list.SelectedItem = null;
            }
        } finally {
            _updatingSelection = false;
        }
        UpdateEditButton();
    }

    private void RefreshPreview() {
        var items = BuildChangeItems(_preview);
        var creates = items.Where(static item => item.Operation == "create").ToList();
        var modifies = items.Where(static item => item.Operation == "modify").ToList();
        var deletes = items.Where(static item => item.Operation == "delete").ToList();

        CreateListBox.ItemsSource = creates;
        ModifyListBox.ItemsSource = modifies;
        DeleteListBox.ItemsSource = deletes;
        CreateGroupBox.Header = L.Format("Osm.Upload.CreateCount", creates.Count);
        ModifyGroupBox.Header = L.Format("Osm.Upload.ModifyCount", modifies.Count);
        DeleteGroupBox.Header = L.Format("Osm.Upload.DeleteCount", deletes.Count);
        ChangeSummaryTextBlock.Text = L.Format(
            "Osm.Upload.Summary",
            _preview.CreateCount,
            _preview.ModifyCount,
            _preview.DeleteCount);
        SettingsTextBlock.Text = L.Format("Osm.Upload.SettingsText", _preview.TotalCount);

        SelectFirstChangeItem();
        UpdateEditButton();
    }

    private void SelectFirstChangeItem() {
        _updatingSelection = true;
        try {
            CreateListBox.SelectedItem = null;
            ModifyListBox.SelectedItem = null;
            DeleteListBox.SelectedItem = null;
            var list = new[] { ModifyListBox, CreateListBox, DeleteListBox }
                .FirstOrDefault(static candidate => candidate.Items.Count > 0);
            if (list is not null) list.SelectedIndex = 0;
        } finally {
            _updatingSelection = false;
        }
    }

    private void UpdateEditButton() {
        EditMetadataButton.IsEnabled = GetSelectedChangeItem()?.Feature is not null;
    }

    private ChangeItem? GetSelectedChangeItem() {
        return ModifyListBox.SelectedItem as ChangeItem ??
            CreateListBox.SelectedItem as ChangeItem ??
            DeleteListBox.SelectedItem as ChangeItem;
    }

    private static List<ChangeItem> BuildChangeItems(OsmChangeBuildResult preview) {
        var featureByPrimitive = preview.References
            .GroupBy(static reference => (reference.Type, reference.OldId))
            .ToDictionary(static group => group.Key, static group => group.First().Feature);
        var document = XDocument.Parse(preview.Xml);
        var result = new List<ChangeItem>();
        foreach (var section in document.Root?.Elements() ?? Enumerable.Empty<XElement>()) {
            var operation = section.Name.LocalName;
            foreach (var element in section.Elements()) {
                var type = element.Name.LocalName;
                var id = ParseLongAttribute(element, "id");
                featureByPrimitive.TryGetValue((type, id), out var feature);
                result.Add(new ChangeItem(operation, type, id, GetPrimitiveDetails(element), feature));
            }
        }

        return result;
    }

    private static string GetPrimitiveDetails(XElement element) {
        return element.Name.LocalName switch {
            "way" => L.Format("Osm.Upload.WayDetails", element.Elements("nd").Count()),
            "relation" => L.Format("Osm.Upload.RelationDetails", element.Elements("member").Count()),
            _ => ""
        };
    }

    private static long ParseLongAttribute(XElement element, string name) {
        return long.TryParse(element.Attribute(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static bool ApplyMetadataDirectly(MapFeature feature, OsmFeatureMetadata? metadata) {
        if (MetadataEqual(feature.Osm, metadata)) return false;

        feature.Osm = metadata?.Clone();
        return true;
    }

    private static bool MetadataEqual(OsmFeatureMetadata? left, OsmFeatureMetadata? right) {
        if (left is null || right is null) return left is null && right is null;
        return left.PrimitiveType == right.PrimitiveType &&
            left.Id == right.Id &&
            left.Version == right.Version &&
            left.NodeReferences.SequenceEqual(right.NodeReferences);
    }

    private sealed class ChangeItem(string operation, string primitiveType, long id, string details, MapFeature? feature) {
        public string Operation { get; } = operation;
        public MapFeature? Feature { get; } = feature;
        public string Label => $"{FormatPrimitiveType(primitiveType)} {id.ToString(CultureInfo.InvariantCulture)} {details}";

        private static string FormatPrimitiveType(string value) {
            return value switch {
                "node" => L.GetString("Osm.Upload.Primitive.Node"),
                "way" => L.GetString("Osm.Upload.Primitive.Way"),
                "relation" => L.GetString("Osm.Upload.Primitive.Relation"),
                _ => value
            };
        }
    }
}
