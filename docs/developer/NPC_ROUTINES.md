# Living NPCs — residents, routines, pathfinding, jobs

Epic #1851 (issues #1865–#1869). Every settlement NPC, station crew member and base resident used to stand on one
point with a 1.6-block leash. They now live a day: work, an evening seat, a bed at night, a route between them,
and — at a player's base — a job. This page is the map of that code.

## 1. Local time (`GameServerWeather.cs`)

`LocalDayFraction(pos)` mirrors the client's `GameBootstrap.LocalTimeOfDay`: the world clock plus
`X / Circumference`. A void world (station deck, ship cabin) has no longitude — the world clock is the local clock.
`IsNightAt` / `IsDawnOrDuskAt` use the client's sunrise/sunset (0.25 / 0.75). Everything that reacts to night asks
the position: creature activity (`SpeciesActive(species, pos)`), VEGA's night tips, the NPC routine.

Test seams: `LocalDayFractionForTest(x)`, `SetLocalDayFractionForTest(fraction, x)` — pin the local clock at the
place under test instead of the world clock.

## 2. Base index (`GameServerBaseIndex.cs`)

One cached `BaseIndex` per base: bed heads, seats (chair/bench shapes), trading posts, mission boards, crates, workbenches,
forges, crops, hydro trays, saplings, sentry posts.

- **Read from the block-edit store**, not the voxels: `IWorldRepository.ListBlockEditsMatching(planet, box, blocks,
  shapeIndices, limit)` (Sqlite, PostgreSQL, Memory). Every indexed thing was placed by a player, so one filtered query
  replaces a voxel scan of up to 193 × 65 × 193 cells. The box is the base's `BaseWallReach`, capped at ±96 × ±32.
- **Inside only** (`BaseCellInside`): the 17³ core zone; or, beside the thing on its floor level, the walled yard
  (`InWalledBaseArea`); or, from the air above it, a sealed base room (`InSealedBaseRoom`) or a closed room
  (`InClosedRoom`: a 6-connected flood that stops at walls, the roof and every door, ≤ 4096 cells, ≤ 24 blocks).
- A bed counts once: the head (or a legacy one-cell bed), never the foot.
- Dirty on any `BlockSet` within the base's reach (`MarkBaseIndexDirty`), rebuilt lazily by the base-life scan,
  dropped with the base (`ForgetBaseIndex`). RAM only.

## 3. Residents (`GameServerBaseLife.cs`)

- `_baseResidents[baseId]` = slots with their NPC ids on the base's world. Desired count =
  `min(5, 1 + beds)` once the first settler earned the base (≥ 3 machines); a founded base keeps its settler.
- Slot 0 keeps the founding settler's seed and memory key (`base_<id>:settler`); slot *n* is `base_<id>#<n>:settler`
  (`BaseResidentKey`). The key is the **person**; the relationship's `Role` follows the job.
- `AssignBaseResidents`: jobs in priority order (vendor, quartermaster, guard, gardener, craftsman), beds and seats in
  the index's order, a resting spot beside the bed (`BedSideSpot`) or near the core (`ResidentHomeNear`), the work
  spot (`ResidentWorkSpot`), and the post markers (`RefreshBaseMarkers`).

## 4. Posts at home (`GameServerBasePosts.cs`)

`WorldState.BaseMarkers` holds `(BaseId, "vendor"|"mission_board", pos)` for posts that have a resident with that job.
`NearBaseVendor` joins `MarketAvailable` and `VendorThemeAt`; `NearBaseMissionBoard` gates `home_<hash>_…`
missions (`IsBaseMission`, coined by `StockBoard` / `EnsureBaseWindow` with the core cell as the board key —
base ids are load order). Placing or mining a post rescans its base at once (`OnBasePostChanged`) and tells the
builder why a post is not staffed (`srv.base.post_*`).

## 5. Pathfinding (`NpcGridPath.cs` in Shared, `GameServerNpcPathing.cs`)

- `NpcGridPath.Find`: A* over feet cells (standable = floor + two free cells), four horizontal moves with dy ∈
  {0, +1, −1, −2}, doors a little dearer, goal reached beside it; box ±48 × ±8, 3000 expansions. Pure and
  unit-tested (`NpcGridPathTests`).
- `SetNpcGoal` → `RequestNpcPath` queues the NPC; `TickNpcPaths` runs **one** search per tick on `GetBlockIfLoaded`
  reads. On a player station a node must be in a sealed pocket or a doorway (the crew never routes through vacuum).
