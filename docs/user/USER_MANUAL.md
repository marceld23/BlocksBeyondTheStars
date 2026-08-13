# Blocks Beyond the Stars — User Manual

The central reference for **how to play**: controls, game mechanics, the in-game editors, and the
chat/admin commands. This is a living document.

> **Maintainers:** keep this file current. Whenever a control changes, a feature is added, or a command
> is introduced, update the relevant section here in the same change. This manual is the single source of
> truth for player-facing operation. (Written in English per project doc policy; in-game text itself is
> localized — English, German, French and Spanish are complete, further community translations such as
> Italian are in progress.)

Last updated: 2026-08-11.

---

## 1. Starting the game

- Launch the client. **Windows:** `BlocksBeyondTheStars.Launcher.exe` (shows a loading splash then starts the game)
  or `BlocksBeyondTheStars.exe` directly. **Linux:** `./BlocksBeyondTheStars.Launcher.Console` (prints "Loading..."
  to the terminal then starts the game) or `./BlocksBeyondTheStars.x86_64` directly.
  From the main menu: **Singleplayer** → pick an existing save or
  start a **New world** (name + seed), **Host Game** → host a world for friends, **Join Server**, or
  **Official Worlds** → online multiplayer on the official servers (see below).
- **Host Game (in-game multiplayer hosting):** the same world picker as singleplayer — *any* saved world
  can be hosted ("open to LAN" style) or a new one created — plus a host bar with **max players** (2–16)
  and an optional **join password**. The game starts the bundled server locally and you join immediately;
  you are the world's admin (the very first player of a fresh world is its **WorldAdmin**; the host's
  name is additionally passed as a server admin). The address friends join is announced in chat and as a
  HUD toast ("Hosting — friends can join at ip:port"). The session ends (and the world saves) when the
  host quits. Friends outside your LAN need a port forward of that UDP port.
- **Official Worlds (online multiplayer, beta):** the in-game portal for hosted worlds on the official
  servers — also available in the browser at [play.blocksbeyondthestars.de](https://play.blocksbeyondthestars.de).
  Create a free account (no email needed), then create your own world. There are **no open public
  servers** — every world belongs to a player. To play with friends: set a **join password**, then
  **list the world publicly** (world's **Manage** dialog → *"List publicly"*; only password-protected
  worlds can be listed). Friends find it under *Official Worlds → Public worlds* and join with your
  password. Worlds are private by default and only visible to their owner until listed.
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
- **Window mode** (Settings tab): the **"Window mode" / "Fenstermodus"** option cycles **Windowed → Borderless →
  Exclusive** and applies immediately. The default is **Windowed** (a normal movable, maximizable window);
  Borderless fills whichever monitor the window currently sits on; Exclusive is classic full-screen.
- **Language** (Settings tab): switch the whole game between **English, German, French and Spanish**.
  Further languages appear in the picker automatically once their community translation clears **45 %
  coverage** (Italian is underway — want to help? See `docs/developer/TRANSLATION_GUIDE.md` in the
  repository).
- On the **very first start** a short (~28 s) **intro cinematic** plays between the title splash and the
  menu — any key skips it, and the Credits screen's **"Watch intro"** button replays it any time.
- On a **new world**, the ship AI **VEGA** boots up and walks you through the first hour (see §5 →
  VEGA) — her opening narration is staged like a scene (letterbox, an orbit shot of your landed ship);
  **Esc** skips it. Veteran saves get a one-line "systems online" instead.

---

## 2. On-foot controls

| Key / input | Action |
|---|---|
| **W / A / S / D** | Move |
| **Mouse** | Look |
| **Space** | Jump — **hold in the air to fire the jetpack** (if equipped); **in water: swim up / surface** |
| **Space ×2** | **Creative/Sandbox worlds only:** toggle free flight — then Space rises, Ctrl/C sinks, and you keep colliding with the world (so you can still land and build). Touching down turns it off |
| **Ctrl / C** (hold) | Crouch/sneak — walk slower, stop at ledges instead of walking off (corners included); climb **down** ladders; descend in zero-g |
| **Left-click** | Mine the targeted block (or **scan** it when a scanner is selected) |
| **Right-click** | Place the selected hotbar block (or **use** the selected gadget, e.g. the terrain scanner) |
| **Mouse wheel** | Cycle hotbar slot |
| **1 – 9** | Select hotbar slot |
| **Middle mouse** | **Hotbar slot actions** on the selected slot: swap it against any backpack item, and for a building material also colour it (dye / glow / own pattern) or re-form it — see §5 → Hotbar slot actions (rebindable) |
| **F** | Attack with the held tool/weapon — hits what's **under your crosshair** (the reticle turns red over a target; with **auto-aim** on, the nearest enemy in front of you is acquired automatically) |
| **R** | Repair the targeted wreck breach with the selected hotbar block (see §5 → Wrecks); with a **shaped block, furniture, ladder or stairs** selected: rotate its placement orientation (**Shift+R** cycles backwards — see §5 → Craftable block shapes) |
| **L** | Toggle the suit headlamp (requires a `suit_lamp`) |
| **G** | Loot the nearest container |
| **E** | Use a nearby ship/station tile (cockpit, workshop, cargo, medbay, …); **trade with a vendor** (opens the Market); **board your hover speeder**; **beam** from a teleporter pad you're standing on |
| **X** | Pack up (stow) a nearby deployed hover speeder back into its item |
| **T** | Send a trade request to a nearby player |
| **K** | Send a dock request to a nearby player |
| **U** | Undock from a player / leave a boarded space station |
| **V** | Toggle first / third-person camera |
| **I** | Toggle **thermal vision** while looking through the thermal binoculars (see §5 → Binoculars) |
| **N** | Advance the current **VEGA** dialogue line (also fast-completes the typewriter) |
| **Tab** | Open / close the gameplay menu (Inventory, Crafting, Tech, Ship, Map, Missions, Character); also closes full-screen menu screens such as the Codex |
| **M** | Toggle the world map (top-down planet view; click to set a waypoint) |
| **Enter** | Open the chat box (Esc cancels) |
| **J** | Hide / show the chat scrollback for this session (rebindable; see also Settings → Comfort → Chat display) |
| **V** (hold) | Push-to-talk voice (if the server enabled voice; needs a radio; key is configurable) |
| **Esc** | Close the current screen; if no game screen is open, show the leave-game confirmation |

Interaction reach is ~6 m (extended by reach equipment).

### Gamepad / controller (experimental)

A connected controller works **alongside** keyboard + mouse — both stay live at once, so you can mix them
freely and neither locks the other out. The HUD control hint swaps to controller labels while a pad is the
device in hand. Mapping targets an **Xbox / XInput** pad on Windows (other pads may report different
buttons — retuning is tracked in issue #195):

| Control | Action |
|---|---|
| **Left stick** | Move |
| **Right stick** | Look |
| **RB** | Mine / attack (hold to keep mining) |
| **LB** | Place the selected hotbar block / use the held gadget |
| **D-pad ◄ ►** | Cycle hotbar slot |
| **(A)** | Jump (hold in air = jetpack; in water = swim up) |
| **(X)** | Use / board / interact |
| **(Y)** | Toggle first / third-person camera |
| **R3** (click the right stick) | **Hotbar slot actions** on the selected slot (see §5) — stick navigates the menu, **(A)** picks, **(B)** closes |
| **Start** | Open / close the gameplay menu |

In menus, the left stick / d-pad navigates, **(A)** confirms and **(B)** goes back. The right stick also
steers the ship in flight. Direct hotbar number-key picks remain keyboard-only.

**Rebinding:** every control row in **Settings** has two buttons — the keyboard key and the pad button.
Tap the pad button and press any controller button to rebind it (actions marked **—** have no pad button
by default but can be given one). *Reset controls* restores both keyboard and pad defaults.

### Touch controls (experimental — tablet / touch browser)

On a touch device (tablet, or a touch-capable browser) on-screen controls appear automatically. The
buttons swap with what you're doing:

| On-screen control | Action |
|---|---|
| **Left stick** (bottom-left) | Move / thrust / steer |
| **Drag** anywhere on the right | Look / steer the ship |
| **◄ ►** | Cycle hotbar slot (ship-systems bar in flight) |
| **…** (beside ►) | **Hotbar slot actions** on the selected slot (see §5); shown only when the menu can open |
| **≡** (top-right) | Open / close the gameplay menu |
| *On foot:* **JUMP · MINE (hold) · PLACE · USE · DOWN · CHAT** | Jump · mine · place · use/board · descend · open chat |
| *Flying / EVA:* **FIRE (hold) · LAND · SHIP · AUTO · VIEW · USE · UP · DOWN** | Fire · landing pads · walk the ship · autopilot · camera · dock/board · float up/down |
| *Speeder:* **BOOST (hold) · JUMP · EXIT · FUEL** | Boost · hop · dismount · refuel |

Menus are tapped directly. Text entry (your name, chat): on a native tablet the on-screen keyboard opens
by itself; in a tablet **browser** a small input prompt opens instead. On a desktop or a desktop browser
nothing changes — controls stay keyboard + mouse (or a gamepad).

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
| **P** | **Autopilot** (needs an `ai_core_mk2`+ module): flies to your nav waypoint if one is set, else the nearest station / landable body; any manual input takes the helm back |
| **M** | **System chart**: a top-down map of the current system. Click a body/station to target it or empty space for a free **nav waypoint** — it shows on the radar with a distance readout, and the autopilot flies to it. The ship holds position while the chart is open |
| **Tab → Map** | Hyperspace **jump to another system** (needs a `jump_generator` module) — flying is within one system |

Ship classes differ in **speed** and **handling** (`data/ships.json`): e.g. the scout is fast and agile,
the hauler slow and heavy. Hull + shield are shown on the HUD; shields recharge, hull does not.
Hosted/default servers allow free manual space flight so players can fly through a system without needing a
separate unlock; admins can still disable it through server world rules.

---

## 4. Menus & HUD

- **Tab menu** — tabs for Inventory, Crafting, Tech (blueprints), Ship (modules/build), Map, Missions,
  Character (appearance), plus **Story**, **Companions** (tamed creatures, see §5) and **Alliances** (see §5),
  with **Settings** pinned far right. Crafting/Tech/Ship are **location-bound** (workshop / lab / ship console); the
  UI tells you when you must go to the right station.
- **Codex and DataQubes screens** — use the top-right **Close** button, **Esc**, or **Tab** to return to play.
  **< Menu** returns from the full-screen screen to the normal Tab menu.
- **Tab availability dimming** — tabs whose context isn't met are **greyed out** (but still clickable to peek):
  **Map** needs you aboard, **Crafting** a workshop, **Tech** a lab, **Ship** the ship console. While not aboard,
  the Map's travel buttons are also disabled (the world is shown but you can't quick-travel from on foot), and
  the Inventory's **Cargo Hold** transfer controls are hidden (the hold is only reachable from aboard the ship).
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
  oxygen), lava (−15/s, reduced by armor), fire (−10/s, reduced by armor), and starvation (−3/s at 0
  hunger). At 0 → death (see below).
