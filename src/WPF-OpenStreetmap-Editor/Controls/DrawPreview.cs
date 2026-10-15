using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Controls;

/// <summary>
/// Transient draw-mode overlay state: the rubber-band segment from the last placed vertex to the
/// pointer and the snap target under the pointer. Stored in document coordinates so the overlay
/// stays attached to the map while the view pans or zooms.
/// </summary>
public sealed record DrawPreview(GeoPoint? AnchorPoint, GeoPoint PointerPoint, SnapTarget? Snap);
