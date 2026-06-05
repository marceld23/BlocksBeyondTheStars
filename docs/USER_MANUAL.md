# SpaceCraft — User Manual

The central reference for **how to play**: controls, game mechanics, the in-game editors, and the
chat/admin commands. This is a living document.

> **Maintainers:** keep this file current. Whenever a control changes, a feature is added, or a command
> is introduced, update the relevant section here in the same change. This manual is the single source of
> truth for player-facing operation. (Written in English per project doc policy; in-game text itself is
> bilingual DE/EN.)

Last updated: 2026-06-04.

---

## 1. Starting the game

- Launch the client (`Spacecraft.exe`). From the main menu: **Singleplayer** → pick an existing save or
  start a **New world** (name + seed), or join a server.
- The **Editors** submenu (main menu) holds the creation tools — see §6.

---

## 2. On-foot controls

| Key / input | Action |
|---|---|
| **W / A / S / D** | Move |
| **Mouse** | Look |
| **Space** | Jump — **hold in the air to fire the jetpack** (if equipped), thrusting upward until suit energy runs out |
| **Left-click** | Mine the targeted block (or **scan** it when a scanner is selected) |
| **Right-click** | Place the selected hotbar block |
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

### Mining & tools
- Tools have a **kind** (drill/scanner/…) and **tier** (1–5). A block has a **hardness** and may require a
  minimum tool tier; mining accumulates the tool's power until it exceeds the hardness, then the block
  breaks and yields its **drops**. Powerful drills can clear a small radius.
- Ship hull, station, settlement and other players' protected landing zones cannot be mined.

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

### Space flight & combat
- Fly within local space instances; asteroids + NPC drones can damage hull/shield. No permanent ship
  loss in the current PvE slice.

### Stations: boarding & docking
- **Space stations**: approach in space and press **E** to board. A station is its **own place in orbit** —
  you arrive inside it, floating in space (black sky, no planet/weather, life support), and can walk the
  interior (vendors, mission board, heal tank, quarters) and talk to its crew NPCs. Press **U** to leave and
  travel back down to your ship on the planet.
- **Player docking**: press **K** near another player to request docking; **U** to undock. Docking is
  modal and gated by server rules + a `docking_module`.

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
  used to unlock blueprints; the readout shows subject/info/threat/knowledge.

### Hyperspace travel
- Open Tab → Map and pick a destination. Travel within the same system is free; jumping to **another star
  system** requires a fitted **`jump_generator`** module. On arrival the world rebuilds at the destination.

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
| `/ai <prompt>` | Generate an AI mission (content tool, not a cheat) |
| `/help` | List the admin commands in chat |

The client parses these slash-commands and sends an `AdminCommandIntent`; `/bump` stays a normal chat
message the server intercepts. Non-admins typing a command just get a rejection toast.

---

## 8. For maintainers
When you change a keybind, add a feature, or add a command/cheat, update the matching section above in the
same commit. Keep the tables accurate against the code (controls live in `PlayerController.cs`,
`PlayerInteractions.cs`, `SpaceView.cs`, `GameMenu.cs`, `WorldMap.cs`, `ChatUi.cs`; commands in
`GameServer.HandleChat` / `HandleAdminCommand`).
