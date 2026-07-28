using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class MapDataLayerSelectionServiceTests {
    [Fact]
    public void SelectPrimaryDataLayer_RoutesNewFeaturesAndDraftLinesToPathMatch() {
        var document = new MapDocument();
        var firstLayer = document.ActiveDataLayer;
        firstLayer.Name = "roads.geojson";
        firstLayer.SourcePath = Path.Combine("data", "roads.geojson");
        var selectedLayer = new MapDataLayer {
            Name = "roads.geojson",
            SourcePath = Path.Combine("overlays", "roads.geojson")
        };
        document.AddDataLayer(selectedLayer);
        var mapLayers = new[] {
            new MapImageLayer {
                Name = firstLayer.Name,
                DataPath = firstLayer.SourcePath,
                Kind = MapLayerKind.Data
            },
            new MapImageLayer {
                Name = selectedLayer.Name,
                DataPath = selectedLayer.SourcePath,
                Kind = MapLayerKind.Data,
                IsPrimary = true
            }
        };

        var match = MapDataLayerSelectionService.SelectPrimaryDataLayer(document, mapLayers);
        var editor = new EditorSession();
        editor.ReplaceDocument(document);
        var node = CreatePoint("node", 1);
        var pasted = CreatePoint("pasted", 2);

        Assert.Same(selectedLayer, match);
        Assert.Same(selectedLayer, document.ActiveDataLayer);
        Assert.True(editor.Execute(new AddFeatureCommand(node)));
        Assert.True(editor.Execute(new AddFeaturesCommand([pasted])));
        Assert.True(editor.AddDraftLinePoint(new GeoPoint(3, 3)));
        Assert.True(editor.AddDraftLinePoint(new GeoPoint(4, 4)));
        var line = Assert.IsType<MapFeature>(editor.FinishDraftLine());

        Assert.Empty(firstLayer.Features);
        Assert.Equal([node, pasted, line], selectedLayer.Features);
    }

    [Fact]
    public void SelectPrimaryDataLayer_FallsBackToLayerName() {
        var document = new MapDocument();
        var selectedLayer = new MapDataLayer { Name = "Buildings" };
        document.AddDataLayer(selectedLayer);

        var match = MapDataLayerSelectionService.SelectPrimaryDataLayer(document, [
            new MapImageLayer {
                Name = "buildings",
                Kind = MapLayerKind.Data,
                IsPrimary = true
            }
        ]);

        Assert.Same(selectedLayer, match);
        Assert.Same(selectedLayer, document.ActiveDataLayer);
    }

    [Fact]
    public void SelectPrimaryDataLayer_FallsBackDeterministicallyWhenPrimaryIsRasterOrUnmatched() {
        var document = new MapDocument();
        var firstLayer = document.ActiveDataLayer;
        var secondLayer = new MapDataLayer { Name = "second.geojson" };
        document.AddDataLayer(secondLayer);
        document.ActiveDataLayer = secondLayer;
        var unmatchedDataLayer = new MapImageLayer {
            Name = "missing.geojson",
            DataPath = "missing.geojson",
            Kind = MapLayerKind.Data
        };

        var selected = MapDataLayerSelectionService.SelectPrimaryDataLayer(document, [
            new MapImageLayer { Name = "Imagery", Kind = MapLayerKind.Raster, IsPrimary = true },
            unmatchedDataLayer
        ]);

        Assert.Same(firstLayer, selected);
        Assert.Same(firstLayer, document.ActiveDataLayer);
        Assert.Same(
            unmatchedDataLayer,
            MapDataLayerSelectionService.FindMapLayer(document.ActiveDataLayer, [unmatchedDataLayer]));
    }

    [Fact]
    public void FeatureCommand_MarksAndRestoresOwningLayerDirtyState() {
        var document = new MapDocument();
        var firstLayer = document.ActiveDataLayer;
        var feature = CreatePoint("overlay", 1);
        var secondLayer = new MapDataLayer { Features = { feature } };
        document.AddDataLayer(secondLayer);
        var stack = new EditCommandStack(new MapEditDataset(document));

        Assert.True(stack.Execute(SetFeatureAttributesCommand.CreatePatch(feature, [
            KeyValuePair.Create<string, string?>("name", "Overlay")
        ])));

        Assert.False(firstLayer.IsDirty);
        Assert.True(secondLayer.IsDirty);

        Assert.True(stack.Undo());
        Assert.False(firstLayer.IsDirty);
        Assert.False(secondLayer.IsDirty);
    }

    [Fact]
    public void SelectPrimaryDataLayer_ChangesDocumentSaveMetadataToSelectedDataset() {
        var document = new MapDocument {
            Name = "first.osm",
            SourcePath = Path.Combine("data", "first.osm"),
            SourceFormat = SpatialFileFormat.OsmXml
        };
        var selectedLayer = new MapDataLayer {
            Name = "second.geojson",
            SourcePath = Path.Combine("data", "second.geojson"),
            SourceFormat = SpatialFileFormat.GeoJson
        };
        document.AddDataLayer(selectedLayer);

        MapDataLayerSelectionService.SelectPrimaryDataLayer(document, [
            new MapImageLayer {
                Name = selectedLayer.Name,
                DataPath = selectedLayer.SourcePath,
                Kind = MapLayerKind.Data,
                IsPrimary = true
            }
        ]);

        Assert.Equal(selectedLayer.Name, document.Name);
        Assert.Equal(selectedLayer.SourcePath, document.SourcePath);
        Assert.Equal(selectedLayer.SourceFormat, document.SourceFormat);
    }

    [Fact]
    public void ActiveDataLayer_SaveStateDoesNotClearOtherDatasetChanges() {
        var document = new MapDocument();
        var firstLayer = document.ActiveDataLayer;
        document.IsDirty = true;
        var secondLayer = new MapDataLayer { IsDirty = true };
        document.AddDataLayer(secondLayer);
        document.ActiveDataLayer = secondLayer;

        document.MarkSaved();

        Assert.False(secondLayer.IsDirty);
        Assert.True(firstLayer.IsDirty);
        Assert.True(document.IsDirty);
    }

    private static MapFeature CreatePoint(string id, double coordinate) {
        return new MapFeature {
            Id = id,
            GeometryType = MapGeometryType.Point,
            Parts = [[new GeoPoint(coordinate, coordinate)]]
        };
    }
}
