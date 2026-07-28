# WOSM

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md) | [Deutsch](README.de.md)

WOSM 是 WPF OpenStreetMap Editor 的簡稱，是一個以 C# / WPF 編寫的 OpenStreetMap 編輯器，長期目標是成為 JOSM 的實用替代方案。

目前應用程式可以載入相容 OpenStreetMap 的圖磚圖層，匯入常見向量地圖格式，預覽和編輯地圖圖徵，並管理可重複使用的影像、佈景主題和外掛程式。

> 專案狀態：Beta 測試。WOSM v0.2.0-beta.1 新增五語介面本地化、支援投影處理的匯入、BetterID AI 輔助 OSM 變更集註解，以及 OSM 編輯流程改進。請預期仍有不完整之處，並在送出任何 OpenStreetMap 上傳前仔細檢查。

## 發行

最新 Beta 測試版提供 Windows x64 自包含套件：

[下載 WOSM v0.2.0-beta.1](https://github.com/CAFEMDDSTEM/WPF-OpenStreetmap-Editor/releases/download/v0.2.0-beta.1/WOSM-v0.2.0-beta.1-win-x64.zip)

登入 OpenStreetMap 或上傳變更前，請使用 [`SHA256SUMS-v0.2.0-beta.1.txt`](https://github.com/CAFEMDDSTEM/WPF-OpenStreetmap-Editor/releases/download/v0.2.0-beta.1/SHA256SUMS-v0.2.0-beta.1.txt) 驗證 ZIP：

```powershell
Get-FileHash .\WOSM-v0.2.0-beta.1-win-x64.zip -Algorithm SHA256
```

解壓縮 ZIP 後執行 `WPF-OpenStreetmap-Editor.exe`。同一個可執行檔也支援 `help`、`import`、`convert`、`download`、`changeset` 和 `upload` 等命令列資料流程命令。此組建未簽署，Windows 可能會顯示 SmartScreen 提示。可重現的問題請回報到 [GitHub Issues](https://github.com/CAFEMDDSTEM/WPF-OpenStreetmap-Editor/issues)。

## 功能特色

- 載入 XYZ、TMS、ArcGIS 風格、類似 WMTS 和 Bing 的影像來源。
- 支援 `{z}`、`{x}`、`{y}`、`{-y}`、`{s}`、`{switch:a,b,c}`、`{zoom}`、`{TileMatrix}`、`{TileCol}`、`{TileRow}` 和 `{access_token}` 等常見預留位置。
- 管理可重複使用的影像預設、存取權杖、署名、縮放限制和無圖磚標記。
- 使用可見性、主要圖層選取、不透明度、滑鼠平移和縮放控制項來呈現多個影像圖層。
- 在系統語言、英文、簡體中文、繁體中文、日文和德文之間切換介面語言。
- 匯入 `.osm`、`.pbf`、Shapefile、GeoJSON、GML、KML/KMZ 和 GPX 地圖資料。
- 為沒有 `.prj` 檔案的投影 GeoJSON、GML 和 Shapefile 選擇預設來源投影；存在 Shapefile `.prj` 時會使用該檔案宣告的 CRS。
- 將編輯後的資料儲存為 GeoJSON、OpenStreetMap XML、GPX、KML 或 GML。PBF、Shapefile 和 KMZ 目前僅支援匯入。
- 在影像上選取、框選、隱藏、刪除、複製、貼上、建立複本、加入點、繪製線、旋轉、移動和正交化圖徵。
- 為單一選取圖徵要求 BetterID AI OSM 標籤建議，並在上傳前產生待審閱的 OSM 變更集註解草稿。
- 下載選取邊界框內的 OSM 資料，並上傳已審閱的建立、修改和刪除變更；選用的第一方 OpenStreetMap 傳輸 addon 可新增工具列和功能表入口。
- 透過命令列彙總匯入的地圖檔案、轉換支援的向量格式、下載邊界框 OSM 資料、預覽 `.osc` 變更集、執行受保護的 OSM 上傳，並啟動 GUI。
- 使用有界記憶體和磁碟快取，驗證下載的影像，並在載入期間回退到已快取的父圖磚。
- 執行啟動診斷，並記錄啟動或圖磚載入失敗資訊。
- 在系統、淺色、深色和經驗證的第三方 ZIP 或 7z 佈景主題套件之間切換。
- 安裝 addon、沙箱處理程序外掛程式和明確信任的原生外掛程式套件。
- 將設定、圖層、視窗狀態、快取和記錄保存在目前使用者的本機應用程式資料目錄。
- 提供聚焦的單元測試，涵蓋設定、呈現、啟動診斷、快取、空間格式、佈景主題、外掛程式、OSM 傳輸、座標轉換和 URL 剖析。

## 環境需求

- Windows 10 或更新版本，x64，用於預先建置的 Windows 發行版
- 從原始碼建置時需要 .NET SDK 10.0 或更新版本

## 專案結構

```text
src/WPF-OpenStreetmap-Editor/          WPF 應用程式原始碼
tests/WPF-OpenStreetmap-Editor.Tests/  單元測試
docs/                                  貢獻者文件
sdk/native/                            原生外掛程式 C ABI 標頭檔
scripts/                               本機 CLI 輔助指令碼
.github/workflows/                     CI 工作流程定義
```

## 快速開始

使用預先建置的應用程式時，請從 [Releases 頁面](https://github.com/CAFEMDDSTEM/WPF-OpenStreetmap-Editor/releases)下載 ZIP，解壓縮到可寫入目錄，然後執行 `WPF-OpenStreetmap-Editor.exe`。

從原始碼建置時，複製儲存庫後執行：

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

從原始碼查看 WOSM 命令列說明：

```powershell
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- help
```

## 使用方式

1. 啟動應用程式。
2. 開啟 **工具 > 設定**，選擇影像來源、新增自訂圖磚 URL 範本、切換語言、設定匯入投影預設值或切換佈景主題。
3. 當影像提供者需要 `{access_token}` 時，在設定中輸入存取權杖。影像存取權杖僅用於目前工作階段，不會寫入磁碟。
4. 使用 **影像** 功能表將來源加入為柵格地圖圖層。
5. 使用 **檔案 > 開啟** 匯入向量地圖資料，或使用 `Ctrl+Shift+Down` 選擇並下載 OSM 邊界框。
6. 使用滑鼠滾輪、`+` / `-` 或 `Page Up` / `Page Down` 縮放，拖曳地圖平移，並使用編輯工具列選取、框選、隱藏、刪除、新增點或繪製線。
7. 使用 `Ctrl+C`、`Ctrl+V`、`Ctrl+Z` 和 `Ctrl+Shift+D` 複製、貼上、復原和建立選取圖徵複本。
8. 按 `R` 或 `M` 後用滑鼠旋轉或移動選取圖徵，再按 `Enter` 或按一下左鍵套用。在選取模式中，拖曳已選取圖徵也可以移動目前選取。
9. 輸入 `r20`、`mx2` 或 `mx20y20` 等精確命令後按 `Enter`。旋轉值單位為度；移動值單位為分米，`x` 表示向東，`y` 表示向北。
10. 按 `Q` 正交化選取的線或面圖徵。
11. 選取單一圖徵，並使用 AI 標籤助理向 BetterID AI 要求建議的 OSM 標籤。套用前請審閱並選擇建議。
12. 使用 **檔案 > 儲存** 或 **另存新檔** 儲存支援的向量格式。向設定的 OSM API 上傳前請審閱預覽；上傳對話方塊可以要求 BetterID AI 產生變更集註解草稿。

命令列工作流程可在從原始碼執行時透過 `--` 傳入 CLI 命令：

```powershell
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- import --input map.geojson
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- convert --input map.geojson --output map.gpx
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- download --bbox minLon,minLat,maxLon,maxLat --output data.osm
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- changeset --input map.geojson --output preview.osc
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- upload --input map.geojson --dry-run --output preview.osc
```

真實 OSM 上傳需要 `--yes`、`--comment`，以及來自 `--token`、`--token-env`、`OSM_ACCESS_TOKEN` 或目前 WOSM 帳戶的認證。

## 圖磚服務說明

圖磚提供者可能設定速率限制、署名要求或存取限制。使用服務 URL 前請確認提供者條款，尤其是使用正式環境認證或公共圖磚伺服器時。

## 執行階段檔案

應用程式會在 `%LOCALAPPDATA%\WPF-OpenStreetmap-Editor\` 下寫入執行階段資料：

- `Cache/tiles/`：下載的圖磚影像
- `layers.json`：已儲存的圖層 URL
- `settings.json`：影像和應用程式設定
- `osm.accounts.json`：OSM 帳戶中繼資料；密碼和 OAuth 存取權杖會分別儲存在 Windows 認證管理員中
- `Plugins/` 和 `plugins.state.json`：已安裝外掛程式和原生外掛程式信任狀態
- `Themes/`：已安裝的第三方佈景主題
- `window_state.json`：已儲存的視窗位置和大小
- `tile_requests.log` 和 `startup.log`：診斷記錄

## 文件

貢獻和維護文件位於 [docs/README.zh-TW.md](docs/README.zh-TW.md)：

- [開發指南](docs/development.md)
- [程式碼風格](docs/code-style.md)
- [測試指南](docs/testing.md)
- [地圖資料、CLI 工作流程和 OSM 傳輸](docs/map-data.md)
- [佈景主題套件](docs/themes.md)
- [外掛程式架構](docs/plugins.md)
- [Issue 與錯誤回報](docs/issues.md)
- [Pull Request](docs/pull-requests.md)

## 參與貢獻

歡迎提交 Issue 和 Pull Request。請使用 GitHub Issue 和 Pull Request 範本，在提交變更前執行測試套件，並遵循 [docs/code-style.md](docs/code-style.md) 中的專案程式碼風格。

## 授權條款

本專案基於 GNU General Public License v3.0 授權。詳見 [LICENSE.txt](LICENSE.txt)。
