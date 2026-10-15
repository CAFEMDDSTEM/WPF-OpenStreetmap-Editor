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
/// Topology-aware editing on the map canvas: snapping while drawing, vertex insertion/removal,
/// junction propagation for whole-feature transforms, JOSM-style geometry tools and measurement.
/// Geometry logic lives in the services; this file only wires pointer/keyboard input to them.
/// </summary>
public partial class MainWindow {
    private const double InsertVertexSegmentTolerance = 10.0;
    private const double JoinNodeSegmentTolerance = 14.0;
    private const int CreateCircleVertexCount = 16;

    private SnapTarget? _drawSnap;
    private GeoPoint? _drawPointerGeo;
    private bool _draftFirstVertexSnapped;

    private void AlignInCircle_Click(object sender, RoutedEventArgs e) => ApplyGeometryEdit(GeometryAlignService.AlignInCircle);

    private void AlignInLine_Click(object sender, RoutedEventArgs e) => ApplyGeometryEdit(GeometryAlignService.AlignInLine);

    private void DistributeVertices_Click(object sender, RoutedEventArgs e) => ApplyGeometryEdit(GeometryAlignService.DistributeVertices);

    private void MirrorSelected_Click(object sender, RoutedEventArgs e) => ApplyGeometryEdit(parts => GeometryAlignService.Mirror(parts, MirrorAxis.Vertical));

    private void CreateCircle_Click(object sender, RoutedEventArgs e) => CreateCircleFromSelection();

    private void CreateCircleFromSelection() {
        ApplyGeometryEdit(parts => GeometryAlignService.CreateCircle(parts, CreateCircleVertexCount), preservesVertexCount: false);
    }

    private void JoinNodeToWay_Click(object sender, RoutedEventArgs e) => JoinSelectedNodesToWay();

    private void DeleteHoveredVertex_Click(object sender, RoutedEventArgs e) {
        if (!TryDeleteHoveredVertex()) ShowTopologyMessage(L.GetString("Main.Vertex.HoverToDelete"));
    }

    private GeoViewportProjection CreateViewportProjection() {
        return GeoViewportProjection.Create(
            _centerLat,
            _centerLon,
            int.TryParse(ZoomTextBox.Text, out var zoom) ? ClampZoom(zoom) : GeoConverter.MinZoom,
            GetMapViewportSize(),
            _panOffsetX,
            _panOffsetY,
            _displayTransform);
    }

    private VertexShareIndex BuildVertexShareIndex(IReadOnlyList<FeaturePartsSnapshot> editedStates) {
        var bounds = GeoBounds.FromPoints(editedStates.SelectMany(static state => state.Parts).SelectMany(static part => part));
        return VertexShareIndex.Build(QueryFeaturesIntersecting(bounds));
    }

    /// <summary>
    /// Features of the active layer whose bounds intersect <paramref name="bounds"/>; anything sharing a
    /// vertex with the edited geometry is necessarily among them, so junction lookups never need a full scan.
    /// </summary>
    private IEnumerable<MapFeature> QueryFeaturesIntersecting(GeoBounds bounds) {
        var layer = Editor.EnsureDocument().ActiveDataLayer;
        if (!bounds.IsValid || !layer.IsVisible || layer.Opacity <= 0) return layer.Features;

        return layer.QueryFeatures(bounds);
    }

    // ----- Draw mode: snapping, closing rings, lone nodes -----

    private void UpdateDrawHover(Point position) {
        if (_document is null) {
            ClearDrawHover();
            return;
        }

        var projection = CreateViewportProjection();
        var pointerGeo = projection.ScreenToGeo(position);
        var draft = Editor.DraftLine;
        var snap = TryGetDraftEndpointSnap(draft, position, projection) ??
            DrawSnapService.FindSnapTarget(GetInteractionCandidates(), position, projection, excludedFeature: draft);
        _drawSnap = snap;
        _drawPointerGeo = pointerGeo;
        Cursor = Cursors.Cross;
        UpdateDrawStatus(draft, snap?.Point ?? pointerGeo);
        if (_hoveredFeature is not null || _hoveredVertex is not null) {
            // Select-mode hover highlights are drawn by the vector layer; clear them once when drawing starts.
            _hoveredFeature = null;
            _hoveredVertex = null;
            UpdateVectorLayer();
        } else {
            UpdateDrawPreviewLayer();
        }
    }

