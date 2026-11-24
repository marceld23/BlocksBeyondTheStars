# Lively places

How settlements, villages, towns and space stations became more varied and alive: what is generated differently, what
the people do all day, what a place sounds and looks like, and what the client is told about it. The world-generation
side is terrain generation 46 (see [WORLD_GENERATION.md](WORLD_GENERATION.md) §61); everything the people DO works in
every world, old or new.

Related: [VILLAGE_LIFE.md](VILLAGE_LIFE.md) (chatter, festival, wishes), [NPC_ROUTINES.md](NPC_ROUTINES.md) (the daily
routine and pathfinding), [STRUCTURE_PIPELINE.md](STRUCTURE_PIPELINE.md) (kits, modules, the planner and the baker),
[STATION_SETTLEMENT_EDITOR.md](STATION_SETTLEMENT_EDITOR.md) (the kit editor).

## 1. Two kinds of change, two gates

| Kind | Gate | Examples |
|---|---|---|
| What a place is built from | `WorldDescription.LivelyPlacesGeneration` = 46 (`CurrentTerrainGeneration`) | new modules, mirrored lots, the place's colour, street furniture, landmarks, children's beds, a station's purpose and colour |
| What happens at a place | none — every world | errand rounds, visible work, the longer evening, night watch, market day, the calendar gathering, sounds, the place banner, far silhouettes, household pets |

A world never changes under a player: the plan of a settlement is pinned text (`SettlementPlan.Serialize`) with a
**revision**, and the baker follows the revision, not the world's generation. `SettlementPlan.ClassicRevision` (1) bakes
what it always baked; `LivelyRevision` (2) is the lively place. `SettlementPlan.RevisionFor(generation)` picks the revision
of a new plan. A station's purpose and colour are pinned in its `StationKitRecord`.

## 2. A settlement of generation 46

### 2.1 Kits and modules

- New planned kits `<tier>_grown_2` and `<tier>_grid_2` (`minTerrainGeneration` 46). The older settlement kits carry
  `maxTerrainGeneration` 45 (`StructureKit.DrawnAt`), so a new world's **regular** draw is the planned ones.
  `StructureCatalog.KitsFor` with `AnyGeneration` (the four-argument overload the editors and tests use) ignores the bounds.
