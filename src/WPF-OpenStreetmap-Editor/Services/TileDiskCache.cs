using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WPF_OpenStreetmap_Editor.Services;

public static class TileDiskCache {
    public const long DefaultMaxBytes = 1024L * 1024 * 1024;
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromDays(30);
    private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromMinutes(10);
    private static long _nextMaintenanceUtcTicks;

    public static void ScheduleMaintenance(string cacheRoot) {
        var now = DateTime.UtcNow;
        var nextTicks = Volatile.Read(ref _nextMaintenanceUtcTicks);
        if (now.Ticks < nextTicks) return;

        var updatedTicks = now.Add(MaintenanceInterval).Ticks;
        if (Interlocked.CompareExchange(ref _nextMaintenanceUtcTicks, updatedTicks, nextTicks) != nextTicks) return;

        _ = Task.Run(() => Trim(cacheRoot, DefaultMaxBytes, DefaultMaxAge, now));
    }

    public static void Trim(string cacheRoot, long maxBytes, TimeSpan maxAge, DateTime? nowUtc = null) {
        if (maxBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (maxAge < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxAge));
        if (!Directory.Exists(cacheRoot)) return;

        try {
            var cutoff = (nowUtc ?? DateTime.UtcNow) - maxAge;
            var files = EnumerateFiles(cacheRoot);
            foreach (var file in files.Where(file => file.LastWriteTimeUtc < cutoff)) {
                TryDelete(file.FullName);
            }

            files = EnumerateFiles(cacheRoot);
            var totalBytes = files.Sum(static file => file.Length);
            foreach (var file in files.OrderBy(static file => file.LastWriteTimeUtc)) {
                if (totalBytes <= maxBytes) break;
                if (!TryDelete(file.FullName)) continue;

                totalBytes -= file.Length;
            }

            RemoveEmptyDirectories(cacheRoot);
        } catch (Exception ex) {
            Logger.Error("Failed to trim tile cache", ex);
        }
    }

    private static List<FileInfo> EnumerateFiles(string cacheRoot) {
        var options = new EnumerationOptions {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        return Directory.EnumerateFiles(cacheRoot, "*", options)
            .Select(static path => new FileInfo(path))
            .Where(static file => file.Exists)
            .ToList();
    }

    private static bool TryDelete(string path) {
        try {
            File.Delete(path);
            return true;
        } catch {
            return false;
        }
    }

    private static void RemoveEmptyDirectories(string cacheRoot) {
        var options = new EnumerationOptions {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (var directory in Directory.EnumerateDirectories(cacheRoot, "*", options)
                     .OrderByDescending(static path => path.Length)) {
            try {
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) {
                    Directory.Delete(directory);
                }
            } catch {
            }
        }
    }
}
