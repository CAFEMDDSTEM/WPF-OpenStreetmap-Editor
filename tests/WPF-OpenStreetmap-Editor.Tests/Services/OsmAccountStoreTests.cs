using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class OsmAccountStoreTests {
    [Fact]
    public void SaveAccount_KeepsTokenOutOfMetadataAndSupportsActiveSwitching() {
        var root = CreateTestDirectory();
        var path = Path.Combine(root, "accounts.json");
        var credentials = new MemoryCredentialStore();
        try {
            var store = new OsmAccountStore(path, credentials);
            var first = new OsmAccount { Id = "first", DisplayName = "First", IsActive = true };
            var second = new OsmAccount { Id = "second", DisplayName = "Second" };

            store.SaveAccount(first, "secret-token");
            store.SaveAccount(second, "other-token");
            store.SetActive(second.Id);

            Assert.Equal("Second", store.GetActive()!.DisplayName);
            Assert.Equal("secret-token", store.GetAccessToken(first));
            Assert.Equal("other-token", store.GetCredential(second)!.Secret);
            Assert.DoesNotContain("secret-token", File.ReadAllText(path), StringComparison.Ordinal);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void SaveAccount_StoresBasicPasswordAsCredentialOnly() {
        var root = CreateTestDirectory();
        var path = Path.Combine(root, "accounts.json");
        var credentials = new MemoryCredentialStore();
        try {
            var store = new OsmAccountStore(path, credentials);
            var account = new OsmAccount {
                Id = "basic",
                DisplayName = "Alice",
                AuthenticationMethod = OsmAuthenticationMethod.BasicPassword,
                UserName = "alice",
                IsActive = true
            };

            store.SaveAccount(account, " password with space ");

            var active = store.GetActive()!;
            Assert.Equal(OsmAuthenticationMethod.BasicPassword, active.AuthenticationMethod);
            Assert.Equal("alice", active.UserName);
            Assert.Equal(" password with space ", store.GetCredential(active)!.Secret);
            Assert.DoesNotContain("password with space", File.ReadAllText(path), StringComparison.Ordinal);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void SaveAccount_ClearsCredentialWhenAuthenticationShapeChangesWithoutNewSecret() {
        var root = CreateTestDirectory();
        var path = Path.Combine(root, "accounts.json");
        var credentials = new MemoryCredentialStore();
        try {
            var store = new OsmAccountStore(path, credentials);
            store.SaveAccount(new OsmAccount { Id = "same", DisplayName = "Alice", IsActive = true }, "oauth-token");

            store.SaveAccount(new OsmAccount {
                Id = "same",
                DisplayName = "Alice",
                AuthenticationMethod = OsmAuthenticationMethod.BasicPassword,
                UserName = "alice",
                IsActive = true
            }, null);

            Assert.False(store.HasCredential(store.GetActive()!));
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void Delete_RemovesCredentialAndActivatesRemainingAccount() {
        var root = CreateTestDirectory();
        var path = Path.Combine(root, "accounts.json");
        var credentials = new MemoryCredentialStore();
        try {
            var store = new OsmAccountStore(path, credentials);
            store.SaveAccount(new OsmAccount { Id = "first", DisplayName = "First" }, "first-token");
            store.SaveAccount(new OsmAccount { Id = "second", DisplayName = "Second" }, "second-token");

            store.Delete("first");

            Assert.Equal("second", store.GetActive()!.Id);
            Assert.Null(credentials.Read("WPF-OpenStreetmap-Editor/OSM/first"));
        } finally {
            DeleteTestDirectory(root);
        }
    }

    private static string CreateTestDirectory() {
        var path = Path.Combine(Path.GetTempPath(), "wpf-osm-editor-account-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTestDirectory(string path) {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private sealed class MemoryCredentialStore : ICredentialStore {
        private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

        public void Write(string target, string userName, string secret) => _secrets[target] = secret;

        public string? Read(string target) => _secrets.GetValueOrDefault(target);

        public void Delete(string target) => _secrets.Remove(target);
    }
}
