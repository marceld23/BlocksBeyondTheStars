# The monorail hover train (#2113)

Justus' idea (2026-09-27), Marcel's decisions: pylons the player places, an **energy line** the game spawns between
linked pylons (data, not blocks), a train of authored wagons that hovers along it, and — the game's first **moving
frame** — riders who **walk inside the train while it moves**. Everything the server and every client must agree on is
Unity-free in `Shared`.

## Shared

- `Shared/Definitions/RailRules.cs` — the constants (auto-link range 32 / 30°, linker range 64, two links per pylon, 64
  pylons per line, hover height 0.5, the wagon box 6 × 3 × 3.2, the top speeds 8 / 16 / 24 blocks/s — #3444, a new
  cab at setting 2 —, the stop halt 8 s, boarding reach 4, stow reach 5), `AutoLinkFits`, `AutoLinkBendDegrees` / `AutoLinkCost` (the fallback's ranking:
  distance + 0.5 blocks per degree of bend, #3256), the seat offsets per wagon kind, `ClampLocal` / `OutsideWagon`
  (the rider's box), `WagonArc` (the wagons hang behind the cab by the train's fixed **facing**, #2741), the train's
  motion — `TrainSpan`, `CabRange` (where the cab's arc may run so every wagon stays on an open line), `Advance` (runs
  the arc; a loop wraps, an open line bounces off the range's ends — only the direction flips, the rest of the step runs
  back, the arc never jumps), `LeadArc` / `ArcForLeadAt` (the leading wagon: the cab when the train runs the way it
  faces, the last wagon when it runs backwards — the halt point until #3463), `MiddleArc` / `ArcForMiddleAt` /
  `HaltMiddleArc` (#3463: the train's middle, the arc it halts at for a stop, held half the train plus half a wagon in
  from an open line's ends) and `PoseFacing` (an older server's facing is its direction) —
  `LocalToWorld` / `WorldToLocal` (the frame: local X across, Y up from the floor, Z toward
  the cab; heading = radians about +Y, forward (cos, 0, sin), right (sin, 0, −cos)) and `FrameId` (`train:wagon`).
- `Shared/Definitions/RailSpline.cs` — a Catmull-Rom spline through the pylon tops with an arc-length table, the
  `SandwormPath` idea in 3D: unwrapped along X and Z (#3256 — Z wraps at the latitude period) relative to the first
  point so a line over either world seam is one curve, wrapped back on the way out (`Nearest` measures both the short
  way round); open (a path, the train reverses at the ends) or closed (a loop); `PointAt`, `YawAt`,
  `PoseAt` (the wagon centre + heading), `Nearest`, `NormalizeArc`. The wire carries only the points; both sides build
  the same curve.
- `Shared/Definitions/RailMotion.cs` and `RailCurveLimits.cs` — the train's speed profile (#3444, see *Speed* below).

## Wire (`Networking/Messages/RailMessages.cs`, ids 270–275)

`RailList` (every line: id, points, closed, stop arcs) on join and on every change; `TrainList` (id, owner, line, arc,
speed, direction, facing — #2741, additive, 0 from an older server —, autopilot, halted + remaining, wagons, riders,
velocity — #3444, additive, see *Speed*; takes passengers — #3446, additive, default true) at 5 Hz while a train moves
and at once on every change; `EnterTrainIntent` (train, wagon, seat −1 = standing), `ExitTrainIntent`, `SetTrainIntent`
(speed / halt / autopilot / passengers — #3446, additive —, −1 = unchanged), `StowTrainIntent`. Additive fields:
`MoveIntent.FrameId` (set → X/Y/Z are the wagon-local offset), `PlayerPresence.FrameId/LocalX/Y/Z`,
`PlayerStateUpdate.InTrain/TrainSeat`, `PlayerState.InTrain/TrainSeat/TrainLocalX/Y/Z` (never a persisted bond: a join
clears it), `NetNpc.FrameId/LocalX/Y/Z/LocalYaw` (#3445: an NPC passenger, see *Passengers*).

## Server (`GameServerRails.cs`)

- **Graph.** `RailWorldState` on `LoadedWorld`: nodes keyed by the pylon's top cell (stacking re-keys the node upward,
  mining the top drops it down), links as data. `OnRailBlockPlaced` (after the Crystal Net hook in `HandlePlace`'s
  `JoinPlacedBlockToNets`, `GameServerPlacement.cs`) auto-links (`AutoLinkPlacedPylon`, `GameServerRailAutoLink.cs`)
  to the builder's previous pylon (`LastPylon`) when `LinkRefusal` is null: range, the two-link cap ("branch" — checked
  before the bend since #3256, so a full pylon says "no forks"), the bend rule against the previous link's direction,
  the 64-pylon cap, and the **clearance check** `LinkClear` — the wagon box (three columns across, four cells up from
  the hover height) swept one block at a time along the straight run between the two tops; any solid, non-liquid,
  non-flora block that is not a pylon or a stop refuses ("Obstacle"). Every delta (the range, the previous direction
  `LineDirectionInto`, the clearance sweep) runs the short way round both seams — #3256: the previous direction used to
  be raw, so the first straight pylon after a link over x = 0 read as a U-turn ("too sharp"). When the previous pylon
  does not take the new one (none — mining and a world load forget it —, out of range, refused), `BestFreeLineEnd`
  tries every **free line end** (≤ 1 link, not the generated intercity line) in auto-link range whose own line the new
  pylon continues within 30°, cheapest first by `RailRules.AutoLinkCost`, until one passes `LinkRefusal` — fill a gap,
  extend the other end, re-place a mined pylon; the straighter continuation beats a parallel line's nearer end. One
  link per placement. When nothing links the builder hears the previous pylon's refusal, or — out of range / none —
  `srv.rail.no_link_hint` (use the linker); the world's very first pylon keeps VEGA's `vega.sys.rail_first`.
  `UseRailLinker` (the `rail_linker` gadget: first use picks A, second couples A–B or uncouples them). `RebuildRailLines`
  walks every component from an end (a loop from its first pylon), keeps a line's id while its first pylon stands,
  builds the spline, and lands every `rail_stop` device within `StopReach` of a line on it. The graph and the trains
  persist as strings in `WorldMetadata.RailGraphs` / `Trains` (`SaveRails` / `LoadRails`, per body).
- **Trains.** `UseRailCab` puts a train on the line nearest the aim (refused where another train stands within its
  length); `UseRailWagon` couples behind the tail; `HandleStowTrain` returns the items (owner, nobody else aboard, in
  reach); a train whose line vanished comes back to its owner (`ReturnTrain`). `TickTrains` (guarded in the main tick
  after the giants): autopilot or a rider in the cab runs it (`RunTrain`, `GameServerRailMotion.cs`) by the speed
  profile (#3444, see *Speed*) through `RailRules.Advance`. A train has
  a fixed `Facing` (set when it is placed: +1 for a player's cab, −1 for the public train) and a `Direction` that only
  moves the arc: an open line flips the direction at the ends of the cab's range and nothing else, so every wagon keeps
  its place and heading and the riders (re-derived from their wagons every tick) ride through the reversal without a
  jump (#2741 — the old reversal re-seated the arc by the train's length, swapped the wagons round and turned each by
  180°, so a rider on the rear bench jumped ~11 m). On one leg the cab therefore pushes from the back (there is no
  jump-free reversal that keeps the cab in front on a line without a loop). A loop wraps. The autopilot halts at a stop
  the train's **middle** crossed this tick (#3463, Lyxette: "put the post at the end of the station and the train is
  all in or all out by its direction; in the middle it only ever pulls in halfway" — until then it was the leading
  wagon; `HaltPointArc`, `StopHaltArc`; `Crossed`, wrap-aware; a reversal splits the tick into the run to the end and
  the run back) and centres the train on the stop (never so near an open line's end that a wagon hangs past it), for
  `StopHaltSeconds`, once per stop until it is clear of it; a stop
  within a wagon's length of the dead end ahead turns the train round while it stands. A coupled wagon or a shortened
  line brings the arc back into the cab's range (`ClampTrainArc`). The save record appends the facing (an older record
  faces its direction) and then the passenger switch (#3446; an older record takes passengers);
  `DepartTrainsAtStop` (the Crystal-Net rising edge on the `RailStop` device, or the owner's press on the stop — the
  machines' manual start) ends the halt early. Two seams carry every stop event: `HaltAtCrossedStop` is *the train
  halted at stop X* (`HaltStop`; the public train also starts its life halted at its first station,
  `EnsureIntercityTrain`), `DepartTrain` is *the train departs* (the halt ran out in `TickTrains`, a signal or press on
  the stop, the cab's "go").
- **The moving frame.** `HandleEnterTrain` (owner or ally, within reach of the wagon) records the rider (`RailRider`:
  wagon, seat, local offset — standing where they were, brought into the box; seated on the seat), sets
  `InTrain`/`Seated` and derives the world position. `HandleFramedMove` (called first in `HandleMove`): a `MoveIntent`
  with the rider's frame stores the clamped offset and derives `Position` from `WagonPose` + offset; a seated rider's
  offset is fixed; an offset outside the box (`OutsideWagon`) is **leaving** — on foot where they stepped off; a world-
  frame report while aboard is a client that left on its own. Every tick the server re-derives every rider's world
  position from the wagon pose it drives, so streaming, creatures, air, pickups and everything else keep their world
  coordinates. `HandleFallDamage` ignores a rider. Death and a join clear the bond (`LeaveTrainSilently`); leaving by
  the door sets the rider down beside the wagon (`LeaveTrain`). The presence carries the frame and the offset.
- **Leaving (#3458/#3459).** The wagon floor hovers `LineRise + HoverHeight` = 1.7 over the pylon's top cell — off the
  block grid — and the door (grid z 2..3, wagon-local z −1…+1) is two blocks high, so a platform one block above the
  floor a player sees (1.3 up) could neither be stepped nor jumped onto. **F** (`LeaveTrain` → `RailExitSpot`,
  `GameServerRailExits.cs`) now searches a standing cell (`StandableAt`, support + two cells of headroom, no-load reads)
  outside the door: the halt stop's side first (read in the line's frame at the stop, so a rear wagon on a curve agrees;
  else the rider's), 2.3 then 3.1 out, along the door, feet from one below to two above the floor (nearest the floor
  first); then the other side; then a ring 4.1 out on either side — never behind the train, on the line's axis, where
  the wagons would run into the rider; today's `ExitSide` point last. A framed move that reaches the server after the
  set-down (still in flight when F was handled) is dropped in `HandleFramedMove` — its wagon-local numbers would read as a
  world position near the origin; the client also holds its framed reports for 1.5 s after asking to leave
  (`PlayerTrainRider.RequestExit`). The client never holds a train exit in the settle freeze: the set-down arrives as
  `GameBootstrap.TrainExitTarget` (not `RespawnTarget`), a walk-off keeps its own spot when the server's lies within
  1.5 blocks of where it stepped off (plus the wagon's speed × 0.5 s for a jump), an F set-down is a plain move
  (`TrainExitRules.SnapToServer`, Client.Core). Pressing outward into the door of a **halted** train for 0.2 s — stuck
  there, not walking out (`TrainExitRules.Stalled`: outward progress under 0.5 b/s) — sends the exit
  (`PlayerTrainRider.StepDoorExit`, `TrainExitRules.PressingIntoDoor`). Off a **moving** train a walk-off is a jump:
  `ApplyKnock(wagon velocity × 0.5, capped at 12, + a hop of 4)` (`TrainExitRules.JumpPush`; the wagon's velocity is
  measured from its own motion over real time, restarted after a gap of more than 0.25 s and on boarding); **F** while
  it moves steps the body just outside the nearest free door and walks off from there (`TryJumpOffMoving` — both doors
  blocked → the plain exit). The server has no move-speed check, and fall damage counts the vertical landing speed only,
  so the leap itself never hurts.
- **Dealer.** `NpcProfessions`: `rail_dealer` (vendor, theme `rails`, post `rail_post`, building function `depot` —
  a new `StructureRoles` plot role); its building `village_depot_1` / `town_depot_1` (+ `_alien`) is a copy of the
  blockfarmer's quarry with the post marker swapped, optional in every modular settlement kit like every profession's;
  the dialogue `profession_rail_dealer`; market recipes `market_rails_*`; blueprint `monorail` (after the speeder and
  the pump); the wagons need polymer and lubricant (#2107).

## Client

- `RailView` — the lines as blue **energy bands** (#2129): per spline sample, two crossed ribbons (one flat, one
  upright) in the additive `BlocksBeyondTheStars/Particle` material with a procedural glow texture (V across: a bright
  core fading into a soft halo; U along: one narrow brighter pulse per 6 m tile). The vertex colour is HDR so the scene's
  URP bloom gives the band a soft glow; the pulses travel by scrolling the material's texture offset every frame (no
  mesh work). A stop is a short, wider amber section of the band. Caches one `RailSpline` per line by the server's list
  reference and hands it out (`SplineOf`).
- `TrainView` — a wagon is a hand-authored voxel grid (`WagonCells`: floor, side sills with glass above, a door
  opening in the middle of each side, a roof, the cab's windscreen and console, the seat wagon's benches, the sleeper's
  bunks, the bar's counter) meshed by `ChunkMesher` like the speeder hull, with a `MeshCollider` that is always solid.
  Every frame each wagon is posed on the spline: the server's arc run on by its velocity (#3444,
  `TrainDrawArc.PredictSpeed`) and direction since the last update
  and eased toward each new one (`DrawArc`, stepped by the Unity-free `Client.Core/TrainDrawArc` through
  `RailRules.Advance`, so the drawing bounces off a line's end like the server), the wagons behind the cab by `WagonArc`
  and posed by the train's facing (`RailRules.PoseFacing`). **The wagon transform is the
  frame**: local X/Y/Z are exactly the rules' local offsets (`Quaternion.LookRotation((cos, 0, sin))` puts local +Z on
  the heading and local +X on its right). `TrainView` runs early in the frame (`DefaultExecutionOrder(-40)`), so the
  wagons are posed before the player controller moves a rider; `WagonAt` answers which wagon's box holds a point.
- `PlayerController` and its `PlayerTrainRider` component (`PlayerTrainRider.cs`, which the controller adds in `Awake`
  and calls where the riding code used to sit) — while `Game.InTrain` names a frame the controller is parented to that
  wagon transform (`UpdateTrainFrame`): the platform's motion and yaw carry the body and the camera, the normal on-foot
  movement runs on top (walking, jumping, gravity onto the wagon's floor collider), `SendMovement` reports the
  **wagon-local** offset with the frame id at 10 Hz (`TrySendFramedMove` → `SendFramedMove`) and treats an offset
  outside the box as leaving; a seated rider is
  parked on the seat with the controller off; E in the cab opens `TrainCabUi`, E beside a seat sits, F leaves; a fall
  is never reported aboard. On foot, E near a wagon boards it (`TryBoardNearbyTrain`), and so does stepping into a
  wagon's box (`StepAboardTrain`, #2129 — edge-triggered, one `EnterTrain` per entry). Walking through an open end
  into the next wagon moves the frame there (`CrossToNeighbourWagon` sends `EnterTrain` for it; in the 0.6 m gap the
  report stays clamped into the old wagon); walking off the train reports the outside spot as a framed move, so the
  server sets the rider down where they stepped off, and the client does not re-parent to that frame before the
  server's confirmation.
- **The physics sync (#2129).** The project runs with `Physics.autoSyncTransforms` off, so a `CharacterController`
  only sees transform changes — its parent wagon's motion included — at the next physics step, and `Move` starts from
  the stale simulated position. Without a sync every rendered frame between two fixed steps threw the wagon's motion
  away: the rider drifted to the rear, crossed `OutsideWagon` and fell out. While aboard, `Update` therefore calls
  `Physics.SyncTransforms()` before anything moves the body (the open-panel branch included). Anything else that moves
  a character on a moving transform needs the same.
- `RemotePlayers` — a presence with a frame is placed with `wagon.TransformPoint(local)` on the wagon this client
  draws (no interpolation), so nobody slides through a wall on a curve or across the seam.
- `TrainCabUi` — the panel (the state line with the speed right now, the top speed 8 / 16 / 24, halt / go, autopilot,
  leave, pack up). A public train (empty owner) shows the state line, the "Intercity line" note and only the leave
  button.

## Speed (#3444)

Lyxette builds long lines to far cities, and the old fixed speeds (4 / 8 / 12 blocks/s, a new cab at 4 — slower than
walking) made them a chore. Modelled on the lift's `LiftMotion` (#3204, ADR 0014 amendment):

- **The settings are top speeds**: 8 / 16 / 24 blocks/s (`RailRules.SpeedTable`, the one table; `RailMotion.TopSpeed`).
  A new cab starts at setting 2 (`RailRules.NewCabSpeed`); the public intercity train keeps setting 2.
- **The profile** (`Shared/Definitions/RailMotion.cs`, pure). Every tick `v ← NextSpeed(v, limit, dt)`: up by
  `Acceleration` (4 blocks/s² — the lift's ease; 0 → 16 in 4 s) toward the limit, down toward a lower limit by at most
  `ServiceBrake` (8 blocks/s²), never below 0. The limit is the lowest of
  - the **top speed**;
  - the **leg's cruise** `CruiseSpeed = clamp(leg / LegSeconds (6 s), MinCruise (4), top)`. The leg is the distance from
    the target behind the train's halt point (its middle, #3463) to the target ahead of it: on autopilot the stops, on an open line also the ends
    of the cab's range. The stop it last halted at (or passed, below) is no target while it still lies ahead; on a
    **loop** it is the target one lap ahead once the train is level with it or past it (`StopTargetDistance`) — so a loop
    with a single stop cruises by the loop's length from the first block after the halt (it used to have no target for a
    wagon's length and set off toward the top speed). No target ahead — a loop without stops, or a driver at the panel —
    cruises at the top speed. A 40-block hop cruises at ≈ 6.7 blocks/s, a 96-block leg reaches 16, a leg of ≈ 150 blocks
    reaches 24 (accelerating and braking at 4 blocks/s² take 72 + 71 blocks);
  - the **braking curve** to the target ahead `BrakingSpeed = √(ArrivalSpeed² + 2·Acceleration·d)` with `ArrivalSpeed`
    2.5 blocks/s — the train arrives at a stop or a line's end at a walking pace. A stop the train **cannot** arrive at
    even at `ServiceBrake` — `v² > MaxHaltSpeed² + 2·ServiceBrake·d` (`RailMotion.CanArriveAt`; `MaxHaltSpeed` 5 =
    2 × `ArrivalSpeed`, because an ordinary arrival runs a hair above 2.5 on its last tick and must never be passed): the
    autopilot switched on a few blocks before a stop at speed, a stop placed just ahead of a fast train — is **passed**:
    `TrainTargets` makes the nearest such stop the train's `LastStop`, so `HaltAtCrossedStop` lets it through, and the
    train brakes into the next stop instead of being cut onto this one and halting dead at speed (at 24 blocks/s that is
    every stop closer than ≈ 34 blocks). Two such stops less than one tick's run (≈ 1.6 blocks) apart would still catch it
    on the second;
  - the **bend limit** (`RailCurveLimits`, sampled once per line on first use, cached on `RailLine.Curves`): every
    2 blocks of arc the lower of two bounds — **sideways** `CurveSpeed = √(LateralAcceleration (4) / κh)` (radius 16 →
    8 blocks/s, 64 → 16, 144 → 24) and **over a crest or through a dip** `CrestSpeed = √(VerticalAcceleration (8) / κv)`
    (radius 8 → 8, 32 → 16, 72 → 24: a rider is pressed into the seat or lifted a little, not thrown sideways, so the
    bound is twice as generous). κh is the change of heading (seen from above) and κv the change of slope between the
    chords before and after the sample, each over the chords' mean length. The chords are `ChordAt` = twice the spline's
    local segment (`RailSpline.SampleSpacingAt` — ten segments per span, 6.4 blocks on a 64-block linker span), at least
    `MinChord` 12 blocks: the polyline's corners then shift κ by at most (segment / chord)² / 4 ≈ 6 %, and in the middle
    of a line κ matches the Catmull-Rom curve's own within a few per cent (the old 4-block chord, shorter than a segment,
    read each polyline corner as a tight bend). A chord far longer than its arc — a loop round the world's seam — reads
    as straight. Then spread both ways by the braking curve (no sample above √(neighbour² + 2·4·2)), so a train slows
    *before* a bend. A train takes the lowest limit over its whole length (cab to last wagon, half a wagon beyond each
    end). Measured — the lowest limit in blocks/s on open lines of 13 pylons, in the line's middle (30–70 % of its arc) /
    anywhere on it (the end spans bend a little harder: the spline doubles its first and last pylon), before (4-block
    chord, one 3D bound of 4 blocks/s²) and now:

    | Line | Before | Now |
    | --- | --- | --- |
    | 64-block spans turning 15° per pylon (radius ≈ 245) | 24 / 19.4 | 24 / 24 |
    | 32-block spans turning 15° (radius ≈ 123) | 20.5 / 16.9 | 21.9 / 19.1 |
    | 32-block spans turning 30° (radius ≈ 62) | 13.8 / 11.6 | 15.0 / 13.3 |
    | 18-block spans turning 20° (radius ≈ 52) | 13.8 / 11.7 | 14.3 / 13.4 |
    | up and down at 1 in 16 over 32-block spans | 22.4 / 22.4 | 24 / 24 |
    | up and down at 1 in 8 over 32-block spans (crest radius ≈ 80) | 15.9 / 15.9 | 24 / 24 |
    | up and down 2 blocks per 18-block span (≈ 1 in 9, intercity-like) | 13.4 / 13.4 | 22.2 / 22.2 |
    | up and down 3 blocks per 18-block span (1 in 6) | 11.0 / 11.0 | 18.2 / 18.2 |
    | up and down 10 blocks per 16-block span (a sharp crest) | 5.5 / 5.5 | 9.7 / 9.7 |
- **Server** (`GameServerRailMotion.cs`). `ServerTrain.Velocity` is RAM only — no save change; a loaded train starts
  from rest. `RunTrain` → `TrainRunDistance` (`TrainTargets`, `TrainCurveLimit`, the new speed; the last step onto a
  stop it halts at is cut to land the train's middle on its halt arc — #3463; the leading wagon before — so
  `HaltAtCrossedStop`'s snap is ~0.001 blocks) →
  `RailRules.Advance` → `HaltAtStopsReached` (the autopilot's stop check, the reversal split as before). A halt — at a
  stop or the driver's — zeroes the speed (the driver's halt is an instant stop); a reversal at an open line's end too
  (it braked into the end and sets off the other way from rest). `DepartTrain` starts from rest and keeps the stop the
  train stood on as its last stop (the cab's "go" used to clear it, and the autopilot halted again at once on that
  stop). A departing train runs on the tick its halt ends. **Driving by hand** (autopilot off, a rider in the cab): the
  same profile at the top speed, braking into an open line's ends; it never halts at a stop. Nobody in the cab: it
  stands, at once, as before. A train **longer than its open line** (`NoRoomToRun`: the cab's range is one spot) stands
  at speed 0 where the line pins it, and is sent as halted with velocity 0 (`WireHalted`, `WireVelocity`) — it used to
  creep up to the arrival speed on the spot and the cab read "Running — 3 blocks/s"; now it reads "Halted".
- **Wire.** `NetTrain.Velocity` (additive float): signed like `Direction` (+ along increasing arc), the speed over the
  next broadcast — the current speed, at least `RailMotion.StartSpeed` (0.4, the average of the first 0.2 s of the
  ease-in) for a running train, 0 for a standing one (then `NetTrain.Halted` is set too — also for a train with no room
  to run, so the client never reads its 0 as an older server's). The client runs its drawing on by it
  (`TrainDrawArc.PredictSpeed`): halted → 0; else |velocity|; velocity 0 on an autopilot train that is not halted can
  only come from an older server, which ran it at the old fixed speed (`TrainDrawArc.LegacySpeedTable`, 4 / 8 / 12).
  The prediction error of a speed that changes by at most 4 blocks/s² is about 0.1 blocks per 0.2 s list; a train that
  halts at a stop is broadcast at once (not at the next 0.2 s slot), so no drawing runs on past the stop for long.
- **Cab panel.** "Top speed (blocks per second)" over three buttons 8 / 16 / 24 (● the chosen one); the state line
  reads "Running — 12 blocks/s".

## Passengers (#3445)

The foundation of Lyxette's wish (2026-11-09): "the settlers who visit me should come by train". NPCs ride as passengers;
who rides where is the trips' business (next sections). Server: `GameServerRailPassengers.cs`.

- **Where they sit.** A **passenger wagon** is every wagon but the cab that has seats (`wagon_seats` 6, `wagon_sleeper` 4,
  `wagon_bar` 2). An NPC takes a free seat (the wagon nearest it first); seats are shared with the players
  (`NpcSeatHeld` looks at both). The NPC passengers live in `ServerTrain.NpcPassengers` (NPC id → `RailRider`), never in
  `Riders` — `TrainRuns` treats any rider in wagon 0 as the driver. A train takes NPCs only when it runs on its own
  (autopilot — a driver never halts at a stop) and its owner lets villagers ride (`TakesPassengers`, #3446; the public
  train always does). A train without a free seat takes nobody: the NPC waits for the next one.
- **Players first.** A player boarding a seat an NPC holds (`HandleEnterTrain` → `MakeSeatForPlayer`) sends the NPC to
  another free seat (its own wagon first), else to a standing spot in the aisle (`AisleSpot`, pose 0) — it rides on, it is
  never pushed off.
- **Boarding and stepping off** only while the train halts at a stop (`HaltStop` — the autopilot's halt, never the
  driver's): `TryBoardNpc`, `AlightNpc`. The NPC steps off at the door — the standable spot `ExitSide` beside its wagon on
  the stop's side, down to 6 below the floor (`NpcDoorSpot`) — else onto the stop's **platform spot**
  (`RailStopPlatformSpot`: the standable spot nearest the stop block within 4, at least 2.2 blocks from the line's middle
  so the wagons pass it by; cached 30 s). Neither fits → it is sent home (a guest copy is not: its visit ends). Out of
  every player's area of interest the door is not looked for — the passenger steps onto the platform spot — and the
  platform spot reads only loaded chunks (see "No terrain loads out of reach" below).
- **Riding.** Every tick `TickTrains` places each passenger from its wagon (`CarryNpcPassengers` → `RiderWorld`, the
  facing turned with the wagon): seated (pose 1) on a side bench facing the aisle, standing (pose 0) toward the cab. A
  reversal at a line's end moves nobody (#2741), so passengers ride through it like players. The
  NPC carries `TrainId` and its ride (`RideFrame`, `RideLocal`, `RideYaw`); the movement (`MoveNpcs`), the routine, the
  route search, the village chatter, the festival, the guests and the wish offers leave it alone. A passenger whose NPC
  is gone (a guest that ended, a resident whose bed went) drops off the list.
- **Wire** (additive, contractless): `NetNpc.FrameId` (`RailRules.FrameId`), `LocalX/Y/Z`, `LocalYaw` (radians, 0 = toward
  the cab). X/Y/Z stay the world position. Client: `NpcView.Train.cs` draws a framed NPC at
  `wagon.TransformPoint(local)` with the wagon's rotation every frame (the pelvis 0.45 down when seated), bypassing the
  interpolation buffer; a changed frame (boarded, stepped off, another wagon) resets the buffer — a snap. The rules are
  Unity-free in `Client.Core/NpcTrainRide.cs`. `WorldRig` hands `NpcView` the `TrainView` (like `RemotePlayers.Trains`). An
  older client ignores the frame and shows the world position.
- **Never lost, never duplicated.** A train that goes away under its passengers sets them down (`SetDownNpcPassenger`):
  beside the wagon (the door spot) when a player's area of interest holds it, else — or when nothing fits — straight home
  (`ServerNpc.Rest`) where nobody sees it. Never popped in front of a player: a traveller out of reach whose home a player
  watches (`SpotWatched`) stays where it rode, unseen, and its trip's timed way home brings it in from out of sight; a
  guest copy riding home is never sent "home" (its `Rest` is where it stepped off at the base's platform) — beside the
  wagon in view it walks off like any leaving guest, else its visit simply ends (`SetDownTrainGuest`). The ways: stowed or
  returned (`ReturnTrain`), its line gone, or its line changed under it by a placed, mined or relinked pylon
  (`SettleNpcPassengersAfterRebuild`, inside `RebuildRailLines` per train **before** it is clamped onto its new line or
  packed away: a train whose line still has exactly the same pylons keeps its passengers — a line rebuilt elsewhere changes
  nothing; the others are set down beside the wagon **on the old line object** they rode — a vanished line used to leave
  no line to find the door on, and they popped home in front of the player). A trip learns it through
  `OnNpcPassengerSetDown`. Passengers are RAM only — never part of the train's save record — and a world load regenerates
  the villagers anyway. No tickets, no fares (Marcel's rule).

Tests: `RailPassengerTests` (board only at a halt, the seat in the passenger wagon, the position following the wagon tick by
tick, the wire's frame, stepping off beside the wagon onto real ground; players first — another seat, else the aisle; a
full train; an unrelated rebuild keeps them, a broken pylon / the stow / nobody near / the line gone while the player
stands by set them down safely — beside the wagon, never popped home; the save record unchanged, nobody lost),
`NpcTrainRideTests` (Client.Tests: aboard, the snap, the facing, the wire round trip, a hand-written older payload without
the frame keys reads on foot; an older `SetTrainIntent` without `Passengers` reads −1 and an older `NetTrain` without
`TakesPassengers` reads on). The test arena (a line with two stops on the living-NPC pad) is `RailTravelArena`.

## Villagers travel by train (#3446)

Lyxette's line should have "a real purpose — more than decoration": villagers ride between rail-connected towns. The
constraint that shapes it: the server simulates NPCs only inside a player's area of interest (`MoveNpcs`, the routine,
the route search — the streaming radius + two chunks), while trains run on the whole occupied world. So a trip is
**virtual out of reach** (timers) and **visible in reach** (walked); riding needs neither (a passenger goes where its
train goes). Server: `GameServerRailTrips.cs`; test seams `GameServerRailTravelSeams.cs`.

- **Places** (`RailPlacesAt`, `GameServerRailBaseReach.cs`): a stop has **up to two** places (#3457). The town's
  (`RailTownAt`): an intercity station's stop → its town (`IntercityStation.SettlementName`); any other stop → the
  settlement whose box holds it or lies within **12** blocks of it (the nearest centre wins). The base's (`RailBaseAt`):
  the nearest player base on this body whose core lies within **96** blocks horizontally (`RailStopBaseReach`) or one of
  whose chained **power relays** lies within **24** (`RailStopRelayReach`), both ±24 up/down. The relay chain is the
  sentries' (`WalkPowerChain`: from the core, every `power_relay` in the 17³ zone cube of the core or a chained relay, at
  most 64), read with no-load block reads (`loadedOnly` — base builds are anchored, #3211) only while the owner is on
  the base's body, cached per base (`BaseRelayCells`) and dropped by the sentries' block hook (`OnSentryChainCellSet` →
  `DropRailRelayChains`: a new relay anywhere drops every chain, a cached relay's cell that changed drops its base's); a
  walk that met an unloaded chunk is walked again after 30 s. So a station beside a village a base surrounds (Lyxette's
  spaceport, ~78 blocks from the core) is the village's **and** the base's. A trip runs between two stops of **different**
  places on the **same line** (no switches, no routing) and never from a stop to itself (`RailTripPairs`; the guest
  routes exclude the base's own stop the same way).
- **Who** (`MayTravelByRail`): a settler of the town with `Job == ""` (no vendor, quartermaster, guard, innkeeper,
  gardener, craftsman or profession), no `CharacterId`, a person key, not a guest or lodge resident, routine on, not
  `VisitingAway`, awake and standing (`Pose` 0), not talking (neither `TalkWith` nor a player's dialogue/hold), not in a
  festival ring, and nobody on the world holds a wish of theirs (`HoldsAWish`: a `WishLane.Giver` — the player would
  look for them at home). Robots of a robot village may go too. #3447: a base's residents without a post
  (`MayTravelAsResident`) ride from the base's stop to a connected town the same way.
- **When** (`PlanRailTrips`, 1 Hz per world): each line has a quiet spell of **0.2 in-game days** (× 0.8–1.2; first sight
  arms 0.3–0.6 of it) — about every five in-game hours; it is due only while a train on it **carries villagers**
  (`CarriesNpcs`: autopilot, a passenger wagon, the switch on — the public train always). Then a random pair (home place,
  destination town) is tried: local day **0.25–0.55** at the home stop, at most **3** travellers per world
  (`MaxRailTripsPerWorld`) and **one per place**; one start per pass.
- **Phases** (`RailTrip`, RAM only in `RailWorldState.Trips`; stepped every tick from `TickTrains`, after the trains
  moved): *ToStation* (to the stop's platform spot) → *Waiting* (the next train halted at that stop that carries
  villagers and has a free seat; in reach the traveller walks to its wagon's door and gets in there — or hops in from up
  to the players' boarding reach in the halt's last 1.5 s; out of reach it boards at once; **300 s** at most, more on a
  long line — see "Timeouts") → *Riding* (until the train halts at the other stop; **900 s** at most, more on a long
  line) → *ToSight* (the destination's festival square, `FestivalAnchorFor`, ±3 blocks — out of reach the layout's middle,
  `TripSquareSpot`) → *Visiting* (**120–240 s**, strolling) → the same way back (`Returning`) → *GoingHome* (to
  `ServerNpc.Rest`) → done: the routine re-thinks (`PhaseCheckedAt = −∞`). While a trip runs the routine, the jobs, the
  chatter, the festival, the guests and the wish offers leave the traveller alone.
- **Walking legs** (`StepTripWalk`): in reach the normal NPC goal and route search (whose unseen fallback helps a stuck
  walker); out of reach a timer of the straight distance at **1 block/s** (3–300 s), then the traveller is placed at the
  leg's end — unless a player there could see it appear (`SpotWatched`, #2680's rule): then it is placed ~28 blocks off
  on the far side, out of sight (`TripApproachSpot`), and walks the last stretch in view. A walking leg in reach that
  takes longer than 120 s + distance/0.4 fails.
- **Timeouts** (`GameServerRailTripReach.cs`, `RailLineTimeouts`; #3447 review — a long line to a far city cut its
  travellers off). A round of a train on its line (`RailTrainTimes`): its length at **60 %** of its setting's top speed
  (8 / 16 / 24 → ~4.8 / 9.6 / 14.4 blocks/s: it eases into every stop and bend), plus a halt at every stop (8 s, the
  public train 30 s); there and back on an open line, one lap on a loop. The platform wait, the ride and a guest's
  boarding window each allow **1.25 rounds** of the slowest train that carries villagers on the line, never less than
  **300 s / 900 s / 180 s** (`RailTripTimeouts`); the ride uses the round of the passenger's own train. The ride is
  measured in rounds, not one way, because a passenger may board a train heading away from its stop (no direction is
  checked) and then rides to the line's end and back. The arena's 80-block line (a round of ~49 s) keeps the plain values;
  a 2000-block line at 8 blocks/s with six stops (a round of ~930 s) waits and rides up to ~1160 s.
- **No terrain loads out of reach** (`GameServerRailTripReach.cs`; #3447 review — the lightweight-server rule). Out of
  every player's area of interest a trip reads no chunk that is not loaded (`RailAreaLoaded`): a stop's platform spot is
  the last one found (however old), else a guess from the line alone (`GuessPlatformSpot`: the cell beside the stop on the
  side away from the line, at the stop's level; never cached); a passenger steps off onto it without looking for the door;
  the square is the layout's middle (`SettlementSquareCentre`). A guessed leg end is looked for properly once it lies in
  reach (`ResolveGuessedLeg`), and a traveller put down out of reach (`RailTrip.Unsettled`) is moved onto real ground
  nearby when it comes into reach (`SettleTraveller`) — at the edge of the area of interest, far from every player. In
  reach everything reads blocks as before.
- **Safety net** (`AbortRailTrip`): every failure — no train in the wait, the train stowed or returned, the stop or the
  line gone (`StopOnLine` each step), a walk that never arrives, a passenger set down (`OnNpcPassengerSetDown`) — ends
  with the traveller home: walking there in reach (the way home itself places it once nobody sees it); out of reach placed
  there at once when nobody watches its home, else by the timed way home, which comes in from out of sight
  (`PlaceAtLegEnd`'s approach spot) — never popped in front of a player. A world not ticked for **20 s** ends every trip
  at once (`EndRailTripAtHome`) — unless a player who just came back would see the traveller vanish or pop in at home:
  then the same way home. The traveller is the villager itself — never a copy — so nobody is lost or doubled; a world load
  regenerates the villagers anyway.
- **The switch** — *Villagers ride along* (`ServerTrain.TakesPassengers`, default on): `NetTrain.TakesPassengers`
  (additive bool, default true so an older server reads "on"), `SetTrainIntent.Passengers` (additive, −1 = unchanged; the
  owner or an ally, as for the other settings; the public train refuses all of them), and the train's save record
  appends it after the facing (`1`/`0`; a record without it takes passengers). Switched off, nobody new boards; anyone
  aboard rides on to their stop. The cab panel shows it as one wide button under halt/autopilot (`TrainCabUi`).
- **Texts.** Nameplates: `npc.activity.train_trip` ("on a train trip") on the way and aboard, `npc.activity.visiting`
  at the other place. In passing (`RailTripHello`, the #2713 idea that neighbours say where they live): aboard
  `npc.hello.passenger` ("Hello, {0}! I'm on my way to {1}."), at the other place `npc.hello.visitor` ("Hello, {0}! I'm
  visiting from {1} — I came by train!"); authored only (no AI re-wording).
- **Cost.** A handful of trip records per world; a stop's platform spot is cached 30 s; route searches only in reach (the
  one-per-tick budget as ever); out of reach no chunk is loaded or generated on the tick thread (see above).

Tests: `RailTripTests` (the places — a stop in a box, within 12 of one, near a base, nowhere; who may go — a post, a
held wish, away visiting a base; the planner — no train for villagers, at night, one per town, at most three; a whole
round trip out of reach on the real train, phase by phase, ending at home; the visible legs — walked to the platform,
boarded, stepped off near the other stop, walked to the square, no unseen move; every failure — a frozen world, no
train for five minutes, the train stowed while riding, the line changed while waiting — nobody lost or doubled; the
switch — the owner's only, on the wire, saved, an older record on; the timeouts — a short line keeps 300/900/180 s, a
2000-block line allows 1.25 rounds; far from the players — a trip to a town whose terrain is unloaded steps off, walks to
the square and visits without loading a chunk there, and a frozen world with a player at the traveller's home brings it
home by the timed leg and a walk-in, never a pop), `IntercityRailTests` (the public train always takes villagers, each
station's stop belongs to its town).

## Guests and residents by train (#3447)

Lyxette's actual wish: "the settlers who visit me should come by train instead of appearing out of nowhere". Server:
`GameServerRailGuests.cs` (the guest script itself is #2602's, `GameServerBaseGuests.cs`; see
[VILLAGE_LIFE.md](VILLAGE_LIFE.md) § Visitors).

- **When a guest comes by train.** A neighbour's visit is due (`TryScheduleBaseGuests`, the roll landed on the
  neighbour) and the base has a **route** (`TrainGuestRoutes`): a line that carries villagers, the base's stop on it (a stop
  the mapping gives the base: within 96 blocks of the core or 24 of a chained relay — a town may claim it too, #3457) and
  a stop of a town (never the base's own stop) at least **150
  blocks** from the core (`TrainGuestMinDistance` — a nearer town keeps the walk-in). Then `TryStartTrainGuest` picks a
  plain villager of a route's town (`MayTravelByRail`) and starts a **`Guest` trip** to the base's stop; the owner reads
  `srv.guest.coming_by_train` ("{name} from {village} is coming to visit you by train!"). The neighbour's weight counts
  when either the walk-in or a train guest is possible (`TrainGuestPossible` — a route, a free town, somebody there who
  may travel and a platform spot at its stop: exactly what the start needs), and a base with a guest on the train counts
  as visited (`TrainGuestUnderway` — not a failed guest trip whose villager walks home, `GoingHome`). When the roll
  counted the train and the start fails after all, the walk-in neighbour comes, else a traveller (`BringBaseGuest`): a
  due window is never spent on nobody. A world without a rail route rolls and draws exactly as before.
- **The traveller is the real villager** (the trip machinery) until the base's stop, then the **guest copy** (#2602's
  pattern): at the halt (`ArriveAsTrainGuest`) — the owner home (`TrainGuestHost`: on foot on this body within 96 + 24
  blocks of the core, within 24 of a chained relay or within 24 of one of the base's stops — `OwnerNearBaseForTrainGuest`,
  #3457: they may wait on the platform —, day 0.25–0.68, nothing hostile, no scouts) — the villager steps off at the
  door, its trip ends, it is sent home and hidden (`VisitingAway`), and `SpawnBaseGuestAt` puts the guest copy (same
  person key, name, face) on the very spot it stepped off; the guest script runs from there (*arriving* → greeting →
  *waiting*). Nobody home (or a guest already there): it stays aboard and rides back (`Returning`). `BaseGuest.ByTrain`
  remembers the line and both stops. This keeps every guest rule of #2602 (the dialogues, the present, the wish hand-over,
  the standings moving onto the guest) and the villager is never hidden longer than the visit: `EndBaseGuest` restores it,
  and also drops the copy from any train (`DropNpcPassenger`).
- **Leaving by train** (`BeginGuestLeavingByTrain`, `StepGuestLeavingByTrain`): the guest walks to the base's platform
  (the owner reads `srv.guest.train_home`), waits for a train halting there with a free seat and boards it (`TryBoardAtHalt`
  — at once within the boarding reach, else at the door). Aboard, the visit ends once no player is within 48 blocks of it,
  or at its town's halt (stepping off and walking into town when somebody is there; nowhere to step off — the visit
  ends). Every step of it has a way out unseen and a time limit (#3447 review: an owner who left while the guest walked to
  the platform kept it walking forever — NPCs only move near a player — and its villager hidden, and the base got no more
  visitors):

  | Step | Nobody near | Too long |
  | --- | --- | --- |
  | walking to the platform | no player within 64 → the visit ends | **120 s** (`TrainGuestToPlatformSeconds`) → walks off on its bearing |
  | waiting on the platform | no player within 64 → the visit ends | its boarding window (**180 s**, more on a long line, from its arrival there) → walks off |
  | aboard | no player within 48 (32 at its town's halt) → the visit ends | the line's ride timeout → set down at the next standstill (a minute later anyway): beside the wagon in view it walks off, else the visit ends |

  Walking off on foot hands it to the ordinary leaving step (its end-of-walk and 120-s hard despawn when unseen). The line
  changed, or the copy set down off its train (`SetDownTrainGuest`), likewise: in view it walks off on its bearing, unseen
  its visit simply ends — never "sent home", since a guest copy's resting spot is where it stepped off at the base's
  platform.
- **Fallback.** A `Guest` trip whose traveller is not aboard within its boarding window after reaching its town's platform
  (`TrainGuestOverdue`: **180 s**, more on a long line), whose walk to the platform fails or has not arrived after **480
  s** (the longest leg out of reach, 300 s, plus a boarding window — a watched platform with nowhere to come in from
  unseen holds an arrival back), or whose line changes while it waits ends (the villager walks home, `FailRailTrip`) and
  today's walk-in neighbour comes instead — else a traveller (`TrainGuestWalksInstead`).
- **Residents** (`MayTravelAsResident`): a base's resident without a post may go on a **`Resident` trip** from the base's
  stop to a connected town and back — the planner's pairs include a base as the home place, within the same caps (three
  per world, one per place). Its way home ends at its `Rest`; the base-life scan leaves a resident on a trip alone.
- **Admin** (cheats on, `GameServerRailTravelAdmin.cs`): `/guest` (`summon_guest`) — a visitor to the admin's nearest own
  base now, by train when a route exists, else the walk-in (neighbour, else traveller; it needs the owner home by day);
  `/railtrip [town]` (`rail_trip`) — a trip now on the nearest line that has two places and a train for villagers (no
  daytime or pace; the caps and who may go still apply). Answers: `srv.admin.guest_train / guest_walk / guest_no_base /
  guest_busy / guest_none`, `srv.admin.railtrip_started / railtrip_none`; help line `ui.admin.help_rail`.
- **Bump snapshot** (`GameServerBumpRails.cs`, key `rails`, only on a resident surface world): the lines nearest the
  reporter (id, pylons, length, closed, intercity, stops with their arc, cell and place), the trains (id, line, owner or
  "public", setting, autopilot, velocity, direction, facing, halted + stop + remaining, wagons, player riders, NPC
  passengers with their wagon and seat, takes passengers), the trips (NPC, kind, phase, returning, home, away, line, train,
  age) and the reporter's nearest base (its stops, its train-guest towns, its guests with `ByTrain` and the leaving step).
  Every list is capped at 8 (stops 16).

Tests: `RailGuestTests` (a far town's neighbour comes by train — the trip, the owner's message, stepping off at the base's
stop as the same person with its villager hidden, walking to the owner, leaving by train on the next halt, gone with the
train and the villager home; the owner leaving while the guest walks back to the platform ends the visit at once (the
reviewer's repro), and a failed guest trip walking home no longer makes `/guest` "busy"; a stop without a platform keeps
the train out of the roll and a train start that fails anyway brings a traveller; a near town keeps the walk-in, no train
in time brings the walk-in, no route without a train for villagers; a resident's round trip; `/railtrip`, `/guest` and
"already on the way"; the snapshot's lines, places, train, passengers, trip and base).

## The intercity line (#2125, terrain generation 19)

Justus suggested abandoned train stations; Marcel's decision first: **no tickets, no ID cards, no vending machines** —
if a world has at least two towns, then with a certain probability a working train line connects two of them, the
station is considered when the towns are generated, and you can ride the train when it is there and waiting. (The
abandoned stations followed a generation later as ruins — see the next section.)

- **When.** Worlds of `WorldDescription.IntercityRailGeneration` (19) or newer, with at least two **eligible** settlements
  (inhabited, tier `town` or `city`, on the ground — villages, hamlets, ruins and sky-island towns do not count), roll
  `ServerConfig.IntercityRailChance` (0.6) once on a lane of their own (`seed ^ StableHash("intercity:" + body)`).
  `ServerConfig.PlaceIntercityRail` switches new decisions off. The city world (one composed city), restricted types
  (`RestrictStructures`: Titas, Valuma), gas giants and airless bodies never get one. The pairs are tried closest first
  (at most six); the first whose route fits wins, a world where none fits simply has no line.
- **Where it runs** (`GameServerIntercityRail.cs`, `StampIntercityRail`, in the stamp chain right after
  `StampSettlement`, before the ruins, camps, factories and everything else). Each station (`RailStationGenerator`,
  20 × 11 × 7, a `SettlementStructure`) stands at its town's edge on the side facing the partner (the dominant axis),
  slid along that edge toward it, its floor on the town's foundation row. The line: the end pylon inside the hall, the
  exit pylon at its far end, a straight 10-block lead, then straight to the partner's lead, a pylon every ≤ 18 blocks.
  Pylon tops stand 2 over the highest ground of their two spans (water at its surface with the ice on top, lava + 3),
  capped by a 1-in-4 grade from both stations and raised where a valley would be steeper. A route fails (the next pair
  is tried) when its free part is longer than 960 blocks, a pylon would be taller than 36 or stand in lava, the corridor
  would cut more than 4000 terrain cells, a station or a span would touch another settlement, a landing pad or the wreck
  site, or — on a world that was already played on — somebody built there.
- **Pinned.** `rail_line`/0 (placed or not; `Composition` = the pylon tops `x,y,z` in line order) and `rail_station`/0,1
  (origin, `GroundY`, seat, `Template` = `heading=N`, `Name` = the town). Every later load replays the records.
- **Stamped once** (feature `intercityrail`): the stations through `StampSettlementBlocks` (carve, foundation, skirt,
  apron; the vegetation carve kept inside the hall so it never reaches into the town's timber), the pylon columns from
  the ground (or the sea bed) up, then the corridor: first exactly the cells `LinkClear` samples, then the wagon's whole
  box swept every half block — only cells that hold something are touched: a tree that reaches into it is cleared as a
  whole (connected trunk, crown and fruit within 6 blocks, 600 per tree, 12 000 per line — then cell by cell), terrain
  and props go cell by cell, fluids stay, a town's blocks are never touched. The boot log reports the pylons, the route,
  the cells cleared and the milliseconds (0.4–2 s on the probed worlds).
- **Protected.** `IsSettlementBlock` covers `IsIntercityRailBlock` (the station halls with their plinth, every
  generated pylon column), so mining (`@srv.protect.rail`), blasts, the pump, the drills and fire all leave it alone. The
  later stampers keep clear of the stations and of a box around every span (`AppendIntercityReservations`,
  `OverlapsAnySettlement`).
- **The runtime.** After `LoadRails`, `EnsureIntercityGraph` registers each station's stop as an owner-less Crystal-Net
  `RailStop` device and writes the generated nodes and links when missing; after `RebuildRailLines`,
  `EnsureIntercityTrain` puts the **public train** on the line when it has none: `OwnerId` empty
  (`RailRules.PublicOwnerId` — no player id is ever empty, so no wire or save change), the cab and a `wagon_seats`,
  speed 2 (top speed 16 blocks/s, #3444), autopilot, facing −1 (its passenger wagon toward the hall's exit), halted at the lower-arc
  station. It shuttles (the open line reverses at both ends — at each station it turns round while it stands, the stop
  being four blocks from the line's end; the cab leads into one station, the passenger wagon into the other) and
  halts at each stop for `RailRules.PublicStopHaltSeconds` (30 s; players' trains keep `StopHaltSeconds`). Anyone may
  board it; `SetTrainIntent`, `StowTrainIntent`, coupling a wagon, placing a cab on the line and the linker on a
  generated pylon are refused with `@srv.rail.public_line`; a signal or a press on its stop does not depart it; a stop a
  player sets beside the line does not join it (only the two stations' stops do); a pylon stacked on a generated one is
  a node of its own. If its line ever vanished it would simply be removed (it returns to
  nobody's pack). The graph and the train persist with every other line (`SaveRails` / `LoadRails`).
- **The map.** Each station is a `rail_station` POI named `poi.rail_station` ("{0} Station" / "Bahnhof {0}"); the client
  draws it with the station icon in amber (`WorldMap.PoiLook`).

Tests: `IntercityRailTests` (the line between two towns with both stops on it, a clear corridor and both stations on
the map; the public train shuttling and waiting 30 s at each station, a stranger riding it, every control refused, the
pylons and the station protected; the same line for the same seed and everything back after a restart; no line on
generation 18, at chance 0, on an airless, a restricted or the city world; the station layout).

## Abandoned stations (#2166, terrain generation 20)

Justus' original idea, built as a ruin: on some worlds the hall of an old station stands out in the open country, long
after the power died. Marcel's rail rules still hold — no tickets, no ID cards, no vending machines.

- **What it is** (`RailRuinGenerator`, 32 × 11 × 7, a `SettlementStructure`). The working station's ground plan —
  `RailStationGenerator.LayHall` is shared, so both halls are one design — run through a decay pass: a stretch of the
  roof caved in across the whole width (always beyond the wagon), more holes, most of the skylight broken, the rest half
  rusted; some posts snapped (the broken piece lies beside them); the lamps down (scrap under some of them); the
  platform edges dark; some benches gone; a cracked floor (moss stone, bare ground); rubble under the gaps and the
  biome's plants pushing through (`SettlementGenerator.BiomeFloraKey`). On the track bed sits a **derelict wagon** built
  of blocks — the wagon the client draws (`TrainView.WagonCells`) one row up: floor, sill, two rows of mostly broken
  windows, a holed roof with a bush on it, open door gaps and ends, two benches by the windows on each side. Beyond the
  exit the old embankment runs on for 12 blocks with two **pylon stumps**, one standing with a dead head
  (`broken_machine`), one toppled across the bed. Nothing glows and nothing is a working rail part: no lamp, strip
  light, `rail_pylon` or `rail_stop` survives (the pylon heads are `broken_machine`, the stop plate rusted over), so no
  linker mistakes the ruin for a line.
- **When.** Worlds of `WorldDescription.RailRuinGeneration` (20) or newer that could once have had towns: not void, not
  a gas world, not airless, no structure whitelist. At most one per world: `ServerConfig.RailRuinChance` (0.35) scaled
  by the structures-frequency option, rolled once on a lane of its own (`LaneRoll(seed, "railruin:" + body)` — the
  same SplitMix finaliser as the intercity line's roll, which now calls it too). `ServerConfig.PlaceRailRuins` switches
  new decisions off.
- **Where** (`GameServerRailRuins.cs`, `StampRailRuins`, in the stamp chain right after `StampRuins`). The heading is
  drawn from the instance seed, the spot by the guaranteed search with the factory seat policy (dry land, no stilts, no
  lava), clear of the pads, the wreck site, every settlement, the intercity line and the ruins stamped during the same
  load (`LoadedWorld.RuinFootprints`). Every later stamper keeps clear of it (`AppendRailRuinReservations` in the bandit
  camps, factories and monuments; `OverlapsRailRuin` inside `OverlapsAnySettlement` for the wreck, vaults, data cubes,
  chests and unique sites).
- **Pinned and stamped once.** `rail_ruin`/0 (placed or a skip; `Template` = `heading=N`); feature `railruins`. Like
  every ruin it is **not protected** — plain terrain once stamped, freely mineable, a mined wall stays mined.
- **Salvage and lore.** Two `rail_cache` markers (one in the wagon's aisle, one on a platform beside a stack of crates)
  become one-time `salvage` containers (`SpawnStructureLoot("rail_ruin", …)`): cable, copper wire, iron plates,
  energy cells, circuit boards — half the time 2–4 salvaged `rail_pylon`s, a quarter of the time a `rail_stop`, enough
  to start a line of one's own. Their ids (`loot_rail_ruin_…`) make them the lore site `rail_ruin` ("Station notice"):
  a faded timetable, a lost-and-found tag, the driver's last log (`data/stories/vega_protocol/lore_sites.json`).
- **Finding it.** Not on the map — discovery content like the ruins. VEGA's "ruins nearby" tip names it within 120
  blocks (`poi.rail_ruin`, "Abandoned station" / "Verlassener Bahnhof"), and an admin jumps there with `/tp railruin`
  (a platform spot in the middle of the hall).

Tests: `RailRuinTests` (the generator deterministic per seed and heading, the caved-in roof, the wagon, the dead line,
no glowing or working rail block, two caches on standable cells; one station on a generation-20 world — recorded,
stamped, two caches with the station's lore voice, clear of the settlements, reachable with `/tp railruin`, mineable,
the same for the same seed and back after a restart without a second stamp; none on generation 19, at chance 0, when
switched off, on airless, gas or restricted worlds; the roll uniform and on its own lane; the texts in all 14 locales).

## Not in this package (honest scope)

Doors that open and close (the wagon sides have permanent openings), wagon meshes beyond the three authored kinds,
forks/switches (a pylon carries two links), a driver's manual throttle beyond the three top speeds, standing
passengers on the speeder (the frame infrastructure is there: the speeder would send its own pose as a frame). Villagers
(#3446/#3447) never change trains or lines (a trip is two stops of one line), never ride a driven train (only the autopilot
halts at stops), and out of every player's reach their walking legs are timers, not walks.

Tests: `RailTests` (auto-link, the bend and the obstacle, the linker's loop and uncouple, the two-link cap, the spline
across the seam and the frame's round trip, the cab and the wagons, running and reversing, speed and halt, stow, the
stop and the signal, two riders — one walking, one seated — riding along, walking out, leaving; a reversing train
carrying a rider on the rear bench and a walker in the cab without a jump, coupling at the line's start; #3444: a new
cab at setting 2, a 35-block hop peaking under 8 and a 230-block leg reaching 16, no speed jump, rolling onto both
stops at a walking pace without moving back, the cab's "go" not caught again by its stop; the dealer, the kit and the
locales), `RailAutoLinkTests` (#3256: a straight row across the X and the Z seam, the spline over the
Z seam, the fallback's ranking, extending the other end and filling a gap, a re-placed pylon after mining, a parallel
line's nearer end not stolen, "branch" before "too sharp", the linker hint but VEGA's welcome on the first pylon),
`RailRulesTrainMotionTests` (`Advance` / `CabRange` / `LeadArc`, pure), `RailMotionTests` (#3444, pure: the top speeds,
the ease-in, the cruise by leg, braking onto the target at a walking pace, a lowered limit without a jolt, a loop
without stops at the top speed, `Gap`, the bend limits on a circle and before a sharp corner) and
`TrainDrawArcTests` (Client.Tests: the drawn arc bounces with the server, never jumps; #3444: the predicted speed is the
server's velocity or an older server's fixed table, and the drawing follows an easing, braking server without a jump).
