# SPS guide towers — the sky-lights, as ruins

> Status: **implemented** (#3893, part of epic #3891, 2026-11-21). On planets from terrain generation **47**
> (`WorldDescription.SkyLightsGeneration`, a server-side stamp — no chunk of any generation changes). Three ruins ship
> (§6). Their scripts: `tools/landmarks/sps_tower_*.py` (run by `tools/gen_landmarks.py`);
> `tools/landmarks/preview.py data/sps_towers/<key>.json` renders a PNG.

A **guide tower** is the ruin of one of the towers the Scout and Pioneer Service raised on every prepared settlement
world: its light led the colony ships in ("the sky-lights"), its antenna was the ground end of the relay lattice. No tower
stands whole anywhere (canon C19, [LORE_STRUCTURE.md](LORE_STRUCTURE.md)). This is the *how it fits together* reference
for the structure kind; the pieces it reuses are documented where they live: [LANDMARKS.md](LANDMARKS.md) (template body,
ruin pass, overlay storage, far silhouette, protection), [STRUCTURE_PIPELINE.md](STRUCTURE_PIPELINE.md) (placement rules,
records), [GIANT_SHIPS.md](GIANT_SHIPS.md) (the exemplar of a kind of its own on the landmark stack). What a tower *does* —
its console's lore, its lights turning blue, the sleeper in the guard post — is the tower's life and lives in its own
files (`GameServerSpsTowerLife*.cs`, `GameServerSleeper*.cs`): §8 (the console, the chip, the lights, the map), §9 (the
guard post and the sleeper) and §10 (what the story packs can name). The kind itself (§1–§7) only stamps the ruin and
hands over a record.

## 1. A kind of its own

A tower is a `StructureTemplate` flagged `spsTower` (the folder decides: `data/sps_towers/<key>.json`,
`SpsTowerRules.DataFolder`), never a landmark: the landmark pool refuses one, and no settlement, station, giant-ship or
civilization pool offers it. It has its own chance, whitelist key (`sps_towers`), placement record kind and map type
(`sps_tower`), its own list on a loaded world (`LoadedWorld.SpsTowers`, `LandmarkInstance.SpsTower`) and its own record
in the world metadata (§4). Everything else is the landmark's: the compact body, the ruin pass, the cell writer, one
structure overlay per tower, chests, sound emitters, the streaming lift and the far silhouette.

It deliberately borrows two **landmark names**: a template's display name is `landmark.<key>.name` and its Codex text
`landmark.<key>.desc`, and the scanner's ledger is the landmark ledger (`landmark:<body>:<key>`). So the Codex lists a
scanned tower among the landmarks with its own text and no client change was needed for that.

## 2. The template

| field | value |
|---|---|
| `spsTower` | `true` (informational — the folder sets the flag) |
| `ruin`, `ruinProfile`, `ruinStrength` | always `always`; `lattice`; 0.3 |
| `protection` | always `protected` — nothing of a tower can be mined, its chests stay lootable |
| `groundRow` | 1: row 0 is the foundation slab, sunk into the ground; row 1 lies on it |
| `minTerrainGeneration` | 47 |
| `ruinZones` | the guard post's box as `masonry` (a wooden hut decays like a wall, not like a frame) |
| `nameKey` | `landmark.<key>.name` (set by the pool when empty) |
| `requires` | `atmosphere`, `inhabited` — stated for the reader; the stamper checks both itself |

**Markers** (`SpsTowerRules.Markers`):

| marker | settings | meaning |
|---|---|---|
| `sps_console` | — | the relay console: exactly one, in the relay room at the foot |
| `sps_lamp` | `of=tank` for one of the capsule tank's two | an emergency lamp. It needs a block under it and a free cell above it |
| `sps_tank` | — | the capsule tank of the guard post (two cells high: the marker's cell and the one above) |
| `sps_guard_post` | `w`, `h`, `l` | the low corner of the guard post's box and its size (template coordinates) |
| `chest`, `sound`, `loot`, `data_terminal`, `relic_cache` | as for landmarks | what a ruin keeps |

The three tower markers are among the markers a ruin keeps (`RuinMarkerRules.Kept`), so they survive the pass and come to
rest on what is left like a chest does. **The body never holds the console or the tank block** — the generator places
them (§3).

`SpsTowerRules.Problems` (plus `GameContent.SpsTowerProblems`: known blocks and loot tables, the three placed blocks
present in the content) drops a template that is no ruin, not protected, flagged as another kind, without exactly one
console, without a lamp of its own, with a tank outside a guard post box, or with a marker of another kind — with a
warning, never a failed load.

## 3. The build — `SpsTowerGenerator`

`SpsTowerGenerator.Build(template, seed, turns, content, surface, atmosphere, guardPost, revision)` is the landmark build
with three steps of its own:

1. **The guard post.** About 40 % of the towers have one (`SpsTowerRules.GuardPostChance`,
   `SpsTowerGenerator.RollsGuardPost` — a hash of the tower's seed); the save's **guaranteed tower always has one** (§4).
   A tower without one loses every cell and marker of the `sps_guard_post` box *before* the ruin pass. The decision is
   pinned in the placement record (`post=0|1`).
2. **The ruin pass** — the landmark's (`LandmarkGenerator.Finish`) at the pinned revision: the lattice profile with its
   breaks, the template's ruin zones, a scrap rubble palette, overgrowth only with air. Kept cells (the spine, the ladder,
   the deck round the spine, the relay room's roof, what a lamp stands on) hold the way up to the highest platform.
3. **The dress** (a `LayoutDress`, after the pass and the marker filter). The pass may switch any authored light off, and
   a console in the body would be eroded like any block — so the tower's own blocks are placed afterwards: `sps_console`
   in the console marker's cell, a burning `light_red` in every lamp marker's cell, two stacked `capsule_tank` cells on the
   tank marker. The markers stay in the layout; `SpsTowerGenerator.PartsOf(layout)` reads them back.

The build is pure and deterministic: the same template, seed, turn, guard post and revision give the same ruin, on the
stamp and on every later load.

## 4. Placement, the guarantee and the record

`StampSpsTowers` (GameServerSpsTowerStamp.cs) is one line of the structures boot stage, right after the guardian giants
(… landmarks → giant wrecks → guardian giants → **guide towers** → buried ruins → …).

- **The gate.** The `sps_tower` kind of `data/structure_types.json` (`StructureTypeRules.SpsTower`): enabled, the world's
  terrain generation at least its `minGeneration` (47), conditions `atmosphere`, `notVoid`, `notGas`, `notAsteroid`,
  whitelist key `sps_towers`. Plus the host's `ServerConfig.PlaceSpsTowers` and the world option
  `WorldDescription.SpsTowers` not *Off* (CLI `--sps-towers`); never the finale body; never a framework civilization's
  home body; only a body untouched at load **that carries an inhabited settlement** (`InhabitedSettlementCount > 0` — the
  settlements are stamped before). A body that was asked and said no records a skip; a world that asks nobody decides
  nothing and may ask again later.
- **The roll.** `LaneRoll(seed, "spstower:roll:<body>")` below the kind's chance (0.18; `ServerConfig.SpsTowerChance`
  overrides it) × the structures frequency × the world option — about every fifth to sixth inhabited world.
- **The guarantee.** While `WorldMetadata.SpsTowerGuaranteeSpent` is false the roll is skipped: the first eligible body a
  save stamps carries a tower for sure. A tower that finds no seat leaves the guarantee for the next body; a chance of 0
  (or the switch off) means no guarantee either. An admin's hand stamp never spends it. The guaranteed tower **always
  keeps its guard post** (`PlanSpsTower`, #3897 — the mission "The Sleeper" needs a capsule tank in reach), whatever its
  seed's roll says; every other tower rolls it as before.
- **Pick, turn, seed, guard post.** One of `SpsTowersFor(…)` that the body is four footprints around, by weight, a quarter
  turn, the instance seed and the guard post — each a hash of its own lane. A pick without a seat gives way to another one
  (up to four tries): a lower ruin may fit where the tall one does not.
- **The seat.** The guaranteed search with the footprint registry (`keepClearFrom`: 60 from a settlement's footprint, 24
  from the pads, 10 from the crash site, 16–24 from wrecks, ruins, factories, the landmark, the giants and the huts), the
  landmark's site rule and grade, dry ground only — **and under the sky**: `PlanetType.AtmosphereHeight` is an *absolute
  world row* that a high massif may rise above, so a seat is refused when the tower's top would come within
  `SpsTowerRules.AtmosphereMargin` (8) rows of it (`SpsTowerFitsUnderSky`). On the worlds with a low sky (the lava worlds'
  150, the ash worlds' 170) only the lower ruins seat.
- **Pinned.** One placement record (`sps_tower`, index 0; a hand stamp takes the next index) with the template key, the
  seat and, in its layout field, `turns`, `rev`, `seed`, `air`, `post`, `fp` (the template fingerprint) and
  `storage=overlay`.
- **Written once** through the landmark cell writer into one structure overlay (key `sps_tower:<index>`), registered in
  the footprint registry as `sps_tower` with a **guard box** from the plinth's foot to its top: mining, placing, the
  blaster, fire and every other protection site refuse a player (`@srv.protect.structure`); an admin passes; the chests
  are ordinary one-time landmark chests.
- **Every later load** replays every record of the body, whatever the switches say now. A record whose template left the
  content or changed since (another fingerprint) keeps its blocks and its tower record and re-derives nothing else.

### The tower's record

`WorldMetadata.SpsTowers` holds one `SpsTowerRecord` per placed tower, written once by the stamp (and never rewritten —
its state belongs to the tower's life):

| field | meaning |
|---|---|
| `LocationId`, `Index`, `SiteId` (`<location>#<index>`) | which tower |
| `Template`, `Turns` | what it was built from |
| `X`, `Y`, `Z`, `Width`, `Length`, `Height` | the footprint's low corner at the seat (Y = the world row of the ground row), its size as it stands, its rows above the seat |
| `Console` | the cell of the `sps_console` block |
| `TowerLamps` | the cells of the tower's own lamps (relay room and mast) — `light_red` as found |
| `HasGuardPost`, `Tank`, `TankLamps` | whether the guard post stands, the LOWER cell of its tank, the cells of the tank's two lamps |
| `Lit`, `TankOpened`, `GuardName`, `GuardSettlement` | state written by the tower's life (#3895–#3897): the lights turned blue, the tank was opened, what its guard is called, where he went |

Every cell is a canonical world cell (`SpsTowerCell` — a JSON-friendly triple with `ToVector()`).

**Accessors** (GameServerSpsTowers.cs): `SpsTowersPossible` (generation ≥ 47 and every switch on), `AnySpsTowerInSave()`,
`SpsTowersOnWorld()` / `SpsTowersOnWorld(location)`, `NearestSpsTower(from)` (and an overload with a location that also
returns the gap to the footprint), `FindSpsTowerByConsole(cell)`, `FindSpsTowerByTank(cell)` (either tank cell),
`FindSpsTower(location, index)`, `SpsTowerCentre(record)`, `SaveSpsTowers()`, `RevealSpsTower(record)` /
`SpsTowerRevealed(record)`.

## 5. Finding one

- **The map.** On everyone's map (POI type `sps_tower`, the ruin's name) once a player on foot comes within 100 blocks of
  its footprint (`SpsTowerRules.RevealRange`); `RevealSpsTower` puts one there without a visit.
- **The client's list.** A tower rides in `LandmarkList` after the giant wrecks and civilization sites with
  `NetLandmark.Kind` = `sps_tower` — its far silhouette is drawn like a landmark's (an older client knows no other way).
- **The game event** `sps_tower:found` (`GameEventKeys.SpsTowerFound`, argument: the site id) once per approach within 48
  blocks (`SpsTowerRules.FoundRange`), measured only while a story step listens. The story predicates
  `world:sps_towers` (a tower stands in the save, or `SpsTowersPossible`) and `world:sps_tower_near`.
- **The scanner.** Any block of a tower reads as the tower: readout kind `sps_tower`, subject `landmark_<key>`, info
  `ui.scan.sps_tower`, threat *inert*, a relic's knowledge once per body and ruin.
- **Admin** (behind the world's cheat rule): `/spstower list`, `/spstower <key> [post|nopost]` (a ruin six blocks in front
  of the admin, a fresh seed each time, the guard post forced or left to the seed), `/tp tower [n]` (also `towers`,
  `leitturm`, `turm` …).

## 6. The three ruins and their scripts

All three are the same mast broken in three ways (`tools/landmarks/_sps_tower_parts.py`): a square lattice of blue-grey
Service steel (the giant ships' palette — `iron_block` dyed gunmetal, SPS blue ring beams, lighter braces), 21 wide at
the foot and 7 at the top deck, six decks, a kept spine with a ladder; the relay room between the legs (steel walls, an
instrument wall, the console, two chests, two emergency lamps on brackets); the guard post beside the foot (a wooden hut,
half its roof gone, the tank between its two lamps, a chest).

| key | name | size (W × H × L) | what is left |
|---|---|---|---|
| `sps_tower_stump` | Fallen guide tower | 88 × 41 × 33 | the foot to its second deck (chest there), the mast lying beside it along +x, sunk a little into the ground |
| `sps_tower_leaning` | Leaning guide tower | 27 × 96 × 33 | the whole mast, 13 blocks out of true at the tip, its corner chords kept; the ladder runs to the top deck (chest there) |
| `sps_tower_broken` | Broken guide tower | 44 × 50 × 33 | the foot to its third deck (chest there), the upper half folded over its edge and hanging down to the ground |

Everything is drawn through a mapping `T(lx, y, lz) -> cell` (`upright`, `lying`, `folded`), and every member is a line —
edge-connected under any mapping, which is what the ruin pass's settle needs. A piece that is meant to stay must touch the
ground row or a kept cell: the lying mast's lower chords are sunk into the ground, the folded piece's chords end on it.

```bash
uv run --no-project python tools/landmarks/sps_tower_stump.py     # -> data/sps_towers/sps_tower_stump.json
uv run --no-project python tools/gen_landmarks.py sps_tower_leaning
uv run --no-project python tools/landmarks/preview.py data/sps_towers/sps_tower_broken.json --cut y=4 --cut z=11
```

A new ruin: a script `tools/landmarks/sps_tower_<name>.py` with a `build()` returning a `TowerDesign` (its `write()` checks
the tower rules — one console, a block under every lamp and nothing above it, the tank inside the post — and writes to
`data/sps_towers/`), the two locale keys `landmark.<key>.name` / `.desc` in `en.json` and `de.json`, and the file in the
client's `StreamingAssetsCache` fallback list (the browser build reads `data/` through it).

## 7. What the client knows

Nothing new is required to see and find a tower: an older client draws the silhouette as a landmark's, shows the scan
card with the ruin's name and lists the discovery among the landmarks. What the current client adds:

- **The planet map**: the POI type `sps_tower` is drawn with the beacon icon in the sky-lights' blue and has its own
  legend entry (`ui.map.kind_sps_tower`; `WorldMap.SpsTowers.cs` — no new image). The legend's fourth row moved the
  map's buttons down by one row.
- **The world option**: a slider on the create-world panel's structures page (`ui.worldopt.sps_towers`,
  `WorldCreationOptions.SpsTowers`, sent as `--sps-towers` only when it is not *Normal*); a playtest task may set it
  (`PlaytestWorldPlan.AllowedOptions`).
- **The sleeper's look**: an NPC with the look `sps_guard` is built in the suit and wears the full Guardian armour set
  with blue lights, the Guardian sword (its lights blue too) in his hand while he stands (`EscortGuardLook`,
  `PlayerAvatar.EscortGuard.cs`, `NpcView.EscortGuard.cs`).
- `/spstower` in the chat (`ChatUi`).

## 8. The life of a tower — console, chip, lights, map

> Status: **implemented** (#3895, #3896, 2026-11-21). `GameServerSpsTowerLife.cs` (the mechanics),
> `GameServerSpsTowerLifeStory.cs` (VEGA, the settlers, the predicates, the guide markers). "Red turns blue again" is the
> package's leitmotif: a tower, a tank, a bridge ([GIANT_SHIPS.md](GIANT_SHIPS.md) — the escort cruiser's wreck), the
> finale's core hack.

**The use.** The block `sps_console` has one use handler (`UseSpsConsole`, GameServerEscortCruiserBridge.cs): a cruiser's
bridge first, else the partial hook `UseTowerConsole`, which this part implements — a console that
`FindSpsTowerByConsole` knows is a tower's.

**Without a chip** every use shows the next record of the console's memory the player has not read. The records are lore
texts of the site kind `sps_tower` in the VEGA pack's `lore_sites.json` (so each read raises `lore:read` with `sps_tower`
and lands in the Journal's field records), read in a fixed order, not drawn by weight:

| order | lore key | what it is |
|---|---|---|
| 1–4 | `tower_ships`, `tower_recall`, `tower_red_night`, `tower_guard_log` | the colony ships led in · the recall order · the night the lamps turned red · a guard's last entry |
| then | `tower_echo_1` … `tower_echo_3` | ECHO's last call in three pieces. **One piece per tower and player** (the next one they have not heard; per-player key `story:sps_tower:echo:<site>`), given once the four records are read — or at once as the tower's message when it is lit. Three towers tell the whole call |

They are records, never narration (LORE_STRUCTURE §G): none names the Guardian or says what the players are, so none is
gated by the world's knowledge level. A line (`srv.sps_tower.console_locked`) says that the console is locked red and what
a hacking chip would do. Without the VEGA story the console has no records and only says that.

**The chip** (`LightSpsTower`). With a `hacking_chip` in the backpack the use takes **one** and lights the tower, **once
per tower**:

1. every cell of `TowerLamps` that holds `light_red` (or its dark twin) becomes `light_blue` — real block edits, stored
   with the call. The clients are told lamp by lamp from the lowest to the highest over two seconds
   (`SpsTowerLampSweepSeconds`; `BlockChanged.Moved`, so no place pop) by the per-world step `TickSpsTowerLife` — the world
   is final at once, only the telling is staggered, so nothing is lost if the server stops in between;
2. the cue `lights_to_blue` (`SoundFx`) plays at the console, `SpsTowerRecord.Lit` is set and saved;
3. `sps_tower:lit` is raised for the player (argument: the tower's site id);
4. the tower shows what it still knows (below); everyone on foot within 64 blocks reads the lines and hears VEGA;
5. the console plays the tower's message: the VEGA pack's cutscene `tower.message` when the pack defines it (once per
   player, anchored at the console), else this tower's piece of ECHO's call, else the next record.

**A player who comes later** finds the tower blue (VEGA: `vega.tower.found_blue`), reads the same records, and their first
use of the lit console raises `sps_tower:lit` for them too (per-player key `story:sps_tower:lit:<site>`) — their own story
step counts. **No chip is ever taken on a lit tower.**

**What the tower still knows** (`RevealWhatSpsTowerKnows`). On the lit tower's world these go on everyone's map
(`WorldMetadata.RevealedPois`, one save, one POI broadcast):

| revealed | not revealed |
|---|---|
| the landmark · the giant ship wrecks · the sites of the lost civilizations · the world's one-of-a-kind place (the observatory or the singing shrine) · the crashed ship nobody claimed · every guide tower of the world | what came after the towers went dark or is somebody's secret: huts and pioneer outposts, treasure chests, bandit camps, fragment signals, hunter camps — and the guardian giants, which the players find themselves |

The **settlements** (lived-in and ruined) are in every player's POI list from the start; the line only counts them
(`srv.sps_tower.revealed`: "settlements: N, other places: M").

**VEGA** speaks at three moments — the first approach (within the found range, once per player and tower; the tower
stamp's `sps_tower:found` event is its own and is not raised twice), the first read at a tower, the lighting — and her
line follows what the **player's own story** knows, like the guardian giants' (`SpsTowerLoreOf`, per-player beat
milestones): key `story.vega.tower.<found|read|lit>.<level>` in the pack's locale.

| level | when | she says |
|---|---|---|
| `puzzled` | before "The Service" (B2) | a broken tower with red lamps — no record of it |
| `service` | from B2 | a guide tower of the Service — the sky-lights |
| `verdict` | from "The Verdict" (B6) | the night it turned red — that was the Guardian |
| `blue` | B6 heard and the core shut down | it may be blue again |

The Guardian is never named before the player's story heard of it. Without the VEGA story the neutral lines
`vega.tower.found|found_blue|read|lit` of the engine's locale. One **insight** for the Journal's VEGA notes: `sps_tower`
(`story.vega.insight.sps_tower`, once per player, from B2) at a read or lit console.

**The settlers.** A villager's hint may be the old sky-light (`TryEmitSpsTowerRumour` in the NPC hint, after the lodge
hint): the nearest tower of their world that is not on the map goes on it, with direction and distance
(`npc.hint.sps_tower`, `npc.hint.sps_tower_blue`). The flavour lines' world tags (`WorldStoryTags`) gain `sps_tower_red`
while a tower of the world is unlit and `sps_tower_blue` once one is lit; the VEGA pack's `flavourLines` use them
(`fl_tower_red`, `fl_tower_legend`, `fl_tower_children` — fear and legend; `fl_tower_blue` — "the sky-light is blue again").

## 9. The guard post and the sleeper

> Status: **implemented** (#3897, 2026-11-21). `GameServerSleeper.cs`, `GameServerSleeperStory.cs`. Canon C20 (the escort
> wing and its armour), C21 (the sleeper) in [LORE_STRUCTURE.md](LORE_STRUCTURE.md).

**The tank.** `capsule_tank` is usable (`usable` in `data/blocks.json`, handler `UseCapsuleTank`); either of its two cells
answers. A tank that belongs to no guard post of a tower record does nothing. Without a chip: `srv.capsule.sealed`. With
one the use takes **one** chip and, in one transaction, turns the two `TankLamps` blue, makes both tank cells
`capsule_tank_open` (walk-in), sets `SpsTowerRecord.TankOpened`; the cue `tank_opens` plays; **everybody on foot within
24 blocks** gets `capsule:opened` (argument: the tower's site id), the line `srv.capsule.opened` and the VEGA pack's
cutscene `tank.opens` (anchored at the tank) — or VEGA's line `story.vega.tank.opens` while the pack has no such scene.
The tower's own lamps stay as they are: the console and the tank are two locks. **Once per tank**: an opened tank is
another block with no use, so a second chip can never be spent on it.

**Only with the VEGA story.** The sleeper is that story's figure (`SleeperCanWake`: the running story is the VEGA pack and
holds the dialog `sps_guard`). In a world with another story or none the tank says `srv.capsule.silent` and takes no chip.

**The guard** steps out beside the tank: an ordinary `ServerNpc` (role `settler` — a settler never trades; no NPC can be
attacked) with the look id **`sps_guard`** on the existing `NetNpc.Look` field (the client draws the escort wing's armour
with blue lights; an older client draws a plain person), the nameplate `npc.role.sps_guard` and the story character id
`sps_guard`.

**Every guard post holds a different guard.** His name is given when his tank is opened and kept in
`SpsTowerRecord.GuardName` (so he has it after a reload and in the settlement): the first tank a save opens wakes the
guard named by the locale key `chr.sps_guard.name`, each later one the next free name of the language-neutral pool
`SleeperNames` (Mara, Tobin, Selka, Joren, Nima, Halden, Ruvi — none of the stories' names, none of the Sounders at the
gates; a save with more opened tanks starts the pool again). All guards are the same story character, so the
once-per-player rule holds across them: whichever guard a player talks to first gives the full talk and the gift, the
others have `sps_guard_again` for that player. No dialog text names the guard — the nameplate does.

**His dialog** is pack data (`data/stories/vega_protocol/story.json`, `dialogs[]`, bound by `character`): `sps_guard`
(once per player, five nodes of two replies) and the repeatable `sps_guard_again`. Either last reply of `sps_guard`
carries `gift:guardian_plasma_rusted:1` — the dialog engine's gift, which puts the item into the backpack and spills it at
the player's feet when the backpack is full (never lost); the dialog is done for that player in the same step
(`dialog:sps_guard:done`), and `dialog:done` with `sps_guard` is raised. What he says holds for every clone and says
nothing of what he is.

**VEGA's truth** follows the player's own story (`TickSleeperAftermath`, 1 Hz, for a player who talked the dialog through
and stands within 16 blocks of him): before "What You Are" (B9) the line `story.vega.guard.unsure` (once); from B9 the
insight `sps_guard` — "He is like you" (`story.vega.insight.sps_guard`, once, kept in the Journal's VEGA notes).

**Where he lives.** He stands by his tank until a minute (`SleeperLeaveSeconds`) after his first conversation ended; then
he is taken away and put down in the **nearest inhabited settlement** of that world — no walk across the world — and
`SpsTowerRecord.GuardSettlement` holds its name (players near the post read `srv.capsule.guard_left`). NPCs are runtime
state: `EnsureSleepersPlaced` (1 Hz on a world with a tower) puts every guard of an opened tank back from the record
whenever his world was loaded — into the recorded settlement while people still live there, else the nearest inhabited
one (recorded), else by his tank. So a tank that was opened and never talked at has its guard in the settlement after
the next load too.

**The record fields the life writes** (everything else is the stamp's and is never rewritten):

| field | written by | when |
|---|---|---|
| `Lit` | `LightSpsTower` | a chip at the console |
| `TankOpened` | `OpenCapsuleTank` | a chip at the tank |
| `GuardName` | `OpenCapsuleTank` (`NextSleeperName`) | with the tank — the guard's name, never changed afterwards |
| `GuardSettlement` | `RecordSleeperHome` | the guard moved (or was placed again) into a settlement — its name |

## 10. For the story packs

| what | name | notes |
|---|---|---|
| events | `sps_tower:found` · `sps_tower:lit` · `capsule:opened` | argument: the tower's site id (`<body>#<index>`). `lit`: the lighter, and each later player at their first use of the lit console. `opened`: everyone within 24 blocks |
| | `lore:read` with `sps_tower` | every record a console shows; a single text by its condition `lore:tower_echo_1` … |
| | `dialog:done` with `sps_guard` | the sleeper's dialog was talked through |
| predicates | `world:sps_towers` · `world:sps_tower_near` | the stamp's (§5) |
| | `world:sps_tower_lit_near` | a lit tower within the found range — the same for every player (`alreadyDoneIf` of "light a tower") |
| | `world:sps_tower_known` | a tower stands on a visited body (a text variant for "none found yet") |
| | `world:sps_guard_post_known` · `world:sps_guard_awake` | a tower with a guard post is known · a tank of the save was opened |
| | `world:sps_tower_lit_by_player` · `world:hacking_chip_carried` | #3899: this player lit a tower before (one of their `story:sps_tower:lit:<site>` keys — `alreadyDoneIf` of "light a tower" for a tower lit chapters ago) · they carry a hacking chip (backpack or hold) |
| guide markers | `nearest_sps_tower` | on this world the nearest tower's console; from a world without one the narrator names a visited world that has one and its system (inside that system the world is the flight target), the way a unique site is named; nothing while no tower is known |
| | `nearest_sps_guard` | the sleeper where he stands (post or settlement), else the sealed tank of the nearest guard post; from afar like the tower's |
| cutscenes (called when defined) | `tower.message` · `tank.opens` | VEGA pack (defined since #3899 — [CUTSCENES.md](CUTSCENES.md#the-sky-lights-scenes-3899)); once per player; fall back to a record / a line |
| insights | `sps_tower` (B2) · `sps_guard` (B9) | pack data, `insights[]` |
| missions | *The Sky-Light* (chapter 2) · *The Sleeper* (chapter 5) | #3899, kept missions of the VEGA pack: reach a tower and read its console · build a chip, light a tower, open a tank, talk to the guard — [STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md#the-sky-lights-missions-evidence-while-a-step-runs-and-echos-cruiser-3899). A step also holds by evidence while it runs (a chip carried, a tank somebody else opened) |

## 11. Key files

| Area | Files |
|---|---|
| The life (§8–§10) | `GameServer/GameServerSpsTowerLife.cs`, `…SpsTowerLifeStory.cs`, `…Sleeper.cs`, `…SleeperStory.cs`; hooks: `GameServerBlockUse.cs`, `GameServerTickSchedule.cs` (`TickSpsTowerLife`), `GameServerStoryConditions.cs`, `GameServerStoryPacks.cs` (`MarkRevealTarget`), `GameServerNpcHints.cs`, `GameServerNpcGreeting.cs` (`WorldStoryTags`); data: `data/stories/vega_protocol/lore_sites.json`, `story.json` (`insights`, `flavourLines`, `dialogs`), its `locales/`; tests: `SpsTowerLifeTests`, `SleeperTests` |
| Rules, template, pool | `Shared/Definitions/SpsTowerRules.cs`, `StructureTemplate.cs` (`SpsTower`), `Shared/Content/StructureCatalog.SpsTowers.cs`, `GameContent.SpsTowers.cs`, `ContentLoader.cs` |
| Placement rule, switches | `data/structure_types.json` (`sps_tower`), `StructureTypeRules.cs`, `WorldDescription.cs` (`SkyLightsGeneration`, `SpsTowers`), `ServerConfig.cs` (`PlaceSpsTowers`, `SpsTowerChance`), `ServerConfigOptions.cs` (`sps-towers`) |
| Build | `WorldGeneration/SpsTowerGenerator.cs`, `RuinMarkers.cs` (the kept markers) |
| Record | `Shared/State/SpsTowerRecord.cs`, `WorldMetadata.cs` (`SpsTowers`, `SpsTowerGuaranteeSpent`) |
| Server | `GameServer/GameServerSpsTowers.cs` (accessors), `…SpsTowerStamp.cs` (gate, guarantee, seat, record, stamp), `…SpsTowersWorld.cs` (map, found, scan), `…SpsTowersAdmin.cs` |
| Data and tools | `data/sps_towers/*.json`, `tools/landmarks/_sps_tower_parts.py`, `tools/landmarks/sps_tower_*.py` |
| Tests | `SpsTowerTemplateTests`, `SpsTowerStampTests` |
