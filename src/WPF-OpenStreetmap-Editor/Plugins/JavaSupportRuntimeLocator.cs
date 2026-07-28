using System.IO;

namespace WPF_OpenStreetmap_Editor.Plugins;

internal static class JavaSupportRuntimeLocator {
    public const string BridgeExecutableFileName = "wosm-java-plugin-bridge.exe";
    public const string BridgeRuntimeDirectoryName = "java-support";
    public const string BridgeExecutableRelativePath = BridgeRuntimeDirectoryName + "/" + BridgeExecutableFileName;
    private const string BridgeDirectoryOverrideEnvironmentVariable = "WOSM_JAVA_BRIDGE_DIR";
    private const string BridgeOverrideEnvironmentVariable = "WOSM_JAVA_BRIDGE_EXE";

    public static string FindBridgeRuntimeDirectory() {
        var overrideDirectory = Environment.GetEnvironmentVariable(BridgeDirectoryOverrideEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overrideDirectory) &&
            File.Exists(Path.Combine(overrideDirectory, BridgeExecutableFileName))) {
            return Path.GetFullPath(overrideDirectory);
        }

        var overridePath = Environment.GetEnvironmentVariable(BridgeOverrideEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath)) {
            return Path.GetDirectoryName(Path.GetFullPath(overridePath))!;
        }

        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[] {
            Path.Combine(baseDirectory, "JavaSupport"),
            Path.Combine(baseDirectory, "JavaSupport", "wosm-java-plugin-bridge"),
            baseDirectory
        };
        foreach (var candidate in candidates) {
            if (File.Exists(Path.Combine(candidate, BridgeExecutableFileName))) {
                return Path.GetFullPath(candidate);
            }
        }

        throw new FileNotFoundException(
            "The Java/JOSM support bridge was not found. Build it with scripts/build-java-plugin-support.ps1 " +
            $"or set {BridgeDirectoryOverrideEnvironmentVariable} to the app-image directory.");
    }
}
