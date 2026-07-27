using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace WPF_OpenStreetmap_Editor.Services;

public static class WindowStartupService {
    private const double NormalWindowScreenRatio = 0.9;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] FullScreenArguments = [
        "--fullscreen",
        "--full-screen",
        "-fullscreen",
        "-full-screen",
        "/fullscreen",
        "/full-screen",
        "--maximized",
        "-maximized",
        "/maximized"
    ];

    public static void ApplyStartupState(Window window, IEnumerable<string>? commandLineArgs = null) {
        if (ShouldStartFullScreen(commandLineArgs ?? Environment.GetCommandLineArgs().Skip(1))) {
            ClearNormalWindowLimits(window);
            window.WindowState = WindowState.Maximized;
            return;
        }

        ApplyNormalWindowLimits(window);
    }

    public static void ApplyNormalWindowLimits(Window window) {
        var maxSize = GetNormalWindowMaxSize();
        window.MaxWidth = maxSize.Width;
        window.MaxHeight = maxSize.Height;

        if (!double.IsNaN(window.Width) && window.Width > maxSize.Width) {
            window.Width = maxSize.Width;
        }

        if (!double.IsNaN(window.Height) && window.Height > maxSize.Height) {
            window.Height = maxSize.Height;
        }
    }

    public static void ClearNormalWindowLimits(Window window) {
        window.MaxWidth = double.PositiveInfinity;
        window.MaxHeight = double.PositiveInfinity;
    }

    public static Size GetNormalWindowMaxSize() {
        var workArea = SystemParameters.WorkArea;
        return new Size(workArea.Width * NormalWindowScreenRatio, workArea.Height * NormalWindowScreenRatio);
    }

    public static bool ShouldStartFullScreen(IEnumerable<string> commandLineArgs) {
        return Load().WasFullScreen || commandLineArgs.Any(IsFullScreenArgument);
    }

    public static void Save(WindowState lastWindowState) {
        var state = new WindowStartupState { WasFullScreen = lastWindowState == WindowState.Maximized };
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.WindowStateFile)!);
        File.WriteAllText(AppPaths.WindowStateFile, JsonSerializer.Serialize(state, JsonOptions));
    }

    private static bool IsFullScreenArgument(string arg) {
        return FullScreenArguments.Contains(arg, StringComparer.OrdinalIgnoreCase);
    }

    private static WindowStartupState Load() {
        try {
            if (!File.Exists(AppPaths.WindowStateFile)) {
                return new WindowStartupState();
            }

            var json = File.ReadAllText(AppPaths.WindowStateFile);
            return JsonSerializer.Deserialize<WindowStartupState>(json) ?? new WindowStartupState();
        }
        catch {
            return new WindowStartupState();
        }
    }

    private sealed class WindowStartupState {
        public bool WasFullScreen { get; set; }
    }
}
