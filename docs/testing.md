# Testing Guide

Run the full test suite from the repository root:

```powershell
.\scripts\test.ps1
```

The test project currently covers:

- Web Mercator coordinate and pixel conversion
- Tile URL template parsing, TMS Y-axis conversion, access-token handling, and cache path generation
- Tile memory and disk cache trimming, image validation, and render layout planning
- Application settings defaults, migration behavior, layer stack rules, and runtime path normalization
- Startup diagnostics and window startup state calculations
- Spatial import/export for GeoJSON, OSM XML/PBF, Shapefile, GML, KML/KMZ, and GPX, including import safety limits
- Vector map interaction helpers, render planning, and feature budget culling
- Theme package manifest, contrast, asset, archive, and install validation
- Plugin manifest parsing, package installation, native trust fingerprints, process sandbox isolation, and JSON-RPC exchange
- OSM account metadata/credential separation, API bounds validation, and change serialization
- Topology editing: draw snapping, vertex insertion/removal, shared-vertex lookup and junction propagation, geometry alignment tools, measurement, node sharing and orphan-node cleanup during OSM synchronization
- Tag editor model, tag autocomplete ranking, and multi-feature tag patch commands
- Topology validation rules (duplicate nodes, untagged objects, highway connectivity)
- Bézier pen path flattening, similar-feature (magic wand) selection, and brush hit-testing
