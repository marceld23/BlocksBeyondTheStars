# Blocks Beyond the Stars — User Manual

The central reference for **how to play**: controls, game mechanics, the in-game editors, and the
chat/admin commands. This is a living document.

> **Maintainers:** keep this file current. Whenever a control changes, a feature is added, or a command
> is introduced, update the relevant section here in the same change. This manual is the single source of
> truth for player-facing operation. (Written in English per project doc policy; in-game text itself is
> bilingual DE/EN.)

Last updated: 2026-06-12.

---

## 1. Starting the game

- Launch the client (`BlocksBeyondTheStars.exe`). From the main menu: **Singleplayer** → pick an existing save or
  start a **New world** (name + seed), **Host Game** → host a world for friends, or **Join Server**.
- **Host Game (in-game multiplayer hosting):** the same world picker as singleplayer — *any* saved world
  can be hosted ("open to LAN" style) or a new one created — plus a host bar with **max players** (2–16)
  and an optional **join password**. The game starts the bundled server locally and you join immediately;
  you are the world's admin (the very first player of a fresh world is its **WorldAdmin**; the host's
  name is additionally passed as a server admin). The address friends join is announced in chat and as a
  HUD toast ("Hosting — friends can join at ip:port"). The session ends (and the world saves) when the
  host quits. Friends outside your LAN need a port forward of that UDP port.
- **Join Server:** enter your **player name**, the server address, port and (if the server has one) the
  password. **Name verification:** the first join under a name claims it for your installation — later
  joins under that name from other machines are rejected ("name belongs to another player"), and a name
  that is currently online can't join twice. So pick your name once and keep it; it also keys your
  inventory/progress on each server.
- **World options** ("Weltoptionen") at world creation: pick a preset (**Friedlich / Standard /
  Feindselig**) or tune sliders — life & threats (creatures, planet enemies, enemy ships, UFOs),
  survival (oxygen, hunger, hazards, death penalty), generated world (flora, ore, settlements,
  wrecks, vaults, stations, exotic worlds, universe size), plus an **advanced page** with a frequency
  slider per planet type. The world *owns* its rules from then on; the world admin can live-edit the
  creature/enemy activity later in-game (Settings tab → "Weltregeln").
- The **Editors** submenu (main menu) holds the creation tools — see §6.
- On a **new world**, the ship AI **VEGA** boots up and walks you through the first hour (see §5 →
  VEGA). Veteran saves get a one-line "systems online" instead.

---

## 2. On-foot controls

| Key / input | Action |
|---|---|
| **W / A / S / D** | Move |
| **Mouse** | Look |
| **Space** | Jump — **hold in the air to fire the jetpack** (if equipped); **in water: swim up / surface** |
| **Left-click** | Mine the targeted block (or **scan** it when a scanner is selected) |
| **Right-click** | Place the selected hotbar block (or **use** the selected gadget, e.g. the terrain scanner) |
| **Mouse wheel** | Cycle hotbar slot |
| **1 – 9** | Select hotbar slot |
| **F** | Attack the nearest creature / swing the held tool |
| **R** | Repair the targeted wreck breach with the selected hotbar block (see §5 → Wrecks) |
| **L** | Toggle the suit headlamp (requires a `suit_lamp`) |
| **G** | Loot the nearest container |
| **E** | Use a nearby ship/station tile (cockpit, workshop, cargo, medbay, …) or **trade with a vendor** (opens the Market) |
| **T** | Send a trade request to a nearby player |
| **K** | Send a dock request to a nearby player |
| **U** | Undock from a player / leave a boarded space station |
| **V** | Toggle first / third-person camera |
| **N** | Advance the current **VEGA** dialogue line (also fast-completes the typewriter) |
| **Tab** | Open / close the gameplay menu (Inventory, Crafting, Tech, Ship, Map, Missions, Character) |
| **M** | Toggle the world map (top-down planet view; click to set a waypoint) |
| **Enter** | Open the chat box (Esc cancels) |
| **Esc** | Pause / close the current screen |

Interaction reach is ~6 m (extended by reach equipment).

---

## 3. Space-flight controls

Enter space by launching the ship; on foot you board/leave via the cockpit. While flying:

