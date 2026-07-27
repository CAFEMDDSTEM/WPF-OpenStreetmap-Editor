# WOSM

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md) | [Deutsch](README.de.md)

WOSM 是 WPF OpenStreetMap Editor 的简称，是一个使用 C# / WPF 编写的 OpenStreetMap 编辑器，长期目标是成为 JOSM 的实用替代品。

当前应用可用于加载兼容 OpenStreetMap 的瓦片图层、导入常见矢量地图格式、预览和编辑地图要素，并管理可复用的影像、主题和插件。

> 项目状态：早期开发中。当前应用重点覆盖影像渲染、矢量数据处理、本地编辑、主题、插件基础设施和基础 OpenStreetMap 传输流程。

## 功能特性

- 加载 XYZ、TMS、ArcGIS 风格以及类似 WMTS 的瓦片 URL 模板。
- 支持 `{z}`、`{x}`、`{y}`、`{-y}`、`{s}`、`{switch:a,b,c}`、`{zoom}`、`{TileMatrix}`、`{TileCol}`、`{TileRow}`、`{access_token}` 等常见占位符。
- 在 WPF Canvas 中渲染地图瓦片，并支持鼠标平移和缩放控制。
- 导入 `.osm`、`.pbf`、Shapefile、GeoJSON、GML、KML/KMZ 和 GPX 地图数据。
- 将编辑后的数据保存为 GeoJSON、OpenStreetMap XML、GPX、KML 或 GML。PBF、Shapefile 和 KMZ 当前仅支持导入。
- 在影像上选择、框选、隐藏、删除、添加点要素并绘制线要素。
- 通过内置 OpenStreetMap 传输插件按框选范围下载 OSM 数据，并在预览后上传创建、修改和删除变更。
- 将下载的瓦片缓存到应用运行目录。
- 使用 `layers.json` 保存可复用的图层 URL。
- 记录瓦片请求和加载错误，便于排查问题。
- 支持跟随系统、浅色、深色以及经过校验的第三方 ZIP 或 7z 主题包。
- 安装 addon、沙箱进程和经过明确信任的原生插件包。
- 提供单元测试覆盖设置、渲染、启动诊断、缓存、空间格式、主题、插件、OSM 传输、坐标转换和 URL 解析。

## 环境要求

- Windows
- .NET SDK 10.0 或更新版本

## 项目结构

```text
src/WPF-OpenStreetmap-Editor/          WPF 应用源码
tests/WPF-OpenStreetmap-Editor.Tests/  单元测试
docs/                                  贡献者文档
sdk/native/                            原生插件 C ABI 头文件
scripts/                               本地 CLI 辅助脚本
.github/workflows/                     CI 工作流定义
```

## 快速开始

克隆仓库后，在仓库根目录执行构建：

```powershell
.\scripts\build.ps1
```

运行测试套件：

```powershell
.\scripts\test.ps1
```

也可以直接通过 .NET CLI 运行应用：

```powershell
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj
```

## 使用方式

1. 启动应用。
2. 打开 **工具 > 设置** 选择影像源、添加自定义瓦片 URL 模板或切换主题。
3. 如果影像服务商需要 `{access_token}`，请在设置中填写访问令牌。影像访问令牌仅用于当前会话，不会写入磁盘。
4. 使用 **影像** 菜单将图源添加为栅格地图图层。
5. 使用 **文件 > 打开** 导入矢量地图数据，或使用内置 OpenStreetMap 传输工具栏选择并下载 OSM 范围。
6. 使用鼠标滚轮、`+` / `-`，或 `Page Up` / `Page Down` 缩放；拖动地图平移；使用编辑工具栏选择、框选、隐藏、删除、添加点或绘制线。
7. 使用 **文件 > 保存** 或 **另存为** 保存支持的矢量格式。上传 OSM 前请先检查预览内容。

## 瓦片服务说明

瓦片服务商可能会设置请求频率限制、署名要求或访问限制。使用服务 URL 前请先确认服务条款，尤其是在使用生产凭据或公共瓦片服务器时。

## 运行时文件

应用会在 `%LOCALAPPDATA%\WPF-OpenStreetmap-Editor\` 下写入运行时数据：

- `Cache/tiles/`：下载的瓦片图片
- `layers.json`：已保存的图层 URL
- `settings.json`：影像和应用设置
- `osm.accounts.json`：OSM 账号元数据；访问令牌单独存储在 Windows 凭据库
- `Plugins/` 与 `plugins.state.json`：已安装插件和原生插件信任状态
- `Themes/`：已安装的第三方主题
- `window_state.json`：保存的窗口位置和大小
- `tile_requests.log` 与 `startup.log`：诊断日志

## 文档

贡献和维护文档位于 [docs/README.md](docs/README.md)：

- [开发指南](docs/development.md)
- [代码风格](docs/code-style.md)
- [测试指南](docs/testing.md)
- [地图数据与 OSM 传输](docs/map-data.md)
- [第三方主题格式](docs/themes.md)
- [插件架构](docs/plugins.md)
- [Issue 与错误报告](docs/issues.md)
- [Pull Request](docs/pull-requests.md)

## 参与贡献

欢迎提交 Issue 和 Pull Request。请使用 GitHub Issue 与 Pull Request 模板，在提交变更前运行测试套件，并遵循 [docs/code-style.md](docs/code-style.md) 中的项目代码风格。

## 许可证

本项目基于 GNU General Public License v3.0 授权。详见 [LICENSE.txt](LICENSE.txt)。
