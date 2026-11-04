# Moving blocks — phase blocks, trapdoors, bridges, pistons and lifts

Crystal Net 2, parts #2264 (twins), #2265 (bridge motor, piston) and #2266 (lift), 2026-10-04. The decision is
recorded in [ADR 0014](adr/0014-moving-blocks-twins-and-server-owned-lifts.md); the rest of the Crystal Net is
described in [CRYSTAL_NET.md](CRYSTAL_NET.md).

## 1. The rules every moving block keeps

- **The server moves blocks; nothing is an entity.** Every moving block is a block write (`CrystalWriteCell`: the
  voxel, a `BlockChanged`, the station write-back) — except the lift platform, which is the one thing that moves
  smoothly and therefore is drawn by the client from a server-owned height (§5). No physics, no carried entities.
- **Moved, not mined (#3173).** Every such write carries `BlockChanged.Moved = true` — `CrystalWriteCell` sets it for
  all of the net's writes (bridge deck out and in, piston head, twin and lamp swaps), the piston sets it on the blocks
  it pushes or pulls, and the weather sets it when a snow drift melts or is cleared from a lift shaft. The client
  passes it on through `GameBootstrap.BlockChangeApplied`: `MiningFx` then plays no crumble (dust, flash, tumbling
  debris) and no place pop, and `ClientAudio` no mining crunch or place knock — the machine brings its own sound and
  effect (`bridge_motor` + sparks, `piston_push` / `piston_retract`, a twin's sound). Decals and room reflections react
  as to any edit. A block a player mines by hand — a bridge deck too — is not moved and still crumbles. The field is
  additive (MessagePack map key / JSON property): an older client ignores it, an older server never sets it.
- **Kid rule: a moving block never hurts.** A block never closes onto a player, an NPC or an animal
  (`CellOccupiedByBody` — the block waits and tries again on the next beat). A floor that opens grants the players
  standing on or just above it a fall grace (`GrantMovingFallGrace`, `CrystalNetRules.MovingFallGraceSeconds` = 3 s,
  `PlayerSession.MovingFallGraceUntil` read by `HandleFallDamage`); a lift rider gets the same grace every tick of the
  ride.
- **Bounded cost.** Twins are level listeners with the 0.5 s actuator limit; a bridge places or removes one deck block
  per 0.25 s; pistons share a world budget of 4 pushes per tick and push at most every 0.5 s; a lift moves its height,
  not blocks. Per-owner caps: 8 bridge motors, 8 pistons, 4 lifts.
- **Never someone else's things.** Bridges and pistons never touch ships, settlements, stations, the Guardian core,
  factories or another player's base (`BridgeCellProtected`, `PistonCellProtected`), and pistons move only ordinary
  mineable blocks (§4).
- **State in the row.** What a moving block has done persists in its device row: a bridge's `ext=`, a piston's `out=`,
  a lift's `at=` (the level it waited at). A reload puts everything back where it was.

## 2. Twinned blocks (#2264)

A twin is a pair of block keys the net swaps between, like a lamp and its `_off` twin (#2048):

| Kind | Plain (OFF) | Twin (ON) | Notes |
|---|---|---|---|
| PhaseBlock | `phase_block` | `phase_block_open` | a wall block → shimmering, walk-through air; tintable; side by side they open together (one network) |
| Trapdoor | `trapdoor` (closed panel at the top of its cell) | `trapdoor_open` (the panel folded against its hinge side) | the form is rewritten with the swap (`PropShapes.TrapdoorClosed` / `TrapdoorOpen`) |
| ForceField | `force_field` | `force_field_off` | ON = open (#2261); a wired field wall switches as one |
| Campfire / Forge | `campfire_off` / `forge_off` | `campfire` / `forge` | ON = burning (#2261) |

`CrystalNetRules.TwinKeyFor(kind, on)` names the block a level asks for and `PlainKeyFor(kind)` the one a block
returns to when it leaves the net (`RestoreCrystalTwin`). `SwapCrystalTwin` keeps dye, glow and form, refuses to
close onto a body, grants the fall grace when a solid block opens, plays its sound (`phase_shimmer`,
`trapdoor_open` / `_close`, `force_field_on` / `_off`) and effect (`phase_shimmer`, `field_flicker`). A twin is **not
assumed** in either state on load (#2096): its first beat reads the block that actually stands there. The open
twins are see-through / walk-through on the client (`ChunkMesher`: transparent + not collidable — the mesher reads
keys, not the `Solid` flag). A phase block opened by a world circuit (a crystal vault's door) counts as
"Safecracker" for everyone in the chamber.

## 3. The bridge motor (#2265)

A directional device (`OutputFace(yaw)`, six directions). Held ON, `BridgeMotorBeat` (run from the machine beat)
places one `bridge_deck` block per `BridgeStepSeconds` (0.25 s) the way its arrow points, until `len=` (menu, 2–12,
default 6) or an obstacle — a non-air cell, a protected cell or a body; then its amber light is ON and it tries
again after the actuator interval. Fully out → amber ON as well. OFF pulls the deck back in, last block first
(`BridgeRetractOne`, with a fall grace, support and fluid updates). Mined or turned, the whole deck goes at once
(`BridgeRetractAll`). The deck drops nothing (it is the motor's). `ext=` persists how far it is out.

## 4. The piston (#2265)

A directional device. On a rising level (`PistonPush`) it pushes the line in front of it one cell forward — up to
`PistonMaxPush` (4) blocks, from the far end back, every block keeping its dye, glow and form — and fills the freed
cell with a `piston_head`. It refuses (amber light, retried after the interval) when the line holds something
immovable, is longer than four, ends in a protected cell, a door or a body, or the world's budget for this tick is
spent. Movable (`PistonMayMove`): an ordinary mineable block the owner may edit — never a container, a Crystal Net
cell, a door, a fluid, a plant, a bed, a multi-cell form, bedrock, another head or a lift platform. On OFF
(`PistonRetract`) the head goes; in **sticky** mode (mode 1) it pulls the block in front of its head back into the
head's cell. `out=` persists the head.

## 5. The lift (#2266)

**Building.** A `lift_motor` at the bottom; the shaft is the 3×3 column above it (`LiftPlatformRadius` 1, at most
`LiftMaxHeight` 48 cells). The platform rests on the motor. A `lift_stop` beside the shaft (within `LiftStopReach`
of the platform's edge) is a floor: the platform's top comes level with the stop's own cell. `LiftLevels` lists the
floors, bottom first. A stop on the bottom floor belongs to the lift too (`StopBelongsTo`, #3161 — it used to be
excluded, so a press on it did nothing and the platform could not be called back down).

**Signals.** A press or a rising edge on a stop calls the platform there (`LiftGoTo`); on the motor it sends the
platform to the next stop up, from the top back down to the bottom. A stop's status light is ON while the platform
waits at it. A **press** that cannot be answered says why (#3163, `ServerMessage`): `srv.lift.stop_no_shaft` (the stop
belongs to no motor), `srv.lift.no_stops` (a motor alone at the bottom), `srv.lift.blocked:<height>` /
`srv.lift.blocked_bottom` (the first blocked layer in the ride's direction, counted from the bottom floor). A circuit's
signal stays silent.

**Snow.** The platform is no block, so the weather snow pass would see an open shaft and lay its drift under the
platform — solid snow then blocked `LiftLayerClear` (Lyxette, 2026-11-03). `TryFindSnowSurface` now skips every lift
shaft (`InLiftShaft`: the 3×3 column above a motor, up to `LiftMaxHeight`), and `LiftGoTo` clears tracked weather
drifts in the ride's layers first (`ClearWeatherSnowInShaft`; a player's own snow still blocks) (#3162).

**Moving.** The server owns `ServerLift.PlatformY` and moves it at `LiftSpeed` (3 cells per second) in `TickLifts`
every tick (not on the beat). It starts only when the shaft between here and there is clear (`LiftLayerClear`), and
**never moves down onto a body** (`LiftLayerOccupied` — it waits). `LiftList` (`NetLift`: motor cell, `PlatformY`,
`TargetY`, `Speed`, `Moving`) goes out on every start and stop and every 0.2 s while one moves; a joining player gets
it with the rest of the net. The motor loops `lift_motor` while it moves; `lift_arrive` plays on arrival. `at=` in
the motor's row persists where it waited. A mined motor removes its lift.

**Riding.** The client (`LiftView`) meshes one 3×3 `lift_platform` slab with the block atlas, places it with a
collider at the eased height (`PlatformY` + `Speed` × time since the last list, clamped to `TargetY`) and moves the
local player standing on it by the platform's height change (`PlayerController.RideBy`). The server grants riders the
fall grace and counts the ride for "Going Up".

**Choosing the floor (#3171).** The platform is an entity, not a block, and the aim ray walks voxels only — a rider had
nothing to press (at the bottom the ray happened to hit the motor under the platform, at the top the motor lies beyond
reach), and "send on" existed only on the motor. Now `LiftList` carries the floors additively (`NetLift.Levels`,
`LevelNames` — the stops' labels, `LevelHasStop`), re-read every 0.5 s per lift (`RefreshLiftFloors`) and sent when they
change. The client (`LiftRiding` in Client.Core) knows when the player stands on a platform (`StandingOn`, the riders'
box; the interact pick `InteractKind.LiftRide`, first of the proximity tier), hides the cells under the platform from the
aim (`HidesCell` — no more motor menu from the platform) and which stop the platform waits at (`WaitingAtStop`). E there
opens `LiftFloorUi` (floors top to bottom, the current one lit; *Go*, *Name*). Device action **5** (at the motor, mode =
the floor's platform height) sends it there, action **6** names a floor (every stop on it the player may configure; an
empty name clears it). The server checks the floor against `LiftLevels`, the motor's operate (5) / configure (6) rights,
and its own reach (`CanReachLift`): on the platform (`OnLiftPlatform`, the grace box) or within reach of the motor or any
stop of that lift (`srv.lift.too_far` otherwise). A press at a stop where the platform already waits says
`srv.lift.already_here` instead of nothing; a circuit's signal there stays a silent call.

**Gates (#3174, `GameServerLiftGates.cs`).** `lift_gate` (solid) / `lift_gate_open` (walk-through: `solid: false`, an
`openTwin` in the mesher, a `WalkThroughBlocks` prop) are a plain block pair, no net device, drawn as cutout cubes like the
energy fence (`IsCutoutCubeKey`, `TextureTiles` cutout; a closed gate keeps its collider toward an open one). A gate in
the stop ring (`LiftPlatformRadius + LiftStopReach` from the motor's column) between `motor.Y + 2` and `LiftMaxHeight`
belongs to the floor one below it — a stack of gates to the floor of its lowest one. `SyncLiftGates` opens the gates of
the floor where the platform rests and closes all others (all of them while it moves, all of them when the motor is
mined); it runs from `UpdateLiftStatuses` (departure, arrival), from "already here", and every 0.5 s per lift
(placement, and a save whose chunk deltas disagree with the motor's `at=`). Chunks that are not loaded are skipped until
they are. A gate never closes onto a body (`CellOccupiedByBody`): it joins `ServerLift.GatesWaiting` and the next tick
with a free cell syncs again. Cost: 16 columns × the lift's height of chunk-cache reads per lift every 0.5 s.

## 6. Not aboard ships

Bridge motors, pistons and lifts edit the hull they stand in, and a ship's hull is meshed in three places (the parked
object, the flight view, the interior) — so aboard the own ship they stay decoration (`CrystalNetRules.WorksAboard`).
Phase blocks and trapdoors do work aboard: their swap writes the ship's structure (live, re-applied after every
rebuild — see [CRYSTAL_NET.md](CRYSTAL_NET.md) §17.7).

## 7. Tests

`CrystalNet2Tests`: a phase block opens and keeps its dye, waits while someone stands in it; a trapdoor opens with
its form and grants the fall grace; a bridge extends to its length, stops at an obstacle and retracts; a piston
pushes and pulls (sticky) and keeps the dye, refuses to push a crate; a lift calls its platform to a stop and back to
the motor. `LiftShaftTests` (#3161–#3163, #3171): the bottom stop, snow in the shaft, the "why" messages; the floor
picker from the top down and from the middle up, refused out of reach / on someone else's lift / for a floor that is
none, the floors and names on the wire, naming a floor. `LiftGateTests` (#3174): a gate opens on arrival and closes on
departure, waits for a body, belongs to its floor (a stack together), leaves gates outside the ring alone, is put right
after a reload and closes with a mined motor. `CrystalDeviceNameTests` (#3172): the name action sets and clears a
device's label, the configure click keeps it, it survives a reload. `LiftRidingTests` (Client.Tests): the rider's box
across the seam, the hidden motor, a stop's lift and the picker's floors. `MovedBlockChangeTests` (#3173): a bridge's deck out and in, a piston's push and sticky pull, a thawing drift
and a drift cleared from a lift shaft broadcast `Moved = true`, a hand-mined block (a bridge deck too) `false`, and the
flag survives both wire paths.
