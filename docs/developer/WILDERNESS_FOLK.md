# Wilderness folk — lodges, their residents, wanderers and pioneers

Epic #2639 (issues #2640–#2647). Before it every NPC was bound to a settlement, a base, a station, a landing pad or one unique
site, and only base residents really worked. The wilderness folk live and work **outside** the settlements: a **lodge** is a
small hut in the open land with one or two residents who gather, mine, chop or hunt around it; a **wanderer** is a named
traveller you meet on the way; a **pioneer outpost** (#2681/#2682, §8) is the lodge variant a player-like NPC lives in — a
small closed base with a pixel-skinned resident. This page is the map of that code. The NPC basics (routine, A*, doors, greetings, dialogues)
are in [NPC_ROUTINES.md](NPC_ROUTINES.md); the village rules the folk share (AI two-mode lines, no sound jumble) are in
[VILLAGE_LIFE.md](VILLAGE_LIFE.md).

## 1. New saves only (`WorldDescription.WildernessFolkGeneration` = 25)

Lodges and wanderers exist only in worlds of terrain generation 25 and newer (`CurrentTerrainGeneration` moved 24 → 25).
Both are server-side — a stamp after the bandit camps and transient NPCs — so nothing in the chunk pipeline changed and
every chunk of every generation is bit-identical; the gate only keeps a lodge from growing into, and a wanderer from walking
through, a world of an older generation. A generation-25 world that was materialised before its lodge records existed
records a skip (the unique-site rule), never a late stamp next to a player's build.

## 2. Lodges (`GameServerLodges.cs`, `LodgeGenerator`, `data/lodges.json`)

- **Data** — `data/lodges.json`, one row per profession (`LodgeProfessionDefinition` in Shared): `gatherer`, `miner`,
  `woodcutter`, `hunter`, each with its held tool (`npc_basket`, `npc_pickaxe`, `npc_axe`, `npc_bow`), its market theme
  (`gatherers`, `miners`, `woodcutters`, `hunters`), its activity key (`npc.activity.gathering|mining|chopping|hunting`), its
  yard kind, its partner profession and its planet-trait weights. `GameContent.Validate` (`ValidateLodges`) checks the
  vocabulary (`LodgeRules`).
- **Gate** (`StampLodges`, boot stage "structures", right after the bandit camps): generation ≥ 25, `ServerConfig.PlaceLodges`
  and `PlaceSettlements`, not void, airless, ruined-only, a gas giant or the G.D.S. city world, `Settlements.StructureFactor()
  > 0`. A restricted body (Titas, Valuma) skips the whole block like the camps.
- **Count** — λ = clamp((hospitability − 0.35) / 0.65) × circumference / 8000 × 2.2 × the Settlements factor, drawn in 12
  Bernoulli slots, cap `LodgeRules.HardCap` = 6. Lush worlds typically get 1–5, harsh ones (rocky, ice) 0–2, airless and
  toxic-only worlds none.
- **Profession** — weighted by the planet's traits (each 0..1): fauna = huntable land species / 3, only when
  `WorldHasHuntableSpecies()`; flora = FloraDensity / 0.15; ores = tier-1 ores / 3; trees = TreeDensity / 0.03. One world's
  professions are spread before one repeats; the placement record's `Template` pins each lodge's profession.
- **Placement** — `TryPlaceStructureGuaranteed` with the camp seat policy (no stilts, no lava), records `lodge/i`. Reserved
  ground (on top of each footprint's half-size): pads + 12, the wreck zone + 8, settlements and camps + 40 (a hut stands
  clearly *outside*), ruins + 24, other lodges + 24, the intercity line and the rail ruin. Monuments and factories reserve
  the lodges in turn (`AppendLodgeReservations`). Voxels are stamped once (`FeatureStamped("lodges")`); instances,
  residents and the store binding are re-derived on every load, and the stamped quarry face is restored there
  (`RestoreNpcCellsNow`).
- **Structure** (`LodgeGenerator.Generate(seed, yard, surface, ores, alien, content, beds)`, WorldGeneration, 13 × 13 × 9):
  the house at local x/z 1..6 — log frame, the biome's wall material (alien trim like the village houses), windows, a 2 × 3
  hinge door facing +Z, the beds along the back wall (55 % of huts have two), the store crate at (3, 1, 2), a torch; the
  campfire at (2, 1, 9) on a stone ring with a three-cell log bench at z = 11 facing it; one yard per profession — the crop
  plot (x 8..12, z 2..7, fenced, a gate at x = 8), the quarry rock (x 11..12, z 3..8, 4–8 face cells of the planet's own
  tier-1 ores in stone), the woodpile (x = 8) with two grove trees at (11, 3) and (11, 10) standing on grass/dirt 7 apart,
  or the hunter's drying frame. No new block — bed, campfire, crates, crops, fences and logs all exist.
- **Protection** (`IsLodgeBlock`, part of `IsSettlementBlock`): the house box (local x 0..7, all z) and the yard part
  (plot, quarry, woodpile or frame), from 48 below the floor to the top. The grove trees and the hunting ground are free;
  picking plants stays allowed (the flora exemption). Mining a protected cell or the hut's door says
  `srv.protect.lodge` ("This hut is {name}'s home — it's protected.").
- **Store crate** — a `wood_crate` container with id prefix `lodge:`. A player may open it to look; loot, deposit, filter
  and move are refused with `srv.lodge.store_owned` ("That's {name}'s — ask them!"). The residents' jobs deposit into it: at
  most 6 different goods, 40 of each; a seventh good pushes out the oldest stack ("brought some to the village"). No machine or base ever uses it: `ContainerAt` — what Crystal Net senders, receivers, fabricators, device prices, the drills and the base jobs see — skips every `lodge:` store, so a matter sender or a base built beside a hut neither drains nor fills it.
- **Map** — POI type `lodge`, named `poi.lodge` ("{0}'s hut"), revealed (`RevealPoi`, key `<loc>|lodge:<key>`) in
  `TickLodges` when a player on foot comes within 40 m, or by a villager (§4).

## 3. Residents and the job state machine (`GameServerLodgeResidents.cs`, `NpcWorkSystem.cs`)

- **Residents** — one per bed (1–2), spawned on every load after the settlement NPCs. Role `vendor` with the profession as
  `Job` and its `Theme` (the profession pattern, so the trade-or-talk prompt and `VendorThemeAt` work unchanged); the second
  resident takes the partner profession when the world supports it (gatherer → hunter, miner → woodcutter, woodcutter →
  gatherer, hunter → gatherer), else the same one. `PersonKey` `lodge:<world>:<x>:<z>#<slot>` — the villager standing rules
  apply (+3 on the first meeting, a talk pays every 10 minutes, the passing hello +1 up to 39). `ServerNpc.LodgeKey`;
  `NewsPlaceKey` = `l:<key>`, so the two residents of one hut chat as their own place.
- **Routine** — `RoutineEnabled`: day = work (anchor = the yard marker), evening = the log bench by the campfire, night =
  the bed (a visitor finds them asleep). `TickNpcJob` hands a resident with a `LodgeKey` to `TickNpcWork`.
- **`TickNpcWork(npc, NpcWorkPlan plan, targets)`** — the generic cycle the lodges and the wanderers share. `NpcWorkPlan`:
  `Kind` (Gather, Mine, Chop, Hunt), `Center` (standable), `Radius`, `FixedSites` (a lodge's plot crops, quarry face),
  `Bank`, `SiteAllowed`, `ActivityKey`, `Held`, `DailyKillCap` = 3, `RespectAllProtection` (a wanderer's plan). The cycle:
  pick a site (fixed sites first, then the core's search, nearest first) → walk beside it (A*; a walk times out after 45 s,
  a site that failed two routes is dropped for 5 minutes) → face it and work with the tool (gather 4 s, mine 5 s, chop 6 s)
  → call the core → wait (gather and mine 60–90 s, chop 360–480 s, hunt 240–360 s). Nothing found: look again after 20 s; a
  core says no: try again after 6 s. A hunter looks 24 m around itself, walks a six-point round about 19 m around its
  frame (hunting ground 32 m), approaches to 11 m, aims 1.5 s and shoots from ≤ 12.5 m, at most 3 kills per in-game day;
  while it walks it calls `ScareCreaturesFrom(pos, 8, 2.5)` once a second. Runtime state in `ServerNpc.WorkState`; a gap of
  more than 5 s between calls resets it. Only in the day phase, standing, in a player's area (frozen otherwise); a resident
  pauses while a customer stands near it (the keeper rule).
- **Talk** (`data/dialogs.json`, home `lodge`): `lodge_<job>` — what they do, "Seen anything interesting around here?"
  (`lodgetip`: the nearest wreck, bandit camp or — for a player the resident already knows — hidden cache the map does not
  show yet goes on the map and its way is told, else the way to the nearest village), the gatherer's warning about this
  world's poisonous plants (`lodgepoison`), and at "trusted" a present (`lodgegift`: the first non-toxic stack of the store
  crate, up to 4, once per in-game day — milestone `lodge:gift:<personKey>:<day>`). Every line is authored in all languages
  and carries an `aiPrompt` for the hosted worlds' re-wording; the AI backend describes the four jobs (`_ROLE_DESC`).
- **Trade** — `data/recipes.json`: `gatherers` (berries and fruit, grain, fibre, seeds), `woodcutters` (logs, saplings),
  `hunters` (cooked meat, meat bait, forage bait); the miner uses the existing `miners` deals. Header lines
  `ui.vendor.theme.<theme>`.

## 4. Villagers know the folk

- **Hint** (`TryEmitLodgeHint`, inside `TryEmitHint`): a villager (memory key `settle_…`) tells anyone about the nearest
  unrevealed lodge within 600 m — "{name} the {job} lives in a hut out in the wild, about … m to the …" — and puts it on the map.
- **Chatter** — topic `lodge_near` (`village_life.json`, need `lodge_near`: a lodge within 400 m; `{npc}` = its first
  resident).

## 5. The work cores

One action per call, no cadence of their own (the caller paces), drops into the plan's bank. All of them never load a
chunk, never touch a cell with a player owner (the repository keeps the last player owner for good; structure stamps and NPC
work write owner ""), and never work inside a settlement box, the intercity line, a station, a factory, the Guardian core, a
base zone, a ship pad or a ship interior. The lodge box is deliberately NOT in that narrow set — a resident works its own
plot and face — unless the plan says `RespectAllProtection` (wanderers), which adds `IsSettlementBlock` and ruined
settlement boxes.

- **Gathering** (`GameServerNpcGathering.cs`, #2642) — `FindNpcGatherSites(center, radius ≤ 32, max ≤ 64,
  respectAllProtection)`: a column walk over the disc on loaded chunks, ±6 around the centre's feet; crops (always), wild land
  species this world did not roll toxic, tree fruit up to 4 above its ground; never saplings, bred or authored plants,
  seabed, cave or other hanging plants. `NpcHarvestFlora(npc, cell, bank, trustedSite, respectAllProtection)` breaks the cell
  through `BreakBlockCore(null, "")` — the player's drops (a spore bloom's bonus included), the fruit's tint into its regrow,
  the regrow (30 s, fruit `FruitRules.RegrowSeconds`, persisted). `trustedSite` = the resident's own plot cell, tended even
  after a visitor picked it.
- **Woodcutting** (`GameServerNpcWoodcutting.cs`, #2644) — only natural trees: a `wood_log` trunk with a `tree_leaves` /
  `pine_needles` / `palm_frond` crown on sapling ground (dirt, grass, mud), inside a ±3 × 14 box, at least 2 trunk logs and
  4 leaves, no player edit in the box widened by one, and nothing but natural blocks in that box from the trunk's base up
  (air, tree cells, flora, fluids, loose snow or sand, terrain without an edit row) — a hut of logs and leaves or a ruin's
  frame is no tree; never in a settlement box, ruins included. `NpcFellTree` takes the tree down from the top over a few
  ticks (`TickNpcFelling`: fruit first, then outer before inner, leaves before wood; ≥ 24 cells/s, ≤ 12 a tick, about 2.5 s),
  one `BlockChanged` per cell, a `tree_fall` `SoundFx` to players within 48 m at the start, logs banked as they fall (≤ 8;
  each as its trunk drops it — a birch's pale bark as `wood_log_b` since #2958);
  from the point where fewer than 4 leaves or 2 trunk logs would remain (`CollapseAt`) the rest comes down in one tick and a
  sapling is planted on the base cell (owner "", the normal 150 s sapling clock, persisted by the flora queue) — a crash
  leaves a tree or a sapling, never a stump. Fellings are RAM-only and dropped when their world unloads.
- **Mining** (`GameServerNpcMining.cs`, #2643) — `NpcMineCell(npc, cell, bank, NpcMineRefillSeconds = 600)` through
  `BreakBlockCore(null, "")`: only solid mineable terrain/ore, no fluid beside or above, nothing loose, a plant, a torch or
  lantern, a hanging plant or a player's edit on or above it, no torch or lantern fixed to its side or hanging under it
  (#2683), the six neighbouring chunks loaded. Every mined cell goes into
  the **refill registry** (`WorldMetadata.NpcRefills`, keyed by location: block by key, tint, glow, shape, due time in world
  playtime; additive JSON, no save bump; at most 512 per world, the oldest restored first, a full registry drops an old cell
  in an unloaded chunk without loading it). `TickNpcRestores` (1 Hz) puts a due cell back when it is still air, no player is
  within 12 m and nothing stands in it (an NPC body, an animal within 1.5 m, a ground drop, a landed ship); a cell a player
  built into or dug again is left alone and forgotten. The registry is written at most once a minute and with every full
  save. `FindNpcOutcrops` (the wanderers' prospector): drill-cut rock of tier ≤ 1 at the top of its column, never protected
  (the full `IsSettlementBlock`).
- **Hunting** (`GameServerNpcHunting.cs`, #2645) — `FindNpcHuntTarget(npc, range)`: one of this world's own rolled species
  (no `au_` story creatures), Land, Passive or Skittish, Standard or Worm body plan, a walker or crawler, bio rarity at most
  Uncommon; never a companion or owned animal (tamer pets `npc:<id>`), a giant, a clone, a begging, called, frozen, provoked
  or hostile animal, the animal a player is taming, one within 4 m of a player or with a player within 1.2 m of the line of
  fire (bow to 3 m past the animal), one without line of sight, or one standing in a base zone, a settlement, a ruin, a lodge
  or within 3 blocks of an energy fence or gate (a pen). `NpcShootCreature` (≤ 14 m, line of sight): `WorldFx` `arrow`
  (feet + 1.35 m → the animal + 0.5 m, `SourceId` = the NPC) to players within 64 m and a `npc_bow_shot` `SoundFx` within
  32 m in the same call as the kill; the animal leaves like a sentry kill (`OnCreatureKilled(target, null)`, the sparkle)
  and its loot is banked. **No player gets any credit** — no mission, achievement, deed, bounty, bio sample or game event.
  Not loop-safe over `_creatures`.
- **Animals notice hunters** — `CombatEntity.ThreatPos` / `ThreatTimer` (server-only): a skittish animal within 6 m of a
  threat flees it (unless a player it already flees is nearer), a startled non-retaliator within 24 m flees the threat rather
  than the player, a bolting skittish animal passes the threat to its kin. After a shot the same species within 12 m are
  startled for 6 s and keep the hunter as their threat. Without a threat the behaviour is bit-identical to before.

## 6. Wanderers (`GameServerWanderers.cs`, #2646)

- **Gate** — the lodges' body rules (generation 25+, not void, airless, ruined-only, a gas giant or the city world, not a
  restricted extreme world, Settlements factor > 0) plus `ServerConfig.PlaceWanderers`. Nothing is saved: the cast, the
  visits and the packs are RAM; only the players' memory of the people persists.
- **The cast** — 4–6 named people per world (`WandererRosterMin/Max`): `"wanderers:" + loc` seeds the count and the
  professions (the lodges' `LodgeTraits` + `PickLodgeProfession` — a hunter only where something is huntable),
  `"wanderer:" + loc + ":" + slot` the name and look. Same job keys as the lodges (a prospector is a `miner`, its nameplate
  `npc.role.prospector`), Role `vendor` with the profession's theme, `RoutineEnabled = false`. Person key
  `wanderer:<world>:<slot>` (`IsWandererPersonKey`; `IsWildernessPersonKey` = lodge or wanderer) — the villager standing
  rules, welcome back after a long absence.
- **The check** — every 60 s per player on foot (not aboard, flying or observing): local day in [0.27, 0.68]; not
  underground (no deeper than 6 below the surface); 60 m from any settlement or ruin box, bandit camp, lodge footprint or
  base zone; no hostile within 24 m; the player's **quiet spell** over — 720 s × 0.8–1.2 of play on this world, armed on
  first sight (a join, a server restart) and after every visit, paused while their own wanderer is out — then a 0.5 roll.
  At most 2 wanderers per world (`WanderersPerWorld`), one per player, nobody of the cast twice; only a member with work at
  the task centre comes (a hunter needs an eligible animal within 16 m of it and fewer than 3 kills that day).
- **The visit** — spawn out of sight on the rings 40 / 47 / 55 m (12 bearings, ≤ 10 blocks above or below the player,
  out-of-sight sides first); the task centre lies 15–25 m from the player towards the spawn. *Arriving*: walk there (75 s
  or two failed routes → work where it stands). *Working*: `TickNpcWork` at 1 Hz with radius 16 and
  `RespectAllProtection`; the prospector's fixed sites are `FindNpcOutcrops(centre, 12, 3)`; until 1–3 harvests, 1–2
  cells, one tree or one animal, within 3–5 minutes of the spawn; the yield goes into a RAM pack (6 kinds × 20), no crate,
  no gift. *Leaving*: walk the bearing away from the player and despawn when no player is within 32 m at the end of the
  walk, or after 120 s with nobody within 24 m. A visit ends early at sunset, when the player is more than 80 m away or
  leaves the world; a world not ticked for 20 s drops its wanderers. A player within 4.5 m stops any walk (the keeper
  rule); a customer or a talk adds 15 s to the work window.
- **Talk** — `wanderer_<job>` dialogues, home `wander` (`DialogDefinition.HomeWander`: never a lodge resident's, and a
  lodge dialogue never matches a wanderer), with an `aiPrompt`; +1 standing on the first answer. The tip (`wandertip`)
  names the nearest unrevealed lodge within 600 m and puts it on the map, else falls back to the lodge residents' tip.
- **Playtest helpers (admin)** — `/wanderer` (`summon_wanderer`, a cheat like `/biped`) calls one now: no quiet spell,
  roll, daytime or work requirement (anyone free comes); the world gate, on foot, the 60 m rule, the hostile check, the
  cap and a spawn spot still apply, a refusal names the reason (`srv.admin.wanderer_*`). `/tp lodge [n]` jumps to the
  n-th nearest lodge's yard (`hut`, `hütte` also work).

## 7. Client (#2647)

- **Map** — `WorldMap.PoiLook` `lodge`: the hut glyph in wood brown (`WorldMap.LodgeCol`) and the `map_lodge` icon
  (generated with `tools/ai-assets/gen_map_icons.py --only map_lodge`); legend row `ui.map.kind_lodge`.
- **Tools** — `HeldItem.ForNpc` `npc_axe` → `Kind.Axe`, `npc_bow` → `Kind.Bow` (procedural models in `HeldItemShapes`).
  The swing cadence table moved to Client.Core (`NpcWorkTools.Cadence`): the axe 2.2–3.4 s, the bow never swings by itself.
- **Arrow** — `CreatureView` handles `WorldFx` `arrow`: `FxArrows` flies a small arrow (48 m/s, 0.12–0.3 s, a gentle arc,
  `ArrowFlight` in Client.Core) from the hunter's bow to `ToX/ToY/ToZ`; the hunter (`SourceId`, standing, holding the bow)
  plays `PlayerAvatar.BowShot()` and turns to the target; a shot animal within 3 m of the landing point keeps standing until
  the arrow lands (≤ 1 s, `ArrowLandings`) and then sparkles. Arrows are drawn within 80 m.
- **Sounds** — `npc_bow_shot` and `tree_fall` arrive as `SoundFx` (volume 0.45 / 0.55, skipped beyond 20 m);
  `npc_axe_chop` plays 0.28 s into the woodcutter's swing while its activity is `npc.activity.chopping`, within 16 m, at most
  once per 0.5 s across all woodcutters and never while VEGA or a cutscene speaks (`NpcChopGate`). See
  [SOUND_DESIGN.md](SOUND_DESIGN.md) §22.

## 8. Pioneer outposts and pioneers (`GameServerPioneers.cs`, `PioneerOutpostGenerator`, #2681/#2682)

"Player bases of NPCs" (Screelit): phase 1 — no ships, travel or combat. An outpost **is a lodge**: a `LodgeInstance` in
`_lodges` with `LodgeInstance.Outpost` (`PioneerOutpost`) set, so everything generic above carries it — the protection
(`IsLodgeBlock`), `LodgeAt`/`OverlapsLodge`/`AppendLodgeReservations` (later stampers keep clear), the reveal on foot
(`TickLodges`), the owned-store refusal, the person rules of the resident, the routine and `TickLodgeResidentJob`. Its own
parts:

- **Gate** (`PioneersBelongHere`, boot stage "structures", right after the huts — `BootDetail("pioneer outposts")`): the lodge
  gate (generation 25+, `PlaceLodges` and `PlaceSettlements`, not void, airless, ruined-only, gas, city or restricted, the
  "Settlements" option not off) plus `ServerConfig.PlacePioneers` and a **breathable** atmosphere. λ = the lodges'
  hospitability shape × world size × `PioneerBaseDensity` (1.4) × the Settlements factor, 12 Bernoulli slots, its own cap
  `PioneerHardCap` = 2 (beside the huts' `LodgeRules.HardCap`). Tests that need a world without them switch `PlaceLodges` or
  `PlacePioneers` off — the crate suites already switch the lodges off, so they never see a pioneer's crates either.
- **Placement** — records `outpost/i` (the template pins the pioneer's work, the kit layout the boat as `x,y,z,x|z`), the lodges'
  reservations plus every lodge (+ `LodgeSpacing`), the camp seat policy. `TryPlaceOutpost` runs the seat search on up to three
  rng lanes and takes the first spot with water for a boat, else the first spot. The **`VirginAtLoad` rule**: a body with any
  block edit gets a skip record, never a late outpost next to a player's build. Voxels once (`FeatureStamped("outposts")`):
  `StampSettlementBlocks`, then the cellar (`StampOutpostBelow`, the cells below the floor row), then the boat. No terrain
  generation bump — the chunk pipeline is untouched.
- **Structure** (`PioneerOutpostGenerator.Generate(seed, surface, content, wallKind?, hidden?, chests)`, 15 × 15 × 6) — the room
  at local x/z 1..9, closed all round: floor, walls with glass windows, a flat roof, all of one material per outpost
  (`WallKinds`: `stone`, `earth` = `EarthBlockKey(surface)` — grass → dirt, sand → sandstone, snow → ice, ash → basalt, so a
  wall never slumps — `iron` = `iron_wall`, `steel` = `steel_wall`; metal ones get a `steel_floor`), a two-wide hinge door at
  +Z. Inside: a `base_core` in the middle (marker `core` — **decoration only**: no `ServerBase` is ever registered, so no zone,
  no air machine, no residents, and `RemoveBaseAt` finds nothing; mining it is refused anyway), 2–4 `crate`s along the back
  wall (markers `store`, the first takes the work's yield), the bed, a table and a chair (the evening seat), a torch. The work
  anchor (`yard`) is in front of the door. **The tunnel**: a ladder shaft at local (8, 8) down to a cellar 5 rows below the floor
  (x/z 4..9, stone walls, one `wood_crate` and a lantern; markers `cellar_store`, `cellar`) — either **visible** (a closet with a
  one-wide hinge door, the ladder through the floor) or **hidden** (two planters of soil with a plant each; the corner one's
  soil, marker `soil`, is the shaft's lid). **The boat** (`Boat(alongX)`): 6 × 3 logs on the water's top row — sides, a pointed
  bow, a stern post, a deck of log slabs with a bench, a lantern; decoration, no vehicle. `FindOutpostBoatSpot` (pure generator
  queries, no chunk loaded): open water within `OutpostBoatReach` (10) of the footprint's edge, parallel to it, every column
  liquid (no ice), at least two deep, one level at most six below the floor.
- **Protection** — `Protected` is the whole footprint (from 48 below, so the cellar is covered); `PioneerOutpost.Soil` (the hidden
  shaft's lid) is the one free cell — pick the plant (the flora exemption), dig the soil, climb down; `PioneerOutpost.Boat`
  protects the boat's cells outside the footprint. The refusal says whose it is: `srv.protect.outpost` ("This outpost belongs
  to {name}"), through `LodgeProtectRefusal` (both mine paths and the door in `GameServer.cs`).
- **Stores** — ids `outpost:<world>:<key>` (the first crate), `…#1`–`#3` and `…#cellar`, kind `crate`. `IsWildernessStoreId`
  (`lodge:` or `outpost:`) is what the refusal and `ContainerAt` (machines, drills, base jobs) check. Each store is created once
  with two or three goods (`OutpostGoods`; the cellar box `OutpostCellarGoods` — ingots, a crystal, a medkit) and then persisted
  like any crate. A friend's present (`lodgegift`) comes from the room's crates (`LodgeGiftStores`), never the cellar's box.
- **Map** — POI type `outpost`, `poi.outpost` ("{0}'s outpost"), reveal key `<world>|outpost:<key>`, found on foot within 40 m
  like a hut, or told: a villager's `TryEmitLodgeHint` says `npc.hint.outpost`, a wanderer's tip goes the same way. The
  `lodge_near` chatter topic skips outposts (it talks of a hut). `/tp outpost [n]` (`pioneer`, `pionier`, `außenposten`);
  `/tp lodge` lists huts only, and `LodgesForTest` too (`OutpostsForTest` has the outposts).
- **The pioneer** (`SpawnPioneer`, one per outpost, after the settlement NPCs) — `MakeNpc("settler", …)` (talk, no trade, never in
  a settlement's lists), `Job` `pioneer` (`IsLodgeJob` knows it: only its own dialogues match), nameplate `npc.role.pioneer`,
  `Look` `pixel` (`PixelLook`) with the skin seed as `FaceSeed` → `NetNpc.FaceVariant` (`StableHash("pioneer-skin:<world>:<key>") | 1`
  — no pixels on the wire), a player's size, the work's tool held, `PersonKey` `lodge:<world>:<key>#0` (the wilderness person
  rules: first-meeting lift, talk pays every ten minutes, the passing hello). An NPC has no attack at all — never hostile. The
  work: `PickPioneerWork` among gatherer, miner and hunter by the planet's traits (the hunter × 1.5 where game lives; never the
  woodcutter), `PioneerWorkPlan` on the lodge job loop round the work anchor — gathering 18 m, the outcrops 16 m, hunting 32 m,
  never inside a hut or an outpost; the day as a hut's resident (work, the chair in the evening, the bed at night).
- **Away** (`PioneerAwayAt(seed, day, localFraction)`) — about one in-game day in three (`PioneerAwayDay`: the outpost seed and
  `MarketDay` through a 64-bit mix, `% 3 == 0` — an FNV string hash's low bits are too regular for that) and then only in the
  working hours, local 0.30–0.66 at the outpost. `StepPioneers` (1 Hz, from `TickLodges`) walks the trip: *Home → Away* at once
  when nobody is within 32 m, else *Leaving* — off along the bearing away from the nearest player (36…12 m) and gone at the end of
  the walk unseen, or after 120 s with nobody within 24 m (the base guests' and wanderers' leave rule); *Away* =
  `ServerNpc.VisitingAway` (not sent, not moved, no routine, no talk). *Away → Home*: with a player within 60 m of the outpost it
  appears on the 40/47/55 m rings, out of sight where possible, and walks home (*Returning*, 90 s, then home unseen); else it is
  simply home. A talk holds every step. RAM only: a load during an away window starts the pioneer away.
- **Talk** (`data/dialogs.json`, home `lodge`, job `pioneer`, `aiPrompt`) — `pioneer_friend` (trusted: the present, a secret —
  the cellar hint — and the tip) before `pioneer` (who they are, the outpost, the tip). The AI's place and situation come from
  `LodgeAiPlaceOf` / `LodgeAiSituationOf`, the job description from the AI backend's `_ROLE_DESC["pioneer"]`.
- **Client** — `NpcView`: a `pixel` look builds the avatar from `PixelSkinTextures.Pool.SkinFor(FaceVariant)` (Client.Core
  `PixelSkinPool`): the skin's base colours (palette indices through `FacePalette.ColorOf`), its hair, a bare lower face, then
  `SetFace` and `SetBodyPaint` per part. The bakes are cached (`PlayerAvatar.TextureCache` = `PixelSkinTextures.Shared`, keyed
  by painting and tint, freed on a world change; a figure never destroys a shared texture), so a pioneer walking out of reach
  and back is not baked again. No pool or no skin → the plain NPC look. The map: `WorldMap.PoiLook` `outpost` — the `map_base`
  icon in `OutpostCol` (sand), legend row `ui.map.kind_outpost`.

### 8.1 The pixel-skin pool — and painting your own

- **The file** — `data/pioneer_skins.json` (it reaches the client like every data file: `scripts/sync-client-libs.*` copy it to
  `StreamingAssets/data/`, the WebGL manifest lists it). One skin per entry: `key`, `author`, five palette indices — `skin` (the
  bare skin under the face's transparent pixels), `hair` (0 = bald), `torso`, `arms`, `legs` (the tint under each part's
  transparent pixels and on its unpainted faces) — and the paintings in the Appearance editor's own format (`BodyPaint`: one
  base32 palette symbol per pixel, `0` = transparent, 32 × 32 chunks): `facePixels` (1024), `torsoPixels` (4096), `armPixels`
  (8192), `legPixels` (8192), `helmetPixels` (5120 or "" — a pioneer wears no helmet, the NPC rule "bare face = NPC"). Client.Tests
  `PixelSkinTests` validate it (`PixelSkinPool.Validate`: unique keys, palette indices, exact lengths, palette symbols).
- **The recolour** (`PixelSkins.Recolour`, per pioneer seed, deterministic): the pick from the pool; the four garment hue
  families — red (22, 8, 23), green (26, 11, 27), blue (28, 12, 29), purple (30, 13, 31), each dark, mid, light — permuted tone
  for tone on every part; the skin tone moved along light 7 → tan 6 → deep 21 (→ 19 as the deep tone's shade); the hair families
  brown (19, 5, 20), dark (16, 2, 17) and golden (24, 10, 25) rotated in the face and the hair only. Paint clothes in those
  family colours and they change from pioneer to pioneer; every other colour stays as painted.
- **The generated skins** — `uv run --no-project python tools/gen_pioneer_skins.py` rebuilds the eight explorers
  (trailblazer, stargazer, tinkerer, trapper, sailor, botanist, scout, nomad) deterministically and keeps every hand-made skin
  in the file; `--check` validates it.
- **Painting one in the game (Justus)** — in the game open **Tab → Character → Appearance** (or the main menu's **Avatar
  Designer**), paint the **Face**, **Torso**, **Arms** and **Legs** tabs and pick the base colours (keep the face's background
  transparent: it becomes the skin), then **Apply** — or **Save outfit** under a name, so it is kept beside your own look. The
  client stores both in `client_settings.json` in the game's data folder (`AppPaths.Root`: on Windows
  `%USERPROFILE%\AppData\LocalLow\JuMaVe Games\Blocks Beyond the Stars\`, or the folder of a portable install) — the applied
  look in `FacePixels`, `TorsoPixels`, `ArmPixels`, `LegPixels`, `HelmetPixels` and `SkinColor`/`TorsoColor`/`ArmColor`/`LegColor`,
  each saved outfit in `Outfits`. Then:

  ```bash
  uv run --no-project python tools/gen_pioneer_skins.py \
      --from-settings "%USERPROFILE%/AppData/LocalLow/JuMaVe Games/Blocks Beyond the Stars/client_settings.json" \
      --key justus_knight --author Justus --outfit "Knight" --hair 19
  ```

  `--outfit` takes a saved outfit (without it, the look you wear), `--hair` the pioneer's hair as a palette index (0 = bald; a
  player's look has no hair — the suit's helmet covers it). The colours are snapped to the nearest palette entry, an old 16 × 16
  face is scaled up, the same key replaces the earlier version. Run the Client.Tests `PixelSkinTests`, commit
  `data/pioneer_skins.json` — the next build's pioneers may wear it.

## Tests

`LodgeTests`, `LodgeResidentTests`, `NpcWorkTests` (§2–§4), `NpcGatheringTests`, `NpcWoodcuttingTests`, `NpcMiningTests`,
`NpcHuntingTests` (§5), `WandererTests` (§6); Client.Tests `NpcWorkToolsTests`, `ArrowFlightTests` and the extended
`AvatarGesturesTests` / `HeldItemShapesTests` (§7); `PioneerOutpostTests`, `PioneerTests` and Client.Tests `PixelSkinTests` (§8).
The shared arena is `NpcLifeWorld`.
