# WOSM

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md) | [Deutsch](README.de.md)

WOSM, short for WPF OpenStreetMap Editor, is a C# / WPF OpenStreetMap editor. Its long-term goal is to become a practical C# alternative to JOSM.

The current application can load OpenStreetMap-compatible tile layers, import common vector map formats, preview and edit map features, and manage reusable imagery, themes, and plugins.

> Project status: early development. The current application focuses on imagery rendering, vector data handling, local editing, theming, plugin infrastructure, and basic OpenStreetMap transfer workflows.

## Test Release

The first public test build is available as a Windows x64 self-contained package:

[Download WOSM v0.1.0-beta.1](https://github.com/CAFEMDDSTEM/WPF-OpenStreetmap-Editor/releases/download/v0.1.0-beta.1/WOSM-v0.1.0-beta.1-win-x64.zip)

Extract the ZIP and run `WPF-OpenStreetmap-Editor.exe`. The package includes the required .NET runtime. This is an unsigned pre-release build, so Windows may display a SmartScreen warning. Please report reproducible problems through [GitHub Issues](https://github.com/CAFEMDDSTEM/WPF-OpenStreetmap-Editor/issues).

## Features

- Load XYZ, TMS, ArcGIS-style, WMTS-like, and Bing imagery sources.
- Support common placeholders such as `{z}`, `{x}`, `{y}`, `{-y}`, `{s}`, `{switch:a,b,c}`, `{zoom}`, `{TileMatrix}`, `{TileCol}`, `{TileRow}`, and `{access_token}`.
- Manage reusable imagery presets, access tokens, attribution, zoom limits, and no-tile markers.
- Render multiple imagery layers with visibility, primary-layer selection, opacity, mouse panning, and zoom controls.
- Import `.osm`, `.pbf`, Shapefile, GeoJSON, GML, KML/KMZ, and GPX map data.
- Save edited data as GeoJSON, OpenStreetMap XML, GPX, KML, or GML. PBF, Shapefile, and KMZ are currently import-only.
- Select, box-select, hide, delete, add point features, and draw line features on top of imagery.
- Download OSM data for a selected bounding box and upload reviewed create/modify/delete changes through the built-in OpenStreetMap transfer addon.
- Use bounded memory and disk caches, validate downloaded images, and fall back to cached parent tiles while loading.
- Run startup diagnostics and log startup or tile-loading failures for troubleshooting.
- Switch between system, light, dark, and validated third-party ZIP or 7z theme packages.
- Install addon, sandboxed process, and explicitly trusted native plugin packages.
- Keep settings, layers, window state, caches, and logs in the current user's local application data directory.
- Include focused unit tests for settings, rendering, startup diagnostics, caching, spatial formats, themes, plugins, OSM transfer, coordinate conversion, and URL parsing.

## Requirements

- Windows 10 or later, x64, for the prebuilt test release
- .NET SDK 10.0 or newer when building from source

## Project Layout

```text
src/WPF-OpenStreetmap-Editor/          WPF application source
tests/WPF-OpenStreetmap-Editor.Tests/  Unit tests
docs/                                  Contributor documentation
sdk/native/                            Native plugin C ABI header
scripts/                               Local CLI helpers
.github/workflows/                     CI workflow definitions
```

## Getting Started

For the prebuilt application, download the ZIP from the [Releases page](https://github.com/CAFEMDDSTEM/WPF-OpenStreetmap-Editor/releases), extract it to a writable directory, and run `WPF-OpenStreetmap-Editor.exe`.

To build from source, clone the repository and run:

```powershell
.\scripts\build.ps1
```

Run the test suite:

```powershell
.\scripts\test.ps1
```

You can also run the application directly with the .NET CLI:

```powershell
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj
```

## Usage

1. Start the application.
2. Open **Tools > Settings** to select an imagery source, add a custom tile URL template, or switch themes.
3. Enter an access token in Settings when an imagery provider requires `{access_token}`. Imagery access tokens are used for the current session and are not saved to disk.
4. Use the **Imagery** menu to add a source as a raster map layer.
5. Use **File > Open** to import vector map data, or use the built-in OpenStreetMap transfer toolbar to choose and download an OSM bounding box.
6. Use the mouse wheel, `+` / `-`, or `Page Up` / `Page Down` to zoom, drag the map to pan, and use the editor toolbar to select, box-select, hide, delete, add points, or draw lines.
7. Use **File > Save** or **File > Save As** to save supported vector formats. Review OSM uploads before sending them to the configured OSM API.

## Tile Service Notes

Tile providers may apply rate limits, attribution requirements, or access restrictions. Check the provider terms before using a service URL, especially when using production credentials or public tile servers.

## Runtime Files

The application writes runtime data under `%LOCALAPPDATA%\WPF-OpenStreetmap-Editor\`:

- `Cache/tiles/` for downloaded tile images
- `layers.json` for saved layer URLs
- `settings.json` for imagery and application settings
- `osm.accounts.json` for OSM account metadata; access tokens are stored separately in Windows Credential Manager
- `Plugins/` and `plugins.state.json` for installed plugins and native-plugin trust state
- `Themes/` for installed third-party themes
- `window_state.json` for the saved window position and size
- `tile_requests.log` and `startup.log` for diagnostics

## Documentation

Contributor and maintenance documentation lives in [docs/README.md](docs/README.md):

- [Development Guide](docs/development.md)
- [Code Style](docs/code-style.md)
- [Testing Guide](docs/testing.md)
- [Map data and OSM transfer](docs/map-data.md)
- [Theme packages](docs/themes.md)
- [Plugin architecture](docs/plugins.md)
- [Issues and Bug Reports](docs/issues.md)
- [Pull Requests](docs/pull-requests.md)

## Contributing

Issues and pull requests are welcome. Please use the GitHub issue and pull request templates, run the test suite before submitting changes, and follow the project code style documented in [docs/code-style.md](docs/code-style.md).

## License

This project is licensed under the GNU General Public License v3.0. See [LICENSE.txt](LICENSE.txt) for details.
