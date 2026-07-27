using System;
using System.Diagnostics;
using System.IO;

namespace WPF_OpenStreetmap_Editor.Services;

public static class Logger {
    private static readonly string LogPath = AppPaths.TileRequestsLogFile;

    static Logger() {
        try {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        } catch {

        }
    }

    public static void Log(string url, string status) {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {status} {url}";
        Debug.WriteLine(line);
        AppendToFile(line);
    }

    public static void Error(string message, Exception? ex = null) {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {message}";
        if (ex != null) {
            line += $" | {ex.GetType().Name}: {ex.Message}";
        }
        Debug.WriteLine(line);
        AppendToFile(line);
    }

    private static readonly object LogLock = new();

    private static void AppendToFile(string line) {
        lock (LogLock) {
            try {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            } catch {
            }
        }
    }
}
