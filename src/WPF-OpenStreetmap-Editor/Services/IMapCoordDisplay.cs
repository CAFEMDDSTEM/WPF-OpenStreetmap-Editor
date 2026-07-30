using System.Windows;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

/// <summary>
/// 地图坐标显示接口。预留接口，当前未在任何位置调用。
///
/// 调用约定（MainWindow.xaml.cs）：
///   1. 构造：_coordDisplay = new MapCoordDisplay();
///      _coordDisplay.AttachTo(SomePanel); // 如 FeatureDataGrid 父级 DockPanel
///   2. MapCanvas_MouseMove 中：
///      var geo = VectorMapInteraction.ScreenToGeo(
///          e.GetPosition(MapViewport),
///          _centerLat, _centerLon, zoom,
///          new Size(MapViewport.ActualWidth, MapViewport.ActualHeight),
///          _panOffsetX, _panOffsetY, _displayTransform);
///      _coordDisplay.Update(geo);
///   3. 鼠标离开地图时：_coordDisplay.Clear();
///   4. 窗口关闭时：_coordDisplay.Dispose();
/// </summary>
public interface IMapCoordDisplay : IDisposable {
    /// <summary>更新显示指定地理坐标（自动格式化）</summary>
    void Update(GeoPoint location);

    /// <summary>清空坐标显示</summary>
    void Clear();

    /// <summary>将显示控件挂载到父级容器</summary>
    void AttachTo(FrameworkElement parent);

    /// <summary>从父级容器卸载显示控件</summary>
    void Detach();

    /// <summary>是否可见</summary>
    bool IsVisible { get; set; }
}
