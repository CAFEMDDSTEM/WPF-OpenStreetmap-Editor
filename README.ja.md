# WOSM

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md) | [Deutsch](README.de.md)

WOSM は WPF OpenStreetMap Editor の略称で、C# / WPF で作成された OpenStreetMap エディターです。長期的には、JOSM の実用的な C# 代替となることを目指しています。

現在のアプリケーションでは、OpenStreetMap 互換のタイル レイヤーの読み込み、一般的なベクター地図形式のインポート、地図フィーチャのプレビューと編集、再利用可能な画像、テーマ、プラグインの管理を行うことができます。

> プロジェクトの状態: 正式版。WOSM v0.2.0 では、5 言語の UI ローカライズ、投影法に対応したインポート、BetterID AI による OSM 変更セット コメント支援、OSM 編集ワークフローの改善が追加されています。OpenStreetMap にアップロードする前に、必ず内容を確認してください。

## リリース

最新の正式版は、Windows x64 の自己完結型パッケージとして提供されています。

[WOSM v0.2.0 をダウンロード](https://github.com/CAFEMDDSTEM/WPF-OpenStreetmap-Editor/releases/download/v0.2.0/WOSM-v0.2.0-win-x64.zip)

OpenStreetMap へのサインインまたは変更のアップロードを行う前に、[`SHA256SUMS-v0.2.0.txt`](https://github.com/CAFEMDDSTEM/WPF-OpenStreetmap-Editor/releases/download/v0.2.0/SHA256SUMS-v0.2.0.txt) を使用して ZIP を検証してください。

```powershell
Get-FileHash .\WOSM-v0.2.0-win-x64.zip -Algorithm SHA256
```

ZIP を展開し、`WPF-OpenStreetmap-Editor.exe` を実行します。同じ実行可能ファイルでは、`help`、`import`、`convert`、`download`、`changeset`、`upload` などのコマンドライン データ ワークフロー コマンドも使用できます。このビルドは署名されていないため、Windows で SmartScreen 警告が表示される場合があります。再現可能な問題は [GitHub Issues](https://github.com/CAFEMDDSTEM/WPF-OpenStreetmap-Editor/issues) で報告してください。

## 機能

- XYZ、TMS、ArcGIS 形式、WMTS 風、Bing の画像ソースを読み込みます。
- `{z}`、`{x}`、`{y}`、`{-y}`、`{s}`、`{switch:a,b,c}`、`{zoom}`、`{TileMatrix}`、`{TileCol}`、`{TileRow}`、`{access_token}` などの一般的なプレースホルダーをサポートします。
- 再利用可能な画像プリセット、アクセス トークン、帰属表示、ズーム制限、タイルなしマーカーを管理します。
- 表示/非表示、プライマリ レイヤー選択、不透明度、マウス パン、ズーム コントロールを使用して、複数の画像レイヤーをレンダリングします。
- システム言語、英語、簡体字中国語、繁体字中国語、日本語、ドイツ語の間で UI 言語を切り替えます。
- `.osm`、`.pbf`、Shapefile、GeoJSON、GML、KML/KMZ、GPX の地図データをインポートします。
- `.prj` ファイルがない投影済み GeoJSON、GML、Shapefile に対して既定のソース投影法を選択できます。Shapefile に `.prj` がある場合は、そのファイルで宣言された CRS が使用されます。
- 編集したデータを GeoJSON、OpenStreetMap XML、GPX、KML、GML として保存します。PBF、Shapefile、KMZ は現在インポートのみです。
- 画像上でフィーチャを選択、ボックス選択、非表示、削除、コピー、貼り付け、複製、点の追加、線の描画、回転、移動、直交化できます。
- 選択した 1 つのフィーチャに対して BetterID AI の OSM タグ候補を要求し、アップロード前に確認する OSM 変更セット コメントの下書きを生成します。
- 選択した境界ボックスの OSM データをダウンロードし、確認済みの作成、変更、削除をアップロードします。任意のファーストパーティ OpenStreetMap 転送 addon により、ツールバーとメニューのエントリ ポイントを追加できます。
- コマンドラインから、インポートした地図ファイルの要約、サポートされるベクター形式の変換、境界ボックス OSM データのダウンロード、`.osc` 変更セットのプレビュー、保護付き OSM アップロード、GUI の起動を実行できます。
- 制限付きのメモリ キャッシュとディスク キャッシュを使用し、ダウンロードした画像を検証し、読み込み中はキャッシュ済みの親タイルにフォールバックします。
- 起動診断を実行し、起動またはタイル読み込みの失敗をログに記録します。
- システム、ライト、ダーク、検証済みのサードパーティ ZIP または 7z テーマ パッケージを切り替えます。
- addon、サンドボックス プロセス、明示的に信頼されたネイティブ プラグイン パッケージをインストールします。
- 設定、レイヤー、ウィンドウ状態、キャッシュ、ログを現在のユーザーのローカル アプリケーション データ ディレクトリに保持します。
- 設定、レンダリング、起動診断、キャッシュ、空間形式、テーマ、プラグイン、OSM 転送、座標変換、URL 解析を対象とした単体テストを含みます。

## 要件

- 事前ビルド済み Windows リリースの場合は、Windows 10 以降、x64
- ソースからビルドする場合は、.NET SDK 10.0 以降

## プロジェクト構成

```text
src/WPF-OpenStreetmap-Editor/          WPF アプリケーションのソース
tests/WPF-OpenStreetmap-Editor.Tests/  単体テスト
docs/                                  コントリビューター向けドキュメント
sdk/native/                            ネイティブ プラグイン C ABI ヘッダー
scripts/                               ローカル CLI ヘルパー
.github/workflows/                     CI ワークフロー定義
```

## はじめに

事前ビルド済みアプリケーションを使用するには、[Releases ページ](https://github.com/CAFEMDDSTEM/WPF-OpenStreetmap-Editor/releases)から ZIP をダウンロードし、書き込み可能なディレクトリに展開して、`WPF-OpenStreetmap-Editor.exe` を実行します。

ソースからビルドするには、リポジトリを複製して次を実行します。

```powershell
.\scripts\build.ps1
```

テスト スイートを実行します。

```powershell
.\scripts\test.ps1
```

.NET CLI からアプリケーションを直接実行することもできます。

```powershell
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj
```

ソースから WOSM コマンドライン ヘルプを実行します。

```powershell
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- help
```

## 使用方法

1. アプリケーションを起動します。
2. **Tools > Settings** を開き、画像ソースの選択、カスタム タイル URL テンプレートの追加、言語の切り替え、インポート投影法の既定値の構成、テーマの切り替えを行います。
3. 画像プロバイダーが `{access_token}` を必要とする場合は、Settings にアクセス トークンを入力します。画像アクセス トークンは現在のセッションでのみ使用され、ディスクには保存されません。
4. **Imagery** メニューを使用して、ソースをラスター地図レイヤーとして追加します。
5. **File > Open** を使用してベクター地図データをインポートするか、`Ctrl+Shift+Down` を使用して OSM 境界ボックスを選択してダウンロードします。
6. マウス ホイール、`+` / `-`、または `Page Up` / `Page Down` でズームし、地図をドラッグしてパンし、エディター ツールバーで選択、ボックス選択、非表示、削除、点の追加、線の描画を行います。
7. `Ctrl+C`、`Ctrl+V`、`Ctrl+Z`、`Ctrl+Shift+D` を使用して、選択したフィーチャのコピー、貼り付け、元に戻す、複製を行います。
8. `R` または `M` を押して、選択したフィーチャをマウスで回転または移動し、`Enter` または左クリックで適用します。選択モードでは、選択済みフィーチャをドラッグして選択全体を移動することもできます。
9. `r20`、`mx2`、`mx20y20` などの精密コマンドを入力して `Enter` を押します。回転値の単位は度です。移動値の単位はデシメートルで、`x` は東、`y` は北を表します。
10. `Q` を押して、選択した線またはポリゴン フィーチャを直交化します。
11. 1 つのフィーチャを選択し、AI タグ アシスタントを使用して BetterID AI から OSM タグ候補を取得します。適用する前に候補を確認して選択します。
12. **File > Save** または **File > Save As** を使用して、サポートされるベクター形式で保存します。構成済みの OSM API に送信する前にアップロードを確認してください。アップロード ダイアログでは、BetterID AI に変更セット コメントの下書きを要求できます。

コマンドライン ワークフローでは、ソースから実行するときに `--` の後に CLI コマンドを渡します。

```powershell
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- import --input map.geojson
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- convert --input map.geojson --output map.gpx
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- download --bbox minLon,minLat,maxLon,maxLat --output data.osm
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- changeset --input map.geojson --output preview.osc
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- upload --input map.geojson --dry-run --output preview.osc
```

実際の OSM アップロードには、`--yes`、`--comment`、および `--token`、`--token-env`、`OSM_ACCESS_TOKEN`、または有効な WOSM アカウントから取得した資格情報が必要です。

## タイル サービスに関する注意

タイル プロバイダーは、レート制限、帰属表示の要件、アクセス制限を適用する場合があります。サービス URL を使用する前に、特に本番資格情報や公開タイル サーバーを使用する場合は、プロバイダーの利用規約を確認してください。

## 実行時ファイル

アプリケーションは、実行時データを `%LOCALAPPDATA%\WPF-OpenStreetmap-Editor\` に書き込みます。

- `Cache/tiles/`: ダウンロードしたタイル画像
- `layers.json`: 保存済みレイヤー URL
- `settings.json`: 画像とアプリケーションの設定
- `osm.accounts.json`: OSM アカウント メタデータ。パスワードと OAuth アクセス トークンは Windows 資格情報マネージャーに個別に保存されます
- `Plugins/` と `plugins.state.json`: インストール済みプラグインとネイティブ プラグインの信頼状態
- `Themes/`: インストール済みサードパーティ テーマ
- `window_state.json`: 保存済みウィンドウ位置とサイズ
- `tile_requests.log` と `startup.log`: 診断ログ

## ドキュメント

コントリビューターおよび保守向けドキュメントは [docs/README.ja.md](docs/README.ja.md) にあります。

- [開発ガイド](docs/development.md)
- [コード スタイル](docs/code-style.md)
- [テスト ガイド](docs/testing.md)
- [地図データ、CLI ワークフロー、OSM 転送](docs/map-data.md)
- [テーマ パッケージ](docs/themes.md)
- [プラグイン アーキテクチャ](docs/plugins.md)
- [Issue とバグ報告](docs/issues.md)
- [Pull Request](docs/pull-requests.md)

## コントリビューション

Issue と Pull Request を歓迎します。GitHub の Issue テンプレートと Pull Request テンプレートを使用し、変更を提出する前にテスト スイートを実行し、[docs/code-style.md](docs/code-style.md) に記載されたプロジェクトのコード スタイルに従ってください。

## ライセンス

このプロジェクトは GNU General Public License v3.0 の下でライセンスされています。詳細については [LICENSE.txt](LICENSE.txt) を参照してください。
