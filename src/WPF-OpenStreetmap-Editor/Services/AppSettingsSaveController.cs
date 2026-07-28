using System.Windows;

namespace WPF_OpenStreetmap_Editor.Services;

internal sealed class AppSettingsSaveController {
    public bool Save(AppSettings settings, Window owner) {
        if (AppSettingsService.Save(settings, out var error)) return true;

        PersistenceErrorPresenter.Show(owner, error);
        return false;
    }
}

internal static class PersistenceErrorPresenter {
    public static void Show(Window owner, Exception? error) {
        var localization = LocalizationService.Instance;
        MessageBox.Show(
            owner,
            localization.Format(
                "Common.SaveFailed",
                error?.Message ?? localization.GetString("Common.UnknownError")),
            localization.GetString("Common.Error"),
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
