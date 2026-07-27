# Development Guide

## Project layout

```text
src/WPF-OpenStreetmap-Editor/          WPF application source
tests/WPF-OpenStreetmap-Editor.Tests/  Unit tests
docs/                                  Contributor documentation
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

The application stores runtime files under the application base directory:

- `Cache/tiles/`
- `layers.json`
- `tile_requests.log`
