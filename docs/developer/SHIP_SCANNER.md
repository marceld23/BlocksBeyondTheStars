# The ship scanner — readouts, planet overview, tiers, life pods and anomalies

Epic #2243 (parts #2237–#2241), branch `feat/space-scanner-wormholes`, 2026-10-04. The same epic carries the
wormholes ([WORMHOLES.md](WORMHOLES.md)) and three fixes (#2233, #2235, #2236). Status: implemented (see
[../../TODO.md](../../TODO.md) for the live Done/Open status). No terrain-generation bump and no protocol bump: the
wire changes are additive fields plus one new message (`WormholeTransitIntent`, id 285, see WORMHOLES.md).

## 1. Goal

Every ship can look at things in space. The scanner is the last slot of the flight hotbar (next to the laser and
the tractor beam); the player points the nose at something and holds fire. What it reads depends on the target:

| Target | Readout |
|---|---|
| planet / moon | the **overview card**: air, temperature, gravity, weather, water, lava, plants, animals, machines, terrain, structures, frontier and a danger level — and, from tier 2, the ore report |
| asteroid, station, wreck, life pod, bandit, Guardian machine | a sentence plus a few traits in the HUD scan panel (a wreck lists its contents, a machine its threat) |
| anomaly | knowledge + a field record (only the scanner reads it — the old "fly close" path is gone) |
| wormhole | where it leads (the pair becomes known, see WORMHOLES.md) |

The scanner has three **tiers**; the cockpit is tier 1, the `planet_scanner` module ("Deep scanner") tier 2 and
the new `quantum_scanner` tier 3. Each tier scans faster, reaches further and reads more.

## 2. Design rules

- **The server is the truth.** The client only picks a target and shows a charge; on completion it sends the
  existing `ScanEntityIntent` (space objects) or `PlanetScanRequest` (bodies). The server checks range, scannable
  kind and a per-target cooldown, then awards knowledge and answers. A modded client can't scan across the system.
- **Data-driven tiers.** Range, scan time and tier are module **stats** (`scanner_strength`, `scanner_range`,
  `scan_time` in `data/ship_modules.json`); `ShipScannerRules.For(modules, lookup)` picks the strongest. A new
  tier is a new module row, no code.
- **Upgrades replace, never stack.** `ShipModuleDefinition.Replaces` (data, e.g. `quantum_scanner` →
  `["planet_scanner"]`, also `ai_core_mk3` → `["ai_core_mk2"]`): building the new module salvages the old one
  (50 % back, like a manual removal) and the build is refused with `srv.module.superseded` if the newer one is
  already fitted. The rule lives in the module build path, not in the scanner.
- **Knowledge once.** Every first reading goes through the player's `Scanned` ledger: space objects
  `KnowledgeSpaceObject = 3`, a body's first overview `KnowledgeBodyOverview = 2` (ledger key `body:<id>`), a
  wormhole `KnowledgeWormhole = 8`, an anomaly through its own key (`AnomalyScanKey`).
- **Kid-friendly, readable.** Danger is a level word (`calm` → `dangerous`), never a number; every row has an icon.

## 3. Server

| File | What |
|---|---|
| `Shared/Definitions/ShipScannerRules.cs` | `ShipScannerSpec` (tier, range, scan time, module key) and `For(...)`; `Cockpit` is the fallback (1 / 150 / 1.2 s) |
| `GameServer/GameServerShipScanner.cs` | `ShipScanner(ship)`, `TakeShipScanTurn` (per player+target, 0.3 s), `IsScannableSpaceObject`, `ScanNewSpaceObject` (pod / station / machine / bandit readouts), `ScannedIdsIn`, `BuildBodyOverview(body, planet, tier)` |
| `GameServer/GameServerScanning.cs` `ScanSpaceEntity` | kind check → range (`ui.scan.out_of_range`) → cooldown (`ui.scan.recharging`) → wreck / wormhole / anomaly / other |
| `GameServer/GameServerPlanetScan.cs` | no module requirement any more; rows + danger for every tier, ores from tier 2, rare ores and data caches from tier 3; bodies of the current system only |

`SpaceState.ScannedIds` tells the client which entities of the instance this player has already read (so a
rescanned pod says "known", a known wormhole names its twin). The overview travels as `NetOverviewRow[]`
(`Topic`, `ValueKey`, `Level`, `DetailKey`, `Extra`) inside the existing planet-scan answer; everything is a locale
key, so the card renders in the player's language.

