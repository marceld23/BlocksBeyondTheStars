# Derelict stations — broken old stations nobody lives on

Epic #2637 — phase 1 #2632 (placement, look, vacuum, nobody home) and #2634 (minable, the damage shows outside, broken
trading posts), 2026-10-15. Status: implemented (see [../../TODO.md](../../TODO.md)). Later phases build on the hooks in
§8: #2633 (crates with old supplies and random logs), #2635 (guardian spawner + robots) and #2636 (falling out is a real
spacewalk, and every derelict sets a boarder down with air — §5).

No terrain-generation bump and no save-version bump: placement is a pure pass after the galaxy generator with its own
salt (like the [debris fields](DEBRIS_FIELDS.md)), the package adds no blocks, and the persisted state reuses the
existing `space_structures` table and structure edits. (The guardian bay of #2635 is a new block and raises the save
version to **4** — see [§ Guardians](#guardians--the-guardian-bay-2635).) No protocol bump: a derelict is a `SpaceStation` body/contact
whose id carries the prefix `derelict:`; the client tells it apart by that id.

## 1. What a derelict is

Screelit (Justus), 2026-10-15: "at least once in a new world — a broken space station that was destroyed, looking like
the old (beta) stations, only broken. You can fall through holes into space and slowly suffocate. Old trader posts.
Nobody lives there. It should be minable and look more and more damaged from outside the more you mine."

Decisions (maintainer, 2026-10-15): "the old stations" = the boxy stations — the procedural iron/glass module generator
(`StationGenerator.Generate`) and the classic complete templates of `data/station_templates.json` (the non-kit ones);
one derelict guaranteed per world, otherwise a system rarely holds one to three; a world option scales that; nobody
lives there; the story's own derelict "The Long Quiet" (#1129, `derelict:longquiet`) stays its own site.

## 2. Placement — `DerelictPlacer` (WorldGeneration)

`GameServer.BuildGalaxy` runs `PlaceDerelicts()` as its LAST pass: after the wormholes, the debris fields, the names and
`EnsureStartSystemStation` — so the guaranteed start station never counts a derelict as "the system's station" and no
existing body moves. `DerelictPlacer.Place(systems, seed, frequency, fixedCount, startSystemId)`:

- Only the fixed procedural systems `sys0 … sys(fixedCount-1)` are candidates; the finale system and grown systems never.
- The start system (the system of the start body) always holds at least one — body `derelict:<sys>-0`, built with
  `proceduralOnly: true` (a medium module station with a sealed command room to arrive in).
- Any other system rolls `Frequency.Probability() × OddsScale (0.3)` — Normal ≈ one system in eight — and then holds 1
  (75 %), 2 or rarely 3 (`MaxPerSystem`).
- Each is a `CelestialKind.SpaceStation` body, id `derelict:<sys>-<n>`, a language-neutral coined name with an old
  designation ("Kiravon IV" — the localized kind "Derelict station" stands beside it in every UI), a star-map spot
  500 units beside a seeded planet/moon/asteroid clear of the other bodies, and `ParentId` = that body (the sky aboard).
- Off places none (the guaranteed one included). Idempotent; seed-stable; a save from before the feature gains its
  derelicts at its next start (`WorldDescription.Derelicts` reads the default *Normal*).

World option `WorldDescription.Derelicts` (`Frequency`, default *Normal*): CLI key `derelicts`, client
`WorldCreationOptions.Derelicts` (`ToArgs` → `--derelicts`), a slider on the structures page of the world options
right below the debris fields (`ui.worldopt.derelicts`). There is no admin/portal control (the debris fields have none
either). The persisted statuses (Visited) are applied to the new bodies in `PlaceDerelicts`.

## 3. The layout — `DerelictGenerator` (WorldGeneration)

`DerelictGenerator.Generate(seed, content, proceduralOnly)` → `DerelictLayout` (a voxel grid + spots), deterministic.
Source: 80 % the module generator (small 35 % / medium 45 % / large 20 %), 20 % a classic template (by weight). Then
the decay pass:

1. **Plants out first** (`RemoveFlora`) — the rooms are judged with the air they leave.
2. **Rooms.** The air the outside flood cannot reach, split at the module doorways (the generator's `door_slide`
   markers; their wall plane comes from the module grid). A classic template is one hall judged by roof and deck
   (`LabelInnerAir`) — those halls were never built airtight; as the arrival the hall is patched shut at the end (10).
3. **Which rooms keep their air.** The arrival room (the generator's spawn marker: a module station's command room, a
   template's hall) is never breached (#2636: a template's hall used to be open half the time — every template
   arrival was vacuum); the hangar loses its force field and stands open; every other room is breached with 60 % (at
   least one when there are two or more). A room with no outer face to tear keeps its air.
4. **Doors.** A doorway with a sealed room on either side gets an intact door: one column walled up (a station door is
   one block wide) and a door record (`DerelictDoor`, kind slide) in the other — airtight like every player-built door.
   A doorway between two breached rooms stays open.
5. **Breaches.** One rough hole (radius 1.3–2.5) per breached room in a wall, floor or roof between its air and space —
   never in a cell a sealed room relies on (`ProtectedCells`: the 6-neighbours of sealed air, the doorway frames, the
   arrival column).
6. **Strip + light.** Force fields → glass beside sealed air, else gone; data caches → warm emergency strips
   (`strip_light_warm`) in sealed rooms, dead strips (`strip_light_cyan_off`) elsewhere; heal tanks → scrap.
7. **Torn panels and scorch.** 6 % of the unprotected outward cells go missing; plating is scorched to carbon (airtight,
   like the iron it replaces) — 55 % within four blocks of a breach, 7 % on the outer hull, 3 % inside.
8. **Broken trading posts.** Up to three vendor stalls (the generator's / template's `vendor` markers) become a plain
   `station_vendor` block with scorched deck and a scrap pile beside it. No marker is produced.
9. **Scrap on the decks** (2–4 pieces in a breached room, 0–1 in a sealed one) and the arrival column cleared.
10. **Spots** for the later phases (§8), the **sealed arrival** and the verdict. #2636: when the arrival's pocket
    still leaks by the server's own air rule — the fill passes everything but an airtight full cube or a door
    (`Ids.Airtight` mirrors `IsStationAirtightCell`) — `SealArrivalPocket` patches it shut: the pocket grows from the
    arrival through the hall (cells with a block above and below, inside the box, plus the arrival column), and every
    leaky cell it would step into beyond that (a wall gap, an open dock mouth, a shaped roof cell) becomes a hull cube.
    Sealed by construction, after every block the decay writes. A module station's command room never needs it (400
    sampled seeds); a classic template's hall always does. Then the verdict by the same rule (`ArrivalSealed` — always
    true now, `SealedRoomCount`, `BreachedRoomCount`).

