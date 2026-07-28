using System.IO;
using System.IO.Compression;

namespace WPF_OpenStreetmap_Editor.Plugins;

public sealed record PluginInstallCandidate(PluginManifest Manifest, bool RequiresCodeExecutionConsent);

public sealed record PluginInstallResult(PluginManifest Manifest, string InstallDirectory);

public sealed class PluginConsentRequiredException : InvalidOperationException {
    public PluginConsentRequiredException(string pluginName)
        : base($"Plugin '{pluginName}' contains executable code and requires explicit consent.") {
    }
}

public sealed class PluginInstaller {
    private readonly string _pluginsDirectory;
    private readonly PluginManifestReader _manifestReader;
    private readonly PluginTrustStore _trustStore;

    public PluginInstaller(
        string pluginsDirectory,
        PluginManifestReader manifestReader,
        PluginTrustStore trustStore) {
        _pluginsDirectory = Path.GetFullPath(pluginsDirectory);
        _manifestReader = manifestReader;
        _trustStore = trustStore;
    }

    public PluginInstallCandidate Inspect(string sourcePath) {
        using var package = PreparePackage(sourcePath);
        var manifest = _manifestReader.Read(package.ManifestPath);
        return new PluginInstallCandidate(manifest, RequiresCodeExecutionConsent(manifest));
    }

    public PluginInstallResult Install(string sourcePath, bool allowCodeExecution) {
        using var package = PreparePackage(sourcePath);
        var manifest = _manifestReader.Read(package.ManifestPath);
        var requiresConsent = RequiresCodeExecutionConsent(manifest);
        if (requiresConsent && !allowCodeExecution) {
            throw new PluginConsentRequiredException(manifest.Name);
        }

        Directory.CreateDirectory(_pluginsDirectory);
        var targetDirectory = Path.Combine(_pluginsDirectory, manifest.Id);
        if (Directory.Exists(targetDirectory)) {
            throw new IOException($"Plugin '{manifest.Id}' is already installed.");
        }
        if (IsInsideDirectory(targetDirectory, package.PackageDirectory)) {
            throw new InvalidDataException("The plugin destination cannot be inside the source package.");
        }

        try {
            CopyPackage(package.PackageDirectory, targetDirectory);
            var installedManifestPath = Path.Combine(targetDirectory, PluginManifestReader.ManifestFileName);
            _manifestReader.Read(installedManifestPath);
            if (requiresConsent) {
                _trustStore.Trust(manifest.Id, PluginPackageFingerprint.Compute(targetDirectory));
            }
            return new PluginInstallResult(manifest, targetDirectory);
        } catch {
            if (Directory.Exists(targetDirectory)) {
                Directory.Delete(targetDirectory, recursive: true);
            }
            throw;
        }
    }

    private PreparedPackage PreparePackage(string sourcePath) {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath)) {
            throw new FileNotFoundException("Plugin package does not exist.", fullSourcePath);
        }

        if (string.Equals(Path.GetFileName(fullSourcePath), PluginManifestReader.ManifestFileName, StringComparison.OrdinalIgnoreCase)) {
            return new PreparedPackage(Path.GetDirectoryName(fullSourcePath)!, fullSourcePath, null);
        }

        var extension = Path.GetExtension(fullSourcePath);
        if (!string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".wosm-plugin", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".jar", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidDataException("Select plugin.json5, a .wosm-plugin package, a .zip package, or a .jar package.");
        }

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "WosmPluginInstall", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try {
            ExtractArchive(fullSourcePath, temporaryDirectory);
            var manifestPath = Path.Combine(temporaryDirectory, PluginManifestReader.ManifestFileName);
            if (!File.Exists(manifestPath)) {
                if (!string.Equals(extension, ".jar", StringComparison.OrdinalIgnoreCase)) {
                    throw new InvalidDataException("Plugin archives must contain plugin.json5 at the package root.");
                }

                Directory.Delete(temporaryDirectory, recursive: true);
                Directory.CreateDirectory(temporaryDirectory);
                JavaPluginPackageBuilder.CreatePackage(fullSourcePath, temporaryDirectory);
            }
            return new PreparedPackage(temporaryDirectory, manifestPath, temporaryDirectory);
        } catch {
            Directory.Delete(temporaryDirectory, recursive: true);
            throw;
        }
    }

    private static void ExtractArchive(string archivePath, string targetDirectory) {
        using var archive = ZipFile.OpenRead(archivePath);
        var totalBytes = 0L;
        var fileCount = 0;
        foreach (var entry in archive.Entries) {
            var destinationPath = Path.GetFullPath(Path.Combine(targetDirectory, entry.FullName));
            var relative = Path.GetRelativePath(targetDirectory, destinationPath);
            if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                Path.IsPathRooted(relative)) {
                throw new InvalidDataException("Plugin archive contains a path outside the package root.");
            }

            if (string.IsNullOrEmpty(entry.Name)) {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            fileCount++;
            totalBytes = checked(totalBytes + entry.Length);
            if (fileCount > PluginPackageFiles.MaximumFileCount ||
                totalBytes > PluginPackageFiles.MaximumTotalBytes) {
                throw new InvalidDataException(
                    $"Plugin packages are limited to {PluginPackageFiles.MaximumFileCount} files and " +
                    $"{PluginPackageFiles.MaximumTotalBytes / 1024 / 1024} MB.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: false);
        }
    }

    private static void CopyPackage(string sourceDirectory, string targetDirectory) {
        Directory.CreateDirectory(targetDirectory);
        foreach (var file in PluginPackageFiles.Enumerate(sourceDirectory)) {
            var destination = Path.Combine(targetDirectory, Path.GetRelativePath(sourceDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: false);
        }
    }

    private static bool IsInsideDirectory(string path, string possibleParent) {
        var relative = Path.GetRelativePath(Path.GetFullPath(possibleParent), Path.GetFullPath(path));
        return relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative);
    }

    private static bool RequiresCodeExecutionConsent(PluginManifest manifest) {
        return PluginManifestReader.ParseKind(manifest.Kind) == PluginKind.Native;
    }

    private sealed class PreparedPackage(
        string packageDirectory,
        string manifestPath,
        string? temporaryDirectory) : IDisposable {
        public string PackageDirectory { get; } = packageDirectory;
        public string ManifestPath { get; } = manifestPath;

        public void Dispose() {
            if (temporaryDirectory is not null && Directory.Exists(temporaryDirectory)) {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }
}
