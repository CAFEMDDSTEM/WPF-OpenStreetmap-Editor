using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace WPF_OpenStreetmap_Editor.Services;

public static class Logger {
    private static readonly object LogLock = new();
    private static readonly Regex SensitiveQueryValueRegex = new(
        @"(?i)([?&](?:access_token|token|key|api_key|subscription-key)=)[^&#\s]+",
        RegexOptions.Compiled);
    private static readonly Regex SensitiveJsonValueRegex = new(
        "(?i)(\"(?:accessToken|access_token|token|apiKey|api_key|subscription-key)\"\\s*:\\s*\")[^\"]*(\")",
        RegexOptions.Compiled);
    private static readonly Regex SensitiveHeaderValueRegex = new(
        @"(?i)(\b(?:authorization|x-api-key)\s*[:=]\s*(?:Bearer\s+)?)[^\s,;]+",
        RegexOptions.Compiled);

    public static void Log(string url, string status) {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {status} {GetRequestOrigin(url)}";
        line = RedactSensitiveData(line);
        Debug.WriteLine(line);
        AppendToFile(AppPaths.TileRequestsLogFile, line);
    }

    public static void Startup(string message) {
        var line = RedactSensitiveData($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
        Debug.WriteLine(line);
        AppendToFile(AppPaths.StartupLogFile, line);
    }

    public static void Error(string message, Exception? ex = null) {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {message}";
        if (ex is not null) {
            line += $" | {ex.GetType().Name}: {ex.Message}";
        }
        line = RedactSensitiveData(line);
        Debug.WriteLine(line);
        AppendToFile(AppPaths.TileRequestsLogFile, line);
    }

    public static string RedactSensitiveData(string value) {
        if (string.IsNullOrEmpty(value)) return value;

        var redacted = SensitiveQueryValueRegex.Replace(
            value,
            static match => match.Groups[1].Value + "***");
        redacted = SensitiveJsonValueRegex.Replace(
            redacted,
            static match => match.Groups[1].Value + "***" + match.Groups[2].Value);
        return SensitiveHeaderValueRegex.Replace(
            redacted,
            static match => match.Groups[1].Value + "***");
    }

    private static string GetRequestOrigin(string url) {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority)
            : RedactSensitiveData(url);
    }

    private static void AppendToFile(string path, string line) {
        lock (LogLock) {
            try {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) {
                    Directory.CreateDirectory(dir);
                }

                File.AppendAllText(path, line + Environment.NewLine);
            } catch {
            }
        }
    }
}
