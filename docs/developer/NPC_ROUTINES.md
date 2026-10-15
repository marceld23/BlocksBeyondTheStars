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
- **Furniture is no floor, a rug is no wall** (`NpcFooting.cs` in Shared, `GameServerNpcFooting.cs`, #1895). The walk
  reads each colliding cell's form as well as its block. `NpcFootings.Of(key, descriptor)` returns one of four footings:
  - `NoFloor` — the table, chair, bench, bed-half, fence and pot forms on any material, the furniture and device keys
    in `NoFloorBlocks`, and a plate hung on a wall. A wall to the body, never under the feet.
  - `FloorPlate` — a sheet or panel lying on the floor. Walked through; carries the feet in its own cell.
  - `CeilingPlate` — a hung plate. Walked through.
  - `Floor` — everything else, slabs, stairs, the hydroponics tray, cores and pipes included.

  The route (`NpcStandableAt`, `Free`) and the stroll probe (`TryNpcGroundFeetYAt`) read without loading
  (`ServerWorld.GetShapeIfLoaded`). The movement sweep (`BlockedByWorld`) and `StandableSpot` use `NpcBodyBlockedAt`.
  Creatures, bandits and enemies keep `StandableAt`. `SeatApproach` tries the chair's sides after its front: the
  furnisher turns every backrest away from the table, so the front is the table. Tests: `NpcFootingTests`.

## 6. The routine (`GameServerNpcRoutine.cs`)

`TickNpcRoutine` (before the path search) looks at each `RoutineEnabled` NPC in a player's area of interest every
2 s. Phases by local time: night ≥ 0.78 or < 0.22, evening ≥ 0.68, else day. The guard inverts it (on duty evening
and night, asleep by day).

| Phase | Goal | On arrival |
|---|---|---|
| Day | `Work` (a post, bench, garden, patrol — or the spawn spot) | idle with the work leash |
| Evening | the spot in front of `Seat` | `SitDown` → `Pose = 1` |
| Night | the spot beside `Bed` | `LieDown` → `Pose = 2` (position mid-bed, facing foot → head) |
| Festival (#2604, §15) | the attendee's ring spot around the village square | stand still, face the square |

No seat / no bed → the resting spot, `ActivityKey = npc.activity.resting`. `StandUp` puts the NPC back on the
approach spot. Villagers and crew find a bed and a seat once within 8 blocks of their marker
(`EnsureNpcFurniture`); guardians and visiting traders keep `RoutineEnabled = false`. A sleeper answers a talk with
`npc.greet.sleepy`. While a player talks with an NPC (§16) its routine and job wait — at most 60 s per talk.

**Keepers hold the counter (#2627).** A trading keeper (role `vendor` — the classic vendor, every trading profession,
a visiting pilot, the travelling merchant — or the `quartermaster`) at work stops strolling while a player stands within
`KeeperHoldRange` (4.5) of its spot: it walks back to the spot (`KeeperHoldSlack` 0.35) and stands there facing the
customer (`KeeperCustomer` in `GameServerVendorTrade.cs`, used by `MoveNpcs`). "At work" = idle (no goal, standing) in
the `Day` phase, or any time for an NPC without a routine (the visitors). The work leash (1.0) let a doctor drift ~1.4
blocks from his post while the player was trading. Evening, night, a walk and every non-trader are unchanged.

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
  they stand right outside the wall); one warning per 5 min per base, respects "NPC calls off" — a ☎ radio call
  (`npc.call.guard_scouts`) when the owner's worn radio reaches the base, else through VEGA (`vega.news.guard`, §12);
  within 8 blocks it calls `BeginBanditLeave`. Fighting bandits are ignored.

## 8. Client (`NpcView.cs`, `PlayerAvatar.cs`, `HeldItem.cs`, `Sky.cs`, `HudUi.cs`)

- Pose 1: `SetSeated`, lowered 0.45 like a seated remote player. Pose 2: `SetLying`, root pitched onto its back with
  the head toward the headboard, lifted onto the mattress, a "z z z" above. No gestures while seated or asleep.
- `HeldItem.ForNpc`: hoe, hammer, blade meshes; the gesture cadence follows the tool.
- Nameplate: `Name · role · stage · activity`.
- `Sky.ApplyLighting`: on a boarded station the fill light, ambient and interior fill follow the station clock
  (floor 0.45, smoothed); emissive strip lights stay bright. `LocalTimeOfDay` drops the longitude aboard a station.
- `HudUi`: *"Talk to … (E)"* beside an NPC within 4.5 m.

## 9. Kit station crew (`GameServerStationCrew.cs`, #1874)

A station composed from a kit (`BoardableStation.Kit != null`) spawns **one resident per `cabin` marker** instead of
the post keepers + filler crew: the posts (vendor, mission board, greenhouse, hangar, medbay, canteen/bar `lounge`) are
staffed by residents in marker order; the rest stroll the arrival hall by day. Each resident starts AT its post
(`Home = Work`), rests at its cabin spot (`Rest`), finds its own bed within `CabinFurnitureReach` (4) via
`EnsureNpcFurniture(npc, reach)` and takes an evening seat from the pool of chairs and benches around the `lounge`
markers (`LoungeSeats`, round-robin). The routine (§6) then walks them post → lounge → bed. Path searches on void worlds
use `StationPathLimits (96, 16, 8000)` so a cabin two decks away is reachable. Test: `StationKitServerTests`.

## 10. Settlement residents per bed (`GameServerSettlementResidents.cs`, #1887)

`SpawnSettlementNpcs` calls `SpawnSettlementResidents` per inhabited settlement. The bed heads of the stamped layout
(`SettlementInstance.Layout`, local → world) are the residents, capped by `ResidentCap(tier)` (hamlet 6, village 10,
town 20, city 32, metropolis 80). Posts come from markers in the order vendor → `mission_board` → `greenhouse` →
`workshop` → `tavern`; resident *i* staffs post *i* (`Job` vendor / quartermaster / gardener / craftsman / innkeeper),
spawns at it (`Work`) and takes the free bed nearest to it (`Bed`, `FurnitureScanned = true`). Residents without a post
take the remaining beds in scan order and spawn at the `npc` spot nearest to their bed. Evening seats: chairs within 8 of
a `tavern` marker (round-robin, from the layout), else the nearest chair within 6 of the bed. The vendor and quartermaster
are staffed even without a bed (`count = max(min(beds, cap), services)`); `guard_post` markers still spawn guardians
(robots, no routine). Tests: `SettlementResidentTests`, `SettlementNpcTests`.

## 11. Professions (`NpcProfessions` in Shared, `GameServerProfessions.cs`, 2026-09)

Eight professions from Justus' ideas, one table (`NpcProfessions.All`, append-only — its order is the staffing order):
**doctor** (theme `medics`), **grocer** (`grocer`, shop-only), **arms_dealer** (`arms`), **sage** (`sage`), **tamer**
(`tamer`, keeps a pet), **blockfarmer** (`blocks`, works outside) trade — Role `vendor` with their own `Job` + `Theme`,
so `NearVendor`, the trade-or-talk prompt, `VendorThemeAt` and the memory keys work unchanged; **streamer** and
**reporter** are settlers. Each row names the post marker (= job), the post block, the settlement building function and
the furnished room (`RoomFurnisher.RoomRole` name).

- **Where they come from.** Settlements: profession buildings (`clinic, shop, armory, library, stable, quarry, studio,
  newsroom`, optional in every modular kit) carry the marker; `SpawnProfessionResidents` adds one resident per marker AFTER
  the bed-bound residents, each with its own seeded generator (`profession:<settlement>:<index>`), so settlements without
  these markers spawn exactly as before. Author templates / station templates: the marker in the editor palette; kit crew
  and generated station crew staff it (`ApplyProfession`, no `vendorIndex` step). Bases: `BaseIndex.ProfessionPosts` from
  the post blocks → jobs right after vendor/quartermaster; `RefreshBaseMarkers` adds one marker per staffed post. Player
  stations: `RegisterPlayerStationPlaceables` turns post blocks into markers (staffed in sealed rooms, §9 air rule).
- **Generated stations (2026-09, Marcel).** Every station kit offers six optional profession rooms (`st_<tier>_clinic`,
  `_shop`, `_armory`, `_library`, `_studio`, `_newsroom` — `tools/gen_station_modules.py`; no tamer, no blockfarmer in
  space): the shop room with the post block, the post marker and a counter, and behind a partition the keeper's own cabin.
  The extra cabin keeps posts ≤ cabins; `SpawnKitStationCrew` staffs profession posts before the settler posts (stable
  order, so a station without them keeps its crew). `StationKitComposer.TryPlace` docks a module with a `cabin` marker on
  the hall deck only (the crew cannot climb ladders). A profession room is furnished like its settlement building
  (`RoleFor` via `NpcProfessions.ByFunction`), the cabin behind it as a cabin. Pinned stations replay their composition.
- **The G.D.S. city (Glutweite).** Two housing-role districts `gds_services_1` (clinic, shop, armory) and `gds_services_2`
  (library, studio, newsroom) — town profession houses in the city's colours plus two flats — optional once each, weight
  3, so a fresh city practically always has all six; residents come from the shared settlement path. A pinned city keeps
  its districts.
- **Trade gates.** `NpcProfessions.IsTradeMarker` (settlement `NearTradeMarker`, `NearSpaceStationVendor`,
  `NearBaseVendor`), plus — #2626 — any vendor within `MarketStock.VendorReach` (6) of the player (`NearVendorNpc`). A
  themed deal needs a vendor within that reach that sells it (`ThemedMarketRefusal`, GameServerVendorTrade.cs) — ANY such
  vendor, nearest first, not just the nearest one, so the vendor the client's trade screen is bound to counts even with
  another one a step closer. The grocer's offers need `InSameClosedRoom(player, keeper)` (`srv.craft.shop_only`); a recipe
  with `marketRotation` N is in stock on every N-th in-game day (`RecipeDefinition.OfferedOnDay(floor(SystemTimeDays))`,
  `srv.craft.not_today`). The stock rule is `MarketStock` (Shared): the client's vendor screen lists
  `MarketStock.ListFor(theme, day)` — the vendor's own goods first, then the themeless deals — and the server checks
  `MarketStock.SellsKind`, so both read one rule.
- **The client's reach (#2626, `VendorReach` in Client.Core).** The "Trade" prompt, E's trade-or-talk and the crafting
  menu's market page pick the nearest vendor within `MarketStock.TradeReach` (4) — the server always accepts that. "Trade"
  binds the screen to that vendor (its NPC id): title "Name · Role", a header line `ui.vendor.theme.<theme>` (#2629), and
  the list stays while the vendor is within `MarketStock.VendorReach` (6) — exactly the server's vendor reach — else
  "Step closer to … to trade" (`ui.vendor.step_closer`). The client knows no post positions, so it measures to the vendor
  alone; where the server would also take a trade from a post (vendor 4–6 away, player at the post) the screen still
  lists it.
- **Visitors bring their own goods (#2630).** A docked trader draws its theme per visit from `VisitingTraderThemes`
  (traders, miners, researchers, settlers, arms, medics, blocks — never the station shop's `traders`); a landed pilot the
  same per landing (any of them, no shop beside a pad). The travelling merchant at a base has theme `mixed`
  (`MarketStock.MixedTheme`): one offer from each of 4 trades, drawn per in-game day by a stable hash
  (`MarketStock.MixedOffers`) — the same pick on both sides, no protocol change (`NetNpc.Theme` carries `mixed`).
- **Dialogues.** `DialogDefinition.Job`: a job dialogue matches only that job; a profession NPC never takes a plain role
  dialogue. Consequences `photo` (the streamer poses — `npc.activity.posing` — and `NpcDialogState.Action = "photo"` makes
  the client take a HUD-free photo), `photo_never` (milestone `streamer:never`), `interview` (`Action = "interview"`, the
  client's `InterviewUi`; `session.PendingInterviewNpcId`), `news` (the closing line lists the place's latest articles),
  `treat` (#2628, the doctor's "Can you treat me?": full health, free, once per in-game day and player — milestone
  `doctor:treated:<day>`, only the latest kept, so it survives a reconnect; a second ask that day hears
  `dlg.doctor.r_treat_again`, a healthy patient `dlg.doctor.r_treat_healthy` and keeps the visit; `Action = "heal"` plays
  the heal sound).
- **Interviews.** `InterviewAnswerIntent` (tag 243) is accepted once, right after that reporter asked; free text ≤ 300,
  screened like chat (blocked → not stored, masked → stored masked); Safe chat mode takes only the four preset keys
  (`@news.preset.*`, localized per reader). Stored in `WorldMetadata.News[place]` (`s:<settlement>`, `b:<baseId>`,
  `w:<world>`), the latest 10.
- **Tick** (`TickProfessions`, per world, 1 Hz): a streamer greets each player within 8 blocks once per in-game day per place
  (`npc.streamer.ask`); a tamer on a planet keeps one pet — a passive/skittish land species of the roster, owned
  `npc:<id>` (exempt from `ReconcileCompanions`, follows its NPC in `MoveCompanion`, cannot be attacked), removed with its
  tamer. The blockfarmer's `Work` lies `QuarryDistance` (10) blocks beyond the settlement edge / base wall reach.
- **Client.** `HeldItem.ForNpc` re-tints existing models (`npc_medkit`, `npc_basket`, `npc_book`, `npc_leash`,
  `npc_pickaxe`, `npc_camera`, `npc_microphone`); `NpcView.FallbackGreeting` uses `npc.greet.<job>` from the NameKey.

## 12. The world greets back (#2593–#2597)

- **Passing greetings** (`GameServerNpcPassing.cs`, `TickPassingGreetings`, 1 Hz per world after `TickNpcs`). Per player on
  foot (not aboard, not in flight, not observing, not in a live conversation — §16's rule): the nearest NPC within 6 m with a
  clear line of sight that is awake, not walking (`Goal == null` — the patrolling guard never greets), not a guardian, not
  the streamer, not chatting with a neighbour and not talking with another player sends an `NpcGreeting` with
  `Kind = Passing` (`Welcome` for welcome back), `Gesture = Wave` and a
  fresh `GreetingId`; it then stands still and faces the player for 2.5 s (`ServerNpc.AttendUntil/AttendPos`, a branch
  in `MoveNpcs` after the pose block). Once per NPC and player per in-game day (`ServerNpc.GreetedDay`, keyed by
  `MarketDay`; a talk via E counts, an E-greeting within 25 s only marks the day); per player at most one every 4 s,
  8 s when more than 6 NPCs who could greet stand within 10 m (no chorus). Lines by stage, text server-localized:
  `npc.hello.stranger|known|trusted.1-3`, own base residents `npc.hello.resident.1-3` or `.<job>` (gardener, craftsman,
  guard, vendor, quartermaster). No hints or story threads are consumed (that is `EmitGreeting`'s), strangers gain
  nothing; `TryHelloStanding` (+1 per session for a villager already met, below 39) and `StampNpcSeen` follow. AI
  (known+ and residents only, never strangers): `passing_hello` / `resident_hello` / `welcome_back`, cache key
  `purpose|job|p<persona>|locale|stage` (welcome back adds the away bucket 1 / 2-6 / 7+ days); a cached variant is used, a
  miss shows the authored line and asks in the background — a passing bubble is never upgraded late. For an own resident the
  greeting also warms the `dialog_open` re-wording of the dialogue they would open next.
- **E-greeting by stage** (`EmitGreeting` → `StageGreetingText`): nothing cached → a known/trusted classic vendor or
  quartermaster says `npc.greet.vendor|quartermaster.known|trusted` with the name, an own resident a resident line; the
  story flavour line and the client's role line stay for everyone else. With AI on it is the instant line the AI line
  upgrades (same id).
- **Resident dialogues** (`DialogDefinition.Home`: `""` any, `own_base` = a resident of a base the talking player owns,
  `away` = never those; filtered in `PickDialog`). `settler_neighbours` is `away`; `resident_gardener_gift` (trusted, once,
  5 berries), `resident_gardener`, `resident_craftsman`, `resident_guard` (by job) and `resident_home` are `own_base`.
  `DialogDefinition.AiPrompt`: the node-0 prompt may be re-worded (`dialog_open`) when already cached — choices, responses
  and consequences stay authored, the panel is never swapped. `GameContent.Validate` rejects an unknown `home`.
- **Welcome back** (NPC): `NpcWelcomeBackDue` (known/trusted on the NPC's own entry, or an own resident; ≥ 18 h since
  `NpcRelationship.LastSeenUtc`; an empty stamp only stamps) is read BEFORE `StampNpcSeen` — in passing (kind `Welcome`,
  `npc.welcome.known|trusted|resident.1-2`) and on E (the welcome bubble alongside the dialogue, or instead of the
  E-greeting when no dialogue opens).
- **VEGA** (`GameServerVegaChatter.cs`, `TickVegaChatterFor` from `TickShipAi`'s 1 Hz pass): seeded on the session's first
  pass. *Welcome aboard* — the aboard flag going false → true after ≥ 600 s outside, at most every 1800 s, never after a
  respawn (`ResetVegaAboardWelcome` in `CompleteRespawn`/`RecoverToShip`): `vega.welcome.aboard.1-4`, kind 5. *Login* —
  `PlayerSession.PreviousSeenUtc` ≥ 18 h old → `vega.welcome.login.1-3` 15 s after the join, kind 1, once. *Praise* —
  `TryAward(…, praise: true)` from `Advance` queues the key (`SettleAchievements` passes false: retro-awards stay silent;
  the `story` category and the join placement are skipped); the first unlock opens a 2 s window, several keys → one
  `vega.praise.many` ({0} = count), one → `vega.praise.<category>` or `vega.praise.any.1-2` ({0} = the achievement's
  name); kind 5, ≥ 90 s between praise lines. All of them only while the tutorial pack's `banter` gate is not false,
  and held back (no cooldown spent) on a channel the player muted. Each pushes the next context tip back by 30 s. AI:
  `vega_welcome` (subject `aboard.day|night` / `login.<away bucket>`) and `vega_praise` (subject = category) — a cached
  variant as `ShipAiLine.Text` with the same kind, else the authored key; VEGA lines are never sent late.
- **Base news via VEGA** (`RelayBaseNewsViaVega` in `GameServerNpcRadio.cs` — the helper other systems call): when the
  radio call does not go out (no worn radio in reach, its cadence) the owner hears the news from VEGA instead. Gates:
  joined, not observing, "NPC calls" All (an `urgent` warning: anything but Off), a kind-1/5 line is held back while the
  hints are muted, and a per-key cooldown shared with the radio's `NpcCallCooldownUntil`. *Settler moved in* — only for a
  NEW resident (no memory entry yet; residents respawn on every world load and the call used to ring again): radio, else
  `vega.news.settler` ({0} name, {1} base, kind 3). *Trader landed* — radio, else for an owner in the same star system
  `vega.news.trader` ({0} body, kind 1). *Guard* — see §7 (`vega.news.guard`, kind 3, urgent). AI: `vega_news`, the
  authored line's `{0}`/`{1}` are required tokens.

## 13. Villagers talk to each other (`GameServerVillageLife.cs`, #2600)

- **Tick** (`TickVillageLife`, per world after `TickProfessions`): running conversations say their next line when it is
  due and are checked once a second; a scan for new ones runs every 3 s. A *place* is `NewsPlaceKey` — a settlement, a
  base, or the whole world for crew without a settlement (a station is ONE place).
- **Who.** Two people of one place, `RoutineEnabled`, awake (`Pose != 2`), not walking (`Goal == null`), not waiting
  for a seat, not posing, not greeting a passer-by (`AttendUntil`), not in a player's dialogue, not a guardian, rested
  (`ServerNpc.ChatRestUntil`, 90 s after their last talk), at most 3.5 m apart (|Δy| ≤ 2 — seated at one tavern table
  counts), with a player on foot (not observing) within 16 m of BOTH. Never at night (`RoutineNightStart/End` at the
  pair). One random fitting pair per place and scan, a coin flip for who starts.
- **No sound jumble.** One conversation per place at a time (two in a settlement of 30+ people), never one in earshot of
  another (48 m between them); after a talk the place rests 25–45 s, or 45–75 s when more than six people stand within
  15 m of the listener (a crowd — a station deck, a busy square); the last four topics of a place are skipped. Lines
  go out as `NpcGreeting` `Kind = Chatter`, no gesture — the client's voice budget ranks them below every greeting.
- **The talk.** 2–4 lines (`data/village_life.json` `chatter`), speaker A first, alternating every 3.6 s, sent by
  `BroadcastNpcLine` to every player on foot within 24 m of the speaker, each in their own language. While talking both
  stand still and face each other (`ServerNpc.TalkWith/TalkUntil`, a branch in `MoveNpcs` right after the pose block;
  a seated talker faces the other instead of a visitor). It ends early when one of them falls asleep, walks off (a routine
  goal), is posing, is gone (a world reload) or a player talks to them (`HandleTalkToNpc` → `StopVillageChat`).
- **Topics** (`VillageChatterTopic`: `key`, `when` day|evening|any, `needs`, `weight`, `hint`, `lines`; vocabulary in
  `VillageChatterNeeds`, Shared). `needs`: `weather_wet|storm|snow|clear|fog` (`BiomeWeatherAt` at the pair — family +
  precipitation; a station has no sky), `trader` (a trader parked on this world within 400 m, `{trader}`), `camp_near`
  (an uncleared camp within 400 m, bandits active), `player_known` (a listener the first speaker knows, `{player}`),
  `festival_pending` (`FestivalPendingHero` — the hero this settlement's planned festival waits for, §15), `deed:<kind>` (a
  deed of the last 3 h of world playtime about this settlement or the whole world, not yet talked about here this
  run; `{player}` = its hero; weight ×5 while the hero listens). Placeholders: `{a}` `{b}` (the speakers), `{place}`
  (the village), plus what the need provides (`VillageChatterNeeds.TokensFor`). `GameContent.Validate` checks the
  structure; `VillageLifeContentTests` checks that every line resolves in en+de raw, has ≤ 56 characters, uses only its
  placeholders and names the fact (`{player}`/`{trader}`) it is about.
- **AI** (`chatter` on `/npc-lines`): at the start, for every language in earshot, a cached variant for the key
  `chatter|<topic>|p<voice A>|<locale>|p<voice B>` is used (filled with this conversation's names), a miss keeps the
  authored lines and asks once in the background (priority Chatter): both speakers (`{a}`/`{b}` tokens, `PersonaFor`
  of each one's memory key, role, job, robot), the topic hint, the situation (time of day, weather), the fact with tokens
  ("{player} chased the bandits out of their camp near the village"), the authored lines, LineCount = the authored count,
  MaxChars 56. Allowed tokens = the topic's, required = those of the authored lines. A language joining mid-talk gets a
  cached variant or the authored lines (no request).
- **`BroadcastNpcLine(npc, range, textFor, kind, gesture)`** — the shared "one bubble for everyone near" helper (also
  the festival's cheers): every joined player on foot within `range` of the NPC gets an `NpcGreeting` with the text
  `textFor(session)` returns for them (empty skips them), one fresh `GreetingId` shared by all receivers, the given kind
  and gesture. Returns the id (0 = nobody heard it).

## 14. Deed log and news (`GameServerVillageLife.cs`, #2599)

- **The log.** `WorldMetadata.Deeds[locationId]` = `DeedRecord{Id, Kind, PlayerId, PlayerName, Settlement, Arg, At}`,
  the latest 16 per world (additive JSON, no save bump; `At` = `CumulativePlaytimeSeconds`, at least 1; `Id` increases per
  world). `RecordDeed(kind, hero, settlement, arg)` (or by player id + name) writes and saves; an observer leaves no deed.
  Kinds (`DeedKinds`): `camp_cleared` — `OnBanditKilled(bandit, killer)`: the attacking player (`GameServerEnemies`), a
  sentry's base owner (`GameServerSentries`), fire → the nearest player on foot within 64 m (`GameServerBurning`); the
  deed's settlement is the nearest within 400 m of the camp, and every villager of the settlements within 400 m the hero
  already has an entry with gains `CampClearedStandingBonus` (+2). `giant` (Arg = colossus|sandworm|leviathan|sky_giant)
  and `sreekmakra` from their defeat hooks (the killer, else the nearest player within 64 m; the nearest settlement within
  400 m, else the whole world). `landing` — `RecordPlaceDiscovery` on a first landing (never the spawn world) on a world
  with an inhabited settlement, world-wide. `settlement_helped` (a "settlement needs" chain finished) and `festival`
  (the party started) are recorded by the festival (§15).
- **News.** `NewsDigest` (the reporter's `news` consequence, and since E13 the **innkeeper's**: dialogue
  `innkeeper_news`, job `innkeeper`, ahead of `settler_neighbours`) = `LatestNews`: the place's interviews
  (`WorldMetadata.News`, `NewsArticle.At` = playtime; articles from before have 0 and sort last) merged with the deeds of
  this world that concern the place (its settlement's or world-wide; crew without a settlement hear all), newest four
  first. A deed line is `news.deed.<kind>` ({0} hero, {1} the settlement or the world's name, {2} the thing —
  `news.thing.<giant>`, German in the accusative). Nothing to tell: `dlg.reporter.no_news` / `dlg.innkeeper.no_news`.
- **AI** (`news_deed`, priority News): the authored headline shows first; reading it asks for a re-wording in the
  teller's voice — key `news_deed|<kind>#<world>#<id>|p<voice>|<locale>|`, one line ≤ 140 characters, the template's
  `{0}/{1}/{2}` as `{player}/{place}/{thing}` (all required), the fact with tokens — and a later read uses it, filled in.

## 15. The village festival (`GameServerVillageFestival.cs`, #2604)

- **Planned.** `OnChainStepTurnedIn` → nothing feasible follows → `OnSettlementChainDone`: for the chain
  `settlement_needs` (the step's `AcceptedFrom` names the settlement) a `settlement_helped` deed, then
  `ScheduleFestival`: `WorldMetadata.Festivals["<locationId>|<settlement>"] = FestivalRecord{State pending|held, HeroId,
  HeroName, ScheduledAt, HeldAt}` (additive JSON, no save bump) — **once per settlement, ever**. A later helper (another
  player) finds the record and gets the thanks (`village.festival.thanks_again`) and the present at once, no second
  festival. The quartermaster (`Job == "quartermaster"`) answers the turn-in in person when within 20 m —
  `village.festival.planned`, `Kind = Addressed` (a direct reply), `Gesture = Cheer` — and VEGA says
  `vega.festival.planned` ({0} = the settlement, kind 3). Already there in the window → the party starts right away and
  the "planned" lines are skipped. While pending, `FestivalPendingHero(settlement)` names the hero (chatter, §13).
- **Start** (`TickVillageFestivals`, Guard-registered after `TickVillageLife`: the tune's notes every tick, a pass once a
  second per world). A pending festival starts when its hero is joined on this world, on foot, not observing, within
  48 m of the **square** and the local day there is in [0.60, 0.95) — it waits forever otherwise (no FOMO). The square
  (`FestivalAnchorFor`, computed once the hero is near, cached per run): the layout centre (`Min + Width/2, GroundY + 1,
  Min + Length/2` — where `SettlementGenerator.StampCentralFeature` paves the 3×3 plaza; the box centre for a settlement
  without a layout) when a ring fits there (≥ 4 of 8 standable spots at radius 3 with no wall between them and a point
  2 m from the middle — a kit grid with a closed centre puts a house there); else the `npc` marker nearest the centre,
  else the `mission_board` marker, else the centre anyway.
- **The party** (180 s, runtime only — `Held` is saved at the start, so a restart mid-party simply ends it). Attendees:
  the settlement's `RoutineEnabled` people (`BaseId == 0`, no guardian, not `VisitingAway`, no guest) within 64 m of the
  square, nearest first, at most 24. `SeatFestivalRing` sorts them by their angle around the square and gives each a ring
  spot — radius clamp(1.5 + 0.25·n, 3, 6), a little in/out/sideways when blocked, else where they stand.
  `ApplyRoutinePhase(npc, NpcPhase.Festival)` sends them there (`npc.activity.celebrating`); `RoutinePhaseFor` keeps
  answering `Festival` while the run lists them, and `MoveNpcs` (a branch before the chatter one) keeps an arrived
  attendee standing, facing the square. Attendees neither chat (`CanStartChatter`) nor greet passers-by
  (`MayGreetInPassing`). After the end the next routine check (≤ 2 s) returns everyone to their phase.
- **Cheers** every 5 s, ONE per festival (NO SOUND JUMBLE): a random attendee (one already in the ring while two or
  more are; never the same voice twice in a row) — `BroadcastNpcLine(…, 32 m, Kind = Cheer, Gesture = Cheer)`, the
  lines of `village_life.json` `festival.cheers` in order, `{player}` = the hero, per receiver language.
- **The tune** at the start and every 45 s: eight positional `SoundFx` notes `note_1_{0,2,4,7,4,2,4,7}` (the melody
  block's bell, synthesized by the client's `ProceduralAudio`), 0.35 s apart, at the square + 1.5 m, to every player of
  the world (not in space) within 64 m. No client change.
- **At the start, once:** the quartermaster's present (`festival.gift`: lanterns ×6, flower pots ×2, a gold ingot) via
  `GiveFestivalGift` — milestone `festival_gift:<key>`; a full bag sets `festival_gift_due:<key>` and says
  `village.festival.bag_full`, and the next E-talk to that quartermaster with room hands it over
  (`TryGiveOwedFestivalGift` at the top of `HandleTalkToNpc`, `village.festival.gift_later`, Kind Addressed). The
  thanks (`village.festival.thanks` + the item list on its own line) is a bubble (`Kind = Welcome`) within 20 m, else a
  chat message. Then `AddStanding(hero, NpcKeyForNpc(attendee), FestivalStandingBonus = 6)` per distinct attendee key,
  a `festival` deed, and 6 s later VEGA's `vega.festival.started` (so the quartermaster is heard first). No block is
  changed.
- **AI** (hosted worlds): when the festival is planned, `PrewarmFestivalLines` asks in the HERO's language for the
  cheers (`cheer`, key `cheer|<settle key>|p200|<locale>|`, LineCount = the authored count (6), ≤ 60 characters, one
  "villagers" speaker) and the quartermaster's thanks (`festival_thanks`, key
  `festival_thanks|<settle key>|p<qm voice>|<locale>|`, one line ≤ 110), priority Personal, `{player}` allowed and
  required, facts = the chain steps the hero finished there ("{player} brought the village iron ore", "{player} built
  new lights for the village" …, no numbers). At the start a cached variant is used (filled per receiver), else the
  authored lines; other languages use a cached variant if one exists. VEGA's lines stay authored.
- **Content.** `GameContent.Validate` checks the gift's items and counts and that the cheer keys are distinct and
  non-empty; `VillageFestivalTests` checks that every line resolves in en+de, the cheers stay ≤ 60 characters with only
  `{player}` and no digits.

## 16. Talking with a player — the conversation's life (`GameServerNpcDialogs.cs`, #2616)

- **Where it lives.** `PlayerSession.Conversation` (`NpcConversation`: the NPC **object**, the world's location id, the
  dialogue, the node, start and last-active time). It exists only while the client shows reply choices:
  `HandleTalkToNpc` and the wish offer open it (`StartNpcConversation`) for a step with choices; an end node, a node without
  choices, the client's leave, a new E-talk (which closes the old panel), walking away, a world change, the NPC leaving the
  world and a disconnect end it (`EndNpcConversation`). NPC ids restart at 1 on every world and every respawn — holding
  the object instead of the id is what stops a stale talk from applying world A's consequence to world B's NPC.
- **One validity rule** (`NpcConversationValid`), used by every reader — the choice, the sweep, the passing greeting's
  "in a conversation", `NpcInPlayerDialog` (village chatter, the visiting-neighbour pick): the player is joined, not in
  space, on the talk's world (the active one); the NPC is still in `_npcs` by reference and not `VisitingAway`; the
  distance (`WrapDistSq`, both seams) is at most talk reach 5 + 2 m. The talk reach itself (`HandleTalkToNpc`, the
  reporter's interview answer) wraps too (#2613: the home pad lies on X = 0).
- **The sweep.** `TickNpcConversations` (1 Hz per world, right after `TickNpcs`): a dead conversation ends and the client
  gets `NpcDialogState { End = true, Action = "close" }` (`NpcDialogState.ActionClose`) — only a client announcing
  `ClientFeatures.DialogClose1` (`dialogclose1`, `NetworkClient.ClosesDialogs`, set by the Unity client; local players
  have it): an older panel would pop open again to show it, so it keeps its old behaviour. A talk whose player left its world is ended by
  whichever world ticks (its own may no longer tick); the full rule runs on the talk's own world.
- **The leave.** `NpcDialogUi.Close()` sends `NpcDialogChoiceIntent { ChoiceIndex = -1 (Leave), NpcId }` while the panel
  still offered replies — every released server already dropped the dialogue on a negative index, so there is **no new
  intent and no codec tag**. `PausedMayHandle` lets only the leave through a singleplayer hold (Esc closes the panel as
  the pause menu opens); a real reply still waits. A choice or leave naming another NPC than the conversation's is a
  late one for an earlier panel and changes nothing (`NpcId` 0 = an older client: the current conversation). The
  server's close only hides the panel — no leave goes back.
- **The NPC while talking** (`HoldNpcForTalk`, renewed by the sweep in 2 s steps, so a talk that ends without a word lets
  go on its own): it stands still and faces the player (`AttendUntil/AttendPos`; `ServerNpc.TalkHoldUntil`, a branch in
  `MoveNpcs` before the festival one keeps a walk it was on waiting without counting as stuck), and `TickNpcRoutine` skips
  its phase check and job — for at most `NpcTalkHoldSeconds` (60 s) after the talk began. Then the routine may walk it on,
  and walking out of reach ends the talk. Chatter skips it (`AttendUntil`, `NpcInPlayerDialog`), it greets no passer-by.

## 17. Out in the wild — lodge residents and wanderers (#2639)

People who live outside the settlements have their own page: [WILDERNESS_FOLK.md](WILDERNESS_FOLK.md). In terms of this
page: a **lodge resident** is a profession NPC (Role `vendor`, `Job` gatherer / miner / woodcutter / hunter, its own `Theme`)
with `RoutineEnabled` — day at the yard, evening on the bench by the campfire, night in its bed — whose job runs in
`TickNpcJob` through the generic work cycle `TickNpcWork` (`GameServerNpcWork.cs`; `ServerNpc.LodgeKey`, `WorkState`);
its `NewsPlaceKey` is `l:<lodge>`, its dialogues have home `lodge`, its person key `lodge:<world>:<x>:<z>#<slot>` follows the
villager standing rules. A **wanderer** runs the same work cycle without a routine and leaves like a base guest.

## Tests

`NpcPassingGreetingTests`, `NpcWelcomeBackTests`, `BaseNewsRelayTests`, `VegaChatterTests` (§12),
`VillageChatterTests`, `VillageLifeContentTests`, `VillageLifeAiTests` (§13), `DeedNewsTests` (§14), `VillageFestivalTests` (§15),
`NpcConversationLifecycleTests` and Client.Tests `NpcDialogLeaveTests` (§16),
`NpcProfessionTests` (table, kits, settlement staffing, content, clinic/streamer/reporter/tamer at a base, Safe-mode
interview, shop room), `NpcGridPathTests`, `BaseResidentsTests`, `NpcRoutineTests`, `NpcJobsTests`, the station-night case in
`PlayerStationReportsTests`, the village-bed case in `SettlementNpcTests` (Slow). The shared arena is `NpcLifeWorld`.