    private void ClearDrawHover() {
        if (_drawSnap is null && _drawPointerGeo is null) return;

        _drawSnap = null;
        _drawPointerGeo = null;
        UpdateDrawPreviewLayer();
    }

    private void UpdateDrawPreviewLayer() {
        DrawPreviewLayer.Update(CreateDrawPreview(), CreateViewportProjection());
    }

    private DrawPreview? CreateDrawPreview() {
        if (_editorMode != EditorMode.DrawLine || _drawPointerGeo is not { } pointer) return null;

        var draft = Editor.DraftLine;
        var anchor = draft is { Parts.Count: > 0 } && draft.Parts[0].Count > 0
            ? draft.Parts[0][^1]
            : (GeoPoint?)null;
        return new DrawPreview(anchor, pointer, _drawSnap);
    }

    /// <summary>
    /// Snapping onto the draft's own endpoints has a special meaning: the first vertex closes the
    /// ring and the last vertex finishes the line, so those win over any other snap target.
    /// </summary>
    private static SnapTarget? TryGetDraftEndpointSnap(MapFeature? draft, Point position, GeoViewportProjection projection) {
        if (draft is null || draft.Parts.Count == 0 || draft.Parts[0].Count == 0) return null;

        var part = draft.Parts[0];
        foreach (var index in new[] { part.Count - 1, 0 }) {
            var screen = projection.GeoToScreen(part[index]);
            if ((screen - position).Length <= DrawSnapService.DefaultVertexTolerance) {
                return new SnapTarget(SnapTargetKind.Vertex, draft, 0, index, part[index], screen);
            }
        }

        return null;
    }

    private void HandleDrawClick(Point position, int clickCount) {
        if (clickCount >= 2) {
            FinishDraftLine();
            return;
        }

        UpdateDrawHover(position);
        var draft = Editor.DraftLine;
        var snap = _drawSnap;
        if (draft is not null && snap is not null && ReferenceEquals(snap.Feature, draft)) {
            if (snap.PointIndex == 0 && draft.Parts[0].Count >= 3) FinishDraftLine(DraftCompletion.ClosedRing);
            else FinishDraftLine();
            return;
        }

        if (!TryResolveSnapPoint(snap, position, out var point)) return;

        if (!Editor.HasDraftLine) _draftFirstVertexSnapped = snap is not null;
        AddDraftPoint(point);
        UpdateDrawHover(position);
    }

    /// <summary>
    /// Turns a click into the document coordinate a new vertex should take: the snapped vertex, a new
    /// shared vertex inserted into the snapped segment, or the raw pointer position. Shared by draw and pen mode.
    /// </summary>
    private bool TryResolveSnapPoint(SnapTarget? snap, Point position, out GeoPoint point) {
        if (snap is null) {
            point = CreateViewportProjection().ScreenToGeo(position);
        } else if (snap.Kind == SnapTargetKind.Vertex) {
            point = snap.Point;
        } else {
            // Joining onto a segment inserts a new vertex into that way first so both ways share it.
            point = snap.Point;
            if (!InsertVertexIntoFeature(snap.Feature, snap.PartIndex, snap.PointIndex, snap.Point)) return false;
        }

        return point.IsValid;
    }