- **The kits from before stay in use** (`GameServerKitsFromBefore.cs`, `KitsFromBeforeTests`): a kit past its upper bound
  is one the world has *outgrown* (`StructureKit.OutgrownAt`, `StructureCatalog.OutgrownKitsFor`), not a dead one. About
  one settlement pick in twenty of a lively world (`KitsFromBeforeShare` 0.05, one draw on the pick's own lane) goes to
  the outgrown kits of the tier — the two plot-grid kits and the two first planned ones — drawn among themselves by
  their own weights. Such a settlement has its old houses; a planned one is planned and baked like every settlement of
  the world (lanterns, benches, stalls, the place's colour), a plot-grid one is built by the plot grid as ever (no
  street furniture, and — having no marked bed — no children). A share, not a weight: a kit somebody adds keeps its
  weight against the current kits. A world before generation 46 has outgrown nothing and draws not one number more.
- New kit fields: `Dressing` (1 none … 4 rich, default 3), `NoMirror`, `Palette` (`none`, a hex colour, or empty = by
  seed), `NoLandmark`, `Purposes` (stations, §8).
- New modules `lively_<village|town>_<function>_<n>[_alien]`, `lively_city_tall_house_1[_alien]` and the landmarks
  `lively_<style>_landmark_<kind>[_alien]` (mill, bell, water tower, light, hall, obelisk, clock, dome), written by
  `tools/gen_lively_settlement_modules.py`. They are stored **compact** (`palette` + `rows` of runs; only markers stay as
  cells) and expanded at load by `TemplateBody.Expand` (`ContentLoader`), which keeps `data/settlement_templates.json`
  small.
- A whole hand-made settlement template of a village or a town is no longer the settlement: it becomes the **centre** of a
  planned one (`SettlementPlanRequest.Centre`, `PlaceCentre`). A hamlet's stays a single house.

### 2.2 The plan

- `PlanLot.Mirrored` (bit 0 of the lot's ninth field): about half the lots are mirrored along X
  (`SettlementPlanBuilder.MirrorsLot`, `TemplateTransform.MirrorX` / `CanMirror` / `MirrorShape`). A module that cannot be
  mirrored (quarter cubes, design bits, a kit with `NoMirror`) is not.
- The plan's options part (`|c=<hex>;d=<n>`): the place's **colour** (`SettlementPalette`, ten colours, by seed or the
  kit's `Palette`) and its **dressing** level.
- One **landmark** per place at most (`OneLandmark`, 75 %; none with a centre template or `NoLandmark`).

### 2.3 The bake

`SettlementPlanBaker.Lively.cs` dresses a revision-2 plan (never a ruin): lanterns on posts that cast light, market stalls
and benches on the plaza, a lantern in every plaza corner the stalls left free, a hamlet's fire (a real `campfire`: a
visitor can cook at it, as at a wilderness hut's — kept that way on the maintainer's decision, 2026-11-20) and a village's well
(on a green village **a real pond** of three by five blocks where the green is level, with a **plank jetty** from the
east bank to the middle and a lantern on a post at its end — the jetty is where somebody draws water, #3872; the plan's
ninth landmark kind, placed on the water a plan owns — a river lies beyond the box, where a jetty could end on dry
land), crates,
barrels and flower pots at the facades, an awning in the place's colour over the door of every building that offers
something. Roof and accent tokens are dyed with the colour (`ModuleMaterials.ForLivelySettlement`); a village's wall
material follows the seed (surface block, planks or stone). Nothing stands in a door lane or on a customer's spot.

Every piece leaves a **marker** the server and the client read:

| Marker | Meaning |
|---|---|
| `lamp`, `bench`, `well`, `stall`, `fire` | street furniture: an anchor for a villager, an effect point for the client |
| `sign` (`fn=<function>;side=<0..3>`) | the door of a building that offers something: the client hangs a sign there |
| `chimney` | the client lets smoke rise |
| `child` | beside a bed: its resident is a child (§6) |

Names: `GameServerSettlementNames.cs` coins a lively place's name from forty roots and an ending that follows its
landmark, its form or its tier — only from generation 46; ruins and older worlds keep the classic table.

## 3. What the client is told

- **`PlaceList`** (message 358, `PlaceMessages.cs`): every inhabited settlement of the active world — or, aboard a
  station, the station — as a `NetPlace`: key, kind, name, tier, the footprint, the ground row, the colour, the middle of
  the meeting place, what it offers (`Trades`), the door signs, the effect points, the state (`PlaceStates`: market,
  festival) and a station's purpose. Sent with the landmark list on world entry (an empty list clears the last world's)
  and again whenever a place's state changes (`SetSettlementEvent` → `BroadcastPlaces`).
- **`NpcEmote`** (359): an NPC's gesture without a line (two villagers nodding, a dancer, a child's wave), to players on
  foot within 24 blocks. `NpcGestures` 5–9 are point, head shake, dance, clap, bow.
- **`NetSoundEmitter.When`** (`SoundEmitterTimes`): the hours an emitter keeps. Additive: an older client plays it round
  the clock.
- The landmark list's tail carries a coarse **silhouette** per settlement (kind `settlement`), §7.

On the client: `PlaceTracker` (Client.Core) decides which place the player stands in (the footprint grown by three blocks,
leaving needs ten; the short way round both seams) and when to greet — once on walking in, again after ten minutes away
or when a market opens around the player. `HudUi.Places.cs` shows the place banner in the celebration slot (last in the
queue, a short hold, a soft chime); `PlaceSignView` hangs the signs; `PlaceAmbienceView` plays the place's sound bed.

## 4. Life by day (every world)

- **Anchors** (`GameServerPlaceAnchors.cs`): per settlement the spots where somebody can go and do something — the plaza,
  the vendor's stall, the notice board, the workshop, the greenhouse, the edges of fields and pens, where a road leaves, and
  in a lively place its benches, well, stalls, fire and lanterns. Read from the markers, cached for two minutes, every
  spot one a person can stand on right now.
