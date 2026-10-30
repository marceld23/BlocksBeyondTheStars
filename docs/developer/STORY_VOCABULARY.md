# Story pack vocabulary (generated)

<!-- GENERATED from the code constants and their doc comments by StoryVocabularyDocTests (#2483) — do not edit by
     hand. To change a meaning, edit the <summary> of the constant in src/BlocksBeyondTheStars.Shared/Story/ (or
     Missions/Mission.cs for the Crystal Net events) and regenerate:
     BBTS_UPDATE_STORY_VOCABULARY=1 dotnet test tests/BlocksBeyondTheStars.Tests -c Release --filter FullyQualifiedName~StoryVocabularyDocTests -->

Every word a story pack may name in its data, with what it means — the reference tables of
[STORY_PACK_AUTHORING.md](STORY_PACK_AUTHORING.md). The lists come straight from the code, so this page is never
behind: a test (`StoryVocabularyDocTests`) fails when a constant is added, renamed or redescribed without
regenerating it. Where each event is raised (file and method) is in
[STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md#game-events-story-conditions-and-the-world-story-guard-2465).

## Packs

### Kinds (`kind`)

| Kind | Meaning |
|---|---|
| `story` | A narrative arc (the default `kind`) — VEGA's story is one; a world-scoped one with beats can be a save's story. |
| `tutorial` | A pack that teaches the game (`kind`); always player-scoped, it follows the world rule `TutorialPolicy`. |
| `side` | A small optional storyline (`kind`), player-scoped — it runs next to the tutorial and the world story. |

### Scopes (`scope`)

| Scope | Meaning |
|---|---|
| `world` | Progress shared by everyone in the save (`scope`; the default for a pack with beats). |
| `player` | Progress per player (`scope`; the default for a pack without beats). |

### Reserved pack ids

No pack may use these ids (`StoryRegistry.ReservedPackIds`): `none`, `mem`, `insight`, `obj`, `vega`, `track`, `cut`.

### Defaults

| Field | Built-in value |
|---|---|
| `narrator.id` | `vega` |
| `escalate.hintAfter` | 180 s of play without progress |
| `escalate.markerAfter` | 360 s |
| `escalate.offerSkipAfter` | 600 s |
| `graduation.suggest` | at most 3 chapters |

## Steps

### Step kinds (`kind`)

| Kind | Meaning |
|---|---|
| `do` | A verified action. |
| `read` | Read and continue — never blocks. |
| `react` | Armed by a situation — never blocks, stays armed until it fires once. |

### Step tiers (`tier`)

| Tier | Meaning |
|---|---|
| `must` | The chapter waits for it. |
| `core` | Blocks until its hint time passes, then counts silently as an open extra. |
| `opt` | Never blocks a non-optional chapter. |

### Objective types (`objective.type`)

Without a `type` the objective is a game event (`objective.event`, below).

| Type | Meaning |
|---|---|
| `Mission` | A mission bridge. |
| `Lead` | A lead bridge (#2476, a world story's missions only): the step is the chapter-lead word in `target` — completed by the same actions and evidence that follow that lead, shown with the lead's own place-aware text (and marker) wherever it can be followed from where the player is. |

### Marker providers (`onStart.reveal`)

| Provider | Where the help marker points |
|---|---|
| `nearest_food_plant` | `nearest_food_plant` — the nearest plant whose harvest is food (berries …) around the player (#2469). |
| `nearest_ore` | `nearest_ore:<block key>` — the nearest block of that kind around the player (a bounded probe). |
| `nearest_settlement` | `nearest_settlement` — the nearest settlement on the player's body. |
| `nearest_station` | `nearest_station` — the nearest space station of the player's system (#2471, the dock lesson): in flight the station of the player's orbit nearest to their ship, landed the first station of the system. Not a ping on the ground but the client's space waypoint (`ShipAiLine.SpaceWaypointId`), so the radar and the edge arrow lead to it. |
| `unique_site` | `unique_site:<observatory\|shrine\|derelict>` — the spot of that one-of-a-kind site while the player is on its world (#2476: "read the observatory's last log" once they landed there). Nothing elsewhere. |

### Gates (`gates`, tutorial packs only)

| Gate | What it opens |
|---|---|
| `opportunityTips` | The opportunity/progression context tips (today: eat + mine + craft done). |
| `banter` | VEGA's LLM banter (today: the whole onboarding done). |
| `stationNear` | The `station_near` tip (today: while the dock lesson is open). |

## Game events (`objective.event`)

107 event keys (`GameEventKeys.All`). The key names the verb, the argument the thing: a step
names a key (or a `prefix*` pattern) and, optionally, the arguments it accepts (`"arg": "a|b"`); the amount
adds up towards `required`. Nothing is raised for an observer, before the join is complete, or for a non-positive
amount.

| Event | Raised when (argument, amount) |
|---|---|
| `ally:joined` | The player and another became allies or crewmates (raised for both who are online). Arg: `alliance` or `crew`. |
| `asteroid:destroyed` | The player's ship weapon broke an asteroid (a big one splits into smaller ones — that counts too). |
| `base:built` | A block was placed inside a base zone the player owns or shares with an ally (the core itself not). Arg: the block key. |
| `base:founded` | A base core founded a base. Arg: the body id. |
| `base:sealed` | A base the player owns or shares with an ally holds air in a sealed room on the player's world (the 1 Hz check, only while a step listens). Arg: the body id. |
| `base:walled` | The player stands inside a closed wall ring of a base they own or share with an ally (the 1 Hz check, only while a step listens). Arg: the body id. |
| `biolab:used` | A bio-lab action worked. Arg: `analyse`, `mix`, `change`, `wash` or `seedling`. |
| `build:any` | A block was placed. Arg: the block key (a blueprint paste: `blueprint_paste`, amount = blocks). |
| `build:rotated` | A form or prop block was placed with an explicit turn of the rotate key. Arg: the block key. |
| `cargo:stowed` | The player stowed items into the ship's cargo hold. Arg: the item key, or `all` for "stow all". |
| `chat:sent` | A chat line of the player's went out on the radio (not a command, not a line the filter dropped). |
| `companion:cared` | The player looked after a companion: named it (`named`) or fed it (`fed`). |
| `craft:any` | A recipe was crafted (stations, markets, the ship console). Arg: the recipe key; amount: the batch count. |
| `craft:paint` | A building material was painted with a design. Arg: the base item key. |
| `craft:shape` | A building material was re-formed (built-in or player-designed form). Arg: the base item key. |
| `craft:station` | A recipe crafted, by the station it needs (raised next to `craft:any`, also for a free Creative craft). Arg: the station — `CraftingStation`'s name in lower case (`hand`, `workshop`, `refinery`, `factory`, `campfire`, `market`, `algaetank` …); amount = the batch count. A factory terminal's craft is `factory`. |
| `craft:tint` | A building material was dyed or made to glow. Arg: the base item key. |
| `creature:fed` | The Feed action threw a piece of food to a begging animal. Arg: the food's item key. |
| `crystal:chime` | Crystal Net (raised for the circuit's owner): A chime of the player's rang. |
| `crystal:chime_by_plate` | Crystal Net (raised for the circuit's owner): Someone stepped on a step plate and a chime rang. |
| `crystal:craft` | Crystal Net (raised for the circuit's owner): A fabricator of the player's crafted something by itself ("Assembly line"). |
| `crystal:door_by_gate` | Crystal Net (raised for the circuit's owner): A logic block locked or opened a door. |
| `crystal:gate_placed` | Crystal Net (raised for the circuit's owner): A logic gate was placed on a network. |
| `crystal:lamp_by_daylight` | Crystal Net (raised for the circuit's owner): A daylight sensor switched a lamp. |
| `crystal:lamp_by_switch` | Crystal Net (raised for the circuit's owner): A switch turned a lamp on. |
| `crystal:laser_deep` | Crystal Net (raised for the circuit's owner): A drill laser of the player's reached its full depth ("Deep driller"). |
| `crystal:lift_ride` | Crystal Net (raised for the circuit's owner): The player rode a lift ("Going up"). |
| `crystal:machine_job` | Crystal Net (raised for the circuit's owner): A machine started by a signal finished a job. |
| `crystal:net_on` | Crystal Net (raised for the circuit's owner): A crystal source of the player's put power on its network for the first time ("First circuit"). |
| `crystal:phase_open` | Crystal Net (raised for the circuit's owner): A phase block opened on a signal. |
| `crystal:vault` | Crystal Net (raised for the circuit's owner): A vault's phase door opened with the player in the chamber. |
| `data:found` | Data for bigger blueprints turned up. Arg: `cache` (a data cache is in reach) or `fragment` (the player carries a data fragment). |
| `defeat:any` | A hostile was defeated by the player. Arg: `creature`, `giant`, `machine`, `bandit` (planet) or `space_machine`, `raider` (space). |
| `door:opened` | A hand-operated door (wood, hinge) was opened by the player. Arg: the door kind. |
| `eat:any` | Real food was eaten (an item with hunger value). Arg: the item key. |
| `eva:started` | A spacewalk began. Arg: `hatch` (out through the hatch of the floating ship) or `flight` (from the flight view). |
| `factory:claimed` | A spawned factory claimed with an access (SPS) code — it became the player's base. Arg: the factory's name. |
| `flight:flown` | The player's ship flew in space. Amount: whole flight units (a hyperjump or a warp never counts). |
| `gadget:used` | A gadget did its work (the terrain scanner pulsed, the medkit healed …; a refused or empty use raises nothing). Arg: the gadget's item key. |
| `gear:equipped` | The player put on a piece of gear. Arg: the base item key. |
| `harvest:any` | A plant was harvested (broken by hand: a fruit, a crop, a flower, a festival pumpkin). Arg: the plant's block key. "Bring in sixty plants" counts every harvest, whatever grows on the world. |
| `hotbar:select` | The player picked another quick-bar slot. Arg: the slot number, 1-based. |
| `hyperjump:any` | A jump to another star system. Arg: the destination system id. |
| `insight:companion_ward` | A companion made a Guardian machine stand down near the player (raised at most every `GameServer.InsightEventCooldownSeconds` while it lasts). Arg: empty. |
| `insight:shape_noncube` | The player formed a non-cube block. Arg: the base item key. |
| `interior:entered` | The pilot stepped from the helm (or in from a spacewalk) into the ship's walkable interior in space. |
| `inventory:full` | The player's backpack has no free slot left (noticed once a second, like VEGA's `invfull` hint). |
| `item:gained` | A mined or harvested block's drops reached the player. Arg: the base item key; amount: the count. |
| `item:moved` | The player moved an item between two slots of the backpack or the quick bar. |
| `lamp:toggled` | The suit lamp was switched. Arg: `on` or `off`. |
| `lane:linked` | A jump lane opened through the player's contribution. Arg: `<systemA>\|<systemB>`. |
| `loot:any` | A world container (not a player crate) was looted empty. Arg: the container kind. |
| `lore:read` | The player read a lore text new to them (a rune inscription, a wreck log, a vault plaque, the observatory's last log — whatever opened in the reader). Arg: the site kind (`monument`, `vault`, `observatory`, `derelict`, `data_terminal` …). |
| `marker:set` | A map marker was set (or moved) on a world, or a spot pinged. Arg: `marker` or `ping`. |
| `mine:any` | A block was mined by hand. Arg: the block key. |
| `mine:refused` | A block refused the swing because the tool tier is too low (and a better tool exists). Arg: the block key. |
| `minigame:played` | A finished run of a DataQube game from the shipped catalogue (every completed run, a replay too — the knowledge it pays is the handler's business). Arg: the game key. |
| `mission:accepted` | A mission was accepted (board, chain step in conversation). Arg: the mission id. |
| `mission:board_accepted` | A job the player picked was accepted: a mission board's, a mission chain's or a stand-alone board job such as the cable run (never a story character's errand, never a pack's own bridge). Arg: the mission id. |
| `mission:turned_in` | A mission was turned in. Arg: the mission id. |
| `module:built` | A ship module was built. Arg: the module key. |
| `move:airborne` | The jetpack fired, or the glider's wing opened. Arg: `jetpack` or `glider`. |
| `move:climbed` | The player climbed a wall (the climbing pose, rising). Amount: whole metres climbed — measured only while a step listens. |
| `move:jump` | The player jumped or crouched on foot (at most once a second). Arg: `jump` or `crouch`. |
| `move:walked` | The player walked on a surface. Amount: whole metres (sub-metre steps add up; jumps between reports longer than a few metres — a teleport, a respawn — never count). |
| `npc:answered` | The player picked an answer in a conversation. Arg: the NPC's role. |
| `npc:people` | The "People you know" list was opened (the client asks for it while the list is shown). |
| `npc:talked` | The player talked to someone (a greeting or a conversation — not to a sleeper). Arg: the NPC's role. |
| `paint:block` | The paint tool painted a design onto a placed block (or wiped one off). Arg: the block key. |
| `planet:scanned` | A world's overview card was read — the ship scanner held on a planet, or the Map's planet scan. Arg: the body id. |
| `plant:any` | Something was planted: a flora block (a sapling, a seed, a bred seedling) placed into the ground. Arg: the block key. |
| `player:respawned` | The player woke up again after fainting (any respawn). |
| `player:zero_g` | The player floated up above a planet's atmosphere line on foot (raised right before VEGA's `zero_g` hint). |
| `poi:reached` | The player stands near a buried vault's entrance or a crashed wreck on a world (the 1 Hz check behind VEGA's `poi` hint, raised right before it). |
| `relay:commissioned` | An SPS relay the player contributed to was completed. Arg: the station id. |
| `research:any` | A blueprint was researched. Arg: the blueprint key. |
| `salvage:found` | Salvage in space: a derelict or a debris field reached for the first time, or a salvage capsule emptied into the hold. Arg: `wreck`, `debris` or `capsule`. |
| `sample:taken` | A sample went into the player's sample case — from a harvested plant or tree, an ore deposit, an animal (the sampler, a defeat, a companion's gift). Arg: `plant`, `animal` or `mineral`. |
| `scan:any` | A first-time scan (a discovery — the achievement counter's moment). Arg: the readout kind. |
| `scan:block` | A block, plant or tree scanned with the hand scanner, once per CELL per player (a client that names the cell; an older one raises nothing here; since #3020 only for a cell within the scanner's reach that really holds the block). Arg: the block key. To count trees use `scan:tree` — a crown has many cells. |
| `scan:performed` | Any scan, first or repeat (hand scanner, ship scanner). Arg: the readout kind (`flora`, `creature`, `block`, `monument`, `asteroid`, `anomaly`, `wreck`, `wormhole` …). |
| `scan:tree` | A tree scanned with the hand scanner, once per TREE per player — the server walks from the scanned leaf or log to the trunk's foot and remembers that; a crown without a natural trunk does not count. Arg: the tree's kind by its crown — `conifer` (pine needles), `broadleaf`, `palm`, or the crown's block key. |
| `ship:commissioned` | A self-built ship was commissioned. Arg: the ship id. |
| `ship:edited` | A block was built onto (or into) the player's parked ship on foot — the ship grows. Arg: the block key. |
| `ship:keel_placed` | A ship keel was laid: the construction site of a self-built ship. Arg: the new fleet id. |
| `ship:repaired` | The player repaired their own ship (the hull points, or a hole refilled). Arg: `all` (the console's "repair everything affordable") or `cell` (one hole refilled on a walk-around or EVA). |
| `ship:returned` | The player got back to their landed ship. Arg: `menu` ("Back to my ship"), `teleport` (the suit teleporter) or `walk` (walked back aboard or close to it after having been far away). |
| `ship:switched` | The player switched their active ship in the fleet. Arg: the ship id switched to. |
| `space:enter` | The player entered space flight. Arg: `launch`, `hyperjump`, `wormhole` or `resume`. |
| `spawn:set` | A bed, a heal tank or a crew bunk became the player's spawn point. Arg: `bed`, `heal_tank` or `crew_bunk`. |
| `station:boarded` | A space station was boarded. Arg: the station id. |
| `station:commissioned` | A player station was commissioned — raised for its owner. Arg: the station id. |
| `station:core_deployed` | A station core was deployed on a spacewalk — the start of a player station. Arg: the station structure id. |
| `station:left` | The player undocked from a boarded station. Arg: the station id. |
| `station:zero_g` | Zero-g construction mode switched aboard a player station. Arg: `on` or `off`. |
| `story:fragment` | A story net fragment was handed to the player (picked up, mission thread, NPC). Arg: the fragment key. |
| `suit:auto_feed` | The suit fed a stored ration to a starving player. Arg: the item key. |
| `suit:climate` | The suit's climate control started fighting the weather. Arg: `cold` or `heat`. |
| `tame:any` | A creature was tamed. Arg: the species id (`au_sreekmakra` for the shapeshifter — also when it follows the tame of the animal it imitated, which raises its own event first). |
| `trade:barter` | A barter at a settlement or station market stall. Arg: the recipe key. |
| `trade:player` | A completed trade with another player. Arg: the other player's id. |
| `travel:landed` | The player's ship set down on a body. Arg: `flight`, `quick` or `admin`. The join placement raises nothing. |
| `vehicle:boarded` | The player boarded a vehicle. Arg: `speeder`, `boat` or `train`. |
| `visit:body` | First arrival on a body (stations included). Arg: the body id. |
| `visit:system` | First arrival in a star system (never the spawn system). Arg: the system id. |
| `visit:world` | First arrival on a planet, moon or asteroid (not a station — `visit:body` counts those too). Arg: the body id. |
| `wormhole:any` | The player flew through a wormhole. Arg: the destination system id. |

### UI teaching flags (`objective.event: "ui:<id>"`)

Reported by the client (tag 298 `UiTutorialEvent`): allowed only as a player pack's step objective — never in a
condition, never paid, never stored.

| Flag | Raised when |
|---|---|
| `ui:vega_continue` | The "continue" acknowledgement of a VEGA line — what completes a `read` step. |
| `ui:menu_open` | The Tab menu was opened. |
| `ui:codex_open` | The Codex was opened. |
| `ui:map_open` | A map was opened (the planet map tab or the flight chart). |
| `ui:waypoint_set` | A waypoint was set on the flight chart. |
| `ui:progress_open` | The Achievements tab (what you have done and what comes next) was opened. |

## Conditions

One grammar for `requires`, `feasibleIf`, `alreadyDoneIf`, `visibleIf`, `offerWhen`, `autoCompleteIf`, `start.after`,
`gates` and `variants[].when` (`StoryConditionParser`); a JSON list of conditions means all of them. Nesting is
limited to 16 levels.

```
condition := atom | all:[c, c, …] | any:[c, c, …] | not:c
```

### Condition forms

| Form | Holds when |
|---|---|
| `step:<pack>:<step>` | That step of a player pack is done or skipped. |
| `chapter:<pack>:<chapter>` | That chapter is done or skipped. |
| `pack:<pack>:<started\|done\|enabled\|optedout>` | A per-player pack state. |
| `blueprint:<key>` | The player owns that blueprint. |
| `world:<predicate>` | A live fact about the world or the player's place in it (`StoryWorldPredicates`). |
| `lore:<key>` | The player has read that lore text (a `lore_sites.json` entry, #2476). |
| `all:[a,b,…]` | Every child holds. |
| `any:[a,b,…]` | At least one child holds. |
| `not:<condition>` | The child does not hold. |

### Pack states (`pack:<pack>:<state>`)

| Form | Holds when |
|---|---|
| `pack:<pack>:started` | The pack has started for this player (`story:<pack>:started`). |
| `pack:<pack>:done` | The player finished the pack (`story:<pack>:done`). |
| `pack:<pack>:enabled` | The pack runs for this player in this world (the world story, or an enabled player pack). |
| `pack:<pack>:optedout` | The player switched the pack off (`story:<pack>:optout`). |

### World predicates (`world:<predicate>`)

86 predicates (`StoryWorldPredicates.All`, held equal to the server's registry by
`StoryConditionTests`). A predicate reads the player's own body and system, never another player's.

| Predicate | Holds when |
|---|---|
| `world:access_code` | The player carries an access (SPS) code — what claims a factory. |
| `world:airless` | The player's body has no breathable air (or is a player-built station deck). |
| `world:all_blueprints` | This world hands every blueprint out (Creative mode or the head-start option) — nothing to research. |
| `world:arcade_ready` | The player owns a DataQube game, or a data cube waits on their world. |
| `world:asteroid_mining` | Asteroids may be broken (the Asteroid destruction rule is not Off). |
| `world:bandit_hail` | A robber is holding the player up right now (a demand waits for an answer). |
| `world:bandits` | Bandits are switched on (survival only). |
| `world:base_core_ready` | The player carries a base core (backpack or hold) or has crafted one before. |
| `world:base_here` | A base the player owns — or an ally's or a crewmate's (E32) — stands on the player's body. |
| `world:base_visitors` | Visitors (settlers, scouts, robbers) come to player bases. |
| `world:climbing` | The player is climbing a wall right now (the climbing pose). |
| `world:companion_owned` | The player has a companion (a tamed animal of theirs). |
| `world:creative` | Crafting costs nothing for this player (Creative, incl. a per-player mode override). |
| `world:creative_flight` | The Creative flight rule is on (double jump flies). |
| `world:creatures` | The world has animals (the Creature abundance rule is not Off) — the animal friends course exists. |
| `world:data_cache_near` | VEGA's tip check found a data cache within reach of the player in its last pass (the moment the "Knowledge &amp; research" course is offered — read from the tips' own once-a-second sweep, no extra probe). |
| `world:debris_fields` | The world generates debris fields in space (the world option is not off). |
| `world:derelict_reachable` | The derelict The Long Quiet exists and the player can get to it — the lead's rule. |
| `world:docked` | The player is aboard a space station. |
| `world:enemies` | Planet enemies (Guardian machines) are switched on. |
| `world:factory_here` | A spawned factory stands on the player's world. |
| `world:factory_near` | VEGA's tip check found a factory's terminal close to the player in its last pass (the moment the "Factory and automation" course is offered at — the tips' own once-a-second sweep, no extra probe). |
| `world:factory_owned` | The player has claimed a factory (anywhere in the save). |
| `world:festival_any` | Some festival of the real-world calendar is on (or switched on by hand). |
| `world:festival_blossoms` | A blossom festival is on (Hanami, Easter, Earth Day). |
| `world:festival_children` | A children's day is on. |
| `world:festival_christmas` | Christmas is on for this player — the Christmas pack's own start (Christmas no longer starts the lights pack). |
| `world:festival_colours` | A festival of colours is on (Holi, Carnival, Maslenitsa). |
| `world:festival_halloween` | Halloween is on for this player — the Halloween pack's own start (Halloween no longer starts the harvest pack). |
| `world:festival_harvest` | A festival of the harvest theme is on (Halloween, Erntedank, Chuseok, Thanksgiving …). |
| `world:festival_lights` | A festival of lights is on (Diwali, St. Martin, Hanukkah, Advent, Christmas, the Lantern Festival …). |
| `world:festival_midsummer` | A midsummer festival is on (Midsummer, Ivan Kupala, the Dragon Boat Festival). |
| `world:festival_newyear` | A new-year festival is on (New Year, Lunar New Year, Seollal, Tết, Nowruz, Songkran). |
| `world:festival_stars` | A star festival is on (Tanabata, Qixi, the Perseids, World Space Week …). |
| `world:food_plants_near` | A plant whose harvest is food (berries …) grows within a short walk of the player — the berry lesson's feasibility (a bounded probe of the loaded blocks, evaluated when a step activates). |
| `world:forge_ready` | The player can refine — a forge crafted before or carried, or a refinery fitted to their ship. |
| `world:fragment_on_world` | A story net fragment lies on the player's body. |
| `world:fragments_left` | The world story still has a net fragment nobody has found (a fragment mission can be done). |
| `world:free_flight` | Free space flight is allowed (launch, dock, land). |
| `world:hazards` | Environmental hazards are on. |
| `world:herd_begging` | An animal of a begging herd came to beg the player within the last minute (the moment VEGA explains the Feed action). |
| `world:hostile_near` | Something hostile — a machine, a hostile animal, a robber — is close to the player on their world. |
| `world:hull_damaged` | The player's active ship has lost hull points (it never heals by itself). |
| `world:hunger` | Hunger drains (food matters). |
| `world:instant_travel` | The Instant Travel rule is on (the map reaches unvisited worlds). |
| `world:job_active` | The player has a job from a mission board (or a settlement's job chain) in progress. |
| `world:jump_ready` | The player's active ship has a jump generator fitted. |
| `world:keel_laid` | The player has laid a ship keel — a self-built ship exists in their fleet, finished or not. |
| `world:machine_catchup` | Running machines catch up on return (the world rule `MachineCatchUpMinutes` is not 0). |
| `world:machine_circuit` | A machine of the player's has finished a job a signal started (the Crystal workshop's fifth job, or any such circuit since) — the "machine circuit" the automation lesson builds on. |
| `world:module_affordable` | The player could build a ship module now — one their active ship lacks, its blueprint owned, its materials in the backpack or the hold (a free Creative build counts). |
| `world:monument_unread` | A rune monument stands on the player's world whose runes they have not read yet. |
| `world:multiplayer` | The world is hosted for more than one player (not a singleplayer world). |
| `world:new_world_in_system` | The player's system has a planet, moon or asteroid they have never landed on — the landing lesson. |
| `world:night_outside` | The player is out on foot on a world's surface while the local sun is down. |
| `world:observatory_reachable` | The Sealed Observatory exists in this save (a world stamped before the unique sites skipped it) and the player can get there (same system, a jump generator, a landing there before, instant travel) — the lead's rule. |
| `world:oxygen` | Oxygen drains (air matters). |
| `world:planet_wrecks` | The world generates crashed wrecks on planets (the world option is not off). |
| `world:player_near` | Another (visible) player is within 32 blocks on the same body. |
| `world:radio_worn` | The player wears a radio (chat needs one). |
| `world:research_ready` | The player's knowledge already covers a blueprint they could research now (prerequisites met, no data fragment needed). |
| `world:return_to_ship` | The pause menu offers "Back to my ship". |
| `world:ruin_near` | VEGA's tip check found a ruin near the player in its last pass (a ruined settlement, a vault, a rail ruin). |
| `world:scanner_unknown` | VEGA's tip check found the player digging a lot without knowing the terrain scanner in its last pass. |
| `world:seed_carried` | The player carries something to plant — a sapling or a seed (an item that places a plant). |
| `world:settlement_near` | VEGA's tip check found a settlement near the player (not yet inside it) in its last pass. |
| `world:settlement_reachable` | The player's body has a settlement. |
| `world:settlements` | The world generates settlements (the world option is not off). |
| `world:shape_block_carried` | The player carries a shaped block (stairs, slopes …) the rotate key can turn. |
| `world:ship_built` | The player has commissioned a self-built ship. |
| `world:space_enemies` | Hostile machines in space are switched on (space combat PvE with drones or UFOs). |
| `world:space_stations` | The world generates space stations. |
| `world:space_wrecks` | The world generates wrecks drifting in space (the world option is not off). |
| `world:starter_teleporter` | Players get the starter teleporter. |
| `world:station_core_ready` | The player carries a station core, or has the materials to craft one. |
| `world:station_in_system` | A boardable space station orbits in the player's system (the dock lesson's feasibility, #1924). |
| `world:station_owned` | The player owns a commissioned space station. |
| `world:story_active` | A world-shared story (VEGA) is active — false on a "none" sandbox. |
| `world:tameable_near` | VEGA's tip check found a tameable animal near the player in its last pass (the player carries the translator — the animal friends course's moment, like `data_cache_near`). |
| `world:tier_gate` | A tool-tier gate turned one of the player's swings away within the last minute (and a better tool exists) — the moment the "Workshop and titanium" course is offered at. |
| `world:train_here` | A monorail train the player may board runs on their world (a public one, their own or an ally's). |
| `world:treasure_near` | VEGA's tip check found a chest a settler revealed near the player in its last pass. |
| `world:trees_near` | A tree (a block that drops wood) stands within a short walk of the player — the campfire lesson. |
| `world:vaults` | The world generates buried vaults (the world option is not off). |
| `world:wormhole_known` | A wormhole opens in a star system the player knows. |
| `world:wreck_signal` | VEGA's tip check picked up a wreck's signal in flight in its last pass. |

## Cutscenes (`cutscenes.json`)

### Tiers (`tier`)

| Tier | Meaning |
|---|---|
| 1 — banner | T1: a banner at the top — icon, kicker, one line; 3–4 s, never blocks. |
| 2 — card | T2: a title or beat card in the frame; never takes the camera, never holds or freezes. |
| 3 — scene | T3: a staged scene on real objects; takes the camera, SP hold, controls frozen. |
| 4 — set piece | T4: a multi-part set piece (B12 reveal, finale, ending). |

### Camera types (`shots[].camera.type`)

A tier 1 or 2 scene never takes the camera: it may only use the types marked "no".

| Type | Takes the camera | Meaning |
|---|---|---|
| `player_view` | no | Letterbox and captions over the live view — the camera stays the player's. |
| `overlay` | no | On top of a running flight sequence; never moves the camera. |
| `static` | yes | A fixed shot of the anchor. |
| `orbit` | yes | Circling the anchor (the prologue's maths, radius by ship size). |
| `crane` | yes | Rising above the anchor. |
| `sky` | yes | Tilted up from the player — always clear, place-neutral. |
| `chart` | yes | The galaxy chart full screen. |
| `dolly` | yes | A straight, eased move between two points near the anchor (a slow push-in that slides a little sideways), the whole path checked against the terrain. |
| `pan` | yes | Turning in place across an arc — from just above the player's head, or from a vantage point across the anchor; only where the view stays open. |
| `flyover` | yes | A high arc over the anchor and its ground, rising in the middle, always looking at the anchor. |
| `space_vista` | yes (synthetic stage) | A synthetic space panorama (stars, nebula, sun, a planet; with the `ship` anchor the player's own ship) — the same recipe as the menu's attract scene, independent of where the player is (on the ground or in space). |
| `diorama` | yes (synthetic stage) | A small synthetic stage showing what the anchor names: `core` the sleeping Guardian core (a red glowing column in an iron shell), `star_map` VEGA's memory of the star chart (the camera drifts to the rim). |

A `diorama` shows one of these anchors: `core`, `star_map`.

### Anchors (`shots[].camera.anchor`)

| Anchor | What the camera is placed around |
|---|---|
| `player` | The player (the default). |
| `ship` | The player's ship (radius scaled by the ship's size). |
| `planet` | The body the player is on. |
| `station` | The space station the player is aboard. |
| `star_map` | The galaxy chart. |
| `heal_tank` | The heal tank of the player's ship (every starter ship has the medbay). |
| `core` | The Guardian core chamber. |
| `custom` | The coordinates carried by the `CutscenePlay` message. |
| `players` | The other players — on a `chart` their star systems are labelled with their names (VEGA's "Many minds" in multiplayer). It needs another player on the star map; without one (singleplayer) the scene plays as a card, like a heal tank that is not there. |

### Caption styles (`shots[].captions[].style`)

| Style | Text budget EN / DE (shown characters) | Meaning |
|---|---|---|
| `subtitle` | 220 / 264 per page | Lower third inside the letterbox, auto-paged at sentence ends. |
| `title_card` | 60 / 72 (title and kicker) | A big centred card: kicker + title. |
| `speaker` | 220 / 264 per page | The speaker box with avatar, name, typewriter and voice. |
| `reading_frame` | 600 / 720 | A framed reading panel over the dimmed scene (long texts: the recap, the epilogue). |
| `credits` | none | The credits roll. |

### Colour grades (`shots[].grade`)

| Grade | Meaning |
|---|---|
| `none` | The live colours. |
| `memory` | VEGA's memory (sepia, used by the memory star chart). |
| `flashback` | The flashback grade of the memory beats. |

### Argument sources (`arg`)

What fills a caption's `{0}` (and, for a chapter, `{1}`/`{2}`).

| Value | Meaning |
|---|---|
| (left out) | The bound chapter: its title key as `{0}`, its place among the pack's chapters of its kind as `{1}` and how many there are as `{2}` (the default). |
| `body` | The name of the body the player is on when the scene is played (a landing). |
| `system` | The name of the player's star system when the scene is played (a hyperjump). |

## Text budgets

Checked by both validators. A `{key:…}` token counts 3 characters, a `{0}` slot 10.

| Text | Longest EN / DE (shown characters) |
|---|---|
| `shortNameKey` | 10 / 10 |
| a basic-training line (a tutorial's non-optional chapters) | 120 / 145 |
| any other step line (start, hint, done) | 200 / 240 |
| a chip text (`chipKey`) | 45 / 53 |
| a cutscene shot | 120 s |

## World stories only

Read for a `scope: world` pack (the save's one narrative arc, today VEGA); a player pack never uses them.

### Lead types (`chapters[].leads[].type`)

| Lead | Meaning |
|---|---|
| `fragment` | An unfound net fragment on the player's current world (its signal on the map, as before). |
| `unique_site` | One of the galaxy's one-of-a-kind places (`unique_site:observatory\|shrine\|derelict`) — feasible only when the site exists in this galaxy (a world stamped before the sites were added skipped it) and the player can reach it. |
| `monument` | A rune monument on the player's current world whose runes they have not read yet. |
| `settlement_help` | A settlement on the player's current world: finish a job from a board. |
| `machines` | Drive back the Guardian machines — only on a world whose rules field any. |
| `heal_tank` | Stand at the heal tank aboard the player's own landed ship. |
| `research` | Research a blueprint (`research:<blueprint>`). |
| `module` | Have a ship module fitted (`module:<module>`). |
| `finale` | Confront the Guardian core (never muted) — or, without a jump generator, `finale_jump`. |
| `finale_jump` | "Fit a jump generator, then fly to the Core" (never muted). |
| `relay` | After the win: link the stars with relays. |
| `explore` | The fallback: explore — new systems and settlement jobs help (its text follows the world's rules). |

### Chapter events (`chapters[].fromEvent`)

| Event | Meaning |
|---|---|
| `guardian_revealed` | The finale system appeared on the star map (`GuardianSystemRevealed`). |
| `guardian_defeated` | The finale was won (`GuardianDefeated`). |

### Finale stages (`finale.cutscenes`)

| Stage | When it plays |
|---|---|
| `arrival` | The player's flight enters the Guardian system (a hyperjump in flight) — a short "now or never" card. |
| `descent` | The player lands on the core world — the way to the iron-ringed shaft (the `guardian_core` hint). |
| `breach` | The hack is complete and the duel begins — for the players on the core world. |
| `duel` | The duel begins — the banner for the players of the finale who are NOT on the core world (#2478: the duel dialogue itself shows only there). Not in the synthesis' binding list; added with the dialogue mode. |
| `standdown` | The duel is won — the machines stand down (before the ending; also owed to whoever missed the win). |
| `resolution` | The ending (card → credits → epilogue) on the director; older clients get `StoryResolved`. |

### Memory triggers (`memoryTriggers`)

| Trigger | Meaning |
|---|---|
| `lead` | Following a chapter lead. |
| `monument` | Reading a rune monument the player had not read. |
