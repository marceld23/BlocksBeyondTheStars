# Base life support — the core zone and sealed rooms

**Status:** implemented (#782 core zone, #794 sealed rooms, #793 energy door; size, reach, allies, causes and cost
#2539–#2543, 2026-10-14). Code: `src/BlocksBeyondTheStars.GameServer/GameServerBaseAir.cs`. Player-facing rules:
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
| `Open` | the fill reached the open sky (see *Cost*) — the ordinary "outside" |
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
  connected) is told; the same cause is not repeated to the same player within 5 minutes. A plain leak stays silent —
  that is simply "outside".
- **`/basewalls`** (owners may) adds the air at the caller's cell with its reason, and a line with the air rules and the
  base's current reach.
- The core's readout (`ui.base.air_none`) and VEGA's `base_airless` hint quote the size limit.

## Tests

`BaseSealedRoomTests` (the model: seal, hole, dirt, mechanical doors, readout, warnings) and `BaseAirReachTests`
(#2539–#2544: a 16 800-cell room breathes, a hall grown past 64 000 is told *too large*, a player-built corridor past 48
breathes while a natural one reads *too far*, allied bases pool their air, a core in the open stops at the sky, an idle
base does not refill, a sealed room without a door says *not connected*, the texts quote the real budget).
