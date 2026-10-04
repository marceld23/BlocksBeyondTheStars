# Wormholes — rare two-way rifts between star systems

Issue #2242 (epic #2243), branch `feat/space-scanner-wormholes`, 2026-10-04. Status: implemented (see
[../../TODO.md](../../TODO.md)). No terrain-generation bump: placement runs after the galaxy generator with its own
salt, so existing galaxies keep every system, body and lane. No protocol bump: one new message
(`WormholeTransitIntent`, NetCodec id 285) plus additive fields.

## 1. Goal

A few systems hold a **wormhole**: a glowing tear in space-time that links two star systems and works in **both
directions**. Flying through needs no jump generator, so it is an early way to reach a far system, and a reason to
scan what you find. Wormholes are rare and **never lead into the story's special systems**.

## 2. Rules

- **Seed-pure, never saved.** The layout is `WormholePlacer.Place(systems, seed, frequency, fixedCount, settings)`,
  computed in `BuildGalaxy` after `UniverseGenerator` (world = seed + parameters + deltas). Only the player's
  knowledge of a pair is stored (the `Scanned` ledger, key `WormholeScanKey(id)`).
- **Pairs only.** Ends come in pairs that point at each other (`LinkedId`); at most one end per system, so there are
  no chains. The two ends are at least `MinMapDistance` apart on the star map (else the farthest free system).
- **Where.** Each end opens beyond the outermost orbit of its system (`EdgeMargin` + up to `EdgeSpread`, a seeded
  angle and height), far from the planets and the station.
- **Which systems.** Only the procedural `sys*` systems of the galaxy's fixed prefix (`sys0 … sys(fixedCount-1)`).
  The finale system (`guardian_finale`) and any other hand-made system are never candidates; a *Growing* galaxy's
  new systems never get or move a wormhole. The start system and landmark systems are allowed.
- **Checked again at transit.** `IsStoryLockedSystem` runs for both ends on every transit, so an edited save or a
  modded client cannot open a way to the Guardian.
- **How many.** The world option `Wormholes` (`WorldDescription.Wormholes`, a `Frequency`): Off · VeryRare · Rare
  (default) · Normal · Frequent → 0 / 0.5 / 1 / 1.5 / 2.5 pairs per 12 systems (`data/wormholes.json`), with a seeded
  rounding — a standard 12-system galaxy on *Rare* has one pair. Server config key `wormholes=<frequency>`; the
  world-creation screen has a slider row.

All numbers live in `data/wormholes.json` (`WormholeDefinition`):

| Field | Default | Meaning |
|---|---|---|
| `veryRarePairsPer12` … `frequentPairsPer12` | 0.5 / 1 / 1.5 / 2.5 | pairs per 12 systems per frequency |
| `minMapDistance` | 350 | star-map distance between the two ends of a pair |
| `edgeMargin`, `edgeSpread` | 320, 220 | how far beyond the outer orbit an end opens |
| `transitRange` | 26 | flight-frame distance at which the [E] prompt appears (the server allows ×1.6 slack) |
| `arrivalOffset` | 45 | how far in front of the twin rift the ship comes out |
| `arrivalLockSeconds` | 6 | no transit right after an arrival (no ping-pong by a held key) |

## 3. Server

`GameServerWormholes.cs`:

- `AddSpaceWormholes(instance, anchor)` adds the anchor system's end to a flight instance as a
  `CombatEntityKind.Wormhole` entity (never hostile, never a weapon target), at its system position in the flight
  frame (`WormholeFlightPosition`, the same layout transform as the wrecks).
- `TraverseWormhole(playerId, wormholeId)` — on `WormholeTransitIntent`. Gates in order: in a flight instance, aboard
  and not on an EVA, the entity and its twin exist, within reach, neither end story-locked, no arrival lock, an
  anchor body in the twin system, and the ship can fly (`ShipLaunchProblem`). Then: `SpaceWarpFx` with
  `Style = "wormhole"` to the instance, `LeaveSpace`, move the player's location into the twin system, mark both ends
  known and the system known (`MarkSystemKnown`), count `AchievementCounters.Wormhole` (achievement `rift_rider`),
  set the arrival lock, and `EnterSpace(..., resume, wormhole: true)` with a pose `arrivalOffset` in front of the
  twin, facing away from it. A star-map refresh and a chat line follow.
- `ScanWormhole` — the ship scanner reads where an end leads; the pair becomes known (both ends), the first reading
  pays `KnowledgeWormhole = 8`, and the instance is re-sent so the rift's label names its twin.
- `WormholesFor(session, currentSystemId)` — the ends a player may see on the star map: every end in a system they
  know or are in, with the twin's system only when they know the pair (otherwise the client shows **???**).

## 4. Client

| File | What |
|---|---|
| `WormholeView.cs` + `Shaders/Wormhole.shader` | the rift: a generated jagged tear mesh, a billboarded halo, crackling arcs, motes, an FX light and the `wormhole_hum` loop. On URP with the opaque texture (Medium+ quality, `_Sc_ScreenFx`) the shader bends the scene behind it (`SampleSceneColor`); otherwise an emissive fallback. `WormholeVisuals.FlashAt` is the burst other players see. |
| `WormholeTransitFx.cs` | the full-screen transit: a crack texture spreading over an overlay canvas (sort order 72), the rush, then the arrival flash; a timeout clears it if the arrival never comes |
| `SpaceView.Encounters.cs` | `BuildWormholeModel`, `UpdateNearWormhole` (the [E] prompt `ui.space.wormhole_prompt_fmt`, which takes priority over station boarding), `BeginWormholeTransit` (sends the intent, 2 s client cooldown), `OnWormholeArrived` |
| `GameBootstrap.cs` | `WormholeArrived` fires when a `SpaceState` with `Wormhole = true` arrives |
| `SpaceRadar.cs`, `SpaceMap.cs`, `GalaxyChartWidget.cs` | violet zigzag marker on the radar (pinned at the rim), a chart marker + waypoint target, and known pairs as violet zigzag lines on the galaxy chart |

The custom shader is registered in all three places custom shaders need (GraphicsSettings *Always Included*,
`BuildScript.RuntimeShaders`, `ShaderPrewarm.FxShaders`); the shader keeps every property in the
`UnityPerMaterial` cbuffer for the SRP Batcher. VEGA explains a wormhole once when the first one comes into view
(`vega.hint.wormhole`). Sounds: `wormhole_hum`, `wormhole_enter`, `wormhole_exit` (see
[SOUND_DESIGN.md](SOUND_DESIGN.md)).

## 5. Tests

`WormholeTests`: a standard universe has exactly one pair, two-way, far apart and beyond every orbit (several
seeds); placement is seed-stable with one end per system; growing the galaxy never moves a wormhole and `Off` places
none; no end ever opens in a story system; a standard world's end is an entity in that system's flight; flying
through needs no jump generator and works both ways; it is refused from afar, on an EVA and for an unknown rift;
a wormhole into the story system is refused even when the galaxy data says it exists; the scanner reads where a
wormhole leads. `NetCodecTests` pins id 285.