| Key / input | Action |
|---|---|
| **W / A / S / D** | Thrust forward / strafe / back |
| **Mouse** | Yaw + pitch (turn). Sensitivity scales with the ship's **handling** stat |
| **V** | Toggle cockpit / third-person camera |
| **W/A/S/D** | Fly through the **system** — every planet/moon is out there at its real position |
| **L** | Land — on the body you've flown up to (the HUD shows "land on <name>") or, if none is near, back where you launched. Opens a confirmation (**Enter** = yes, **Esc** = no) |
| **E** | Board a nearby space station (within range; a short dock-approach plays before you board) |
| **P** | **Autopilot** (needs an `ai_core_mk2`+ module): flies to the nearest station / landable body; any manual input takes the helm back |
| **Tab → Map** | Hyperspace **jump to another system** (needs a `jump_generator` module) — flying is within one system |

Ship classes differ in **speed** and **handling** (`data/ships.json`): e.g. the scout is fast and agile,
the hauler slow and heavy. Hull + shield are shown on the HUD; shields recharge, hull does not.

---

## 4. Menus & HUD

- **Tab menu** — tabs for Inventory, Crafting, Tech (blueprints), Ship (modules/build), Map, Missions,
  Character (appearance). Crafting/Tech/Ship are **location-bound** (workshop / lab / ship console); the
  UI tells you when you must go to the right station.
- **World map (M)** — top-down view of explored terrain (fog-of-war), with player/ship/station markers and
  click-to-set waypoints.
- **HUD** — health/oxygen/hunger/energy, hotbar, location, compass, scan readout (bottom-left), and the
  wreck panel (right) when near a repairable wreck.
- **VEGA panel** — the ship AI speaks through a typewriter speech panel with a persistent **objective
  chip** (live progress, e.g. "mine 1/3") during onboarding. Advance lines with **N**. Advisor hints can
  be muted (Settings → VEGA hints); the tutorial can be skipped or **restarted** from the Settings tab.

---

## 5. Game mechanics

### Survival
- **Health** (max 100): regenerates aboard ship / in breathable air; drained by suffocation (−5/s at 0
  oxygen), lava (−15/s, reduced by armor), and starvation (−3/s at 0 hunger). At 0 → death (see below).
- **Oxygen** (max 100 + tank bonuses): refills aboard ship / on breathable worlds; drains outside on
  toxic/airless worlds. An `oxygen_extractor` cuts the drain on extractable atmospheres.
- **Hunger** (max 100): drains off-ship; below ~15 the suit auto-eats stored/loose rations.
- **Suit energy** (max 100): powers the stealth-suit cloak and the **jetpack** (hold Space in the air to
  thrust up); both stop when it hits 0. Recharges aboard the ship and refills fully at a heal-tank.
- **Armor**: each piece (chest/legs/helmet) adds resistance, summed and capped (~75%).

### Swimming & diving
- Water is not solid: you sink in with gentle buoyancy and **dive**; hold **Space** to swim up and
  surface. Water **breaks falls**. Deep, swimmable water (lakes, ponds, seas) is common on wet worlds;
  oxygen keeps draining while submerged on non-breathable worlds.

### Mining & tools
- Tools have a **kind** (drill/scanner/…) and **tier** (1–5). A block has a **hardness** and may require a
  minimum tool tier; mining accumulates the tool's power until it exceeds the hardness, then the block
  breaks and yields its **drops**. Powerful drills can clear a small radius.
- Ship hull, station, settlement and other players' protected landing zones cannot be mined.
- **Your ship is a real parked object** on its landing pad (pads are naturally flat). You can
  **furnish the interior**: place blocks in free cabin space (and mine those again) — they stay with
  the ship across launches, landings and the walk-in interior. The hull cannot be damaged and ship
  modules (medbay, cockpit, …) cannot be removed. Step or hop up through the hatch to enter.

### Crafting, blueprints, tech
- Recipes are made at **stations**: hand (free), workshop, refinery, lab, machine room, detoxifier, market
  (barter). Inputs are consumed, outputs produced (free in Creative).
- **Blueprints** gate advanced recipes — unlock them with **knowledge points** (earned by scanning) plus
  research materials; some require prerequisite blueprints.
- **Disassemble** (at a workshop): break a crafted item back into ~50 % of its recipe inputs. In the
  Inventory detail pane, select the item and press **Disassemble** (shows what it recovers).

