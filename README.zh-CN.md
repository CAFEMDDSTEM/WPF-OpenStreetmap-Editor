# WOSM

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md) | [Deutsch](README.de.md)

WOSM 是 WPF OpenStreetMap Editor 的简称，是一个使用 C# / WPF 编写的 OpenStreetMap 编辑器，长期目标是成为 JOSM 的实用替代品。

当前应用可用于加载兼容 OpenStreetMap 的瓦片图层、预览地图影像，并管理可复用的瓦片服务 URL。

> 项目状态：早期开发中。当前应用重点覆盖瓦片渲染、图层配置、缓存和配套工程基础设施。

## 功能特性

- 加载 XYZ、TMS、ArcGIS 风格以及类似 WMTS 的瓦片 URL 模板。
- 支持 `{z}`、`{x}`、`{y}`、`{-y}`、`{s}`、`{switch:a,b,c}`、`{zoom}`、`{TileMatrix}`、`{TileCol}`、`{TileRow}`、`{access_token}` 等常见占位符。
- 在 WPF Canvas 中渲染地图瓦片，并支持鼠标平移和缩放控制。
- 将下载的瓦片缓存到应用运行目录。
- 使用 `layers.json` 保存可复用的图层 URL。
- 记录瓦片请求和加载错误，便于排查问题。
- 提供单元测试覆盖坐标转换、URL 解析、缓存路径和运行时路径处理。

## 环境要求

- Windows
- .NET SDK 10.0 或更新版本

## 项目结构

```text
src/WPF-OpenStreetmap-Editor/          WPF 应用源码
tests/WPF-OpenStreetmap-Editor.Tests/  单元测试
docs/                                  贡献者文档
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
2. 在地图 URL 输入框中填写瓦片 URL 模板。
3. 如果服务商需要 `{access_token}`，请填写访问令牌。
4. 加载地图后，可使用鼠标滚轮、`+` / `-`，或 `Page Up` / `Page Down` 缩放。
5. 拖动地图画布进行平移。
6. 从工具菜单打开图层窗口，管理已保存的图层 URL。

## 瓦片服务说明

瓦片服务商可能会设置请求频率限制、署名要求或访问限制。使用服务 URL 前请先确认服务条款，尤其是在使用生产凭据或公共瓦片服务器时。

## 运行时文件

应用会在程序基目录下写入运行时数据：

- `Cache/tiles/`：下载的瓦片图片
- `layers.json`：已保存的图层 URL
- `tile_requests.log`：瓦片请求诊断日志

## 文档

贡献和维护文档位于 [docs/README.md](docs/README.md)：

- [开发指南](docs/development.md)
- [代码风格](docs/code-style.md)
- [测试指南](docs/testing.md)
- [Issue 与错误报告](docs/issues.md)
- [Pull Request](docs/pull-requests.md)

## 参与贡献

欢迎提交 Issue 和 Pull Request。请使用 GitHub Issue 与 Pull Request 模板，在提交变更前运行测试套件，并遵循 [docs/code-style.md](docs/code-style.md) 中的项目代码风格。

## 许可证

本项目基于 GNU General Public License v3.0 授权。详见 [LICENSE.txt](LICENSE.txt)。