**Missions.** The asteroid survey and the `relay_survey` chain scan space objects; they were uncompletable while
only wrecks/anomalies were scannable and are completable again. `relay_survey_1` is `firstOnly` (#2238).

## 4. Client

| File | What |
|---|---|
| `SpaceView.cs` | the hotbar always ends with the scanner (`Kind "scanner"`, icon from `CurrentScanner().ModuleKey`); the old fallback laser is gone |
| `SpaceView.Scanner.cs` | targeting (entities + landable bodies in range; 12° auto-aim cone with auto-aim on, 4° off), lock stickiness while charging (#2247), hold progress, the lock HUD (corner brackets, an empty ring track from the lock on, the filling ring, name + distance + the "Hold {fire}: scan" line), `ChargeFx`, `CompleteScan`, the early-release line and VEGA hints |
| `PlanetOverviewCard.cs` | the right-side card for EVERY ship readout (#2247): a planet's rows with `ov_*` icons and level colours, or an object's readout (sentence, traits, threat, knowledge); a refusal shows as a short notice; auto-fades after 18 s (6 s for a notice) |
| `ScanReadoutText.cs` | the readout's words (title + description from the structured payload) — shared by the card and the hand scanner's HUD panel |
| `HudUi.cs` | the hand scanner's panel (bottom left) — hidden in the space view and never fed a ship readout (`Game.LastScanFromShip`) |
| `CraftingTechShipUI.cs` | the planet-scan button for every ship; the report reads from `Game.PlanetOverviews` |
| `GameBootstrap.cs` | `PlanetOverviews` cache, `OpenOverviewOnNextPlanetScan`, routing a piloting `ScanResult` to the card, `SpaceSystemPingUntil`, the knowledge toast, the first-card VEGA line |

**Making the hold discoverable (#2247).** The first playtest scanned and "saw no result": the trigger was tapped, not
held, so nothing reached the server — and the bottom-left HUD panel kept showing the last *surface* scan (its
"scanner in hand" test read the frozen on-foot hotbar) under VEGA's objective chip. Since then: the lock shows an empty
ring and *Hold LMB: scan* (pad RB, touch FIRE) until the ring runs; letting go early shows *Keep holding until the ring
is full* for 2.5 s and VEGA says it once (`vega.hint.ship_scan_hold`, client flag `ShipScanHoldHintShown`); the "every
ship has a scanner" tip runs on the first flight whatever system is selected; the first planet card gets
`vega.hint.planet_card` once; and the on-foot controls line is hidden in the space view (it ran under the flight
overlay's own line).

**Tier 3 system sweep.** With the Quantum scanner and no target under the nose, holding fire charges a ping
(8 s cooldown): a wide pulse, and for 60 s the radar pins every scannable object of the system at its rim
(`Game.SpaceSystemPingUntil`). It is a client presentation of data the client already has; nothing is revealed that
the instance did not already send.

**Effects and sounds.** The charge fans four beams from the nose to the target's corners, sweeps a holo plane and
pulls motes back; completion runs `FxScanWave` over the target, a flash and a short FX light. The colour comes from
the module's data-driven `fx` look (cockpit cyan, Deep scanner teal `#4dffc8`, Quantum gold `#ffe08a`); remote
players see the scan through the existing ship-FX relay (`FxActionKinds.Scan`). Sounds: `ship_scan_lock`,
`ship_scan_charge`, `ship_scan_complete`, `ship_scan_hostile`, `planet_scan_overview` (ElevenLabs, see
[SOUND_DESIGN.md](SOUND_DESIGN.md)). Icons: `item_ship_scanner`, `item_quantum_scanner`, `item_sensor_lens`,
`item_quantum_sensor` (OpenAI via `tools/ai-assets/gen_item_icons.py`) and the overview row icons `ov_*`
(procedural, `tools/ai-assets/gen_hud_icons.py`).

## 5. Tiers and content

| Tier | Module | Stats (strength / range / time) | Unlock | Component |
|---|---|---|---|---|
| 1 | `cockpit` | 1 / 150 / 1.2 s | every ship | — |
| 2 | `planet_scanner` (now "Deep scanner") | 2 / 300 / 0.8 s | blueprint `planet_scanner` | `sensor_lens` (workshop) |
| 3 | `quantum_scanner` | 3 / 500 / 0.5 s | blueprint `quantum_scanner` (needs `planet_scanner` + `radar_array`, 120 knowledge) | `quantum_sensor` (workshop) |

Ranges are flight-frame units (the same frame as the instance entity positions). Old saves keep a fitted
`planet_scanner`; it simply became tier 2.

## 6. Life pods and anomalies (#2241)

Both used to render as the generic red placeholder cube. They now have their own models, built in
`SpaceView.Encounters.cs`:

- **Life pod** (`EscapePodView`): a small capsule with orange bands, a porthole with a waving figure, an antenna and
  a blinking beacon (`pod_beacon` loop). When the server removes it (rescue on fly-close), `OnEntityGone` plays a
  short tractor effect and `pod_rescue`.
- **Anomaly** (`AnomalyView`): a soap-bubble shell (`FxShell` with a property block) with glitching `Unlit/Color`
  cubes orbiting it and a calm hum (`anomaly_hum`); a completed scan calls `React()` — a ripple, `anomaly_react`,
  then a calmer state.

Both are static encounters (never interpolated), show on the radar (pod orange, anomaly violet pulse) pinned at the
rim, and on the system chart as markers and waypoint targets. VEGA mentions once that an anomaly needs the scanner
(`vega.hint.anomaly_scan`).

## 7. Tests

- `ShipScannerTests` — tier selection, reach per tier, anomaly knowledge once, readouts for pods / stations /
  machines / raiders, no scanning of drops and no hammering one target, the overview per tier (and without loading
  another body's world), the Quantum scanner replacing the Deep scanner, the content wiring.
- `PlanetScannerTests.Scan_WithoutTheModule_GivesTheOverview_ButNoResources` — tier 1 gets rows, no ores.
- `NetCodecTests` — the golden message list (285).

## 8. Adding things

- **A new tier:** add a module with `scanner_strength` > 3, range, time, an `fx` look and `replaces` the previous
  tier; a blueprint and an icon. Server and client pick it up from the stats.
- **A new overview topic:** add a row in `BuildBodyOverview`, the locale keys `ui.overview.topic.<t>` /
  `ui.overview.<t>.<value>` and an icon — `PlanetOverviewCard.TopicIcon` maps a few topics to existing HUD icons
  (air, temperature, water, structures, frontier) and the rest to `ov_<t>`; a missing icon leaves the row without one.
