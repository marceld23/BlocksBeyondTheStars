# The monorail hover train (#2113)

Justus' idea (2026-09-27), Marcel's decisions: pylons the player places, an **energy line** the game spawns between
linked pylons (data, not blocks), a train of authored wagons that hovers along it, and — the game's first **moving
frame** — riders who **walk inside the train while it moves**. Everything the server and every client must agree on is
Unity-free in `Shared`.

## Shared

- `Shared/Definitions/RailRules.cs` — the constants (auto-link range 32 / 30°, linker range 48, two links per pylon, 64
  pylons per line, hover height 0.5, the wagon box 6 × 3 × 3.2, the speed table 4 / 8 / 12 blocks/s, the stop halt 8 s,
  boarding reach 4, stow reach 5), `AutoLinkFits`, the seat offsets per wagon kind, `ClampLocal` / `OutsideWagon`
  (the rider's box), `WagonArc`, `LocalToWorld` / `WorldToLocal` (the frame: local X across, Y up from the floor, Z toward
  the cab; heading = radians about +Y, forward (cos, 0, sin), right (sin, 0, −cos)) and `FrameId` (`train:wagon`).
- `Shared/Definitions/RailSpline.cs` — a Catmull-Rom spline through the pylon tops with an arc-length table, the
  `SandwormPath` idea in 3D: unwrapped along X relative to the first point so a line over the world seam is one curve,
  wrapped back on the way out; open (a path, the train reverses at the ends) or closed (a loop); `PointAt`, `YawAt`,
  `PoseAt` (the wagon centre + heading), `Nearest`, `NormalizeArc`. The wire carries only the points; both sides build
  the same curve.

## Wire (`Networking/Messages/RailMessages.cs`, ids 270–275)

`RailList` (every line: id, points, closed, stop arcs) on join and on every change; `TrainList` (id, owner, line, arc,
speed, direction, autopilot, halted + remaining, wagons, riders) at 5 Hz while a train moves and at once on every
change; `EnterTrainIntent` (train, wagon, seat −1 = standing), `ExitTrainIntent`, `SetTrainIntent` (speed / halt /
autopilot, −1 = unchanged), `StowTrainIntent`. Additive fields: `MoveIntent.FrameId` (set → X/Y/Z are the wagon-local
offset), `PlayerPresence.FrameId/LocalX/Y/Z`, `PlayerStateUpdate.InTrain/TrainSeat`, `PlayerState.InTrain/TrainSeat/
TrainLocalX/Y/Z` (never a persisted bond: a join clears it).

## Server (`GameServerRails.cs`)

- **Graph.** `RailWorldState` on `LoadedWorld`: nodes keyed by the pylon's top cell (stacking re-keys the node upward,
  mining the top drops it down), links as data. `OnRailBlockPlaced` (after the Crystal Net hook in `HandlePlaceBlock`)
  auto-links to the builder's previous pylon (`LastPylon`) when `LinkRefusal` is null: range, the bend rule against the
  previous link's direction, the two-link cap, the 64-pylon cap, and the **clearance check** `LinkClear` — the wagon
  box (three columns across, four cells up from the hover height) swept one block at a time along the straight run
  between the two tops; any solid, non-liquid, non-flora block that is not a pylon or a stop refuses ("Obstacle").
  `UseRailLinker` (the `rail_linker` gadget: first use picks A, second couples A–B or uncouples them). `RebuildRailLines`
  walks every component from an end (a loop from its first pylon), keeps a line's id while its first pylon stands,
  builds the spline, and lands every `rail_stop` device within `StopReach` of a line on it. The graph and the trains
  persist as strings in `WorldMetadata.RailGraphs` / `Trains` (`SaveRails` / `LoadRails`, per body).
- **Trains.** `UseRailCab` puts a train on the line nearest the aim (refused where another train stands within its
  length); `UseRailWagon` couples behind the tail; `HandleStowTrain` returns the items (owner, nobody else aboard, in
  reach); a train whose line vanished comes back to its owner (`ReturnTrain`). `TickTrains` (guarded in the main tick
  after the giants): autopilot or a rider in the cab runs it by the speed table; an open line reverses the direction
  at both ends and re-seats the arc so the wagons stay put; a loop wraps; the autopilot halts at a stop the cab crossed
  this tick (`Crossed`, wrap-aware) for `StopHaltSeconds`, once per stop until it is clear of it;
  `DepartTrainsAtStop` (the Crystal-Net rising edge on the `RailStop` device, or the owner's press on the stop — the
  machines' manual start) ends the halt early.
- **The moving frame.** `HandleEnterTrain` (owner or ally, within reach of the wagon) records the rider (`RailRider`:
  wagon, seat, local offset — standing where they were, brought into the box; seated on the seat), sets
  `InTrain`/`Seated` and derives the world position. `HandleFramedMove` (called first in `HandleMove`): a `MoveIntent`
  with the rider's frame stores the clamped offset and derives `Position` from `WagonPose` + offset; a seated rider's
  offset is fixed; an offset outside the box (`OutsideWagon`) is **leaving** — on foot where they stepped off; a world-
  frame report while aboard is a client that left on its own. Every tick the server re-derives every rider's world
  position from the wagon pose it drives, so streaming, creatures, air, pickups and everything else keep their world
  coordinates. `HandleFallDamage` ignores a rider. Death and a join clear the bond (`LeaveTrainSilently`); leaving by
  the door sets the rider down beside the wagon (`LeaveTrain`). The presence carries the frame and the offset.
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
  Every frame each wagon is posed on the spline: the server's arc run on by speed and direction since the last update
  and eased toward each new one (`DrawArc`), the wagons behind the cab by `WagonArc`. **The wagon transform is the
  frame**: local X/Y/Z are exactly the rules' local offsets (`Quaternion.LookRotation((cos, 0, sin))` puts local +Z on
  the heading and local +X on its right). `TrainView` runs early in the frame (`DefaultExecutionOrder(-40)`), so the
  wagons are posed before the player controller moves a rider; `WagonAt` answers which wagon's box holds a point.
- `PlayerController` — while `Game.InTrain` names a frame the controller parents itself to that wagon transform
  (`UpdateTrainFrame`): the platform's motion and yaw carry the body and the camera, the normal on-foot movement runs
  on top (walking, jumping, gravity onto the wagon's floor collider), `SendMovement` reports the **wagon-local** offset
  with the frame id at 10 Hz (`SendFramedMove`) and treats an offset outside the box as leaving; a seated rider is
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
- `TrainCabUi` — the panel (speed 1–3, halt / go, autopilot, leave, pack up).

## Not in this package (honest scope)

Doors that open and close (the wagon sides have permanent openings), wagon meshes beyond the three authored kinds,
forks/switches (a pylon carries two links), a driver's manual throttle beyond the three speed settings, standing
passengers on the speeder (the frame infrastructure is there: the speeder would send its own pose as a frame).

Tests: `RailTests` (auto-link, the bend and the obstacle, the linker's loop and uncouple, the two-link cap, the spline
across the seam and the frame's round trip, the cab and the wagons, running and reversing, speed and halt, stow, the
stop and the signal, two riders — one walking, one seated — riding along, walking out, leaving; the dealer, the kit and
the locales).
