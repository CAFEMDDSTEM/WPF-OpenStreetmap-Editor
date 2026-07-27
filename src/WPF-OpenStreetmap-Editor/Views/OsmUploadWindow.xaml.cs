using System.Windows;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public partial class OsmUploadWindow : Window {
    public OsmUploadWindow(OsmAccount account, OsmChangeBuildResult preview) {
        InitializeComponent();
        AccountTextBlock.Text = $"账号：{account.DisplayName}    API：{account.ApiBaseUrl}";
        ChangeSummaryTextBlock.Text = $"新建 {preview.CreateCount:N0}，修改 {preview.ModifyCount:N0}，删除 {preview.DeleteCount:N0}";
    }

    public string Comment => CommentTextBox.Text.Trim();

    private void Upload_Click(object sender, RoutedEventArgs e) {
        if (string.IsNullOrWhiteSpace(Comment)) {
            MessageBox.Show("请输入变更说明。", "上传到 OpenStreetMap", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
}
