# WOSM

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md) | [Deutsch](README.de.md)

WOSM 是 WPF OpenStreetMap Editor 的简称，是一个使用 C# / WPF 编写的 OpenStreetMap 编辑器，长期目标是成为 JOSM 的实用替代品。

当前应用可加载兼容 OpenStreetMap 的瓦片图层，导入常见矢量地图格式，预览和编辑地图要素，并管理可复用的影像、主题和插件。

> 项目状态：早期 Alpha。WOSM v0.2.0-alpha.1 新增五语界面本地化、带投影处理的导入、BetterID AI 辅助 OSM 变更集注释，以及 OSM 编辑流程改进。请预期仍有不完善之处，并在发送任何 OpenStreetMap 上传前仔细检查。

## 发布

最新 Alpha 版提供 Windows x64 自包含软件包：

[下载 WOSM v0.2.0-alpha.1](https://github.com/CAFEMDDSTEM/WPF-OpenStreetmap-Editor/releases/download/v0.2.0-alpha.1/WOSM-v0.2.0-alpha.1-win-x64.zip)

登录 OpenStreetMap 或上传变更前，请使用 [`SHA256SUMS-v0.2.0-alpha.1.txt`](https://github.com/CAFEMDDSTEM/WPF-OpenStreetmap-Editor/releases/download/v0.2.0-alpha.1/SHA256SUMS-v0.2.0-alpha.1.txt) 校验 ZIP：

```powershell
Get-FileHash .\WOSM-v0.2.0-alpha.1-win-x64.zip -Algorithm SHA256
```

解压 ZIP 后运行 `WPF-OpenStreetmap-Editor.exe`。同一可执行文件也支持 `help`、`import`、`convert`、`download`、`changeset` 和 `upload` 等命令行数据流程命令。此构建未签名，Windows 可能显示 SmartScreen 提示。可复现的问题请提交到 [GitHub Issues](https://github.com/CAFEMDDSTEM/WPF-OpenStreetmap-Editor/issues)。

## 功能特性

- 加载 XYZ、TMS、ArcGIS 风格、类似 WMTS 和 Bing 的影像源。
- 支持 `{z}`、`{x}`、`{y}`、`{-y}`、`{s}`、`{switch:a,b,c}`、`{zoom}`、`{TileMatrix}`、`{TileCol}`、`{TileRow}` 和 `{access_token}` 等常见占位符。
- 管理可复用的影像预设、访问令牌、署名、缩放级别限制和无瓦片标记。
- 使用可见性、主图层选择、不透明度、鼠标平移和缩放控件渲染多个影像图层。
- 在系统语言、英语、简体中文、繁体中文、日语和德语之间切换界面语言。
- 导入 `.osm`、`.pbf`、Shapefile、GeoJSON、GML、KML/KMZ 和 GPX 地图数据。
- 为没有 `.prj` 文件的投影 GeoJSON、GML 和 Shapefile 选择默认源投影；存在 Shapefile `.prj` 时会优先使用该文件声明的 CRS。
- 将编辑后的数据保存为 GeoJSON、OpenStreetMap XML、GPX、KML 或 GML。PBF、Shapefile 和 KMZ 当前仅支持导入。
- 在影像上选择、框选、隐藏、删除、复制、粘贴、创建副本、添加点、绘制线、旋转、移动和直角化要素。
- 为单个选定要素请求 BetterID AI OSM 标签建议，并在上传前生成待审核的 OSM 变更集注释草稿。
- 下载选定范围的 OSM 数据，并上传经过审核的创建、修改和删除变更；可选的第一方 OpenStreetMap 传输插件可添加工具栏和菜单入口。
- 通过命令行汇总导入文件、转换支持的矢量格式、下载边界框 OSM 数据、预览 `.osc` 变更集、执行受保护的 OSM 上传，并启动 GUI。
- 使用有界内存和磁盘缓存，验证下载的图像，并在加载期间回退到缓存的父级瓦片。
- 运行启动诊断，并记录启动或瓦片加载失败信息。
- 在系统、浅色、深色和经过校验的第三方 ZIP 或 7z 主题包之间切换。
- 安装 addon、沙箱进程插件和经过明确信任的原生插件包。
- 将设置、图层、窗口状态、缓存和日志保存在当前用户的本地应用数据目录中。
- 提供聚焦的单元测试，覆盖设置、渲染、启动诊断、缓存、空间格式、主题、插件、OSM 传输、坐标转换和 URL 解析。

## 环境要求

- Windows 10 或更高版本，x64，用于预构建 Windows 版本
- 从源码构建时需要 .NET SDK 10.0 或更高版本

## 项目结构

```text
src/WPF-OpenStreetmap-Editor/          WPF 应用源代码
tests/WPF-OpenStreetmap-Editor.Tests/  单元测试
docs/                                  贡献者文档
sdk/native/                            原生插件 C ABI 头文件
scripts/                               本地 CLI 辅助脚本
.github/workflows/                     CI 工作流定义
```

## 快速开始

使用预构建应用时，请从 [Releases 页面](https://github.com/CAFEMDDSTEM/WPF-OpenStreetmap-Editor/releases)下载 ZIP，解压到可写目录，然后运行 `WPF-OpenStreetmap-Editor.exe`。

从源码构建时，克隆仓库后运行：

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

从源码查看 WOSM 命令行帮助：

```powershell
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- help
```

## 使用方式

1. 启动应用。
2. 打开 **工具 > 设置**，选择影像源、添加自定义瓦片 URL 模板、切换语言、配置导入投影默认值或切换主题。
3. 当影像提供商需要 `{access_token}` 时，在设置中输入访问令牌。影像访问令牌仅用于当前会话，不会写入磁盘。
4. 使用 **影像** 菜单将来源添加为栅格地图图层。
5. 使用 **文件 > 打开** 导入矢量地图数据，或使用 `Ctrl+Shift+Down` 选择并下载 OSM 边界框。
6. 使用鼠标滚轮、`+` / `-` 或 `Page Up` / `Page Down` 缩放，拖动地图平移，并使用编辑工具栏选择、框选、隐藏、删除、添加点或绘制线。
7. 使用 `Ctrl+C`、`Ctrl+V`、`Ctrl+Z` 和 `Ctrl+Shift+D` 复制、粘贴、撤销和创建选定要素副本。
8. 按 `R` 或 `M` 后用鼠标旋转或移动选定要素，再按 `Enter` 或单击左键应用。在选择模式下，拖动已选要素也可以移动当前选择。
9. 输入 `r20`、`mx2` 或 `mx20y20` 等精确命令后按 `Enter`。旋转值单位为度；移动值单位为分米，`x` 表示向东，`y` 表示向北。
10. 按 `Q` 直角化选定的线或面要素。
11. 选择单个要素，并使用 AI 标签助手向 BetterID AI 请求建议的 OSM 标签。应用前请检查并选择建议。
12. 使用 **文件 > 保存** 或 **另存为** 保存支持的矢量格式。向配置的 OSM API 上传前请检查预览；上传对话框可以请求 BetterID AI 生成变更集注释草稿。

命令行工作流可在从源码运行时通过 `--` 传入 CLI 命令：

```powershell
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- import --input map.geojson
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- convert --input map.geojson --output map.gpx
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- download --bbox minLon,minLat,maxLon,maxLat --output data.osm
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- changeset --input map.geojson --output preview.osc
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- upload --input map.geojson --dry-run --output preview.osc
```

真实 OSM 上传需要 `--yes`、`--comment`，以及来自 `--token`、`--token-env`、`OSM_ACCESS_TOKEN` 或当前 WOSM 账户的凭据。

## 瓦片服务说明

瓦片提供商可能设置速率限制、署名要求或访问限制。使用服务 URL 前请确认提供商条款，尤其是使用生产凭据或公共瓦片服务器时。

## 运行时文件

应用会在 `%LOCALAPPDATA%\WPF-OpenStreetmap-Editor\` 下写入运行时数据：

- `Cache/tiles/`：下载的瓦片图像
- `layers.json`：已保存的图层 URL
- `settings.json`：影像和应用设置
- `osm.accounts.json`：OSM 账户元数据；密码和 OAuth 访问令牌单独存储在 Windows 凭据管理器中
- `Plugins/` 和 `plugins.state.json`：已安装插件和原生插件信任状态
- `Themes/`：已安装的第三方主题
- `window_state.json`：保存的窗口位置和大小
- `tile_requests.log` 和 `startup.log`：诊断日志

## 文档

贡献和维护文档位于 [docs/README.zh-CN.md](docs/README.zh-CN.md)：

- [开发指南](docs/development.md)
- [代码风格](docs/code-style.md)
- [测试指南](docs/testing.md)
- [地图数据、CLI 工作流和 OSM 传输](docs/map-data.md)
- [主题包](docs/themes.md)
- [插件架构](docs/plugins.md)
- [Issue 与错误报告](docs/issues.md)
- [Pull Request](docs/pull-requests.md)

## 参与贡献

欢迎提交 Issue 和 Pull Request。请使用 GitHub Issue 和 Pull Request 模板，提交变更前运行测试套件，并遵循 [docs/code-style.md](docs/code-style.md) 中的项目代码风格。

## 许可证

本项目基于 GNU General Public License v3.0 授权。详见 [LICENSE.txt](LICENSE.txt)。
