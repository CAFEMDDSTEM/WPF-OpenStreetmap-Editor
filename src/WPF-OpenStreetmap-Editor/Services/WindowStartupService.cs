using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace WPF_OpenStreetmap_Editor.Services;

public static class WindowStartupService {
    private const double NormalWindowScreenRatio = 0.8;
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
        CenterNormalWindow(window);
    }

    public static void ApplyNormalWindowLimits(Window window) {
        var maxSize = GetNormalWindowMaxSize();
        ClearNormalWindowLimits(window);

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
        return GetNormalWindowMaxSize(SystemParameters.WorkArea);
    }

    public static Size GetNormalWindowMaxSize(Rect workArea) {
        return new Size(workArea.Width * NormalWindowScreenRatio, workArea.Height * NormalWindowScreenRatio);
    }

    public static void CenterNormalWindow(Window window) {
        var position = GetCenteredWindowPosition(new Size(window.Width, window.Height), SystemParameters.WorkArea);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = position.X;
        window.Top = position.Y;
    }

    public static Point GetCenteredWindowPosition(Size windowSize, Rect workArea) {
        var left = workArea.Left + (workArea.Width - windowSize.Width) / 2.0;
        var top = workArea.Top + (workArea.Height - windowSize.Height) / 2.0;
        return new Point(left, top);
    }

    public static bool ShouldStartFullScreen(IEnumerable<string> commandLineArgs) {
        return ShouldStartMaximized(commandLineArgs, Load().WasFullScreen);
    }

    public static bool ShouldStartMaximized(IEnumerable<string> commandLineArgs, bool savedAsMaximized) {
        return savedAsMaximized || commandLineArgs.Any(IsFullScreenArgument);
    }

    public static WindowState GetStateToSave(WindowState currentWindowState, WindowState lastNonMinimizedWindowState) {
        return currentWindowState == WindowState.Minimized ? lastNonMinimizedWindowState : currentWindowState;
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
            var windowStateFile = AppPaths.ResolveReadPath(AppPaths.WindowStateFile, AppPaths.LegacyWindowStateFile);
            if (!File.Exists(windowStateFile)) {
                return new WindowStartupState();
            }

            var json = File.ReadAllText(windowStateFile);
            return JsonSerializer.Deserialize<WindowStartupState>(json) ?? new WindowStartupState();
        } catch {
            return new WindowStartupState();
        }
    }

    private sealed class WindowStartupState {
        public bool WasFullScreen { get; set; }
    }
}
