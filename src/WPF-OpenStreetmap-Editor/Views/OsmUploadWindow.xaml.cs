using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public partial class OsmUploadWindow : Window {
    private readonly Func<OsmChangeBuildResult> _previewFactory;
    private OsmChangeBuildResult _preview;
    private bool _updatingSelection;

    public OsmUploadWindow(OsmAccount account, OsmChangeBuildResult preview)
        : this(account, preview, () => preview) {
    }

    public OsmUploadWindow(OsmAccount account, OsmChangeBuildResult preview, Func<OsmChangeBuildResult> previewFactory) {
        InitializeComponent();
        _preview = preview;
        _previewFactory = previewFactory;
        AccountTextBlock.Text = $"上传到“{account.ApiBaseUrl}”    账号：{account.DisplayName}    认证：{OsmAuthenticationMethodDisplay.GetName(account.AuthenticationMethod)}";
        CommentComboBox.ItemsSource = new[] {
            "更新 OpenStreetMap 数据",
            "修正地图要素",
            "添加缺失地图要素"
        };
        SourceComboBox.ItemsSource = new[] {
            "survey",
            "local knowledge",
            "Bing aerial imagery",
            "OpenStreetMap"
        };
        RefreshPreview();
    }

    public string Comment => CommentComboBox.Text.Trim();
    public string Source => SourceComboBox.Text.Trim();
    public bool ReviewRequested => ReviewRequestedCheckBox.IsChecked == true;
    public bool MetadataChanged { get; private set; }

    private void Upload_Click(object sender, RoutedEventArgs e) {
        if (string.IsNullOrWhiteSpace(Comment)) {
            MessageBox.Show("请输入变更说明。", "上传到 OpenStreetMap", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_preview.TotalCount == 0) {
            MessageBox.Show("当前没有可上传的变更。", "上传到 OpenStreetMap", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void EditMetadata_Click(object sender, RoutedEventArgs e) {
        if (GetSelectedChangeItem()?.Feature is not { } feature) {
            MessageBox.Show("请选择一个当前文档中的对象。删除项和共享节点不能在这里编辑原始 ID。", "编辑原始 ID", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new OsmFeatureMetadataWindow(feature) { Owner = this };
        if (window.ShowDialog() != true) return;

        try {
            MetadataChanged = true;
            _preview = _previewFactory();
            RefreshPreview();
            StatusTextBlock.Text = "原始 OSM 元数据已更新，上传预览已刷新。";
        } catch (Exception ex) {
            StatusTextBlock.Text = ex.Message;
            MessageBox.Show(ex.Message, "刷新上传预览", MessageBoxButton.OK, MessageBoxImage.Error);
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
        CreateGroupBox.Header = $"待创建对象：{creates.Count:N0}";
        ModifyGroupBox.Header = $"待修改对象：{modifies.Count:N0}";
        DeleteGroupBox.Header = $"待删除对象：{deletes.Count:N0}";
        ChangeSummaryTextBlock.Text = $"新建 {_preview.CreateCount:N0}，修改 {_preview.ModifyCount:N0}，删除 {_preview.DeleteCount:N0}";
        SettingsTextBlock.Text =
            "对象会上传到新的修改集。\n" +
            $"这次上传包含 {_preview.TotalCount:N0} 个 OSM 原始对象。\n" +
            "上传前可以选中当前文档中的对象并编辑其原始 OSM ID 和版本。";

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
            "way" => $"（{element.Elements("nd").Count().ToString("N0", CultureInfo.InvariantCulture)} 个节点）",
            "relation" => $"（{element.Elements("member").Count().ToString("N0", CultureInfo.InvariantCulture)} 个成员）",
            _ => ""
        };
    }

    private static long ParseLongAttribute(XElement element, string name) {
        return long.TryParse(element.Attribute(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private sealed class ChangeItem(string operation, string primitiveType, long id, string details, MapFeature? feature) {
        public string Operation { get; } = operation;
        public MapFeature? Feature { get; } = feature;
        public string Label => $"{FormatPrimitiveType(primitiveType)} {id.ToString(CultureInfo.InvariantCulture)} {details}";

        private static string FormatPrimitiveType(string value) {
            return value switch {
                "node" => "节点",
                "way" => "路径",
                "relation" => "关系",
                _ => value
            };
        }
    }
}