### Ship, modules, building
- A ship is a set of fitted **modules** (cockpit, reactor, life support, workshop, medbay, cargo holds,
  lab, refinery, …). Modules enable on-board stations and cargo capacity. Build/expand from the Ship tab.

### VEGA — the ship AI
- **Onboarding (new worlds):** VEGA guides you through an 8-stage chain (mine → craft → scan → unlock a
  blueprint → launch → dock a station → trade/take a mission → land elsewhere), each stage tracked on the
  objective chip. **Skippable and restartable** from the Settings tab; veteran saves skip automatically.
- **Advisor:** one-time contextual hints (low oxygen/energy/hunger, full inventory, first nightfall,
  "ruins detected", world-type flavour). Mute via Settings → **VEGA hints**.
- **AI-core modules:** `ai_core_mk2` adds +6 terrain-scanner radius, hostile-contact callouts in space and
  the **autopilot** (press **P** in flight); `ai_core_mk3` adds a 12 % evasive-manoeuvre damage negation.
- **Memory fragments:** data terminals in wrecks and vaults drop `ai_memory_fragment`s — VEGA redeems them
  aboard (+3 knowledge each) and tells her backstory over 10 beats; the final beat teaches the Mk3 blueprint.

### Dynamic AI text (optional LLM backend)
- A server can enable an optional AI text service that makes some flavour text dynamic: **NPC greeting
  speech bubbles** (vendors and mission-board givers greet you personally, in your language, aware of
  your past visits), **mission-board flavour text** (title/description written around the fixed job),
  occasional extra **VEGA banter**, and admin-generated missions (`/ai <prompt>`, see §7).
- It is **off by default**, and the game is fully playable without it — every AI line has a localized
  scripted fallback (DE+EN), so you simply see the standard text instead. AI text is flavour only;
  it never decides gameplay (the server validates everything).
- Enabling it is a server-side setup: run the `ai-backend/` service and set `aiLevel` in the server
  config — see [SELF_HOSTING.md](SELF_HOSTING.md) §8.

### Space flight & combat
- Fly within local space instances; asteroids + NPC drones can damage hull/shield. No permanent ship
  loss in the current PvE slice.

### Stations: boarding & docking
- **Space stations**: approach in space and press **E** to board. A station is its **own place in orbit** —
  you arrive inside it, floating in space (black sky, no planet/weather, life support), and can walk the
  interior (vendors, mission board, heal tank, quarters) and talk to its crew NPCs. Press **U** to leave and
  travel back down to your ship on the planet.
- **Build your own**: deploy a **Station Core** on a spacewalk (press **B**), build a hull + an airlock door
  around it, and it commissions into a boardable station on the star map.
- **Name it**: rename a station you built from **Tab → Map** (select it → **Rename**), or by pressing **E**
  on the **station core** while standing inside your own station. Only the owner can rename it.
- **Player docking**: press **K** near another player to request docking; **U** to undock. Docking is
  modal and gated by server rules + a `docking_module`.

### Bases (Grundstein): your own home on a world
- Craft a **Base Core** at a workshop (`stone` ×6 + `iron_plate` ×2 — available from the start) and **place**
  it on a planet, moon or asteroid to **found a base**. It's the surface counterpart to a space station: the
  stone is the base's **position marker on the planet map** (key **M**), shown as a teal **⌂** with its name.
- **One base per world** per player. **Mining** the base core removes the base. Walk up and press **E** on the
  stone to **name or rename** it (only the owner can).
- On **Tab → Map**, a world where you have a base (or a station orbiting it) is **marked** and its details note
  *"You have a base/station here"*; you can also rename the base from there.

### Wrecks: repair & claim
- A crashed wreck shows a **wreck panel** (right HUD) with a hull-repair progress bar. Aim at a breach
  (missing hull cell) and press **R** while holding the **matching block** — the panel lists which blocks
  are still needed. When fully repaired, **Claim ship** adds it to your fleet.

### Trade
- **Player ↔ player:** press **T** near a player to open a modal trade. Each side stages an offer (+/−) and
  confirms; the swap executes atomically once both confirm.
