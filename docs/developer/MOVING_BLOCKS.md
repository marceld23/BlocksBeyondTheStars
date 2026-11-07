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
  not blocks, and reads only the shaft layers its step enters (#3204). Per-owner caps: 8 bridge motors, 8 pistons,
  4 lifts (a lift's motor and stops never count against the network caps, #3205).
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
`LiftMaxHeight` 256 cells — 48 until #3204; nothing recorded a reason for 48, and the platform is one server float, so
riding cost does not grow with height). The platform rests on the motor: its cells fill `motor.Y + 1`, its top is
`motor.Y + 2`. A `lift_stop` beside the shaft (within `LiftStopReach` of the platform's edge) is a floor: the platform's
top comes level with the stop's own cell, so a stop belongs only from `motor.Y + 2` up. `LiftLevels` lists the floors,
bottom first. A stop on the bottom floor belongs to the lift too (`StopBelongsTo`, #3161 — it used to be excluded, so a
press on it did nothing and the platform could not be called back down). The client mirrors the rule
(`LiftRiding.LiftOfStop`, Client.Core).

**The build rule, told (#3297–#3302).** A level entry needs the motor one cell *below* the ground: a 3×3 pit one deep,
the motor in the middle of its floor, the resting platform filling the pit. The natural build — the motor set on the
ground — leaves the platform's top two cells above the ground (the jump clears ~1.2) and every stop on the ground beside
it belonging to no lift. The rule itself stays (moving where the platform rests would shift every saved lift); the game
says it instead: the manual, the Codex (`wiki.crystal-machines` / `wiki.crystal-recipes`, the picture drawn by
`tools/wiki/gen_circuit_diagrams.py`), the item and blueprint texts, the VEGA hints, and `GameServerLiftAdvice.cs` — a
placed motor reads the stop ring for the floor a builder stands on (a solid cell with two free above it, so a wall is no
floor) one or two cells below the platform's top and answers `srv.lift.motor_step` / `srv.lift.motor_high` (a level
landing or no ground near says nothing); a stop in a motor's ring up to three cells below the lowest stop height answers
`srv.lift.stop_too_low` when placed and when pressed.

**Network caps (#3205).** A lift's motor and stops are `CrystalNetRules.IsLiftPart`: they never open a network that
counts against `MaxNetsPerWorld` / `MaxNetsPerPlayer` (`OpensCountedNet`, `CountNets` via `CountsForNetCap`). Before,
every unwired stop opened its own network and counted — a 16-floor lift used half a player's share, and a stop over the
cap registered inert, so its floor silently vanished from `LiftLevels`. `MaxLiftsPerOwner` (4) is a lift's budget. A
conduit or a device that wires a lone stop into a circuit makes that network count, and the cap is read then (a lift
part beside a new cell does not hold it in a counted network).

**Signals.** A press or a rising edge on a stop calls the platform there (`LiftGoTo`); on the motor it sends the
platform to the next stop up, from the top back down to the bottom. A stop's status light is ON while the platform
waits at it. A **press** that cannot be answered says why (#3163, `ServerMessage`): `srv.lift.stop_no_shaft` (the stop
belongs to no motor; `srv.lift.stop_no_shaft:<LiftMaxHeight>` since #3204 — the height fills the text's `{name}`),
`srv.lift.stop_too_low` (#3300: the stop stands in a motor's ring, but below the bottom floor), `srv.lift.no_stops` (a
motor alone at the bottom; #3301: `srv.lift.only_bottom_floor` when a stop already stands on the bottom floor — the lift
has no *other* floor yet), `srv.lift.blocked:<height>` / `srv.lift.blocked_bottom` (the first
blocked layer in the ride's direction, counted from the bottom floor — at the press, or later to the player who called
when the ride stops short, see *Moving*). A circuit's signal stays silent.

**Snow.** The platform is no block, so the weather snow pass would see an open shaft and lay its drift under the
platform — solid snow then blocked `LiftLayerClear` (Lyxette, 2026-11-03). `TryFindSnowSurface` now skips every lift
shaft (`InLiftShaft`: the 3×3 column above a motor, up to `LiftMaxHeight`), and `LiftGoTo` clears tracked weather
drifts in the ride's layers first (`ClearWeatherSnowInShaft`; a player's own snow still blocks) (#3162).

**Moving.** The server owns `ServerLift.PlatformY` and moves it in `TickLifts` every tick (not on the beat;
`GameServerLiftRide.cs` since #3204). **Speed curve** (`Shared/Definitions/LiftMotion.cs`): a ride cruises at its
distance over `CruiseSeconds` (12 s), clamped to [`LiftSpeed` 3, `MaxSpeed` 12] cells per second — rides up to 36
cells keep the old constant 3 — eases in from 3 and out to 3 at `Acceleration` 4 cells/s² (`NextSpeed`: the braking
term `√(3² + 2·a·remaining)`), and never cruises above `SafeSpeed(TickRate)` = one layer per tick (15 at the default
15 Hz). 255 cells take about 23 s instead of 85. One tick moves at most `LiftMaxStepLayers` (2) layers.
**Clearance on the way:** `LiftGoTo` reads only the first `LiftStartCheckLayers` (8) layers of the ride
(`BlockedLiftLayer` — a blocked start still answers at once). **Going up, the rider's room is part of what moves**
(`LiftRiderHeadroom` 2): a rider's feet stand on the platform's top and the head reaches 1.8 above them, so a platform
at `y` carries its rider through `ceil(y)+1` and `ceil(y)+2` — and the client moves a rider with the platform's height
change *without collision* (`RideBy`), so a block found only when the platform itself reaches it would drive the
rider's head and then feet into it (review finding on #3204). The leading layer is therefore `ceil(y)+2` going up and
`floor(y)` going down (`LiftLeadLayer`); a blocked layer `L` stops the platform at `L − 3` going up (head below `L`) and
`L + 1` going down (`LiftStopBefore`); the start window going up reaches the target floor's rider room (`level + 2`),
so a floor whose shaft has no room for a rider is refused. Every tick `StepLift` reads the leading layers the step
enters (`BlockedLayerEntered`: `lead(from)±1 … lead(to)`) and the `LiftLookaheadLayers` (2) beyond them, up to the
target's own lead layer (`ShortenRideBeforeBlock`) — over a ride every layer is read once, at most about 48 block reads
per tick. A block found ahead **shortens the ride**: `TargetY` moves to the stop height and the list goes out at once,
so the clients, which ease towards `TargetY` and never past it, slow at the same height instead of running on into the
block for one list's latency; the arrival turns the motor's light amber and tells the caller
(`ServerLift.BlockedAt`). A block put into the shaft right in front of the platform (already behind the read-ahead)
is caught by the entering read and stops it at once (`StopLiftShort`). `LiftLayerClear` uses `GetBlock`, so a missing
chunk is loaded on demand — at most the few chunks of a few layers per tick, instead of the whole shaft (up to ~64
chunks) in the press's tick before #3204. Either way the platform stops clear of the block, the motor's light turns
amber and `srv.lift.blocked:<height>` goes to the player who called (`ServerLift.CalledBy`).
It **never moves down onto a body**: every layer the step sweeps is read (`LiftWayDownOccupied` over
`LiftLayerOccupied`), not only the next one, so a slow tick cannot carry it past someone — it waits, with speed 0.
`LiftList` (`NetLift`: motor cell, `PlatformY`, `TargetY`, `Speed`, `Moving`) goes out on every start and stop, when a
wait starts or ends, and every 0.2 s while one moves; a joining player gets it with the rest of the net. `NetLift.Speed`
is the ride's **average speed until the next list** (`LiftMotion.AverageSpeed` over `LiftBroadcastSeconds`), so the
client's linear easing lands where the next list puts the platform — the curve needed no client or protocol change; it
is 0 while the platform waits. The motor loops `lift_motor` while it moves; `lift_arrive` plays on arrival. `at=` in
the motor's row persists where it waited (or stopped short). A mined motor removes its lift.

**Space elevator (#3204).** The shaft may cross the planet's atmosphere line (`atmosphereHeight`, 100–280 above the
terrain's ~64); stops above it work like any other. A ride that starts below the line and ends above it sends each of
its riders `srv.lift.space_elevator` once (`TellLiftRidersAboveAir`) — not on a body without air (`AtmospherePresent`),
and not for rides between two floors above the line. The snow-free shaft, the gates and the rider grace reach the whole
256 cells; stepping off up there is the usual on-foot zero-g float (`UpdateAboveAtmosphere`).

**Riding.** The client (`LiftView`) meshes one 3×3 `lift_platform` slab with the block atlas, places it with a
collider at the eased height (`PlatformY` + `Speed` × time since the last list, clamped to `TargetY`) and moves the
local player standing on it by the platform's height change (`PlayerController.RideBy`; standing on the platform the
controller is grounded, so the zero-g float above the atmosphere line does not lift a rider off it). The server grants
riders the fall grace and counts the ride for "Going Up". #3204: the rider box (`OnLiftPlatform`) widens by
`(Speed − 3) × LiftRiderLagSeconds` (0.4 s) while the platform moves — at 12 cells per second the position a rider
reports trails the server's platform by a few cells, and the grace must not slip for that. A remote rider's avatar
follows that player's own reports, so at full speed it may sit a little above or below the platform drawn from the lift
list for a moment.

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
energy fence (`IsCutoutCubeKey`, `TextureTiles` cutout; a closed gate keeps its collider toward an open one). An open
gate is a sill (#3220): its side tile keeps only the bottom rail (`bake_fence_alpha.py --sill-only`) and the mesher draws
no top or bottom face for it (`BlockTraits.IsCaplessSill`) — the energy fence's lid at hip height read as a second
platform the player walked through. A gate in
the stop ring (`LiftPlatformRadius + LiftStopReach` from the motor's column) between `motor.Y + 2` and `LiftMaxHeight`
belongs to the floor one below it — a stack of gates to the floor of its lowest one. `SyncLiftGates` opens the gates of
the floor where the platform rests and closes all others (all of them while it moves, all of them when the motor is
mined); it runs from `UpdateLiftStatuses` (departure, arrival), from "already here", and every 0.5 s per lift
(placement, and a save whose chunk deltas disagree with the motor's `at=`). Chunks that are not loaded are skipped until
they are. A gate never closes onto a body (`CellOccupiedByBody`): it joins `ServerLift.GatesWaiting` and the next tick
with a free cell syncs again. Cost: 16 columns × the lift's height of chunk-cache reads per lift every 0.5 s — #3204:
read a chunk segment at a time (`SyncLiftGateColumn`: one `LoadedChunkOrNull` lookup per 16 cells, then array reads;
an unloaded segment is skipped whole), so a 256-cell lift costs at most 16 × 17 lookups and 16 × 255 array reads per
pass instead of 4 080 hash lookups.

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
