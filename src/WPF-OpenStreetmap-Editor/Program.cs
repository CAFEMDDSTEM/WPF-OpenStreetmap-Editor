using System;
using System.IO;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor;

public static class Program {
    [STAThread]
    public static int Main(string[] args) {
        var startup = AppCommandLine.Parse(args);
        if (startup.Mode == AppStartupMode.Cli) {
            return new CliApplication().RunAsync(startup.Arguments).GetAwaiter().GetResult();
        }

        ConsoleAttachment.DetachFromConsole();
        App.SetStartupArguments(startup.Arguments);
        var app = new App();
        app.InitializeComponent();
        app.Run();
        return 0;
    }
}