- **Vendors / market:** press **E** next to a settlement or space-station **vendor** to open the **Market**
  (the gameplay menu's Crafting tab on the *Market* category). Barter recipes there trade your raw
  resources for goods. The market is also available **aboard your ship** (Tab → Crafting → Market), via the
  ship's trade console — so you can trade without a vendor too.

### Scanning & knowledge
- With a scanner selected, **left-click** a creature or block to scan it. Scans award **knowledge points**
  used to unlock blueprints; the readout shows subject/info/threat/knowledge (first-time scans highlight
  the "new discovery" bonus).
- **Terrain scanner** (`terrain_scanner`, workshop recipe + blueprint): a **right-click** gadget that
  pulses once (10 suit energy, 10 s cooldown) and reveals ores, crystal and data caches within 20 blocks
  as through-wall glow markers for 8 s, tinted by ore type. An `ai_core_mk2` extends the radius.

### Travel & the star map
- Open **Tab → Map**. The system list is grouped: **Current system** at the top (its reachable worlds, plus
  the **Launch into space / Leave space** button), then **Hyperspace** for the other systems. Selecting a
  system you've visited shows its worlds and an animated mini star map on the right.
- **Quick-travel** ("Travel" / "Hyperjump" on a world) is gated by the **Instant Travel** world option
  (Settings, world admin):
  - **Off (default):** you can only quick-travel to worlds you've already **landed on manually**. To reach a
    new world, **launch into space and fly there**, then land (pick a pad). A never-visited star system shows
    only as a single **"Hyperjump to this system"** entry — jumping there drops you into its flight space, and
    you fly to its worlds and land. Once you've been somewhere, quick-travel to it works from then on.
  - **On:** quick-travel works for any world/system immediately, visited or not.
- Jumping to **another star system** always requires a fitted **`jump_generator`** module.
- **Space stations** appear in the world list too (yours show their owner; others show *"Station of …"*).
  Selecting one offers **Board** — but only if you've **docked there at least once** before (just like landing
  gates worlds); a never-visited station shows *"visit it once to unlock"*. Boarding takes you straight inside.

### Day/night & weather
- **The world wraps east–west** — the X axis is a longitude, so walking continuously east (or west) brings
  you back to where you started, as if the planet were round. The seam is invisible (terrain, biomes, caves
  and structures line up exactly). North/south (latitude) does not wrap.
- **Day/night is by location** — because X is a longitude, a planet has a real day/night terminator: one
  player can be in daylight while another, far away, is in night, and one lap around the world is one day.
  The clock still advances.
- **Weather is per biome** — a stormy biome can rain while a neighbouring clear biome stays sunny; some
  biomes are reliably wetter than others. Weather is hidden + silent in caves/underground. Admins can
  override time/weather (see §7).
- **Multiplayer:** players can be on **different planets / star systems at once**, each with their own ship
  and start point. The star map (Tab → Map) shows where everyone is ("◈ Alice, Bob").

### Creatures
- Fauna spawn near players (habitat-gated), with temperaments; hostile creatures show visible attacks.
  Flora regrows when its host block survives.
