using System.Windows.Media;
using WPF_OpenStreetmap_Editor.Controls;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Controls;

public class VectorRenderPaletteTests {
    [Fact]
    public void Create_UsesVisibleFillOpacityForGenericAndThematicAreas() {
        var palette = VectorRenderPalette.Create(key => key switch {
            "Theme.Map.GenericAreaFillBrush" => new SolidColorBrush(Colors.Gray),
            "Theme.Map.ForestFillBrush" => new SolidColorBrush(Colors.Green),
            _ => null
        });

        var genericArea = palette.GetAreaStyle(VectorFeatureStyleKind.GenericArea);
        var forest = palette.GetAreaStyle(VectorFeatureStyleKind.Forest);

        Assert.Equal(0.28, genericArea.Fill.Opacity, precision: 2);
        Assert.Equal(0.38, forest.Fill.Opacity, precision: 2);
        Assert.Equal(Colors.Gray, ((SolidColorBrush)genericArea.Fill).Color);
        Assert.Equal(Colors.Green, ((SolidColorBrush)forest.Fill).Color);
    }

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
