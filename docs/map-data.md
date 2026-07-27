# Map data and OSM transfer

WOSM can render raster imagery and a vector document at the same time. Raster
layers come from imagery sources, while vector documents come from local files
or the built-in OpenStreetMap transfer addon.

## Supported formats

| Format | Import | Save | Notes |
| --- | --- | --- | --- |
| OpenStreetMap XML (`.osm`) | Yes | Yes | Preserves node/way IDs and versions for later OSM change uploads. Relations are skipped for now. |
| OpenStreetMap PBF (`.pbf`) | Yes | No | Import-only; save as OSM XML or another supported export format. |
| Shapefile (`.shp`, `.dbf`, `.shx`) | Yes | No | Choose any companion file; WOSM loads the same-named `.shp`, optional `.dbf`, `.cpg`, and `.prj`. |
| GeoJSON (`.geojson`, `.json`) | Yes | Yes | Reads FeatureCollection, Feature, geometry, and geometry collection inputs. |
| GML (`.gml`) | Yes | Yes | Reads common Point, LineString/Curve, and Polygon geometry. |
| KML (`.kml`) | Yes | Yes | Reads placemark Point, LineString, and Polygon geometry. |
| KMZ (`.kmz`) | Yes | No | Reads the contained KML file; save as KML or another supported export format. |
| GPX (`.gpx`) | Yes | Yes | Reads waypoints, routes, and tracks. Saves points as waypoints and lines as tracks. |

Imports are bounded by default to 250,000 features and 2,000,000 coordinates.
Large OSM API downloads are also capped at 128 MB.

## Editing workflow

Use **File > Open** to import a vector document. WOSM fits the map to the
document bounds, lists features in the side panel, and renders visible features
above imagery. The editor toolbar supports:

- pan mode
- single selection and shift-add selection
- box selection and shift-add box selection
- drawing line features
- adding a point at the current map center
- hiding, showing, and deleting selected features

Use **File > Save** for formats that can be updated in place. WOSM prompts for
**Save As** when the source format is import-only, such as PBF, Shapefile, or
KMZ.

## OpenStreetMap transfer

The built-in OpenStreetMap transfer addon adds toolbar and Tools menu commands
for download, upload, and account management.

To download from OSM:

1. Use the OSM download toolbar button, **Tools > Download OSM data**, or `Ctrl+Shift+Down`.
2. In the download window, pan or zoom the map to the area you need.
3. Drag a bounding box and confirm the download.

The selected bounding box must be valid and no larger than 0.25 square degrees.
Downloaded data replaces the current unsaved vector document after confirmation.

To upload to OSM:

1. Configure an OSM account in **Tools > OSM accounts**.
2. Review the current document changes.
3. Use the OSM upload toolbar button, **Tools > Upload to OSM**, or `Ctrl+Shift+Up`.
4. Enter a changeset comment and confirm the upload preview.

Uploads are built from the current document as OSM API 0.6 changes. WOSM
currently handles nodes and ways; multipart features must be split before
upload. The configured API base URL must use HTTPS, except loopback test
servers may use HTTP.

Account metadata is written to `osm.accounts.json` under local application
data. Access tokens are stored separately in Windows Credential Manager and
are not written to the JSON metadata file.

## CLI workflows

The console project at `src/WPF-OpenStreetmap-Editor.Cli` exposes the same
data pipeline for automation and batch work:

```powershell
dotnet run --project .\src\WPF-OpenStreetmap-Editor.Cli\WPF-OpenStreetmap-Editor.Cli.csproj -- help
dotnet run --project .\src\WPF-OpenStreetmap-Editor.Cli\WPF-OpenStreetmap-Editor.Cli.csproj -- download --bbox 103.8,1.3,103.9,1.4 --output data.osm
dotnet run --project .\src\WPF-OpenStreetmap-Editor.Cli\WPF-OpenStreetmap-Editor.Cli.csproj -- import --input data.osm
dotnet run --project .\src\WPF-OpenStreetmap-Editor.Cli\WPF-OpenStreetmap-Editor.Cli.csproj -- convert --input data.geojson --output data.gpx
dotnet run --project .\src\WPF-OpenStreetmap-Editor.Cli\WPF-OpenStreetmap-Editor.Cli.csproj -- changeset --input data.geojson --output preview.osc --feature-id survey-path
```

`download` writes raw OSM XML for a bounding box. `import` validates a file and
prints feature, coordinate, skipped-feature, and bounds information. `convert`
imports any supported read format and saves to a supported write format based
on the output extension. `changeset` builds an OSM API 0.6 change preview
without contacting OSM.

`upload` can submit selected data to OSM, but it is deliberately guarded because
it writes to the live API. Real uploads require `--yes`, `--comment`, and a
token from `--token`, `--token-env`, `OSM_ACCESS_TOKEN`, or the active WOSM
account. Use `--dry-run --output preview.osc` before a real upload:

```powershell
dotnet run --project .\src\WPF-OpenStreetmap-Editor.Cli\WPF-OpenStreetmap-Editor.Cli.csproj -- upload --input data.geojson --dry-run --output preview.osc
dotnet run --project .\src\WPF-OpenStreetmap-Editor.Cli\WPF-OpenStreetmap-Editor.Cli.csproj -- upload --input data.geojson --comment "Add surveyed paths" --token-env OSM_ACCESS_TOKEN --yes
```

Most data commands accept `--feature-id` and `--tag key=value` to process a
specific subset. Output commands refuse to overwrite existing files unless
`--force` is provided. `launch --app WPF-OpenStreetmap-Editor.exe --fullscreen`
starts the GUI in the existing fullscreen startup mode when the published GUI
executable is available.