## 4. On the server — `GameServerDerelicts.cs`

A derelict rides the **player-station machinery**, not the NPC-station one:

- `RegisterDerelicts()` (registries boot stage, right after `LoadPlayerStations` and The Long Quiet) registers the
  sites only; `EnsureDerelict(id)` builds one on first need — called from `GetOrCreateStation`, so the flight contacts,
  a menu travel and a rejoin all build it on the way (a server that never flies there never pays for it). It generates
  the layout and the `SpaceStructure` (kind `station`, owner empty, `Boardable`) — from its persisted row when it has one
  (block ids from the row, dye/shape back from the layout where the block is unchanged), else fresh from the layout —
  puts it into `_playerStationCells` and registers a `BoardableStation` (stamp origin `(8,64,8)`, the persisted stamp
  anchor). The door records come from the save once anything was stored, else from the layout.
- **In flight** it has no host body, so it is a contact in every orbit instance of its system (`AddStationContacts`),
  its hull added by `AddDerelictHulls`, at a fixed spot behind the launch point above the flight plane
  (`DerelictSpacePosition`: lanes x = −95/0/95, z ≤ −110, y ≥ 50). Its position follows the ORIGINAL layout, so mining an
  edge never moves it.
- **Boarding** (`BoardStation` → `EnterBoardedStation`) stamps it with `StampPlayerStation` into `station:derelict:…`;
  the arrival search starts at the layout's arrival (`DerelictArrivalHint`) and takes a sealed pocket first. No crew,
  no visiting trader (`SpawnStationNpcs` is skipped for every `derelict:` id), no placeables (`RegisterPlayerStationPlaceables`
  is skipped — so a `station_vendor` block there never becomes a trade marker). `OnDerelictBoarded` sends VEGA's
  `vega.derelict.boarded` once per player and derelict (kind 2, never muted). From a spacewalk, E boards through the
  nearer of a door (#2607) and an opening the suit floats at (#2636, §5).
- **Nobody's station:** the trading rules skip every `derelict:` id — the start station guarantee (placement order),
  the system fallback station (`StationContactsForCurrentSystem` still adds it when the system holds only derelicts),
  trader traffic (`AmbientTrafficFor`, the docking pick), the dock lesson (`NearestStationFor`, `StationInSystem` →
  `TradingStationsForCurrentSystem`), VEGA's `station_near` tip, and the home spawn (`HandleSetSpawnPoint` refuses with
  `srv.derelict.no_home`, `TryRespawnAtHomeStation` falls back to the ship).