- **Oxygen** (max 100 + tank bonuses): refills aboard ship / on breathable worlds; drains outside on
  toxic/airless worlds. An `oxygen_extractor` cuts the drain on extractable atmospheres.
- **Hunger** (max 100): drains off-ship; below ~15 the suit auto-eats stored/loose rations.
  Food sources: hunt creatures (meat), harvest berry flora (replantable via seeds), raid a settlement's
  **greenhouse** (see below), craft emergency rations — or build an **algae tank** (workshop, no blueprint)
  at a base: standing next to it grows 2 algae rations from 1 water (melt 2 snow or 2 ice into water by
  hand if there is no lake).
- **Suit energy** (max 100): powers the stealth-suit cloak, the **jetpack** (hold Space in the air to
  thrust up) — and the suit's **climate control** (below); consumers stop when it hits 0. Recharges
  aboard the ship and refills fully at a heal-tank.
- **Temperature** (Survival only): roughly **−5…40 °C is free**. Beyond that the suit's climate control
  drains **suit energy** — the further past the band (ice-world night, lava plain at noon, vacuum on an
  EVA), the faster — and once the energy is empty you slowly take exposure damage (≤ 3/s, the HUD says
  "Freezing"/"Overheating"). The HUD temperature readout turns blue/red as a warning, and the energy bar
  glows orange while climate control is running. Defenses: **dig in** (a couple of blocks below the
  surface every world settles near a mild ground temperature — unless lava or ice is right beside you),
  **build a roof** (shelter halves the stress), wait out midday/midnight, stay near the ship, and craft
  **suit liners** at the workshop (Thermo Liner 40% → Insulation Suite 65% → Climate Rig 85%; only the
  best carried liner counts — armor pieces also carry a little insulation). In vacuum the readout shows
  the sun-side/shadow hull temperature (about +120 °C to −150 °C). The world option **Environmental
  hazards** (world creation, or live in the in-game Settings tab as world admin) scales this from Off to
  Hard; Creative/Sandbox worlds are always exempt.
- **Heal tank** (workshop, blueprint-gated): the life-support unit for your own base or station. Everyone
  within a few blocks of a placed tank is slowly healed and fed and the suit recharges — the only off-ship
  suit recharge. Press **E** on the tank to make it your **home spawn**: on death you then choose between
  waking at your ship's medbay or at that home (ship is always the fallback if the home is gone).
- **Bed** (hand-crafted from logs + plant fibre, no research needed): the low-tech forerunner of the heal
  tank. Press **E** on a placed bed to make it your **home spawn** (same death choice as the tank), and
  resting near it slowly mends your health — but a bed never feeds you and never recharges the suit;
  those stay the heal tank's job.
- **Campfire** (hand-crafted from logs + stone): a contained flame that never spreads. It lights the camp,
  counters the cold while you stand near it, and is a **cooking station** — with creature meat in your
  pockets, craft **cooked meat** at the fire (far more filling than raw, and it heals).
- **Wood box** (hand-crafted from logs): early-game storage sharing the crate's stash/loot keys, but it
  only holds a few kinds of material (8 stacks) — the workshop's iron crate stores everything.
- **Armor**: each piece (chest/legs/helmet) adds resistance, summed and capped (~75%).

### Fire
- **What burns:** plants, wood and leaves. Grass and the ground itself never catch, so a fire stays in
  the vegetation it started in — and aquatic plants (kelp, seagrass, coral, water lilies) don't burn at
  all. A burnt block leaves **ash** (collect 64 of it to compact into matter dust).
- **Starting one:** hit something flammable with a lit **torch**, shoot it with a **laser pistol** or
  **plasma blaster** (kinetic guns like the scrap or gauss pistol won't ignite anything), or let flowing
  **lava** touch it. Standing in fire hurts (−10/s), so light it and step back.
- **Putting it out:** **hit the flame** to stamp it out, or place **water** next to it. **Rain and
  storms** put out fires under open sky by themselves — but not one burning under a roof or in a cave,
  and the ash-rain of a hot lava world does nothing. While rain is falling on it, wet vegetation won't
  catch at all.
- **It won't burn down the world:** fire creeps rather than sweeping, and stops spreading a limited
  distance from where it was lit. Nothing built is at risk — ships, settlements, stations, factories and
  claimed bases never catch fire, including a village's wooden greenhouse.
- **Warmth:** an open fire is a heat source on a cold world (see the temperature rules above) — a
  deliberate campfire is a real survival tool, not just a hazard.

### Swimming & diving
- Water is not solid: you sink in with gentle buoyancy and **dive**; hold **Space** to swim up and
  surface. Water **breaks falls**. Deep, swimmable water (lakes, ponds, seas) is common on wet worlds;
  oxygen keeps draining while submerged on non-breathable worlds.

### Mining & tools
- Tools have a **kind** (drill/scanner/…) and **tier** (1–5). A block has a **hardness** and may require a
  minimum tool tier; mining accumulates the tool's power until it exceeds the hardness, then the block
  breaks and yields its **drops**. Powerful drills can clear a small radius — the sweep only takes blocks
  the drill could mine directly (same tier rules).
- Some powered drills (titanium drill, mining beam) **draw suit energy with every swing**; with an empty
  suit the swing is refused. The basic and diamond drills need no energy, so you can always keep mining.
- **A full backpack does not stop you.** When neither your inventory nor (while aboard) the cargo hold has
  room, the block still breaks and the drop lands at your feet as a small **block packet**. Packets stack —
  everything you dig nearby joins the same bundle — and once you have space again, walking near one pours it
  back into your inventory automatically. Packets stay where they are until collected, saving and reloading
  included. Out in space (EVA, ship hulls) there is no ground to drop onto, so a full pack still refuses the
  break there and the block stays where it is.
- Ship hull, station, settlement and other players' protected landing zones cannot be mined — **except
  plants**: you may always pick flora, wherever it grows (that is what makes a settlement greenhouse worth
  visiting), you just cannot take the building apart.
- **Your ship is a real parked object** on its landing pad (pads are naturally flat). You can
  **furnish the interior**: place blocks in free cabin space (and mine those again) — they stay with
  the ship across launches, landings and the walk-in interior. The hull cannot be damaged and ship
  modules (medbay, cockpit, …) cannot be removed. Step or hop up through the hatch to enter.

### Inventory & cargo hold
- Your **inventory** is your personal backpack (24 slots) — it travels with you everywhere, and its first
  nine slots are the **quick-bar** (the on-screen hotbar).
- Your ship's **cargo hold** is bulk storage that belongs to the ship (48 slots, growing with cargo-hold
  modules) and is shared by everyone aboard that ship.