- **Errand rounds** (`GameServerNpcErrands.cs`): a villager without a post goes round three to five anchors near its
  home, stays 20–55 s at each with an activity on its nameplate (and a basket at the stall or the field), goes home for a
  while and starts again. Around noon and on a market day the meeting places draw more of them. Arriving beside a
  neighbour, both nod. In rain, snow or a storm everybody goes home. After a morning round every other villager sweeps
  at home (`npc_broom`).
- **Visible work** (`GameServerSettlementWork.cs`): the village gardener walks greenhouse → field edge → stall with the
  harvest basket; gardener and craftsman hold their tool; the innkeeper serves through the evening.
- **The animal keeper's round** (`GameServerAnimalKeeper.cs`): the tamer of a place with a pen within walking reach of
  its post walks to the fence, stands there 40–80 s (`npc.activity.watching_animals`; its own animal follows it anyway)
  and walks back for one to two minutes at the stall. It does not set out in bad weather or while a player stands
  within six blocks of it — it is a trader, and its goods are with it wherever it stands. No pen in reach: the post is
  the whole job, as before. Nothing is fed and nothing is laid.
- **Cheap by construction.** Called from the routine pass, so only for people in a player's area of interest. A new walk
  starts only while at most two route searches wait (one runs per tick) and fewer than a third of the place is on the way.
  Nothing changes hands: no inventory, no crop, no stock.

## 5. Evening, night and events (every world)

- **Hours** (`RoutineHoursOf`): a villager's and a kit station's crew member's evening begins between 0.62 and 0.66 of
  the local day, the night between 0.80 and 0.86, the morning between 0.20 and 0.24 — each person its own stable draw.
  Base residents keep the common hours.
- **Evenings on the plaza** (`EveningSeatFor`, `FreePlazaBench`): once the tavern's chairs are taken, the next residents
  without a post get a bench of the plaza as their evening seat — the bench block beside a `bench` marker, never one
  somebody else has; only then the chair at home. A bench sitter (`ServerNpc.BenchSeat`) sits there in dry weather only:
  the evening is decided again whenever the weather at the bench changes (`BenchWeatherTurned`), so rain sends it home
  and it comes back when the rain stops. Settlements without bench markers seat people exactly as before.
- **Night watch**: a town has one, a city two (`AssignNightWatch`, job `watch`, given after all the spawn draws so no
  name moves). On its round with a lantern (`npc_lantern`, the client lights it) through evening and night, asleep
  until noon. Passing a player on its round it says a line of its own (`npc.hello.watch.1`–`3`, no name in them — a
  stranger hears them too).
- **Market day** (`GameServerPlaceEvents.cs`): every third in-game day per settlement with a vendor (not a hamlet), one
  to three travelling merchants walk in from a road exit and stand at the stalls from morning to 0.60; the place's state
  is `market`. Nothing runs out: the next one is three days away.
- **Calendar gathering**: when a calendar festival is on for a nearby player, the village gathers in a ring in the early
  evening for two minutes, dances and claps — once per day and place, no present, no condition.
- **Trader pilot** (`GameServerTraderPilot.cs`): the pilot of a landed trader walks to a stall or the market within 56
  blocks once per stay and back before take-off. **With no market near** it steps about six blocks off, stands there
  for a look at its ship (`npc.activity.checking_ship`), walks back and **sits down on a crate** with its back to the
  ship — pose 1 with the prop `npc_crate_seat`, which the client draws under the seated figure (`NpcView.SetCrateSeat`;
  no block is placed, nothing can be mined or taken). A customer within twelve blocks of the ship calls it back from
  its look and keeps it on its feet; a customer within five blocks, or the last 45 s before lift-off, gets it up from
  the crate. Its goods are with it all the while.
- **Platform**: a train halting at a place's stop rings a bell and a few people wave.

## 6. Children and pets

