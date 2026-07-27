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