- **What goes where:** mined and crafted items fill your inventory first and only spill into the cargo hold
  once it's full (and only while you're aboard). Salvage you scoop up while flying in space goes straight
  into the cargo hold. While you're aboard the ship, crafting draws from **both** at once.
- **Moving things by hand:** open the **Tab menu → Inventory**. The **Inventory** tab has a **"Stow all
  materials in cargo"** button (loose materials/components only — your tools, weapons and quick-bar items
  stay put), and selecting any item offers **"Move to cargo hold"**. The **Cargo Hold** tab shows the hold's
  **used/total** capacity, a **"Take all out"** button, and per-item **"Move to inventory"**. These only work
  while you're aboard (in flight or standing in the landed cabin); on foot the cargo tab says so.
- **Auto-stow (optional):** turn on *Settings → Comfort → "Auto-stow into cargo on boarding"* to have loose
  materials moved into the hold automatically each time you board. Off by default.
- **Throwing things away:** select an item in the **Inventory** or **Cargo Hold** tab and press **"Throw
  away"** — it asks once ("Really throw away?"), and the second click destroys *every* stack of that item.
  This cannot be undone and gives nothing back. Your starting equipment (drill, scanner, suit lamp, machete,
  sidearm) has no such button, so you can never leave yourself without a way to dig or a way to see.
- **When everything is full:** if your backpack *and* your hold are full, whatever you mine or craft next is
  simply gone — the game warns you when that happens, so throw something away or empty the hold first.

