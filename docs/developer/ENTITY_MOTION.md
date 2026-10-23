# Entity motion — from the server tick to a smooth body on screen

How animals, settlement NPCs, planet machines and bandits get from the server's 15 Hz simulation onto the
client's screen without stutter (#2664–#2670). Remote players and space entities use the older
`RemoteEntityInterpolator`; everything that moves on a planet surface and is simulated by the server uses the
pieces described here.

## Why it changed

Until #2664 the planet entities were drawn straight from their lists:

| Actor | Sync | Client smoothing | What it looked like |
|---|---|---|---|
| Animals | full `CreatureList` every ~0.53 s | dead reckoning capped at 0.3 s, then `Lerp(dt·8)` | drawn speed 0.17×–2× of the real one, twice a second; a stopped animal drawn up to 0.8 blocks past its spot |
| NPCs | `NpcList` every 0.2 s | `Lerp(dt·5)` toward the newest, yaw raw | speed 0.59×–1.59× at 5 Hz, the body turning in 0.2 s steps |
| Machines, bandits | `PlanetEnemyList` every 0.2 s while moving | `Lerp(dt·8)` | the same 5 Hz pulse |

The animators then read their speed from raw per-frame root deltas, so every ripple became a leg-cadence pulse.

## Server side

### The velocity every list carries

`TickVelocity` (`GameServerEntityMotion.cs`) is sampled for every creature, machine, bandit and NPC at the start of
`FlushEntityLists` — once per world tick, after all simulation. It is the real displacement over the last tick
(wrap-aware), whatever moved the entity: the locomotion controller, a jump, a giant's own rules, a knock-back. A move
faster than `TickVelocity.MaxPlausibleSpeed` (40 blocks/s) is a teleport and reads as standing still.

Additive wire fields (older peers ignore them; an older server leaves them 0):

- `CreatureList.ServerTime`, `NpcList.ServerTime`, `PlanetEnemyList.ServerTime` — the server uptime of the tick.
- `NetCreature.VelX/VelY/VelZ`, `NetNpc.VelX/VelZ` (horizontal only), `NetCombatEntity.VelX/VelY/VelZ`.
- `NetCreature.Facing` — now filled for every creature (the locomotion heading), not only for giants.

### The creature motion stream

`CreatureMotionFrame` (tag 301) goes to each player every `CreatureMotionInterval` (0.125 s → every second 15 Hz
tick, 7.5 Hz) with the animals within `CreatureMotionRange` (48 blocks) of that player. It packs `Stride` = 8
integers per id: X, Y, Z (hundredths of a block), VelX, VelY, VelZ (hundredths of a block/s), the heading
(thousandths of a radian) and flag bits (`FlagAirborne`, `FlagPerched`). Integers keep both the MessagePack and the
browser JSON form small (~30 bytes per animal). Farther animals ride the 2 Hz list only. An older client drops the
unknown tag and keeps interpolating the list; a newer client on an older server does the same.

Moving machines and bandits sync every 0.125 s as well (`EnemyMotionSyncInterval`), so their display delay matches
the animals'.

### Movement rules that keep the curves smooth

Interpolation can only draw what the server simulates. These rules removed kinks at the source:

- **Roam segments turn** (`LocomotionState.TurnTo`/`Turning`): a finished roam segment used to write its new heading
  straight into `Heading` — up to a half-turn in one tick. Now the segment starts with a turn at the profile's turn
  rate (walking on, curving round). A re-roll asked for mid-turn (a blocked caller zeroing `ModeTimer` every tick)
  finishes the turn first, so a blocked machine does not pivot toward a new random target each tick.
- **Detours turn too** (`ApplyCreatureStep`): a blocked step still takes the free detour position found by
  `TrySteerAround`, but the heading turns toward it at twice the turn rate instead of snapping 35–110°. A roamer makes
  the detour its new roam heading; a hunter or fleer keeps to it for `CreatureDetourHoldSeconds` (aiming the
  controller along it) instead of turning back into the wall every tick.
