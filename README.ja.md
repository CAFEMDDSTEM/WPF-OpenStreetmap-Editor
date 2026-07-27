# WOSM

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md) | [Deutsch](README.de.md)

WOSM は WPF OpenStreetMap Editor の略称で、C# / WPF で書かれた OpenStreetMap エディターです。長期的には JOSM の実用的な代替になることを目指しています。

現在のアプリケーションは、OpenStreetMap 互換のタイルレイヤーを読み込み、地図画像をプレビューし、再利用可能なタイルサービス URL を管理できます。

> プロジェクトの状態: 初期開発中。現在のアプリケーションは、タイルレンダリング、レイヤー設定、キャッシュ、周辺の開発基盤に重点を置いています。

## 機能

- XYZ、TMS、ArcGIS 形式、WMTS 風のタイル URL テンプレートを読み込めます。
- `{z}`、`{x}`、`{y}`、`{-y}`、`{s}`、`{switch:a,b,c}`、`{zoom}`、`{TileMatrix}`、`{TileCol}`、`{TileRow}`、`{access_token}` などの一般的なプレースホルダーに対応します。
- WPF Canvas 上に地図タイルを描画し、マウスによるパンとズーム操作に対応します。
- ダウンロードしたタイルをアプリケーションの実行ディレクトリにキャッシュします。
- `layers.json` に再利用可能なレイヤー URL を保存します。
- トラブルシューティング用にタイルリクエストと読み込みエラーを記録します。
- 座標変換、URL 解析、キャッシュパス、実行時パス処理を対象にした単体テストを含みます。

## 要件

- Windows
- .NET SDK 10.0 以降

## プロジェクト構成

```text
src/WPF-OpenStreetmap-Editor/          WPF アプリケーションのソース
tests/WPF-OpenStreetmap-Editor.Tests/  単体テスト
docs/                                  コントリビューター向けドキュメント
scripts/                               ローカル CLI 補助スクリプト
.github/workflows/                     CI ワークフロー定義
```

## はじめに

リポジトリをクローンした後、リポジトリのルートでビルドします:

```powershell
.\scripts\build.ps1
```

テストスイートを実行します:

```powershell
.\scripts\test.ps1
```

.NET CLI から直接アプリケーションを実行することもできます:

```powershell
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj
```

## 使い方

1. アプリケーションを起動します。
2. 地図 URL 入力欄にタイル URL テンプレートを入力します。
3. プロバイダーが `{access_token}` を必要とする場合は、アクセストークンを入力します。
4. 地図を読み込んだら、マウスホイール、`+` / `-`、または `Page Up` / `Page Down` でズームします。
5. 地図キャンバスをドラッグしてパンします。
6. ツールメニューからレイヤーウィンドウを開き、保存済みのレイヤー URL を管理します。

## タイルサービスに関する注意

タイルプロバイダーはリクエスト頻度の制限、帰属表示の要件、アクセス制限を設けている場合があります。サービス URL を使用する前に、特に本番用の認証情報や公共のタイルサーバーを使用する場合は、プロバイダーの利用規約を確認してください。

## 実行時ファイル

アプリケーションは実行時データをアプリケーションのベースディレクトリに書き込みます:

- `Cache/tiles/`: ダウンロードしたタイル画像
- `layers.json`: 保存済みのレイヤー URL
- `tile_requests.log`: タイルリクエスト診断ログ

## ドキュメント

コントリビューターおよびメンテナンス向けドキュメントは [docs/README.md](docs/README.md) にあります:

- [開発ガイド](docs/development.md)
- [コードスタイル](docs/code-style.md)
- [テストガイド](docs/testing.md)
- [Issue とバグ報告](docs/issues.md)
- [Pull Request](docs/pull-requests.md)

## コントリビューション

Issue と Pull Request を歓迎します。GitHub の Issue と Pull Request テンプレートを使用し、変更を提出する前にテストスイートを実行し、[docs/code-style.md](docs/code-style.md) に記載されたプロジェクトのコードスタイルに従ってください。

## ライセンス

このプロジェクトは GNU General Public License v3.0 の下でライセンスされています。詳細は [LICENSE.txt](LICENSE.txt) を参照してください。