- **Scanner:** subject `derelict_station`, info `ui.scan.derelict`, trait `ui.scan.trait.derelict_breached` + the size,
  ledger `space:derelict`. **VEGA in flight:** `CheckDerelictApproach` (with the wreck approach on every ship pose) says
  `vega.derelict.approach` once per player and derelict within 110 units.

## 5. Air, gravity, falling out

- `UsesSealedPocketAir(stationId)` = player station or generated derelict: `StationLifeSupport` gives air only inside a
  sealed pocket, `IsSealedAirStationWorld` turns the world-level "breathable" flag off (`InitWeather`, `BodyBreathable`),
  `BreathableAirAt` (flames) follows. A breached room drains the suit tank (`@station_air_lost` warns once), a sealed
  room refills it — and a room the player closes with blocks (or a door) breathes again. The Long Quiet keeps its
  blanket air.
- Gravity: `TryGetBoardedPlayerStation` now covers every `derelict:` id — The Long Quiet included (walking off its open
  corner used to fall for ever; probed and fixed). Inside the box + 8 the boarder walks, outside the suit floats.
- **Leaving the hull is a real spacewalk (#2636, `GameServerDerelictSpacewalk.cs`).** Screelit: "You can fall through
  holes into space and then slowly suffocate." Caught in ONE place, `RescueDriftingBoarder` (the 1 Hz void check): a
  generated derelict's boarder `DerelictSpacewalkMargin` (2) blocks clear of the hull's own cell box — outside every
  cell of the hull by construction, so a deck, the roof or a jump on it never triggers it — is queued and stepped out
  after the world loop (`TickDerelictSpacewalks`, a world change never runs inside another world's tick):
  - `LeaveStation(playerId, spacewalkFrom)` — the undock path — with the ship's float spot in `_dockedFromEva`: the
    spot remembered at a spacewalk boarding when it lies 12+ blocks clear of the hull (`BoardStation` remembers the
    SUIT's spot — at a door or inside a breach a ship would stick in the hull), else `DerelictShipWaitPoint` (20 blocks
    off the hull box's face toward the launch point). The flight opens resumed (no take-off) on an EVA: `EnterSpace(…, evaStart)` seeds the
    suit's pose, `SpaceState.HasEvaStart/EvaStartX/Y/Z/Yaw` (additive) tells the flight view, which starts the suit
    there (`GameBootstrap.TakeSpaceEvaStart` in `SpaceView.BeginEvaMode`) instead of behind the ship. No undock line,
    no `StationLeft` event; VEGA says `vega.derelict.spacewalk` instead (kind 3, never muted). The suit tank drains as
    on every EVA (`InEva` overrides every life support) — no instant death.
  - **The mapping** inverts the door rule (`StationDoorNearSuit`): interior position → build cell
    (`− Origin + StampMin`, X on the lap nearest the origin) → flight (`structure position + cell − cell-box centre`;
    a derelict is never rotated), held within `DerelictSpacewalkStartReach` (6) of the box — a long fall the 1 Hz check
    caught late still starts beside the hull — and raised to the suit's eye (+1.5, the EVA camera).
  - No flight to open (free flight off, a ship that can't fly — `SpaceLaunchProblem`): the old pull-back
    (`ReturnDriftingDerelictBoarder`, `srv.derelict.drifted_back`).
- **Coming back.** E on the spacewalk boards as at every station; for a generated derelict `BoardStation` also asks
  `DerelictBreachNearSuit`: from the suit's body cell through open build cells (air, no doorway) for at most 6 steps,
  the suit within 3 blocks of the box, the nearest deck (headroom, a block underfoot, a roof somewhere above, inside
  the box — never the hull's top) — a breach, the open hangar, a torn panel. The nearer of that deck and a door in
  reach wins; `EnterBoardedStation(…, throughBreach)` sets the boarder on it (`DerelictBreachArrival`: the standable
  cell nearest to it, never in a doorway), in the vacuum of that room. Neither in reach: the arrival in the sealed
  command room. Or fly back to the ship.
- **The Long Quiet** keeps the pull-back (`DerelictDriftRescueDistance`, 24 beyond the gravity volume): a tiny story
  site with blanket air, never a place to fall out of — and its flight frame (one body's orbit, its own placement) is
  not the generated derelicts'.
- **Every arrival holds air.** Docking sets a boarder down in a sealed pocket first (`TryFindStandableInStation`), and
  every layout has one at its arrival by the server's own rule (§3 step 10) — the classic templates included, which
  before #2636 always set the boarder down in vacuum.

## 6. Mining and the hull seen from space (#2634)

- Inside: no protection (`IsStationBlock` only covers stamped NPC stations). A mined/placed block is written back into
  the build (`WriteBackStationCell` → `MirrorStationCell`, now also for `station:derelict:` worlds) and the design is
  re-sent to the pilots of EVERY orbit instance holding it (`PublishStationCells`). Door changes inside follow
  (`SyncStationDoorRecordsFromWorld`, `RefreshStationBoundsAfterDoorChange`).
- Outside (spacewalk, `HandleStructureEdit`): a generated derelict is nobody's — anyone mines it like salvage (tool
  tier + hardness, like the same block aboard) and patches it (the station place rules); the interior gets the change at
  once (`MirrorStationCellOutside`), other orbits get the block change (`PublishDerelictHullToOtherOrbits`).
- A broken trading post is a plain `station_vendor` block: mining it drops a working `station_vendor` item.

## 7. Persistence — seed + deltas

An untouched derelict stores nothing. The first boarding (the stamp anchor) or the first edit writes
`PersistDerelict(s)`: a `StoredSpaceStructure` row (id `derelict:…`, location = its system id, the cells, `Stamped` +
`StampMin`) plus its door records as structure edits (`PersistStationDoors`). `PersistIfDerelict` is the one switch every
player-station persistence site calls first (first stamp, absorb, publish, EVA mine/place, EVA doors, inside doors).
`LoadPlayerStations` routes `derelict:` rows into `_storedDerelictRows` (never a player station; `/builds` skips them).
The interior world keeps its own block edits like any world. Restart: the row's cells are the hull; `Materialised` keeps
the anchor, so the next boarding tops up instead of re-stamping.

## 8. Hooks for the later phases

- **#2633 crates + logs:** `DerelictLayout.CrateSpots` (3–6 empty deck cells against a wall in walkable rooms, sealed
  rooms first) → interior world cells via `GameServer.DerelictCrateSpotsWorld(stationId)` once stamped. Implemented —
  see [§ Old supply crates and logs](#old-supply-crates-and-logs-2633).
- **#2635 guardian spawner:** `DerelictLayout.SpawnerSpot` (the middle of the largest walkable room but the arrival's,
  on its floor) → `GameServer.DerelictSpawnerSpotWorld(stationId)`. Implemented — the `guardian_spawner` block stands
  there; see [§ Guardians](#guardians--the-guardian-bay-2635).
- **#2636 real spacewalk:** implemented — `RescueDriftingBoarder` stays the one caller and queues the spacewalk;
  re-boarding through a breach has its own entry beside the door's. See §5.

## 9. Client

`Client.Core/SpaceTargeting.IsDerelictId` (prefix `derelict:`, pinned to `DerelictPlacer.IdPrefix` by a test). The
travel screen labels the body `ui.map.kind_derelict` with `ui.map.derelict_detail`; the flight chart draws it once as a
named amber marker (`name · Derelict station`, a waypoint target) instead of a live station blip; the radar treats it
like the wreck. The hull renders through the existing `station` design path.

## 10. Tests

`DerelictStationTests` (placement guarantee/rarity/Off/idempotence, seed stability + fixed prefix only, an old save
gains them without moving a body or the start station, layout determinism + rooms/doors/posts/spots/no leftovers,
boarding: nobody/no trade/sealed arrival/hooks/no home spawn, the dock lesson never targets one, air in sealed vs
breached rooms + falling out is a spacewalk, mining persists across a restart + hull follows + the post drops a vendor,
EVA mining reaches the interior, The Long Quiet catches a fall, the CLI option),
`DerelictSpacewalkTests` (#2636: falling out opens an EVA at the matching flight spot and the tank drains; a long fall
starts just under the hull; nothing aboard — decks, roof, a jump, the box edge — steps out; E inside a breached room
boards onto that deck, E away from any opening arrives in the sealed room; 400 seeds of both sources have a sealed,
standable arrival by an independent copy of the server's air rule; boarding real template derelicts (a pinned world
seed) refills the tank) and `Client.Tests/SpaceTargetingTests.DerelictIds_UseTheServersPrefix`.

## Old supply crates and logs (#2633)

Screelit (Justus), 2026-10-15: "old crates with notes from people and records of disasters that are always random! The
crates can also contain rotten food and medkits." No block, no save-version and no protocol bump: the crate is the
existing `crate` block, the loot is an ordinary structure-loot container, the log is an item key with a seed payload.

**The crates** (`GameServerDerelictCrates.cs`). `OnDerelictBoarded` calls `SpawnDerelictCrates` after the stamp (every
boarding; it does work only once per crate). Each crate spot (`DerelictCrateSpotsWorld`) is marked in
`WorldMetadata.GeneratedLoot` as `derelict-crate:<station>:<n>` (so it fills once ever, also across restarts and after it
was looted away), gets a `crate` block — set in the interior world AND written into the build through
`MirrorStationCell` + `PublishStationCells`, so the row and the hull keep it like a player edit — and a `salvage`
container `loot_oldcrate_<station>_<n>` in that cell (deliberately not `loot_derelict_…`, which `LoreSiteOfContainer`
would read as the story's wreck-manifest lore site). A spot someone built into first stays empty. Contents
(`BuildDerelictCrateLoot`, seed-pure from world seed + station + crate): a medpack (always in crate 0, else 70 %,
sometimes two), a field medkit (10 %), emergency rations (50 %), **spoiled food** (45 %, 1–3), 1–2 picks of scrap metal /
cable / iron plate, and one log (two at 35 %). G takes everything (`LootContainer`); the empty container goes away, the
crate block stays (mining it drops a crate item, like any crate block).

**Spoiled food** (`spoiled_food`, `data/items.json`): a consumable like a mild `toxic_meat` — consumeHealth −6,
consumeHunger 6. Food is data-driven everywhere (`ConsumeHunger > 0`), so it behaves like other food; the herd does not
take it (`HerdRules.IsFoodForCreatures` wants `ConsumeHealth >= 0`), and since #2633 the suit's ration dispenser refuses
harmful food (`LoadRation` → `srv.ration.harmful`) and never auto-feeds one it already holds (an older save's toxic meat).
There is no compost / bin in the game to extend.

**The logs** (`WorldGeneration/DerelictLogs.cs`, fragments in `data/derelict_logs.json`). A log is the item
`derelict_log#x<seed>` (`ItemKey.WithSeed`, max stack 1); its text is a pure function of the seed, written from locale
fragments by the same code on the server and every client — nothing is stored or sent but the key. Seed layout: bits
16–31 the station's story (`SeedFor(worldSeed, stationId, index)`: the incident with its telling count, the escape, the
day, the first job), bits 4–15 the writer (name via `NameGenerator.Person`, which telling, the last words), bits 0–3 the
index — crate n carries indices 2n and 2n+1 (≤ 12 < 16), and job = jobs[(first + index) % jobs] gives every writer of a
station a different job (14 jobs). So one derelict tells ONE story (same disaster, same way out, same day) from up to
twelve people. Parts: `derelict.log.header` ("Day {0} — log entry by {1}"), `derelict.log.job.<job>`,
`derelict.log.incident.<key>.<1..variants>`, `derelict.log.escape.<key>`, `derelict.log.close.<key>`. Tone per
`docs/user/PARENTS.md`: curious, a little sad, never frightening — nobody is hurt on the page and everyone always gets
out (escape pods, a rescue ship, a friendly freighter, the shuttle, the neighbour station). Adding a fragment = a JSON
entry + its en/de keys (the community languages fall back to English); `DerelictCrateTests` checks every key.

**Reading — why an item, not a lore pop-up.** The environmental lore channel (`LoreTextRevealed`, `StoryLogLore`)
carries one fixed locale key per text, belongs to the active story pack (no story = no lore) and is rebuilt from the
pack on every join — a generated text fits none of that, and a new message would need a NetCodec tag and new UI. The
item path needs neither: the inventory detail pane (`CraftingTechShipUI.AddDerelictLogText`) writes the text under the
description with a **Read** button that opens the existing story reader (`StoryReaderUi.OpenOverMenu`); the icon is one
for all logs (`IconResolver.IconKey` files the key under its base). Taking a log out of any container
(`LootContainer` → `RecordFoundDerelictLogs`) also adds `log:<seed>` to the player's Discoveries ledger (named after the
writer, sited at the derelict, toast `srv.derelict.log_found`), and the Codex's Discoveries chapter lists them as
**Station logs** with their full text (`WikiUI.AppendFoundLogs`) — readable again after the note itself was dropped,
sold or stashed. A log already in the ledger (picked up twice, a copy a friend left in a crate) only moves.

Tests: `DerelictCrateTests` (crates fill once with supplies + 1–2 distinct logs, blocks in world and build, looting
records the logs, re-boarding and a restart refill nothing and keep the rest unchanged; loot is seed-pure, spoiled food
sometimes, medpacks mostly; one story per station / a different writer and job per log / stable / rendered in en + de
without placeholders or raw keys; every fragment in en + de; spoiled food edible + harmful + refused by the dispenser).

## Guardians — the guardian bay (#2635)

Screelit (Justus), 2026-10-15: "Nobody lives there anymore, only inside there are sometimes robots or drones that are
spawned every 5 minutes by a kind of spawn block."

**The block.** `guardian_spawner` ("Guardian Bay" / "Wächter-Dock", `data/blocks.json`): a broken maintenance dock of the
Guardian machines — category `machine`, a picture front (`textures/guardian_spawner`, the red-glowing alcove) on the
shared rust casing (`face_rust_side` / `face_rust_top`), facing `toward`, emission 0.6 with a red light (radius 5).
Hardness 4, drill tier 1 (the starter drill opens it). It drops salvage — 2 circuit boards, 3 copper wire, a power cell
— and has **no item**: nobody crafts or places one, so a bay exists only where the generator put it. It is a new block,
so the block ids shifted: save version **4** (`WorldMetadata.CurrentSaveVersion`, the `SaveCompatibilityTests` row).

**Placement** (`DerelictGenerator`, step 13). Every generated derelict gets exactly one, on `DerelictLayout.SpawnerSpot`
(the largest walkable room but the arrival's, as far from the arrival as it allows), its front turned toward the arrival
(`CubeFacing.Pack(FaceToward(...))`). The spot is on the room's **floor** (`OnTheDeck`): never on top of a scrap pile or
a post (cells above `reserved` cells are dropped) and only at the room's lowest feet level — the room-centre pick weights
height, so before this a cell one up (on the scrap) sat nearer the centre and the bay stood on a pile with no floor
beside it for its guardians. The Long Quiet has no bay (it is not generated).

**The release** (`GameServerDerelictGuardians.cs`). `TickEnemies` is the one place station worlds are hostile-free; for a
generated derelict's interior world it runs `TickDerelictGuardians` instead — the deliberate, narrow exception:

- **Gate** — exactly the planet machines' `PlanetEnemiesActive` (hostile machines not Off, game mode Survival): none in
  Peaceful, Creative or with the machines off; guardians still aboard when that changes power down at once.
- **Cadence** — the bay's clock runs only while a player is aboard (the world only ticks then): the first guardian
  `GuardianFirstReleaseSeconds` (60) after someone came aboard, then one every `GuardianReleaseIntervalSeconds` (300).
  While `GuardianCapPerBay` (3) of its own guardians are alive the clock holds at zero, so a destroyed guardian is
  replaced a full interval later, never at once. If nothing beside the bay is free (walled in) it tries again 10 s later.
  The clock is runtime state (a restart starts it over); the bay itself is a block of the interior world.
- **What comes** — beside the bay (a ring of 12 cells, the start rotating per release): a walking Guardian robot, every
  other time a scan-drone (`Rules.PlanetDrones`; only drones in the Remnant era — the planet rule), the hunter variant
  when hostile machines are Frequent/Extreme. Same bodies, stats and loot as the planet machines (`NewPlanetMachine`,
  extracted from `SpawnPlanetEnemyNear`); a drone needs four free cells or a robot comes instead. Each release plays the
  `guardian_bay` cue (`SoundFx`) and a red spark `WorldFx` (`guardian_bay`) at the bay.
- **Movement and fights** — the planet machines' own pass (`RunMachinePass`, extracted from `TickEnemies`): hunting by
  sight, roaming, the damage aura, the companion ward. Inside a station a drone hovers 2 above the deck instead of 4
  (`DroneHoverHere`; a room is 4–5 high) and circles a target at 3 ± 1 instead of 7 ± 3. The real-block ground probe
  already treats a breach or an outer wall as a wall, so a robot does not walk out; any guardian that still ends up more
  than one block outside the stamped box (`BeyondStationBox`) is removed — nothing falls for ever. If a guardian kills a
  boarder, the death carries them to the ship's world and the pass stops there (the active world changed under it).
- **Killing** — the ordinary attack path (`AttackEntity` on the active world's machine list): every weapon works, the
  usual 2 carbon drop, it counts as a machine defeat (story, missions, achievements).
- **VEGA** — `vega.derelict.guardian` once per player and derelict, the first time that player is aboard while one of
  its guardians is about (milestone `derelict:guardian:<id>`, kind 2); `vega.derelict.bay_down` to everyone aboard when
  a bay that has released goes silent (mined).
- **The end of the bay** — mining it stops the releases for good: the bay is a block of the interior world, so its
  absence persists with the world's block edits and the derelict's build row (`WriteBackStationCell`), across restarts.

**Client.** Nothing gates the machines by world: `WorldEntities` draws `GameBootstrap.PlanetEnemies` wherever the server
sends them, so the robots and drones render and animate aboard a derelict like on a planet (targeting, health bars and
hit markers read the same list). New: the `guardian_bay` case in `CreatureView.PlayWorldFx`; the cue clip
`Resources/audio/guardian_bay.mp3` plays through the existing `SoundFx` path. A newly arrived boarder gets the list on the
next tick (`TickDerelictGuardians` marks it dirty when the boarder count grows).

Tests: `DerelictGuardianTests` (one bay per generated layout, on the floor, facing, with floor beside it; mined with the
starter drill, salvage drops, no item; the cadence — first at 60 s, then never sooner than 300 s apart, never more than
three, always inside the hull, robots and drones, VEGA's warning, no release at the cap; none in Peaceful or Creative;
mining stops it, also after a restart; a guardian dies to a laser pistol with its carbon and the next one waits a full
interval; a guardian outside the hull is gone) and the save-version row in `SaveCompatibilityTests`.
