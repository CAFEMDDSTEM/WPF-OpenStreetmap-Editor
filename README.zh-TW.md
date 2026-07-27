# WOSM

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md) | [Deutsch](README.de.md)

WOSM 是 WPF OpenStreetMap Editor 的簡稱，是一個使用 C# / WPF 編寫的 OpenStreetMap 編輯器，長期目標是成為 JOSM 的實用替代品。

目前應用程式可用於載入相容 OpenStreetMap 的圖磚圖層、預覽地圖影像，並管理可重複使用的圖磚服務 URL。

> 專案狀態：早期開發中。目前應用程式著重於圖磚渲染、圖層設定、快取和配套工程基礎設施。

## 功能特色

- 載入 XYZ、TMS、ArcGIS 風格以及類似 WMTS 的圖磚 URL 範本。
- 支援 `{z}`、`{x}`、`{y}`、`{-y}`、`{s}`、`{switch:a,b,c}`、`{zoom}`、`{TileMatrix}`、`{TileCol}`、`{TileRow}`、`{access_token}` 等常見佔位符。
- 在 WPF Canvas 中渲染地圖圖磚，並支援滑鼠平移和縮放控制。
- 將下載的圖磚快取到應用程式執行目錄。
- 使用 `layers.json` 儲存可重複使用的圖層 URL。
- 記錄圖磚請求和載入錯誤，便於排查問題。
- 提供單元測試覆蓋座標轉換、URL 解析、快取路徑和執行時期路徑處理。

## 環境需求

- Windows
- .NET SDK 10.0 或更新版本

## 專案結構

```text
src/WPF-OpenStreetmap-Editor/          WPF 應用程式原始碼
tests/WPF-OpenStreetmap-Editor.Tests/  單元測試
docs/                                  貢獻者文件
scripts/                               本機 CLI 輔助指令碼
.github/workflows/                     CI 工作流程定義
```

## 快速開始

複製儲存庫後，在儲存庫根目錄執行建置：

```powershell
.\scripts\build.ps1
```

執行測試套件：

```powershell
.\scripts\test.ps1
```

也可以直接透過 .NET CLI 執行應用程式：

```powershell
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj
```

## 使用方式

1. 啟動應用程式。
2. 在地圖 URL 輸入框中填寫圖磚 URL 範本。
3. 如果服務商需要 `{access_token}`，請填寫存取權杖。
4. 載入地圖後，可使用滑鼠滾輪、`+` / `-`，或 `Page Up` / `Page Down` 縮放。
5. 拖曳地圖畫布進行平移。
6. 從工具選單開啟圖層視窗，管理已儲存的圖層 URL。

## 圖磚服務說明

圖磚服務商可能會設定請求頻率限制、署名要求或存取限制。使用服務 URL 前請先確認服務條款，尤其是在使用正式環境憑證或公共圖磚伺服器時。

## 執行時期檔案

應用程式會在程式基底目錄下寫入執行時期資料：

- `Cache/tiles/`：下載的圖磚圖片
- `layers.json`：已儲存的圖層 URL
- `tile_requests.log`：圖磚請求診斷記錄

## 文件

貢獻和維護文件位於 [docs/README.md](docs/README.md)：

- [開發指南](docs/development.md)
- [程式碼風格](docs/code-style.md)
- [測試指南](docs/testing.md)
- [Issue 與錯誤回報](docs/issues.md)
- [Pull Request](docs/pull-requests.md)

## 參與貢獻

歡迎提交 Issue 和 Pull Request。請使用 GitHub Issue 與 Pull Request 範本，在提交變更前執行測試套件，並遵循 [docs/code-style.md](docs/code-style.md) 中的專案程式碼風格。

## 授權條款

本專案基於 GNU General Public License v3.0 授權。詳見 [LICENSE.txt](LICENSE.txt)。
