# Landmarks — giant authored buildings, derived into ruins

> Status: **framework implemented** (epic #3483, parts #3484–#3500, terrain generation 37); **the school club's
> eleven buildings** ship on top of it from terrain generation **39** (`WorldDescription.SchoolClubLandmarksGeneration`,
> save version 15) together with overlay storage (§4), ruin zones (§3), the span seat and fixed landmarks (§4) and
> settable protection (§5). The three small **samples** stay pin-only (`/landmark <key>`). Their generator scripts:
> `tools/landmarks/<key>.py`, run by `tools/gen_landmarks.py`; `tools/landmarks/preview.py <key>` renders a PNG.

A **landmark** is one giant building that stands on a planet on its own: a leaning tower, a city gate, a mine, a
statue. It is authored (and stays editable) in the structure editor, and most of them stand only as **ruins** that the
server derives from the intact design with the instance's seed — so the same tower never falls apart the same way
twice. This is the *how it fits together* reference; for the stamp chain around it see
[WORLD_GENERATION.md](WORLD_GENERATION.md), for the editor [STATION_SETTLEMENT_EDITOR.md](STATION_SETTLEMENT_EDITOR.md),
for the older ruins [FACTORIES_RUINS_AND_CLAIMING.md](FACTORIES_RUINS_AND_CLAIMING.md).

## 1. Why a kind of its own

Before the landmarks an authored template was a whole settlement (always intact and inhabited — a template without a
`vendor` marker even gets a fallback vendor), a plot or district module of a kit, a station module or an underground
structure. A single building on the surface had no stamper. And the game had five decay passes — the fallen
settlements, the monuments, the buried G.D.S. ruin, the abandoned rail station, the rune shrines — each wired into
its own generator; none could be called for an arbitrary structure.

## 2. The template

A landmark is a `StructureTemplate` with `landmark: true`, one file per landmark under `data/landmarks/<key>.json`
(the editor's own under `usercontent/landmark_templates/`). It never enters a settlement, module or underground pool,
and nothing else enters the landmark pool (`GameContent.Landmarks`, `LandmarksFor`).

| field | meaning |
|---|---|
| `ruin` | `always` = run through the ruin pass; `never` / empty = as built |
| `ruinProfile` | `masonry` (walls and towers, the default), `lattice` (light erosion, whole spans fall; from ruin revision 2 a tall frame loses its top, a long one a section of its span — §3), `underground` (cave-ins) |
| `ruinStrength` | 0..1, 0 = the profile's own (0.5; lattice 0.4) |
| `requires` | `atmosphere`, `inhabited` (the body carries at least one inhabited settlement) |
| `groundRow` | the template row that lies on the ground; the rows below are carved into the rock |
| `protected` | worlds below generation 39: an inhabited landmark — read-only like a lodge, its posts staffed (ignored on a ruin) |
| `protection` | generation 39 (every whole template, not only landmarks): `auto` (default) = the kind's rule — a landmark is protected while intact, open as a ruin; `protected`; `open` (§5) |
| `ruinZones` | generation 39: `[{ "x0", "y0", "z0", "x1", "y1", "z1", "profile", "strength" }]` — boxes (template coordinates, inclusive) that decay with their own profile / strength (§3) |
| `seat` | generation 39: empty = the guaranteed search, `span` = across water or a valley, a bridge (§4) |
| `minTerrainGeneration` | the oldest world generation that ROLLS it (the school club's landmarks: 39); 0 = every world. Records, fixed landmarks and `/landmark` do not ask |
| `nameKey` | the locale key of its display name (map, VEGA); empty = `name` |
| `planetTypes`, `pack`, `weight`, `pinOnly` | as for every template |

A landmark that is broken (an unknown word, a size above 256, an unknown block, a ground row outside the structure, a ruin
zone outside it or with an unknown profile, a span shorter than 12) is dropped at load with a warning — it never breaks the
load (`LandmarkRules.Problems`). A settlement or station template with an unknown `protection` word is reported and keeps
its kind's rule.

### The compact body (#3485)

One JSON object per cell costs about 120 bytes and an object in memory; a giant has 50 000 to 600 000 cells. A
template may therefore carry its plain cells — a block or a cavity with tint, glow, shape and the *keep* flag — as a
`palette` plus run-length `rows`, one string per (y, z) row: `"y,z:x.count.palette x.count.palette"`. Cells with
settings (markers, ports, Crystal Net devices) stay in `cells`. `TemplateBody` compacts, expands and — what the
readers use — walks the plain cells of either form without ever building cell objects. A hollow 256 × 128 × 256 hall
(600 000 cells) is under 1 MB.

The files are small, so they are loaded with the rest of the content and stay in memory in compact form; the dense
voxel grid exists only while a landmark is built.

### Marker settings (#3488)

A marker cell's `config` is its settings line, `key=value;key=value` (`MarkerSettings`). It travels as
`SettlementMarker.Data` through every offset and quarter turn.

| marker | settings |
|---|---|
| `chest` | `items=iron_plate:2-4,gold_ingot:1` (fixed; a range draws), `table=<key>` (a roll of `data/loot_tables.json`), both, or neither (the table `ruin_common`); `chance` (the chest exists at all); `block` (`wood_crate` / `crate`); `hidden=1` (a stone on top) |
| `vendor` | `theme` (the `marketTheme` of the recipes it sells), `inside=1` (sells only to a customer in its own closed room), `name` (a locale key for the nameplate) |
| `sound` | `sound` (a bundled loop), `radius` (blocks), `volume` (0..1) |
| `den` | `species` (an authored creature key, or `native` = the planet's first peaceful species), `count`, `radius` |

`data/loot_tables.json` holds named tables: rolls of weighted entries with count ranges and a chance. Every draw is a
hash of the chest's own cell, never the stamp's random stream — a chest's content does not depend on what was rolled
before it. The older marker kinds (`loot`, `chest` in a settlement, `relic_cache` …) keep their hard-coded rolls: their
streams are in saved worlds.

## 3. The ruin pass (#3487)

`LandmarkGenerator.Build(template, seed, turns, content, surface)` reads the body into a voxel grid, turns it by
quarter turns, resolves the material tokens (`@wall` … follow the planet) and — for a ruin — runs `StructureRuiner`.
Every choice is a stateless function of seed, lane and cell (hashes and value noise), so the same seed gives the same
ruin and no step moves another:

1. **Crumbled patches** — in coherent patches of columns (2-D value noise) everything above a cut height falls: a
   tower loses one flank of its top.
2. **Erosion** — bites out of the walls (3-D value noise, so a hole is a hole) plus a light speckle, both rising with
   height. The first course above the ground stays.
3. **Collapse spots** — ragged shafts through roofs and floors (masonry) or a ball cut out of the frame (lattice).
   *Breaks* (revision 2, lattice only) follow — see below.
4. **Openings** — glass is smashed; a light becomes its switched-off twin (`<key>_off`) or is gone.
5. **Cave-ins** (profile `underground`) — stretches of a tunnel or cavern fill with rock, never within five rows of
   the ground row: the way in stays open.
6. **Settle** — whatever no longer hangs together with the ground comes down (a flood fill through faces and edges,
   so a stepped arch and a diagonal lattice member hold).
7. **Rubble and overgrowth** on the ground where something fell.

What stays: cells the author marked **keep** are never touched and hold what hangs on them; ladders and shaped pieces
are never eroded but fall with the wall that carried them; rows at and below the ground row are anchored. A marker's
cell is never filled, and a marker whose floor fell comes down with it and rests on what is left. A ruin keeps only
the markers a visitor finds — chests, the classic loot markers, sounds and dens; residents, vendors, doors, rooms and
pre-built circuits are gone.

`StructureRuiner.Revision` is pinned with every placement: a stamped ruin is re-derived on later loads for its
markers, so a change of the pass needs a new revision that old records do not take. Since #3515 the record's `rev` is
read back and handed to `LandmarkGenerator.Build(…, ruin, revision)` (a record without `rev` replays as 1, a revision
the build does not know as the newest it knows); `StructureRuiner.Run` dispatches on it. Revision 1 is the pass above,
pinned by `StructureRuinerGoldenTests` and the landmark goldens.

**Revision 2 — the lattice breaks (generation 39).** A lattice's collapse balls rarely cut a member loose — a frame hangs
together through every deck and brace — so a lattice tower or bridge came out of revision 1 almost whole (2–7 % of the
Eifelnyde tower, under 1 % of the bridge). Revision 2 adds, right after the collapse spots, the **breaks**
(StructureRuiner.Breaks.cs): one to three thin planes (two or three cells, gently leaning, their faces frayed over three
cells) cut clean through the frame, never through a kept cell and never at or below the ground row; the settle then brings
down what no longer hangs on anything. A **tall** frame (at least 1.5 times as high above the ground row as its longest
side) is cut horizontally at 25–65 % of its height — its top falls; a **long** one (its longest side at least 1.5 times
its height and three times its other side) loses a section, 20–45 % of an unsupported run long, cut out between two planes
across the long axis — a run is a stretch of slices with no block on the ground row and no kept block (a span between two
pylons), picked with a weight of its length squared, so the main span breaks far more often than a short side span. A
compact frame has no breaks. In revision 2 a lattice whose ground row lies above the template's bottom (a bridge) also lays
no rubble on the ground row where nothing of the frame lies right below (no stone hanging in the air over the river).
Masonry, underground and hull build exactly what revision 1 builds. A FRESH landmark takes revision 2 on a world of
generation 39 and revision 1 on an older one (`LandmarkGenerator.RuinRevisionFor`), so an older world's new landmarks —
and their records — stay as that world's build stamped them; a record keeps the revision it pins. The plain
`StructureRuiner.Run` without a revision runs revision 1.

**Shared with the other ruins (#3515).** `RuinOptions` change what a caller builds, every field defaulting to the pass
above: `Overgrowth` / `OvergrowthBlock`, a weighted `RubblePalette`, `ErodeShaped` (column cylinders, arch ramps),
`StrengthAt` per column (`RuinOptions.SpareBox` spares a plot), `ProtectRowsUpTo`, a fixed `LightOffBlock`,
`Substitutions` (weathering after the openings: stone → moss stone, panel → rusted panel, empty target = gone) and
`Scorch` (a block and/or tint, one chance next to a breach, one elsewhere). Each new step draws on a lane of its own, so
none moves another. The profile `hull` (a wreck) has no ground: no crumbled patches, no height grading, no rubble, no
flora — erosion bites and collapse balls breach it anywhere, and what stays is what hangs on a kept cell, a protected
row or the largest piece left. Landmarks refuse `hull` (`LandmarkRules.Profiles`; `AllProfiles` lists all four). The
marker filter and seating live in `RuinMarkerRules`; `VoxelGrid.Over` / `CopyOf` / `CopyTo` run the pass over another
generator's block, tint and shape arrays, `KeepWhere` / `KeepBox` mark kept cells.

**Ruin zones (the school club's landmarks).** A template's `ruinZones` give parts of one landmark their own decay (the
lattice tower on the masonry pyramid). `LandmarkGenerator` turns the boxes with the structure and calls
`StructureRuiner.RunZoned` (StructureRuiner.Zones.cs) at the landmark's revision (revision 2 breaks a lattice zone the way
it breaks a whole lattice — the Eifelnyde's slender upper shaft loses its top): every zone in order runs the decay steps (1–5,
the cave-ins included) on a window of the grid that is exactly its box — its own heights, patches and spots, as if it were
the whole structure — with its profile, its strength and the instance seed mixed with its index, every cell of an earlier
zone kept; then the cells of no zone run the decay steps over the whole grid with the template's profile and strength, all
zone cells kept (so they fall exactly as without zones); then ONE settle, rubble and overgrowth over the whole grid, with
only the author's keep cells (what fell in a zone counts for the rubble of its column). A template without zones never
takes this path — its ruin is byte-identical to before. The editor's ruin preview runs the same `Build`.

## 4. The stamp (#3486, #3490)

`StampLandmarks` is one line of the structures boot stage, after everything that stands on the ground and before the
buried ruins — so the vaults, cubes, chests and unique sites stamped later keep clear (`OverlapsAnySettlement`; on terrain
generation 38+ the footprint registry, #3514, which also replaces the landmark's own reserved list — see
[FACTORIES_RUINS_AND_CLAIMING.md](FACTORIES_RUINS_AND_CLAIMING.md)).

- **Gate.** `WorldDescription.LandmarkGeneration` = 37 (the framework); `SchoolClubLandmarksGeneration` = 39 =
  `CurrentTerrainGeneration` (overlay storage, the span seat, fixed landmarks, another candidate, settable protection;
  the school club's templates carry `minTerrainGeneration: 39`, so a gen-37/38 world never rolls them). A server-side
  stamp: no chunk of any generation changes.
- **Where never.** An asteroid, an airless, gas or void world, the finale body; with `PlaceLandmarks` off; on a type
  with a structure whitelist unless it names `landmarks` — and then only landmarks whose `planetTypes` name that type
  (Soaniri, #3500).
- **Roll.** `LandmarkRolls`: a hash of seed and body below the `landmark` chance of `data/structure_types.json` (0.34; a set
  `ServerConfig.LandmarkChance` overrides it, #3517) × the structures frequency — about every third planet.
- **Pick, turn, seed.** One of the planet's candidates by weight (only those the body is four footprints around), a
  quarter turn and the instance seed, each a hash of its own.
- **Seat.** The guaranteed placement search (#586) with every pad and structure reserved. A landmark that only stands
  on the ground may stand in water on its piers; one with an underground part needs a flat or sloped dry seat.
- **Generation 39** (`WorldDescription.SchoolClubLandmarksGeneration`, GameServerLandmarkSeats.cs):
  - *Another candidate.* A pick that finds no seat gives way to another one (a fresh hash per try, up to four tries);
    an older world keeps its one pick.
  - *The span seat* (`seat: span`, a bridge). The template's ground row is its **deck row** — the banks' level: its ends
    rest on the banks there, the rows below it (piers, pylon feet) hang into the gap. (The shipped bridge sets it just
    under its trusses, five below the road, so the whole span hangs in the air and its lattice breaks can drop a section;
    an embankment and a short stair at each end take the road down to the bank.) The search draws spots around pad 0 (2000
    per orientation, clear of every reserved box) and takes the first where, on three lines along the long axis, the outer
    eighth at each end is dry ground (no water, sea ice or lava) within `SpanBankTolerance` (4) of the deck row — the mean
    of the two outermost banks —, between the ends the ground nowhere rises more than that above the deck row and nothing
    is lava, and ONE unbroken gap — columns that are, on all three lines, water whose surface lies at least two below the
    deck row or ground at least `SpanGapDepth` (12) below it — runs at least `SpanGapLength` long (40 blocks, or a sixth of
    the long axis when that is more, or its middle third when that is less) with its middle in the middle third; the pylons
    may stand in the water on their piles. (Until this was relaxed the whole middle third had to be gap: the bridge seated in
    about one of four boots and never on the river or island worlds.) The plan's quarter turn is tried first, then one more (the
    long axis the other way); the record pins the turn that stood. Seat `span` in the record. The writer carries no plinth
    into the gap (only where a foot rests on a bank) and carries every pier that reaches the template's bottom row down to
    the bed or the valley floor (at most 48). No spot → no seat, and the body takes another candidate.
  - *Fixed landmarks* (`fixedLandmarks` of a planet type: template keys; Soaniri: the castle and the singer statue).
    Every body of the type carries each of them, always, instead of a rolled one (the roll is off for that type; packs,
    planet types, conditions and `pinOnly` are not asked) — each its own record (index = its position in the list), seed
    and turn (hashes of its key and the body) and seat; the footprint registry keeps the second clear of the first and of
    the Sounder sites and monuments stamped before. A key the pool does not hold is reported at load and skipped; a type
    whose keys none resolve rolls as before. A fixed template should name its type in `planetTypes` (or be `pinOnly`) so
    no other planet rolls it. A body that already carries records but none for one of its fixed indices (a crash between
    two stamps, or a content update that added one) plans that one on its next load, its seat clear of every player-built
    cell (`StampMissingFixedLandmarks`).
- **Pinned.** One placement record (`landmark`, index 0): the template key, the seat, and `turns`, `rev`, `seed` in
  its layout field. A body that was visited before, a roll that said no and a failed seat all record a skip.
- **Written once** (feature `landmark:<index>`), in one transaction:
  - *above the ground row* the monument rule — no foundation plate; a column that carries something is cleared from
    the ground row to its top and seated on a plinth (a pile every fourth column in water);
  - *below it* only the template's blocks and its **cavity** cells carved to air; an empty cell keeps the planet's
    rock. Beside every cavity that touches air, a liquid or loose ground a stone seals it (the buried ruin's rule);
  - a crate block in every chest marker's cell that exists by its chance.

### Overlay storage (terrain generation 39)

On a world created at `WorldDescription.SchoolClubLandmarksGeneration` (39) the record's pin also says
`storage=overlay`, and the cells above are not written as a block edit each (a giant is half a million to four million
cells — every carrying column is cleared from its ground row to its top, so most of them are air). The stamp runs the very
same cell list (`WriteLandmarkCells`, through an `IStructureCellSink`) into a collecting sink that reads through to the
world — so a seal stone or a hidden stone decides on exactly what the cell-by-cell stamp saw — and stores the result as
ONE blob per placed landmark: `Shared/World/StructureOverlay` (cells grouped by canonical chunk, runs of consecutive local
indices with one block / dye / glow / shape, LEB128 varints, raw deflate, a format byte), table `structure_overlay`
(planet, key = the feature key `landmark:<index>`) in the SQLite, PostgreSQL (and its backup dump) and in-memory
repositories (and the browser's snapshot blob). A 200 × 100 × 200 hollow hall (3.9 million cells) is 82 000 runs and a
10 KB blob; the samples are well under 2 KB.

- **Applied on load.** A world reads all overlays of its body once (`StructureOverlayIndex`, in stamp order) and
  `ServerWorld.Store` applies a chunk's overlay cells to the generated chunk BEFORE its block edits: a mined stone, a
  placed block, a dug tunnel are ordinary edits on top and win — in a chunk with an overlay an edit always writes its
  dye, glow and shape, zeros included, so a plain block put over a stair or a dyed stone stays plain after a reload
  (a generated chunk has neither under an edit, so elsewhere the zero writes are skipped as before). A cell counts as built exactly where it differs from the
  generated chunk — where the cell-by-cell stamp would have written a row. Chunks already resident when a landmark is
  stamped get the cells at once (with `BlockSet` / `ShapedBlockReplaced`) and are re-streamed to the players who hold
  them; a cell that already carried a block edit (an admin's stamp over a build) gets a row with the landmark's cell, as
  the cell-by-cell stamp overwrote it — on a fresh body there is none.
- **Fluids.** Applying cells wakes no fluid, and neither did the stamp's `SetBlock`s (fluids wake on a player's edit);
  the seal stones are part of the overlay, so a tunnel is as dry as before.
- **The rest is unchanged.** Chests, their `GeneratedLoot` keys, emitters, dens and residents are re-derived from the
  record. A body whose only stamp is an overlay counts as materialised (`HasAnyBlockEdits`). A content update that shifts
  the block ids remaps the overlays' cells with the edits (`EnsureBlockPalette`). An overlay stored before a crash cut the
  feature mark short is never stamped again. Records without `storage=overlay` — every world below generation 39 — keep
  the cell-by-cell stamp byte for byte (`LandmarkOverlayWorldTests` compares both paths chunk by chunk). One exception:
  an admin's `/landmark` of a template made for a newer generation (`minTerrainGeneration` above the world's — the school
  club's giants) is pinned `storage=overlay` on any world (`LandmarkPlan.ForceOverlay`), so it never writes millions of
  rows.

Ruins are not protected: what players mine stays mined (from generation 39 unless the template says `protected`, §5).

## 5. In the living world

- **Chests** — a one-time `salvage` container in the crate's cell (`GeneratedLoot`), content as its settings say.
- **Sound emitters** (#3494) — the client is sent the world's emitters on entry (`LandmarkList`, tag 335) and plays
  the three loudest, one per sound, on pooled voices: silent at the radius, a smooth swell, full within 12 % of it
  (`SoundEmitterMix`).
- **Dens** (#3495) — while a player is within the den's radius + 48 it brings its pack up to the count, one animal a
  second, keeps them within the radius and out of the far-creature prune; an animal that was killed or tamed is
  replaced after five minutes. A den only houses a species the world's roster already has — it never adds one.
- **Discovery** (#3496) — on everyone's map (type `landmark`) when a player on foot comes within 80 blocks; VEGA
  points the first one out once per player; a scan of any of its blocks reads as the landmark and is worth a relic's
  knowledge once per body and landmark (ledger `landmark:<body>:<key>`, its own group in the Codex).
- **Inhabited landmarks** (#3499) — `protected` and not a ruin: the box, down through the plinth, is read-only through
  `IsSettlementBlock`; profession posts and `vendor` / `npc` markers are staffed on every load and keep their post.
  That is the rule of the worlds below generation 39.
- **Protection, settable (generation 39).** Every whole template — a settlement, a station, a landmark, an underground
  structure, a generator entry — names its `protection`: `auto` (default) keeps its kind's own rule (an inhabited
  settlement and a station protected, a ruin and an underground structure open; a landmark protected while **intact**),
  `protected` and `open` overrule it. A protected box rides on the structure's own entry in the footprint registry
  (`StructureFootprints.GuardBox`, so the keep-clear checks see the same boxes as before) — a landmark from the plinth's
  (or the piles') foot to its top, a settlement from 48 under its floor, an underground structure from its floor to the
  surface — and ONE query, `IsProtectedStructureCell`, sits beside the station check at every protection site: mining,
  the area burst and the drills, placing (rule `structure`), blueprints, the construction guard, multi-cell forms, the
  blaster and the pump, the Crystal Net's bridges and pistons, fire, the Guardian's reward, the sound chambers, the NPC
  miners and gatherers. A player is told `@srv.protect.structure`; an admin passes. `open` switches a kind's own check off
  for that one structure (a station's hull; an intact settlement's box for what PLAYERS do — mining, the blaster, the
  pump, `IsSettlementGuardedForPlayers` — while fire, the villagers' and wilderness folk's work and the hunter sites still
  keep out through `IsSettlementBlock`); the old per-kind checks stay for their kinds.
  Residents no longer depend on protection: every **intact** landmark staffs its post markers (an open burger restaurant
  still has its vendor), and VEGA calls only a landmark with residents a house. GameServerStructureProtection.cs.
- **Admin** — `/landmark list`, `/landmark <key>` (cheats on: stamps one six blocks in front of the admin under the
  next record index, with a fresh seed each time), `/tp landmark [n]`.

## 6. Seeing it: streaming and the far silhouette

- **Streaming (#3489).** A near chunk column streams three chunks above the player, a far one its surface chunk plus
  one. A column that carries a landmark streams up to the landmark's top *in that column*, capped by the player's
  chunk sphere (the sent-set prune radius minus one): a chunk beyond it would be dropped and sent again every pass.
  The stone text tablets keep their own uncapped lift.
- **Far silhouette (#3497).** Beyond the streamed chunks a build is a height blob per 8- or 32-block cell. The server
  sends a coarse model of each landmark with the list (`LandmarkSilhouette`: one cell per 4³ blocks from the ground
  row up, filled from three solid blocks, a palette of block ids and dyes). The client meshes its skin
  (`LandmarkSilhouetteMesh`), colours it from the block atlas and draws it with the far terrain's shader; vertices
  marked `TEXCOORD0.x < 0` are cut by a sphere around the player (`_Sc_LandmarkClip`) instead of the column mask, so
  the top of a tower above the streamed sphere stays drawn. The far tiles leave a landmark's cells out.
- **From the overlay (generation 39).** A landmark stored as a structure overlay takes both the column tops of the lift
  and its silhouette (`LandmarkSilhouette.FromCells`, the same model `Build` makes of a layout with these blocks) from
  its overlay's cells — crates and hidden stones included.

Known limits: the model comes from the pinned layout, so a landmark players dismantled still shows whole from afar;
with the far view off it is drawn up to 512 blocks and only as far as the camera's far plane reaches.

## 7. Authoring

- **In the editor** (town editor, *Use as → Landmark*): the room is 256 blocks a side; the form sets ruin, profile,
  strength, conditions, the ground row, the seat (on the ground / a span), the ruin zones (a list with corners, profile
  and strength, drawn as wire boxes, set by two clicks with the palette's *Ruin zone* tool — `StructureEditor.Zones.cs`)
  and the **protection** picker (auto / protected / open — in every *Use as* form of a whole structure, not only
  landmarks; it replaced the old "inhabited" toggle, and a loaded legacy `protected: true` reads as *protected*); the
  *Stamping* panel lists protection, seat and zones (`StampStepsDescriber`); **Show as a ruin** runs the server's own pass with a seed and
  shows the result (nothing can be edited or saved while it shows); the palette has the **keep** brush, the **cavity**
  brush, the **sound** and **den** markers and the **settings** tool. Save writes the compact file to the user-content
  folder — the local server offers it to the next new world — and `landmark_exports/<key>/landmark.json`.
- **Into the game:** `python tools/merge_structure.py <landmark_exports/<key>>` writes `data/landmarks/<key>.json`
  (defaults stripped, `pinOnly` of an existing file kept).
- **By script:** the toolkit in `tools/landmarks/` ([README](../../tools/landmarks/README.md)) — a `Design` with
  cylinders, rings, domes, cones, arches and arcades, trusses, cables, stairs, pyramids, tunnels and caves, block
  letters, statues, mirroring, leaning and the marker helpers — writes the same compact format; one script per landmark
  (`tools/landmarks/<key>.py`), all of them and the three samples by `tools/gen_landmarks.py`. `preview.py` renders a
  template to a PNG (front, side, top, isometric, cut-away sections) without Unity. A generated landmark opens in the
  editor like any other.
- **New blocks** for the content: `mine_track`, `mine_cart`, `dripstone`, `burger_bun`, `burger_patty`,
  `burger_cheese` (#3498, save version 14). A stalagmite is a `dripstone` cone; hanging, the same cone with its up
  face turned down. The school club package adds `travertine`, `blackened_sandstone`, `verdigris_copper`,
  `burger_lettuce`, `burger_tomato`, `burger_bun_sesame` (#3483, save version 15). Every building material is made at
  the workbench (`data/recipes.json`), the #3498 ones included; `dripstone` stays a found material without a recipe
  (`MaterialEconomyTests.LandmarkBuildingMaterials_AreMadeAtTheWorkbench_DripstoneIsFoundOnly`).
- **The school club's eleven** (terrain generation 39; `SchoolClubLandmarkTests`, `SchoolClubSoaniriLandmarkTests`):

  | key | where | state |
  |---|---|---|
  | `leaning_tower`, `porta_nigra`, `portasseum`, `golden_bird_gate` | rolled, air | ruin (masonry, tuned so ~20–30 % falls: strengths 0.8 / 0.75 / 0.9 with the fallen side's zones at 1.0 and 0.95 / 0.5 with zones for the gate body (0.9) and both pavilions (1.0) — the 104-row bird would otherwise grade the low parts' decay by its own height; the bird's kept chain stands) |
  | `old_mine` | rolled, air | ruin (underground: shaft, three tunnel levels, dripstone caverns) |
  | `eifelnyde` | rolled, air | ruin, zones: pyramid masonry, tower lattice — revision 2 breaks the upper shaft (its top falls, ~25 % of the tower); kept: only the pyramid's inner stairs and the legs' stairs to the first platform |
  | `suspension_bridge` | rolled, air, `seat: span` | ruin (lattice) — the ground row lies under the trusses, so revision 2 drops a section of the main span |
  | `soundi_statue` | rolled, air | intact, protected |
  | `soaniri_castle` | fixed on `sounder_world` | ruin (zones: keep, rest); dens of Soundis |
  | `singer_statue` | fixed on `sounder_world` | intact, protected; a choir loop, radius 120 |
  | `blocks_king` | rolled, air + inhabited | intact, protected; burger keeper inside |

  Sizes stay within 256 a side; the largest overlay blob is in the tens of KB.

## 8. Key files

| Area | Files |
|---|---|
| Template, rules, settings, loot | `Shared/Definitions/StructureTemplate.cs`, `LandmarkRules.cs`, `MarkerSettings.cs`, `LootTable.cs`; `Shared/Content/TemplateBody.cs`, `StructureCatalog.cs`, `GameContent.Landmarks.cs`, `ContentLoader.cs` |
| Build and ruin | `WorldGeneration/LandmarkGenerator.cs`, `StructureRuiner*.cs`, `RuinOptions.cs`, `RuinMarkers.cs`, `VoxelGrid.cs`, `LandmarkSilhouette.cs` |
| Server | `GameServer/GameServerLandmarks.cs` (roll, pick, seat, record), `…LandmarkStamp.cs`, `…LandmarkLife.cs` (markers, chests), `…LandmarkWorld.cs` (emitters, dens, discovery, the client's list), `…LandmarkResidents.cs`, `…LandmarkStream.cs`, `…LandmarkAdmin.cs`, `…LandmarkOverlay.cs` (overlay storage), `ServerWorld.Overlays.cs`, `StructureOverlayIndex.cs`, `…LandmarkSeats.cs` (fixed landmarks, the span seat), `GameServerStructureProtection.cs` + `StructureFootprints.cs` (protection) |
| Overlay storage | `Shared/World/StructureOverlay.cs`, `StructureOverlayBuilder.cs`; `Persistence/*WorldRepository.Overlays.cs` (table `structure_overlay`) |
| Wire | `Networking/Messages/LandmarkMessages.cs` (tag 335) |
| Client | `Client.Core/Audio/SoundEmitterMix.cs`, `Client.Core/LandmarkSilhouetteMesh.cs`; `SoundEmitterView.cs`, `LandmarkSilhouetteView.cs`, `Shaders/FarTerrain.shader`, `WorldMap.cs`, `WikiUI.cs` |
| Editor | `StructureEditor.Landmark.cs`, `StructureEditor.Zones.cs`, `EditorMarkerPanel.cs`; `Client.Core/StampStepsDescriber.cs`, `StructureStampRules.cs` |
| Data and tools | `data/landmarks/*.json`, `data/loot_tables.json`, `tools/gen_landmarks.py`, `tools/landmarks/*.py`, `tools/merge_structure.py` |
| Tests | `LandmarkTemplateTests`, `StructureRuinerTests` (+ `…GoldenTests`, `…OptionTests`, `…HullTests`), `LandmarkStampTests`, `LandmarkWorldTests`, `LandmarkClientTests`, `LandmarkOverlayWorldTests`, `StructureOverlayTests`, `StructureRuinerZoneTests`, `LandmarkSchoolClubTests` (span, fixed), `StructureProtectionTests` |
