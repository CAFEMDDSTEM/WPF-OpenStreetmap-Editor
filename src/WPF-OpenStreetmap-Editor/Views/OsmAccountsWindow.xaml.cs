using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public partial class OsmAccountsWindow : Window {
    private static readonly IReadOnlyList<AuthMethodOption> AuthenticationMethods = [
        new(OsmAuthenticationMethod.BasicPassword, "Osm.Accounts.BasicPassword"),
        new(OsmAuthenticationMethod.OAuth2, "OAuth 2.0")
    ];

    private readonly OsmAccountStore _store;
    private OsmAccount? _editingAccount;
    private CancellationTokenSource? _authorizationCts;
    private static LocalizationService L => LocalizationService.Instance;

    public OsmAccountsWindow(OsmAccountStore store) {
        InitializeComponent();
        _store = store;
        AuthenticationMethodComboBox.ItemsSource = AuthenticationMethods;
        AuthenticationMethodComboBox.DisplayMemberPath = nameof(AuthMethodOption.LocalizedDisplayName);
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
        OAuthStatusTextBlock.Text = "";
        LoadOAuthConfig(account);
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
            L.Format("Osm.Accounts.DeleteConfirm", account.DisplayName),
            L.GetString("Osm.Accounts.DeleteTitle"),
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
            StatusTextBlock.Text = L.GetString("Osm.Accounts.SavedStatus");
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
            StatusTextBlock.Text = L.Format("Osm.Accounts.TestSucceeded", displayName);
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
            OAuthClientId = OAuthClientIdTextBox.Text.Trim(),
            OAuthClientSecret = HasClientSecretCheckBox.IsChecked == true ? OAuthClientSecretBox.Password.Trim() : "",
            OAuthRedirectUri = OAuthRedirectUriTextBox.Text.Trim(),
            OAuthPort = int.TryParse(OAuthPortTextBox.Text.Trim(), out var port) ? port : OAuth20Service.DefaultPort,
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
        OAuthStatusTextBlock.Text = "";
        LoadOAuthConfig(new OsmAccount());
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
            ? L.GetString("Osm.Accounts.PasswordRequired")
            : L.GetString("Osm.Accounts.AuthorizeFirst");
    }

    private void UpdateCredentialFields() {
        var isBasicPassword = GetSelectedAuthenticationMethod() == OsmAuthenticationMethod.BasicPassword;
        UserNameTextBox.IsEnabled = isBasicPassword;
        UserNameLabelTextBlock.Opacity = isBasicPassword ? 1 : 0.55;
        CredentialPasswordBox.Visibility = isBasicPassword ? Visibility.Visible : Visibility.Collapsed;
        OAuthPanel.Visibility = isBasicPassword ? Visibility.Collapsed : Visibility.Visible;
        CredentialLabelTextBlock.Text = isBasicPassword
            ? L.GetString("Osm.Accounts.Password")
            : L.GetString("Osm.Accounts.Authorization");
    }

    private void LoadOAuthConfig(OsmAccount account) {
        OAuthClientIdTextBox.Text = account.OAuthClientId;
        var hasSecret = !string.IsNullOrWhiteSpace(account.OAuthClientSecret);
        HasClientSecretCheckBox.IsChecked = hasSecret;
        ClientSecretLabelTextBlock.Visibility = hasSecret ? Visibility.Visible : Visibility.Collapsed;
        OAuthClientSecretBox.Visibility = hasSecret ? Visibility.Visible : Visibility.Collapsed;
        OAuthClientSecretBox.Password = account.OAuthClientSecret;
        OAuthRedirectUriTextBox.Text = account.OAuthRedirectUri;
        OAuthPortTextBox.Text = account.OAuthPort.ToString(CultureInfo.InvariantCulture);
    }

    private void HasClientSecretCheckBox_Changed(object sender, RoutedEventArgs e) {
        var hasSecret = HasClientSecretCheckBox.IsChecked == true;
        ClientSecretLabelTextBlock.Visibility = hasSecret ? Visibility.Visible : Visibility.Collapsed;
        OAuthClientSecretBox.Visibility = hasSecret ? Visibility.Visible : Visibility.Collapsed;
        if (!hasSecret) OAuthClientSecretBox.Password = "";
    }

    private async void Authorize_Click(object sender, RoutedEventArgs e) {
        try {
            var account = BuildAccount();
            if (string.IsNullOrWhiteSpace(account.OAuthClientId)) {
                throw new InvalidDataException(L.GetString("Osm.Auth.MissingClientId"));
            }
            var request = BuildAuthorizationRequest(account);
            _authorizationCts = new CancellationTokenSource();
            SetAuthorizingState(true);
            OAuthStatusTextBlock.Text = L.GetString("Osm.Accounts.Authorizing");
            var result = await new OAuth20Service().SignInAsync(account, request, _authorizationCts.Token);

            if (string.IsNullOrWhiteSpace(account.DisplayName)) {
                account.DisplayName = result.DisplayName;
            }
            _store.SaveAccount(account, result.AccessToken);
            _editingAccount = account;
            RefreshAccounts(account.Id);
            var message = L.Format("Osm.Accounts.AuthorizeSucceeded", result.DisplayName);
            OAuthStatusTextBlock.Text = message;
            StatusTextBlock.Text = message;
        } catch (OperationCanceledException) {
            OAuthStatusTextBlock.Text = L.GetString("Osm.Accounts.AuthorizeCancelled");
        } catch (Exception ex) {
            OAuthStatusTextBlock.Text = L.Format("Osm.Accounts.AuthorizeFailed", ex.Message);
            ShowError(ex);
        } finally {
            _authorizationCts?.Dispose();
            _authorizationCts = null;
            SetAuthorizingState(false);
        }
    }

    private void CancelAuthorization_Click(object sender, RoutedEventArgs e) {
        _authorizationCts?.Cancel();
    }

    private static OsmAuthorizationRequest BuildAuthorizationRequest(OsmAccount account) {
        return new OsmAuthorizationRequest(
            account.OAuthClientId,
            account.OAuthClientSecret,
            account.OAuthRedirectUri,
            account.OAuthPort,
            OAuth20Service.DefaultScopes);
    }

    private void SetAuthorizingState(bool authorizing) {
        AuthorizeButton.IsEnabled = !authorizing;
        CancelAuthorizationButton.Visibility = authorizing ? Visibility.Visible : Visibility.Collapsed;
        AccountsListView.IsEnabled = !authorizing;
        DisplayNameTextBox.IsEnabled = !authorizing;
        ApiBaseUrlTextBox.IsEnabled = !authorizing;
        AuthenticationMethodComboBox.IsEnabled = !authorizing;
        HasClientSecretCheckBox.IsEnabled = !authorizing;
        OAuthClientIdTextBox.IsEnabled = !authorizing;
        OAuthClientSecretBox.IsEnabled = !authorizing;
        OAuthRedirectUriTextBox.IsEnabled = !authorizing;
        OAuthPortTextBox.IsEnabled = !authorizing;
        if (!authorizing) UpdateCredentialFields();
    }

    private void ShowError(Exception ex) {
        StatusTextBlock.Text = ex.Message;
        MessageBox.Show(ex.Message, L.GetString("Osm.Accounts.DialogTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private sealed record AuthMethodOption(OsmAuthenticationMethod Method, string DisplayName) {
        public string LocalizedDisplayName => DisplayName.StartsWith("Osm.", StringComparison.Ordinal)
            ? L.GetString(DisplayName)
            : DisplayName;
    }

    private sealed class OsmAccountListItem {
        public OsmAccountListItem(OsmAccount account, bool hasCredential) {
            Account = account;
            CredentialStatus = hasCredential
                ? L.GetString("Osm.Accounts.Saved")
                : L.GetString("Osm.Accounts.NotSaved");
        }

        public OsmAccount Account { get; }
        public string CurrentText => Account.IsActive ? "*" : "";
        public string DisplayName => Account.DisplayName;
        public string Platform => "OpenStreetMap";
        public string AuthenticationMethod => OsmAuthenticationMethodDisplay.GetName(Account.AuthenticationMethod);
        public string ApiBaseUrl => Account.ApiBaseUrl;
        public string CredentialStatus { get; }
    }
}
