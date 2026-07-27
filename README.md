# WPF OpenStreetMap Editor

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md)

A Windows WPF desktop application for loading OpenStreetMap-compatible tile layers, previewing map imagery, and managing reusable tile service URLs.

> Project status: early development. The current application focuses on tile rendering, layer configuration, caching, and supporting infrastructure.

## Features

- Load XYZ, TMS, ArcGIS-style, and WMTS-like tile URL templates.
- Support common placeholders such as `{z}`, `{x}`, `{y}`, `{-y}`, `{s}`, `{switch:a,b,c}`, `{zoom}`, `{TileMatrix}`, `{TileCol}`, `{TileRow}`, and `{access_token}`.
- Render map tiles in a WPF canvas with mouse panning and zoom controls.
- Cache downloaded tiles under the application runtime directory.
- Store reusable layer URLs in `layers.json`.
- Log tile requests and tile loading errors for troubleshooting.
- Include unit tests for coordinate conversion, URL parsing, cache paths, and runtime path handling.

## Requirements

- Windows
- .NET SDK 10.0 or newer

## Project Layout

```text
src/WPF-OpenStreetmap-Editor/          WPF application source
tests/WPF-OpenStreetmap-Editor.Tests/  Unit tests
docs/                                  Contributor documentation
scripts/                               Local CLI helpers
.github/workflows/                     CI workflow definitions
```

## Getting Started

Clone the repository and build from the repository root:

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
2. Enter a tile URL template in the map URL field.
3. Enter an access token if the provider requires `{access_token}`.
4. Load the map, then use the mouse wheel, `+` / `-`, or `Page Up` / `Page Down` to zoom.
5. Drag the map canvas to pan.
6. Open the layer window from the tools menu to manage saved layer URLs.

## Tile Service Notes

Tile providers may apply rate limits, attribution requirements, or access restrictions. Check the provider terms before using a service URL, especially when using production credentials or public tile servers.

## Runtime Files

The application writes runtime data under the application base directory:

- `Cache/tiles/` for downloaded tile images
- `layers.json` for saved layer URLs
- `tile_requests.log` for tile request diagnostics

## Documentation

Contributor and maintenance documentation lives in [docs/README.md](docs/README.md):

- [Development Guide](docs/development.md)
- [Code Style](docs/code-style.md)
- [Testing Guide](docs/testing.md)
- [Issues and Bug Reports](docs/issues.md)
- [Pull Requests](docs/pull-requests.md)

## Contributing

Issues and pull requests are welcome. Please use the GitHub issue and pull request templates, run the test suite before submitting changes, and follow the project code style documented in [docs/code-style.md](docs/code-style.md).

## License

This project is licensed under the GNU General Public License v3.0. See [LICENSE.txt](LICENSE.txt) for details.
