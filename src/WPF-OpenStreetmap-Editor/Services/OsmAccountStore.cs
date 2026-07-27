using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed class OsmAccount {
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "";
    public string ApiBaseUrl { get; set; } = OsmApiClient.DefaultApiBaseUrl;
    public OsmAuthenticationMethod AuthenticationMethod { get; set; } = OsmAuthenticationMethod.OAuth2;
    public string UserName { get; set; } = "";
    public bool IsActive { get; set; }
}

public interface ICredentialStore {
    void Write(string target, string userName, string secret);
    string? Read(string target);
    void Delete(string target);
}

public sealed class OsmAccountStore {
    private const string CredentialPrefix = "WPF-OpenStreetmap-Editor/OSM/";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _metadataPath;
    private readonly ICredentialStore _credentialStore;

    public OsmAccountStore()
        : this(AppPaths.OsmAccountsFile, new WindowsCredentialStore()) {
    }

    public OsmAccountStore(string metadataPath, ICredentialStore credentialStore) {
        _metadataPath = Path.GetFullPath(metadataPath);
        _credentialStore = credentialStore;
    }

    public IReadOnlyList<OsmAccount> Load() {
        if (!File.Exists(_metadataPath)) return [];
        try {
            var accounts = JsonSerializer.Deserialize<List<OsmAccount>>(File.ReadAllText(_metadataPath)) ?? [];
            EnsureSingleActive(accounts);
            return accounts;
        } catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) {
            throw new InvalidDataException("无法读取 OSM 账号配置。", ex);
        }
    }

    public OsmAccount? GetActive() => Load().FirstOrDefault(static account => account.IsActive);

    public OsmAccountCredential? GetCredential(OsmAccount account) {
        var secret = _credentialStore.Read(GetCredentialTarget(account.Id));
        return string.IsNullOrWhiteSpace(secret)
            ? null
            : new OsmAccountCredential(account.AuthenticationMethod, account.UserName, secret);
    }

    public bool HasCredential(OsmAccount account) => GetCredential(account) is not null;

    public string? GetAccessToken(OsmAccount account) {
        return account.AuthenticationMethod == OsmAuthenticationMethod.OAuth2
            ? _credentialStore.Read(GetCredentialTarget(account.Id))
            : null;
    }

    public void SaveAccount(OsmAccount account, string? credentialSecret) {
        Validate(account);
        var accounts = Load().ToList();
        var existingIndex = accounts.FindIndex(candidate => candidate.Id == account.Id);
        var existing = existingIndex >= 0 ? accounts[existingIndex] : null;
        if (existingIndex >= 0) accounts[existingIndex] = account;
        else accounts.Add(account);
        if (account.IsActive || accounts.Count == 1) {
            foreach (var candidate in accounts) candidate.IsActive = candidate.Id == account.Id;
        }
        EnsureSingleActive(accounts);
        SaveMetadata(accounts);
        if (!string.IsNullOrWhiteSpace(credentialSecret)) {
            _credentialStore.Write(GetCredentialTarget(account.Id), GetCredentialUserName(account), NormalizeSecret(account, credentialSecret));
        } else if (HasCredentialShapeChanged(existing, account)) {
            _credentialStore.Delete(GetCredentialTarget(account.Id));
        }
    }

    public void SetActive(string accountId) {
        var accounts = Load().ToList();
        if (accounts.All(account => account.Id != accountId)) throw new InvalidOperationException("OSM 账号不存在。");
        foreach (var account in accounts) account.IsActive = account.Id == accountId;
        SaveMetadata(accounts);
    }

    public void Delete(string accountId) {
        var accounts = Load().Where(account => account.Id != accountId).ToList();
        EnsureSingleActive(accounts);
        SaveMetadata(accounts);
        _credentialStore.Delete(GetCredentialTarget(accountId));
    }

    public static void Validate(OsmAccount account) {
        if (string.IsNullOrWhiteSpace(account.DisplayName) || account.DisplayName.Trim().Length > 100) {
            throw new InvalidDataException("账号名称长度必须为 1-100 个字符。");
        }
        if (!Enum.IsDefined(account.AuthenticationMethod)) {
            throw new InvalidDataException("OSM 认证方式无效。");
        }
        if (account.AuthenticationMethod == OsmAuthenticationMethod.BasicPassword &&
            (string.IsNullOrWhiteSpace(account.UserName) || account.UserName.Trim().Length > 100)) {
            throw new InvalidDataException("OSM 用户名长度必须为 1-100 个字符。");
        }
        if (!Uri.TryCreate(account.ApiBaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))) {
            throw new InvalidDataException("OSM API 地址必须使用 HTTPS；本机测试地址可以使用 HTTP。");
        }
    }

    private static string NormalizeSecret(OsmAccount account, string secret) {
        return account.AuthenticationMethod == OsmAuthenticationMethod.OAuth2 ? secret.Trim() : secret;
    }

    private static string GetCredentialUserName(OsmAccount account) {
        return account.AuthenticationMethod == OsmAuthenticationMethod.BasicPassword ? account.UserName.Trim() : account.DisplayName;
    }

    private static bool HasCredentialShapeChanged(OsmAccount? existing, OsmAccount account) {
        return existing is not null &&
            (existing.AuthenticationMethod != account.AuthenticationMethod ||
                !string.Equals(existing.UserName, account.UserName, StringComparison.Ordinal));
    }

    private void SaveMetadata(IReadOnlyList<OsmAccount> accounts) {
        Directory.CreateDirectory(Path.GetDirectoryName(_metadataPath)!);
        var temporaryPath = _metadataPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(accounts, JsonOptions), new UTF8Encoding(false));
        File.Move(temporaryPath, _metadataPath, overwrite: true);
    }

    private static void EnsureSingleActive(List<OsmAccount> accounts) {
        var active = accounts.FirstOrDefault(static account => account.IsActive) ?? accounts.FirstOrDefault();
        foreach (var account in accounts) account.IsActive = ReferenceEquals(account, active);
    }

    private static string GetCredentialTarget(string accountId) => CredentialPrefix + accountId;
}

