# WOSM

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md) | [Deutsch](README.de.md)

WOSM steht für WPF OpenStreetMap Editor und ist ein in C# / WPF geschriebener OpenStreetMap-Editor. Langfristig soll es eine praktische C#-Alternative zu JOSM werden.

Die aktuelle Anwendung kann OpenStreetMap-kompatible Kachel-Layer laden, Kartenbilder anzeigen und wiederverwendbare URLs für Kacheldienste verwalten.

> Projektstatus: frühe Entwicklung. Die aktuelle Anwendung konzentriert sich auf Kachelrendering, Layer-Konfiguration, Caching und die begleitende technische Infrastruktur.

## Funktionen

- Lädt XYZ-, TMS-, ArcGIS-ähnliche und WMTS-ähnliche Kachel-URL-Vorlagen.
- Unterstützt gängige Platzhalter wie `{z}`, `{x}`, `{y}`, `{-y}`, `{s}`, `{switch:a,b,c}`, `{zoom}`, `{TileMatrix}`, `{TileCol}`, `{TileRow}` und `{access_token}`.
- Rendert Kartenkacheln in einem WPF Canvas mit Mausverschiebung und Zoomsteuerung.
- Speichert heruntergeladene Kacheln im Laufzeitverzeichnis der Anwendung.
- Speichert wiederverwendbare Layer-URLs in `layers.json`.
- Protokolliert Kachelanfragen und Ladefehler zur Fehlersuche.
- Enthaelt Unit-Tests fuer Koordinatenumrechnung, URL-Analyse, Cache-Pfade und Laufzeitpfade.

## Anforderungen

- Windows
- .NET SDK 10.0 oder neuer

## Projektstruktur

```text
src/WPF-OpenStreetmap-Editor/          WPF-Anwendungsquellcode
tests/WPF-OpenStreetmap-Editor.Tests/  Unit-Tests
docs/                                  Dokumentation für Mitwirkende
scripts/                               Lokale CLI-Hilfsskripte
.github/workflows/                     CI-Workflowdefinitionen
```

## Einstieg

Repository klonen und im Repository-Stammverzeichnis bauen:

```powershell
.\scripts\build.ps1
```

Testsuite ausführen:

```powershell
.\scripts\test.ps1
```

Die Anwendung kann auch direkt mit der .NET CLI gestartet werden:

```powershell
dotnet run --project .\src\WPF-OpenStreetmap-Editor\WPF-OpenStreetmap-Editor.csproj
```

## Verwendung

1. Anwendung starten.
2. Im Feld fuer die Karten-URL eine Kachel-URL-Vorlage eingeben.
3. Wenn der Anbieter `{access_token}` verlangt, ein Zugriffstoken eingeben.
4. Nach dem Laden der Karte mit Mausrad, `+` / `-` oder `Page Up` / `Page Down` zoomen.
5. Den Karten-Canvas ziehen, um die Karte zu verschieben.
6. Das Layer-Fenster im Tools-Menü öffnen, um gespeicherte Layer-URLs zu verwalten.

## Hinweise zu Kacheldiensten

Kachelanbieter können Ratenbegrenzungen, Anforderungen an die Quellenangabe oder Zugriffsbeschränkungen haben. Prüfe vor der Verwendung einer Service-URL die Nutzungsbedingungen des Anbieters, besonders bei Produktionszugangsdaten oder öffentlichen Kachelservern.

## Laufzeitdateien

Die Anwendung schreibt Laufzeitdaten unterhalb des Anwendungsbasisverzeichnisses:

- `Cache/tiles/`: heruntergeladene Kachelbilder
- `layers.json`: gespeicherte Layer-URLs
- `tile_requests.log`: Diagnoselog fuer Kachelanfragen

## Dokumentation

Dokumentation für Mitwirkende und Wartung liegt in [docs/README.md](docs/README.md):

- [Entwicklungsleitfaden](docs/development.md)
- [Codestil](docs/code-style.md)
- [Testleitfaden](docs/testing.md)
- [Issues und Fehlerberichte](docs/issues.md)
- [Pull Requests](docs/pull-requests.md)

## Mitwirken

Issues und Pull Requests sind willkommen. Bitte verwende die GitHub-Vorlagen für Issues und Pull Requests, führe die Testsuite vor dem Einreichen von Änderungen aus und folge dem Projekt-Codestil in [docs/code-style.md](docs/code-style.md).

## Lizenz

Dieses Projekt ist unter der GNU General Public License v3.0 lizenziert. Details stehen in [LICENSE.txt](LICENSE.txt).
