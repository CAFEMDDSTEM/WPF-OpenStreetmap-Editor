using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class AppCommandLineTests {
    [Fact]
    public void Parse_NoArguments_StartsGui() {
        var command = AppCommandLine.Parse([]);

        Assert.Equal(AppStartupMode.Gui, command.Mode);
        Assert.Empty(command.Arguments);
    }

    [Fact]
    public void Parse_GuiCommand_StartsGuiAndRemovesCommandName() {
        var command = AppCommandLine.Parse(["gui", "--fullscreen"]);

        Assert.Equal(AppStartupMode.Gui, command.Mode);
        Assert.Equal(["--fullscreen"], command.Arguments);
    }

    [Fact]
    public void Parse_LaunchCommand_StartsGuiAndRemovesCommandName() {
        var command = AppCommandLine.Parse(["launch", "--maximized"]);

        Assert.Equal(AppStartupMode.Gui, command.Mode);
        Assert.Equal(["--maximized"], command.Arguments);
    }

    [Fact]
    public void Parse_FullScreenArgument_StartsGui() {
        var command = AppCommandLine.Parse(["--fullscreen"]);

        Assert.Equal(AppStartupMode.Gui, command.Mode);
        Assert.Equal(["--fullscreen"], command.Arguments);
    }

    [Theory]
    [InlineData("help")]
    [InlineData("import")]
    [InlineData("convert")]
    [InlineData("download")]
    [InlineData("changeset")]
    [InlineData("upload")]
    public void Parse_CliCommand_RunsCli(string commandName) {
        var command = AppCommandLine.Parse([commandName]);

        Assert.Equal(AppStartupMode.Cli, command.Mode);
        Assert.Equal([commandName], command.Arguments);
    }
}
