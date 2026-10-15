# Base life support — the core zone and sealed rooms

**Status:** implemented (#782 core zone, #794 sealed rooms, #793 energy door; size, reach, allies, causes and cost
#2539–#2543, 2026-10-14; the leak told on walk-in and located #2614/#2615, 2026-10-15). Code:
`src/BlocksBeyondTheStars.GameServer/GameServerBaseAir.cs`. Player-facing rules:
[USER_MANUAL.md § Bases](../user/USER_MANUAL.md).

A founded base (the *Grundstein*, block `base_core`) keeps players breathing on worlds without breathable air in two
ways:

1. **The core zone** — the 17³ cube around the core (`WorldConstants.BaseZoneRadius` = 8), unconditional. It doubles as
   the build-protection zone (`InAnyBaseZone`).
2. **Sealed rooms** — every room beyond the cube that is *sealed* and *connected* to the base (`InSealedBaseRoom`).

The server is the truth: the oxygen tick asks both predicates per player; the client only shows the result
(`LifeSupportSource` 3) and the core's readout (`NetBase.AirCells` / `SealedRooms`).

## The pocket model

A **pocket** is a maximal 6-connected region of non-airtight cells: air, flora, loose dirt and sand, shaped cells
(ramps, stairs, spheres) — leaks flow right through those. It is bounded by

- **airtight full cubes** (`BlockDefinition.Airtight`: terrain/building/ore/machine/light categories minus the loose
  list in `GameContent.AirtightExceptions`, plus explicit opt-ins such as the force field), and
- **energy-door cells** (the door's width × its 3-tall column). The energy door is an air curtain (#793): it bounds a
  pocket AND links it to the pocket on its other side, whether its panels are open or not. Mechanical doors are no
  boundary at all.

A pocket is **sealed** when its fill terminates. When it does not, the reason is kept (`BaseAirLeak`, #2542):

| Cause | When |
|---|---|
| `TooLarge` | the fill exceeds `WorldConstants.BaseSealedRoomMaxCells` (64 000, #2539; was 16 000) |
| `TooFar` | an open cell lies outside every partner's reach box (see below) |
| `Open` | the fill reached the open sky (see *Cost*) — the ordinary "outside"; the pocket keeps where (`LeakCell`/`LeakBlock`, #2614) |
| `Disconnected` | sealed, but no partner cube and no energy door to a supplied pocket |

A sealed pocket is **supplied** when it touches a partner core's 17³ cube, or shares an energy door with a supplied
pocket (a fixpoint over the door graph — room chains extend the base door by door, without a length limit).

## Partners — allies share their air (#2541)

The fill runs per **partner group**, not per base: a base's partners are itself and every base on the same body whose
owner is the same player or allied with them (`AreAllied` — pairwise handshake or shared crew). Any partner's cube
supplies, the leak box is the **union** of the partners' reach boxes, and every partner's energy doors seed the fill.
Groups are keyed by the partners' ids, so two allied bases share one computed volume. Visitors still breathe in anyone's
base — ownership never gates breathing, only the pooling.

## Reach — the box grows with the build (#2540)

A base's box is the wall ring's (`BaseWallReach`, #1862): the outermost **player-edited** cell around the core (any
player's builds, digs or dyes, from the block-edit store, grown live on `ServerWorld.PlayerBlockSet`) plus 6, at least
48 (`SealedRoomMaxReach`) and at most 192 (or a quarter lap on a small body). A cave the player walled in or dug out is
in reach without anything to learn; a natural space that runs past the box reads `TooFar`. A wall standing just outside
the box still bounds a pocket — only an *open* cell outside it leaks.

## Cost (#2543)

A sealed 60 000-cell hall costs tens of milliseconds to fill, and before #2543 a leaky pocket (a core in the open, the
outside of every entrance door) ran to the full budget on **every** 1.5 s recompute. Two guards keep it cheap:

- **Sky exit** (`AirOpenToSky`): a cell with nothing but open cells above it up past the top of every reach box is
  exactly the column the fill would climb to leave the box, so its pocket can never seal — the fill stops at once.
  Probed for every seed, every power-of-two-th dequeued cell and every 512th after that. A core standing in the open
  now visits a handful of cells.
- **Dirty recompute**: a group is refilled only when a block was set inside its boxes (`MarkBaseAirDirty` on
  `ServerWorld.BlockSet` — every mutation path, fluids and fire included), an energy door was built, removed or the
  door registry rebuilt (`_baseAirDoorsVersion`), or a partner's reach grew; at most every 1.5 s
  (`SealedRoomRecomputeInterval`), with an 8 s backstop (`BaseAirBackstopInterval`) for what no block set reports —
  chunks loading, alliances forming or breaking. An idle base costs nothing. When the active world changes, every group
  starts over (no comparison against the last visit's cells while chunks are still loading).

Blocks are read with `GetBlockIfLoaded`, so an idle base never drags chunk generation; an unloaded cell reads as air
and at worst leaks its pocket.

## Telling the player why (#2542)

The four reasons used to read the same ("not sealed"), so a closed hall over the budget sent its builder looking for a
hole (the 2026-10-14 report; the same trap as the station's #1559 and the wall ring's #1728). Now:

- **A room drops out**: when cells that held air no longer do, the cause of the pocket they fell into
  (`LostAirCause` — too large / too far win over a plain leak) is told to every player within the group's boxes:
  `@srv.base.air_too_large:<budget>`, `@srv.base.air_too_far:<reach>`, `@srv.base.air_disconnected`,
  `@srv.base.air_room_leak` — or the old `@base_air_lost` when the whole volume went through a leak. Only on worlds
  without breathable air and with oxygen on.
- **Walking out of base air** (`TrackBaseAirPresence`, from the oxygen tick): the pocket at the player's feet is
  diagnosed once (`DiagnoseBaseAir`, at most every 3 s per player) and an informative cause (too large, too far, not
  connected) is told; the same cause is not repeated to the same player within 5 minutes (tracked per cause since
  #2614, so two causes met alternately are not retold on every alternation). A **leak** is told too when the spot is
  clearly a room (#2614): the pocket touches an energy door, or something a body would bump into stands above the
  player (`RoofAbove` — a sand ceiling counts, a tree crown does not). A leak under the open sky stays silent — that is
  simply "outside". The leak line names where the air gets out (below) and is resolved on the server in the player's
  locale (`srv.base.air_leak_at` / `_open_at`), because the block name and the height phrase need localizing too and
  one `@srv.*:arg` token carries a single argument.
- **`/basewalls`** (owners may) adds the air at the caller's cell with its reason — for a leak with the block, cell and
  height (`srv.basewalls.air_leak_at` / `_open_at`) — and a line with the air rules and the base's current reach. Its
  wall-ring line is labelled "for animals" (#2615): "walled in" is the spawn verdict, not air, and it used to stand right
  above "Air at your cell: none — leaky" like a contradiction.
- The core's readout (`ui.base.air_none`) and VEGA's `base_airless` hint quote the size limit; when other rooms breathe
  but the spot you stand on does not, the readout adds "· /basewalls tells you why" (`ui.base.air_here_why`, #2615).
  The leak texts and the VEGA hint name sand, snow, dirt and geyser vents as never sealing.

### Locating a leak (#2614)

Lyxette's cave hall stayed without air because its ceiling was sand under the open sky (sand never seals — it is
granular, and making it airtight would hand out free sealing material and break the rule bases, stations and ships
share). "Leaky" alone did not tell him that. The diagnosis fill (`FillAirPocket(..., locate: true)`, on demand only)
therefore probes **every** dequeued cell's column instead of the sparse cadence of the group fills: it runs outward
from the player, so the first open column it meets is the leak nearest to them — the cell under a sand patch, the
column under a hole. A per-column memo (`ProbeSkyOnce`: a stretch known to end at a seal) keeps it to about one scan
per column. The open column's scan (`ColumnOpenToSky`) records the **leak cell**: the first block on the way up — one
that does not seal, or the scan would have stopped — or, when the whole way is air, the last cell inside the reach box
(a hole: its column is named, at the top of the box). The group fills keep their sparse probe and their cost; they record
the same fields but nothing reads them. A shaped cell of a sealing material is named as "shaped" (only full blocks
seal). The cell is printed in the player's frame across the wrap seam, with its height above or below the player.

## Tests

`BaseSealedRoomTests` (the model: seal, hole, dirt, mechanical doors, readout, warnings) and `BaseAirReachTests`
(#2539–#2544: a 16 800-cell room breathes, a hall grown past 64 000 is told *too large*, a player-built corridor past 48
breathes while a natural one reads *too far*, allied bases pool their air, a core in the open stops at the sky, an idle
base does not refill, a sealed room without a door says *not connected*, the texts quote the real budget) and
`BaseAirLeakTests` (#2614/#2615: a sand or snow roof under the open sky is told on walk-in with the block and its cell,
a stone layer under the sand breathes and says nothing, the leak is told once per five minutes, a sand patch in an ice
roof is named rather than the ice, a hole is named by its column, walking out into the open stays silent, `/basewalls`
names the leak and labels the wall verdict "for animals", the texts carry their placeholders).