    private void UpdateDrawStatus(MapFeature? draft, GeoPoint pointer) {
        if (draft is null || draft.Parts.Count == 0 || draft.Parts[0].Count == 0 || !pointer.IsValid) {
            _viewModel.MeasurementStatus = L.GetString("Main.Measure.DrawHint");
            return;
        }

        var anchor = draft.Parts[0][^1];
        _viewModel.MeasurementStatus = L.Format(
            "Main.Measure.DrawSegment",
            GeoMeasurement.FormatLength(GeoMeasurement.DistanceMeters(anchor, pointer)),
            GeoMeasurement.BearingDegrees(anchor, pointer),
            GeoMeasurement.FormatLength(GeoMeasurement.LengthMeters(draft.Parts[0]) + GeoMeasurement.DistanceMeters(anchor, pointer)));
    }

    private bool RemoveLastDraftVertex() {
        if (!Editor.HasDraftLine) return false;

        if (!Editor.RemoveLastDraftLinePoint() || Editor.DraftLine!.Parts[0].Count == 0) {
            Editor.CancelDraftLine();
            SetSelectedFeatures([]);
        }
        RefreshFeatureList();
        UpdateDrawHover(Mouse.GetPosition(MapViewport));
        UpdateVectorLayer();
        UpdateDocumentUi();
        return true;
    }

    // ----- Vertex insertion / removal -----

    private bool TryInsertVertexAt(Point position) {
        if (_document is null) return false;

        var snap = DrawSnapService.FindSnapTarget(
            GetInteractionCandidates(),
            position,
            CreateViewportProjection(),
            segmentTolerance: InsertVertexSegmentTolerance);
        if (snap is null || snap.Kind != SnapTargetKind.Segment) return false;
        if (!InsertVertexIntoFeature(snap.Feature, snap.PartIndex, snap.PointIndex, snap.Point)) return false;

        if (!Selection.Features.Contains(snap.Feature)) SetSelectedFeatures([snap.Feature]);
        UpdateMapHover(position);
        return true;
    }

    private bool InsertVertexIntoFeature(MapFeature feature, int partIndex, int segmentStartIndex, GeoPoint point) {
        var before = CaptureFeatureParts(feature);
        var after = new FeaturePartsSnapshot(
            feature,
            VertexEditService.InsertVertex(before.Parts, partIndex, segmentStartIndex, point));
        if (!Editor.Execute(new SetFeaturePartsCommand([before], [after]))) return false;

        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
        return true;
    }

    private bool TryDeleteHoveredVertex() {
        // Hover state survives the pointer leaving the map (the Split menu relies on that), so a
        // destructive key must only act on it while the pointer is actually over the map.
        if (_hoveredVertex is not { } hit || _document is null || !MapViewport.IsMouseOver ||
            _featureRotation is not null || _featureMove is not null || _vertexMove is not null || _segmentExtrude is not null) {
            return false;
        }

        var targets = GetSharedVertexTargets(hit)
            .Where(static target => target.Feature.GeometryType != MapGeometryType.Point)
            .GroupBy(static target => target.Feature);
        var beforeStates = new List<FeaturePartsSnapshot>();
        var afterStates = new List<FeaturePartsSnapshot>();
        foreach (var group in targets) {
            var before = CaptureFeatureParts(group.Key);
            List<List<GeoPoint>>? parts = before.Parts.Select(static part => part.ToList()).ToList();
            foreach (var target in group.OrderByDescending(static target => target.PartIndex).ThenByDescending(static target => target.PointIndex)) {
                parts = VertexEditService.RemoveVertex(parts, target.PartIndex, target.PointIndex);
                if (parts is null) {
                    ShowTopologyMessage(L.GetString("Main.Vertex.DeleteWouldCollapse"));
                    return true;
                }
            }

            beforeStates.Add(before);
            afterStates.Add(new FeaturePartsSnapshot(group.Key, parts));
        }

        if (beforeStates.Count == 0 || !Editor.Execute(new SetFeaturePartsCommand(beforeStates, afterStates))) return false;

        _hoveredVertex = null;
        RefreshFeatureList();
        UpdateMapHover(Mouse.GetPosition(MapViewport));
        UpdateVectorLayer();
        UpdateDocumentUi();
        return true;
    }

