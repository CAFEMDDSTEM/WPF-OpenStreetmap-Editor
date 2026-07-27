namespace WPF_OpenStreetmap_Editor.Services;

public enum AppStartupMode {
    Gui,
    Cli
}

public sealed record AppStartupCommand(AppStartupMode Mode, IReadOnlyList<string> Arguments);

public static class AppCommandLine {
    public static AppStartupCommand Parse(IEnumerable<string> args) {
        var tokens = args.ToArray();
        if (tokens.Length == 0) {
            return new AppStartupCommand(AppStartupMode.Gui, []);
        }

        if (IsGuiCommand(tokens[0])) {
            return new AppStartupCommand(AppStartupMode.Gui, tokens.Skip(1).ToArray());
        }

        if (tokens.All(WindowStartupService.IsFullScreenArgument)) {
            return new AppStartupCommand(AppStartupMode.Gui, tokens);
        }

        return new AppStartupCommand(AppStartupMode.Cli, tokens);
    }

    private static bool IsGuiCommand(string value) {
        return value.Equals("gui", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("launch", StringComparison.OrdinalIgnoreCase);
    }
}

