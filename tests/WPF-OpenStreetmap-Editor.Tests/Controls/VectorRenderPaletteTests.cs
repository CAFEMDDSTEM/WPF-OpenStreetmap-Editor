using WPF_OpenStreetmap_Editor.Controls;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Controls;

public class VectorRenderPaletteTests {
    [Fact]
    public void Create_UsesDistinctFallbackRoadWidths() {
        var palette = VectorRenderPalette.Create(_ => null);

        var service = palette.GetLineStyle(VectorFeatureStyleKind.ServiceRoad);
        var residential = palette.GetLineStyle(VectorFeatureStyleKind.ResidentialRoad);
        var secondary = palette.GetLineStyle(VectorFeatureStyleKind.SecondaryRoad);
        var primary = palette.GetLineStyle(VectorFeatureStyleKind.PrimaryRoad);
        var motorway = palette.GetLineStyle(VectorFeatureStyleKind.Motorway);

        Assert.True(service.Stroke.Thickness < residential.Stroke.Thickness);
        Assert.True(residential.Stroke.Thickness < secondary.Stroke.Thickness);
        Assert.True(secondary.Stroke.Thickness < primary.Stroke.Thickness);
        Assert.True(primary.Stroke.Thickness < motorway.Stroke.Thickness);
        Assert.NotNull(service.Casing);
        Assert.NotNull(residential.Casing);
    }
}
