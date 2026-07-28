using System.IO;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public static class DocumentBackupService {
    private static readonly HashSet<SpatialFileFormat> SaveableFormats = [
        SpatialFileFormat.OsmXml,
        SpatialFileFormat.GeoJson,
        SpatialFileFormat.Gml,
        SpatialFileFormat.Kml,
        SpatialFileFormat.Gpx
    ];

    public static async Task<string> SaveAutosaveAsync(
        MapDocument document,
        int filesPerLayer,
        CancellationToken ct = default) {
        var path = CreateAutosavePath(document, filesPerLayer);
        await SpatialDataService.WriteSnapshotAsync(document, path, ct).ConfigureAwait(false);
        return path;
    }

    public static async Task<IReadOnlyList<string>> SaveDirtyAutosavesAsync(
        MapDocument document,
        int filesPerLayer,
        CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(document);

        var snapshots = document.DataLayers
            .Where(static layer => layer.IsDirty)
            .Select(layer => document.CreateSnapshot(layer))
            .Select(snapshot => (Snapshot: snapshot, Path: CreateAutosavePath(snapshot, filesPerLayer)))
            .ToList();
        foreach (var item in snapshots) {
            await SpatialDataService.WriteSnapshotAsync(item.Snapshot, item.Path, ct).ConfigureAwait(false);
        }
        return snapshots.Select(static item => item.Path).ToList();
    }

    public static Task<string?> SaveKeepBackupAsync(string destinationPath, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(destinationPath)) return Task.FromResult<string?>(null);

        var backupPath = CreateKeepBackupPath(destinationPath);
        AtomicFile.Write(backupPath, temporaryPath =>
            File.Copy(destinationPath, temporaryPath, overwrite: false));
        return Task.FromResult<string?>(backupPath);
    }

    internal static string CreateAutosavePath(MapDocument document, int filesPerLayer, DateTimeOffset? now = null) {
        var count = Math.Clamp(
            filesPerLayer,
            AppSettings.MinAutosaveFilesPerLayer,
            AppSettings.MaxAutosaveFilesPerLayer);
        var slot = count == 1 ? 0 : (now ?? DateTimeOffset.Now).ToUnixTimeSeconds() / 60 % count;
        var fileName = CreateAutosaveFileName(document, (int)slot);
        return Path.Combine(AppPaths.DocumentBackupsDirectory, fileName);
    }

    internal static string CreateKeepBackupPath(string destinationPath) {
        var directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath)) ?? AppPaths.DocumentBackupsDirectory;
        var name = Path.GetFileName(destinationPath);
        return Path.Combine(directory, name + "~");
    }

    private static string CreateAutosaveFileName(MapDocument document, int slot) {
        var baseName = Path.GetFileNameWithoutExtension(document.SourcePath);
        if (string.IsNullOrWhiteSpace(baseName)) {
            baseName = Path.GetFileNameWithoutExtension(document.Name);
        }
        if (string.IsNullOrWhiteSpace(baseName)) {
            baseName = "map";
        }

        var extension = GetAutosaveExtension(document);
        var layerIdentifier = SanitizeFileName(document.ActiveDataLayer.Id);
        var layerSuffix = document.DataLayers.Count > 1
            ? $"-{layerIdentifier[..Math.Min(8, layerIdentifier.Length)]}"
            : "";
        var slotSuffix = slot <= 0 ? "" : $"-{slot + 1}";
        return $"{SanitizeFileName(baseName)}{layerSuffix}{slotSuffix}.autosave{extension}";
    }

    private static string GetAutosaveExtension(MapDocument document) {
        var format = document.SourceFormat;
        if (format is not null && SaveableFormats.Contains(format.Value)) {
            var extension = Path.GetExtension(document.SourcePath);
            if (!string.IsNullOrWhiteSpace(extension)) return extension;
            return format.Value == SpatialFileFormat.OsmXml ? ".osm" : $".{format.Value.ToString().ToLowerInvariant()}";
        }

        return document.Osm is not null ? ".osm" : ".geojson";
    }

    private static string SanitizeFileName(string value) {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = value.Trim()
            .Select(ch => invalid.Contains(ch) ? '_' : ch)
            .ToArray();
        var sanitized = new string(chars);
        return string.IsNullOrWhiteSpace(sanitized) ? "map" : sanitized;
    }
}