`GameServerChildren.cs`. A bed with a `child` marker beside it houses a child: two per hamlet or village, three per town
or city, **in addition** to the residents (`TakeChildBeds` takes those beds out before the grown-ups are housed).
Children are spawned last, from a generator of their own, so no grown-up's name, face or job moves.

- **Plays.** Short runs with short stops: to another child (and cheers there — tag), to a grown-up of the place, to a spot
  on the plaza. Home in the rain, home earlier in the evening, in bed earlier, up a little later. Waves at a player who
  walks by.
- **Not a counterpart.** Role `child`, no person key: no wish, no trade, no mission, no present, no standing, no rail
  trip, no visit to a base, no authored story face, no night watch, no startle, never the festival's speaker — and never
  an AI line. Speaking to one gets one of ten written answers (`npc.child.talk.<n>`), in turn.
- **Talks with children only.** A chatter topic with `"who": "children"` (`data/village_life.json`) is for two children;
  every other topic is for two grown-ups; a child and a grown-up share none.
- **On the client** the look `child` draws its own proportions (`ChildFigure`, `PlayerAvatar.Child.cs`): the head grows
  in place, never a beard, never bald, and the nameplate sits over its own head.

`GameServerHouseholdPets.cs`: up to two households per settlement keep a small peaceful animal of the world — the animal
tamer's pet for people without that profession (owner `npc:<id>`, so it follows through the same code and the town's
creature rule spares it). Who keeps one comes from the people's own keys; the animal exists only while a player on foot
is within 72 blocks of its person and is gone beyond 128.

