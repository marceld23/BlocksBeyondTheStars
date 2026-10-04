# Moving blocks — phase blocks, trapdoors, bridges, pistons and lifts

Crystal Net 2, parts #2264 (twins), #2265 (bridge motor, piston) and #2266 (lift), 2026-10-04. The decision is
recorded in [ADR 0014](adr/0014-moving-blocks-twins-and-server-owned-lifts.md); the rest of the Crystal Net is
described in [CRYSTAL_NET.md](CRYSTAL_NET.md).

## 1. The rules every moving block keeps

- **The server moves blocks; nothing is an entity.** Every moving block is a block write (`CrystalWriteCell`: the
  voxel, a `BlockChanged`, the station write-back) — except the lift platform, which is the one thing that moves
  smoothly and therefore is drawn by the client from a server-owned height (§5). No physics, no carried entities.
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
floors, bottom first.

**Signals.** A press or a rising edge on a stop calls the platform there (`LiftGoTo`); on the motor it sends the
platform to the next stop up, from the top back down to the bottom. A stop's status light is ON while the platform
waits at it.

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

## 6. Not aboard ships

Bridge motors, pistons and lifts edit the hull they stand in, and a ship's hull is meshed in three places (the parked
object, the flight view, the interior) — so aboard the own ship they stay decoration (`CrystalNetRules.WorksAboard`).
Phase blocks and trapdoors do work aboard: their swap writes the ship's structure (live, re-applied after every
rebuild — see [CRYSTAL_NET.md](CRYSTAL_NET.md) §17.7).

## 7. Tests

`CrystalNet2Tests`: a phase block opens and keeps its dye, waits while someone stands in it; a trapdoor opens with
its form and grants the fall grace; a bridge extends to its length, stops at an obstacle and retracts; a piston
pushes and pulls (sticky) and keeps the dye, refuses to push a crate; a lift calls its platform to a stop and back to
the motor.
