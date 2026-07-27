# Development Guide

## Project layout

```text
src/WPF-OpenStreetmap-Editor/          WPF application source
tests/WPF-OpenStreetmap-Editor.Tests/  Unit tests
docs/                                  Contributor documentation
sdk/native/                            Native plugin C ABI header
scripts/                               CLI helpers for local automation
.github/workflows/                     CI pipeline definitions
```

## Requirements

- Windows
- .NET SDK 10.0 or newer

## Common commands

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
```

The application stores runtime files under `%LOCALAPPDATA%\WPF-OpenStreetmap-Editor\`:

- `Cache/tiles/`
- `layers.json`
- `settings.json`
- `window_state.json`
- `tile_requests.log`
- `startup.log`
- `Themes/`
- `Plugins/`
- `plugins.state.json`
- `osm.accounts.json`

Legacy `layers.json`, `settings.json`, and `window_state.json` files in the application base directory are still read until a current file exists under local application data.