    /// <summary>JOSM "Join node to way" (J): moves each selected point onto the nearest line and inserts it as a vertex there.</summary>
    private void JoinSelectedNodesToWay() {
        CommitPendingModes();
        var points = GetSelectedFeaturesInDocumentOrder()
            .Where(static feature => feature.GeometryType == MapGeometryType.Point)
            .ToList();
        if (points.Count == 0) {
            ShowTopologyMessage(L.GetString("Main.Topology.SelectNodes"));
            return;
        }

        var result = NodeJoinService.JoinPointsToWays(points, GetInteractionCandidates(), CreateViewportProjection(), JoinNodeSegmentTolerance);
        if (result.JoinedCount == 0) {
            ShowTopologyMessage(L.GetString("Main.Topology.NoWayNearby"));
            return;
        }
        if (!Editor.Execute(new SetFeaturePartsCommand(result.Before, result.After))) return;

        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    // ----- Topology command preconditions -----

    /// <summary>
    /// Topology commands need an exact way-to-vertex mapping. Vertex insertion, removal and dragging
    /// leave the cached node references behind until the next synchronization, so run it on demand
    /// (the same step saving or uploading performs) instead of failing with an "out of sync" error.
    /// </summary>
    private bool EnsureTopologyMappingCurrent(IEnumerable<MapFeature> features) {
        if (features.All(IsWayMappingCurrent)) return true;
        if (!EnsureSyncedDocument()) return false;

        RefreshFeatureList();
        UpdateVectorLayer();
        return true;
    }

    private static bool IsWayMappingCurrent(MapFeature feature) {
        if (feature.Osm is not { PrimitiveType: OsmPrimitiveType.Way } osm || feature.Parts.Count != 1) return true;

        var part = feature.Parts[0];
        return osm.NodeReferences.Count == part.Count &&
            osm.NodeReferences.Select(static reference => reference.Point).SequenceEqual(part);
    }

    // ----- Whole-feature transforms with junction propagation -----

    /// <summary>
    /// Applies a per-feature geometry edit to the selected lines/areas as one undoable command,
    /// moving junction vertices of connected features along (see <see cref="SharedVertexFollowers"/>).
    /// Edits that change the vertex count cannot carry junctions along, so they are refused when the
    /// selection shares nodes with other features instead of silently detaching them.
    /// </summary>
    private void ApplyGeometryEdit(
        Func<IReadOnlyList<IReadOnlyList<GeoPoint>>, GeometryEditResult> edit,
        bool preservesVertexCount = true) {
        if (_featureRotation is not null || _featureMove is not null || _vertexMove is not null || _segmentExtrude is not null) return;
        if (Editor.HasDraftLine) FinishDraftLine();

        var selectedFeatures = GetSelectedFeaturesInDocumentOrder()
            .Where(static feature => feature.GeometryType != MapGeometryType.Point)
            .ToList();
        if (selectedFeatures.Count == 0) {
            ShowTopologyMessage(L.GetString("Main.Topology.SelectOne"));
            return;
        }

        var beforeStates = selectedFeatures.Select(CaptureFeatureParts).ToList();
        var followers = CaptureFollowerOriginals(beforeStates);
        if (!preservesVertexCount && followers.Originals.Count > 0) {
            ShowTopologyMessage(L.GetString("Main.Align.SharedNodesUnsupported"));
            return;
        }

        var afterStates = new List<FeaturePartsSnapshot>(beforeStates.Count);
        foreach (var state in beforeStates) {
            var result = edit(state.Parts);
            if (!result.IsSuccess) {
                ShowTopologyMessage(result.Error ?? L.GetString("Common.UnknownError"));
                return;
            }

            afterStates.Add(new FeaturePartsSnapshot(state.Feature, result.Parts!));
        }

        if (!ExecuteTransformWithFollowers(beforeStates, afterStates, followers)) return;

        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private bool ExecuteGeometryChangeWithFollowers(
        IReadOnlyList<FeaturePartsSnapshot> beforeStates,
        IReadOnlyList<FeaturePartsSnapshot> afterStates) {
        if (!ExecuteTransformWithFollowers(beforeStates, afterStates, CaptureFollowerOriginals(beforeStates))) return false;

        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
        return true;
    }

    /// <summary>Commits an edit of the given features plus the matching junction moves of their followers as one undoable command.</summary>
    private bool ExecuteTransformWithFollowers(
        IReadOnlyList<FeaturePartsSnapshot> beforeStates,
        IReadOnlyList<FeaturePartsSnapshot> afterStates,
        TransformFollowers followers) {
        var followerStates = SharedVertexFollowers.CreateFollowerStates(followers.Index, beforeStates, afterStates, followers.Originals);
        return Editor.Execute(new SetFeaturePartsCommand(
            beforeStates.Concat(followers.Originals),
            afterStates.Concat(followerStates)));
    }

    /// <summary>
    /// Captures the share index and the original geometry of connected features before an
    /// interactive transform starts; the index must predate the preview edits because they move
    /// the very vertices it is keyed on.
    /// </summary>
    private TransformFollowers CaptureFollowerOriginals(IReadOnlyList<FeaturePartsSnapshot> editedStates) {
        var index = BuildVertexShareIndex(editedStates);
        var originals = SharedVertexFollowers.FindFollowerFeatures(index, editedStates)
            .Select(CaptureFeatureParts)
            .ToList();
        return new TransformFollowers(index, originals);
    }

    /// <summary>Moves follower junction vertices to match the edited features' current geometry during an interactive transform.</summary>
    private void ApplyFollowerPreview(IReadOnlyList<FeaturePartsSnapshot> editedOriginals, TransformFollowers followers) {
        if (followers.Originals.Count == 0) return;

        var current = editedOriginals.Select(state => CaptureFeatureParts(state.Feature)).ToList();
        foreach (var state in SharedVertexFollowers.CreateFollowerStates(followers.Index, editedOriginals, current, followers.Originals)) {
            Editor.Dataset.ReplaceParts(state.Feature, state.Parts.Select(static part => part.ToList()), markDirty: false);
        }
    }

    private void RestoreSnapshots(IEnumerable<FeaturePartsSnapshot> states) {
        foreach (var state in states) {
            Editor.Dataset.ReplaceParts(state.Feature, state.Parts.Select(static part => part.ToList()), markDirty: false);
        }
    }

    private sealed record TransformFollowers(VertexShareIndex Index, IReadOnlyList<FeaturePartsSnapshot> Originals);

    // ----- Measurement -----

    private void UpdateMeasurementStatus() {
        if (_editorMode == EditorMode.DrawLine) {
            // Draw hover owns the status while a draft exists; otherwise explain how drawing works.
            if (!Editor.HasDraftLine) _viewModel.MeasurementStatus = L.GetString("Main.Measure.DrawHint");
            return;
        }
        if (_editorMode == EditorMode.Pen) {
            _viewModel.MeasurementStatus = L.GetString("Main.Measure.PenHint");
            return;
        }
        if (_editorMode == EditorMode.QuickSelect) {
            _viewModel.MeasurementStatus = L.GetString("Main.Status.QuickSelectHint");
            return;
        }

        var selected = Selection.Features;
        if (selected.Count == 0) {
            _viewModel.MeasurementStatus = "";
            return;
        }

        var measurement = FeatureMeasurementService.Measure(selected);
        var parts = new List<string>();
        if (measurement.LengthMeters > 0) parts.Add(L.Format("Main.Measure.Length", GeoMeasurement.FormatLength(measurement.LengthMeters)));
        if (measurement.AreaSquareMeters > 0) parts.Add(L.Format("Main.Measure.Area", GeoMeasurement.FormatArea(measurement.AreaSquareMeters)));
        if (measurement.VertexCount > 0) parts.Add(L.Format("Main.Measure.Vertices", measurement.VertexCount));
        _viewModel.MeasurementStatus = string.Join("  ", parts);
    }

    private void ShowTopologyMessage(string message) {
        MessageBox.Show(this, message, L.GetString("Main.Menu.Topology"), MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
