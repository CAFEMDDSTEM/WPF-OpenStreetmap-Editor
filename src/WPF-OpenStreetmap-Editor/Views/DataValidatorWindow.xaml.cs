using System.Windows;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public partial class DataValidatorWindow : Window {
    public DataValidatorWindow() {
        InitializeComponent();
        ThemeService.ApplyWindowTheme(this);

        DataContext = new DataValidatorViewModel();
    }
}

public sealed class DataValidatorViewModel {
    public IReadOnlyList<DataValidatorRuleItem> Rules { get; } = [
        new("DataValidator.Rule.Addresses"),
        new("DataValidator.Rule.IncludePoiInDuplicateAddressChecks", runWhenUploading: false, hasUploadOption: false, level: 1),
        new("DataValidator.Rule.ApiCapabilities"),
        new("DataValidator.Rule.BarriersAndEntrances"),
        new("DataValidator.Rule.Coastlines"),
        new("DataValidator.Rule.ConditionalKeys"),
        new("DataValidator.Rule.ConnectivityRelations"),
        new("DataValidator.Rule.CrossingBoundaries"),
        new("DataValidator.Rule.CrossingSelf"),
        new("DataValidator.Rule.CrossingWays"),
        new("DataValidator.Rule.CycleDetector"),
        new("DataValidator.Rule.DirectionNodes"),
        new("DataValidator.Rule.DuplicateNodes"),
        new("DataValidator.Rule.DuplicateRelations"),
        new("DataValidator.Rule.DuplicateWays"),
        new("DataValidator.Rule.DuplicateWayNodes"),
        new("DataValidator.Rule.Highways"),
        new("DataValidator.Rule.InnerMultipolygons"),
        new("DataValidator.Rule.LaneAttributes"),
        new("DataValidator.Rule.TagChecker"),
        new("DataValidator.Rule.MissingName"),
        new("DataValidator.Rule.Multipolygon"),
        new("DataValidator.Rule.PublicTransportRoute"),
        new("DataValidator.Rule.RelationChecker"),
        new("DataValidator.Rule.RoadIntersections"),
        new("DataValidator.Rule.UnconnectedWays"),
        new("DataValidator.Rule.Waterways")
    ];
}

public sealed class DataValidatorRuleItem {
    public DataValidatorRuleItem(
        string nameKey,
        bool runWhenEditing = true,
        bool runWhenUploading = true,
        bool hasUploadOption = true,
        int level = 0) {
        var l = LocalizationService.Instance;
        Name = l.GetString(nameKey);
        RunWhenEditing = runWhenEditing;
        RunWhenUploading = runWhenUploading;
        HasUploadOption = hasUploadOption;
        UploadOptionName = l.Format("DataValidator.UploadOptionFormat", Name);
        Indent = new Thickness(level * 20, 0, 0, 0);
    }

    public string Name { get; }
    public bool RunWhenEditing { get; set; }
    public bool RunWhenUploading { get; set; }
    public bool HasUploadOption { get; }
    public string UploadOptionName { get; }
    public Thickness Indent { get; }
}
