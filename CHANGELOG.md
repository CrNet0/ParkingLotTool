# Changelog

## 1.0.4 — October 4, 2026

- Traffic inside parking lots drives at 25 km/h, so through traffic avoids them.
- "Update" brings existing lots to the new speed in seconds.
- New option: trees don't age; save and reset for the vegetation window.
- Pending updates are shown after loading; a click opens the lot list.
- Crash reports now show where the game crashed; the button is now "Report the last crash".
- Fix: crash when editing a lot with bus stops while utilities were connected.
- Fix: bus stops disappeared after editing a lot.
- Fix: cancelling an edit could remove the lot's paths.
- Fix: crash after loading a save whose lots use surfaces from other mods.

## 1.0.3 — October 1, 2026

- Crash reports show how far the mod got while loading a save.
- Fix: the crash notice pointed to a tab players cannot see.

## 1.0.2 — September 29, 2026

- Clearer status messages: a missing preview now says why and what to do;
  all messages fully translated.
- New status messages for the next step (preview ready, lot built).
- Fix: the preview could stop appearing until the game was restarted.

## 1.0.1 — September 28, 2026

- Fix: EV charging stations could be scattered across the lot.
- Find It added to the dependencies (required by Asset Icon Library).

## 1.0.0 — September 28, 2026

- First release on Paradox Mods.

## Unreleased — September 20, 2026

- Preserve existing zoning road entities when an edit leaves the complete
  zoning road layout and prefab unchanged. Transfer their ownership to the
  replacement lot instead of rebuilding them, preserving the basis for zone
  painting and buildings. In-game retention validation is still pending.

- Add Alley In and Alley Out entrances using both driving lanes of a PLT
  clone of the regular Alley, with reversed course orientation for exits.
  No RoadBuilder dependency or Vanilla Alley Oneway parking strips are used.
- Refine entrance widths, surface coverage, arrows and terrain transitions;
  refresh surviving street junctions after removing old parking lot edges.
- Use a separate pedestrian entrance prefab that permits street connections
  while keeping automatic extensions disabled for internal pedestrian paths.
- Reduce utility connection planning work with bounded searches, exact
  projection fixed points and candidate-local start caches. Replace the fixed
  post-build delay with readiness checks and retain failed-target history.
- Validate utility connections through newly created vertical pipe sections
  and skip utility construction when no nearby street is available.
- Accelerate charging-station placement with conservative spatial filtering
  before the existing exact collision checks.
- Vary free-mode vegetation across equal-sized planting areas, sample across
  full search cells and add smoothly varying planting density. Persist the
  random seed in vegetation options; keep Line placement unchanged.
- Extend entrance, utility, vegetation and parking-facility diagnostics and
  regression tests, including mutation checks.
- Correct quoting in the existing stale temporary build-cache cleanup target.

Validation: Release build and all 27 required checks passed; vegetation tests
passed 23,165 checks and detected five injected faults. The Release build
still reports two existing CS0162 warnings.

Known limits: Alley clones still reference Vanilla section/piece assets;
full asset isolation has not been implemented. The intermittent Alley visual
issue was not conclusively traced to a particular shared resource. In/Out
behavior was confirmed in game, but this does not establish long-term visual
stability. The new vegetation distribution still needs visual acceptance.

Alle Aenderungen, die es in eine veroeffentlichte Version geschafft haben.
Dieselben Zeilen gehoeren vor jeder Veroeffentlichung in `<ChangeLog>` in
`Properties/PublishConfiguration.xml`.

## 1.0.0 - noch nicht veroeffentlicht

Erste Fassung.

- Parkplatz aus einer frei gezeichneten Flaeche: Buchten, Fahrgassen,
  Querstrassen, Randstrasse und die beiden Bodenflaechen.
- Die Buchten sind echte Parkspuren - Autos fahren hinein und parken.
- Zufahrten von Hand setzen, sie fangen sich an einer vorhandenen Strasse.
- Ecken und Kanten des Polygons lassen sich vor dem Bauen ziehen.
- Die Flaechenauswahl zeigt Vorschaubilder statt nur Namen, zwei
  Kacheln nebeneinander (braucht die Asset Icon Library).
- Einstellbar: Randabstand, Reihenwinkel, Fahrgassen- und
  Querstrassenbreite, Verbindung alle N Buchten, Mittelgruen und seine
  Tiefe, Kappen an Querstrassen, beide Flaechen (frei waehlbar oder aus),
  Buchtmarkierung.
- Jeder Wert laesst sich als eigener Standard speichern.
- Der ganze Parkplatz hat einen Besitzer: einmal anklicken, einmal
  abreissen.
- Panel auf Englisch, Deutsch in den Modeinstellungen.
- "Report a problem": Stelle in der Welt markieren, kurzen Bericht
  schreiben lassen.
