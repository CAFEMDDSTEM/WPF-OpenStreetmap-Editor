using Microsoft.Win32;
using System.Windows;

namespace WPF_OpenStreetmap_Editor.Services;

public enum SystemThemeMode {
    Light,
    Dark,
    HighContrast
}

public static class SystemThemeService {
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    public static SystemThemeMode GetCurrentTheme() {
        if (SystemParameters.HighContrast) {
            return SystemThemeMode.HighContrast;
        }

        return TryReadAppsUseLightTheme(out var useLightTheme) && !useLightTheme
            ? SystemThemeMode.Dark
            : SystemThemeMode.Light;
    }

    private static bool TryReadAppsUseLightTheme(out bool useLightTheme) {
        useLightTheme = true;
        try {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            var value = key?.GetValue(AppsUseLightThemeValue);
            if (value is int intValue) {
                useLightTheme = intValue != 0;
                return true;
            }

            if (value is string stringValue && int.TryParse(stringValue, out var parsedValue)) {
                useLightTheme = parsedValue != 0;
                return true;
            }
        } catch (Exception ex) {
            Logger.Startup($"读取系统主题失败：{ex.Message}");
        }

        return false;
    }
}
