using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using WPF_OpenStreetmap_Editor.Controls;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public partial class OsmDownloadWindow : Window {
    private const string OsmTileTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
    private const int MinimumZoom = 1;
    private const int MaximumZoom = 19;
    private const int TileWorkerCount = 2;
    private readonly Func<GeoBounds, IProgress<OsmDownloadStage>, CancellationToken, Task<bool>> _downloadAsync;
    private readonly TileService _tileService = new();
    private GeoPoint _center = new(0, 0);
    private int _zoom = MinimumZoom;
    private Point? _selectionStart;
    private Point? _panStart;
    private CancellationTokenSource? _renderCts;
    private CancellationTokenSource? _renderDebounceCts;
    private CancellationTokenSource? _downloadCts;
    private bool _isDownloading;
    private static LocalizationService L => LocalizationService.Instance;

    public OsmDownloadWindow(
        Func<GeoBounds, IProgress<OsmDownloadStage>, CancellationToken, Task<bool>> downloadAsync) {
        InitializeComponent();
        ThemeService.ApplyWindowTheme(this);
        _downloadAsync = downloadAsync ?? throw new ArgumentNullException(nameof(downloadAsync));
        _tileService.TileTemplate = OsmTileTemplate;
        _tileService.ApplySourceOptions(MaximumZoom, MaximumZoom);
        Loaded += (_, _) => ScheduleRender();
        Closed += OsmDownloadWindow_Closed;
        UpdateZoomText();
    }

    public GeoBounds? SelectedBounds { get; private set; }

    private async Task RenderTilesAsync() {
        var viewportWidth = MapViewport.ActualWidth;
        var viewportHeight = MapViewport.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0) return;

        _renderCts?.Cancel();
        _renderCts?.Dispose();
        var renderCts = new CancellationTokenSource();
        _renderCts = renderCts;
        var ct = renderCts.Token;
        var renderZoom = _zoom;
        var (centerX, centerY) = GeoConverter.LatLonToPixelXY(
            _center.Latitude,
            _center.Longitude,
            renderZoom);
        var tileRange = TileRenderLayout.GetVisibleTileRange(
            centerX,
            centerY,
            viewportWidth,
            viewportHeight,
            renderZoom,
            tileBuffer: 1);
        var requests = new List<(int X, int Y)>();
        for (var y = tileRange.StartY; y <= tileRange.EndY; y++) {
            for (var x = tileRange.StartX; x <= tileRange.EndX; x++) {
                requests.Add((x, y));
            }
        }

        var tiles = new List<TileRenderItem>();
        var tilesLock = new object();
        try {
            await Parallel.ForEachAsync(
                requests,
                new ParallelOptions {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = TileWorkerCount
                },
                async (request, token) => {
                    var source = await TileImageLoader.Shared.LoadAsync(
                        _tileService,
                        renderZoom,
                        request.X,
                        request.Y,
                        accessToken: null,
                        token).ConfigureAwait(false);
                    if (source is null || token.IsCancellationRequested) return;
                    var placement = TileRenderLayout.GetTilePlacement(
                        request.X,
                        request.Y,
                        centerX,
                        centerY,
                        viewportWidth,
                        viewportHeight);
                    lock (tilesLock) {
                        tiles.Add(new TileRenderItem(source, placement));
                    }
                });
        } catch (OperationCanceledException) {
            return;
        }

        if (ct.IsCancellationRequested || !ReferenceEquals(_renderCts, renderCts)) return;
        TileLayer.RenderTransform = Transform.Identity;
        TileLayer.SetTiles(tiles.OrderBy(static tile => tile.Placement.Top).ThenBy(static tile => tile.Placement.Left));
        if (tiles.Count == 0 && SelectedBounds is null) {
            SelectionStatusTextBlock.Text = L.GetString("Osm.Download.TileUnavailable");
        }
    }

    private void MapViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (_panStart is not null) return;
        MapViewport.Focus();
        _selectionStart = e.GetPosition(MapViewport);
        ShowSelectionRectangle(new Rect(_selectionStart.Value, _selectionStart.Value));
        MapViewport.CaptureMouse();
        e.Handled = true;
    }

    private void MapViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        if (_selectionStart is not { } start) return;
        var selection = new Rect(start, e.GetPosition(MapViewport));
        _selectionStart = null;
        MapViewport.ReleaseMouseCapture();
        if (selection.Width < 4 || selection.Height < 4) {
            ClearSelection();
            e.Handled = true;
            return;
        }

        SelectedBounds = VectorMapInteraction.ScreenRectToGeoBounds(
            selection,
            _center.Latitude,
            _center.Longitude,
            _zoom,
            new Size(MapViewport.ActualWidth, MapViewport.ActualHeight));
        UpdateSelectionStatus();
        e.Handled = true;
    }

    private void MapViewport_MouseRightButtonDown(object sender, MouseButtonEventArgs e) {
        if (_selectionStart is not null) return;
        MapViewport.Focus();
        _panStart = e.GetPosition(MapViewport);
        MapViewport.Cursor = Cursors.Hand;
        MapViewport.CaptureMouse();
        e.Handled = true;
    }

    private void MapViewport_MouseRightButtonUp(object sender, MouseButtonEventArgs e) {
        if (_panStart is not { } start) return;
        var delta = e.GetPosition(MapViewport) - start;
        _panStart = null;
        MapViewport.ReleaseMouseCapture();
        MapViewport.Cursor = Cursors.Cross;
        TileLayer.RenderTransform = Transform.Identity;
        _center = VectorMapInteraction.GetCenterAfterPan(_center, delta, _zoom);
        ClearSelection();
        ScheduleRender();
        e.Handled = true;
    }

    private void MapViewport_MouseMove(object sender, MouseEventArgs e) {
        var position = e.GetPosition(MapViewport);
        if (_selectionStart is { } selectionStart) {
            ShowSelectionRectangle(new Rect(selectionStart, position));
        } else if (_panStart is { } panStart) {
            var delta = position - panStart;
            TileLayer.RenderTransform = new TranslateTransform(delta.X, delta.Y);
        }
    }

    private void MapViewport_MouseWheel(object sender, MouseWheelEventArgs e) {
        ChangeZoom(e.Delta > 0 ? 1 : -1, e.GetPosition(MapViewport));
        e.Handled = true;
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) {
        ChangeZoom(1, GetViewportCenter());
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e) {
        ChangeZoom(-1, GetViewportCenter());
    }

    private void ChangeZoom(int delta, Point anchor) {
        var nextZoom = Math.Clamp(_zoom + delta, MinimumZoom, MaximumZoom);
        if (nextZoom == _zoom) return;
        _center = VectorMapInteraction.GetCenterAfterZoom(
            _center,
            _zoom,
            nextZoom,
            anchor,
            new Size(MapViewport.ActualWidth, MapViewport.ActualHeight));
        var preview = TileLayer.RenderTransform.Value;
        var scale = Math.Pow(2, nextZoom - _zoom);
        preview.ScaleAt(scale, scale, anchor.X, anchor.Y);
        TileLayer.RenderTransform = new MatrixTransform(preview);
        _zoom = nextZoom;
        ClearSelection();
        UpdateZoomText();
        ScheduleRender(90);
    }

    private Point GetViewportCenter() {
        return new Point(MapViewport.ActualWidth / 2.0, MapViewport.ActualHeight / 2.0);
    }

    private void ShowSelectionRectangle(Rect rect) {
        Canvas.SetLeft(SelectionRectangle, rect.Left);
        Canvas.SetTop(SelectionRectangle, rect.Top);
        SelectionRectangle.Width = rect.Width;
        SelectionRectangle.Height = rect.Height;
        SelectionRectangle.Visibility = Visibility.Visible;
    }

    private void ClearSelection() {
        SelectedBounds = null;
        SelectionRectangle.Visibility = Visibility.Collapsed;
        SetStatus(L.GetString("Osm.Download.NoSelection"));
        DownloadButton.IsEnabled = false;
    }

    private void UpdateSelectionStatus() {
        if (SelectedBounds is not { } bounds) {
            ClearSelection();
            return;
        }

        var area = (bounds.MaxLongitude - bounds.MinLongitude) *
            (bounds.MaxLatitude - bounds.MinLatitude);
        try {
            OsmApiClient.ValidateDownloadBounds(bounds);
            if (OsmApiClient.RequiresOverpassFallback(bounds)) {
                SetStatus(
                    L.Format("Osm.Download.AreaOverpass", area),
                    isError: true);
            } else {
                SetStatus(
                    L.Format(
                        "Osm.Download.AreaSelected",
                        bounds.MinLongitude,
                        bounds.MinLatitude,
                        bounds.MaxLongitude,
                        bounds.MaxLatitude,
                        area));
            }
            DownloadButton.IsEnabled = true;
        } catch (InvalidDataException ex) {
            SetStatus(ex.Message, isError: true);
            DownloadButton.IsEnabled = false;
        }
    }

    private void UpdateZoomText() {
        ZoomTextBlock.Text = L.Format("Osm.Download.ZoomLevel", _zoom);
    }

    private async void Download_Click(object sender, RoutedEventArgs e) {
        if (SelectedBounds is not { } bounds) return;
        try {
            OsmApiClient.ValidateDownloadBounds(bounds);
            SetDownloadState(isDownloading: true);
            _downloadCts = new CancellationTokenSource();
            var progress = new Progress<OsmDownloadStage>(stage => SetStatus(GetProgressMessage(stage)));
            if (await _downloadAsync(bounds, progress, _downloadCts.Token)) {
                SetDownloadState(isDownloading: false);
                DialogResult = true;
                return;
            }

            SetStatus(L.GetString("Osm.Download.CanceledRetry"));
        } catch (OperationCanceledException) {
            SetStatus(L.GetString("Osm.Download.CanceledRetry"));
        } catch (Exception ex) {
            Logger.Error("OSM download failed", ex);
            SetStatus(OsmDownloadErrorFormatter.GetMessage(ex), isError: true);
        } finally {
            _downloadCts?.Dispose();
            _downloadCts = null;
            if (IsVisible) SetDownloadState(isDownloading: false);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) {
        if (_isDownloading) {
            CancelButton.IsEnabled = false;
            SetStatus(L.GetString("Osm.Download.Canceling"));
            _downloadCts?.Cancel();
            return;
        }

        DialogResult = false;
    }

    private void SetDownloadState(bool isDownloading) {
        _isDownloading = isDownloading;
        MapViewport.IsEnabled = !isDownloading;
        DownloadButton.IsEnabled = !isDownloading && SelectedBounds is not null;
        CancelButton.IsEnabled = true;
        DownloadProgressBar.Visibility = isDownloading ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetStatus(string message, bool isError = false) {
        SelectionStatusTextBlock.Text = message;
        SelectionStatusTextBlock.SetResourceReference(
            TextBlock.ForegroundProperty,
            isError ? "Theme.ErrorBrush" : "Theme.MutedTextBrush");
    }

    private static string GetProgressMessage(OsmDownloadStage stage) {
        return stage switch {
            OsmDownloadStage.StandardApi => L.GetString("Osm.Download.Progress.StandardApi"),
            OsmDownloadStage.OverpassFallback => L.GetString("Osm.Download.Progress.Overpass"),
            OsmDownloadStage.Importing => L.GetString("Osm.Download.Progress.Importing"),
            _ => L.GetString("Osm.Download.Progress.Default")
        };
    }

    private void MapViewport_SizeChanged(object sender, SizeChangedEventArgs e) {
        if (IsLoaded) ScheduleRender(120);
    }

    private void ScheduleRender(int delayMilliseconds = 0) {
        _renderDebounceCts?.Cancel();
        _renderDebounceCts?.Dispose();
        var debounceCts = new CancellationTokenSource();
        _renderDebounceCts = debounceCts;
        _ = RenderAfterDelayAsync(delayMilliseconds, debounceCts);
    }

    private async Task RenderAfterDelayAsync(int delayMilliseconds, CancellationTokenSource debounceCts) {
        try {
            if (delayMilliseconds > 0) {
                await Task.Delay(delayMilliseconds, debounceCts.Token);
            }
            if (!debounceCts.IsCancellationRequested && ReferenceEquals(_renderDebounceCts, debounceCts)) {
                await RenderTilesAsync();
            }
        } catch (OperationCanceledException) {
        }
    }

    private void Attribution_RequestNavigate(object sender, RequestNavigateEventArgs e) {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void OsmDownloadWindow_Closed(object? sender, EventArgs e) {
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        _downloadCts = null;
        _renderDebounceCts?.Cancel();
        _renderDebounceCts?.Dispose();
        _renderDebounceCts = null;
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = null;
        _tileService.Dispose();
    }

    protected override void OnClosing(CancelEventArgs e) {
        if (_isDownloading) {
            e.Cancel = true;
            Cancel_Click(this, new RoutedEventArgs());
            return;
        }

        base.OnClosing(e);
    }
}
