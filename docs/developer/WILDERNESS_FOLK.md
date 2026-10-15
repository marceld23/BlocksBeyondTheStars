# Wilderness folk — lodges, their residents and wanderers

Epic #2639 (issues #2640–#2647). Before it every NPC was bound to a settlement, a base, a station, a landing pad or one unique
site, and only base residents really worked. The wilderness folk live and work **outside** the settlements: a **lodge** is a
small hut in the open land with one or two residents who gather, mine, chop or hunt around it; a **wanderer** is a named
traveller you meet on the way. This page is the map of that code. The NPC basics (routine, A*, doors, greetings, dialogues)
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
  most 6 different goods, 40 of each; a seventh good pushes out the oldest stack ("brought some to the village").
- **Map** — POI type `lodge`, named `poi.lodge` ("{0}'s hut"), revealed (`RevealPoi`, key `<loc>|lodge:<key>`) in
  `TickLodges` when a player on foot comes within 40 m, or by a villager (§4).

## 3. Residents and the job state machine (`GameServerLodgeResidents.cs`, `GameServerNpcWork.cs`)

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
  one `BlockChanged` per cell, a `tree_fall` `SoundFx` to players within 48 m at the start, logs banked as they fall (≤ 8);
  from the point where fewer than 4 leaves or 2 trunk logs would remain (`CollapseAt`) the rest comes down in one tick and a
  sapling is planted on the base cell (owner "", the normal 150 s sapling clock, persisted by the flora queue) — a crash
  leaves a tree or a sapling, never a stump. Fellings are RAM-only and dropped when their world unloads.
- **Mining** (`GameServerNpcMining.cs`, #2643) — `NpcMineCell(npc, cell, bank, NpcMineRefillSeconds = 600)` through
  `BreakBlockCore(null, "")`: only solid mineable terrain/ore, no fluid beside or above, nothing loose, a plant, a torch or
  lantern, a hanging plant or a player's edit on or above it, the six neighbouring chunks loaded. Every mined cell goes into
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

*(filled in with the wanderer implementation)*

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

## Tests

`LodgeTests`, `LodgeResidentTests`, `NpcWorkTests` (§2–§4), `NpcGatheringTests`, `NpcWoodcuttingTests`, `NpcMiningTests`,
`NpcHuntingTests` (§5), `WandererTests` (§6); Client.Tests `NpcWorkToolsTests`, `ArrowFlightTests` and the extended
`AvatarGesturesTests` / `HeldItemShapesTests` (§7). The shared arena is `NpcLifeWorld`.
