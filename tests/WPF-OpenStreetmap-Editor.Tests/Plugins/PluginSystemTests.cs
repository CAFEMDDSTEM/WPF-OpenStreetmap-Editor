using System.Globalization;
using System.IO.Compression;
using WPF_OpenStreetmap_Editor.Plugins;

namespace WPF_OpenStreetmap_Editor.Tests.Plugins;

public class PluginSystemTests {
    [Fact]
    public void ManifestReader_ParsesJson5Addon() {
        using var testDirectory = new TestDirectory();
        var manifestPath = testDirectory.WriteManifest(AddonManifest);

        var manifest = new PluginManifestReader().Read(manifestPath);

        Assert.Equal("org.example.addon", manifest.Id);
        Assert.Equal("icon.png", manifest.Icon);
        Assert.Equal("description.md", manifest.DescriptionFile);
        Assert.Equal("# Example plugin", PluginManifestReader.ReadDescription(manifest, testDirectory.Path));
        Assert.Equal(PluginKind.Addon, PluginManifestReader.ParseKind(manifest.Kind));
        Assert.Single(manifest.Contributions.Menus);
        Assert.Equal("Hello", manifest.Contributions.Menus[0].Label);
        var toolbarItem = Assert.Single(manifest.Contributions.Toolbar);
        Assert.Equal("main", toolbarItem.Location);
        Assert.Equal("Download", toolbarItem.Icon);
        Assert.Equal("Download data", toolbarItem.ToolTip);
        Assert.Equal("hello", toolbarItem.Command);
        Assert.Equal(20, toolbarItem.Order);
        Assert.Equal("world", manifest.Contributions.Commands[0].Actions[0]
            .Arguments.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("secondary", "Download", "Download data", "hello", 0, "location")]
    [InlineData("main", "NotAnIcon", "Download data", "hello", 0, "icon")]
    [InlineData("main", "123", "Download data", "hello", 0, "icon")]
    [InlineData("main", "Download", "", "hello", 0, "tooltip")]
    [InlineData("main", "Download", "Download data", "missing", 0, "unknown command")]
    [InlineData("main", "Download", "Download data", "hello", 10001, "order")]
    public void ManifestReader_RejectsInvalidToolbarContribution(
        string location,
        string icon,
        string toolTip,
        string command,
        int order,
        string expectedMessage) {
        using var testDirectory = new TestDirectory();
        var manifest = AddonManifest
            .Replace("location: 'main'", $"location: '{location}'", StringComparison.Ordinal)
            .Replace("icon: 'Download'", $"icon: '{icon}'", StringComparison.Ordinal)
            .Replace("tooltip: 'Download data'", $"tooltip: '{toolTip}'", StringComparison.Ordinal)
            .Replace("command: 'hello', order: 20", $"command: '{command}', order: {order}", StringComparison.Ordinal);
        var manifestPath = testDirectory.WriteManifest(manifest);

        var exception = Assert.Throws<PluginManifestException>(() =>
            new PluginManifestReader().Read(manifestPath));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("icon.png", "icon")]
    [InlineData("description.md", "description")]
    public void ManifestReader_RejectsMissingRequiredPackageFile(string fileName, string expectedMessage) {
        using var testDirectory = new TestDirectory();
        var manifestPath = testDirectory.WriteManifest(AddonManifest);
        File.Delete(Path.Combine(testDirectory.Path, fileName));

        var exception = Assert.Throws<PluginManifestException>(() =>
            new PluginManifestReader().Read(manifestPath));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManifestReader_RejectsEntryOutsidePackage() {
        using var testDirectory = new TestDirectory();
        var manifestPath = testDirectory.WriteManifest(ProcessManifest("../bridge.exe"));

        var exception = Assert.Throws<PluginManifestException>(() =>
            new PluginManifestReader().Read(manifestPath));

        Assert.Contains("escapes", exception.Message);
    }

    [Fact]
    public void Installer_RequiresExplicitConsentForNativePlugin() {
        using var source = new TestDirectory();
        using var destination = new TestDirectory();
        const string kind = "native";
        const string entry = "plugin.dll";
        source.WriteFile(entry, "not executed by this test");
        var manifestPath = source.WriteManifest(ExecutableManifest(kind, entry));
        var installer = CreateInstaller(destination.Path);

        var candidate = installer.Inspect(manifestPath);

        Assert.True(candidate.RequiresCodeExecutionConsent);
        Assert.Throws<PluginConsentRequiredException>(() => installer.Install(manifestPath, false));
        Assert.False(Directory.Exists(Path.Combine(destination.Path, "Plugins", "org.example.executable")));
    }

    [Fact]
    public void Installer_InstallsSandboxedProcessWithoutNativeConsent() {
        using var source = new TestDirectory();
        using var destination = new TestDirectory();
        source.WriteFile("bridge.exe", "not executed by this test");
        var manifestPath = source.WriteManifest(ExecutableManifest("process", "bridge.exe"));
        var installer = CreateInstaller(destination.Path);

        var candidate = installer.Inspect(manifestPath);
        var result = installer.Install(manifestPath, allowCodeExecution: false);

        Assert.False(candidate.RequiresCodeExecutionConsent);
        Assert.Equal("org.example.executable", result.Manifest.Id);
    }

    [Fact]
    public void Installer_InstallsAddonWithoutExecutableConsent() {
        using var source = new TestDirectory();
        using var destination = new TestDirectory();
        var manifestPath = source.WriteManifest(AddonManifest);
        var installer = CreateInstaller(destination.Path);

        var result = installer.Install(manifestPath, false);

        Assert.Equal("org.example.addon", result.Manifest.Id);
        Assert.True(File.Exists(Path.Combine(result.InstallDirectory, PluginManifestReader.ManifestFileName)));
        Assert.True(File.Exists(Path.Combine(result.InstallDirectory, "icon.png")));
        Assert.True(File.Exists(Path.Combine(result.InstallDirectory, "description.md")));
    }

    [Fact]
    public void Installer_RejectsArchivePathTraversal() {
        using var source = new TestDirectory();
        using var destination = new TestDirectory();
        var archivePath = Path.Combine(source.Path, "unsafe.wosm-plugin");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create)) {
            var entry = archive.CreateEntry("../outside.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("outside");
        }
        var installer = CreateInstaller(destination.Path);

        Assert.Throws<InvalidDataException>(() => installer.Inspect(archivePath));
        Assert.False(File.Exists(Path.Combine(source.Path, "outside.txt")));
    }

    [Fact]
    public void PackageFingerprint_ChangesWhenAnyPackageFileChanges() {
        using var testDirectory = new TestDirectory();
        testDirectory.WriteManifest(AddonManifest);
        var contentPath = testDirectory.WriteFile("content.txt", "first");
        var first = PluginPackageFingerprint.Compute(testDirectory.Path);

        File.WriteAllText(contentPath, "second");
        var second = PluginPackageFingerprint.Compute(testDirectory.Path);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void PackageFingerprint_LengthPrefixesPreventFileBoundaryAmbiguity() {
        using var singleFilePackage = new TestDirectory();
        using var twoFilePackage = new TestDirectory();
        File.WriteAllBytes(Path.Combine(singleFilePackage.Path, "a"), "x\0b\0y"u8.ToArray());
        File.WriteAllBytes(Path.Combine(twoFilePackage.Path, "a"), "x"u8.ToArray());
        File.WriteAllBytes(Path.Combine(twoFilePackage.Path, "b"), "y"u8.ToArray());

        var singleFileFingerprint = PluginPackageFingerprint.Compute(singleFilePackage.Path);
        var twoFileFingerprint = PluginPackageFingerprint.Compute(twoFilePackage.Path);

        Assert.NotEqual(singleFileFingerprint, twoFileFingerprint);
    }

    [Fact]
    public async Task PluginHost_ReportsInvalidSandboxedProcessPlugin() {
        using var testDirectory = new TestDirectory();
        var packageDirectory = Path.Combine(
            testDirectory.Path,
            "Plugins",
            "org.example.bridge");
        Directory.CreateDirectory(packageDirectory);
        File.WriteAllText(Path.Combine(packageDirectory, "bridge.exe"), "not an executable");
        testDirectory.WriteRequiredAssets(packageDirectory);
        File.WriteAllText(
            Path.Combine(packageDirectory, PluginManifestReader.ManifestFileName),
            ProcessManifest("bridge.exe"));
        await using var host = new PluginHost(
            Path.Combine(testDirectory.Path, "Plugins"),
            Path.Combine(testDirectory.Path, "state.json"));

        await host.ReloadAsync();

        Assert.Equal(
            PluginLoadStatus.Failed,
            Assert.Single(host.Plugins, plugin => plugin.Id == "org.example.bridge").Status);
        Assert.DoesNotContain(
            host.MenuContributions,
            contribution => contribution.PluginId == "org.example.bridge");
    }

    [Fact]
    public async Task PluginHost_LoadsAddonAndExecutesContributedCommand() {
        using var testDirectory = new TestDirectory();
        var pluginsDirectory = Path.Combine(testDirectory.Path, "Plugins");
        var packageDirectory = Path.Combine(pluginsDirectory, "org.example.addon");
        Directory.CreateDirectory(packageDirectory);
        testDirectory.WriteRequiredAssets(packageDirectory);
        File.WriteAllText(Path.Combine(packageDirectory, PluginManifestReader.ManifestFileName), AddonManifest);
        await using var host = new PluginHost(pluginsDirectory, Path.Combine(testDirectory.Path, "state.json"));

        await host.ReloadAsync();
        var result = await host.ExecuteCommandAsync("org.example.addon", "hello");

        Assert.Equal(
            PluginLoadStatus.Loaded,
            Assert.Single(host.Plugins, plugin => plugin.Id == "org.example.addon").Status);
        Assert.Single(
            host.MenuContributions,
            contribution => contribution.PluginId == "org.example.addon");
        var toolbarContribution = Assert.Single(
            host.ToolbarContributions,
            contribution => contribution.PluginId == "org.example.addon");
        Assert.Equal("Download", toolbarContribution.Toolbar.Icon);
        Assert.Equal("hello", toolbarContribution.Toolbar.Command);
        Assert.Single(result.Actions);
        Assert.Equal(PluginActionTypes.ShowMessage, result.Actions[0].Type);
        Assert.Equal(
            "# Example plugin",
            Assert.Single(host.Plugins, plugin => plugin.Id == "org.example.addon").Description);
    }

    [Fact]
    public async Task PluginHost_ExchangesJsonRpcWithProcessPlugin() {
        using var source = new TestDirectory();
        using var destination = new TestDirectory();
        CopyCommandInterpreter(source);
        source.WriteFile("bridge.cmd", """
            @echo off
            set /p request=
            echo {"jsonrpc":"2.0","id":1,"result":{"actions":[]}}
            set /p request=
            echo {"jsonrpc":"2.0","id":2,"result":{"actions":[{"type":"showMessage","arguments":{"message":"hook"}}]}}
            set /p request=
            echo {"jsonrpc":"2.0","id":3,"result":{"actions":[{"type":"showMessage","arguments":{"message":"bridge"}}]}}
            set /p request=
            echo {"jsonrpc":"2.0","id":4,"result":{"actions":[]}}
            """);
        var manifestPath = source.WriteManifest("""
            {
              schemaVersion: 1,
              id: 'org.example.process-test',
              name: 'Process test bridge',
              version: '1.0.0',
              icon: 'icon.png',
              descriptionFile: 'description.md',
              kind: 'process',
              hooks: ['mainWindow.loaded'],
              runtime: {
                entry: 'cmd.exe',
                arguments: ['/D', '/Q', '/C', 'bridge.cmd'],
                hostActions: ['showMessage'],
                timeoutMilliseconds: 5000,
              },
              contributions: {
                menus: [
                  { location: 'tools', label: 'Bridge', command: 'bridge.test' },
                ],
              },
            }
            """);
        var pluginsDirectory = Path.Combine(destination.Path, "Plugins");
        var statePath = Path.Combine(destination.Path, "state.json");
        var reader = new PluginManifestReader();
        var installer = new PluginInstaller(
            pluginsDirectory,
            reader,
            new PluginTrustStore(statePath));
        installer.Install(manifestPath, allowCodeExecution: false);
        await using var host = new PluginHost(pluginsDirectory, statePath);

        await host.ReloadAsync();
        var hookActions = await host.PublishAsync(PluginHooks.MainWindowLoaded);
        var result = await host.ExecuteCommandAsync("org.example.process-test", "bridge.test");

        Assert.Equal(
            PluginLoadStatus.Loaded,
            Assert.Single(host.Plugins, plugin => plugin.Id == "org.example.process-test").Status);
        var hookAction = Assert.Single(hookActions);
        Assert.Equal("org.example.process-test", hookAction.PluginId);
        Assert.Equal("Process test bridge", hookAction.PluginName);
        Assert.Equal("hook", hookAction.Action.Arguments.GetProperty("message").GetString());
        var action = Assert.Single(result.Actions);
        Assert.Equal("bridge", action.Arguments.GetProperty("message").GetString());
    }

    [Fact]
    public async Task PluginHost_SandboxedProcessCannotReadFileOutsidePackage() {
        using var source = new TestDirectory();
        using var destination = new TestDirectory();
        var outsideSecretPath = destination.WriteFile("outside-secret.txt", "must-not-be-readable");
        CopyCommandInterpreter(source);
        source.WriteFile("bridge.cmd", """
            @echo off
            set /p request=
            echo {"jsonrpc":"2.0","id":1,"result":{"actions":[]}}
            set /p request=
            set leaked=
            for /f "usebackq delims=" %%a in ("%~1") do set leaked=%%a
            if defined leaked (
              echo {"jsonrpc":"2.0","id":2,"result":{"actions":[{"type":"showMessage","arguments":{"message":"leaked"}}]}}
            ) else (
              echo {"jsonrpc":"2.0","id":2,"result":{"actions":[{"type":"showMessage","arguments":{"message":"isolated"}}]}}
            )
            set /p request=
            echo {"jsonrpc":"2.0","id":3,"result":{"actions":[]}}
            """);
        var manifestPath = source.WriteManifest($$"""
            {
              schemaVersion: 1,
              id: 'org.example.sandbox-test',
              name: 'Sandbox test bridge',
              version: '1.0.0',
              icon: 'icon.png',
              descriptionFile: 'description.md',
              kind: 'process',
              runtime: {
                entry: 'cmd.exe',
                arguments: ['/D', '/Q', '/C', 'bridge.cmd', '{{outsideSecretPath.Replace(@"\", @"\\")}}'],
                hostActions: ['showMessage'],
                timeoutMilliseconds: 5000,
              },
            }
            """);
        var pluginsDirectory = Path.Combine(destination.Path, "Plugins");
        var statePath = Path.Combine(destination.Path, "state.json");
        var installer = new PluginInstaller(
            pluginsDirectory,
            new PluginManifestReader(),
            new PluginTrustStore(statePath));
        installer.Install(manifestPath, allowCodeExecution: false);
        await using var host = new PluginHost(pluginsDirectory, statePath);

        await host.ReloadAsync();
        var result = await host.ExecuteCommandAsync("org.example.sandbox-test", "sandbox.test");

        var action = Assert.Single(result.Actions);
        Assert.Equal("isolated", action.Arguments.GetProperty("message").GetString());
    }

    [Fact]
    public async Task PluginHost_PreservesProcessPluginLoadError() {
        using var source = new TestDirectory();
        using var destination = new TestDirectory();
        CopyCommandInterpreter(source);
        source.WriteFile("bridge.cmd", """
            @echo off
            set /p request=
            echo {"jsonrpc":"2.0","id":"invalid","result":{"actions":[]}}
            """);
        var manifestPath = source.WriteManifest("""
            {
              schemaVersion: 1,
              id: 'org.example.invalid-response',
              name: 'Invalid response bridge',
              version: '1.0.0',
              icon: 'icon.png',
              descriptionFile: 'description.md',
              kind: 'process',
              runtime: {
                entry: 'cmd.exe',
                arguments: ['/D', '/Q', '/C', 'bridge.cmd'],
                timeoutMilliseconds: 5000,
              },
            }
            """);
        var pluginsDirectory = Path.Combine(destination.Path, "Plugins");
        var statePath = Path.Combine(destination.Path, "state.json");
        var installer = new PluginInstaller(
            pluginsDirectory,
            new PluginManifestReader(),
            new PluginTrustStore(statePath));
        installer.Install(manifestPath, allowCodeExecution: false);
        await using var host = new PluginHost(pluginsDirectory, statePath);

        await host.ReloadAsync();

        var descriptor = Assert.Single(
            host.Plugins,
            plugin => plugin.Id == "org.example.invalid-response");
        Assert.Equal(PluginLoadStatus.Failed, descriptor.Status);
        Assert.Contains("response id must be an integer", descriptor.Error);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.ExecuteCommandAsync("org.example.invalid-response", "unused"));
        Assert.Contains(descriptor.Error, exception.Message);
    }

    private static PluginInstaller CreateInstaller(string root) {
        var reader = new PluginManifestReader();
        var trustStore = new PluginTrustStore(Path.Combine(root, "state.json"));
        return new PluginInstaller(Path.Combine(root, "Plugins"), reader, trustStore);
    }

    private static void CopyCommandInterpreter(TestDirectory destination) {
        const string commandFileName = "cmd.exe";
        File.Copy(
            Path.Combine(Environment.SystemDirectory, commandFileName),
            Path.Combine(destination.Path, commandFileName));

        for (var culture = CultureInfo.CurrentUICulture;
             culture != CultureInfo.InvariantCulture;
             culture = culture.Parent) {
            var muiPath = Path.Combine(
                Environment.SystemDirectory,
                culture.Name,
                commandFileName + ".mui");
            if (!File.Exists(muiPath)) continue;

            var destinationPath = Path.Combine(
                destination.Path,
                culture.Name,
                commandFileName + ".mui");
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(muiPath, destinationPath);
            break;
        }
    }

    private static string ProcessManifest(string entry) => $$"""
        {
          schemaVersion: 1,
          id: 'org.example.bridge',
          name: 'Example bridge',
          version: '1.0.0',
          icon: 'icon.png',
          descriptionFile: 'description.md',
          kind: 'process',
          runtime: { entry: '{{entry}}' },
        }
        """;

    private static string ExecutableManifest(string kind, string entry) => $$"""
        {
          schemaVersion: 1,
          id: 'org.example.executable',
          name: 'Example executable',
          version: '1.0.0',
          icon: 'icon.png',
          descriptionFile: 'description.md',
          kind: '{{kind}}',
          runtime: { entry: '{{entry}}' },
        }
        """;

    private const string AddonManifest = """
        {
          // JSON5 comments, single quotes, unquoted keys, and trailing commas are supported.
          schemaVersion: 1,
          id: 'org.example.addon',
          name: 'Example addon',
          version: '1.0.0',
          icon: 'icon.png',
          descriptionFile: 'description.md',
          kind: 'addon',
          contributions: {
            menus: [
              { location: 'tools', label: 'Hello', command: 'hello' },
            ],
            toolbar: [
              {
                location: 'main',
                icon: 'Download',
                tooltip: 'Download data',
                command: 'hello', order: 20,
              },
            ],
            commands: [
              {
                id: 'hello',
                actions: [
                  { type: 'showMessage', arguments: { message: 'world' } },
                ],
              },
            ],
          },
        }
        """;

    private sealed class TestDirectory : IDisposable {
        private static readonly byte[] IconBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

        public TestDirectory() {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "wpf-osm-editor-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string WriteManifest(string content) {
            WriteRequiredAssets(Path);
            return WriteFile(PluginManifestReader.ManifestFileName, content);
        }

        public void WriteRequiredAssets(string packageDirectory) {
            Directory.CreateDirectory(packageDirectory);
            File.WriteAllBytes(System.IO.Path.Combine(packageDirectory, "icon.png"), IconBytes);
            File.WriteAllText(
                System.IO.Path.Combine(packageDirectory, "description.md"),
                "# Example plugin");
        }

        public string WriteFile(string relativePath, string content) {
            var path = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose() {
            if (Directory.Exists(Path)) {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
