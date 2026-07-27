using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public partial class OsmAccountsWindow : Window {
    private static readonly IReadOnlyList<AuthMethodOption> AuthenticationMethods = [
        new(OsmAuthenticationMethod.BasicPassword, "账号密码"),
        new(OsmAuthenticationMethod.OAuth2, "OAuth 2.0")
    ];

    private readonly OsmAccountStore _store;
    private OsmAccount? _editingAccount;

    public OsmAccountsWindow(OsmAccountStore store) {
        InitializeComponent();
        _store = store;
        AuthenticationMethodComboBox.ItemsSource = AuthenticationMethods;
        AuthenticationMethodComboBox.DisplayMemberPath = nameof(AuthMethodOption.DisplayName);
        AuthenticationMethodComboBox.SelectedValuePath = nameof(AuthMethodOption.Method);
        RefreshAccounts();
        NewAccount();
    }

    private void AccountsListView_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (AccountsListView.SelectedItem is not OsmAccountListItem item) return;
        var account = item.Account;
        _editingAccount = account;
        DisplayNameTextBox.Text = account.DisplayName;
        ApiBaseUrlTextBox.Text = account.ApiBaseUrl;
        UserNameTextBox.Text = account.UserName;
        AuthenticationMethodComboBox.SelectedValue = account.AuthenticationMethod;
        CredentialPasswordBox.Clear();
        StatusTextBlock.Text = "";
        UpdateCredentialFields();
    }

    private void New_Click(object sender, RoutedEventArgs e) => NewAccount();

    private void Activate_Click(object sender, RoutedEventArgs e) {
        if (AccountsListView.SelectedItem is not OsmAccountListItem item) return;
        try {
            _store.SetActive(item.Account.Id);
            RefreshAccounts(item.Account.Id);
        } catch (Exception ex) {
            ShowError(ex);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e) {
        if (AccountsListView.SelectedItem is not OsmAccountListItem item) return;
        var account = item.Account;
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
            _store.SaveAccount(account, GetEnteredCredentialSecret());
            _editingAccount = account;
            CredentialPasswordBox.Clear();
            RefreshAccounts(account.Id);
            StatusTextBlock.Text = "账号已保存。";
        } catch (Exception ex) {
            ShowError(ex);
        }
    }

    private async void Test_Click(object sender, RoutedEventArgs e) {
        try {
            var account = BuildAccount();
            var credential = ResolveCredential(account) ?? throw new InvalidDataException(GetMissingCredentialMessage(account));
            IsEnabled = false;
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var displayName = await new OsmApiClient(client).GetUserDisplayNameAsync(account.ApiBaseUrl, credential);
            StatusTextBlock.Text = $"验证成功：{displayName}";
        } catch (Exception ex) {
            ShowError(ex);
        } finally {
            IsEnabled = true;
        }
    }

    private OsmAccount BuildAccount() {
        var method = GetSelectedAuthenticationMethod();
        var account = new OsmAccount {
            Id = _editingAccount?.Id ?? Guid.NewGuid().ToString("N"),
            DisplayName = DisplayNameTextBox.Text.Trim(),
            ApiBaseUrl = ApiBaseUrlTextBox.Text.Trim(),
            AuthenticationMethod = method,
            UserName = method == OsmAuthenticationMethod.BasicPassword ? UserNameTextBox.Text.Trim() : "",
            IsActive = _editingAccount?.IsActive ?? false
        };
        OsmAccountStore.Validate(account);
        return account;
    }

    private void NewAccount() {
        _editingAccount = null;
        AccountsListView.SelectedItem = null;
        DisplayNameTextBox.Text = "";
        ApiBaseUrlTextBox.Text = OsmApiClient.DefaultApiBaseUrl;
        UserNameTextBox.Text = "";
        AuthenticationMethodComboBox.SelectedValue = OsmAuthenticationMethod.BasicPassword;
        CredentialPasswordBox.Clear();
        StatusTextBlock.Text = "";
        UpdateCredentialFields();
        DisplayNameTextBox.Focus();
    }

    private void RefreshAccounts(string? selectedId = null) {
        var accounts = _store.Load()
            .Select(account => new OsmAccountListItem(account, _store.HasCredential(account)))
            .ToList();
        AccountsListView.ItemsSource = accounts;
        if (selectedId is not null) {
            AccountsListView.SelectedItem = accounts.FirstOrDefault(item => item.Account.Id == selectedId);
        }
    }

    private void AuthenticationMethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        UpdateCredentialFields();
    }

    private OsmAuthenticationMethod GetSelectedAuthenticationMethod() {
        return AuthenticationMethodComboBox.SelectedValue is OsmAuthenticationMethod method
            ? method
            : OsmAuthenticationMethod.BasicPassword;
    }

    private string? GetEnteredCredentialSecret() {
        return string.IsNullOrWhiteSpace(CredentialPasswordBox.Password) ? null : CredentialPasswordBox.Password;
    }

    private OsmAccountCredential? ResolveCredential(OsmAccount account) {
        var secret = GetEnteredCredentialSecret();
        if (!string.IsNullOrWhiteSpace(secret)) {
            return new OsmAccountCredential(account.AuthenticationMethod, account.UserName, secret);
        }
        if (_editingAccount is null ||
            _editingAccount.AuthenticationMethod != account.AuthenticationMethod ||
            !string.Equals(_editingAccount.UserName, account.UserName, StringComparison.Ordinal)) {
            return null;
        }

        return _store.GetCredential(_editingAccount);
    }

    private static string GetMissingCredentialMessage(OsmAccount account) {
        return account.AuthenticationMethod == OsmAuthenticationMethod.BasicPassword
            ? "请先输入密码。"
            : "请先输入访问令牌。";
    }

    private void UpdateCredentialFields() {
        var isBasicPassword = GetSelectedAuthenticationMethod() == OsmAuthenticationMethod.BasicPassword;
        UserNameTextBox.IsEnabled = isBasicPassword;
        UserNameLabelTextBlock.Opacity = isBasicPassword ? 1 : 0.55;
        CredentialLabelTextBlock.Text = isBasicPassword ? "密码" : "OAuth 2 访问令牌";
    }

    private void ShowError(Exception ex) {
        StatusTextBlock.Text = ex.Message;
        MessageBox.Show(ex.Message, "OSM 账号", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private sealed record AuthMethodOption(OsmAuthenticationMethod Method, string DisplayName);

    private sealed class OsmAccountListItem {
        public OsmAccountListItem(OsmAccount account, bool hasCredential) {
            Account = account;
            CredentialStatus = hasCredential ? "已保存" : "未保存";
        }

        public OsmAccount Account { get; }
        public string CurrentText => Account.IsActive ? "✓" : "";
        public string DisplayName => Account.DisplayName;
        public string Platform => "OpenStreetMap";
        public string AuthenticationMethod => OsmAuthenticationMethodDisplay.GetName(Account.AuthenticationMethod);
        public string ApiBaseUrl => Account.ApiBaseUrl;
        public string CredentialStatus { get; }
    }
}
