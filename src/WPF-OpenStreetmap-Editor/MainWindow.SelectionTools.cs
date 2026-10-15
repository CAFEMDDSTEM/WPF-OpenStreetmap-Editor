using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor;

/// <summary>
/// Photoshop-style selection tools: the magic wand selects every feature sharing the clicked
/// feature's main tag (connected ones by default), and the quick-selection brush paints features
/// into or out of the selection while dragging. Similarity and hit-testing live in the services.
/// </summary>
public partial class MainWindow {
    private const double DefaultQuickSelectBrushRadius = 20.0;
    private const double MinimumQuickSelectBrushRadius = 6.0;
    private const double MaximumQuickSelectBrushRadius = 80.0;
    private const double QuickSelectBrushStep = 4.0;

    private double _quickSelectBrushRadius = DefaultQuickSelectBrushRadius;
    private bool _quickSelectStroking;
    private bool _quickSelectSubtract;
    private Point _quickSelectLastPoint;

    private void MagicWandTool_Click(object sender, RoutedEventArgs e) => SetEditorMode(EditorMode.MagicWand);

    private void QuickSelectTool_Click(object sender, RoutedEventArgs e) => SetEditorMode(EditorMode.QuickSelect);

    private void MapCanvas_MouseLeave(object sender, MouseEventArgs e) {
        BrushCursor.Visibility = Visibility.Collapsed;
    }

    // ----- Magic wand -----

    private void SelectSimilarAt(Point position, bool extendSelection, bool contiguous) {
        if (_document is null) return;

        var seed = VectorMapInteraction.HitTest(
            GetInteractionCandidates(),
            position,
            _centerLat,
            _centerLon,
            int.TryParse(ZoomTextBox.Text, out var zoom) ? ClampZoom(zoom) : GeoConverter.MinZoom,
            GetMapViewportSize(),
            tolerance: 10,
            displayTransform: _displayTransform,
            panOffsetX: _panOffsetX,
            panOffsetY: _panOffsetY);
        if (seed is null) {
            if (!extendSelection) SetSelectedFeatures([]);
            return;
        }

        // Connectivity may run far outside the viewport, so similarity is evaluated on the whole layer.
        var similar = SimilarFeatureSelector.SelectSimilar(seed, _document.Features, contiguous);
        var selected = extendSelection
            ? Selection.Features.Concat(similar).Distinct().ToList()
            : similar.ToList();
        SetSelectedFeatures(selected);
    }

    // ----- Quick selection brush -----

    private void BeginQuickSelectStroke(Point position, bool subtract) {
        if (_document is null) return;

        _quickSelectStroking = true;
        _quickSelectSubtract = subtract;
        _quickSelectLastPoint = position;
        MapViewport.Focus();
        MapViewport.CaptureMouse();
        ApplyQuickSelectBrush(position);
        UpdateBrushCursor(position);
    }

    private void UpdateQuickSelectStroke(Point position) {
        UpdateBrushCursor(position);
        if (!_quickSelectStroking) return;

        // Sample along the pointer path so fast strokes do not skip features between events.
        var delta = position - _quickSelectLastPoint;
        var steps = Math.Max(1, (int)Math.Ceiling(delta.Length / Math.Max(1.0, _quickSelectBrushRadius / 2.0)));
        for (var step = 1; step <= steps; step++) {
            ApplyQuickSelectBrush(_quickSelectLastPoint + delta * ((double)step / steps));
        }
        _quickSelectLastPoint = position;
    }

    private bool EndQuickSelectStroke(bool hideBrush) {
        if (hideBrush && BrushCursor is not null) BrushCursor.Visibility = Visibility.Collapsed;
        if (!_quickSelectStroking) return false;

        _quickSelectStroking = false;
        if (MapViewport.IsMouseCaptured) MapViewport.ReleaseMouseCapture();
        // Panels, measurement and the feature grid are synchronized once per stroke.
        SetSelectedFeatures(Selection.Features.ToList());
        return true;
    }

    private void ApplyQuickSelectBrush(Point position) {
        var hits = VectorMapInteraction.FindNear(
            GetInteractionCandidates(),
            position,
            _centerLat,
            _centerLon,
            int.TryParse(ZoomTextBox.Text, out var zoom) ? ClampZoom(zoom) : GeoConverter.MinZoom,
            GetMapViewportSize(),
            tolerance: _quickSelectBrushRadius,
            displayTransform: _displayTransform,
            panOffsetX: _panOffsetX,
            panOffsetY: _panOffsetY);
        if (hits.Count == 0) return;

        var selected = Selection.Features.ToHashSet();
        var changed = false;
        foreach (var hit in hits) {
            changed |= _quickSelectSubtract ? selected.Remove(hit) : selected.Add(hit);
        }
        if (!changed) return;

        // Only the map follows the brush live; panels, measurement and the grid sync once when the stroke ends.
        if (Selection.Set(selected)) _selectionRevision++;
        UpdateVectorLayer();
    }

    private void AdjustQuickSelectBrush(int direction) {
        _quickSelectBrushRadius = Math.Clamp(
            _quickSelectBrushRadius + direction * QuickSelectBrushStep,
            MinimumQuickSelectBrushRadius,
            MaximumQuickSelectBrushRadius);
        UpdateBrushCursor(Mouse.GetPosition(MapViewport));
    }

    private void UpdateBrushCursor(Point position) {
        if (_editorMode != EditorMode.QuickSelect || !MapViewport.IsMouseOver && !_quickSelectStroking) {
            BrushCursor.Visibility = Visibility.Collapsed;
            return;
        }

        BrushCursor.Width = _quickSelectBrushRadius * 2;
        BrushCursor.Height = _quickSelectBrushRadius * 2;
        Canvas.SetLeft(BrushCursor, position.X - _quickSelectBrushRadius);
        Canvas.SetTop(BrushCursor, position.Y - _quickSelectBrushRadius);
        BrushCursor.Visibility = Visibility.Visible;
    }
}
