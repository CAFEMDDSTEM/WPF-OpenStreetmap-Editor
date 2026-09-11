# WOSM Installer

This folder contains the Inno Setup script that builds the per-user Windows installer
(`WOSM-Setup-<tag>-win-x64.exe`). The portable ZIP remains available for users who
prefer an archive, including command-line workflows.

## Behavior

- Per-user installation without administrator rights (`PrivilegesRequired=lowest`).
- Default install location: `%LOCALAPPDATA%\Programs\WOSM`.
- Start Menu entry; optional desktop shortcut (unchecked by default).
- Upgrades reuse the same AppId and application directory.
- Uninstalling does not delete user data under `%LOCALAPPDATA%\WPF-OpenStreetmap-Editor`
  (settings, caches, plugins, themes, logs).
- The setup and the application are currently unsigned; Windows SmartScreen may show a
  warning. Code signing is intentionally out of scope here.

## Requirements

- Inno Setup 6.3 or newer (`ISCC.exe`). GitHub `windows-latest` runners already include
  Inno Setup 6.7.x.
- The application payload published for `win-x64`.

## Building locally

```powershell
dotnet publish .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -o .\artifacts\publish\WOSM-win-x64

iscc .\installer\WOSM.iss
```

The default output is `artifacts\release\WOSM-Setup-dev-win-x64.exe`.

The release workflow passes these defines on the compiler command line; all are optional
and default to the local layout above:

| Define              | Release value                                          |
| ------------------- | ------------------------------------------------------ |
| `AppVersion`        | Tag without the `v` prefix, e.g. `0.2.2` or `0.2.2-beta.1` |
| `VersionInfoVersion`| Numeric version with any prerelease suffix stripped    |
| `SourceDir`         | Publish output directory                               |
| `OutputDir`         | Release artifact directory                             |
| `OutputBaseFilename`| `WOSM-Setup-<tag>-win-x64`                             |

## Smoke testing

```powershell
.\artifacts\release\WOSM-Setup-dev-win-x64.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
& "$env:LOCALAPPDATA\Programs\WOSM\unins000.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```

## Language files

English, German, Japanese and Russian come from the Inno Setup distribution itself.
`Languages\ChineseSimplified.isl` and `Languages\ChineseTraditional.isl` are the
community translations from the Inno Setup source repository
(`Files/Languages/` on <https://github.com/jrsoftware/issrc>), vendored unchanged.
