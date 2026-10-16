# Derelict stations — broken old stations nobody lives on

Epic #2637 — phase 1 #2632 (placement, look, vacuum, nobody home) and #2634 (minable, the damage shows outside, broken
trading posts), 2026-10-15. Status: implemented (see [../../TODO.md](../../TODO.md)). Later phases build on the hooks in
§8: #2633 (crates with old supplies and random logs), #2635 (guardian spawner + robots) and #2636 (falling out is a real
spacewalk — §5). The rework of 2026-10-16 (Layex on v2026.10.17, in orbit next to a derelict: "WRONG model — from the
outside they should look like the old station models from the beta phase, only a bit more broken!"): #2674 (every new
derelict is the wreck of a **beta station**, §3), #2675 (**no air inside and no residents** — dead life support, §5) and
#2676 (sparks, tumbling pieces and a flickering beacon in the flight view, §9).

No terrain-generation bump and no save-version bump: placement is a pure pass after the galaxy generator with its own
salt (like the [debris fields](DEBRIS_FIELDS.md)), the package adds no blocks, and the persisted state reuses the
existing `space_structures` table and structure edits. (The guardian bay of #2635 is a new block and raises the save
version to **4** — see [§ Guardians](#guardians--the-guardian-bay-2635).) No protocol bump: a derelict is a `SpaceStation` body/contact
whose id carries the prefix `derelict:`; the client tells it apart by that id. The beta rework adds no block, no
protocol change and no save-version bump either: which shape a stored derelict has is one `WorldMetadata.GeneratedLoot`
key (§7).

## 1. What a derelict is

Screelit (Justus), 2026-10-15: "at least once in a new world — a broken space station that was destroyed, looking like
the old (beta) stations, only broken. You can fall through holes into space and slowly suffocate. Old trader posts.
Nobody lives there. It should be minable and look more and more damaged from outside the more you mine."

Decisions (maintainer, 2026-10-15): one derelict guaranteed per world, otherwise a system rarely holds one to three; a
world option scales that; nobody lives there; the story's own derelict "The Long Quiet" (#1129, `derelict:longquiet`)
stays its own site. "The old stations" first meant the boxy stations of the day — the procedural iron/glass module
generator (`StationGenerator.Generate`) and the classic complete templates of `data/station_templates.json`.

Decisions (maintainer, 2026-10-16): "the beta shape" is the simple boxy model every NPC station showed in flight until
v2026.9.9 (`SpaceView.BuildStationModel`) — built as a **real, walkable voxel station** (the outside is the inside:
mining shows outside, the spacewalk and EVA mining meet the same hull; never a client skin, never rotated) and broken.
**Every new derelict** is one, the guaranteed one included; a derelict a save already stored keeps its old layout (§3.4).
A derelict is a wreck: **no air inside** (dead life support — even a room the player seals stays airless) and no
residents. The Long Quiet keeps its blanket air and is otherwise untouched.

## 2. Placement — `DerelictPlacer` (WorldGeneration)

`GameServer.BuildGalaxy` runs `PlaceDerelicts()` as its LAST pass: after the wormholes, the debris fields, the names and
`EnsureStartSystemStation` — so the guaranteed start station never counts a derelict as "the system's station" and no
existing body moves. `DerelictPlacer.Place(systems, seed, frequency, fixedCount, startSystemId)`:

- Only the fixed procedural systems `sys0 … sys(fixedCount-1)` are candidates; the finale system and grown systems never.
- The start system (the system of the start body) always holds at least one — body `derelict:<sys>-0`. Since #2674 it is
  a beta wreck like every other; its "guaranteed" flag only matters to the legacy layout (§3.4, where it forced a medium
  module station).
- Any other system rolls `Frequency.Probability() × OddsScale (0.3)` — Normal ≈ one system in eight — and then holds 1
  (75 %), 2 or rarely 3 (`MaxPerSystem`).
- Each is a `CelestialKind.SpaceStation` body, id `derelict:<sys>-<n>`, a language-neutral coined name with an old
  designation ("Kiravon IV" — the localized kind "Derelict station" stands beside it in every UI), a star-map spot
  500 units beside a seeded planet/moon/asteroid clear of the other bodies, and `ParentId` = that body (the sky aboard).
- Off places none (the guaranteed one included). Idempotent; seed-stable; a save from before the feature gains its
  derelicts at its next start (`WorldDescription.Derelicts` reads the default *Normal*).

World option `WorldDescription.Derelicts` (`Frequency`, default *Normal*): CLI key `derelicts`, client
`WorldCreationOptions.Derelicts` (`ToArgs` → `--derelicts`), a slider on the structures page of the world options
right below the debris fields (`ui.worldopt.derelicts`). There is no admin/portal control (the debris fields have none
either). The persisted statuses (Visited) are applied to the new bodies in `PlaceDerelicts`.

## 3. The layout — `DerelictGenerator` (WorldGeneration)

`DerelictGenerator.Generate(seed, content)` → `DerelictLayout` (a voxel grid + spots, `Source = "beta"`, tier `large`),
deterministic: the beta station (§3.1) run through the shared decay pass (§3.2) and the beta wreck damage (§3.3).
`DerelictGenerator.GenerateLegacy(seed, content, proceduralOnly)` is the pre-#2674 path for stored derelicts (§3.4).

### 3.1 The beta station — `BetaStationGenerator`

The old flight model, about four blocks per model unit (it was 4 × 4 × 4 units of hub), never rotated. Coordinates are
blocks relative to the station centre (the middle of the hub at the arms' height); all constants are public on
`BetaStationGenerator` (the silhouette test reads them). Grid 77 × 27 × 51 (a one-block margin round the shape).

| Part | Old model (units) | Voxel build (blocks) |
|---|---|---|
| Hub | iron cube 4 × 4 × 4 | iron cube 15 × 15 × 15: a solid machine base under the command deck (deck layer −2, feet −1, 8 high inside), roof at +7 |
| Collar | dark 5.6 × 1 × 5.6 at the centre | carbon ring out to ±10, layers −3 … −1 |
| View band | glass 4.2 × 1 × 4.2 just above the centre | glass in the hub walls, layers 0 … 3 (eye height on the deck), iron corner columns |
| Arms | dark, 11 long, 0.8 thick, along X and Z | four carbon tubes, outside 5 × 5, inside 3 wide × 3 high, from the hub wall (±7) to the pod (±16); a light in each roof |
| Pods | iron 1.8³ at ±5 | iron cubes 9 × 9 × 9 at ±20 (inside 7 × 5 × 7), a solid base, a small window in each side wall |
| Solar wings | flat deep blue 3.2 × 0.15 × 5.2 at ±7.4 on X | one block thick on layer 0, from ±25 to ±37, 21 wide: blue-tinted glass (`StationKitExterior.SolarTint`, the kit stations' solar blue) in a carbon frame grid, on the outer faces of the ±X pods |
| Mast + beacon | carbon 2.6 tall + a yellow beacon | carbon on a 3 × 3 foot, layers +8 … +16, the beacon (`light_white`, tinted `BeaconTint` yellow) on +17 |

Doorways: a 2-wide, 3-high cut at both joints of every arm (the standard station airlock cut); the `door_slide` marker
names its wall's axis in `StationMarker.Data` (`x` / `z`), so the decay needs no module grid to read it. The pods: the
**hangar** is always the +Z pod (it faces the launch point — the derelicts float behind it), its outer wall a force-field
mouth, a cargo deck and stacked cargo; the **trading corner** (a counter with a `vendor` marker in its middle), the
**medbay** (benches, two heal tanks) and the **quarters** (bunks) are shuffled over +X, −X and −Z by the seed. Lights
are `data_cache` cells (the decay turns them into strips). The `spawn` marker is the middle of the command deck.

### 3.2 The decay pass (shared)

1. **Plants out first** (`RemoveFlora`) — the rooms are judged with the space they leave.
2. **Rooms.** The space the outside flood cannot reach, split at the doorways (the `door_slide` markers; their wall
   plane comes from the marker's axis — a beta station — or the module grid). A classic template is one hall judged by
   roof and deck (`LabelInnerAir`).
3. **Which rooms stay closed.** No room breathes since #2675 — "closed" is geometry: a closed room keeps its door and a
   warm emergency light, a breached one is open to space. The arrival room (the spawn marker: the hub, a module
   station's command room, a template's hall) is never breached; the hangar loses its force field and stands open;
   every other room is breached with 60 % (at least one when there are two or more). A room with no outer face to tear
   stays closed.
4. **Doors.** A doorway with a closed room on either side gets an intact door: one column walled up (a station door is
   one block wide) and a door record (`DerelictDoor`, kind slide) in the other. A doorway between two breached rooms
   stays open.
5. **Breaches.** One rough hole (radius 1.3–2.5) per breached room in a wall, floor or roof between its inside and
   space — never in a cell a closed room relies on (`ProtectedCells`: the 6-neighbours of its space, the doorway frames,
   the arrival column).
6. **Strip + light.** Force fields → glass beside a closed room, else gone; data caches → warm emergency strips
   (`strip_light_warm`) in closed rooms, dead strips (`strip_light_cyan_off`) elsewhere; heal tanks → scrap.
7. **Torn panels and scorch.** 6 % of the unprotected outward cells go missing; plating is scorched to carbon — 55 %
   within four blocks of a breach, 7 % on the outer hull, 3 % inside.
8. **The beta wreck** (§3.3; a beta station only).
9. **Broken trading posts.** Up to three vendor stalls (the `vendor` markers) become a plain `station_vendor` block with
   scorched deck and a scrap pile beside it. No marker is produced.
10. **Scrap on the decks** (2–4 pieces in a breached room, 0–1 in a closed one) and the arrival column cleared. On a beta
    station nothing (scrap, a crate) is set right in front of a doorway — its arms are narrow tubes.
11. **Spots** for the later phases (§8) — on a beta station the guardian bay never goes into the open hangar (its
    guardians would drift out of the mouth) — and the verdict (`ArrivalSealed` = the arrival room is closed,
    `SealedRoomCount`, `BreachedRoomCount`).

### 3.3 The beta wreck — `Grid.BetaDamage`

On top of the shared decay, all on the outside (no room is opened by it): one **solar wing half torn off** — a ragged
edge row by row around its middle, the cells along it scorched (60 %), two to four of its cells drifting just beyond;
the **mast snapped** two to four blocks above its foot, its top (three carbon cells) floating one block above the break
and one to the side with the **dead beacon** (`light_white_off`, still tinted yellow) on it; four to seven **rusted
patches** (`rusted_panel`, an airtight full cube like the iron it replaces) on the outer iron hull; and three to five
**loose debris blocks** (scrap metal, carbon, iron) floating in free space within four blocks of the hull.

### 3.4 Stored old derelicts — `GenerateLegacy`

Before #2674 a derelict was 80 % a module station (small 35 % / medium 45 % / large 20 %; the guaranteed one always a
medium) and 20 % a classic template (by weight), through the same decay; when the arrival's pocket leaked by the
station air rule, `SealArrivalPocket` patched it shut (#2636 — the arrival held air then). A derelict a save already
stored was built from exactly that layout: its row's cells, its crates, its guardian bay, its interior world and its
flight position. So the server rebuilds such a derelict with `GenerateLegacy` (§7 decides which) — and that path must
keep producing the same layouts, patch included. `DerelictStationTests.TheLegacyGenerator_StillBuildsTheLayoutsOldSavesWereMadeFrom`
pins fingerprints of seven legacy layouts (block keys, dyes, shapes, spots, doors): if a change to the decay pass, to
`StationGenerator.Generate` or to a classic template moves them, every stored old derelict would be rebuilt from a layout
that no longer matches its build — undo it for the legacy path (or decide on a migration) before updating the pins.
Every beta-only step is guarded by the `beta` flag of `Decay`, so the legacy path draws exactly the random numbers it
always drew.

## 4. On the server — `GameServerDerelicts.cs`

A derelict rides the **player-station machinery**, not the NPC-station one:

- `RegisterDerelicts()` (registries boot stage, right after `LoadPlayerStations` and The Long Quiet) registers the
  sites only; `EnsureDerelict(id)` builds one on first need — called from `GetOrCreateStation`, so the flight contacts,
  a menu travel and a rejoin all build it on the way (a server that never flies there never pays for it). It generates
  the layout — a beta wreck, or the legacy layout for a stored old one (`UsesLegacyDerelictShape`, §7) — and the
  `SpaceStructure` (kind `station`, owner empty, `Boardable`) — from its persisted row when it has one (block ids from
  the row, dye/shape back from the layout where the block is unchanged), else fresh from the layout — puts it into
  `_playerStationCells` and registers a `BoardableStation` (stamp origin `(8,64,8)`, the persisted stamp anchor). The
  door records come from the save once anything was stored, else from the layout.
- **In flight** it has no host body, so it is a contact in every orbit instance of its system (`AddStationContacts`),
  its hull added by `AddDerelictHulls`, at a fixed spot behind the launch point above the flight plane
  (`DerelictSpacePosition`: lanes x = −95/0/95, z ≤ −110, y ≥ 50 — a beta wreck is 77 wide, so three side by side keep
  18 blocks between their wing tips). Its position follows the ORIGINAL layout, so mining an edge never moves it.
- **Boarding** (`BoardStation` → `EnterBoardedStation`) stamps it with `StampPlayerStation` into `station:derelict:…`;
  the arrival search (`TryFindStandableInStation`) starts at the layout's arrival (`DerelictArrivalHint`) and, for a
  generated derelict, takes the standable deck nearest to it **under the hull's roof** (`RoofedInStation` — never its
  top face), else any standable cell; no air is asked for (#2675). No crew, no visiting trader (`SpawnStationNpcs` is
  skipped for every `derelict:` id), no placeables (`RegisterPlayerStationPlaceables` is skipped — so a `station_vendor`
  block there never becomes a trade marker). `OnDerelictBoarded`: the very first boarding of any derelict gets VEGA's
  hint `vega.hint.derelict_no_air` (`ShipAiHintOnce`, kind 2 — never muted: no air aboard, the suit tank is the air,
  the ship refills it); after that the first boarding of each further derelict gets `vega.derelict.boarded` (kind 2).
  Every boarding re-arms the one-time "no air" warning (§5). From a spacewalk, E boards through the nearer of a door
  (#2607) and an opening the suit floats at (#2636, §5).
- **Nobody's station:** the trading rules skip every `derelict:` id — the start station guarantee (placement order),
  the system fallback station (`StationContactsForCurrentSystem` still adds it when the system holds only derelicts),
  trader traffic (`AmbientTrafficFor`, the docking pick), the dock lesson (`NearestStationFor`, `StationInSystem` →
  `TradingStationsForCurrentSystem`), VEGA's `station_near` tip, and the home spawn (`HandleSetSpawnPoint` refuses with
  `srv.derelict.no_home`, `TryRespawnAtHomeStation` falls back to the ship).
- **Scanner:** subject `derelict_station`, info `ui.scan.derelict` (no air aboard), trait
  `ui.scan.trait.derelict_breached` + the size (`large` for a beta wreck), ledger `space:derelict`. **VEGA in flight:**
  `CheckDerelictApproach` (with the wreck approach on every ship pose) says `vega.derelict.approach` once per player and
  derelict within 110 units.

## 5. Air, gravity, falling out

- **Dead life support (#2675).** A generated derelict has no air anywhere, sealed or not: `StationLifeSupport` returns
  false for every `derelict:` id but The Long Quiet's, before any pocket is filled — so even a room the player closes
  with blocks or a door stays airless, and the HUD's life-support source is none. The suit tank drains aboard as on a
  spacewalk; the ship's life support refills it. The first time a player stands aboard after each boarding the server
  sends `@srv.derelict.no_air` once ("No air aboard … your suit tank is giving you air now; our ship fills it up again" —
  it names the suit tank; re-armed in `OnDerelictBoarded`), instead of the player stations' hull-open warning, which
  would promise that patching helps. `BreathableAirAt` (flames) is false there too. `UsesSealedPocketAir` /
  `IsSealedAirStationWorld` still cover the derelict: its world has no blanket air (`InitWeather`, `BodyBreathable`),
  and the pockets still tell a closed room from a breached one where that matters for placement (a boarder through a
  door is set on the door's closed side, `StationDoorArrival`; the tests' breached-deck probe). The Long Quiet keeps its
  blanket air.
- Gravity: `TryGetBoardedPlayerStation` now covers every `derelict:` id — The Long Quiet included (walking off its open
  corner used to fall for ever; probed and fixed). Inside the box + 8 the boarder walks, outside the suit floats.
- **Leaving the hull is a real spacewalk (#2636, `GameServerDerelictSpacewalk.cs`).** Screelit: "You can fall through
  holes into space and then slowly suffocate." Caught in ONE place, `RescueDriftingBoarder` (the 1 Hz void check): a
  generated derelict's boarder `DerelictSpacewalkMargin` (2) blocks clear of the hull's own cell box — outside every
  cell of the hull by construction, so a deck, the roof or a jump on it never triggers it — is queued and stepped out
  after the world loop (`TickDerelictSpacewalks`, a world change never runs inside another world's tick):
  - `LeaveStation(playerId, spacewalkFrom)` — the undock path — with the ship's float spot in `_dockedFromEva`: the
    spot remembered at a spacewalk boarding when it lies 12+ blocks clear of the hull (`BoardStation` remembers the
    SUIT's spot — at a door or inside a breach a ship would stick in the hull), else `DerelictShipWaitPoint` (20 blocks
    off the hull box's face toward the launch point). The flight opens resumed (no take-off) on an EVA: `EnterSpace(…, evaStart)` seeds the
    suit's pose, `SpaceState.HasEvaStart/EvaStartX/Y/Z/Yaw` (additive) tells the flight view, which starts the suit
    there (`GameBootstrap.TakeSpaceEvaStart` in `SpaceView.BeginEvaMode`) instead of behind the ship. No undock line,
    no `StationLeft` event; VEGA says `vega.derelict.spacewalk` instead (kind 3, never muted). The suit tank drains as
    on every EVA (`InEva` overrides every life support) — no instant death.
  - **The mapping** inverts the door rule (`StationDoorNearSuit`): interior position → build cell
    (`− Origin + StampMin`, X on the lap nearest the origin) → flight (`structure position + cell − cell-box centre`;
    a derelict is never rotated), held within `DerelictSpacewalkStartReach` (6) of the box — a long fall the 1 Hz check
    caught late still starts beside the hull — and raised to the suit's eye (+1.5, the EVA camera).
  - No flight to open (free flight off, a ship that can't fly — `SpaceLaunchProblem`): the old pull-back
    (`ReturnDriftingDerelictBoarder`, `srv.derelict.drifted_back`).
- **Coming back.** E on the spacewalk boards as at every station; for a generated derelict `BoardStation` also asks
  `DerelictBreachNearSuit`: from the suit's body cell through open build cells (air, no doorway) for at most 6 steps,
  the suit within 3 blocks of the box, the nearest deck (headroom, a block underfoot, a roof somewhere above, inside
  the box — never the hull's top) — a breach, the open hangar, a torn panel. The nearer of that deck and a door in
  reach wins; `EnterBoardedStation(…, throughBreach)` sets the boarder on it (`DerelictBreachArrival`: the standable
  cell nearest to it, never in a doorway). Neither in reach: the arrival on the command deck. Or fly back to the ship.
- **The Long Quiet** keeps the pull-back (`DerelictDriftRescueDistance`, 24 beyond the gravity volume): a tiny story
  site with blanket air, never a place to fall out of — and its flight frame (one body's orbit, its own placement) is
  not the generated derelicts'.
- **Every arrival stands.** Docking sets a boarder down on the deck nearest the layout's arrival under the roof — a
  beta wreck's command deck in its closed hub; a stored old derelict's command room or (patched) template hall.

## 6. Mining and the hull seen from space (#2634)

- Inside: no protection (`IsStationBlock` only covers stamped NPC stations). A mined/placed block is written back into
  the build (`WriteBackStationCell` → `MirrorStationCell`, now also for `station:derelict:` worlds) and the design is
  re-sent to the pilots of EVERY orbit instance holding it (`PublishStationCells`). Door changes inside follow
  (`SyncStationDoorRecordsFromWorld`, `RefreshStationBoundsAfterDoorChange`).
- Outside (spacewalk, `HandleStructureEdit`): a generated derelict is nobody's — anyone mines it like salvage (tool
  tier + hardness, like the same block aboard) and patches it (the station place rules); the interior gets the change at
  once (`MirrorStationCellOutside`), other orbits get the block change (`PublishDerelictHullToOtherOrbits`).
- A broken trading post is a plain `station_vendor` block: mining it drops a working `station_vendor` item.

## 7. Persistence — seed + deltas

An untouched derelict stores nothing. The first boarding (the stamp anchor) or the first edit writes
`PersistDerelict(s)`: a `StoredSpaceStructure` row (id `derelict:…`, location = its system id, the cells, `Stamped` +
`StampMin`) plus its door records as structure edits (`PersistStationDoors`). `PersistIfDerelict` is the one switch every
player-station persistence site calls first (first stamp, absorb, publish, EVA mine/place, EVA doors, inside doors).
`LoadPlayerStations` routes `derelict:` rows into `_storedDerelictRows` (never a player station; `/builds` skips them).
The interior world keeps its own block edits like any world. Restart: the row's cells are the hull; `Materialised` keeps
the anchor, so the next boarding tops up instead of re-stamping.

**Which shape (#2674).** The row's cells, the crates, the guardian bay and the interior world of a stored derelict were
all made from one layout, so a restart must rebuild that same layout. `PersistDerelict` of a beta wreck first adds the
`WorldMetadata.GeneratedLoot` key `derelict-shape:beta:<id>` and saves the metadata, then writes the row — a row never
exists without its marker. `UsesLegacyDerelictShape(id)` = a stored row **without** that marker: a derelict entered or
edited before #2674, rebuilt with `GenerateLegacy` (§3.4). Everything else — no row (only seen, scanned or never met),
or a row with the marker — is a beta wreck. No schema change, no save-version bump.

## 8. Hooks for the later phases

- **#2633 crates + logs:** `DerelictLayout.CrateSpots` (3–6 empty deck cells against a wall in walkable rooms, closed
  rooms first) → interior world cells via `GameServer.DerelictCrateSpotsWorld(stationId)` once stamped. Implemented —
  see [§ Old supply crates and logs](#old-supply-crates-and-logs-2633).
- **#2635 guardian spawner:** `DerelictLayout.SpawnerSpot` (the middle of the largest walkable room but the arrival's,
  on its floor) → `GameServer.DerelictSpawnerSpotWorld(stationId)`. Implemented — the `guardian_spawner` block stands
  there; see [§ Guardians](#guardians--the-guardian-bay-2635).
- **#2636 real spacewalk:** implemented — `RescueDriftingBoarder` stays the one caller and queues the spacewalk;
  re-boarding through a breach has its own entry beside the door's. See §5.

## 9. Client

`Client.Core/SpaceTargeting.IsDerelictId` (prefix `derelict:`, pinned to `DerelictPlacer.IdPrefix` by a test). The
travel screen labels the body `ui.map.kind_derelict` with `ui.map.derelict_detail`; the flight chart draws it once as a
named amber marker (`name · Derelict station`, a waypoint target) instead of a live station blip; the radar treats it
like the wreck. The hull renders through the existing `station` design path.

**Looking broken from outside (#2676).** For a `station` design whose id is a GENERATED derelict's (`VoxStruct.Derelict`,
`SpaceTargeting.IsGeneratedDerelictId` — The Long Quiet keeps its own quiet look) the flight view adds, each time it
(re)builds the hull mesh (`ReconcileStructs` → `AddDerelictDecor`):

- **Grime** — the hull's iron plating meshed a little darker and warmer through the mesher's hull-paint stream
  (`ShipMeshBuilder.HullPaint`, the ship-colour path): baked into the vertex data, so there is no material instance and
  no `MaterialPropertyBlock` on the chunk renderers (the SRP batcher keeps batching them). The paint path also gives the
  iron faces the ship hull's sparse greeble panels.
- **The plan** (`Client.Core/DerelictDecorPlan`, Unity-free, tested headless): from the hull cells the client already
  has (no protocol change) — the **beacon** cell (the highest `light_white_off` in the upper half: a beta wreck's dead
  beacon on its snapped mast; else the highest cell within 4.5 blocks of the box's centre column — an old derelict's
  dome or antenna), up to eight **spark** cells (ragged edges: four or more open faces — the torn wing's edge, the mast
  stump, a loose block) and three to six **debris homes** (free cells two or three blocks off a ragged edge, no hull cell
  next to them). Deterministic from the structure id, so every client shows the same picture.
- **The animation** (`SpaceView.DerelictDecor`, a child of the hull root): the debris are small primitive cubes
  (no collider, one shared lit material per flight view) that tumble slowly and drift to and fro around their homes;
  the beacon is a small unlit yellow cube that stays dark for 1–4 s and then stutters on and off a few times (no real
  `Light`); every 1.5–5 s a few weightless spark motes and a pop of glow fly at a random spark cell (the pooled `FxKit`
  emitters). The holder is a child of the hull root, so a hull rebuild (a mined block) clears it with the chunks and it
  is added afresh; a far hull (unloaded mesh) has none. Per frame: a handful of transforms and one toggled cube —
  nothing is meshed or lit; WebGL and weak laptops are not meant to notice it. The voxel hull is never moved or rotated.

## 10. Tests

`DerelictStationTests` (placement guarantee/rarity/Off/idempotence, seed stability + fixed prefix only, an old save
gains them without moving a body or the start station; layout determinism + closed/breached rooms/doors/posts/spots/no
leftovers for the beta wreck and both legacy sources; **the beta silhouette** — hub roof and hall, view band, the collar
on all four sides, a pod at every arm end with its tube, the solar wings on ±X only with exactly one torn at its far end,
the mast snapped with its one dead beacon beside the stump, rust; **the legacy fingerprints** (§3.4); boarding: a beta
wreck, set down on the command deck, nobody (no crew, no trader, no NPC — also after a while)/no trade/hooks/no home
spawn; the dock lesson never targets one; **no room breathes** — not even the closed command room — the tank drains, the
warning (`@srv.derelict.no_air`) comes once per boarding and names the suit tank, VEGA's first-boarding hint stands in
for the plain line, the ship refills the tank, falling out is a spacewalk; **a stored beta wreck stays beta across a
restart, a derelict nobody stored is beta, a stored old one (a row without the marker) keeps its old layout and arrival
and is airless too**; mining persists across a restart + hull follows + the post drops a vendor, EVA mining reaches the
interior, The Long Quiet still breathes and catches a fall, the CLI option),
`DerelictSpacewalkTests` (#2636: falling out opens an EVA at the matching flight spot and the tank drains; a long fall
starts just under the hull; nothing aboard — decks, roof, a jump, the box edge — steps out; E inside a breached room
boards onto that deck, E away from any opening arrives on the command deck; 60 beta seeds have a standable arrival under
the roof in a closed hub; 200 legacy seeds of both sources keep their closed, standable arrival by an independent copy of
the station air rule; a menu travel boards onto the command deck, where the tank drains),
`DerelictCrateTests` / `DerelictGuardianTests` (their layout checks over the beta wreck and both legacy sources),
`Client.Tests/DerelictDecorPlanTests` (#2676: the beacon is the dead lamp, spark cells are ragged edges, debris homes
float free beside the hull; the plan does not depend on the order the design arrived in; the fallback beacon; an empty
hull plans nothing) and `Client.Tests/SpaceTargetingTests.DerelictIds_UseTheServersPrefix`.

## Old supply crates and logs (#2633)

Screelit (Justus), 2026-10-15: "old crates with notes from people and records of disasters that are always random! The
crates can also contain rotten food and medkits." No block, no save-version and no protocol bump: the crate is the
existing `crate` block, the loot is an ordinary structure-loot container, the log is an item key with a seed payload.

**The crates** (`GameServerDerelictCrates.cs`). `OnDerelictBoarded` calls `SpawnDerelictCrates` after the stamp (every
boarding; it does work only once per crate). Each crate spot (`DerelictCrateSpotsWorld`) is marked in
`WorldMetadata.GeneratedLoot` as `derelict-crate:<station>:<n>` (so it fills once ever, also across restarts and after it
was looted away), gets a `crate` block — set in the interior world AND written into the build through
`MirrorStationCell` + `PublishStationCells`, so the row and the hull keep it like a player edit — and a `salvage`
container `loot_oldcrate_<station>_<n>` in that cell (deliberately not `loot_derelict_…`, which `LoreSiteOfContainer`
would read as the story's wreck-manifest lore site). A spot someone built into first stays empty. Contents
(`BuildDerelictCrateLoot`, seed-pure from world seed + station + crate): a medpack (always in crate 0, else 70 %,
sometimes two), a field medkit (10 %), emergency rations (50 %), **spoiled food** (45 %, 1–3), 1–2 picks of scrap metal /
cable / iron plate, and one log (two at 35 %). G takes everything (`LootContainer`); the empty container goes away, the
crate block stays (mining it drops a crate item, like any crate block).

**Spoiled food** (`spoiled_food`, `data/items.json`): a consumable like a mild `toxic_meat` — consumeHealth −6,
consumeHunger 6. Food is data-driven everywhere (`ConsumeHunger > 0`), so it behaves like other food; the herd does not
take it (`HerdRules.IsFoodForCreatures` wants `ConsumeHealth >= 0`), and since #2633 the suit's ration dispenser refuses
harmful food (`LoadRation` → `srv.ration.harmful`) and never auto-feeds one it already holds (an older save's toxic meat).
There is no compost / bin in the game to extend.

**The logs** (`WorldGeneration/DerelictLogs.cs`, fragments in `data/derelict_logs.json`). A log is the item
`derelict_log#x<seed>` (`ItemKey.WithSeed`, max stack 1); its text is a pure function of the seed, written from locale
fragments by the same code on the server and every client — nothing is stored or sent but the key. Seed layout: bits
16–31 the station's story (`SeedFor(worldSeed, stationId, index)`: the incident with its telling count, the escape, the
day, the first job), bits 4–15 the writer (name via `NameGenerator.Person`, which telling, the last words), bits 0–3 the
index — crate n carries indices 2n and 2n+1 (≤ 12 < 16), and job = jobs[(first + index) % jobs] gives every writer of a
station a different job (14 jobs). So one derelict tells ONE story (same disaster, same way out, same day) from up to
twelve people. Parts: `derelict.log.header` ("Day {0} — log entry by {1}"), `derelict.log.job.<job>`,
`derelict.log.incident.<key>.<1..variants>`, `derelict.log.escape.<key>`, `derelict.log.close.<key>`. Tone per
`docs/user/PARENTS.md`: curious, a little sad, never frightening — nobody is hurt on the page and everyone always gets
out (escape pods, a rescue ship, a friendly freighter, the shuttle, the neighbour station). Adding a fragment = a JSON
entry + its en/de keys (the community languages fall back to English); `DerelictCrateTests` checks every key.

**Reading — why an item, not a lore pop-up.** The environmental lore channel (`LoreTextRevealed`, `StoryLogLore`)
carries one fixed locale key per text, belongs to the active story pack (no story = no lore) and is rebuilt from the
pack on every join — a generated text fits none of that, and a new message would need a NetCodec tag and new UI. The
item path needs neither: the inventory detail pane (`CraftingTechShipUI.AddDerelictLogText`) writes the text under the
description with a **Read** button that opens the existing story reader (`StoryReaderUi.OpenOverMenu`); the icon is one
for all logs (`IconResolver.IconKey` files the key under its base). Taking a log out of any container
(`LootContainer` → `RecordFoundDerelictLogs`) also adds `log:<seed>` to the player's Discoveries ledger (named after the
writer, sited at the derelict, toast `srv.derelict.log_found`), and the Codex's Discoveries chapter lists them as
**Station logs** with their full text (`WikiUI.AppendFoundLogs`) — readable again after the note itself was dropped,
sold or stashed. A log already in the ledger (picked up twice, a copy a friend left in a crate) only moves.

Tests: `DerelictCrateTests` (crates fill once with supplies + 1–2 distinct logs, blocks in world and build, looting
records the logs, re-boarding and a restart refill nothing and keep the rest unchanged; loot is seed-pure, spoiled food
sometimes, medpacks mostly; one story per station / a different writer and job per log / stable / rendered in en + de
without placeholders or raw keys; every fragment in en + de; spoiled food edible + harmful + refused by the dispenser).

## Guardians — the guardian bay (#2635)

Screelit (Justus), 2026-10-15: "Nobody lives there anymore, only inside there are sometimes robots or drones that are
spawned every 5 minutes by a kind of spawn block."

**The block.** `guardian_spawner` ("Guardian Bay" / "Wächter-Dock", `data/blocks.json`): a broken maintenance dock of the
Guardian machines — category `machine`, a picture front (`textures/guardian_spawner`, the red-glowing alcove) on the
shared rust casing (`face_rust_side` / `face_rust_top`), facing `toward`, emission 0.6 with a red light (radius 5).
Hardness 4, drill tier 1 (the starter drill opens it). It drops salvage — 2 circuit boards, 3 copper wire, a power cell
— and has **no item**: nobody crafts or places one, so a bay exists only where the generator put it. It is a new block,
so the block ids shifted: save version **4** (`WorldMetadata.CurrentSaveVersion`, the `SaveCompatibilityTests` row).

**Placement** (`DerelictGenerator`, step 13). Every generated derelict gets exactly one, on `DerelictLayout.SpawnerSpot`
(the largest walkable room but the arrival's — on a beta wreck never the open hangar — as far from the arrival as it
allows), its front turned toward the arrival (`CubeFacing.Pack(FaceToward(...))`). The spot is on the room's **floor** (`OnTheDeck`): never on top of a scrap pile or
a post (cells above `reserved` cells are dropped) and only at the room's lowest feet level — the room-centre pick weights
height, so before this a cell one up (on the scrap) sat nearer the centre and the bay stood on a pile with no floor
beside it for its guardians. The Long Quiet has no bay (it is not generated).

**The release** (`GameServerDerelictGuardians.cs`). `TickEnemies` is the one place station worlds are hostile-free; for a
generated derelict's interior world it runs `TickDerelictGuardians` instead — the deliberate, narrow exception:

- **Gate** — exactly the planet machines' `PlanetEnemiesActive` (hostile machines not Off, game mode Survival): none in
  Peaceful, Creative or with the machines off; guardians still aboard when that changes power down at once.
- **Cadence** — the bay's clock runs only while a player is aboard (the world only ticks then): the first guardian
  `GuardianFirstReleaseSeconds` (60) after someone came aboard, then one every `GuardianReleaseIntervalSeconds` (300).
  While `GuardianCapPerBay` (3) of its own guardians are alive the clock holds at zero, so a destroyed guardian is
  replaced a full interval later, never at once. If nothing beside the bay is free (walled in) it tries again 10 s later.
  The clock is runtime state (a restart starts it over); the bay itself is a block of the interior world.
- **What comes** — beside the bay (a ring of 12 cells, the start rotating per release): a walking Guardian robot, every
  other time a scan-drone (`Rules.PlanetDrones`; only drones in the Remnant era — the planet rule), the hunter variant
  when hostile machines are Frequent/Extreme. Same bodies, stats and loot as the planet machines (`NewPlanetMachine`,
  extracted from `SpawnPlanetEnemyNear`); a drone needs four free cells or a robot comes instead. Each release plays the
  `guardian_bay` cue (`SoundFx`) and a red spark `WorldFx` (`guardian_bay`) at the bay.
- **Movement and fights** — the planet machines' own pass (`RunMachinePass`, extracted from `TickEnemies`): hunting by
  sight, roaming, the damage aura, the companion ward. Inside a station a drone hovers 2 above the deck instead of 4
  (`DroneHoverHere`; a room is 4–5 high) and circles a target at 3 ± 1 instead of 7 ± 3. The real-block ground probe
  already treats a breach or an outer wall as a wall, so a robot does not walk out; any guardian that still ends up more
  than one block outside the stamped box (`BeyondStationBox`) is removed — nothing falls for ever. If a guardian kills a
  boarder, the death carries them to the ship's world and the pass stops there (the active world changed under it).
- **Killing** — the ordinary attack path (`AttackEntity` on the active world's machine list): every weapon works, the
  usual 2 carbon drop, it counts as a machine defeat (story, missions, achievements).
- **VEGA** — `vega.derelict.guardian` once per player and derelict, the first time that player is aboard while one of
  its guardians is about (milestone `derelict:guardian:<id>`, kind 2); `vega.derelict.bay_down` to everyone aboard when
  a bay that has released goes silent (mined).
- **The end of the bay** — mining it stops the releases for good: the bay is a block of the interior world, so its
  absence persists with the world's block edits and the derelict's build row (`WriteBackStationCell`), across restarts.

**Client.** Nothing gates the machines by world: `WorldEntities` draws `GameBootstrap.PlanetEnemies` wherever the server
sends them, so the robots and drones render and animate aboard a derelict like on a planet (targeting, health bars and
hit markers read the same list). New: the `guardian_bay` case in `CreatureView.PlayWorldFx`; the cue clip
`Resources/audio/guardian_bay.mp3` plays through the existing `SoundFx` path. A newly arrived boarder gets the list on the
next tick (`TickDerelictGuardians` marks it dirty when the boarder count grows).

Tests: `DerelictGuardianTests` (one bay per generated layout, on the floor, facing, with floor beside it; mined with the
starter drill, salvage drops, no item; the cadence — first at 60 s, then never sooner than 300 s apart, never more than
three, always inside the hull, robots and drones, VEGA's warning, no release at the cap; none in Peaceful or Creative;
mining stops it, also after a restart; a guardian dies to a laser pistol with its carbon and the next one waits a full
interval; a guardian outside the hull is gone) and the save-version row in `SaveCompatibilityTests`.
