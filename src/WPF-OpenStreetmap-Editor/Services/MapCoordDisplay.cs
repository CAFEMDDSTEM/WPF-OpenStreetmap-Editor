using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

/// <summary>
/// IMapCoordDisplay 的默认实现：以 DDD°MM′SS.s″（纬度 ≥1°）或 Lat: XX.XXXXXX°（小数度）格式显示经纬度。
///
/// 控件通过 AttachTo 动态挂载到任意 Panel / ContentControl / Decorator，无需 XAML。
/// </summary>
public sealed class MapCoordDisplay : IMapCoordDisplay {
    private readonly Border _container;
    private readonly TextBlock _textBlock;
    private FrameworkElement? _parent;
    private bool _disposed;

    /// <summary>构造坐标显示控件（默认隐藏）</summary>
    public MapCoordDisplay() {
        _textBlock = new TextBlock {
            FontSize = 11,
            FontFamily = new FontFamily("Consolas"),
            VerticalAlignment = VerticalAlignment.Center
        };

        _container = new Border {
            Child = _textBlock,
            Height = 26,
            Padding = new Thickness(8, 0, 8, 0),
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            Visibility = Visibility.Collapsed
        };
    }

    /// <summary>获取或设置控件可见性</summary>
    public bool IsVisible {
        get => _container.Visibility == Visibility.Visible;
        set => _container.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>将显示控件挂载到父级容器底部</summary>
    public void AttachTo(FrameworkElement parent) {
        if (_parent == parent) return;
        Detach();
        _parent = parent;
        if (parent is Panel panel) {
            panel.Children.Add(_container);
        } else if (parent is ContentControl contentControl) {
            contentControl.Content = _container;
        } else if (parent is Decorator decorator) {
            decorator.Child = _container;
        }
    }

    /// <summary>从父级容器卸载</summary>
    public void Detach() {
        if (_parent is Panel panel) {
            panel.Children.Remove(_container);
        } else if (_parent is ContentControl contentControl) {
            contentControl.Content = null;
        } else if (_parent is Decorator decorator) {
            decorator.Child = null;
        }
        _parent = null;
    }

    /// <summary>更新显示坐标（自动选择 DMS 或小数度格式）</summary>
    public void Update(GeoPoint location) {
        _textBlock.Text = FormatCoordinates(location);
        IsVisible = true;
    }

    /// <summary>清空并隐藏</summary>
    public void Clear() {
        _textBlock.Text = "";
        IsVisible = false;
    }

    /// <summary>格式化：纬度 ≥1° 用 DDD°MM′SS.s″，否则用 Lat: XX.XXXXXX°</summary>
    private static string FormatCoordinates(GeoPoint location) {
        var latDir = location.Latitude >= 0 ? 'N' : 'S';
        var lonDir = location.Longitude >= 0 ? 'E' : 'W';
        var lat = Math.Abs(location.Latitude);
        var lon = Math.Abs(location.Longitude);

        if (lat >= 1.0) {
            var latDeg = (int)lat;
            var latMin = (int)((lat - latDeg) * 60);
            var latSec = (lat - latDeg - latMin / 60.0) * 3600;
            var lonDeg = (int)lon;
            var lonMin = (int)((lon - lonDeg) * 60);
            var lonSec = (lon - lonDeg - lonMin / 60.0) * 3600;
            return $"{latDeg:00}°{latMin:00}′{latSec:00.0}″{latDir}  {lonDeg:000}°{lonMin:00}′{lonSec:00.0}″{lonDir}";
        }

        return $"Lat: {lat:F6}°{latDir}  Lon: {lon:F6}°{lonDir}";
    }

    /// <summary>卸载控件并释放资源</summary>
    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        Detach();
    }
}
