# Base and station life support — the core zone, rooms and the shared air engine

**Status:** implemented (#782 core zone, #794 sealed rooms, #793 energy door, #2539–#2543 reach/allies/cost, #2614/#2615
leak location; **one rule for bases and stations on a shared engine since #2649**: #2650 engine, #2651 base air, #2652
station air, #2653 bulkheads, #2654 warnings — 2026-10-16). Code: `src/BlocksBeyondTheStars.GameServer/AirEngine.cs`
(the engine), `GameServerAirSpace.cs` (how it reads a world, the per-tick budget), `GameServerBaseAir.cs` (bases),
`GameServerStationAir.cs` (player stations, derelicts). Player-facing rules: [USER_MANUAL.md § Bases](../user/USER_MANUAL.md).

A founded base (the *Grundstein*, block `base_core`) keeps players breathing on worlds without breathable air in two
ways:

1. **The core zone** — the 17³ cube around the core (`WorldConstants.BaseZoneRadius` = 8), unconditional: the start zone
   before anything is built. It doubles as the build-protection zone (`InAnyBaseZone`).
2. **Rooms** — every enclosed room the base owns (`InSealedBaseRoom`), however big and however many.

A player-built station (`pstation:`) and a generated derelict (#2632) breathe only inside their sealed rooms
(`StationLifeSupport` / `InSealedStationPocket`); NPC and template stations keep the blanket air. The server is the
truth: the oxygen tick asks these predicates per player; the client only shows the result (`LifeSupportSource` 2/3) and
the core's readout (`NetBase.AirCells` = supplied cells, `NetBase.SealedRooms` = supplied rooms).

## The rule (decided 2026-10-16, #2649)

- **Walls** are airtight full cubes (`BlockDefinition.Airtight` + `ShapeCode.IsCube`: terrain/building/ore/machine/light
  categories minus the loose list in `GameContent.AirtightExceptions`, plus opt-ins such as the force field) and
  **every door cell of every kind, open or closed** — on a base every registered door (its width × the 3-tall doorway)
  and the door blocks, on a station the old `IsStationAirtightCell` set (door blocks, a door built into an air cell, the
  station core). Fluids follow their block's flag (water and lava are terrain, so they seal) — unchanged per place.
- **Every enclosed room breathes** — no cell budget, no reach box around the fill, no "connected to the cube or an energy
  door chain" rule. The old 64 000-cell budget (`BaseSealedRoomMaxCells`) and the station's `TooLarge` are gone.
- **Outside**: on a base, an open column that reaches the base's **sky line** — `max(core.Y + reach, highest player-built
  Y + 4)` over the partners (`BaseAirSkyY`). The highest build is read from the block-edit store once per base over the
  horizontal reach cap at any height and grown live on `ServerWorld.PlayerBlockSet` (`GrowBaseAirTop`), so a tower taller
  than the reach cap still works. On a station, anything beyond the build box plus `StationAirMargin` (8) — the void.
- **The base core stays the air machine.** A base's rooms are the pockets seeded from the core's six neighbours and from
  **both sides of every door within the base's reach box** (`WithinBaseWallReach`), then chained through the doors of
  **every sealed pocket found**, at any distance. A leaky pocket never continues the chain, so the search never spreads
  over a planet. A room no base finds (no door in reach, not touching the core, not behind a sealed room's door) never
  breathes. Allied bases (#2541) pool: the group's seeds are the union of the partners' cores and door boxes.
- **Doors are bulkheads** (#2653): every door separates two pockets, and every door in reach is its own seed — so in
  *base → A (leak) → B* the room B is still found. A leak empties only its own pocket.
- **Unloaded chunks are unknown, not a leak**: they bound a fill like walls. Two refinements keep this honest:
  - *The unbuilt sky.* The server streams only two chunks above a player, so the sky over a roof hole is often not
    loaded. An open column climbing into an unloaded chunk counts as the sky when no player edit lies at or above that
    height in its chunk column (`AirColumnTop`, read once per chunk column from the edit store, grown live). Under a
    tall hall's ceiling — a player edit above — it stays unknown.
  - *Rooms hidden by unloaded chunks keep their last state.* When a fill met an unknown seed or a sealed pocket that
    touches unknown cells (`AirFill.SawUnknown`), the sealed rooms of the previous result it did not reach are carried
    over unchanged under their old room ids (`AirFill.AdoptSealedFrom`) — a player far out along a door chain whose
    core's chunks unloaded keeps the air in the rooms around them.

## The engine (#2650)

`AirFill` fills **vertical spans** — maximal runs of open cells in one column — instead of single cells. Spans are kept
per column (`Dictionary<columnKey, head>` + a linked span array: x, z, lo, hi, pocket, next ≈ 24 bytes each); "is
there air here?" is a column lookup plus a short list walk. A span is built upward first (a column open to the sky decides
the pocket without the way down), and its four neighbour columns are scanned over its height — a known span is jumped
over, a new open run becomes a span. A 100 × 100 × 100 hall is 10 000 spans; measured headless
(`AirEngineTests.AMillionCellHall_…`): 1 000 000 cells, 1.09 M cell reads, **29 steps of the 2 ms budget (57.6 ms in
all, worst step 2.1 ms), 1.7 MB allocated**. The world reader (`PlanetAirSpace` / `StationAirSpace`) reads a resident
chunk's block array directly (`ServerWorld.LoadedChunkOrNull`) and canonicalizes a column once, so a column scan is an
array read per cell; door cells are a hash lookup only in chunks that hold a door.

- **Time slicing.** `AirFill.Step(budget)` works until the budget is spent and returns; every fill on the server shares
  one budget per tick (`AirTickBudgetMs` = 2 ms, renewed when the uptime moves). Queries may work on a pending fill only
  while budget is left; the tick driver `TickAirFills` moves every pending fill of the active world by at least 64 spans
  per tick, so a fill always finishes. **Until it does, the previous result keeps answering** — nobody loses air
  mid-fill. A group's very first fill (no previous result) runs to the end at once, like the old synchronous fill did.
- **Emergency brake.** A run past `WorldConstants.AirFillBrakeCells` (2 000 000) or `AirFillBrakeSpans` (200 000) stops,
  keeps the previous result, logs once (`_log.Warn`) and never takes air away; a braked group then waits for a real
  change instead of refilling on the backstop. The on-demand diagnosis fills are held to 400 000 cells / 40 000 spans.
- **State per pocket.** Each pocket records sealed/leaky, its doors, whether it touches unknown cells, and for a leak its
  **opening** (below). Room ids (`AirPocket.Id`) are unique per process and stable within one result
  (`AirRoomIdAt`, used by the bed assignment to prefer a bed in the same room as a work post).

## Recomputing (dirty triggers)

A base group is refilled when a block was set inside its area (`MarkBaseAirDirty` on `ServerWorld.BlockSet` — the reach
boxes or the box of its last and pending fills, plus one), a door was built, removed or the registry rebuilt
(`_baseAirDoorsVersion`), a partner's reach or build top moved (`AirSignature`), or the 8 s backstop elapsed
(`BaseAirBackstopInterval`, for chunk loads and alliances) — at most every 1.5 s (`SealedRoomRecomputeInterval`). An idle
base nobody asks about costs nothing; group state is kept per body (the old "clear everything when the active world
changes" would restart every time-sliced fill on a server ticking two planets) and dropped after 120 s without a query.

A station's air is filled per room on demand: the first query about a room fills it at once (a boarder's arrival, a crew
spot or a path step must be answered correctly), and the known sealed rooms are refilled time-sliced — seeded from every
span of the last result — after a block set in the station world, a door change or the backstop. The places that rewrite
a hull (`TryFindStandableInStation`, the spacewalk mirrors) drop the station's state so it starts over.

## Telling the player (#2654)

- **The opening.** A leaky pocket names where the room meets the outside, at the opening's height: when the outside
  column runs on below the room's floor it is a column outside the room, and the opening is the room's cell beside it — the
  hole in the wall (a doorway at the floor the player stands on); when it rises out of the room it pierces the ceiling,
  and the opening is the first block on the way up that does not seal (a sand roof) or the first air cell with walls on two
  sides (the hole in the roof). The text is resolved on the server in the player's language (`srv.base.air_leak_at` /
  `_open_at`, with the block name, the cell in the player's frame across the seam and the height phrase).
- **A room drops out** (`WarnBaseAirChange`): only the players **in or at the damaged room** — their feet, head or a face
  neighbour held air before and does not now — hear the located leak line (or `@srv.base.air_room_leak` when no opening
  is known). Only when **every** room of the group is gone does everyone in the base's area hear `@base_air_lost` ("air
  only reaches the core zone"). A room that merely lost its seed (its door went) is closed, not leaking — nothing is told.
- **Walking out of base air** (`TrackBaseAirPresence`): the pocket at the player's feet is diagnosed once (at most every
  3 s); a leak is told, once per 5 minutes, when the spot is clearly a room (the pocket touches a door, or something stands
  above — `RoofAbove`). A leak under the open sky stays silent. `DiagnoseBaseAir` knows core zone / supplied / leak / unknown
  (a closed room no door joins to the base, or a diagnosis that braked).
- **Stations**: the hull warning goes once per breach to the player standing in the breached room and names its opening
  (the base's leak text); without a pocket (standing inside a wall cell) the general `@station_air_lost` stands in. Only
  the posts in the breached room lose their keeper (#1487).
- **`/basewalls`** (owners may) shows the air at the caller's cell — the zone, supplied (cells, rooms), the leak with its
  opening, or `srv.basewalls.air_unknown` (closed, but no door leads into it from the base) — and the air rules
  (`srv.basewalls.air_rules`: every door holds air and is a bulkhead, no size limit, how a room joins the base, the reach).
  The too large / too far / not connected lines and keys are gone.

## Tests

`AirEngineTests` (headless, an array world: a sealed box, the roof hole and side hole and ground-level doorway named at
their opening, a sand roof, doors separating rooms and chaining, a leaky room never chaining, unknown cells bounding and
the unbuilt sky over a roof hole, slicing, the brake, carrying rooms over, a leak into a huge cave ending at the sky, the
million-cell measurement). `BaseAirReachTests` (which rooms breathe: past the old 16 000 and 64 000 budgets, past 48
blocks built or natural, up the stairs behind an ordinary door, a shed entered from outside, no core → no air, a door chain
past 192 blocks, a shaft above the reach cap, allies pooling, a core in the open stopping at the sky, an idle base not
refilling, a doorless room staying without air with `/basewalls` saying why, the texts). `BaseAirBulkheadTests` (a leak
behind a door empties only that room, the chain survives a leaky first room, an open energy door is still a bulkhead, the
warning reaches only the damaged room and `@base_air_lost` only when all fail, a fill in progress keeps the old result,
an unloaded wing keeps its air, the brake keeps air). `BaseSealedRoomTests` (seal, hole, dirt, every door kind seals, an
open doorway leaks, readout, core mined, total loss warns). `BaseAirLeakTests` (#2614/#2615 walk-in leak line, the roof
hole and a side hole named at the opening). `PlayerStationReportsTests` (station air: a hole leaks and is named, the
force-field plug, a hall past 64 000 cells breathes, a breach behind a door empties only that room and sends only its
post's keeper away). `TorchAirTests` (a flame burns in base air).
