using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class EditKeyboardCommandParserTests {
    [Theory]
    [InlineData("r 20", 20)]
    [InlineData("r-15.5", -15.5)]
    public void TryParse_RotateCommand_ReturnsDegrees(string text, double expectedDegrees) {
        Assert.True(EditKeyboardCommandParser.TryParse(text, out var command));

        Assert.Equal(EditKeyboardCommandKind.Rotate, command.Kind);
        Assert.Equal(expectedDegrees, command.RotationDegrees);
    }

    [Theory]
    [InlineData("m x 2", 2, 0)]
    [InlineData("mx20y20", 20, 20)]
    [InlineData("m y-3 x4dm", 4, -3)]
    public void TryParse_MoveCommand_ReturnsDecimeterOffsets(
        string text,
        double expectedEastDecimeters,
        double expectedNorthDecimeters) {
        Assert.True(EditKeyboardCommandParser.TryParse(text, out var command));

        Assert.Equal(EditKeyboardCommandKind.Move, command.Kind);
        Assert.True(command.HasMoveDistance);
        Assert.Equal(expectedEastDecimeters, command.MoveEastDecimeters);
        Assert.Equal(expectedNorthDecimeters, command.MoveNorthDecimeters);
    }

    [Theory]
    [InlineData("a", "DrawLine")]
    [InlineData("r", "Rotate")]
    [InlineData("m", "Move")]
    public void TryParse_ModeCommand_ReturnsCommandWithoutNumericValue(
        string text,
        string expectedKind) {
        Assert.True(EditKeyboardCommandParser.TryParse(text, out var command));

        Assert.Equal(expectedKind, command.Kind.ToString());
        Assert.Null(command.RotationDegrees);
        Assert.False(command.HasMoveDistance);
    }
}
