# Map data and OSM transfer

WOSM can render raster imagery and a vector document at the same time. Raster
layers come from imagery sources, while vector documents come from local files
or OpenStreetMap transfer workflows.

## Supported formats

| Format | Import | Save | Notes |
| --- | --- | --- | --- |
| OpenStreetMap XML (`.osm`) | Yes | Yes | Preserves node, way, and relation IDs, versions, topology, and memberships for later OSM change uploads. |
| OpenStreetMap PBF (`.pbf`) | Yes | No | Import-only; save as OSM XML or another supported export format. |
| Shapefile (`.shp`, `.dbf`, `.shx`) | Yes | No | Choose any companion file; WOSM loads the same-named `.shp`, optional `.dbf`, `.cpg`, and `.prj`. |
| GeoJSON (`.geojson`, `.json`) | Yes | Yes | Reads FeatureCollection, Feature, geometry, and geometry collection inputs. |
| GML (`.gml`) | Yes | Yes | Reads common Point, LineString/Curve, and Polygon geometry. |
| KML (`.kml`) | Yes | Yes | Reads placemark Point, LineString, and Polygon geometry. |
| KMZ (`.kmz`) | Yes | No | Reads the contained KML file; save as KML or another supported export format. |
| GPX (`.gpx`) | Yes | Yes | Reads waypoints, routes, and tracks. Saves points as waypoints and lines as tracks. |

Imports are bounded by default to 250,000 features and 2,000,000 coordinates.
Large OSM API downloads are also capped at 128 MB.

## Import projections

WOSM stores vector data internally as WGS 84 longitude/latitude so it can align
with XYZ and TMS imagery. Open **Tools > Settings > Data** to choose the default
source projection used when importing projected GeoJSON, GML, or Shapefiles
without a `.prj` file. Built-in options include WGS 84, Web Mercator, CGCS2000
longitude/latitude, CGCS2000 Mercator, JGD2011, JGD2000, ETRS89, and German
ETRS89 / UTM zones 32N and 33N. Use **Custom WKT** for another CRS.

Shapefiles with a companion `.prj` keep using the CRS declared by that file.
OSM, GPX, and KML are treated as longitude/latitude formats.
Legacy German DHDN / Gauss-Kruger data should keep its `.prj` file or use a
site-approved custom WKT because high-accuracy datum conversion can depend on
local transformation parameters.

## Editing workflow

Use **File > Open** to import a vector document. WOSM fits the map to the
document bounds, lists features in the side panel, and renders visible features
above imagery. The editor toolbar supports:

- pan mode
- single selection and shift-add selection
- box selection and shift-add box selection
- drawing line features
- adding a point at the current map center
- copying, pasting, duplicating, hiding, showing, and deleting selected features
- moving, rotating, and orthogonalizing selected line or polygon features

Common editing shortcuts:

| Shortcut | Action |
| --- | --- |
| `Ctrl+C` | Copy selected features. |
| `Ctrl+V` | Paste copied features with a small offset. |
| `Ctrl+Z` | Undo the last committed edit. |
| `Ctrl+Y` or `Ctrl+Shift+Z` | Redo the last undone edit. |
| `Ctrl+Shift+D` | Duplicate selected features. |
| `A` | Enter line drawing mode. Press `Enter` to finish the current line. |
| `R` | Rotate selected features with the mouse. Press `Enter` or left-click to apply; right-click or `Esc` cancels. |
| `M` | Move selected features with the mouse. Press `Enter` or left-click to apply; right-click or `Esc` cancels. |
| `Q` | Orthogonalize selected line or polygon features. |

In select mode, press and hold the left mouse button on an already-selected
feature to drag the whole selection. Releasing the button commits the move as a
single undoable edit.

Precision edit commands can be typed directly, then applied with `Enter`:

| Command | Result |
| --- | --- |
| `r20` or `r 20` | Rotate selected features 20 degrees around their shared geometry center. |
| `mx2` or `m x 2` | Move selected features 2 decimeters east. |
| `mx20y20` | Move selected features 20 decimeters east and 20 decimeters north. |
| `my-10` | Move selected features 10 decimeters south. |

Move command values are real-world decimeters. The `x` axis moves east/west and
the `y` axis moves north/south. Negative values move west or south.

Use **File > Save** for formats that can be updated in place. WOSM prompts for
**Save As** when the source format is import-only, such as PBF, Shapefile, or
KMZ.

## AI-assisted editing

