using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using WPF_OpenStreetmap_Editor.Controls;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor;

/// <summary>
/// Pen tool (Bézier curves): a click places a corner anchor, press-and-drag pulls out symmetric
/// handles, and finishing flattens the curve into vertices. Anchors snap like draw mode so curved
/// ways still connect to the existing network. Path math lives in <see cref="BezierPenPath"/>.
/// </summary>
public partial class MainWindow {
    private const double PenDragThresholdPixels = 4.0;
    private const double PenFlattenTolerancePixels = 1.5;
    // 1.5 px is ~0.3 m at zoom 19 and ~2.4 m at zoom 16; the ceiling only matters at overview zooms.
    private const double PenMinimumToleranceMeters = 0.2;
    private const double PenMaximumToleranceMeters = 25.0;

    private readonly BezierPenPath _penPath = new();
    private SnapTarget? _penSnap;
    private GeoPoint? _penPointerGeo;
    private bool _penClosingHover;
    private bool _penDragging;
    private Point _penPressPosition;
    private bool _penFirstAnchorSnapped;

    private void PenTool_Click(object sender, RoutedEventArgs e) => SetEditorMode(EditorMode.Pen);

    private void HandlePenMouseDown(Point position, int clickCount) {
        if (clickCount >= 2) {
            FinishPenPath();
            return;
        }

        EnsureDocument();
        UpdatePenHover(position, leftButtonPressed: false);
        if (_penClosingHover) {
            FinishPenPath(closeRing: true);
            return;
        }
        if (IsOnLastPenAnchor(position)) {
            FinishPenPath();
            return;
        }

        var snap = _penSnap;
        if (!TryResolveSnapPoint(snap, position, out var point)) return;

        if (_penPath.IsEmpty) _penFirstAnchorSnapped = snap is not null;
        _penPath.AddCorner(point);
        _penDragging = true;
        _penPressPosition = position;
        MapViewport.CaptureMouse();
        UpdatePenPreviewLayer();
        UpdateDocumentUi();
    }

    private void UpdatePenHover(Point position, bool leftButtonPressed) {
        if (_document is null) {
            ClearPenHover();
            return;
        }

        var projection = CreateViewportProjection();
        var pointerGeo = projection.ScreenToGeo(position);
        if (_penDragging && leftButtonPressed) {
            // Dragging after the press turns the fresh corner into a smooth anchor whose handle follows the pointer.
            var dragged = (position - _penPressPosition).Length >= PenDragThresholdPixels;
            _penPath.SetLastHandle(dragged ? pointerGeo : _penPath.Anchors[^1].Point);
            _penSnap = null;
            _penClosingHover = false;
            _penPointerGeo = pointerGeo;
            UpdatePenPreviewLayer();
            return;
        }

        _penClosingHover = _penPath.Count >= 3 &&
            (projection.GeoToScreen(_penPath.Anchors[0].Point) - position).Length <= DrawSnapService.DefaultVertexTolerance;
        _penSnap = _penClosingHover
            ? null
            : DrawSnapService.FindSnapTarget(GetInteractionCandidates(), position, projection);
        _penPointerGeo = pointerGeo;
        Cursor = Cursors.Pen;
        _viewModel.MeasurementStatus = L.GetString("Main.Measure.PenHint");
        if (_hoveredFeature is not null || _hoveredVertex is not null) {
            _hoveredFeature = null;
            _hoveredVertex = null;
            UpdateVectorLayer();
        } else {
            UpdatePenPreviewLayer();
        }
    }

    private bool EndPenDrag(Point position) {
        if (!_penDragging) return false;

        _penDragging = false;
        if (MapViewport.IsMouseCaptured) MapViewport.ReleaseMouseCapture();
        if ((position - _penPressPosition).Length < PenDragThresholdPixels && !_penPath.IsEmpty) {
            _penPath.SetLastHandle(_penPath.Anchors[^1].Point);
        }
        UpdatePenHover(position, leftButtonPressed: false);
        return true;
    }

    private bool IsOnLastPenAnchor(Point position) {
        if (_penPath.IsEmpty) return false;

        var last = CreateViewportProjection().GeoToScreen(_penPath.Anchors[^1].Point);
        return (last - position).Length <= DrawSnapService.DefaultVertexTolerance;
    }

