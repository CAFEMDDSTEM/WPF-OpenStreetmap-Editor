using System.IO;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public static class DocumentExportPathService {
    public static string CreateDefaultFileName(MapDocument document, string extension) {
        ArgumentNullException.ThrowIfNull(document);

        var baseName = Path.GetFileNameWithoutExtension(document.SourcePath ?? document.Name);
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "map";
        return $"{baseName}{extension}";
    }

    public static string? CreateSiblingPath(MapDocument document, string extension) {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(document.SourcePath)) return null;

        var directory = Path.GetDirectoryName(Path.GetFullPath(document.SourcePath));
        if (string.IsNullOrWhiteSpace(directory)) return null;

        var baseName = Path.GetFileNameWithoutExtension(document.SourcePath);
        if (string.IsNullOrWhiteSpace(baseName)) baseName = Path.GetFileNameWithoutExtension(document.Name);
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "map";
        return Path.Combine(directory, $"{baseName}{extension}");
    }

    public static string? CreateParentPath(MapDocument document) {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(document.SourcePath)) return null;

        var sourcePath = Path.GetFullPath(document.SourcePath);
        var directory = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrWhiteSpace(directory)) return null;

        var parent = Directory.GetParent(directory);
        return parent is null ? null : Path.Combine(parent.FullName, Path.GetFileName(sourcePath));
    }
}