The main editing window includes a BetterID AI tag assistant for a single
selected feature. The request includes the description entered by the mapper,
the feature's current tags, a coarse geometry type, and a representative
location when one is available. Returned suggestions are normalized before
display: unsafe source-only tags, unchanged values, invalid keys or values, and
non-HTTP sources are filtered out. The mapper must review the suggestions and
choose which ones to apply; WOSM does not silently edit tags.

The OSM upload dialog can also ask BetterID AI to draft a changeset comment.
WOSM sends a bounded summary of the pending create, modify, and delete actions,
including feature types, names, tag keys, and representative tag changes. The
generated comment is only a draft and must be reviewed before a guarded upload.

Both AI helpers use the default BetterID AI endpoint at
`https://map.osm.asia/api/osm-ai/` and require network access. They are GUI-only
helpers; CLI `changeset` and `upload` commands continue to build deterministic
local previews unless a user-provided comment is supplied.

## OpenStreetMap transfer

OpenStreetMap transfer is available from the keyboard shortcuts below. The
optional first-party transfer addon can also add toolbar and Tools menu commands
for download, upload, and account management.

To download from OSM:

1. Use `Ctrl+Shift+Down`, or use the OSM download toolbar/menu command when the optional transfer addon is installed.
2. In the download window, pan or zoom the map to the area you need.
3. Drag a bounding box and confirm the download.

The selected bounding box must be valid and no larger than 0.25 square degrees.
Downloaded data replaces the current unsaved vector document after confirmation.

To upload to OSM:

1. Configure an OSM account from the upload prompt, or use **Tools > OSM accounts** when the optional transfer addon is installed.
2. Review the current document changes.
3. Use `Ctrl+Shift+Up`, or use the OSM upload toolbar/menu command when the optional transfer addon is installed.
4. Enter a changeset comment and confirm the upload preview.

Uploads are built from the current document as OSM API 0.6 changes. WOSM
preserves OSM nodes, ways, and relations in a shared dataset; feature geometry
is synchronized back to shared node identities before upload. Multipart
non-relation features must be split before upload. The configured API base URL
must use HTTPS, except loopback test servers may use HTTP.

Account metadata is written to `osm.accounts.json` under local application
data. Account passwords and OAuth access tokens are stored separately in
Windows Credential Manager and are not written to the JSON metadata file.

## CLI workflows

The main WOSM executable exposes the same data pipeline for automation and
batch work. Run without data commands to open the GUI, or use `gui`/`launch`
with window startup options such as `--fullscreen`:

```powershell
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- help
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- gui --fullscreen
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- download --bbox 103.8,1.3,103.9,1.4 --output data.osm
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- import --input data.osm
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- convert --input data.geojson --output data.gpx
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- changeset --input data.geojson --output preview.osc --feature-id survey-path
```

`download` writes raw OSM XML for a bounding box. It uses `--api-base-url` when
provided, otherwise it uses the active WOSM account API base URL or the public
OpenStreetMap API. The CLI falls back to Overpass when the standard API rejects
the request or the selection is larger than 0.25 square degrees, up to the
25-square-degree safety limit.

`import` validates a file and prints feature, coordinate, skipped-feature, and
bounds information. `convert` imports any supported read format and saves to a
supported write format based on the output extension. `changeset` builds an OSM
API 0.6 change preview without contacting OSM; use `--changeset-id` to set the
preview changeset id when needed.

`upload` can submit selected data to OSM, but it is deliberately guarded because
it writes to the live API. Real uploads require `--yes`, `--comment`, and a
credential from `--token`, `--token-env`, `OSM_ACCESS_TOKEN`, or the active
WOSM account. Use `--dry-run` before a real upload; add `--output preview.osc`
to write the generated OSM change XML:

```powershell
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- upload --input data.geojson --dry-run --output preview.osc
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- upload --input data.geojson --comment "Add surveyed paths" --token-env OSM_ACCESS_TOKEN --yes
```

Commands that load an input document (`import`, `convert`, `changeset`, and
`upload`) accept `--feature-id` and `--tag key=value` to process a specific
subset. Repeat `--feature-id`, comma-separate IDs, or repeat `--tag` for exact
tag filters. Feature IDs are combined as alternatives; tag filters must all
match. For `changeset` and `upload`, use `--treat-input-as-new` to ignore
imported OSM IDs and build creates instead of modifies.

The CLI import safety defaults are `--max-features 1000000` and
`--max-coordinates 8000000`. Output commands refuse to overwrite existing files
unless `--force` is provided. `gui` and `launch` start the WPF interface from
the same executable, and `--fullscreen`, `--full-screen`, or `--maximized`
starts the window in fullscreen mode.
