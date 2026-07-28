using System.Globalization;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class SettingsFieldParserTests {
    [Fact]
    public void TryParseZoom_ClampsParsedValuesAndDefaultsInvalidInput() {
        Assert.True(SettingsFieldParser.TryParseZoom(" -10 ", out var belowMinimum));
        Assert.Equal(GeoConverter.MinZoom, belowMinimum);
        Assert.True(SettingsFieldParser.TryParseZoom("100", out var aboveMaximum));
        Assert.Equal(GeoConverter.MaxZoom, aboveMaximum);

        Assert.False(SettingsFieldParser.TryParseZoom("auto", out var invalid));
        Assert.Equal(GeoConverter.MaxZoom, invalid);
    }

    [Fact]
    public void TryParseDouble_UsesCurrentCultureThenInvariantCulture() {
        var originalCulture = CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            Assert.True(SettingsFieldParser.TryParseDouble(" 1,5 ", out var localized));
            Assert.Equal(1.5, localized);
            Assert.True(SettingsFieldParser.TryParseDouble("2.25", out var invariant));
            Assert.Equal(2.25, invariant);
        } finally {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void TryParseIntegerInRange_RejectsInvalidAndOutOfRangeValues() {
        Assert.True(SettingsFieldParser.TryParseIntegerInRange(" 5 ", 1, 10, out var valid));
        Assert.Equal(5, valid);

        Assert.False(SettingsFieldParser.TryParseIntegerInRange("11", 1, 10, out var aboveRange));
        Assert.Equal(1, aboveRange);
        Assert.False(SettingsFieldParser.TryParseIntegerInRange("invalid", 1, 10, out var invalid));
        Assert.Equal(1, invalid);
    }

    [Fact]
    public void ParseSignatures_SplitsSupportedSeparatorsAndPreservesOrder() {
        var signatures = SettingsFieldParser.ParseSignatures(" first\r\nsecond, third;fourth\rfifth\nfirst ");

        Assert.Equal(["first", "second", "third", "fourth", "fifth", "first"], signatures);
    }
}