- `MoveNpcs` → `GoalStep`: follows waypoints (`Seek`, arrival radius 0.45, goal 0.4), re-routes after 4 s without
  progress, and after 3 failed routes calls `TeleportNpcToGoalIfUnseen` (no player within 24 blocks of the NPC or
  the goal). Walkers skip NPC separation (no doorway shoving).
- Doors: `TickDoors` adds walking NPCs to the slide/energy door targets; a walker blocked by a shut hinge/wood door
  calls `OpenDoorForNpc`, which sets `ServerDoor.NpcHeldUntil` — the door closes once that passed and nobody stands
  in the gap. A player's toggle clears the hold.
- `MarkNpcPathsDirty` drops routes next to a changed block.

## 6. The routine (`GameServerNpcRoutine.cs`)

`TickNpcRoutine` (before the path search) looks at each `RoutineEnabled` NPC in a player's area of interest every
2 s. Phases by local time: night ≥ 0.78 or < 0.22, evening ≥ 0.68, else day. The guard inverts it (on duty evening
and night, asleep by day).

| Phase | Goal | On arrival |
|---|---|---|
| Day | `Work` (a post, bench, garden, patrol — or the spawn spot) | idle with the work leash |
| Evening | the spot in front of `Seat` | `SitDown` → `Pose = 1` |
| Night | the spot beside `Bed` | `LieDown` → `Pose = 2` (position mid-bed, facing foot → head) |

No seat / no bed → the resting spot, `ActivityKey = npc.activity.resting`. `StandUp` puts the NPC back on the
approach spot. Villagers and crew find a bed and a seat once within 8 blocks of their marker
(`EnsureNpcFurniture`); guardians and visiting traders keep `RoutineEnabled = false`. A sleeper answers a talk with
`npc.greet.sleepy`.

Wire (additive, contractless — no codec tag): `NetNpc.Pose`, `NetNpc.ActivityKey`, `NetNpc.Held`.

## 7. Jobs (`GameServerNpcJobs.cs`)

- **Gardener**: sites = crops, saplings, the air over trays (one per column, ≤ 24); 20 s per site; a standing crop is
  harvested every 90 s per base into the first crate that takes the drops (`NpcDepositToContainer` — consumables
  allowed, filters and wood-box slots respected), regrowth via `ScheduleFloraRegrow`; saplings lose 30 s of growth
  time per visit; harvest toast at most every 10 min.
- **Craftsman**: at the bench every 300 s — forge + ≥ 2 iron ore in a crate → 1 ingot; else 2 plant fibre.
- **Guard**: patrol = the standable yard cells within three wall cells of the outside (`ComputeGuardPatrol`, cached
  per index rebuild, ≤ 24 waypoints; a sentry post without walls gets a square round at radius 6). Every 2 s it
  looks for scouts of its base or a robber approaching within 40 of the core (24-block range, no line of sight —
  they stand right outside the wall); one radio warning per 5 min per base (`npc.call.guard_scouts`, respects
  "NPC calls off"); within 8 blocks it calls `BeginBanditLeave`. Fighting bandits are ignored.

## 8. Client (`NpcView.cs`, `PlayerAvatar.cs`, `HeldItem.cs`, `Sky.cs`, `HudUi.cs`)

- Pose 1: `SetSeated`, lowered 0.45 like a seated remote player. Pose 2: `SetLying`, root pitched onto its back with
  the head toward the headboard, lifted onto the mattress, a "z z z" above. No gestures while seated or asleep.
- `HeldItem.ForNpc`: hoe, hammer, blade meshes; the gesture cadence follows the tool.
- Nameplate: `Name · role · stage · activity`.
- `Sky.ApplyLighting`: on a boarded station the fill light, ambient and interior fill follow the station clock
  (floor 0.45, smoothed); emissive strip lights stay bright. `LocalTimeOfDay` drops the longitude aboard a station.
- `HudUi`: *"Talk to … (E)"* beside an NPC within 4.5 m.

## Tests

`NpcGridPathTests`, `BaseResidentsTests`, `NpcRoutineTests`, `NpcJobsTests`, the station-night case in
`PlayerStationReportsTests`, the village-bed case in `SettlementNpcTests` (Slow). The shared arena is `NpcLifeWorld`.
