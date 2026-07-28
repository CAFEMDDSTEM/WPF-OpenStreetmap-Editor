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
    [InlineData("ex", "X", 0, false)]
    [InlineData("ey", "Y", 0, false)]
    [InlineData("es", "Segment", 0, false)]
    [InlineData("ess", "InnerSquare", 0, false)]
    [InlineData("ex10", "X", 10, true)]
    [InlineData("ey-5", "Y", -5, true)]
    public void TryParse_ExtrudeCommand_ReturnsModeAndDistance(
        string text,
        string expectedMode,
        double expectedDistanceDecimeters,
        bool expectedHasDistance) {
        Assert.True(EditKeyboardCommandParser.TryParse(text, out var command));

        Assert.Equal(EditKeyboardCommandKind.Extrude, command.Kind);
        Assert.Equal(expectedMode, command.ExtrudeMode.ToString());
        Assert.Equal(expectedDistanceDecimeters, command.ExtrudeDistanceDecimeters);
        Assert.Equal(expectedHasDistance, command.HasExtrudeDistance);
    }

    [Theory]
    [InlineData("a", "DrawLine")]
    [InlineData("r", "Rotate")]
    [InlineData("m", "Move")]
    [InlineData("e", "Extrude")]
    public void TryParse_ModeCommand_ReturnsCommandWithoutNumericValue(
        string text,
        string expectedKind) {
        Assert.True(EditKeyboardCommandParser.TryParse(text, out var command));

        Assert.Equal(expectedKind, command.Kind.ToString());
        Assert.Null(command.RotationDegrees);
        Assert.False(command.HasMoveDistance);
    }
}
