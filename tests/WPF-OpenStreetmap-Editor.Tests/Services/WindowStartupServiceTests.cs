using System.Windows;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class WindowStartupServiceTests {
    [Fact]
    public void GetNormalWindowMaxSize_UsesEightyPercentOfWorkArea() {
        var size = WindowStartupService.GetNormalWindowMaxSize(new Rect(0, 0, 1000, 800));

        Assert.Equal(800, size.Width);
        Assert.Equal(640, size.Height);
    }

    [Fact]
    public void GetCenteredWindowPosition_UsesWorkAreaCenter() {
        var position = WindowStartupService.GetCenteredWindowPosition(
            new Size(800, 640),
            new Rect(100, 50, 1000, 800));

        Assert.Equal(200, position.X);
        Assert.Equal(130, position.Y);
    }

    [Fact]
    public void ShouldStartMaximized_UsesSavedMaximizedState() {
        var shouldStartMaximized = WindowStartupService.ShouldStartMaximized([], true);

        Assert.True(shouldStartMaximized);
    }

    [Fact]
    public void ShouldStartMaximized_UsesCommandLineArgument() {
        var shouldStartMaximized = WindowStartupService.ShouldStartMaximized(["--maximized"], false);

        Assert.True(shouldStartMaximized);
    }

    [Theory]
    [InlineData(WindowState.Normal, WindowState.Maximized, WindowState.Normal)]
    [InlineData(WindowState.Maximized, WindowState.Normal, WindowState.Maximized)]
    [InlineData(WindowState.Minimized, WindowState.Maximized, WindowState.Maximized)]
    public void GetStateToSave_PreservesLastNonMinimizedState(
        WindowState currentWindowState,
        WindowState lastNonMinimizedWindowState,
        WindowState expectedWindowState) {
        var stateToSave = WindowStartupService.GetStateToSave(currentWindowState, lastNonMinimizedWindowState);

        Assert.Equal(expectedWindowState, stateToSave);
    }
}