- **Individuals of a kind vary in size.** Within any one species/type, each plant, tree or animal gets its
  own size (most near the normal size, the occasional runt or giant) — a wood is a mix of saplings and tall
  trees with varying crown widths, a herd has small and large animals. The variation is cosmetic (a creature's
  size doesn't change its health, damage or loot).

### Taming creatures (companions)
- Craft a **Creature Translator** (`creature_translator`, workshop recipe + blueprint) and some **bait**
  (`forage_bait` / `meat_bait` / `nectar_lure`, hand-crafted). Select the translator and **right-click** a
  wild creature to start a **taming ritual**.
- A HUD panel shows the creature's **mood** and what it **wants now** — offer the bait it craves, **calm** it,
  **approach** slowly, or **give it space**. Each correct response builds **trust**; reach the threshold and it
  becomes your **companion** and is named. A first tame of a species also grants **research knowledge**.
- **Harder creatures are harder to tame:** skittish animals **bolt** at the first wrong move, territorial and
  aggressive ones turn on you; placid grazers forgive mistakes. Exotic (cave/lava/flying), glowing and oversized
  creatures need more steps — and two animals of the same kind can behave differently.
- A companion **lives on the world you tamed it on**: it follows you there (friendly green-cyan tint + a floating
  name), re-appears whenever you return, and is hidden elsewhere. Manage them in the **Companions** menu tab
  (rename, release). Companions are peaceful and can't be hurt.

### Death & respawn
- At 0 health you respawn at the ship's **Medbay heal-tank** (vitals restored); a salvage capsule may drop
  at the death site to recover cargo, per the active rules.

---

## 6. Editors (main menu → Editors)

All editors are menu tools that **export a JSON bundle**; a developer folds it into the game data with the
matching Python merge tool (review the diff, translate locale placeholders, commit). Shared build-room
controls (Ship/Station/Town/Material 3D editors): **hold Right-mouse** to look, **WASD** to fly, **Q/E**
(or Space/Ctrl) up/down, **Shift** faster, **Left-click** place, **Middle-click** remove, **Esc** to exit.

| Editor | Designs | Export → merge tool |
|---|---|---|
| **Ship Editor** | Custom ship types (hull, viewports, lights, engine, hatch, station tiles) | `ship.json` + `layout.json` → `tools/merge_ship.py` |
| **Station Editor** | Space stations (hull/glass/light + hangar/vendor/mission/heal/quarters/console markers) | `structure.json` + `layout.json` → `tools/merge_structure.py` |
| **Town Editor** | Settlements/villages (walls, windows, ladders/stairs, lamps + vendor/mission/NPC markers) | `structure.json` + `layout.json` → `tools/merge_structure.py` |
| **Avatar Editor** | Player skin (per-part colours + gear preview) | `skin.json` → `tools/merge_avatar.py` (Apply also saves locally) |
| **Item & Recipe Editor** | Items + recipes + optional blueprint gating | `content.json` → `tools/merge_recipe.py` |
| **Material Editor** | Block materials: paint a 64×64 tile, set mining (hardness/tool/drops), look (gloss/metal/glow/colour), world spawn (frequency/depth/world-type) | `material.json` + `texture.bytes` → `tools/merge_material.py` |

**Material Editor painting:** Left-click paints with the selected swatch, Right-click erases to the base
colour; Fill/Flat/Clear and an RGB base-colour picker are in the side panel. "World type" targets which
planets get the ore: any / airless / with-atmosphere / single-biome / multi-biome.

---

## 7. Chat & admin / cheat commands

### Chat
- Press **Enter**, type, **Enter** to send (scrollback in the chat panel). Normal chat requires a **comm
  radio** in your inventory; messages are rate-limited and length-capped.

### `/bump` — debug snapshot (any player, no radio needed)
- **Syntax:** `/bump <description of the problem>`
- Writes a detailed JSON snapshot of your current situation (player state, environment, nearby
  blocks/creatures/players, ship status, and a 30-second history) to the world's `bumps/` folder. The
  server replies with the saved filename. Use it to capture a bug in the moment.

### Admin cheats (world admin / admin only)
Type these **in the chat box** (Enter to open). They are **server-authoritative** and gated twice: the
player must be an **admin** (`IsAdmin` — the world creator, or a name in the server's admin list) **and**
the server's `CheatsAllowed` rule must be on; otherwise the command is rejected with a message. Every use
is logged (`[CHEAT] …`). Type **`/help`** in chat to see the list in-game.

| Command | Effect |
|---|---|
| `/give <item> [count] [player]` | Give an item to yourself or a target player |
| `/tp <x> <y> <z>` | Teleport to coordinates |
| `/tpp <player>` | Teleport to a player |
| `/settime <day\|night\|…>` | Set the world time of day |
| `/setweather <clear\|storm\|…>` | Set the world weather |
| `/fly` | Toggle creative flight (no gravity) |
| `/god` | Toggle invulnerability |
| `/instant` | Toggle free/instant crafting |
| `/ai <prompt>` | Generate an AI mission (content tool, not a cheat; needs the optional AI backend — see §5 → *Dynamic AI text* and [SELF_HOSTING.md](SELF_HOSTING.md) §8) |
| `/help` | List the admin commands in chat |

The client parses these slash-commands and sends an `AdminCommandIntent`; `/bump` stays a normal chat
message the server intercepts. Non-admins typing a command just get a rejection toast.

---

## 8. For maintainers
When you change a keybind, add a feature, or add a command/cheat, update the matching section above in the
same commit. Keep the tables accurate against the code (controls live in `PlayerController.cs`,
`PlayerInteractions.cs`, `SpaceView.cs`, `GameMenu.cs`, `WorldMap.cs`, `ChatUi.cs`; commands in
`GameServer.HandleChat` / `HandleAdminCommand`).