### Hotbar slot actions (swap · colour · form)
- Press **middle mouse** (rebindable: Settings → Controls → *Hotbar slot actions*; gamepad: **R3**; touch:
  the **…** button beside the hotbar arrows) while playing on foot (or
  during an EVA) to act directly on the **selected hotbar slot** — no trip through the Tab menu. A **radial
  menu** opens around the screen centre: **Swap** on top, **Colour** on the left, **Form** on the right and
  **Close** at the bottom; quarters that don't apply to the held item stay visible but dimmed:
  - **Swap** — a grid of all 24 backpack slots; pick one and it exchanges with the hotbar slot (an empty slot
    simply receives the item). *Remove from quick-bar* stows the slot into the first free backpack slot.
  - **Colour** — only for a dyeable building material: the familiar swatch palette recolours the **whole
    stack** in place. **Dye** is free; **Glow** turns the stack into coloured light sources and costs **one
    crystal per block** (the button shows the cost, and the panel refuses if you're short). Under **My
    patterns** your saved paint designs (see §5 → Paint tool) apply to the stack as an **own texture** —
    the item then shows the pattern in the hotbar, places with it, and *Remove pattern* strips it again.
  - **Form** — only for a shapeable material: the 19 built-in forms as silhouettes of the actual material,
    plus **My forms** once you carry the shaping tool. Re-forms the whole stack; *Cube* reverts it.
- Everything lands **back in the same slot** (colour/form/pattern are free 1:1 exchanges — the same actions
  the crafting menu offers, so nothing is lost if the panel is closed mid-way).
- Painted items round-trip like dyed ones now: **placing** a patterned block shows the pattern to everyone,
  **mining** it gives the patterned item back.
- **You can see that it's there**: while the selected slot holds an item, a small **key badge** (e.g. *MMB*)
  floats over that hotbar cell — bright when Colour/Form apply to the held material, dimmed when only Swap
  does — and the controls hint line at the bottom appends *MMB slot actions*. Both follow your rebinding.

### Crafting, blueprints, tech
- Recipes are made at **stations**: hand (free), workshop, refinery, detoxifier, transmuter, market
  (barter). Inputs are consumed, outputs produced (free in Creative).
- The **workshop** is your everyday bench: it smelts the common ores (iron, copper, gold, silver, …) into
  ingots and fabricates parts, tools, weapons and building blocks.
- The **refinery** (the *forge* block on a base, or the refinery ship module — both unlocked via Tech) is
  the high-heat metallurgy station. It alone smelts the **rare Tier-2 metals** (titanium, cobalt, tungsten,
  platinum, uranium, neodymium) and refines diamond, carbide and reactor fuel. It also offers **higher-yield
  smelts** of the common ores (e.g. iron and copper) than the workshop — handy for bulk metalwork. You can
  never need it before you can build it: its metals are also the ones that need a titanium-tier drill to mine.
- The **transmuter** (the *matter forge* block or ship module, unlocked via Tech) compacts spare terrain
  (sand, dirt, stone, …) into *matter dust* and synthesises it back into ore — a sink for surplus digging.
- **Blueprints** gate advanced recipes — unlock them with **knowledge points** (earned by scanning) plus
  research materials; some require prerequisite blueprints.
- **Tiered upgrades consume their predecessor:** where an item is a straight upgrade of another —
  oxygen tanks II/III, the terrain scanner, weapon upgrade chains, the AI cores — the recipe **requires
  and consumes the previous tier**, so you never end up carrying a redundant Mk1 after building the Mk2.
  Fitting the `ai_core_mk3` module **replaces** the fitted Mk2 and returns half its materials as salvage.
  (Your starter gear — basic drill, scrap pistol — is deliberately chain-free.)
- **Disassemble** (at a workshop): break a crafted item back into ~50 % of its recipe inputs. In the
  Inventory detail pane, select the item and press **Disassemble** (shows what it recovers). Raw resources
  and refinery/transmuter-synthesised ore can't be disassembled.

### Ship, modules, building
- A ship is a set of fitted **modules** (cockpit, reactor, life support, workshop, medbay, cargo holds,
  lab, refinery, …). Modules enable on-board stations and cargo capacity. Build/expand from the Ship tab.
  Each cargo-hold module adds slots to the shared **cargo hold** (see *Inventory & cargo hold* above); the
  Cargo tab shows the current used/total capacity.

### Building your own ship (keel → commissioning)
- Unlock the **Shipwright** blueprint (Tech tab), craft a **Ship Keel** at a workshop and place it on open,
  solid ground **anywhere on a planet** — no landing pad needed. That founds a construction site (one at a
  time per player).
- Build the hull straight onto the keel, block by block, up to **15×15 blocks and 15 high**. Blocks must
  attach to the build; the hull sits on the keel's ground level. Mining a construction block gives it back;
  taking the last block out cancels the build.
- A flyable ship needs: at least **20 blocks**, exactly **one Ship Helm**, at least **one Ship Engine**, a
  **door**, and an **airtight hull** — glass and doors seal fine, any open gap does not.
- Stand at the helm and press **E** ("Commission ship"). If something is missing the message tells you what;
  once it passes, the build becomes your **active ship**, parked right where you built it. Launch as usual
  via menu (Tab) → Map → *Enter space*.
- **It flies the way you built it:** hull strength grows with the hull size, speed and handling come from
  engines versus weight — more engines fly faster, a heavy brick turns slowly. You can keep editing your
  ship on foot afterwards; the launch check re-runs every start (no engine → grounded until you add one).

### Repairing your own ship
- Combat dents your ship's **hull** (it never regenerates on its own), and EVA-carved hull cells stay missing
  until you refill them. Use the **cockpit** to fix both in one action: press **E** at the cockpit and a
  **"Repair ship" / "Schiff reparieren"** panel appears on the HUD (a hull bar plus the materials still needed).
- Hull is bought with **`iron_plate`** (10 hull per plate); each missing cell costs the item that originally
  placed it (or `iron_plate` for structural blocks like lights/engine, so they never block a repair). The
  repair is **greedy/partial** — it fixes the hull first, then as many cells as your materials stretch to, and
  never hard-blocks when you're short. Only the **ship's owner** can repair it. There is no passive hull regen.
- **Lost ships (if "Keep ship on death" is off):** a destroyed ship becomes a **downed wreck on your landing
  pad**. Repair it through this same flow — restore the hull and refill every missing cell and it can fly again.

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
  scripted fallback in every supported language, so you simply see the standard text instead. AI text is flavour only;
  it never decides gameplay (the server validates everything).
- Enabling it is a server-side setup: run the `ai-backend/` service and set `aiLevel` in the server
  config — see [SELF_HOSTING.md](../developer/SELF_HOSTING.md) §8.

### Space flight & combat
- Fly within local space instances; asteroids + NPC drones can damage hull/shield. Whether you can lose your
  ship is set by the world's **"Keep ship on death"** rule (see *Repairing your own ship* above).
- **Aiming**: the ship laser acquires the best target roughly **ahead of the nose** (the centre dot lights up
  cyan on lock). Weapon **range and fire rate come from the fitted module** — bigger cannons genuinely reach
  further.

### Aiming & enemy health bars
- Damaged enemies (and the one under your crosshair) show a small **health bar** that ramps
  green → amber → red; tamed companions show a friendly cyan bar. Turn bars off under
  **Settings → Comfort → "Enemy health bars"**.
- When one of **your** shots lands, the crosshair flashes a **hit marker**.
- **Auto-aim** is a **world rule** (default **on**): weapons pick a target in a forward cone by
  themselves — kid- and gamepad-friendly. The world admin can turn it **off** in the in-game
  **world rules** panel (or create the world with `--auto-aim false`): then only what is actually
  **under the crosshair** (on foot) or **on the ship's boresight** (in space) can be hit — misses
  really miss. Shots are server-validated either way, including line-of-sight (no shooting through
  walls).

### Asteroid belts
- In worlds created with belts (the default for new worlds), a system's landable asteroids orbit
  together in **1–2 shared belts** — the flight chart (M) draws them as one translucent band with an
  "Asteroid belt" label instead of separate orbit rings.
- Belts are the place to mine with the ship laser: **every asteroid body has a cluster of mineable
  rocks floating around it**, and launching from an asteroid you landed on puts you inside a dense
  local rock field (nine rocks instead of the usual three near planets).

### Peaceful NPC trader ships
- Space and busy systems feel alive with **civilian trader traffic**: merchant ships **warp in** at the system
  edge, **cruise** to a station to **dock**, or head into the inner system and **land on a planet/moon** if a
  pad is free, then later depart. They are **peaceful scenery** — a trader can't be locked, shot or damaged.
- A trader that **docks at a station** becomes a **visiting merchant**: board that station and you'll find its
  pilot as an extra **vendor** beside the trade post — barter with them like any other vendor (press **E**).
- A trader that **lands on a world** parks its ship on a pad with the **pilot standing in front** as a merchant
  you can trade with. One landed trader per body at a time; they re-appear when you return to that world.

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
- **A base always has air**: the base core projects a **life-support field** over its whole zone (the same
  cube where the base protects your blocks — about 8 blocks in every direction from the stone). Inside it your
  **oxygen refills** even on toxic or airless worlds — the HUD marks the O2 bar with *(base life support)* and
  greets you with *"Life support: <base name>"* when you step in. **Anyone** may breathe at a base (guests
  still can't build or mine there), and the field disappears with the base if the core is mined. It even works
  under water — sink the zone and you've built a diving dome. On worlds without breathable air the zone shows
  as a soft **blue shield dome** so you can see where the air ends.
- **Sealed rooms extend the air**: rooms built at the base out of **airtight materials** — stone, metals,
  concrete, bricks, glass (natural rock counts too: dig a cave!) — get life support **beyond the zone cube**,
  as long as they are **sealed** and **connected to the base**. Loose stuff (dirt, sand, snow, plants) and
  shaped blocks (ramps, spheres) leak. Doorways need the **Energy Door** (workshop, blueprint-gated): its blue
  field is an **air curtain** — you walk right through, the air stays in. Ordinary doors (wood, hinged,
  sliding) do NOT hold air. Chain rooms door by door to grow a whole airtight outpost; if a wall is mined or
  burns away, everyone at the base gets a **"no longer airtight"** warning and the rooms fall back to suit
  oxygen until the hole is closed.
- On **Tab → Map**, a world where you have a base (or a station orbiting it) is **marked** and its details note
  *"You have a base/station here"*; you can also rename the base from there.

### Alliances (shared access + no friendly fire)
- Two players can form an **alliance** from the **Alliances** menu tab. It has three views: **Allies**
  (accept/decline incoming requests, end an alliance), **Find players** (propose an alliance to any online
  player), and **Radio / Funk** (a live chat scrollback with a transmit box).
- Allies gain **access to each other's built space stations and planet bases** (boarding and building inside the
  base's protection zone), and **cannot harm one another** even with PvP enabled. Player-built **stations are
  private** — only the owner and their allies can board them.
- The **base core / station core** itself stays **owner-only** (an ally can't dissolve your base or rename your
  station). Alliances are pairwise (no named groups or roles).
- **Family-friendly play:** this is a game for all ages, so keep radio chat and the names you give players,
  bases, stations, beacons and creatures friendly. You can **mute** any player you don't want to hear, and
  the host can turn **voice chat** off entirely. See the in-game Codex chapter **House Rules** for the full
  list.

### Beam blocks (teleporter pads)
- Craft a **beam block** (`beam_block`, blueprint-gated workshop recipe: titanium + cable + energy cell + crystal),
  **place** it, and **name** it. Step onto one of your pads and press **E** to open a **transporter panel** listing
  every beam block you can reach — your own and any **allied** player's — **on the same world**, with each pad's
  name, coordinates and distance; pick one to **beam** there.
- Beaming costs **6 suit energy** with a **6 s cooldown**. Each pad shows its name on the planet map (key **M**)
  and as a floating label in the world. Only the owner can rename a pad; mining it removes it.

### Wrecks: repair & claim
- A crashed wreck shows a **wreck panel** (right HUD) with a hull-repair progress bar. Aim at a breach
  (missing hull cell) and press **R** while holding the **matching block** — the panel lists which blocks
  are still needed. When fully repaired, **Claim ship** adds it to your fleet.

### Factories
- **Factories** are rare industrial halls found on some breathable worlds — metal walls and windows with
  one or more **machine bays** whose presses, rotors and conveyors run continuously, plus a **factory
  terminal** by the door. No two are alike (size, machine count and layout are world-specific).
- Stand near the **factory terminal** to craft from it (Tab → Crafting → **Factory** category). Factory
  recipes turn **cheaper, less-rare raw materials into the same output as the workshop, but in bulk** —
  more input per step, fewer refining stages. The catch: each factory only makes the **1–4 items on its
  own roster**, so a recipe the menu lists may say *"Use a factory terminal that makes this"* — you'll need
  a different factory. Factory crafts **cannot be disassembled** back into their inputs.
- **Operating a terminal is public** — you don't need to own a factory to craft there. But a spawned
  factory is **read-only** (you can't mine or rebuild it) until you **claim** it: stand at the terminal
  with an **SPS access code** (see below) and press **E**. Claiming spends one code, makes the factory
  **your base** (you and your **allies** can rebuild it freely), and persists across reloads. One code
  claims one factory.

### Greenhouses
- Every village and city keeps a **greenhouse** — a glass house full of berry bushes you can walk into and
  harvest. A **village** grows them in soil beds under a timber-and-glass gable behind a hinged door; a
  **city** runs a two-tier **hydroponics bay** with grow lights and a sliding door, which is easy to spot at
  night. Space stations of any size above the smallest carry the same bay aboard.
- The berries are a **crop**, not wild flora: they are safe to eat on every world, and each picked bush
  **grows back on its bed** after a short while — so a greenhouse is a food source you can come back to.
- Want your own? Craft **berry seeds** by hand from 3 berries and plant them on soil — or on a crafted
  **hydroponic tray** (workshop), which lets crops root with no soil at all.

### Ruins & treasure chests
- **Ruins** are the collapsed remains of fallen settlements — mostly surviving ground walls, one
  half-standing tower, and rubble overgrown by flora. Unlike bases and stations they are **not protected**:
  every block is **freely mineable**, and what you clear stays cleared. VEGA may hint at *"structural
  echoes nearby — ruins or wreckage"*; bring a scanner, there's often something worth digging out.
- **Treasure chests** are standalone lootable caches scattered away from settlements. Each is looted
  **once** and holds richer salvage than ordinary drops — and they are the main world source of a rare
  **SPS access code**.
- **SPS access codes** (`access_code`) are the rare item used to claim a factory as your base. You get them
  two ways, both uncommon: as loot from a **treasure chest**, or by **buying** one from a trader's **Market**
  (a steep barter recipe). Keep one if you find it — it's your key to turning a factory into a home.

### Monuments & runes
- Somebody was here long before you. **Monuments** are the relics they left: a half-collapsed
  **arcade** of arches, a free-standing **gate** that leads nowhere, a ring of standing stones
  (**stone circle**), a weathered **obelisk**, or a **rune altar**. Each world carries up to three,
  and never two of the same kind.
- They are the only structure that also stands on **airless moons and dead worlds** — nobody needed
  air to raise them, and nothing has weathered them since.
- Every monument is carved with **glowing runes**. **Scan one where it stands** and you read the
  inscriptions themselves: that is worth far more **knowledge** than identifying the stone, and it
  goes into your Codex under **Discoveries → Monuments**. Each kind of monument pays once **per
  planet**, so the stone circle on the next world is worth walking to as well.
- Scanning a rune block you mined and carried home only identifies the material — the writing needs
  its monument.
- Monuments are **freely mineable** like ruins (the masonry, `Ancient Brick` and `Rune Stone`, is
  yours to build with), and what you clear stays cleared. Roughly one in three hides a small
  **relic cache** nearby.

### Bandits (robbers, camps, pirate space)
- Bandits are **people**, not machines: you can tell one by the cloth mask over the nose and mouth
  and the scruffy jacket — not by glowing eyes, which belong to the Guardian robots.
- **Lone robbers** roam some survival worlds. One may walk straight up to you and demand roughly a
  third of your two biggest stacks (**never your tools**). You have ~25 seconds and a real choice:
  **[1] Hand it over** — the robber keeps its word, leaves, and won't bother you again for a long
  while — or **[2] Refuse** (attacking or just ignoring it counts as refusing) and it fights.
  Players with empty pockets are simply left alone. Kill a bandit and its loot is yours — including
  anything you paid it earlier.
- **Bandit camps** are small raider outposts (huts, palisade, campfire) whose guards attack on
  sight but never chase far from home. Their **stash** holds better loot than ruins. The camp is
  **freely mineable**; a razed camp stays razed, and once all guards are down the camp is cleared
  **forever** — no respawns.
- **Pirate space:** about a quarter of star systems have a bandit reputation — VEGA warns you on
  entry, *before* anything happens. There, a raider ship may warp in and hail you with a cargo
  demand (drawn from inventory **and** hold) while you keep flying: pay and it warps away for good,
  refuse or open fire and it fights. Raiders only appear where the rules let you shoot back.
- The **"Bandits"** world option (world options / at creation) scales all three — Off disables them
  entirely (peaceful/family presets default to Off).

### Missions & bounties
- **Mission boards** stand in settlements and aboard space stations; vendors and board givers hand out
  gather/delivery jobs. Accepted missions are tracked in the **Tab → Missions** tab; report back to the
  giver for the reward.
- **Camp bounty:** a settlement mission board on a planet with an uncleared **bandit camp** offers a
  bounty to drive the bandits out. Accepting it marks the camp on your planet map (key **M**); clear the
  camp — everyone holding the bounty gets credit, so it's co-op friendly — and report back for a reward
  that beats the usual gather jobs.
- **Raider bounty:** station mission boards in **pirate systems** put a price on the raider ship prowling
  the sector. While you hold the bounty, the raider *will* show up on your next flight — destroy it and
  report back. Bounties follow the world's **Bandits** option: no bandits, no bounty missions.

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
- **Plants and trees scan as named species.** A scanned plant (flora) or tree reads as this world's coined
  species name with an **edible/toxic** classification, not just a block. A tree's trunk and its leaves are
  the same species, so scanning either one counts as a single discovery.
- **Micro-fauna scans too.** Stand near a butterfly, firefly, wisp or any other ambient critter with the
  scanner selected and left-click (when no larger creature is in reach): the kind enters the Codex's
  **Micro-fauna** discoveries chapter and awards a little knowledge. Thermal vision (see §5 → Binoculars)
  also picks critters up as small named contacts.
- **Terrain scanner** (`terrain_scanner`, workshop recipe + blueprint): a **right-click** gadget that
  pulses once (10 suit energy, 10 s cooldown) and reveals ores, crystal and data caches within 20 blocks
  as through-wall glow markers for 8 s, tinted by ore type. An `ai_core_mk2` extends the radius.

### Binoculars & thermal vision
- Craft **Binoculars** (`binoculars`, workshop recipe + a cheap blueprint). Select them and **right-click**
  to raise the optic; each further right-click steps the magnification (**2× · 3.3× · 6×**) and one more
  lowers them again. **Left-click** also lowers them. They cost no suit energy.
- While raised, the mouse gets proportionally finer and the head-bob is damped, so a 6× view stays steady.
  The scope draws its own reticle and a magnification readout; the optic drops automatically when you open
  a menu, mount a speeder, switch hotbar slot or go third-person.
- **They do not extend the render distance** — the world only exists as far as your view-distance setting
  streams it, so magnification enlarges what is already there. Seeing *past* the haze is what thermal mode
  is for.
- **Thermal Binoculars** (`thermal_binoculars`) are the upgrade: research the blueprint (needs the
  `binoculars` blueprint first) and craft them at the workshop — the recipe **consumes a plain pair**.
  Press **I** while looking through them to switch infrared on and off.
- In thermal mode the world reads cold and every energy signature glows **through terrain and haze**:
  hostiles hot red-orange, wild animals amber (dimmer when asleep, icy while held in stasis), tamed
  companions green, villagers and traders cyan-white, other players white, lava deep orange, and
  settlements, factories, ruins, your bases and your ship as tall magenta columns. Each contact is
  labelled with its name and distance. Contacts further than 220 m are shown at that distance along their
  true bearing — the label still gives you the real range.

### Camera & photos
- Craft a **Camera** (`camera`, workshop recipe + the cheap `camera` blueprint). Select it and **right-click**
  to take a photo of exactly what you see — the **HUD is left out** of the shot. It costs no energy (just a
  short cooldown), plays a shutter sound and a quick flash.
- Photos are saved to disk as JPGs, in a **per-world** folder under your local app data
  (`…/LocalLow/<company>/Blocks Beyond The Stars/Photos/world_<seed>/`).
- Browse them in **Tab → Photos**: pick a photo to see it full-size, **add or edit a note** (saved with the
  photo) and **delete** ones you don't want. The list shows the newest first with the capture time and your note.

### Paint tool & block designs
- Craft a **Paint Tool** (`paint_tool`, workshop recipe + the cheap `paint_tool` blueprint). Select it and
  **right-click a placed solid block** — a **32×32 pixel editor** opens (same palette and tools as the
  appearance screen: left-click paints, right-click erases, **E** is the eraser swatch, **Fill area** floods,
  **Alt+click** picks up a colour and **Undo** takes a step back; unpainted pixels show the design's paper-white
  canvas, which is what the block will look like). **Apply** paints the
  design onto the block for everyone; **Clear + Apply** removes it. Works on every block form — panels/plates
  on a wall are the natural canvas, but slabs, ramps and plain cubes take a design too (all faces show it).
- **Save & reuse**: the *My designs* column in the editor stores designs **locally** ("Save design") and loads
  them back with one click — across blocks, worlds and servers. Applying a saved design in a new world just
  works; the server keeps one shared copy per world (up to 256 distinct designs).
- **Mining a painted block keeps the paint**: the drop carries shape, colour *and* the design, so the item
  re-places with its artwork (it stacks separately from unpainted material). Designs can also be applied to
  the **held stack** directly — see §5 → Hotbar slot actions.
- **Multiplayer**: designs are visible to everyone. If you see something inappropriate, stand next to it and
  type **`/reportpaint`** in chat — the world operator gets the details (`/report Player` stays the separate
  player report). Operators can remove a player's designs everywhere at once with `/paintwipe` (see §Commands).

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
- **Weather comes in episodes** — it swells, holds and fades rather than switching on and off, and every
  world has its own temper: some flip between squalls, others brood under one sky for minutes. Storms
  build through the afternoon, mist gathers around dawn, and a slow wet/dry season rides on top.
- **Weather is per position** — a stormy biome can rain while a neighbouring clear one stays sunny,
  mountain tops sit in cloud and snow while the valley below is clear, and **fronts drift across the
  world**, so you can watch a storm arrive and move on. Weather is hidden + silent in caves/underground.
  Admins can override time/weather (see §7).
- **Beyond rain and snow** — drizzle, sleet, hail, ground fog and whiteout fog, gales, blizzards,
  heatwaves, and the genuinely alien: **acid rain** on toxic worlds, **ember fall** on volcanic ones,
  **spore blooms** in jungles and swamps, and **ion storms** and **meteor showers** that even airless
  moons and asteroids get — those used to have no weather at all.
- **Weather has consequences** — corrosive and falling weather drains your suit out in the open, so a
  roof is a real answer; rain waters planted flora so it regrows faster; scanners lose range in blown
  grit and charged air; animals hunker down in violent weather; snow settles on the ground and melts
  again when it warms up.
- **…and opportunities** — an **ion storm charges an exposed suit**, a **spore bloom** fattens what you
  harvest. Sometimes the right move is to walk into the bad weather. Craft the **weather scanner** to
  read what is coming before you set out.
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
- **Energy fence pens**: craft **energy fence** pylons (`energy_fence`, workshop, no blueprint — 2 metal
  panels + 2 cable → 4) and ring in a pen: no creature — wild, tamed or hostile machine — can cross the
  humming pylons, so animals stay home and fiends stay out. Add an **energy gate** (`energy_gate`,
  workshop — 2 metal panels + 1 energy cell + 1 circuit board) as the entrance: you and settlement folk
  walk straight through its membrane while fauna bounce off; there is nothing to open or close. Only
  flying creatures glide over a normal-height fence.

### Hover speeder (surface vehicle)
- Craft a **hover speeder** (`speeder`, blueprint-gated workshop recipe: titanium_plate ×8, cable ×10,
  energy_cell_1 ×4, circuit_board ×2, crystal ×2). It's a single-seat ground vehicle for crossing a planet fast.
- **Deploy** it from its hotbar slot (**right-click**), then **board** with **E**. While driving: **W/A/S/D** to
  steer (arcade hover handling), the camera follows in a chase view, **R** refuels it from an `energy_cell_1`,
  and **F** dismounts. Press **X** near a parked speeder you own to **pack it back up** into the item.
- The speeder has its **own energy tank** (driving drains it) and a **voxel hull** that takes collision damage —
  hard impacts dent it and enough damage destroys it. It persists with you across reloads (like a companion).

### Craftable block shapes
- Any held **building material** can be re-formed into a non-cube **shape** — **slab, pyramid, dome (half-sphere),
  sphere, ramp, stairs, cone, cylinder, panel** (thin plate), **post** (slim pillar), **beam** (horizontal bar),
  **low ramp** (gentle half-height wedge), **quarter cube** (small corner block), plus the **furniture forms**:
  **table, chair, fence** (posts + rails that connect across cells), **sheet** (an ultra-thin 1/16 plate for
  veneers) and **pot** (a small planter) — so a *wooden* table and an *iron* table are the same form on
  different materials. Every shape still places, mines and
  stacks like a block (form and dye colour combine freely). Shaped forms are **player-craft only**: world-gen,
  settlements, stations and ships stay plain cubes.
- **Sitting:** press **E** on any **chair**-shaped cell to sit down — the camera settles to seat height and
  other players see you sitting. Stand up with **E**, jump, crouch or any movement key.
- Do it in the **Crafting** menu under the **"Formen" / "Shape"** category: pick a building block, choose a form
  button (it's a free 1:1 reshape that keeps the block's colour), and **cube** reverts to a plain block.
- **Orientation:** by default a shape **auto-orients** — it follows the way you're facing, and building against a
  wall or ceiling tilts it onto that surface. Press **R** while the shaped block is selected to override this:
  it cycles **Auto → each of the six up-faces → Auto** (a HUD message shows the current pick — "Upright",
  "Upside down", "On its side" plus the quarter-turn), giving the full set of 24 placements; **Shift+R**
  cycles backwards. While a rotatable block is held, a **translucent preview** of the form hovers in the
  target cell showing exactly how it will land — this works for the built-in shapes, **your own designed
  forms**, and furniture alike. **Furniture** (bed, campfire, rug, flower pot) rotates too, but only around
  the vertical axis — it always stays upright so beds and campfires keep working. Symmetric forms (sphere,
  dome, cylinder, …) ignore orientation. Mining returns the shaped item; orientation is re-derived each
  time you place it again.
- **Auto follows your crosshair:** when a cell has no floor under it, the shape leans on **the wall face you
  actually clicked** instead of whichever neighbouring wall the game finds first. Building on a floor still
  keeps the shape upright, so extending a floor sideways lays the next block flat as before.
- **Ladders** are rotatable too, with their own short cycle: **Auto → each of the four walls → free-standing
  → Auto**. A placed ladder now *keeps* the side you gave it — mining the wall next to it no longer flips the
  whole column around or turns it into poles. Ladders placed before this update, and those in settlements,
  keep choosing their wall automatically.
- **Stairs** you craft are a real staircase now instead of a full cube: they get step geometry you can walk
  up, and **R** turns them to face any direction (or tips them upside down for an inverted step). Stairs
  placed before this update stay cubes until you mine and place them again.

### Designing your own forms
- Craft the **Shaping Tool** (`shape_tool`, workshop recipe + the cheap `shape_tool` blueprint) and
  **right-click** with it to open the **form editor**: a grid you fill in **one layer at a time**, from the
  block's floor up to its ceiling, with the layer below showing through dimly so a stacked shape stays
  readable. A small **3-D preview** turns beside the canvas, and helpers cover the fiddly parts — copy the
  layer below, mirror, clear, and a **4 / 8 grid toggle** (the finer grid is 8×8×8 micro cubes per block).
- Give the form a **name** and save it. Saved forms live under **"Eigene Formen" / "My forms"** in the
  Crafting menu's Forms tab, where they craft out of **any material** — the same arch in wood, stone or
  metal — free 1:1 like every built-in form. Carrying the tool is what unlocks that section.
- **Detail budget:** the editor shows how many boxes your form needs (e.g. "12 / 48"). Beyond the limit the
  form is refused — it keeps a wall of self-made forms affordable to draw and to walk into.
- **Sharing:** aim the shaping tool at a block **somebody else shaped** and their form opens in your editor,
  ready to save with their name on it. You can also stamp a form onto a **Form Stencil** (`shape_stencil`)
  and hand that over — right-clicking a stamped stencil adds the form to the receiver's library — or copy a
  form as a **code** (the Copy/Paste code buttons) to send outside the game.
- **Limits worth knowing:** self-made forms are decoration, so behaviour tied to specific built-in forms
  (sitting on a chair, sleeping in a bed) does not apply to them; airtightness follows the block, not its
  form, so a hollow form still seals a room; and a world holds a limited number of forms. Painting works on
  them normally. If a form is ever wiped by an operator, blocks still holding it fall back to plain cubes.
- **Reporting:** `/reportshape` flags the nearest self-made form for the server operator, the same way
  `/reportpaint` flags a painted block.

### Appearance: colours, your pixel face and body paint
- **Tab → Settings → Appearance** opens one screen for how your figure looks. Along the top are tabs for
  **Face · Torso · Arms · Legs · Helmet**; a slowly turning figure beside the canvas shows what you are doing,
  including the back you just painted. Switching tabs saves the part you were on, **Apply** saves and closes,
  **Back** leaves the part you are on unsaved. The same screen sits behind the **Appearance** button in the
  main-menu **Avatar Designer**.
- **Base colour** — every tab has the part's colour right beside the canvas: 30 swatches, or the **colour
  wheel** for any colour at all (skin and suit colours are not limited to the list). Unpainted pixels are drawn
  in that colour on the canvas, because that is what will show through. The helmet takes the torso colour.
- **Painting** — left-click paints, right-click erases, **E** is the eraser swatch. **Fill area** floods the
  area you click with the current colour (right-click fills it back to empty, **Shift** replaces that colour
  everywhere on the face you are on). **Pick colour** takes the colour under the cursor for one click —
  **Alt+click** or the **middle mouse button** do the same at any time. **Undo** takes the last step back;
  press it again and the step returns.
- **Colours** — 32 paint colours plus the eraser, arranged so each hue has a lighter and a darker partner for
  shading. The colour wheel picks by hue (outward = more saturated) with a **brightness column** beside it, and
  snaps to the closest palette entry. Faces drawn at the old 16×16 size still work — they are scaled up
  automatically the first time you open them.
- **The body parts** are painted **one face at a time at full size**. The part's faces are stacked as small
  labelled **live tiles** beside the canvas (Front / Right / Back / Left; arms and legs get one tile column per
  limb, headed Left/Right) — click a tile to paint that face, and the tiles update while you draw so you always
  see the whole part. **Clear** wipes only the face you are on, and a fill never runs over onto another face.
  The helmet's front stays open, so your face always shows.
- Everything here appears on **your figure** and **on your avatar for every other player** — it is
  server-persistent, so your look follows you to any world.

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
| **Item & Recipe Editor** | Items (stats, tool/weapon properties, worn + eaten effects) + recipes (station, inputs, market vendor theme) + optional blueprint gating | `content.json` → `tools/merge_recipe.py` |
| **Material Editor** | Block materials: paint a 64×64 tile, set mining (hardness/tool/drops), palette section, dyeable/shapeable, look (gloss/metal/glow/colour), world spawn (frequency/depth/world-type) | `material.json` + `texture.bytes` → `tools/merge_material.py` |

**Material Editor painting:** Left-click paints with the selected swatch, Right-click erases to the base
colour; Fill/Flat/Clear and an RGB base-colour picker are in the side panel. "World type" targets which
planets get the ore: any / airless / with-atmosphere / single-biome / multi-biome. Under the frequency
stepper the editor shows what the generator will actually do with that number — it scales and caps the
raw value, and a vein that starts within 8 blocks of the surface is deliberately twice as dense.

**Item & Recipe Editor keys:** recipe inputs, unlock costs and "places block" are checked against the
loaded game content when you save — an unknown key is reported instead of being written into a bundle
that would later stop the game from starting. A new item that does not place a block also needs an icon
(`client/Assets/Resources/icons/item_<key>.png`, see `tools/ai-assets/gen_icons.py`); the merge tool
reminds you.

---

## 7. Chat & admin / cheat commands

### Chat
- Press **Enter**, type, **Enter** to send (scrollback in the chat panel). Normal chat requires a **comm
  radio** in your inventory; messages are rate-limited and length-capped.
- The scrollback **fades out on its own** a few seconds after the last line (opening the chat box always
  brings the recent lines back). Prefer it always visible — or never? **Settings → Comfort → Chat
  display** offers *Fade out / Always on / Off*, and **J** hides/shows it on the spot for the session.

### Radio reach (tiered) — text **and** voice
Your radio determines **how far** your comms carry — text chat and voice both follow the same reach:
- **Comm Radio** — players on the **same planet/world**.
- **System Radio** (upgrade) — everyone in the **same star system**.
- **Galaxy Radio** (upgrade) — **everyone**, anywhere in the galaxy.

Craft the upgrades at the workshop (each tier builds on the previous one and unlocks via the tech tree).
The **widest** radio you carry sets your reach. Without any radio you can't transmit (you'll get a
"need a comm radio" notice).

### Voice chat (push-to-talk)
- Hold the **push-to-talk key** (default **V**, configurable in Settings → Voice chat) to talk to your
  radio audience; a "● Talking…" indicator shows while you transmit. Release to stop.
- Voice is **on by default**. When you **host a world for friends** it works out of the box; it needs the
  same **radio** as text chat and carries to the same tiered reach (planet / system / galaxy).
- Settings → Voice chat: a master **on/off** switch, **Voice volume**, **Transmit mic** (turn your own mic
  off while still hearing others), and the **push-to-talk key**.
- Audio is relayed **live through the server and never recorded**. Use a **headset** to avoid echo.
- *On a standalone **dedicated** server, the admin must enable voice (`voiceChatEnabled` / `--voice true` /
  `BBS_VOICE=true`); local hosting enables it automatically.*

### `/bump` — debug snapshot + screenshot (any player, no radio needed)
- **Syntax:** `/bump Description of the problem`
- Writes a detailed JSON snapshot of your current situation (player state **and inventory**, environment,
  nearby blocks/creatures/players + a wider block/flora census, ship status, and a 30-second history)
  **plus a screenshot** of the moment (the chat box is hidden for the shot; the HUD stays). It adapts to
  your context: on a surface it captures the terrain around you; **while flying in space** it instead
  records your ship's position in the system and the nearby asteroids/hostiles. The server replies with
  the saved filename. Use it to capture a bug in the moment.
- **Where it's saved:** when the game runs from inside the project source tree (a developer build under
  `client/Build/Windows/…` or the Unity Editor), reports go to the repository's `bugreports/server/`
  folder so they sit next to the code. In a normal installed build they go to the world's `bumps/` folder
  (under your save data) as before. Each report is a `bump_<world>_<timestamp>_<n>.json` with a matching
  `.jpg` screenshot beside it.

### `/report` — report a player (official hosted worlds only)
- **Syntax:** `/report Player [what happened]`
- Files a player report with the worlds portal, exactly like the report button in the ship UI's
  Alliance tab — one command, no menu digging. The report automatically attaches the reported
  player's **last 10 chat lines** as evidence and the **world id**, so the operators know what was
  said and where. The outcome shows as a local-only chat line.
- Only works while you are on an **official hosted world** (joined via the Official Worlds menu);
  everywhere else the command explains that reporting is unavailable. Reports are **reviewed by
  humans** — nobody is punished automatically.

### Admin cheats (world admin / admin only)
Type these **in the chat box** (Enter to open). They are **server-authoritative** and gated twice: the
player must be an **admin** (`IsAdmin` — the world creator, or a name in the server's admin list) **and**
the server's `CheatsAllowed` rule must be on; otherwise the command is rejected with a message. Every use
is logged (`[CHEAT] …`). Type **`/help admin`** in chat to see the list in-game — plain `/help` is the
short player help (`/report`, `/bump`) so a normal player is not buried under commands they cannot run.

**Singleplayer & hosting from the game:** cheats are enabled out of the box (`--admin-cheats true` is
passed by the bundled host) — as the world creator you can use all of these immediately. Friends who
join your hosted world still cannot: they are not admins. On a **dedicated** server the rule stays off
unless the operator starts it with `--admin-cheats true`. Command replies (target lists, results,
rejections) appear in the **chat scrollback**, not just the brief HUD toast.

| Command | Effect |
|---|---|
| `/give Item [Count] [Player]` | Give an item to yourself or a target player |
| `/tp X Y Z` | Teleport to coordinates |
| `/tp Target` | Teleport to a landmark **on the body you are standing on** — see *Named teleport targets* below |
| `/tp` | List every named target here, with the exact word to type and its distance |
| `/tpp Player` | Teleport to a player |
| `/settime day\|night\|…` | Set the world time of day |
| `/setweather clear\|storm\|…` | Set the world weather |
| `/fly` | Toggle free flight for yourself (no gravity). In **Creative/Sandbox** worlds everybody can already fly — double-tap **Space**; this is the per-player admin cheat for the other modes |
| `/god` | Toggle invulnerability |
| `/instant` | Toggle free/instant crafting |
| `/ai Prompt` | Generate an AI mission (content tool, not a cheat; needs the optional AI backend — see §5 → *Dynamic AI text* and [SELF_HOSTING.md](../developer/SELF_HOSTING.md) §8) |
| `/help admin` | List the admin commands in chat (`/admin` does the same) |

**Player names with spaces** work everywhere a command takes one: the name is simply the rest of the
line, so `/tpp mincraft Fan` teleports you to *mincraft Fan*. Capitalisation does not matter, quoting
the name is allowed (`/tpp "mincraft Fan"`) and a leading `@` is ignored. In `/give` the name comes
last for the same reason — `/give iron_plate 5 mincraft Fan`. Don't know the exact spelling? `/players`
lists everyone.

#### Named teleport targets

Typing coordinates to get to the village you can see on the map is silly, so `/tp` also takes a **target
word**. Targets are addressed by **kind + number** — never by name, because settlement names are generated
and easy to mistype. The numbering is stable for a world: `village2` is the same village tomorrow.

| Word | Where it takes you |
|---|---|
| `ship` | Your own parked ship (the medbay heal tank — same spot the suit teleporter recalls to) |
| `pad` | A landing pad |
| `village` / `ruin` | An inhabited settlement / a ruined one (`settlement` also works) |
| `vault` | A buried vault's surface pillar ring |
| `wreck` | The crashed ship — even before an NPC has pointed you at it |
| `factory` | A factory's production terminal |
| `camp` | A bandit camp (`bandit` also works) |
| `monument` | A rune monument |
| `treasure` | A hidden loot chest |
| `base` / `beacon` / `beam` / `station` | Something a player built here |

Write the number straight after the word or separated by a space — `/tp village2` and `/tp village 2` are
the same thing, and leaving it off means the first one. **`/tp` on its own lists everything on this body**
with the exact word to type and how far away it is, so you never have to guess a number.

Two limits worth knowing: this only ever resolves on the body you are **currently standing on** (the
cross-body jump is `/goto`, fleet admin only), and it does not work while you are flying in space — land
first.

#### Inspection (admins) — who is here and what have they built

Unlike the cheats above, these are **not** gated by the "admin cheats" world option: they are moderation tools,
and that option is off by default on hosted worlds.

| Command | Effect |
|---|---|
| `/players` | Every player this world knows — role, body, position and when they were last seen. Offline players come from the save |
| `/builds [Player]` | Named structures (bases, beacons, beam pads, stations) with owner, body and a ready-to-use `/goto` line; optionally for one player |
| `/where Player` | One player's body, position and last-seen time — works while they are offline |
| `/kick Player` | Ends that player's session right now. **Momentary** — they can come back; to keep someone out for good, block them in *Manage world → Manage players* (below) |
| `/paintwipe Player` (or `#designId`) | Removes that player's painted block designs **everywhere at once** (or a single design by id, taken from the report log). Wiped designs stay wiped across restarts |

#### Blocking players from your own hosted world

Your world, your rules: open the **Official Worlds** menu (or the worlds page on the portal),
pick *Manage world → Manage players*. You get everyone who has ever played on that world, and for
each of them two buttons: **kick** (out now, may return) and **block** (cannot join again). An
optional reason is shown to the player when they try to come back. Blocking follows the *account*,
so changing the in-game name does not get around it — and it only ever affects **this** world; the
rest of the game stays open to them. Unblocking is one click in the same list.

#### Observer mode (fleet admin only)

Reserved for the operator of the installation (`BBS_FLEET_ADMINS`, see
[SELF_HOSTING.md](../developer/SELF_HOSTING.md)). The owner of an individual world does **not** get it.

To reach a world: sign in on the Official Worlds screen with a **developer account** (registered with the
secret claim code) — an extra **"All worlds (operator)"** section then lists every world on the fleet,
private and password-protected ones included, each with a Play button. Joining as the fleet-admin player name
skips the world password (child-safety oversight, issue #495); once in, `/spectate on` observes invisibly.

| Command | Effect |
|---|---|
| `/spectate on\|off` | Enter/leave observer mode: invisible to players, creatures and NPCs, invulnerable, free flight through walls, no ship, no landing pad, no player slot |
| `/goto Player` | Travel to that player's body and jump to their position (works for offline players via their last position) |
| `/goto base\|beacon\|beam\|station Name` | Jump to a named structure anywhere in the save |
| `/goto BodyId X Y Z` | Jump to raw coordinates on any body — this is the cross-body teleport `/tp` is not (`/tp` names landmarks, but only on the body you are already on) |
| `/say Text` | Speak while observing. Chat is muted by default in observer mode, so a stray line can't give you away |

While observing you fly with WASD (in the direction you look), Space/Ctrl for up/down, Shift for a burst of
speed, and the mouse wheel to set the cruise speed. You may still **mine** blocks — removing an offensive build
is the one in-world moderation lever there is, and every removal is written to the server log.

The fleet admin panel's per-world page lists the same players and structures plus **build hotspots** (clusters
of changed blocks — how you find a house built without a base core), each with a `/goto` line to paste here.

#### Story / finale testing (skip ahead to the endgame)
These fast-track the story so you can read the full arc and reach the **Guardian finale** (the dialogue-duel
boss) without grinding the whole playthrough. Same admin + `CheatsAllowed` gating as above.

| Command | Effect |
|---|---|
| `/story` | Print the story status (fragments, kills, milestones, beats revealed, finale revealed/defeated) |
| `/advance [n]` | Advance the shared arc by `n` milestones (default 1), revealing any beats you cross |
| `/revealfinale` | Drive the arc to completion: reveal all narrator beats **and** place the Guardian finale system on the star map |
| `/lore` | Reveal **every** story fragment and personal memory to you (fills the Story tab); also completes the readable beats |
| `/jumpdrive` | Fit a **jump generator** (`jump_generator`) to your active ship so you can hyperjump between star systems |
| `/gotocore` | Reveal the finale (if needed) and drop you straight into the Guardian core chamber — **skips the flight and the orbital gauntlet** |

Two ways to test the finale:
- **Full run:** `/revealfinale` → `/jumpdrive` → (optionally `/god`) → open the star map and hyperjump to
  *Guardian Core* → survive the orbital gauntlet → land → descend the aperture shaft → hold to hack the core →
  win the argument duel.
- **Shortcut:** `/lore` (to read everything) → `/gotocore` → hack the core and win the duel.

The duel cannot be lost — wrong rebuttals are dismissed and you stay on the node — so just keep trying each
option until the core concedes and shuts down.

The client parses these slash-commands and sends an `AdminCommandIntent`; `/bump` is special — the client
captures a screenshot and sends it as a `BumpReport` (falling back to a plain chat message the server
intercepts if the screenshot fails). Non-admins typing a command just get a rejection toast.

---

## 8. For maintainers
When you change a keybind, add a feature, or add a command/cheat, update the matching section above in the
same commit. Keep the tables accurate against the code (controls live in `PlayerController.cs`,
`PlayerInteractions.cs`, `SpaceView.cs`, `GameMenu.cs`, `WorldMap.cs`, `ChatUi.cs`; commands in
`GameServer.HandleChat` / `HandleAdminCommand`).
