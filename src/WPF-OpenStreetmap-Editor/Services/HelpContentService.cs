using System.IO;
using System.Reflection;
using System.Text;

namespace WPF_OpenStreetmap_Editor.Services;

internal sealed record HelpContent(
    string ProgramName,
    string Version,
    string LicenseName,
    string LicenseText,
    IReadOnlyList<HelpSection> Sections,
    IReadOnlyList<ProgramInfoItem> ProgramInfo);

internal sealed record HelpSection(string Title, IReadOnlyList<string> Items);

internal sealed record ProgramInfoItem(string Name, string Value);

internal static class HelpContentService {
    private const string LicenseResourceName = "WPF_OpenStreetmap_Editor.LICENSE.txt";

    public static HelpContent Create() {
        var assembly = typeof(HelpContentService).Assembly;
        var version = GetVersionText(assembly);

        return new HelpContent(
            "WPF OpenStreetmap Editor",
            version,
            "GNU General Public License v3.0",
            ReadLicenseText(assembly),
            [
                new HelpSection("快速开始", [
                    "使用“文件 > 打开”导入 GeoJSON、OSM、PBF、Shapefile、GML、KML、KMZ 或 GPX 数据。",
                    "使用“文件 > 保存”或 Ctrl+S 保存当前地图；“另存为”可导出为 GeoJSON、OSM XML、GPX、KML 或 GML。",
                    "地图图层可在右侧图层列表中切换主图层、隐藏显示或移除。"
                ]),
                new HelpSection("地图编辑", [
                    "左侧工具栏提供拖动、选择、框选、画线、添加节点和隐藏选定对象。",
                    "鼠标滚轮、+、-、Page Up 和 Page Down 可缩放地图。",
                    "选择对象后可按 Delete 删除，按 H 隐藏，Esc 取消当前交互。"
                ]),
                new HelpSection("图源与主题", [
                    "“工具 > 设置”可切换主题、导入主题包并管理图源。",
                    "“影像 > 影像选项...”可配置 XYZ/TMS 图源、访问令牌、署名文本和最大缩放层级。",
                    "图源署名会显示在地图右下角，包含链接时可直接打开。"
                ]),
                new HelpSection("OpenStreetMap 与插件", [
                    "“工具 > 插件...”可安装、信任、重新扫描和查看插件详情。",
                    "配置 OSM 账号后，可下载框选区域或上传当前 OSM 修改。",
                    "下载前先按 V 使用框选工具划定区域；上传前请确认变更说明和账号信息。"
                ]),
                new HelpSection("快捷键", [
                    "F1 打开本帮助窗口。",
                    "Ctrl+S 保存地图。",
                    "Ctrl+C 复制选定对象，Ctrl+V 粘贴对象，Ctrl+Z 撤销。",
                    "A 进入画线模式，S 进入选择模式，V 进入框选模式。",
                    "R 进入旋转模式，M 进入移动模式，Q 直角化选定对象，Ctrl+Shift+D 创建选定对象副本。",
                    "输入 r20 旋转 20 度，输入 mx20y20 向东/向北各移动 20 分米，Enter 应用当前绘制、旋转或移动。",
                    "选择模式下按住已选对象并拖动可移动对象。",
                    "Insert 在地图中心添加节点，Delete 删除选定对象，H 隐藏选定对象。"
                ])
            ],
            [
                new ProgramInfoItem("程序", "WPF OpenStreetmap Editor"),
                new ProgramInfoItem("版本", version),
                new ProgramInfoItem("许可证", "GPL v3"),
                new ProgramInfoItem("运行时", $".NET {Environment.Version}"),
                new ProgramInfoItem("主要功能", "离线/在线地图查看、矢量数据导入导出、基础要素编辑、图源管理、主题和插件")
            ]);
    }

    internal static string GetVersionText(Assembly assembly) {
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion)) {
            return informationalVersion.Split('+', 2)[0];
        }

        var version = assembly.GetName().Version;
        if (version is null) return "0.1.0";

        return $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }

    internal static string ReadLicenseText(Assembly assembly) {
        using var stream = assembly.GetManifestResourceStream(LicenseResourceName);
        if (stream is null) {
            return "GPL v3 license text is not available in this build. See https://www.gnu.org/licenses/gpl-3.0.txt";
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
