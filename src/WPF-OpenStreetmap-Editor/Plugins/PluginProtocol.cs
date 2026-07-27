namespace WPF_OpenStreetmap_Editor.Plugins;

public static class PluginHooks {
    public const string ApplicationStarted = "application.started";
    public const string MainWindowLoaded = "mainWindow.loaded";
    public const string ApplicationStopping = "application.stopping";
}

public static class PluginActionTypes {
    public const string ShowMessage = "showMessage";
    public const string OpenUrl = "openUrl";
    public const string AddImagery = "addImagery";
    public const string ManageOsmAccounts = "manageOsmAccounts";
    public const string DownloadOsm = "downloadOsm";
    public const string UploadOsm = "uploadOsm";
}