- **Boxed in, an animal slows down**, so the way out starts with an ease-in.
- **Personal space between animals** (#2753, `GameServerCreatureSpacing`): a ground-bound wild animal is nudged out
  of the footprint (`CreatureFootprint.Radius`, the drawn body's reach) of the neighbour it stands deepest inside —
  any species — before its step runs through the ship/fence/terrain/body barriers, so a nudge into a wall is refused
  like any step. The nudge may undo the whole step plus a cruise step outward, so a sprinting animal cannot push
  through another body; a roamer whose step it cancels re-rolls its heading instead of pressing on. Sleepers do not
  push awake animals; overlapping sleepers shuffle apart slowly (`SleeperShuffleSpeed`), only the later one moving.
- **The drawn body against the blocks** (#2943–#2945, `GameServerCreatureBodies`): the block checks used to read the
  centre column only. A **sky ray's wing disc** (`CreatureFootprint.RayWingPlane`, the plane the builder draws the wings
  in) is sampled on eight bearings: the hover band rises until the disc is clear (`WingLiftCells`), and a step that would
  need more than `WingStepLift` of rise for the wings is refused, so the ray turns at a wall. A **big land animal's head**
  (`CreatureFootprint.HeadReach`) is followed along each step: a rise past the step-up limit anywhere on that line stops
  the step, so the animal halts with its head outside a wall while hills and stairs stay open. **Crowns carry fliers
  only** (`CanopyCarries`): every ground probe of a non-air animal reads leaves as no floor — it neither climbs a crown
  that reaches the ground nor is lifted onto one, and one already up there drops; birds still perch on crowns.
- **Running jump**: a walker that will reach a jumpable one-block rise within `RunningJumpLeadSeconds` takes off
  early (under the same checks as a jump at the lip) and keeps moving forward **inside its own column** while its
  feet come up; it crosses into the ledge column only once the feet are above the lip, so the body never clips the
  ledge. Crawlers and giants keep hauling themselves up in place.
- **NPC facing turns** at `NpcFaceTurnRate` (6 rad/s): every look-at (a visitor, a neighbour, the festival square, a
  customer, the way it walks) sets `ServerNpc.FaceGoal`; `MoveNpcs` turns the body there. Sitting down and lying
  down still snap.
- **NPC routes are pulled straight** (`NpcGridPath.Smooth`, see [NPC_ROUTINES.md](NPC_ROUTINES.md) §5).

## Client side

### Clock: `ServerClockSync` (Client.Core)

One instance per connection (`GameBootstrap.EntityClock`, reset on a world reset) maps a snapshot's server time to
the local render clock (`GameBootstrap.WorldTime`, which stops while the world is held). The offset follows the
fastest arrival seen (the least delayed packet is the best estimate) and creeps up slowly when arrivals are later; a
jump larger than `ResyncSeconds` (a new server, a held world resuming) re-syncs at once. Stamping on the server clock
keeps the spacing between snapshots exact — jitter, frame quantisation and WebGL's TCP bursts do not bend the motion.
A snapshot without a server time (an older server) is stamped on arrival.

### Interpolation: `MotionInterpolator` (Client.Core)

Each drawn entity owns one. Snapshots go in with position, velocity (or "none"), yaw and flags; the pose comes out
for `now − delay`:

- **Adaptive delay** = `DelayIntervals` (1.25) × the measured snapshot spacing + `JitterMargin` (0.05 s), clamped per
  user, growing toward its target at `DelaySlewUp` and shrinking at `DelaySlewDown` (a change of rate plays at
  0.5×–1.25× for a moment, never a jump; the spacing estimate rises fast and falls slowly).
  In practice: ~0.2 s for animals on the motion stream and for machines, ~0.3 s for NPCs, ~0.7 s for a far animal on
  the 2 Hz list.
- **Cubic Hermite** between the two snapshots straddling the render time, tangents = the server's velocities (or a
  backward difference when there are none — never the snapshot after, a tangent must not change while its span is
  on screen). Each tangent component is held within `TangentLimit` (3) × that segment's displacement on the axis,
  which keeps every component monotone: a server-side snap (an NPC stepping up a block within one tick, a creature's
  per-tick velocity spike) eases instead of bulging past the step.
- **Starvation brakes**: past the newest snapshot the body coasts on its last velocity, slowing to a stop within
  `MaxExtrapolation`; when data comes back the gap fades out over `CorrectionTau` instead of snapping.
- **Teleports reset** (farther than `TeleportDistance` and than `MaxPlausibleSpeed` could cover); a long silence
  followed by a move gets a synthetic copy of the old pose a beat before the new one, so the move starts at once.
- Everything is **wrap-aware** (the seam is crossed the short way).

Users: `CreatureView` (the 2 Hz list + the motion stream; facing from the drawn velocity, a standing body turns
slowly toward the server heading; the animator's airborne/perched pose follows the flags of the snapshot being drawn,
so the pose changes with the body it belongs to), `NpcView` (pose changes reset the buffer; NPC yaw comes out of the
interpolator), `WorldEntities` (machines and bandits).

### Animation

`CreatureAnimator` and `PlayerAvatar` low-pass the measured root speed (and velocity) with an 80 ms time constant
before it drives cadence, gait choice, swing and the foot planner. `PlayerAvatar`'s leg swing follows the stride the
body actually covers (`asin(stride / 4 / leg length)`, never below the old curve, capped at 40°), and
`PlayerAvatar.NeverAirborne` keeps NPCs out of the jump pose on an eased step up.

### Space: no wrap, and the stale-view scan

The flight view (`SpaceView`) smooths remote pilots, NPC traders and every moving space entity with
`RemoteEntityInterpolator`. A space instance never wraps, so it samples with `RemoteEntityInterpolator.NoWrap` (any
circumference ≤ 0): a plain straight-line lerp. Never pass a "very large" circumference instead — `WrapX` moves every
negative X up by the circumference, and the old `int.MaxValue` drew everything west of the origin ~2.1 billion units
away (#2707; it also fed the lock-distance overflow of #2428/#2450).

A space entity that leaves (a collected drop, a rescued life pod, a salvage capsule, a destroyed hostile) has no
message of its own — it is simply missing from the next `SpaceState`. `SpaceEntitySync.CollectStale` (Client.Core)
finds the models whose id is not in the snapshot; it runs every frame and must not be skipped by comparing counts,
because asteroids, debris and hull-backed stations/wrecks sit in the snapshot without a model (#2708).

A pilot on a spacewalk arrives as the suit pose plus `NetSpacePlayer.HasShipPose`/`ShipX…ShipYaw` (the ship floating
where they stepped out) and their suit colours; the view draws a `PlayerAvatar` at the suit and keeps the ship at
that pose, and clears the pilot's interpolation buffer when the pose switches between ship and suit (#2709).

## Tests

- `MotionInterpolatorTests`, `ServerClockSyncTests` (Client.Tests): even drawn speed on the stream and on the 2 Hz
  list, no overshoot at a stop, braking starvation, teleport, seam, step-ups and velocity spikes without a bulge,
  adaptive delay, yaw, flags.
- `CreatureMotionStreamTests` (Client.Tests): real client against the real server over the encoded wire — the stream
  every second tick, only for near animals, positions and velocities matching the list; `TickVelocity`.
- `LocomotionControllerTests`: roam segment changes stay within the turn clamp; a re-roll mid-turn finishes the turn.
- `CreatureMotionArenaTests`: the running jump never stalls at the lip and never clips the ledge; a hunter steering
  round a wall never turns faster than the clamp.
- `CreatureSpacingTests`: two big animals of different species planted inside each other step apart; an awake animal
  yields to a sleeper and overlapping sleepers come apart with only the later one moving; a spawn spot inside a big
  animal is refused; a big herd spawns with room between every pair, and titan herds of 3–4 (and a big grazer herd)
  spawn whole on flat ground; the footprint grows with size and segments.
- `CreatureBodyBlockTests`: a sky ray beside a roof rides over it and a hunting ray never puts its wings into a wall; a
  big walker stops with its head outside a wall and still climbs a stair; a crawler never climbs a stair of leaves, a
  land animal on a crown comes down, the ground probe reads a crown as floor for fliers only, a bird still perches on one.
- `NpcGridPathTests`: string-pulling (open square, walls, corners, doorways, steps).
- `RemoteEntityInterpolatorTests`, `SpaceEntitySyncTests` (Client.Tests): the no-wrap lerp at negative X and across 0,
  the stale-view scan with view-less rocks around. `SpaceFlightClientTests` (Client.Tests, real client and server): a
  partner west of the origin sampled where the server says, a collected drop leaving the snapshot while rocks stay,
  a spacewalking partner arriving with the suit and the floating ship.

## Tuning knobs

| Knob | Where | Value |
|---|---|---|
| Motion stream beat / radius | `GameServer.CreatureMotionInterval` / `CreatureMotionRange` | 0.125 s / 48 blocks |
| Machine + bandit sync | `EnemyMotionSyncInterval` | 0.125 s |
| Render delay | `MotionInterpolator.DelayIntervals`, `JitterMargin`, per-user min/max | 1.25 × spacing + 0.05 s |
| Coast after starvation | `MotionInterpolator.MaxExtrapolation` | ≤ 0.25 s |
| NPC body turn rate | `GameServer.NpcFaceTurnRate` | 6 rad/s |
| Running-jump lead | `GameServer.RunningJumpLeadSeconds` | 0.3 s |
| Detour hold | `GameServer.CreatureDetourHoldSeconds` | 0.5 s |
| Animal personal space | `CreatureFootprint.SeparationFactor` × (rA + rB) | 0.8 |
| Sleeper shuffle | `GameServer.SleeperShuffleSpeed` | 0.4 blocks/s |
| Ray wing lift / per step | `GameServer.WingLiftCells` / `WingStepLift` | 12 / 1.5 blocks |
| Head line (share of the reach) | `GameServer.HeadReachShare` | 0.9 |
| Animator speed low-pass | `SpeedSmoothTau` (CreatureAnimator, PlayerAvatar) | 0.08 s |
