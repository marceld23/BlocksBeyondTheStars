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
- `TrainCabUi` — the panel (speed 1–3, halt / go, autopilot, leave, pack up). A public train (empty owner) shows the
  "Intercity line" note and only the leave button.

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
  speed 2 (8 blocks/s), autopilot, halted at the lower-arc station. It shuttles (the open line reverses at both ends) and
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
forks/switches (a pylon carries two links), a driver's manual throttle beyond the three speed settings, standing
passengers on the speeder (the frame infrastructure is there: the speeder would send its own pose as a frame).

Tests: `RailTests` (auto-link, the bend and the obstacle, the linker's loop and uncouple, the two-link cap, the spline
across the seam and the frame's round trip, the cab and the wagons, running and reversing, speed and halt, stow, the
stop and the signal, two riders — one walking, one seated — riding along, walking out, leaving; the dealer, the kit and
the locales).