    private bool RemoveLastPenAnchor() {
        if (!_penPath.RemoveLast()) return false;

        // A drag in progress belonged to the anchor that was just removed; end it before the next move
        // tries to set a handle on a shorter (possibly empty) path.
        _penDragging = false;
        if (MapViewport.IsMouseCaptured) MapViewport.ReleaseMouseCapture();
        UpdatePenHover(Mouse.GetPosition(MapViewport), leftButtonPressed: false);
        UpdateDocumentUi();
        return true;
    }

    /// <summary>
    /// Converts the path into a feature. A lone anchor becomes a node unless it was snapped onto
    /// existing geometry (it already lives there); two or more anchors become a line, or an area
    /// when the ring was closed on the first anchor.
    /// </summary>
    private void FinishPenPath(bool closeRing = false) {
        if (_penPath.IsEmpty) return;

        var anchors = _penPath.Anchors.ToList();
        var firstAnchorSnapped = _penFirstAnchorSnapped;
        var points = _penPath.Flatten(GetPenFlattenToleranceMeters(), closeRing).ToList();
        ResetPenPath();

        MapFeature? feature = null;
        if (anchors.Count == 1) {
            if (!firstAnchorSnapped) {
                feature = new MapFeature { GeometryType = MapGeometryType.Point, Parts = [[anchors[0].Point]] };
            }
        } else if (points.Count >= 2) {
            // A ring needs three distinct vertices, like EditorSession's closed-ring completion.
            var isArea = closeRing && points.Distinct().Count() >= 3;
            feature = new MapFeature {
                GeometryType = isArea ? MapGeometryType.Polygon : MapGeometryType.LineString,
                Parts = [points]
            };
        }

        if (feature is null || !Editor.Execute(new AddFeatureCommand(feature))) {
            UpdateDocumentUi();
            return;
        }

        SetSelectedFeatures([feature]);
        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void CancelPenPath() {
        if (_penPath.IsEmpty && _penSnap is null && _penPointerGeo is null) return;

        ResetPenPath();
        UpdateDocumentUi();
    }

    private void ClearPenHover() {
        if (_penSnap is null && _penPointerGeo is null && !_penClosingHover) return;

        _penSnap = null;
        _penPointerGeo = null;
        _penClosingHover = false;
        UpdatePenPreviewLayer();
    }

    private void ResetPenPath() {
        _penPath.Clear();
        _penDragging = false;
        _penFirstAnchorSnapped = false;
        _penSnap = null;
        _penPointerGeo = null;
        _penClosingHover = false;
        if (MapViewport.IsMouseCaptured) MapViewport.ReleaseMouseCapture();
        UpdatePenPreviewLayer();
    }

    private void UpdatePenPreviewLayer() {
        PenPreviewLayer?.Update(CreatePenPreview(), CreateViewportProjection());
    }

    private PenPreview? CreatePenPreview() {
        if (_editorMode != EditorMode.Pen || (_penPath.IsEmpty && _penSnap is null)) return null;

        var tolerance = GetPenFlattenToleranceMeters();
        var pending = _penPointerGeo is { } pointer && !_penDragging && !_penPath.IsEmpty
            ? _penPath.FlattenPending(_penClosingHover ? _penPath.Anchors[0].Point : _penSnap?.Point ?? pointer, tolerance)
            : [];
        return new PenPreview(
            _penPath.Flatten(tolerance),
            pending,
            _penPath.Anchors,
            _penSnap,
            _penClosingHover ? _penPath.Anchors[0].Point : null);
    }

    /// <summary>Flattening tolerance in metres: about 1.5 screen pixels at the current zoom, kept within sensible OSM node spacing.</summary>
    private double GetPenFlattenToleranceMeters() {
        var zoom = int.TryParse(ZoomTextBox.Text, out var parsed) ? ClampZoom(parsed) : GeoConverter.MinZoom;
        var metersPerPixel = GeoConverter.GetMetersPerPixel(_centerLat, zoom);
        return Math.Clamp(PenFlattenTolerancePixels * metersPerPixel, PenMinimumToleranceMeters, PenMaximumToleranceMeters);
    }
}