public sealed class WindowsCredentialStore : ICredentialStore {
    public void Write(string target, string userName, string secret) {
        EnsureWindows();
        var secretBytes = Encoding.Unicode.GetBytes(secret);
        if (secretBytes.Length > 5120) throw new ArgumentOutOfRangeException(nameof(secret), "凭据长度超过 Windows 凭据库限制。");
        var secretPointer = Marshal.AllocHGlobal(secretBytes.Length);
        try {
            Marshal.Copy(secretBytes, 0, secretPointer, secretBytes.Length);
            var credential = new NativeCredential {
                Type = CredentialTypeGeneric,
                TargetName = target,
                CredentialBlobSize = (uint)secretBytes.Length,
                CredentialBlob = secretPointer,
                Persist = CredentialPersistLocalMachine,
                UserName = userName
            };
            if (!CredWrite(ref credential, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        } finally {
            ZeroMemory(secretPointer, secretBytes.Length);
            Marshal.FreeHGlobal(secretPointer);
        }
    }

    public string? Read(string target) {
        EnsureWindows();
        if (!CredRead(target, CredentialTypeGeneric, 0, out var pointer)) {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) return null;
            throw new System.ComponentModel.Win32Exception(error);
        }
        try {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0) return "";
            return Marshal.PtrToStringUni(credential.CredentialBlob, checked((int)credential.CredentialBlobSize / 2));
        } finally {
            CredFree(pointer);
        }
    }

    public void Delete(string target) {
        EnsureWindows();
        if (!CredDelete(target, CredentialTypeGeneric, 0) && Marshal.GetLastWin32Error() != ErrorNotFound) {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static void EnsureWindows() {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("OSM 凭据需要 Windows 凭据库。");
    }

    private static void ZeroMemory(IntPtr pointer, int length) {
        for (var i = 0; i < length; i++) Marshal.WriteByte(pointer, i, 0);
    }

    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);
}
