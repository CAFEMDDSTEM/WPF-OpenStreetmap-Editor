using System.Net.Http;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public partial class OsmAccountsWindow : Window {
    private readonly OsmAccountStore _store;
    private OsmAccount? _editingAccount;

    public OsmAccountsWindow(OsmAccountStore store) {
        InitializeComponent();
        _store = store;
        RefreshAccounts();
        NewAccount();
    }

    private void AccountsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (AccountsListBox.SelectedItem is not OsmAccount account) return;
        _editingAccount = account;
        DisplayNameTextBox.Text = account.DisplayName;
        ApiBaseUrlTextBox.Text = account.ApiBaseUrl;
        AccessTokenPasswordBox.Clear();
        StatusTextBlock.Text = "";
    }

    private void New_Click(object sender, RoutedEventArgs e) => NewAccount();

    private void Activate_Click(object sender, RoutedEventArgs e) {
        if (AccountsListBox.SelectedItem is not OsmAccount account) return;
        try {
            _store.SetActive(account.Id);
            RefreshAccounts(account.Id);
        } catch (Exception ex) {
            ShowError(ex);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e) {
        if (AccountsListBox.SelectedItem is not OsmAccount account) return;
        var answer = MessageBox.Show(
            $"确定删除账号“{account.DisplayName}”及其本机凭据吗？",
            "删除 OSM 账号",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        try {
            _store.Delete(account.Id);
            RefreshAccounts();
            NewAccount();
        } catch (Exception ex) {
            ShowError(ex);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e) {
        try {
            var account = BuildAccount();
            _store.SaveAccount(account, AccessTokenPasswordBox.Password);
            _editingAccount = account;
            AccessTokenPasswordBox.Clear();
            RefreshAccounts(account.Id);
            StatusTextBlock.Text = "账号已保存。";
        } catch (Exception ex) {
            ShowError(ex);
        }
    }

    private async void Test_Click(object sender, RoutedEventArgs e) {
        try {
            var account = BuildAccount();
            var token = string.IsNullOrWhiteSpace(AccessTokenPasswordBox.Password)
                ? _store.GetAccessToken(account)
                : AccessTokenPasswordBox.Password;
            if (string.IsNullOrWhiteSpace(token)) throw new InvalidDataException("请先输入访问令牌。");
            IsEnabled = false;
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var displayName = await new OsmApiClient(client).GetUserDisplayNameAsync(account.ApiBaseUrl, token);
            StatusTextBlock.Text = $"验证成功：{displayName}";
        } catch (Exception ex) {
            ShowError(ex);
        } finally {
            IsEnabled = true;
        }
    }

    private OsmAccount BuildAccount() {
        var account = new OsmAccount {
            Id = _editingAccount?.Id ?? Guid.NewGuid().ToString("N"),
            DisplayName = DisplayNameTextBox.Text.Trim(),
            ApiBaseUrl = ApiBaseUrlTextBox.Text.Trim(),
            IsActive = _editingAccount?.IsActive ?? false
        };
        OsmAccountStore.Validate(account);
        return account;
    }

    private void NewAccount() {
        _editingAccount = null;
        AccountsListBox.SelectedItem = null;
        DisplayNameTextBox.Text = "";
        ApiBaseUrlTextBox.Text = OsmApiClient.DefaultApiBaseUrl;
        AccessTokenPasswordBox.Clear();
        StatusTextBlock.Text = "";
        DisplayNameTextBox.Focus();
    }

    private void RefreshAccounts(string? selectedId = null) {
        var accounts = _store.Load();
        AccountsListBox.ItemsSource = accounts;
        if (selectedId is not null) {
            AccountsListBox.SelectedItem = accounts.FirstOrDefault(account => account.Id == selectedId);
        }
    }

    private void ShowError(Exception ex) {
        StatusTextBlock.Text = ex.Message;
        MessageBox.Show(ex.Message, "OSM 账号", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
