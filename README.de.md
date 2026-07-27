# WOSM

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md) | [Deutsch](README.de.md)

WOSM steht für WPF OpenStreetMap Editor und ist ein in C# / WPF geschriebener OpenStreetMap-Editor. Langfristig soll es eine praktische C#-Alternative zu JOSM werden.

Die aktuelle Anwendung kann OpenStreetMap-kompatible Kachel-Layer laden, gängige Vektorkartenformate importieren, Kartenobjekte anzeigen und bearbeiten sowie wiederverwendbare Bildquellen, Designs und Plugins verwalten.

> Projektstatus: frühe Alpha. WOSM v0.2.0-alpha.1 ergänzt eine Benutzeroberfläche in fünf Sprachen, projektionsbewusste Importe, BetterID AI-Unterstützung für OSM-Änderungssatzkommentare und Verbesserungen am OSM-Bearbeitungsworkflow. Rechne mit rauen Kanten und prüfe alle OpenStreetMap-Uploads vor dem Senden.

## Release

Die aktuelle Alpha-Version ist als selbstständiges Windows-x64-Paket verfügbar:

[WOSM v0.2.0-alpha.1 herunterladen](https://github.com/CAFEMDDSTEM/WPF-OpenStreetmap-Editor/releases/download/v0.2.0-alpha.1/WOSM-v0.2.0-alpha.1-win-x64.zip)

Prüfe die ZIP-Datei gegen [`SHA256SUMS-v0.2.0-alpha.1.txt`](https://github.com/CAFEMDDSTEM/WPF-OpenStreetmap-Editor/releases/download/v0.2.0-alpha.1/SHA256SUMS-v0.2.0-alpha.1.txt), bevor du dich bei OpenStreetMap anmeldest oder Änderungen hochlädst:

```powershell
Get-FileHash .\WOSM-v0.2.0-alpha.1-win-x64.zip -Algorithm SHA256
```

Entpacke die ZIP-Datei und starte `WPF-OpenStreetmap-Editor.exe`. Dieselbe ausführbare Datei unterstützt auch Befehle für Datenworkflows wie `help`, `import`, `convert`, `download`, `changeset` und `upload`. Dieser Build ist nicht signiert, daher kann Windows eine SmartScreen-Warnung anzeigen. Reproduzierbare Probleme bitte über [GitHub Issues](https://github.com/CAFEMDDSTEM/WPF-OpenStreetmap-Editor/issues) melden.

## Funktionen

- Lädt XYZ-, TMS-, ArcGIS-ähnliche, WMTS-ähnliche und Bing-Bildquellen.
- Unterstützt gängige Platzhalter wie `{z}`, `{x}`, `{y}`, `{-y}`, `{s}`, `{switch:a,b,c}`, `{zoom}`, `{TileMatrix}`, `{TileCol}`, `{TileRow}` und `{access_token}`.
- Verwaltet wiederverwendbare Bildquellen-Voreinstellungen, Zugriffstoken, Quellenangaben, Zoomgrenzen und Markierungen für fehlende Kacheln.
- Rendert mehrere Bild-Layer mit Sichtbarkeit, Auswahl des primären Layers, Deckkraft, Mausverschiebung und Zoomsteuerung.
- Schaltet die Benutzeroberfläche zwischen Systemsprache, Englisch, vereinfachtem Chinesisch, traditionellem Chinesisch, Japanisch und Deutsch um.
- Importiert `.osm`, `.pbf`, Shapefile, GeoJSON, GML, KML/KMZ und GPX-Kartendaten.
- Wählt eine Standard-Quellprojektion für projizierte GeoJSON-, GML- und Shapefile-Daten ohne `.prj`; vorhandene Shapefile-`.prj`-Dateien werden beachtet.
- Speichert bearbeitete Daten als GeoJSON, OpenStreetMap XML, GPX, KML oder GML. PBF, Shapefile und KMZ sind derzeit nur importierbar.
- Wählt, rahmenwählt, blendet aus, löscht, kopiert, fügt ein, dupliziert, setzt Punkte, zeichnet Linien, dreht, verschiebt und orthogonalisiert Kartenobjekte über der Bildgrundlage.
- Fordert BetterID AI-Tagvorschläge für ein ausgewähltes Objekt an und erzeugt vor dem Upload prüfbare Entwürfe für OSM-Änderungssatzkommentare.
- Lädt OSM-Daten für einen ausgewählten Begrenzungsrahmen herunter und lädt geprüfte Erstellungs-, Änderungs- und Löschvorgänge hoch. Ein optionales First-Party-OpenStreetMap-Transfer-addon kann Symbolleisten- und Menüeinträge hinzufügen.
- Nutzt `WPF-OpenStreetmap-Editor.exe` über die Befehlszeile, um importierte Kartendateien zusammenzufassen, unterstützte Vektorformate zu konvertieren, OSM-Daten für Begrenzungsrahmen herunterzuladen, `.osc`-Änderungssätze anzuzeigen, geschützte OSM-Uploads auszuführen und die GUI zu starten.
- Verwendet begrenzte Speicher- und Datenträgercaches, validiert heruntergeladene Bilder und fällt beim Laden auf zwischengespeicherte übergeordnete Kacheln zurück.
- Führt Startdiagnosen aus und protokolliert Start- oder Kachelladefehler zur Problembehandlung.
- Wechselt zwischen System-, Hell-, Dunkel- und validierten Drittanbieter-Designpaketen im ZIP- oder 7z-Format.
- Installiert addon-, Sandboxprozess- und ausdrücklich vertrauenswürdige native Pluginpakete.
- Speichert Einstellungen, Layer, Fensterzustand, Caches und Protokolle im lokalen Anwendungsdatenverzeichnis des aktuellen Benutzers.
- Enthält fokussierte Unit-Tests für Einstellungen, Rendering, Startdiagnosen, Caching, räumliche Formate, Designs, Plugins, OSM-Transfer, Koordinatenumrechnung und URL-Analyse.

## Anforderungen

- Windows 10 oder neuer, x64, für die vorgefertigte Windows-Version
- .NET SDK 10.0 oder neuer beim Bauen aus dem Quellcode

## Projektstruktur

```text
src/WPF-OpenStreetmap-Editor/          WPF-Anwendungsquellcode
tests/WPF-OpenStreetmap-Editor.Tests/  Unit-Tests
docs/                                  Dokumentation für Mitwirkende
sdk/native/                            C-ABI-Header für native Plugins
scripts/                               Lokale CLI-Hilfsskripte
.github/workflows/                     CI-Workflowdefinitionen
```

## Einstieg

Für die vorgefertigte Anwendung lade die ZIP-Datei von der [Releases-Seite](https://github.com/CAFEMDDSTEM/WPF-OpenStreetmap-Editor/releases) herunter, entpacke sie in ein beschreibbares Verzeichnis und starte `WPF-OpenStreetmap-Editor.exe`.

Zum Bauen aus dem Quellcode klone das Repository und führe aus:

```powershell
.\scripts\build.ps1
```

Testsuite ausführen:

```powershell
.\scripts\test.ps1
```

Du kannst die Anwendung auch direkt mit der .NET CLI starten:

```powershell
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj
```

WOSM-Befehlszeilenhilfe aus dem Quellcode ausführen:

```powershell
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- help
```

## Verwendung

1. Anwendung starten.
2. **Tools > Settings** öffnen, um eine Bildquelle auszuwählen, eine benutzerdefinierte Kachel-URL-Vorlage hinzuzufügen, die Sprache zu wechseln, Standardwerte für Importprojektionen festzulegen oder Designs zu wechseln.
3. Ein Zugriffstoken in Settings eintragen, wenn eine Bildquelle `{access_token}` erfordert. Bildquellen-Zugriffstoken werden nur für die aktuelle Sitzung verwendet und nicht auf Datenträger gespeichert.
4. Über das Menü **Imagery** eine Quelle als Rasterkarten-Layer hinzufügen.
5. Über **File > Open** Vektorkartendaten importieren oder mit `Ctrl+Shift+Down` einen OSM-Begrenzungsrahmen auswählen und herunterladen.
6. Mit Mausrad, `+` / `-` oder `Page Up` / `Page Down` zoomen, die Karte durch Ziehen verschieben und über die Editor-Symbolleiste auswählen, rahmenwählen, ausblenden, löschen, Punkte hinzufügen oder Linien zeichnen.
7. Mit `Ctrl+C`, `Ctrl+V`, `Ctrl+Z` und `Ctrl+Shift+D` ausgewählte Objekte kopieren, einfügen, rückgängig machen und duplizieren.
8. `R` oder `M` drücken, um ausgewählte Objekte mit der Maus zu drehen oder zu verschieben, dann mit `Enter` oder Linksklick anwenden. Im Auswahlmodus kann ein bereits ausgewähltes Objekt gezogen werden, um die Auswahl zu verschieben.
9. Präzisionsbefehle wie `r20`, `mx2` oder `mx20y20` eingeben und `Enter` drücken. Rotationswerte sind Grad; Bewegungswerte sind Dezimeter, wobei `x` nach Osten und `y` nach Norden verschiebt.
10. `Q` drücken, um ausgewählte Linien- oder Polygonobjekte zu orthogonalisieren.
11. Ein einzelnes Objekt auswählen und den AI-Tag-Assistenten verwenden, um vorgeschlagene OSM-Tags von BetterID AI anzufordern. Vorschläge vor dem Anwenden prüfen und auswählen.
12. **File > Save** oder **File > Save As** verwenden, um unterstützte Vektorformate zu speichern. OSM-Uploads vor dem Senden an die konfigurierte OSM-API prüfen; der Uploaddialog kann BetterID AI um einen Entwurf für den Änderungssatzkommentar bitten.

Für Befehlszeilenworkflows werden CLI-Befehle beim Ausführen aus dem Quellcode nach `--` übergeben:

```powershell
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- import --input map.geojson
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- convert --input map.geojson --output map.gpx
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- download --bbox minLon,minLat,maxLon,maxLat --output data.osm
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- changeset --input map.geojson --output preview.osc
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj -- upload --input map.geojson --dry-run --output preview.osc
```

Echte OSM-Uploads benötigen `--yes`, `--comment` und Zugangsdaten aus `--token`, `--token-env`, `OSM_ACCESS_TOKEN` oder dem aktiven WOSM-Konto.

## Hinweise zu Kacheldiensten

Kachelanbieter können Ratenbegrenzungen, Anforderungen an Quellenangaben oder Zugriffsbeschränkungen anwenden. Prüfe die Nutzungsbedingungen des Anbieters, bevor du eine Service-URL verwendest, insbesondere bei Produktionszugangsdaten oder öffentlichen Kachelservern.

## Laufzeitdateien

Die Anwendung schreibt Laufzeitdaten unter `%LOCALAPPDATA%\WPF-OpenStreetmap-Editor\`:

- `Cache/tiles/` für heruntergeladene Kachelbilder
- `layers.json` für gespeicherte Layer-URLs
- `settings.json` für Bildquellen- und Anwendungseinstellungen
- `osm.accounts.json` für OSM-Kontometadaten; Passwörter und OAuth-Zugriffstoken werden separat in der Windows-Anmeldeinformationsverwaltung gespeichert
- `Plugins/` und `plugins.state.json` für installierte Plugins und Vertrauensstatus nativer Plugins
- `Themes/` für installierte Drittanbieter-Designs
- `window_state.json` für gespeicherte Fensterposition und -größe
- `tile_requests.log` und `startup.log` für Diagnoseprotokolle

## Dokumentation

Dokumentation für Mitwirkende und Wartung liegt in [docs/README.de.md](docs/README.de.md):

- [Entwicklungsleitfaden](docs/development.md)
- [Codestil](docs/code-style.md)
- [Testleitfaden](docs/testing.md)
- [Kartendaten, CLI-Workflows und OSM-Transfer](docs/map-data.md)
- [Designpakete](docs/themes.md)
- [Pluginarchitektur](docs/plugins.md)
- [Issues und Fehlerberichte](docs/issues.md)
- [Pull Requests](docs/pull-requests.md)

## Mitwirken

Issues und Pull Requests sind willkommen. Bitte verwende die GitHub-Vorlagen für Issues und Pull Requests, führe die Testsuite vor dem Einreichen von Änderungen aus und folge dem Projekt-Codestil in [docs/code-style.md](docs/code-style.md).

## Lizenz

Dieses Projekt ist unter der GNU General Public License v3.0 lizenziert. Details stehen in [LICENSE.txt](LICENSE.txt).