**Dogs and cats** (#3976, generation 49): on a world with air to breathe a person's pet — a household's or the tamer's — may
be a dog or a cat although the world's roster does not hold them (`PersonPetSpecies` → `KeptAnimals.PoolFor`): every pet on
Earth, half of them elsewhere (a stable hash of the person's key), all of them on a world without a peaceful animal of its
own. Such a pet comes at its own size and is known to the world's species table, so the client draws it with its coats and
calls; it never roams the wild. The pens likewise may keep cows, sheep and chickens that lay milk, wool and eggs.
[WORLD_GENERATION.md](WORLD_GENERATION.md) §64.

**Small for real** (#3879): a person's pet — a household's or the tamer's — is never a giant, and its drawn body is never
wider than `PetMaxRadius` (0.8 blocks of `CreatureFootprint.Radius`), so it fits the rooms and doorways it follows its person
through. `PetScaleFor` keeps the usual size factor (× 0.6 household, × 0.8 tamer) for a species that fits and scales a big
one down — it comes as a young one. Before, "small" was only relative: a 3.35-block arachnid walked into the shop behind
its person 4.4 blocks long and stood half through the walls (the server tracks a creature as one point). A pet from before
shrinks on the next pass, a giant one is replaced. Tests: `PersonPetSizeTests`.

## 7. Sound and sight

- **Emitters with hours.** A sound marker may carry `when=day|evening|night|waking`; `SoundEmitterMix.TimeGate` eases an
  emitter in and out at the edges of its hours. Four voices play at once (was three), never two with the same sound.
- **Derived emitters** (`GameServerPlaceSounds.cs`): from the markers every settlement has — the tavern murmurs in the
  evening, the workshop rings by day, the well drips round the clock, a lively place's market stalls clink by day
  (`amb_place_stall`, #3868); at most three per kind and place. Aboard a station
  (`GameServerStationLife.cs`): the hangar, the canteen while the crew is awake, the rooms with equipment.
- **The place's bed** (`PlaceAmbience`, `PlaceAmbienceView`): a quiet layer under the weather for the place the player
  stands in — a village's or a town's day, busier on a market day, a deck's hum aboard a station; softer under a roof,
  silent at night and in a vacuum; a hamlet has none.
- **A station's hum is computed** (`PlaceAmbience.StationHumLevel`, `PitchFor`): one loop (`amb_station_deck`), played
  louder the bigger the station (small 0.6 … colossal 1.0 of the bed's share) and at a pitch of its own — deeper the
  bigger the station (1.06 … 0.94), deeper again on a mining station (×0.94), a little brighter on a research station
  (×1.05); always within 0.85–1.15 of the recording. A settlement's bed is never bent.
- **Stations are audible.** The emitters used to be muted aboard a station; they play wherever there is air.
- **Far silhouettes** (`GameServerPlaceSilhouettes.cs`): a settlement's baked layout sampled down (only what stands above
  each column's floor), sent like a landmark's and drawn by the same client view.
- **Small things**: sparks off the craftsman's hammer, the stroke of a broom, the night watch's lantern as a light.
- **Dust in the lamplight** (`AmbientParticles`): the slow warm motes of a home drift under every roof of an inhabited
  village or town too (near the light fixtures, off with Reduced effects) — not aboard a station.
- **Where the small creatures are** (`PlaceGathering`, `MicroFaunaView`): half of the micro-fauna that appears while the
  player stands in a place appears at one of its gardens by day and at one of its street lanterns by night
  (`NetPlace.Points`, between 8 and 28 blocks from the player) instead of anywhere in the ring. The population is the
  world's own — only its places change.

**Not built: lanterns on a daylight sensor.** The plaza lanterns were wired to the Crystal Net in a first version and
taken out again on the maintainer's decision: every village's net was no longer empty, net cells keep chunks loaded,
VEGA's ruin hint fires near any world circuit, and a line of conduit ran through every plaza. The lanterns of a place
are plain lanterns and burn round the clock. Do not wire place features into the Crystal Net without asking.

## 8. Space stations

`GameServerStationPurpose.cs`, `StationPurposes` (WorldGeneration).

- **Purpose**: trade, mining or research. A kit station composed for the first time in a world of generation 46 gets
  one (`NewStationPurpose`): the start system's station trades, a system with two or more asteroid fields leans to
  mining, elsewhere one of the three by weight from the station's seed. Pinned (`StationKitRecord.Purpose`); every
  station from before has none.
- **Colour**: one of eight, pinned with the purpose. `StationKitComposer.PaintBand` dyes the foot and the head row of
  every module's walls and the edge of its floor — only plain `iron_wall` / `steel_floor` cells that carry no dye. The
  wall is one block thick, so the band shows on the hull too. A replay paints it again from the pinned composition.
- **What the purpose changes**: the kit (`StructureKit.Purposes`: a kit made for a purpose is drawn for that purpose
  only; a station whose purpose has no kit of its own takes one made for none), the first vendor's goods and the crew's
  clothes (`StationPurposes.ThemeOf`), on a trade station the order of the further vendors' trades (all four in turn),
  and the head line of the place banner (`ui.place.station.<purpose>`).
- **Rooms and kits made for a purpose** (`tools/gen_station_purpose_modules.py`, `StationPurposeKitTests`): one kit
  per tier and purpose (`station_<tier>_<purpose>_1`, fifteen) — the classic kit of the tier plus the purpose's own
  rooms: a trade station's **bazaar** (three stalls) and **customs** office, a mining station's **ore bay**, **smelter**
  and **assay** office, a research station's **laboratory**, **observatory** and **archive**. The first room of each is
  on every tier, the second from medium, the third from large; below that they are optional. Forty rooms, written in
  the compact body (115 kB in all), each a passage (two door ports or more — a required dead end takes a port and gives
  none back) with the cross of walkways behind its doors free. **One room works, the others are decoration**: the
  smelter's two furnaces are real, lit `forge` blocks — a mining station's service, a refinery for whoever stands
  beside them (`GameServer.StationAvailable`; the maintainer's decision, 2026-11-20). No other room holds a block that
  works where it stands — the laboratory's tanks are the medbay's ice tanks — and nothing anywhere is a Crystal Net
  device or conduit, so the furnaces are on no circuit (`OnlyTheSmelter_HoldsABlockThatWorksWhereItStands`). A trade
  kit has the cabins
  for its four stalls, and a station with a purpose staffs its stalls and its board before every other post, so all
  four trades have a trader. The laboratory and the observatory carry a **sound marker** (`amb_station_lab`): the kit
  composer now carries a module's sound marker with its settings into the station (`StationMarker.Data`) — every other
  marker of a kit module stays without settings. The hull shows the purpose too: more antennas on a trade station,
  more solar wings on a mining station, more domes on a research station (the kit's exterior counts).
- **Three rules only a station with a purpose has** (so every station composed before stays block for block what it
  is): no module is placed in front of a hangar's mouth (`BlocksAHangarMouth` — the kits are bigger, and a walled-in
  mouth leaves a ship to dock at a wall), no exterior piece lies beside a window of any module (`clearOfWindows`), and
  a purpose's own kit that does not fit from a station's seed falls back to the tier's kit without a purpose while the
  station keeps its purpose (`ComposeWithoutPurposeRooms`).
- **The station's own exchange** (`StationPurposes.ServiceThemeOf`, `data/recipes.json`): the first trader of a mining
  station deals in the market theme `ore_exchange`, the first of a research station in `data_exchange` — the miners' /
  researchers' goods at the station's rates (silicate → 8 iron instead of 6, → 5 lead instead of 4, 6 carbon → 1 crystal
  instead of 8, 5 silicate → 2 titanium; 2 titanium + 1 crystal → 1 data fragment instead of 3 + 1, 1 fragment → 3
  circuit boards instead of 2, 3 rune stones → 2 fragments instead of 2 → 1). The trader dresses as its trade
  (`LookOf`). The rows are `marketExclusive`: the travelling merchant's mixed stock never carries them
  (`MarketStock.MixedOffers`). **A rate is better only where nothing trades the goods back** — silicate → copper stays
  4 → 3 and iron → titanium is not offered, because with the trader discount either would mint ore; the loop guard
  (`GearSetsTests.NoTwoMarketTrades_FormAProfitableLoop…`) checks every pair.
- **The station archive takes samples** (#3870, `GameServerStationArchive.cs`, every research station): a sample
  carries its species in its key and lies in the sample case, which no market row reads, so it has a mechanic of its
  own. Aboard a research station the card of a sample (Tab → Inventory → Samples) offers **Give one to the station
  archive** (`GiveSampleToStationIntent`, message 360, served while a singleplayer menu holds the world). The archive
  pays `1 + rarity` data fragments (1 common … 5 legendary; a deposit by its rarity points) **once per species and
  player** — samples are collected endlessly, so a second sample of a species is declined and nothing is farmed. One
  sample leaves the case; the research book keeps what was learned; the given species are kept in the player's research
  record (`BioResearch.GivenToStations`, additive). Anywhere else the server answers `srv.station.archive_not_here`.
- **Hull parts and floor plans by purpose** (#3871): only on a station composed with a purpose, after every other
  exterior piece (`StationKitPurposeParts`, one part per five modules, one to four): a trade station gets **cargo
  racks** (two rows of freight crates on a free side wall under a beacon light), a mining station **ore hoppers** (a
  funnel of hull plates filled with ore on a roof), a research station **radio dishes** (a bowl on a post on a roof, a
  light at its feed) — same rules as every exterior piece (outside the modules, off the windows, out of the hangar
  approach). The floor plans live in the kit generator (`LAYOUT`): a trade station is a **concourse** (two corridors and
  a crossroads junction), a mining station a **spine** (a long line of corridors, no junction), a research station has
  **decks** (two ladder junctions or more from large on; while it is composed its ladder junctions take an upward port
  first and its own rooms the upper deck first — `StationPurposes.Climbs`). Classic stations compose and bake exactly
  as before (the golden station pins hold).
- **Where the purpose is read**: on the boarding banner (`NetPlace.Purpose`), in the dock prompt ("Meridian · Trade
  station" — `SpaceShipDesign.Purpose`, sent with the hull's design) and on the star map and the travel list
  (`NetBody.Purpose`). Both wire fields are additive and ride on rarely sent messages; a station that was never composed
  (nobody has seen it yet) has no purpose and reads as a plain station.
- **The station as a place**: aboard, the `PlaceList` holds the station (kind `station`, its purpose, its colour).
- **Announcements** (`AnnounceAboardStation`): a gong and a short status line to everybody aboard — a trader has docked
  or casts off, the night rest begins, the day shift begins; at most one a minute per station.
- **A merchant's ship stays** (`GameServerDockedTraders.cs`, every world): an NPC trader that docks no longer vanishes
  in a warp flash. It lies moored beside the hangar's mouth (`NpcTraderPhase.Docked`, sent like a pilot's parked ship)
  while its pilot trades aboard, then casts off and flies out. The station's `VisitingTrader` record knows the ship, so a
  flight scene that finds none for it makes one (`EnsureMooredTraders`) — a pilot who enters space later sees it too. A
  station with a visitor is no destination for the next trader. Aboard, `SyncVisitingPilot` stands the merchant at the
  trade post when a ship docked while somebody was already there and takes it away when the stay is over (both were
  missing before).
- **On the hull** (client, `SpaceView.StationLife.cs`, `StationLifePlan`): position lights blink — red to port, green to
  starboard, white fore and aft — and a small dish turns over the middle. Not on a derelict, not on a player's own
  station.
- **The screens are on** (client, `StationScreenView`): aboard a station every open side face of a console screen
  (the `data_cache` block the rooms use as a display) within twelve blocks of the player carries a thin lit panel that
  breathes and now and then blips brighter — the curve of the ship terminals (`StationDecorView`), in four colours and
  rhythms so a wall of screens is not one pulse. Render-only: unlit boxes on four shared materials, no light, no
  particle, no shader change, nothing on the wire; the nearest 16 (10 and 6 on the lower presets), looked up once a
  second, none with Reduced effects.
- **The dock hand**: the crew member at the hangar post of a kit station holds a hammer and works where one can see it.
- **A round through the station** (`GameServerStationRounds.cs`, every world): one of a kit station's crew members
  without a post — the first android among them, else the first of them — walks from post to post by day (a free spot
  beside each post marker, chained nearest-first from the hall), stops ten to twenty seconds at each and nods to whoever
  stands there; its nameplate reads `npc.activity.rounds`. Nobody is added for it, and the evening and the night stay
  the routine's. A station whose every cabin has a post has nobody on rounds.
- **The mission board** (`StationBoardWindows`): a mining station shows five delivery jobs at once instead of three, a
  research station three survey jobs instead of one. The jobs come from the same id streams as ever — the window is
  wider, no job's id or roll moves.

## 9. Rules that bind every part

- **Kid-friendly**: nobody comes to harm, nobody is frightened (a child is never startled), nothing runs out and is
  missed.
- **The server decides, the client shows**: the place list, the emitters, the signs and the silhouettes are render-only;
  nothing the client does with them is sent back.
- **No jumble of voices**: a place's murmur is a bed, not speakers; gestures without a line carry no voice.
- **Small hardware**: path searches are budgeted, pets exist only near a player, silhouettes are capped
  (`PlaceSilhouetteCells`), signs hang only within 64 blocks.

## 10. Tests

`LivelyPlacesFoundationTests`, `LivelySettlementBakeTests`, `LivelyModuleContentTests`, `NpcErrandTests`,
`PlaceEventTests`, `PlaceSoundAndSightTests`, `ChildVillagerTests`, `PlazaBenchSeatTests`, `StationPurposeTests`,
`StationPurposeKitTests`, `StationPurposeShapeTests`, `StationArchiveTests`, `StationRoundsTests`, `KitsFromBeforeTests`,
`DockedTraderTests`, the
pilot's walk in `LandedTraderStayTests` (server); `PlaceTrackerTests`, `StationLifePlanTests` and the emitter and
place-list cases in `LandmarkClientTests` (client core).

## 11. Not built yet

- **Dropped: window glow at night.** The maintainer decided against it (2026-11-20); a lively place's light at night is
  its lanterns.
- The gesture head shake is defined and never sent (the plan names no use for it).
- Not measured: the low quality preset and the browser build (only the tests and the Windows build ran).
