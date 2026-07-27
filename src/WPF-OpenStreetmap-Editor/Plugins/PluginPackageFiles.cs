using System.IO;

namespace WPF_OpenStreetmap_Editor.Plugins;

internal static class PluginPackageFiles {
    public const int MaximumFileCount = 10000;
    public const long MaximumTotalBytes = 512L * 1024 * 1024;

    public static IReadOnlyList<string> Enumerate(string packageDirectory) {
        var packageRoot = Path.GetFullPath(packageDirectory);
        var pendingDirectories = new Stack<string>();
        var files = new List<string>();
        var totalBytes = 0L;
        pendingDirectories.Push(packageRoot);

        while (pendingDirectories.Count > 0) {
            var directory = pendingDirectories.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory)) {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) {
                    throw new InvalidDataException(
                        "Plugin packages cannot contain symbolic links or reparse points.");
                }

                if ((attributes & FileAttributes.Directory) != 0) {
                    pendingDirectories.Push(entry);
                    continue;
                }

                files.Add(entry);
                totalBytes = checked(totalBytes + new FileInfo(entry).Length);
                if (files.Count > MaximumFileCount || totalBytes > MaximumTotalBytes) {
                    throw new InvalidDataException(
                        $"Plugin packages are limited to {MaximumFileCount} files and " +
                        $"{MaximumTotalBytes / 1024 / 1024} MB.");
                }
            }
        }

        return files;
    }
}
