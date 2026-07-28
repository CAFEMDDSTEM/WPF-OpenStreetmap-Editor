using System.IO;

namespace WPF_OpenStreetmap_Editor.Plugins;

internal static class PythonInterpreterLocator {
    private const string ExecutableName = "python.exe";
    private const int MaximumRuntimeFileCount = 5000;
    private const long MaximumRuntimeBytes = 256L * 1024 * 1024;
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase) {
        "__pycache__",
        "site-packages",
        "test",
        "tests"
    };

    public static string Find() {
        foreach (var directory in EnumerateSearchDirectories(Environment.GetEnvironmentVariable("PATH"))) {
            string candidate;
            try {
                candidate = Path.GetFullPath(Path.Combine(directory, ExecutableName));
            } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
                continue;
            }

            if (File.Exists(candidate)) return candidate;
        }

        throw new InvalidOperationException(
            "Python process plugins require python.exe on PATH. Install Python 3.11+ or make python.exe discoverable.");
    }

    internal static IEnumerable<string> EnumerateSearchDirectories(string? pathVariable) {
        if (string.IsNullOrWhiteSpace(pathVariable)) yield break;

        foreach (var value in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
            var directory = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
            if (!string.IsNullOrWhiteSpace(directory)) yield return directory;
        }
    }

    internal static string StageRuntime(string interpreterPath, string sessionDirectory) {
        var runtimeRoot = Path.GetDirectoryName(Path.GetFullPath(interpreterPath)) ??
            throw new InvalidOperationException("Python interpreter path has no parent directory.");
        var destinationRoot = Path.Combine(sessionDirectory, "PythonRuntime");
        Directory.CreateDirectory(destinationRoot);

        var files = Directory.EnumerateFiles(runtimeRoot, "python*.dll", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(runtimeRoot, "python*.zip", SearchOption.TopDirectoryOnly))
            .Concat(Directory.EnumerateFiles(runtimeRoot, "python*._pth", SearchOption.TopDirectoryOnly))
            .Concat(Directory.EnumerateFiles(runtimeRoot, "vcruntime*.dll", SearchOption.TopDirectoryOnly))
            .Append(Path.GetFullPath(interpreterPath))
            .Concat(EnumerateRuntimeDirectory(Path.Combine(runtimeRoot, "DLLs")))
            .Concat(EnumerateRuntimeDirectory(Path.Combine(runtimeRoot, "Lib")))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var totalBytes = files.Sum(static path => new FileInfo(path).Length);
        if (files.Count > MaximumRuntimeFileCount || totalBytes > MaximumRuntimeBytes) {
            throw new InvalidDataException(
                $"Python runtime exceeds the sandbox staging limit of {MaximumRuntimeFileCount} files and " +
                $"{MaximumRuntimeBytes / 1024 / 1024} MB.");
        }

        foreach (var sourcePath in files) {
            var relativePath = Path.GetRelativePath(runtimeRoot, sourcePath);
            var destinationPath = Path.Combine(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: false);
        }

        return Path.Combine(destinationRoot, Path.GetRelativePath(runtimeRoot, interpreterPath));
    }

    private static IEnumerable<string> EnumerateRuntimeDirectory(string root) {
        if (!Directory.Exists(root)) yield break;

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0) {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory)) {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) {
                    throw new InvalidDataException("Python runtime paths cannot contain reparse points.");
                }
                if ((attributes & FileAttributes.Directory) != 0) {
                    if (!ExcludedDirectoryNames.Contains(Path.GetFileName(entry))) pending.Push(entry);
                } else if (!string.Equals(Path.GetExtension(entry), ".pyc", StringComparison.OrdinalIgnoreCase)) {
                    yield return entry;
                }
            }
        }
    }
}
