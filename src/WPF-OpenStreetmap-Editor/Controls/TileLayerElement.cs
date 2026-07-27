using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Controls;

public readonly record struct TileRenderItem(BitmapSource Source, TilePlacement Placement, bool IsFallback = false);
public sealed record TileRenderGroup(IReadOnlyList<TileRenderItem> Tiles, double Opacity);

public sealed class TileLayerElement : FrameworkElement {
    private const double TileOverlap = 0.5;
    private readonly List<TileRenderGroup> _layers = [];

    public void SetTiles(IEnumerable<TileRenderItem> tiles) {
        SetLayers([new TileRenderGroup([.. tiles], 1.0)]);
    }

    public void SetLayers(IEnumerable<TileRenderGroup> layers) {
        _layers.Clear();
        _layers.AddRange(layers);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext) {
        base.OnRender(drawingContext);

        foreach (var layer in _layers) {
            var opacity = Math.Clamp(layer.Opacity, 0.0, 1.0);
            if (opacity <= 0) continue;

            drawingContext.PushOpacity(opacity);
            foreach (var tile in layer.Tiles) {
                drawingContext.DrawImage(
                    tile.Source,
                    new Rect(
                        tile.Placement.Left,
                        tile.Placement.Top,
                        tile.Placement.Width + TileOverlap,
                        tile.Placement.Height + TileOverlap));
            }
            drawingContext.Pop();
        }
    }
}
