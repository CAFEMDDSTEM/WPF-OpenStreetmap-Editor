# Theme packages

WOSM includes system, light, and dark themes. Every theme uses the same three-file contract, whether it is built in or installed from a third party:

```text
theme.json
icon.png
README.md
assets/background.png   # optional and declared by theme.json
```

The three required files must be at the package root with exactly these names. A wrapper directory inside the archive is not supported.

## Installing a theme

Open **Tools > Settings > Appearance**, select **Import theme**, and choose one of these formats:

- `.wosm-theme`: a ZIP archive using WOSM's theme extension
- `.zip`
- `.7z`

WOSM validates and normalizes the package into:

```text
%LOCALAPPDATA%\WPF-OpenStreetmap-Editor\Themes\<theme-id>\
```

An installed theme can be removed from the same settings page. WOSM never overwrites a theme with the same ID; remove the old version before installing an update.

The repository includes the source of an [example package](examples/clear-night/). To build a `.wosm-theme` package with PowerShell, run this from inside that directory:

```powershell
Compress-Archive -Path theme.json,icon.png,README.md -DestinationPath clear-night.zip
Rename-Item clear-night.zip clear-night.wosm-theme
```

Include the `assets` directory in `Compress-Archive -Path` when the manifest declares a background image.

## Required files

`icon.png` is the theme thumbnail shown in Settings. It must be a square PNG from 32 x 32 through 512 x 512 pixels and no larger than 1 MB.

`README.md` is the description shown for the selected theme. It must be non-empty UTF-8 text, no larger than 32 KB or 8,000 characters. Keep it concise; plain Markdown paragraphs display cleanly in the current settings view.

`theme.json` must be UTF-8 JSON no larger than 64 KB and use schema version 1:

```json
{
  "schemaVersion": 1,
  "id": "example.clear-night",
  "name": "Clear Night",
  "author": "Example Author",
  "version": "1.0.0",
  "baseTheme": "dark",
  "backgroundImage": "assets/background.png",
  "backgroundImageOpacity": 0.18,
  "colors": {
    "window": "#181A1D",
    "surface": "#22252A",
    "surfaceAlt": "#2D3137",
    "text": "#F4F6F8",
    "mutedText": "#B4BBC4",
    "border": "#59616C",
    "accent": "#4CC2FF",
    "accentText": "#0B1B24",
    "selection": "#155778",
    "selectionText": "#FFFFFF",
    "mapBackground": "#343A42"
  }
}
```

`id` must contain 1-64 lowercase ASCII letters, digits, periods, or hyphens. `system`, `light`, and `dark` are reserved. `baseTheme` must be `light` or `dark` and controls integration with the Windows title bar.

All colors are required and must use opaque `#RRGGBB`. WOSM requires at least a 4.5:1 contrast ratio for normal text, muted text, accent text, and selected text against their corresponding backgrounds. Unknown manifest properties are rejected.

`backgroundImage` is optional and must point to a package-relative PNG or JPEG file. Its opacity must be between 0 and 0.35. Backgrounds are limited to 8 MB, 4096 pixels on either side, and 8,388,608 total pixels. WOSM re-encodes installed images as PNG.

## Package safety

Packages are declarative. They cannot load XAML, assemblies, scripts, executables, remote resources, or undeclared files. Paths must stay inside the archive, are matched case-sensitively, and cannot be duplicated. Encrypted archives are rejected.

An archive can contain at most 64 files, be at most 20 MB compressed, and expand to at most 32 MB. Windows high-contrast colors always take precedence over an application or third-party theme.
