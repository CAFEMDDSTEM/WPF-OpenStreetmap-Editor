using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class OsmApiClientTests {
    [Fact]
    public void ValidateBounds_AcceptsSmallValidSelection() {
        OsmApiClient.ValidateBounds(new GeoBounds(103.8, 1.3, 103.9, 1.4));
    }

    [Fact]
    public void ValidateBounds_RejectsSelectionAboveApiLimit() {
        Assert.Throws<InvalidDataException>(() =>
            OsmApiClient.ValidateBounds(new GeoBounds(0, 0, 1, 1)));
    }
}
