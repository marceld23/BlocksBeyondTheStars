# Blocks Beyond the Stars — User Manual

The central reference for **how to play**: controls, game mechanics, the in-game editors, and the
chat/admin commands. This is a living document.

> **Maintainers:** keep this file current. Whenever a control changes, a feature is added, or a command
> is introduced, update the relevant section here in the same change. This manual is the single source of
> truth for player-facing operation. (Written in English per project doc policy; in-game text itself is
> localized — English, German, French and Spanish are complete, further community translations such as
> Italian are in progress.)

Last updated: 2026-08-26.

> **Parents and teachers:** what the game contains, who your child can meet online and which switches you
> hold are summarised on the [parents page](PARENTS.md) ([Deutsch](PARENTS.de.md)).

---

## 1. Starting the game

- Launch the client. **Windows:** `BlocksBeyondTheStars.Launcher.exe` (shows a loading splash then starts the game)
  or `BlocksBeyondTheStars.exe` directly. **Linux:** `./BlocksBeyondTheStars.Launcher.Console` (prints "Loading..."
  to the terminal then starts the game) or `./BlocksBeyondTheStars.x86_64` directly.
  From the main menu: **Singleplayer** → pick an existing save (newest first; a long list scrolls with the mouse
  wheel, the scrollbar or the gamepad; **✕** deletes a save after a confirmation) or
  start a **New world** (name + seed), **Host Game** → host a world for friends, **Join Server**, or
  **Official Worlds** → online multiplayer on the official servers (see below).
- **Host Game (in-game multiplayer hosting):** the same world picker as singleplayer — *any* saved world
  can be hosted ("open to LAN" style) or a new one created — plus a host bar with **max players** (2–16)
  and an optional **join password**. The host bar also shows **your address** (`ip:port`) with a **Copy**
  button — read it out or paste it to your friends while the world is still loading; it is the address
  they type into *Join Server*. The game starts the bundled server locally and you join immediately;
  you are the world's admin (the very first player of a fresh world is its **WorldAdmin**; the host's
  name is additionally passed as a server admin). The same address is announced again in chat and as a
  HUD toast once you are in ("Hosting — friends can join at ip:port"). The session ends (and the world
  saves) when the host quits. Friends outside your LAN need a port forward of that UDP port.
- **Browser singleplayer (play.blocksbeyondthestars.de, glitch.fun and any `/play` page):** no account, no
  download — type a player name on the portal's start page and press **Play now**. One world per browser,
  saved in the browser itself (and in Glitch Cloud Save when you are logged in on Glitch — that is what
  keeps it across game updates and devices). **The name you pick is your player in that world** — choose it
  once and keep it; a different name later is a new player starting from scratch. The game asks for a
  name once if it does not know one yet, and recognises the saved world's player when you come back on
  another device. **New world…** next to *Singleplayer* deletes that world after a confirmation and starts
  over; your name and settings stay.
- **Official Worlds (online multiplayer, beta):** the in-game portal for hosted worlds on the official
  servers — the same site, [play.blocksbeyondthestars.de](https://play.blocksbeyondthestars.de), further down.
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
- **Portable data folder** (portable zip, USB stick, several profiles): by default the game keeps its
  settings, singleplayer saves, photos and exports in your user profile (Windows:
  `%USERPROFILE%\AppData\LocalLow\JuMaVe Games\Blocks Beyond the Stars\`). To keep them next to the game
  instead, create a text file named **`portable_data_dir.txt`** in the same folder as the game executable
  (macOS: next to the `.app`). Leave it **empty** to use a `userdata` folder next to the executable, or write
  one line with the folder you want — absolute (`D:\Games\BBTS\data`) or relative to the executable
  (`..\BBTS-Data`); `%ENV%` variables are expanded, `#` lines are comments. Existing data is **not** moved
  automatically — copy the old folder over once if you want to keep it. A folder that cannot be written falls
  back to the default with a warning in `Player.log`. Not available in the browser version.

### Crashes and blue screens — what to send

- **Game closes or freezes:** the log of the last run is `Player.log`, the run before it `Player-prev.log`,
  both in `%USERPROFILE%\AppData\LocalLow\JuMaVe Games\Blocks Beyond the Stars\`. Unity's crash handler
  additionally writes a folder per crash (`crash.dmp`, `error.log`) under
  `%LOCALAPPDATA%\Temp\JuMaVe Games\Blocks Beyond the Stars\Crashes\`. Attach the newest of each — the
  in-game **F1** report (§7 → Player feedback) already carries your OS, CPU, GPU and driver version and notes
  whether the previous session ended without a clean exit.
- **Windows blue screen (the whole PC restarts):** a game cannot cause that by itself — it is almost always
  the graphics driver, RAM/XMP, power or heat. What helps us: the **stop code** from *Event Viewer → Windows
  Logs → System*, source **BugCheck** (event **1001**); the newest `C:\Windows\Minidump\*.dmp`; your **GPU
  model and driver version** (updating the driver is the first thing to try); and whether it happens at a
  particular moment (loading, mining, flying). If you have turned **VSync off** in the Settings tab, set a
  **frame-rate cap** (e.g. 120) there — uncapped, the GPU runs at 100 % even in light scenes, which is exactly
  the load that exposes a flaky driver or power supply.

---

## 2. On-foot controls

| Key / input | Action |
|---|---|
| **W / A / S / D** | Move |
| **Mouse** | Look |
| **Space** | Jump — **hold in the air to fire the jetpack** (if one is in your backpack — there is nothing to equip); **in water: swim up / surface** |
| **Space ×2** | **Creative/Sandbox worlds only:** toggle free flight — then Space rises, Ctrl/C sinks, and you keep colliding with the world (so you can still land and build). Touching down turns it off |
| **Ctrl / C** (hold) | Crouch/sneak — walk slower, stop at ledges instead of walking off (corners included); climb **down** ladders; descend in zero-g |
| **Left-click** | Mine the targeted block (or **scan** it when a scanner is selected) |
| **Right-click** | Place the selected hotbar block (or **use** the selected gadget, e.g. the terrain scanner; with the **suit teleporter** selected it opens the destination picker — back to ship / to an ally, see §5) |
| **Mouse wheel** | Cycle hotbar slot |
| **1 – 9** | Select hotbar slot |
| **Middle mouse** | **Hotbar slot actions** on the selected slot: swap it against any backpack item, and for a building material also colour it (dye / glow / own pattern) or re-form it — see §5 → Hotbar slot actions (rebindable) |
| **F** | Attack with the held tool/weapon — hits what's **under your crosshair** (the reticle turns red over a target; with **auto-aim** on, the nearest enemy in front of you is acquired automatically) |
| **R** | At your own **cockpit / ship console** while the repair panel is up: repair the ship (see §5 → Repairing your own ship); otherwise repair the targeted wreck breach with the selected hotbar block (see §5 → Wrecks); with a **shaped block, furniture, ladder or stairs** selected: rotate its placement orientation (**Shift+R** cycles backwards — see §5 → Craftable block shapes) |
| **L** | Toggle the suit headlamp (requires a `suit_lamp`) |
| **G** | Loot the nearest container |
| **Q** | **Feed** — with food in your hand and a **begging herd** nearby, throw the animals one piece (see §5 → Creatures); does nothing otherwise, so it never wastes food |
| **H** | Store your loose materials and blocks in the nearest storage crate / wood box (tools, weapons and equipment stay with you) |
| **E** | Use a nearby ship/station tile (cockpit, workshop, cargo, medbay, …); **at a vendor: trade or talk** (a small question — **E** again trades, *Talk* opens the conversation); **board your hover speeder**; **beam** from a teleporter pad you're standing on; **choose what belongs in a storage crate** you're aiming at (see §5 → Storage crates) |
| **X** | Pack up (stow) a nearby deployed hover speeder or boat back into its item; at your own landed ship's **cockpit / console**: **recall** every speeder / boat you left out on this world straight into your inventory (parked beside the ship, with a marker, only when no slot is free; see §5 → Hover speeder) |
| **T** | Send a trade request to a nearby player |
| **K** | Send a dock request to a nearby player |
| **U** | Undock from a player / leave a boarded space station |
| **O** | Aboard your own (or any player-built) station: toggle **zero-g construction mode** for yourself — the suit floats over the decks (Jump rises, Crouch sinks) so you can extend the hull without walking; press again to walk. Not saved; off whenever you board |
| **V** | Toggle first / third-person camera |
| **I** | Toggle **thermal vision** while looking through the thermal binoculars (see §5 → Binoculars) |
| **N** | Advance the current **VEGA** dialogue line (also fast-completes the typewriter) — rebindable; gamepad **View** (Back), touch **NEXT ▶** |
| **Tab** | Open / close the gameplay menu (Inventory, Crafting, Tech, Ship, Map, Missions, Character); also closes full-screen menu screens such as the Codex |
| **M** | Toggle the world map (top-down planet view; click to set a waypoint) — rebindable; touch **MAP** |
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
| **D-pad ▲** | Open the chat (with the on-screen keyboard) |
| **D-pad ▼** | Turn the building block you are holding |
| **(A)** | Jump (hold in air = jetpack; in water = swim up) |
| **(X)** | Use / board / interact |
| **(Y)** | Toggle first / third-person camera |
| **R3** (click the right stick) | **Hotbar slot actions** on the selected slot (see §5) — stick navigates the menu, **(A)** picks, **(B)** closes |
| **L3** (click the left stick) | **Actions** — a list of everything you can do right now (rotate the held block, trade / dock with the player beside you, undock, loot / stash, repair, lamp, thermal vision, feed a begging herd, deploy a station in EVA, leave / refuel the speeder, …); stick navigates, **(A)** picks, **(B)** closes |
| **View** (the two-rectangles button left of the Xbox logo; "Back" on a 360 pad, Share on PlayStation, − on Nintendo) | **VEGA: continue** — advance or dismiss the ship AI's line (the same as **N** on the keyboard) |
| **Menu** (☰ — the three-lines button right of the Xbox logo; Unity and 360-era pads call it Start. The Xbox-logo button itself belongs to Windows' Game Bar and never reaches the game) | Open / close the gameplay menu — its top strip has the **Pause menu** button (Resume / Settings / Quit, the same dialog **Esc** opens on the keyboard); **(B)** resumes |

In menus, the left stick / d-pad navigates, **(A)** confirms and **(B)** goes back — that includes **every
tab of the gameplay menu** (Inventory, Crafting, Tech, Ship, Map, Missions, Character, …), the Codex, the
Arcade, the landing-pad chooser (pick a pad with the stick, **(A)** lands, **(B)** cancels), trade and
docking requests, the bandit demand, the blueprint / beacon / transporter windows, and both maps. **(B)**
also closes the gameplay menu itself and backs out of the main menu, Settings, the world picker and the
editors. The right stick also steers the ship in flight; the d-pad cycles the **ship-systems bar**
(laser ↔ tractor beam) at the helm. Direct hotbar number-key picks remain keyboard-only. Verbs without a
face button — everything in the **L3 Actions** list — can also be given their own button in Settings.

While a pad is in your hands every menu shows a **hint strip along its bottom edge** — "(A) choose · (B)
back", "type" when a text box is selected, and whatever else that screen offers — in the button names you
picked in *Settings → Controller*. The selected control wears a **cyan frame** and moving the cursor
clicks, long lists **scroll along with the cursor**, the **right stick** (or the d-pad) scrolls any page by hand — the credits, *What's new*, a story page — and in the gameplay menu **LB / RB** step through the
tabs. In the slot-action pie the stick walks the four wedges (up = Swap, left = Colour, right = Form, down =
Close).

**Typing without a keyboard.** Move to a text box and press **(A)**: an on-screen keyboard opens, and you
pick letters with the stick and **(A)**. **ABC/abc** switches upper and lower case, **?123** shows the
punctuation page, **(X)** deletes the last letter, **Menu (☰)** is done and **(B)** cancels. Password fields
show bullets instead of letters, number fields accept only digits, and when you are done the focus lands
back on the box you came from. It works
everywhere text is asked for — the world name, a server address, a beacon or blueprint label, the chat —
so a whole session really can be played with nothing but a controller. (On a tablet the device's own
keyboard opens instead; nothing changes there.)

**Aiming carefully.** Holding **LB** or **RB** — the place and mine buttons — slows the right stick to half
speed, so lining a block up is easier without changing your normal look speed.

**Editors on a controller.** The Ship Editor and the pixel (face/paint) editor have two modes, and
**Menu (☰)** swaps between them: the side panels (stick walks them, **(A)** picks) and the work surface. In
the Ship Editor's surface mode the left stick flies, the right stick looks, **LB/RB** drop and rise,
**L3** flies faster, the d-pad steps through the palette, **(A)** places, **(X)** removes and **(Y)** turns
the block — a crosshair in the middle of the screen shows where it will land. In the pixel editor the stick
moves a cell cursor, **(A)** paints, **(X)** erases and **(Y)** picks up a colour. **(B)** returns to the
panels; the hint line under the tools always says which controls are live.

**Controller settings.** *Settings → Controller* has the pad's own rows: the **stick dead zone** (raise it
if a worn stick drifts on its own), separate **look speeds left/right and up/down** as a multiplier on your
normal sensitivity, **invert up/down for the pad only**, **mine and place on the triggers** (extra to
LB/RB — switch it off again if your controller mines by itself, which some pads do because they report
their triggers differently), the **button names** shown in hints (Xbox, PlayStation or Nintendo — the
buttons themselves do not change, only what we call them), and a **vibration** switch whose setting is
remembered but which nothing acts on yet.

**Rebinding:** every control row in **Settings** has two buttons — the keyboard key and the pad button.
Tap the pad button and press any controller button to rebind it (actions marked **—** have no pad button
by default but can be given one). *Reset controls* restores both keyboard and pad defaults. The **Menus**
group at the bottom (close/back, open the menu) shows its keyboard keys greyed out: **Esc** and **Tab**
stay fixed so nobody can bind away the key that leaves a window — the pad buttons **(B)** and **Menu**
are rebindable as usual.

### Touch controls (experimental — tablet / touch browser)

On a touch device (tablet, or a touch-capable browser) on-screen controls appear automatically. The
buttons swap with what you're doing:

| On-screen control | Action |
|---|---|
| **Left stick** (bottom-left) | Move / thrust / steer |
| **Drag** anywhere on the right | Look / steer the ship |
| **◄ ►** | Cycle hotbar slot (ship-systems bar — laser ↔ tractor beam — at the helm) |
| **…** (beside ►) | **Hotbar slot actions** on the selected slot (see §5); shown only when the menu can open |
| **ACT** (beside ◄) | **Actions** — a list of everything you can do right now: rotate the held block, trade / dock with the player beside you, undock, loot / stash, repair a wreck, lamp, thermal vision, deploy a station in EVA, leave / refuel the speeder, … Tap an entry to do it. Shown only when something applies |
| **NEXT ▶** (top-centre) | **VEGA: continue** — advance or dismiss the ship AI's line; shown only while a line is up |
| **≡** (top-right) | Open / close the gameplay menu |
| *On foot:* **JUMP · MINE (hold) · PLACE · USE · DOWN · CHAT · VIEW · MAP** | Jump · mine · place · use/board · descend · open chat · camera · planet map |
| *On foot, when it applies:* **ROTATE · ATTACK · FEED** | Rotate the held block's placement (appears while a rotatable block is selected) · swing / fire the held weapon (hold on the Guardian core to breach it) · throw one piece of the held food to a begging herd (appears while you hold food and an animal begs nearby) |
| *Flying:* **FIRE (hold) · LAND · SHIP · AUTO · MAP · VIEW · USE · UP · DOWN** | Fire · landing pads · walk the ship · autopilot · system chart · camera · dock/board · float up/down |
| *EVA (spacewalk):* **FIRE (hold) · PLACE · DEPLOY · VIEW · USE · UP · DOWN** | Mine · place the selected block · deploy a station core · camera · board · float up/down |
| *Speeder:* **BOOST (hold) · JUMP · EXIT · FUEL** | Boost · hop · dismount · refuel |

Menus are tapped directly. Text entry (your name, chat): on a native tablet the on-screen keyboard opens
by itself; in a tablet **browser** a small input prompt opens instead. On a desktop or a desktop browser
nothing changes — controls stay keyboard + mouse (or a gamepad).

---

## 3. Space-flight controls

Enter space by launching the ship; on foot you board/leave via the cockpit. On a landed ship, **E at the
cockpit asks "Launch into space?"** — confirm with the button, **E** or **Enter**; "Not yet" opens the map instead
(the Map tab keeps its own **Launch into space** button). While flying:

| Key / input | Action |
|---|---|
| **W / A / S / D** | Thrust forward / strafe / back |
| **Mouse** | Yaw + pitch (turn). Sensitivity scales with the ship's **handling** stat |
| **V** | Toggle cockpit / third-person camera |
| **W/A/S/D** | Fly through the **system** — every planet/moon is out there at its real position |
| **L** | Land — on the body you've flown up to (the HUD shows "land on <name>") or, if none is near, back where you launched. Opens a confirmation (**Enter** = yes, **Esc** = no) |
| **E** | Board a nearby space station (within range of its hull; the ship flies round to the station's hangar mouth and docks there before you board) |
| **P** | **Autopilot** (needs an `ai_core_mk2`+ module): flies to your nav waypoint if one is set, else the nearest station / landable body; any manual input takes the helm back |
| **M** | **System chart**: a top-down map of the current system. Click a body/station to target it or empty space for a free **nav waypoint** — it shows on the radar with a distance readout, and the autopilot flies to it. The ship holds position while the chart is open. Space distances (radar, chart) read in **km**; only on a spacewalk is the way back to your ship given in metres. The chart's **Hyperspace** tab (LB/RB on a pad) shows the whole galaxy as stars in their real colours: the ringed star is where you are, named stars are systems you have visited, a **?** is one you have never entered, lines are relay jump lanes. Click a star to read about it and — with a jump generator aboard or a lane — **hyperjump to it straight from the chart** |
| **Tab → Map** | Hyperspace **jump to another system** (needs a `jump_generator` module) — flying is within one system |

Ship classes differ in **speed** and **handling** (`data/ships.json`): e.g. the scout is fast and agile,
the hauler slow and heavy. Hull + shield are shown on the HUD; shields recharge, hull does not.
Hosted/default servers allow free manual space flight so players can fly through a system without needing a
separate unlock; admins can still disable it through server world rules.

---

## 4. Menus & HUD

- **Tab menu** — tabs for Inventory, Crafting, Tech (blueprints), Ship (modules/build), Map, Missions,
  Character (appearance), plus **Story** (the story log, and a **Notes** category: up to 20 titled notes of your own,
  saved with your character — `§0`–`§f` colours a span, `§l` makes it bold, `§r` resets), **Companions** (tamed
  creatures, see §5), **Alliances** (see §5) and
  **Achievements**, with **Settings** pinned far right. The **Achievements** tab opens with a **Progress** block —
  research N of M blueprints, Codex discoveries (with a button straight into the Codex chapter, where every entry
  now names the planet and system it was first scanned on), story %, achievements done — and a **Journey** grid of your
  lifetime tallies (worlds visited, systems entered, blocks mined/placed, subjects scanned, missions …); the
  goals below run from the first blocks to the late game (thousands of blocks, dozens of worlds, the whole
  tech tree, your own station or ship, the Guardian finale) and each pays an item reward. The **Blueprints** tab
  header shows how much of the tree you have researched and which blueprint you could research next; its filter
  row shows your **knowledge points** and data fragments and has an **"Enough knowledge"** toggle that hides every
  blueprint your knowledge does not cover yet (each card also shows *Knowledge have/need*). Crafting/Blueprints/Ship are **location-bound**: Crafting to a station **block**
  (workbench, forge, …), Blueprints to your ship's **cockpit**, Ship to the **workshop module** aboard. The **gate row**
  above the list names the block you need (with its icon), how far away the nearest one is and in which
  direction; **Show** marks it on your compass, **Craft one →** jumps to its recipe when you don't own one yet.
  Hand recipes work anywhere.
- **Codex and DataQubes screens** — use the top-right **Close** button, **Esc**, or **Tab** to return to play.
  **< Menu** returns from the full-screen screen to the normal Tab menu.

### View distance and far view (Settings → Graphics)

- **View distance (chunks, 1–16)** — how far the real, walkable world streams around you. It costs the most:
  every step up loads many more chunks. It takes effect the next time a world starts.
- **Far view (Off · 512 · 1024 blocks)** — a lightweight low-detail horizon *beyond* the streamed chunks: mountains,
  valleys, seas and lava seas out to the chosen distance, plus cities, settlements, ruins and player builds as they
  stand. You cannot walk or mine it; as you get closer, the real chunks replace it. It applies at once, even from the
  pause menu. Defaults: **1024** on the desktop client, **512** in the browser and on tablets.
- With the far view on, the haze reaches farther: worlds with thin air show a long, clear horizon, airless moons are
  crisp to the edge, dense atmospheres still close in — and fog, sandstorms and ash storms still pull the view in hard.

### Arcade (minigames)
- The **DataQubes Arcade** holds 20 built-in minigames. Locked cabinets unlock through data cubes you find
  in the world; beating your **best score** on a completed run pays **+5/+10/+15 knowledge** by rating.
- **Keyboard:** arrows/WASD move, **Enter** confirms, **Space** is the primary action, **Shift** secondary,
  **P** pauses, **R** restarts, **H** opens help — and the mouse plays the pointer games directly. **Esc**
  acts as the in-game *Cancel* in games that use one; otherwise it closes the screen as usual.
- **Gamepad — every game is playable on the pad:** left stick / D-pad steer, **(A)** confirms / acts,
  **(B)** cancels, **(X)** secondary, **(Y)** help, **Menu (☰)** pauses, **View** (Back) restarts. In the pointer
  games (puzzles like Star Memory, Void Solitaire, Laser Grid …) the stick glides a **cursor reticle**
  across the board and **(A)** clicks — drags included — while the D-pad keeps serving any arrow moves.
  The start/pause/result overlays are stick-navigable like every other menu.
- **Tab availability dimming** — tabs whose context isn't met are **greyed out** (but still clickable to peek):
  **Map** needs you aboard, **Crafting** dims only when no station block is in reach (hand recipes still work),
  **Blueprints** needs the cockpit, **Ship** the workshop module aboard; a dimmed tab shows the icon of the block it is
  waiting for. While not aboard,
  the Map's travel buttons are also disabled (the world is shown but you can't quick-travel from on foot), and
  the Inventory's **Cargo Hold** transfer controls are hidden (the hold is only reachable from aboard the ship).
- **World map (M)** — top-down view of explored terrain (fog-of-war), with player/ship/station markers and
  click-to-set waypoints. The map **remembers where you have been**: ground you explored earlier — even in a
  previous session — stays lifted in a lighter tone, while live terrain around you draws in full colour.
- **HUD** — health/oxygen/hunger/energy, hotbar, location, compass, scan readout (bottom-left), and the
  wreck panel (right) when near a repairable wreck.
- **Compass** (bottom-right, on foot) — a heading-up dial: the top is the way you look, and the **N** that
  moves around the dial marks north (the map is north-up, so the two agree). A blue **arrow on the rim** points
  the way to your ship and stays readable however far off it is; the blue square inside the dial is the ship
  itself and the amber pin the waypoint, and the captions under the dial ("Ship 114 m", "Waypoint 138 m") give
  each distance. Beacons and saved markers show as smaller blips, a landed trader ship as a gold ship icon. Turn
  until the arrow points to the top of the dial and walk; VEGA reminds you of this once you are a long way from
  the hull.
- **VEGA panel** — the ship AI speaks through a typewriter speech panel with a persistent **objective
  chip** (live progress, e.g. "mine 1/3") during onboarding. Advance lines with **N** — a line stays on
  screen until you do (no auto-dismiss), and further lines wait in the queue. Advisor hints can
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
  best carried liner counts — armor pieces also carry a little insulation; the liners also slow a toxic world's
  corrosive air, see below). In vacuum the readout shows
  the sun-side/shadow hull temperature (about +120 °C to −150 °C). The world option **Environmental
  hazards** (world creation, or live in the in-game Settings tab as world admin) scales this from Off to
  Hard; Creative/Sandbox worlds are always exempt.
- **Titas** (a very rare frozen planet — only one per galaxy): the suit's climate control is not enough there. An
  **exposure meter** replaces the energy drain — the HUD shows your **cold protection** (or **heat protection** in the
  volcanic hot zones). Outside it runs out in about **40 minutes** (hot zones **30**), half as fast under a roof; liners
  (×1.25 / ×1.5 / ×2) and the hazard setting (Light ×1.5, Hard ×0.75) stretch or shorten it. Your ship, a station, a base,
  a campfire or digging deep refill it. At zero the cold (or heat) hurts more and more — VEGA warns at 50, 75 and 90 %.
  The **yellow water is toxic**: a few seconds are fine, then it burns (standing on the ice is safe). The old **SPS
  research stations** hold salvage and a log terminal — but no air and −90 °C inside, and the machines guard them.
- **Valuma** (a rare planet of wide, flat grass plains): the animals there never bite — but one of them is not an
  animal. A **Sreekmakra** wears the shape of a local animal and changes it now and then when nobody is looking. Kill an
  animal of the kind it is copying, or hit it, and it comes for you (faster and harder-biting than the animal it looks
  like) until it falls or you leave the planet. Beaten down, it drops its disguise and fights on in its true form;
  defeating that unlocks its Codex entry and the achievement *Unmasked*. Your hand scanner shows an **anomaly** when
  you scan the disguised one — under its true name, *Sreekmakra*. With enemies switched off it only shows itself and
  flees. **You can also tame it** while it wears a shape: use the creature translator on it, or tame any animal of the
  kind it is copying — then it drops its disguise and comes to you as a companion in its true form (one per player;
  achievement *Shapeshifter's Friend*). In its true form it cannot be tamed. Stay long and VEGA starts to feel watched;
  later the fog closes in.
- **Toxic worlds** (rare — about one in every other galaxy, planets and moons; an exotic world type, so the world option
  *Exotic worlds* scales it): ash and sulphur, **nothing grows or lives on the surface**, and the air is never breathable
  (the oxygen extractor barely helps). Every toxic world **rolls its own dangers**: on some (4 in 10) the **air is
  corrosive** — outdoors your health slowly drops (about 8 minutes from full at Normal); your ship, a station, a base or
  a sealed room keep it out, and the **suit liners** slow it (Thermo Liner 25 %, Insulation Suite 45 %, Climate Rig
  65 % — the best one you carry counts). On most (8 in 10) the **water is toxic** and looks it (sickly green or rust
  brown): a few seconds are fine, then it burns. The reward: **rare ores lie right under the surface** — every vein
  starts within a few blocks, uranium, neodymium, platinum, tungsten, diamond, titanium and cobalt first (a tier-2 drill
  mines them) — and on some worlds clumps of them lie **on the ground**. Rarely, a little life survives in the caves (cave
  animals, cave plants). Nobody lives there: settlements are rare and always **ruined**, ruins and factories rare, no
  bandit camps. VEGA reports each toxic world's air and water when you land. Like every hazard, corrosive air and toxic
  water follow the *Environmental hazards* option and spare Creative mode.
- **Heal tank** (workshop, blueprint-gated): the life-support unit for your own base or station. Everyone
  within a few blocks of a placed tank is slowly healed and fed and the suit recharges — the only off-ship
  suit recharge. Press **E** on the tank to make it your **home spawn**: on death you then choose between
  waking at your ship's medbay or at that home (ship is always the fallback if the home is gone).
- **Bed** (hand-crafted from logs + plant fibre, no research needed): the low-tech forerunner of the heal
  tank. Press **E** on a placed bed to make it your **home spawn** (same death choice as the tank), and
  resting near it slowly mends your health — but a bed never feeds you and never recharges the suit;
  those stay the heal tank's job. A bed is **two cells long**: the foot half lands on the cell you are facing.
- **Crew bunk** (crafted at the workshop from metal panels + plant fibre): a **one-cell** wall bunk for places
  where a bed does not fit — a narrow cabin, a corridor niche. It counts as a bed: **E** makes it your home
  spawn and resting nearby mends your health. Your **starter ship's cabin** has one; roomier ships (hammerhead,
  corvette, courier, hauler, thunderbolt, deathblock) carry a real two-cell bed instead.
- **Campfire** (hand-crafted from logs + stone): a contained flame that never spreads. It lights the camp
  (like the lantern, it throws real light onto the ground and walls around it — a forge's hearth glows dimly too),
  counters the cold while you stand near it, and is a **cooking station** — with creature meat in your
  pockets, craft **cooked meat** at the fire (far more filling than raw, and it heals). The flame also does
  the slow jobs better than your hands: char logs to carbon, melt ice to water one-for-one, boil water down
  to salt, split one log into six torches. Research the **Field Kitchen** blueprint (cheap, no prerequisite)
  and the fire cooks real **meals** — **hearty stew** (cooked meat + grain + berries + water; the most filling
  food in the game), **algae soup** (rations + water + salt) and **mushroom skewers** (giant-mushroom caps on
  a stem). Meals heal too.
- **Algae tank & detoxifier packs** (the **Bio-Refining** blueprint, after *Detoxifier*): the tank grows
  **biofuel**, **plant fibre** and **polymer** from water and rations; the detoxifier **washes toxic berries**
  back into safe ones (with carbon), filters **mud to water** and turns giant-mushroom parts into **forage
  bait**. **Archaeology** (after *Terrain Scanner*) reworks **ancient bricks** into concrete and **obsidian**
  into glass; researchers at any market buy **rune stones** for data fragments.
- **Wood box** (hand-crafted from logs): early-game storage sharing the crate's stash/loot keys, but it
  only holds a few kinds of material (8 stacks) — the workshop's iron crate stores everything.
- **Storage crates — choose what goes in:** aim at a placed crate or wood box and press **E** to pick
  which items belong in it (an ore crate, a food crate, …). From then on **H** only stores the chosen
  items there — walk your loot past a row of dedicated crates and it sorts itself. The HUD prompt shows
  **Filter on** at such a crate; select nothing in the dialog (or hit *Allow everything*) to go back to
  accepting it all. Dyed or re-formed variants of a chosen material count as that material.
- **Armor**: each piece (chest/legs/helmet) adds resistance, summed and capped (~75%).
- **Water meets lava**: lava hardens wherever water touches it — a lava **pool** (a source) turns to
  **obsidian**, a flowing lava **tongue** cools to **basalt**. Place water onto a lava pool and the pool is
  quenched to obsidian in place; place it beside lava and the neighbouring lava crusts over while the water
  stays. Water flowing into lava still chills to obsidian at the contact face.
- **Lava is slow, water is quick**: lava creeps at half the speed water flows, so a breached crater gives
  you time to step back.
- **Lava and fire burn everybody**: animals, robbers and Guardian machines take contact damage exactly as
  you do (lava −15/s, fire −10/s), so a flooded fire trench really defends a base. Land animals are kept out
  of a lava column anyway and mostly never reach it; what burns is what flies in, what was inside when you
  flooded it, and what gets driven in. Tame companions never burn, creatures that live in lava are at home
  in it, and with **environmental hazards off** (or on a Creative world) nothing burns at all.
- **Aim at water and lava**: holding a block, the crosshair stops at a fluid's surface and the block goes
  *into* that cell (the fluid makes way) — so you can bridge a lake or a lava field from its edge. Holding
  a tier-3 drill (mining beam, diamond drill) the surface is mineable too. While you are swimming the aim
  still passes through the water to the bed.
- **Sand, ash and snow fall.** Mine the block under a sand column and it settles onto the next floor;
  place sand over a pit and it lands at the bottom. Dropped onto lava it sinks one cell at a time and
  replaces the lava — a slow, safe way to fill a lava lake. Loose ground you never touched stays where the
  world made it, and a carved sand form stays a built thing.

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
- Plants, ladders and shaped blocks standing in water are **under water too**: you swim through a kelp forest
  and your suit air drains inside it — a kelp stalk or a ladder is no hidden air pocket.
- **You can fight in the water.** Water dims the view rather than ending it: a few blocks of it are
  see-through, so you can hit something swimming beside you or just under the surface, while a whole lake
  still hides what is on the far side of it — nothing notices you across open water, and you cannot snipe
  through it either.

### Mining & tools
- Tools have a **kind** (drill/scanner/…) and **tier** (1–5). A block has a **hardness** and may require a
  minimum tool tier; mining accumulates the tool's power until it exceeds the hardness, then the block
  breaks and yields its **drops**. Powerful drills can clear a small radius — the sweep only takes blocks
  the drill could mine directly (same tier rules).
- **The game tells you which tool a block wants.** Scanning a block lists a `Needs:` line naming the cheapest
  tool that opens it, and a swing the held tool cannot land is refused by name ("Needs: Titanium Drill")
  rather than a bare "wrong tool". The first time a tier gate turns you away, VEGA explains it once.
  Tier-2 blocks (machine housings, factory terminals, titanium/platinum/tungsten and the rare ores) will not
  budge for the starter drill — and since titanium ore is itself tier 2, your first titanium comes from a
  wreck, from loot or from **trade**, never from digging.
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
- **Landing pads are on dry land whenever the world offers any** — the pad search looks in every direction,
  and on ocean worlds further. A pad that still sits in open sea rises on a small island (beach, grass, a few
  plants); only in shallow water does the ship park in a dry shaft on the seabed. The approach map draws such
  pads **blue** and says how deep the water is ("underwater · seabed · 6 m"); your first landing and any
  landing you do not pick by hand prefer dry ground.
- **Never in the lava.** On worlds with lava seas, rivers or lakes a pad over lava stands on a **basalt island**. Worlds
  created before that change keep their pads: a pad there that lies in lava is drawn **orange-red** ("lava!"), you can
  only land on it when every other pad is taken, and a ship that was parked on one is moved to a free pad the next time
  the world loads — you wake up aboard.
- **Your ship is a real parked object** on its landing pad (pads are naturally flat). You can
  **furnish the interior**: place blocks in free cabin space (and mine those again) — they stay with
  the ship across launches, landings and the walk-in interior. The hull cannot be damaged and ship
  modules (medbay, cockpit, …) cannot be removed. Step or hop up through the hatch to enter.

### Inventory & cargo hold
- Your **inventory** is your personal backpack (24 slots) — it travels with you everywhere, and its first
  nine slots are the **quick-bar** (the on-screen hotbar).
- **Suit gear works while carried.** Armour, oxygen tanks, suit liners, the jetpack, the suit lamp and the
  stealth suit take effect as soon as they are *anywhere* in your backpack — there is nothing to equip and
  no slot to put them in. The Inventory's **Suit** tab lists just that gear and shows what it currently
  gives you: **armour** (pieces add up, capped at 75 %), **maximum oxygen** and **insulation** (of tanks and
  liners only the best one you carry counts). The same line sits at the top of the Backpack tab, and the
  HUD oxygen bar's full mark is your real maximum.
- Your ship's **cargo hold** is bulk storage that belongs to the ship (48 slots, growing with cargo-hold
  modules) and is shared by everyone aboard that ship.
- **What goes where:** mined and crafted items fill your inventory first and only spill into the cargo hold
  once it's full (and only while you're aboard). Salvage you scoop up while flying in space goes straight
  into the cargo hold. While you're aboard the ship, crafting draws from **both** at once. Asteroid ore that
  finds no room **floats at the rock** instead of vanishing; a tractor beam pulls it in from 16 m, without one
  you fly through it. Floating salvage survives a landing — it is still there when you launch again.
- **Moving things by hand:** open the **Tab menu → Inventory**. The **Inventory** tab has a **"Stow all
  materials in cargo"** button (loose materials/components only — your tools, weapons and quick-bar items
  stay put), and selecting any item offers **"Move to cargo hold"**. The **Cargo Hold** tab shows the hold's
  **used/total** capacity, a **"Take all out"** button, and per-item **"Move to inventory"**. These only work
  while you're aboard (in flight or standing in the landed cabin); on foot the cargo tab says so.
- **Auto-stow (optional):** turn on *Settings → Comfort → "Auto-stow into cargo on boarding"* to have loose
  materials moved into the hold automatically each time you board. Off by default.
- **Sandbox: All items.** In a Sandbox world (or while an admin gave you the Creative mode) the Inventory has an
  **All items** page: every item of the game, searchable — pick one and **Take 1** or take a full stack. Nothing has
  to be crafted.
- **Sandbox: crafting is free.** In the same mode every recipe can simply be crafted — no materials, no blueprint,
  no workbench in reach, a full stack per order — and the craft panel says so instead of "Materials missing". The
  only refusal left is a full inventory.
- **Every tool looks like itself in your hand:** the titanium and diamond drills, the mining beam, each pistol and
  blaster, the machete, vibro knife and plasma sword and the advanced scanner all have their own model.
- **Throwing things away:** select an item in the **Inventory** or **Cargo Hold** tab and press **"Throw
  away"** — it asks once ("Really throw away?"), and the second click destroys *every* stack of that item.
  This cannot be undone and gives nothing back. Your starting equipment (drill, scanner, suit lamp, machete,
  sidearm) has no such button, so you can never leave yourself without a way to dig or a way to see. (To put
  **one piece of food on the ground** for a begging herd, use **Feed** — **Q** — instead; see §5 → Creatures.)
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
    crystal per block** (the button shows the cost, and the panel refuses if you're short). A glowing block
    **shines in its light colour itself** — it blooms like a lamp and stays visible at night and from afar — and
    lights everything around it in that colour (glowing glass takes the colour but stays a frosted pane). **Glass and the
    light fixtures dye too**: dyed glass stays frosted, just coloured (the blueprint-gated **Clear Glass** — two glass + one polymer at the workshop — is the see-through exception for canopies and domes, and dyes too); a **dyed lamp casts light in its dye
    colour** (no crystal needed — it already is a lamp); a dyed torch burns with a coloured flame. Doors
    can't be dyed (they're moving fittings, not blocks). Under **My
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
- Recipes are made at **stations**: hand (free, anywhere), workshop, refinery, detoxifier, transmuter, market
  (barter). Inputs are consumed, outputs produced (free in Creative). Each station is a **block** you stand
  next to — **workbench** (workshop), **forge** (refinery), **detoxifier**, **matter forge** (transmuter),
  **algae tank**, **campfire** — or the matching **ship module** while you're aboard. The menu names the block
  a recipe needs and where the nearest one is; aiming at a placed station block shows on the HUD which
  menu tab it powers. The Codex guide *Stations & the Tab menu* has the full table.
- The **workshop** is your everyday bench: it smelts the common ores (iron, copper, gold, silver, …) into
  ingots and fabricates parts, tools, weapons and building blocks.
- The **refinery** (the *forge* block on a base, or the refinery ship module — both unlocked via Tech) is
  the high-heat metallurgy station. It alone smelts the **rare Tier-2 metals** (titanium, cobalt, tungsten,
  platinum, uranium, neodymium) and refines diamond, carbide and reactor fuel. It also offers **higher-yield
  smelts** of the common ores (e.g. iron and copper) than the workshop — handy for bulk metalwork. You can
  never need it before you can build it: its metals are also the ones that need a titanium-tier drill to mine.
- **Every metal has a job:** each ore family (aluminium, tin, nickel, cobalt, platinum, lead, zinc, tungsten,
  lithium, neodymium, plus light alloy, biofuel and magnets) feeds at least three recipes across two stations —
  refinery variants of bronze, brass, steel, carbide, power cells and magnets out-yield the workshop ones, lithium
  triples a cell batch, and biofuel makes torches and lanterns where no tree grows.
- **Interior decor is craftable:** the lights, light strips, force field, medbay/lab/cargo/engine panels, engine
  nozzle, factory terminal, pipe and machine housing that ship interiors, stations and factories are built from all
  have workshop recipes (lights: crystal in a glass housing — no power needed; the force field needs the energy-door
  blueprint). Only the data cache stays loot-only.
- The **transmuter** (the *matter forge* block or ship module, unlocked via Tech) compacts spare terrain
  (sand, dirt, stone, …) into *matter dust* and synthesises it back into ore — a sink for surplus digging.
  With the **matter resynth** blueprint it also rebuilds titanium, silver and cobalt ore and lithium from dust
  plus a power cell. The endgame ores (tungsten, platinum, neodymium, uranium, diamond) stay **mining-only**.
- **Blueprints** form **chains**: most advanced nodes build on a cheaper one (the stasis projector on the field
  medkit, the beam pad on the energy door, the suit teleporter on the jump generator, …), so the Blueprints tab
  reads as a tree rather than a flat list — a node lights up once its prerequisite is researched, and it is
  never cheaper than what it builds on. Anything you had already unlocked stays unlocked.
- **Blueprints** gate advanced recipes — research them at your ship's **cockpit** (Blueprints tab; the helm counts
  while flying) with **knowledge points** (earned by scanning) plus
  research materials; some require prerequisite blueprints.
- **Tiered upgrades consume their predecessor:** where an item is a straight upgrade of another —
  oxygen tanks II/III, the terrain scanner, weapon upgrade chains, the AI cores — the recipe **requires
  and consumes the previous tier**, so you never end up carrying a redundant Mk1 after building the Mk2.
  Fitting the `ai_core_mk3` module **replaces** the fitted Mk2 and returns half its materials as salvage.
  (Your starter gear — basic drill, scrap pistol — is deliberately chain-free.)
- **Disassemble** (at a workshop): break a crafted item back into ~50 % of its recipe inputs. In the
  Inventory detail pane, select the item and press **Disassemble** (shows what it recovers). Raw resources,
  refinery/transmuter-synthesised ore and campfire produce (charcoal, salt, meals) can't be disassembled, and
  an item that would recover nothing (berries, seeds, a skewer) is simply kept — nothing ever vanishes.

### Ship, modules, building
- Every ship's **cockpit front screen is clear glass** — you look out forward through it; the side and rear
  windows keep the frosted look. Self-built ships choose per pane (`glass` vs. `glass_clear`).
- A ship is a set of fitted **modules** (cockpit, reactor, life support, workshop, medbay, cargo holds,
  refinery, detoxifier, transmuter, …). Modules enable on-board stations and cargo capacity. Build/expand from the
  Ship tab — aboard, at the workshop module.
  Each cargo-hold module adds slots to the shared **cargo hold** (see *Inventory & cargo hold* above); the
  Cargo tab shows the current used/total capacity.
- **What's fitted, and taking it out again:** the Modules tab lists your active ship's fit at the top and
  marks fitted modules **Fitted** (one of each per ship — there is no slot budget); the Fleet tab shows each
  hangar ship's modules. Any module except the hull essentials (cockpit, reactor, life support), the stations
  (workshop, medbay, quarters) and the basic hold can be **removed** from its detail pane — aboard, at the
  workshop — and gives back **50 %** of its parts (per part, rounded down). A cargo expansion only comes out
  while the remaining hold still fits everything stored in it. Moving a module to another ship means removing
  it here and building it there.
- **Reactor fuel** (uranium + lead at the refinery) is a **one-time build cost** of the big things: the three
  capital ships (Thunderbolt 2, Hammerhead 3, Deathblock 4), the heavy laser cannon and the jump generator ignite
  their reactors with it once. Nothing burns fuel while running — every device carries its own energy cell.

### Building your own ship (keel → commissioning)
- Unlock the **Shipwright** blueprint (Blueprints tab), craft a **Ship Keel** at a workshop and place it on open,
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
  until you refill them. Whenever your ship needs work, a **"Repair ship" / "Schiff reparieren"** panel sits on
  the right of the HUD (a hull bar plus the materials still needed; it greys out and says so while you are
  short). To repair, stand at the **cockpit** or the **ship console** and press **R** — one action fixes hull and
  cells together. The panel refreshes on every landing and login, so it is never stale.
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
- **Advisor:** one-time contextual hints (full inventory, first nightfall, "ruins detected", world-type
  flavour) plus **context tips** that notice your situation and may come back — rarely: gear you carry
  but aren't using (a suit lamp switched off in the dark, torches underground, food when hungry, a medkit
  when hurt, a drill in the pack while digging by hand, an idle terrain scanner), rare ore or a missing
  ingredient right in front of you, a recipe or blueprint you can afford now, a settlement / ruin /
  factory / trader / other player close by, and in space minable asteroids, a station, a low hull or a
  ready jump generator. Low oxygen/energy/hunger and temperature warnings repeat with a long cooldown.
  Every tip waits a few seconds, VEGA says at most one every couple of minutes, each repeats at most two
  or three times per save (and stops for good once you react to it). Mute via Settings → **VEGA hints**.
- **AI-core modules:** `ai_core_mk2` adds +6 terrain-scanner radius, hostile-contact callouts in space and
  the **autopilot** (press **P** in flight); `ai_core_mk3` adds a 12 % evasive-manoeuvre damage negation.
- **Memory fragments:** data terminals in wrecks and vaults drop `ai_memory_fragment`s — VEGA redeems them
  aboard (+3 knowledge each) and tells her backstory over 10 beats; the final beat hands over the **research
  materials** for the AI Core Mk3 (stowed in your pack/hold — VEGA waits until there is room), which you then
  research at the cockpit like any other blueprint once you have the knowledge for it.

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
- A trader that **docks at a station** becomes a **visiting merchant** for **7–12 minutes**: board that station
  and you'll find its pilot as an extra **vendor** beside the trade post — barter with them like any other
  vendor (press **E**).
- A trader that **lands on a world** parks its ship on a pad with the **pilot standing in front** as a merchant
  you can trade with — it barters from the same **traders** list as a traders-themed vendor (see *Vendors /
  market* below). It stays **10–15 minutes**, and it **never lifts off while a player is near it**
  (within about 32 blocks of the pilot or ship) or trading: it leaves only after nobody has been close for
  about half a minute. One landed trader per body at a time; they re-appear when you return to that world.
- While a trader is landed, the **planet map (M)** shows a **gold ship marker** with its name at the pilot (and
  lists it with its distance), your **compass** shows a matching gold blip, and its pad shows as taken — so a
  trader that set down far from your pad is easy to find. The marker disappears when it lifts off.

### Stations: boarding & docking
- **Space stations**: approach in space and press **E** to board. A station is its **own place in orbit** —
  you arrive inside it, floating in space (black sky, no planet/weather, life support), and can walk the
  interior (vendors, mission board, heal tank, quarters) and talk to its crew NPCs. Press **U** to leave and
  travel back down to your ship on the planet. **Quit the game aboard a station and you come back aboard** — at
  the spot where you stood; your ship waits at the planet the station orbits.
- **What you see from the cockpit is the station you walk.** Every station floats in space as its real hull, block
  for block and at full size — the module halls, the windows, the glowing **force-field mouth of its hangar**, solar
  wings, antenna masts and domes. Big stations are big: a colossal one is over a hundred blocks across. The hull is
  solid (you slide along it and can fly between its modules), the dock prompt appears near any side of it, and on
  **E** the ship flies round to the hangar and noses in. The autopilot and a chart target fly you to the space in front
  of the hangar. A station's layout is fixed the first time you see it.
- **Your start system always has a station** (new and existing worlds): if the system rolled none, one hangs
  over your start planet. Other systems may have none — VEGA then points you to the star map.
- **Build your own**: deploy a **Station Core** on a spacewalk (press **B**), build a hull + an airlock door
  around it, and it commissions into a boardable station on the star map.
- **Air only fills sealed rooms** in a station you built: walls, glass and doors are airtight, a hole in the
  hull means **helmet on** (the suit tank takes over, and you get a warning) until you patch it — a
  **force-field block** seals an opening you want to keep. **Crew** (two civilians) moves in once you build a
  **trading post** or a **mission board** aboard — in a sealed room; a post open to space stays unstaffed
  until you seal it. Visiting traders still dock regardless. Windows show the planet you orbit, the sun and a moon.
  The crew keeps the **station clock**: at station night the deck lights dim and the crew sleeps in the beds of
  their rooms (see *Daily routines & jobs*).
- **Stations are built from modules.** A new station is assembled from docking segments — an arrival hall,
  corridors, a market hall, a mission office, a canteen or a bar, a medbay, hydroponics, a store room, the hangar,
  and **crew quarters with one cabin per crew member** (bed, locker, lamp, a chair). Big stations have two decks
  joined by a ladder shaft. The crew works its post by day, sits in the canteen in the evening and sleeps in its own
  cabin at night; you may walk into the cabins. A station you have boarded before keeps its layout for good.
- **Villages, towns and cities are built from modules too.** A new settlement is put together from buildings made to
  fit the planet — the walls are sand on a desert, ice on a frozen world, iron in a town — and half of all settlements
  are alien, with crystal trim and other roofs. Every settlement has a market and a notice house, villages and larger
  places a **tavern** where the people meet in the evening, towns and cities a **workshop**, and houses of two or three
  kinds. Town houses have two storeys (city flats three) joined by stairs. **Everyone who lives there has a bed**: the
  shopkeeper, the quartermaster, the innkeeper and the gardener sleep in a back room of their building. A settlement
  houses as many people as it has beds — at most 6 in a hamlet, 10 in a village, 20 in a town, 32 in a city and 80 in
  the G.D.S. city. Settlements that already stand in your world keep their buildings exactly as they are; their people
  now follow the beds that are there.
- **Gravity ends at the hull**: inside your station's box (plus a few blocks around it) you walk; step or fall
  past that and the suit **floats** — jump rises, crouch sinks, so you drift back to the deck or build the
  outer hull from outside. Drift very far away and you are set back on the pad. **U** always returns you to
  your ship.
- **Name it**: rename a station you built from **Tab → Map** (select it → **Rename**), or by pressing **E**
  on the **station core** while standing inside your own station. Only the owner can rename it.
- **SPS relay** (late game): any commissioned player station can be converted into a relay of the old
  Stellar Positioning Service — select it on **Tab → Map** and **deliver** the bill of materials in person
  (bulk metal plates, circuit boards, reactor fuel from the refinery). The meter is shared: friends can chip
  in. Two finished relays in **neighbouring systems** form a **jump lane** — hyperjumps between those two
  systems need **no jump generator**; lane routes carry a **⇄** mark on the Map. The network changes the
  world: relay systems **draw traders**, and on a *Growing* world a lane into one of the outermost systems
  **grows the galaxy** beyond it. VEGA comments each first — relay, lane, growth — in the Story Log. The
  Codex article *SPS Relays* has the full picture.
- **SPS Survey Orders** (after the ending): once the Guardian is defeated, **station** mission boards start
  posting a four-step survey chain — scan two anomalies, look in on a system the relay network has not
  reached yet, bring circuit boards to a station that is being converted into a relay, and drive off three
  remnant machines. Each step is handed out at a station board, one after the other, and pays materials plus
  knowledge. **The chain starts over** once you finish it, so there is always something on the board out
  there. Steps the world cannot offer are simply skipped — no unlinked systems left, no relay left to build,
  or machines switched off in the world rules.
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
  sliding) do NOT hold air. Holding any door shows a **hologram of it in the target cell**, turned by the wall
  beside it, so you see which way it will face before you place it. Chain rooms door by door to grow a whole airtight outpost; if a wall is mined or
  burns away, everyone at the base gets a **"no longer airtight"** warning and the rooms fall back to suit
  oxygen until the hole is closed. **Check it:** aim at your own core — the prompt shows how many cells and
  sealed rooms currently have air and whether the spot you stand on does (*here: air*). VEGA explains the
  three rules once when you found a core on a world without breathable air.
- **Sentry post** (workshop, blueprint-gated after the Heal Tank): a small automatic turret you place inside
  your own base zone. It fires at hostile machines, at **hostile animals**, and at robbers who have
  **already started a fight** — never at players, never at tame animals, and never at somebody walking up to
  talk (you always get to answer a hold-up yourself). It needs neither power nor ammunition, and you can
  build as many as you like. Three things worth knowing: it has to stand **within 8 blocks of your base
  core** (place one farther out and the game tells you on the spot that it will not fire), it reaches
  **14 blocks**, and it only works **while you are home** on that world. Machines appear 35–50 blocks away
  from you — so a sentry is the thing that covers your back while you build, not a fence that clears the
  neighbourhood. Scan a post to read its range and the zone it needs back. On **Creative** or **Peaceful**
  worlds it stays quiet, like everything else.
  A sentry's kill **counts for you**: bandit and machine bounty steps progress, a scout it finishes still
  counts towards *Guard the homestead* and the base-defended tally, and the drops land on the ground where
  the target fell. Only the plain "defeat" achievement stays yours to earn by hand.
- **Visitors at the base** (Settings → world rules, world admin, **off by default** — on in the `dangerous`
  preset): with it on, and only while **you are home**, two bandit **scouts** occasionally walk up to the
  **edge of your zone**, stand there for about a minute, and wander off again. They **never step inside, never
  demand anything, never damage a block and never take a thing** — they look, and that is all; your sentry
  leaves them alone too, exactly as it leaves a robber who is still talking. Hit one and it fights like any
  robber. Driving two of them off at your own base is the **"Guard the homestead"** bounty (on any board,
  only offered while the option is on) and the *Not On My Doorstep* achievement. Needs **both** the
  **Bandits** slider and **Planet enemies** on, plus Survival — with hostiles off the sentry post is silent,
  so no scouts come either, and the switch is not even offered. An update never turns it on for you: on
  worlds from before the option existed it stays off until you flip it yourself.
- **Residents, posts and jobs**: beds bring people, and a trading post or mission board at home is staffed by
  one of them — see *People you know* and *Daily routines & jobs* below.
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
  station).
- **Crews (named groups):** the **Crew** view in the Alliances tab holds a named group of up to **8 players**.
  Everyone in the crew counts as **allied with everyone else** — one invite instead of a web of handshakes, ideal
  for a family or a class. The **owner** founds the crew (the name is screened like any player name), invites
  **online** players (no join codes), can rename, remove members or disband; anyone may leave at any time, and if
  the owner leaves, the longest-serving member takes over. Crew and manual alliances are **independent**: leaving
  the crew keeps an alliance you formed yourself, and ending a manual alliance never cuts crew access. Crew mates
  appear in your Allies list with a "Crew" tag (no End button — leave the crew instead).
- Allies can **beam to each other**: a held **suit teleporter** (right-click) lists every ally standing on the
  same planet — see *Suit teleporter* below. Ships stay private: the jump is refused while your ally is
  aboard theirs.
- **Family-friendly play:** this is a game for all ages, so keep radio chat and the names you give players,
  bases, stations, beacons and creatures friendly. You can **mute** any player — type `/mute <name>` in the
  chat box (`/unmute <name>` undoes it), and you will neither see their chat lines nor hear their voice.
  Muting is yours alone: it is stored on your own device, the other player is never told, and the list is
  under **Settings → Muted players**, where one click unmutes again. You can also mute or unmute a player
  straight from their row on the **Alliances tab** (Find players, Allies and Crew lists) — same list, same
  rule. The host can turn **voice chat** off entirely. See the in-game Codex chapter **House Rules** for the full list.

### Beam blocks (teleporter pads)
- Craft a **beam block** (`beam_block`, blueprint-gated workshop recipe: titanium + cable + energy cell + crystal),
  **place** it, and **name** it. Step onto one of your pads and press **E** to open a **transporter panel** listing
  every beam block you can reach — your own and any **allied** player's — **on the same world**, with each pad's
  name, coordinates and distance; pick one to **beam** there.
- Beaming costs **6 suit energy** with a **6 s cooldown**. Each pad shows its name on the planet map (key **M**)
  and as a floating label in the world. Only the owner can rename a pad; mining it removes it. A pad glows teal
  and softly lights the room around it.

### Suit teleporter (back to ship · to an ally)
- Craft the **suit teleporter** (`suit_teleporter`; blueprint *Suit Teleporter* in the Suit category, then a
  workshop recipe: titanium plates + cable + energy cells + data fragments). Put it in the hotbar, select it and
  **right-click** (touch/pad: the *place* action) — a small **destination picker** opens:
  - **Back to ship** — recalls you to your ship's heal tank (the respawn point). This is what the device has
    always done.
  - **Allies on this planet** — one row per **allied** player who is currently on the same world (name + distance
    when they're in view; *out of sight* otherwise). **Beam** puts you down **beside** them, facing them.
    Non-allied players are never listed, and the server checks the alliance again on every jump.
- Both jumps cost **10 suit energy** and share a **30 s cooldown**; neither works while flying in space, and the
  ally jump is refused while that ally is **aboard their ship** (ships stay private) or on **another body**.
- **Multiplayer hosts:** the world rule **Starter teleporter for everyone** (Tab → Settings → world rules,
  world admin; or launch with `--starter-teleporter true`) hands every player who joins without one a suit
  teleporter — switching it on also gives one to everybody online. It stays an ordinary item (unlike the
  protected starter kit) and is off by default, so singleplayer progression is unchanged.

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
- Besides the basic parts (plates, panels, cable, glass, steel, energy cells, circuit boards, polymer) factories
  run a **raw-ore tier**: light alloy, bronze, brass, power cells, carbide, magnets, diamonds and even reactor
  fuel straight from ore — every ore has a second buyer that way. A factory's roster is **pinned the first
  time its hall is stamped** and stored with the world, so recipes added in a later update turn up only in
  factories stamped after that update — a hall you have already found (or claimed) keeps making exactly what
  it made before.
- **Operating a terminal is public** — you don't need to own a factory to craft there. But a spawned
  factory is **read-only** (you can't mine or rebuild it) until you **claim** it: stand at the terminal
  with an **SPS access code** (see below) and press **E**. Claiming spends one code, makes the factory
  **your base** (you and your **allies** can rebuild it freely), and persists across reloads. One code
  claims one factory.

### Greenhouses
- Every village and city keeps a **greenhouse** — a glass house full of crops you can walk into and
  harvest. A **village** grows them in soil beds under a timber-and-glass gable behind a hinged door; a
  **city** runs a two-tier **hydroponics bay** with grow lights and a sliding door, which is easy to spot at
  night. Space stations of any size above the smallest carry the same bay aboard.
- There are three **crops**: the **crop berry** bush (berries), **crop grain** (tall golden ears → grain, edible
  raw but meant for cooking) and the **crop mushroom** bed (mushroom caps). Each greenhouse grows one of them —
  which one depends on the settlement, so a city with several houses serves a mixed harvest, and two stations
  need not grow the same thing.
- Crops are farmed, not wild flora: they are safe to eat on every world, the scanner reads them as **Edible** under
  their own name, and each picked plant **grows back on its bed** after a short while — so a greenhouse is a food
  source you can come back to.
- Want your own? Craft **seeds** by hand — 3 berries → 2 berry seeds, 3 grain → 2 grain seeds, 2 mushroom caps →
  2 mushroom spawn — and plant them on soil (mushrooms also take mud and mycelium) — or on a crafted
  **hydroponic tray** (workshop), which lets any crop root with no soil at all.
- **Trees grow from saplings.** Craft a **sapling** by hand (1 log + 2 plant fibre → 2 saplings) or pick one up
  now and then when you clear leaves. Plant it on soil, grass or mud with **four free blocks above it**; after a
  couple of minutes it becomes a tree with a log trunk and a leafy crown. Under a low ceiling it simply waits.
  Leaves, needles and fronds drop as blocks now, so you can build and shape a canopy by hand — and a sapling
  planted on the dirt floor of a sealed station hall grows just like one on a planet, so an arboretum in orbit
  is a matter of dirt, saplings and headroom. A sapling you pick up stays a sapling.

### Ruins & treasure chests
- **Ruins** are the collapsed remains of fallen settlements — mostly surviving ground walls, one
  half-standing tower, and rubble overgrown by flora. Unlike bases and stations they are **not protected**:
  every block is **freely mineable**, and what you clear stays cleared. VEGA may hint at *"structural
  echoes nearby — ruins or wreckage"*; bring a scanner, there's often something worth digging out.
- **Field records:** ruins, wrecks, buried vaults and data terminals carry **readable texts** — logs,
  notes and plaques that surface while you scavenge them. Each opens in a reader panel, is kept in the
  Story tab (*Field records*) and the Codex **Lore** chapter, and some only appear once the story has
  come far enough. Data terminals and monument relic caches may also hold a **net fragment** of the
  story itself — VEGA occasionally picks up a **fragment signal** and marks it on your map.
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

### Mysteries: one-of-a-kind places & space surprises
- Three places exist **exactly once per galaxy**: the **Singing Shrine** (a ring of rune pillars with
  quiet keepers living around it), the **Sealed Observatory** (a glass dome of the old Service), and
  **The Long Quiet** — a named derelict drifting in one system's space, boardable like any station and
  full of salvage and its ship's manifest. Where they stand, only a **Friend**-tier NPC will tell you
  (and mark your map); the derelict shows on the star map of its system.
- Space holds two friendly surprises: a **life pod** sometimes drifts through a system — **fly close**
  and you rescue the survivor (a small thank-you, a new person in *People you know*, and a radio call
  later) — and an **anomaly** no catalogue explains: **scan** it for knowledge and a field record.
- Many systems also hold a **space wreck**: the travel screen lists it with its ship's name and "Wreck",
  it shows amber on the radar and on the system chart (**M**, click it to set a waypoint), and it is a
  **fly-to**, never a quick-travel destination. Coming close reads its manifest (a field record, and the
  wreck counts as visited); the **mining laser** carves it up like an asteroid for plating, cabling, a metal
  and now and then a data fragment — an EVA pick works too.
- Every **asteroid** listed for a system stays a visible dot from anywhere in that system, and the green
  bearing blips on the radar's rim carry the body's name — the chart's waypoint is still the quickest way there.
- Everything in this section is peaceful and appears under every preset — nothing here fights back.

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
- **Build jobs:** settlement boards also offer one **building assignment** — raise a shelter, light the
  camp, raise a beacon, or extend your own base. Progress counts as you **place** blocks (mining them back
  out never loses credit) and the job turns in at the board like any other.
- **Survey jobs:** every board also offers one **scan assignment** for explorers — settlements ask you to
  *survey the wildlife* (scan three creatures — the same herd counts), keep a *hostile watch* (scan one
  hostile creature, from a safe distance), *read the stones* (scan a monument's runes) or run a *botany
  survey* (discover two plant species you have never scanned); space-station boards ask for an *asteroid
  survey* with the ship scanner. Surveys pay a few **knowledge points** on top of the item reward. The
  mission detail now names every objective ("Scan · any creature 2/3", "Deliver · Cable 0/5").
- **Mission chains:** some jobs come in **parts** — the card and the detail say *Part 2 of 4*. Every
  settlement board offers the four-part **"Settlement needs"** chain (iron → lights → a wildlife survey →
  clear a bandit camp, or scout a neighbouring body on peaceful worlds); the next part appears on the
  **same board** once you turn the previous one in, and the quartermaster gives you a **radio nudge** a
  little later if you wandered off. After **three turn-ins at one board** a quartermaster who knows you
  posts a two-part **big order** — a doubled delivery with the pay to match, then a large build or survey.
  Later parts can't be taken early (the server checks), and they stay where you started them.

### NPC professions: doctor, shopkeeper, arms dealer, sage, tamer, blockfarmer, streamer, reporter
- **Where you meet them.** Newly generated villages and towns may have a **clinic, shop, armory, library, stable, quarry,
  studio or newsroom** — each with its keeper. Newly generated **space stations** may have a clinic, shop, armory, archive,
  studio or newsroom (no tamer, no blockfarmer in space), and the city of **Ember Reach** has a services quarter with the
  same six. You can also get them at home: research **Profession posts** (Station tree)
  and build a **clinic post, shop counter, arms rack, sage's terminal, tamer's post, quarry post, streamer set or press desk**
  inside your base or on your own station — a resident takes the job (like the trading post).
- **Doctor:** medpacks, field medkits and — expensive — a detoxifier; a bed and a stretcher only **every other day**.
- **Shopkeeper:** food — but only while you stand **inside the shop** with them.
- **Arms dealer:** cables, plates, energy cells, and finished laser pistols or plasma blasters for diamonds and gold.
- **Sage:** data fragments and AI memory fragments at a very high price.
- **Animal tamer:** baits and the creature translator; a tame animal of the planet walks at their side.
- **Blockfarmer:** quarries outside the settlement by day and sells stone, sand, dirt and basalt.
- **Streamer:** a huge fan — walks by once a day asking for a **photo together**; say yes and a photo is taken, say
  "never" and they stop asking.
- **Reporter:** gives you an **interview** — write what you have been up to (at most 300 characters; in Safe chat mode
  you pick a ready answer). Everyone can read the place's latest stories by asking the reporter "What's in the news?".

### People you know & NPC radio calls
- **NPCs remember you.** Trading at a stall or taking a board job raises your standing with that vendor or
  quartermaster: **Stranger → Acquaintance → Friend**. The stage shows on their **nameplate** when you walk
  up, and everyone you know is listed under **Tab → Character → People you know** (name, role, stage, where
  they live).
- **Talk to people.** Walk up to any NPC and press **E** (away from station blocks, which keep their own E). At a
  **vendor**, E asks *Trade or talk?* — **E** once more opens the Market, **Talk** the conversation.
  Settlers chat with anyone; vendors open up once they know you. Some conversations offer **choices**
  ([1]/[2]/[3] or click) — your pick is remembered and can deepen a friendship, hand you something, reveal a
  piece of the story, or make someone **call you on the radio later**. Talking itself counts toward your
  standing. A few **recurring faces** exist out there — the same person at more than one place, and they
  remember you everywhere.
- **Favours for friends.** A vendor who **knows** you will, in conversation, ask for a favour — the
  three-part *"A favour for a regular"* chain (crystals → a plant survey → titanium for a trusted friend).
  Saying yes puts the job straight into **Tab → Missions**; hand it in to the **same vendor, in person**.
  "Not today" is fine — they ask again on your next visit, and the ordinary smalltalk stays available.
- **The world calls you.** People you know reach out over the radio — "📻 Name (Place)" in chat: a
  quartermaster with bandits dug in **near their settlement** points you at the bounty (clearing a camp you
  were called about earns extra gratitude), a refilled mission board gets a mention, a trader landing near
  your base hails you, raider ships sighted in your star system draw a warning, a settlement running short
  on food asks for a delivery, and a friend may pass along a **story rumour**. The most recent calls are
  also listed at the top of **Tab → Missions**, so nothing drowns in chat. Calls need a **radio you carry**
  (comm = same world, system = same system, galaxy = anywhere), come at most every few minutes, and never
  repeat themselves. The **Settings → Comfort → NPC radio calls** option switches them to *missions only*
  or *off*.
- **Your base attracts life.** Trader ships prefer worlds with a founded base. Once your base holds a few
  machines (workbench, forge, …), a **settler moves in** — they know you from day one and count toward your
  people. **Every bed** you place inside your base brings **one more resident**, up to **five** people: a bed
  counts in the core zone, in a sealed room, inside your walls, or in any **closed room** (walls, a roof and a
  door) at the base — a bed out in the open field brings nobody. Take a bed away and its sleeper moves on.
  No visitor ever damages a block.
- **E to talk.** Standing next to someone shows *"Talk to … (E)"*.

### Daily routines & jobs
- **People keep the hours of the sun above them.** By day they are at work, in the evening they sit down on a
  **chair or bench** near home, and at night they walk to **their bed** and sleep in it (*"asleep"* on the
  nameplate, a soft *z z z* above them). Without a bed they rest where they live. Talk to a sleeper and you get
  a mumbled *"come back in the morning"*. This goes for your residents, for **villagers** (every house has a
  bed) and for **station crew** — a station keeps its own clock, and its deck lights **dim at station night**
  (the HUD clock shows it). On a modular station every crew member has a **cabin of its own** and the evening is
  spent in the **canteen or bar**.
- **They find their way.** People walk around walls, up single steps and **through doors**: sliding doors
  open for them like for you, and they swing a wooden or hinged door open and it **closes behind them** (a door
  you opened yourself is left as you left it). Only when there truly is no way — you walled someone in — and
  **nobody is watching** do they turn up at the other side.
- **Jobs come from what your base holds** — one job per resident, in this order:
  - **Trading post** (the *Trade Post* block): a resident becomes your **trader** — barter right at home.
  - **Mission board**: a resident becomes your **quartermaster** — board jobs at home (take them and hand
    them in at the board).
  - **Walls around the base** (or a sentry post): the **guard** walks the inside of the wall on the **night
    shift** and sleeps by day. Bandit scouts watching your base or a robber closing in get a warning to you
    over the radio and are **sent on their way** — the guard never fights; a robber who already attacks is the
    sentry's business.
  - **Crops, hydroponic trays or saplings**: the **gardener** (with a hoe) walks from bed to bed, **harvests**
    ripe crops into a **crate** at the base (they regrow as if you had picked them) and helps saplings grow
    faster. No crate — the gardener only tends.
  - **Workbench or forge**: the **craftsman** (with a hammer) puts two **plant fibres** into a base crate every
    five minutes — or, with a forge and **iron ore** in the crate, smelts **two ore into one ingot** (the same
    as the workbench would).
  - A crate that only takes certain items (a filter) or a full wooden box is respected: what does not fit
    stays in the field.
- A post, a crop or a workbench only counts **inside the base** (the same rule as for beds). A trading post
  or a mission board placed where nobody can staff it tells you why.

### Trade
- **Player ↔ player:** press **T** near a player (pad/touch: **Actions → trade**) to send a request; the
  other player accepts or declines. The trade window lists **your inventory** on the left — `−` / `+` on an
  item stages it — and shows **You give / You get from {partner}** on the right, each with a `READY` /
  `waiting…` badge. Both **Confirm**; the swap executes atomically once both sides are ready (changing an
  offer resets both confirmations, and your Confirm button turns green while you wait). **Esc** / pad **B**
  or **Cancel** aborts. If you know more than your partner you can also *teach knowledge* here (`−` / `+` /
  Max in the "You give" box).
- **Vendors / market:** press **E** next to a settlement or space-station **vendor** and pick **Trade** (or press **E**
  again) to open the **Market** (the gameplay menu's Crafting tab on the *Market* category). A vendor's themed goods
  only trade while that vendor stands right beside you. Barter recipes there trade your raw
  resources for goods. The market is also available **aboard your ship** (Tab → Crafting → Market), via the
  ship's trade console — so you can trade without a vendor too. Vendors have **themes**: miners sell iron,
  copper and lead ore for silicate, traders buy crystal, gold and silver, researchers buy refined uranium and
  sell data fragments and circuit boards, settlers trade food.

### Story: finding the thread
- After the tutorial the **objective chip** keeps a quiet story pointer ("a net fragment is on this
  world", "search ruins and wrecks", …) with the arc's progress as its counter. It respects the
  **VEGA hints** setting — turn hints off and the chip clears once the tutorial is done.
- Net fragments, personal memories and field records open in a **reader panel** and stay re-readable in
  the Story tab (Read buttons) — and they survive rejoining a server.
- Scanning **runes** at a monument now also reveals their inscription. Settlement folk who know you
  (trade with them!) may share what they know — one of them keeps a page of the settler legend.
- **Finishing the story is a moment now**: winning the finale plays a short **ending** — the resolution,
  the credits roll, and an epilogue that opens the door to what comes after. It's skippable (**Esc**),
  plays once on the next join for anyone who was offline at the time, and the Story tab keeps a
  **"Watch the ending again"** button once you've earned it.
- **After the ending the galaxy is calmer, not empty.** The Guardian's machines don't vanish for good: the
  hunter robots are gone and only a thinned-out remnant of scan-drones keeps roaming (half as many, spawning
  half as often), space stays hostile only where it is dangerous by nature — **pirate havens** and the
  opt-in *Frontier danger* tier — and raiders keep their havens, so the station **raider bounty** can still
  be earned. Worlds with planet enemies *Off* (the family presets) stay peaceful as before.

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
- **They do not extend the render distance** — the walkable world only exists as far as your view-distance setting
  streams it (beyond it the low-detail far view, §4), so magnification enlarges what is already there. Seeing *past*
  the haze is what thermal mode is for.
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
  **Fill everything** paints the whole canvas at once, **Alt+click** picks up a colour and **Undo** / **Redo**
  walk 32 steps back and forth (`Ctrl+Z` / `Ctrl+Y`); unpainted pixels show the design's paper-white
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

### Blueprint tool — share whole builds
- Craft a **Blueprint Tool** (`blueprint_tool`, cheap workshop recipe). It shares **whole builds** the way
  forms and paint designs already share: as a **`BBTS1-B-…` code** you can paste into chat, a forum post or
  a message.
- **Copy:** use the tool on a block to mark **corner A**, then on a second block for **corner B** (up to
  **16×16×16**). Name the build in the dialog — the code lands in your **clipboard** and credits you as the
  author.
- **Paste:** with a build code in the clipboard, use the tool on the ground where the build should stand
  and confirm. Blocks are **paid from your inventory** (free in Creative); cells that are occupied,
  protected (someone else's base) or unaffordable are skipped and tallied honestly — nothing is ever forced
  into another player's build. Doors, chests, beacons and other "living" blocks don't travel in a blueprint;
  shapes and dye do (custom forms fall back to plain cubes in a world that doesn't know them).

### Map markers & ping (planet map)
- On the **planet map** (key **M**), click to set a waypoint, then **"Save marker at waypoint"** turns it into a
  **named marker**: label (up to 24 characters, screened), one of **8 icons** (flag, home, ore, danger, water,
  star, heart, question) and one of **6 colours**. **8 markers per world**; each row in the map's marker list
  offers **Navigate** (sets the compass waypoint) and **Delete**.
- A marker saved as **"Visible to allies & crew"** appears on the map and compass of every ally / crew mate on
  the **same world** — **also while you are offline**, so the family meeting point does not vanish when you
  log off. The list refreshes on its own the moment an alliance forms or ends or a crew changes. Private
  markers stay yours alone.
- **Ping ("look here!"):** press **C** (rebindable; pad/touch: the Actions list) to drop a **pulsing pin** at
  your crosshair for ~30 seconds, visible to your allies + crew on the world — perfect for "the entrance is
  HERE". Rate-limited to one every few seconds; pings are never saved.

### Travel & the star map
- Open **Tab → Map**. The system list is grouped: **Current system** at the top (its reachable worlds, plus
  the **Launch into space / Leave space** button), then **Hyperspace** for the other systems. Selecting a
  system you've visited shows its worlds and an animated mini star map on the right.
- **Quick-travel** ("Travel" / "Hyperjump" on a world) is gated by the **Instant Travel** world option
  (Settings, world admin):
  - **Off (default):** you can only quick-travel to worlds you've already **landed on manually**. To reach a
    new world, **launch into space and fly there**, then land (pick a pad). A never-visited star system shows
    only as a single **"Hyperjump to this system"** entry — jumping there drops you into its flight space, and
    you fly to its worlds and land. Once you've been somewhere, quick-travel to it works from then on. Every
    other system keeps its **"Hyperjump to this system"** entry above its worlds, so a system you have jumped
    into but never landed in stays reachable — a locked world in another system offers the same jump in its
    detail pane (#1638).
  - **On:** quick-travel works for any world/system immediately, visited or not.
- Jumping to **another star system** always requires a fitted **`jump_generator`** module.
- A star system you have **never entered** shows as **"Unknown system"** — its name is part of what you
  discover. A fitted **`radar_array`** module decodes the beacon signals and reveals all system names.
- Your **first landing on a world** records it in the Codex under **Discoveries → Places** and pays
  **+5 knowledge** — exploring the galaxy is itself a way to learn. (Your starting world gets its Places
  entry too, but pays nothing — home isn't a discovery.)
- **Space stations** appear in the world list too (yours show their owner; others show *"Station of …"*).
  Selecting one offers **Board** — but only if you've **docked there at least once** before (just like landing
  gates worlds); a never-visited station shows *"visit it once to unlock"*. Boarding takes you straight inside.

### The frontier — why flying far pays
- Systems far from your home system carry a **"Frontier"** tag on the star map (shown even before you know
  their name — it's the reason to go). Out there worlds generate **richer rare-ore veins** (titanium, cobalt,
  uranium, platinum, tungsten, neodymium, diamond — everything the late tech tree wants), roll an **extra
  buried vault and monument**, and structure caches carry a bonus late-game find. Your starter ores (iron,
  copper, …) are the same everywhere — the frontier is the better place to *return to*, never the better
  place to begin.
- **Frontier danger** (Settings → world rules, world admin, off by default): when switched on, the machines
  out in the frontier hit like the toughest world settings — richness and risk scale together. On
  peaceful/family worlds there are no machines at all, so those stay "richer, never more dangerous". The
  `dangerous` preset ships with it on.
- **Visitors at the base** (Settings → world rules, world admin, off by default): bandit scouts look at a
  founded base from its edge while the owner is home — never inside, never destructive; see § Bases. Gated
  on the **Bandits** slider (no robbers, no scouts); the `dangerous` preset ships with it on.
- **Growing galaxy** (world creation → Universe size → **"Growing"**): the galaxy starts at the normal 12
  systems — but every time someone hyperjumps into one of the current **outermost** systems, deep-space
  telescopes report a **brand-new system beyond it**. The galaxy literally grows at your frontier, up to a
  generous cap ("the frontier is quiet"). Growth is permanent: new systems survive save/reload like any
  others. Worlds created with a fixed size never change.

### Day/night & weather
- **The world wraps east–west** — the X axis is a longitude, so walking continuously east (or west) brings
  you back to where you started, as if the planet were round. The seam is invisible (terrain, biomes, caves
  and structures line up exactly). North/south (latitude) does not wrap.
- **Day/night is by location** — because X is a longitude, a planet has a real day/night terminator: one
  player can be in daylight while another, far away, is in night, and one lap around the world is one day.
  The clock still advances. Animals and people keep the hours of the sun above **them**: on the far side of the
  planet the day-active animals are asleep while yours are grazing.
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

### Plant life
- **Caves grow plants** (worlds created from 2026-09 on, terrain generation 11): the dark **cave cap**, carpets
  of **glow moss** and **glow threads** hanging from the ceiling, and the mushrooms of the surface. Glow moss, glow
  threads and the prism bloom **light up the rock around them** in their own colour — a planted cave is never
  pitch black. Even a barren world or an asteroid may hide them: about every second one does.
- **The prism bloom** glows in every colour — each plant its own. It grows in caves and, rarely, in small
  clusters on the surface: a find worth marking on the map.
- **Cold worlds have plants too:** frost flowers, snow bushes, ice reeds and lichen grow on snow and ice (on a
  tundra, an ice world, a snow-capped mountain) where every other plant gives up.
- The glowing plants' colour (and every other wild plant's) is this world's own; harvested cave plants regrow on
  their rock like any other flora.

### Creatures
- Fauna spawn near players (habitat-gated), with temperaments; hostile creatures show visible attacks.
  Flora regrows when its host block survives. Nothing spawns inside a **sealed room** of a founded base
  (the same airtight rooms the base core supplies with air), no single species fills the whole area, and
  an animal you wall or floor in while it sleeps wakes up and steps clear.
- **Every animal moves the way its body says.** Walkers jump one-block ledges (like you) and are stopped by
  two; crawlers (legless, serpentine or many-legged things) haul over one block and never jump; giants
  step but never jump; fliers land to rest and to sleep and take off again when disturbed — a skittish bird
  flushes when you get close; gas sacs and medusae just hover. Everything on the ground falls if you dig
  its floor away. The scan readout names the type ("Walks and jumps", "Crawls", "Flies, perches to rest"…).
- **Individuals of a kind vary in size.** Within any one species/type, each plant, tree or animal gets its
  own size (most near the normal size, the occasional runt or giant) — a wood is a mix of saplings and tall
  trees with varying crown widths, a herd has small and large animals. The variation is cosmetic (a creature's
  size doesn't change its health, damage or loot).
- **Big herds and begging animals** (worlds created from terrain generation 12 on): some peaceful land grazers
  live in **herds of six to twelve**, and about a third of the passive herd species have a **nose for food**. Hold
  anything edible in your hand — berries, grain, a ration, cooked meat; nothing poisonous, and the taming baits
  don't count — and a herd within about ten blocks comes **hopping over**, circles you at arm's length and calls
  excitedly; put the food away (or walk off) and they trot away, and after half a minute of getting nothing they
  lose interest for a while. Press **Feed** (**Q**; gamepad: the L3 actions list; touch: the **FEED** button) to
  throw them **one piece**: it lands a few blocks ahead, the herd rushes it, squabbles over it for a few seconds,
  eats it and leaves. You cannot pick a thrown piece up again for ten seconds (after that it is an ordinary drop
  packet). The scan readout marks such a species with **"Comes running for food"**; VEGA explains the trick the
  first time a herd begs from you. A sleeping herd ignores food, a startled one runs like any other, and skittish
  species never beg. Big herds are cheap for the world's animal budget (three herd animals count as one), so a herd
  never crowds out the other species.
- **Arachnids** are eight-legged animals as big as a speeder that some worlds roll into their fauna (about every
  third or fourth world has one species). Their heads are boxes or pyramids of several kinds, their eyes come in
  clusters of 2, 4, 6 or 8, and their temper is rolled like any other animal's: many graze or run away, but a
  hunting or territorial arachnid **lies in wait** — it sits perfectly still like a rock until you come within
  about six blocks, then rushes. The **scanner** tells you which kind you are looking at ("Eight-legged", "Lies
  in wait"), and VEGA points out the first one you get near. You bump into its body, your shots land where you
  aim on it, and it can be tamed like any other animal.

### Giants (the colossus and the sandworm)
- **A colossus** is a 40–60 block tall four-legged giant. It only lives on **very flat, very light worlds** (the lighter
  moons of plains, downs and dune types) and even there only on one world in three. Every colossus is different:
  peaceful ones migrate and graze on treetops, shy ones walk away, territorial ones fight back when hit, aggressive
  ones stalk you. Before it stomps, a **dark ring** marks the spot — get out of it, or get under a roof or into a
  cave: a stomp never reaches inside. You bump into its legs; you can fight it (it takes a long time — bring friends).
- **A sandworm** lives only on **sand-sea worlds** — a planet class (new worlds) where half the surface is a sea of deep
  sand between rock islands, mountains, canyons, lava and water. It hears you through the sand: walking, mining,
  drilling, blasting, a hard landing, a speeder. **Sneaking** (crouching) barely makes a sound, and **rock, a landing pad
  or a floor you built are silent** — only the sand of the sand sea carries a step (VEGA tells you so the first time you
  land on a sand-sea world; the worm is out there from the start, hidden under the dunes until it hears you). When it
  comes, the sand ripples and rumbles and VEGA warns you: get onto rock. It breaches in an arc, or rears up as a tower and strikes the spot it heard.
  You can only hit it while it is above the sand.
- **Thumper** (workshop recipe): place it on sand-sea ground and it pounds the ground every two seconds for a minute
  and a half — the worm comes for it and swallows it. Use it to lure the worm away from you or to watch it. On rock
  nothing hears it; mine it back to switch it off.
- A defeated giant is gone for a few in-game days, then another one comes. Neither ever changes a block.

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
  (rename, **feed**, release — each with a **bond bar**). Companions are peaceful and can't be hurt.
- **Feed & bond:** every companion has a **bond** (0–100; a fresh tame starts around 40–60). **Feed** it from the
  Companions tab — any bait (forage, meat or nectar) will do, one meal a minute, **+5 bond** each. Feeding
  happens **in person**: the animal has to be on your world within about six blocks of you, otherwise the
  Feed button is greyed out — and the
  number buys three things: at **50** it **fetches from a wider circle** (half again as far), at **70**
  **robbers keep away** from you while it is at your side, and at **90** it has **a nose for the place** and
  will occasionally point out a landmark (it knows the big things — the crashed wreck — not the secrets the
  villagers keep for friends). A pet you do not feed loses **one point a day**, but **never below 40**: a
  holiday costs the perks, never the friendship.
- **What a companion does for you:** it **fetches** — any dropped packet it can reach (about three times your own
  pickup reach) pours straight into *your* inventory while you are nearby; it **growls** when a hostile machine,
  bandit or aggressive animal has line of sight to it (a toast plus an amber **"!"** nameplate for a few seconds,
  at most once every 30 s) and a robber walking up to you **stops at the animal** for a few seconds; once its
  **bond is high** (≥ 70) a companion at your side keeps robbers from picking you at all — wander off from it and
  hold-ups are possible again; and every **ten minutes** a present companion **drops a little of what its kind
  gives** (the species' drop item) at its feet — auto-picked, and a **penned** pet stockpiles it for your next visit.
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
- It is a **land vehicle**: it unfolds only on dry ground (deploying towards water is refused) and **stops at the
  shoreline** — water ahead caps the throttle, and if it does get pushed into water it floats and can only back out.
  A speeder wedged against a lip hops over it by itself after a moment; a hull that ends up inside a block lifts out.
- **Dismounting** puts you on the ground **beside** the hull, and a parked speeder or boat is **solid** — you can
  stand on it, not walk through it.
- **Boarding:** walk up to your parked speeder or boat — the centre prompt reads **Board (E) · Pack up (X)** (pad and
  touch: the USE button boards, the ACT list carries the pack-up).
- **Lost it?** Walk within 5 m to pack it up (the HUD names the vehicle and its distance while it is within 30 m).
  If it is somewhere you cannot get to — the far shore, a ravine — go to your landed ship's **cockpit or console**
  and press **X**: the ship **recalls** every speeder and boat you left out on this world **straight into your
  inventory**. Only when no slot is free is it parked beside the ship instead — on the cell nearest you, with a
  "look here" marker on the spot and its distance in the message. Only from the landed ship, never while someone
  drives it, never from another world. Dying while driving releases the seat, so you can board or pack it up again
  afterwards.

#### Boat (water vehicle)
- The **boat** (`boat`) is the water kind of the same system — an early-game workshop craft with **no
  blueprint**: wood_log ×8, iron_plate ×2, cable ×1. On an **ocean** world you start with one.
- Stand at the shore (or swim) and **use** it from the hotbar: it needs **open water in front of you** (2–5
  blocks ahead, a little to either side) and is set onto the **waterline**; on dry land you get "you need open
  water" and keep the item. **E** boards it from the bank or while swimming, **F** gets off, **X** packs it up.
- While driving: **W/A/S/D** as in the speeder but slower (9 m/s, **Shift** 13), lazier steering and the hull
  **drifts** through turns; there is **no hop** and **no energy** — the boat never runs dry and the HUD shows no
  gauge. It keeps your **head above water** (the speeder would follow the seabed and drain your oxygen).
- Leave the water and the boat **runs aground**: it settles onto the sand, forward speed bleeds off, you can
  reverse off or step out — no damage. Keep driving over land anyway and after a few seconds the server sets you
  back onto the last spot that floated. Shallow water lets you nose onto a beach to get out.
- Hull damage, destruction and persistence match the speeder; the boat cannot be refuelled (nothing to fill).

### Craftable block shapes
- Any held **building material** can be re-formed into a non-cube **shape** — **slab, pyramid, dome (half-sphere),
  sphere, ramp, stairs, cone, cylinder, panel** (thin plate), **post** (slim pillar), **beam** (horizontal bar),
  **low ramp** (gentle half-height wedge), **quarter cube** (small corner block), plus the **furniture forms**:
  **table, chair, bench, fence** (posts + rails that connect across cells; benches join up too), **sheet** (an
  ultra-thin 1/16 plate for veneers) and **pot** (a small planter) — so a *wooden* table and an *iron* table are the same form on
  different materials. Every shape still places, mines and
  stacks like a block (form and dye colour combine freely). Shaped forms are **player-craft only**: world-gen,
  settlements, stations and ships stay plain cubes.
- **Sitting:** press **E** on any **chair**- or **bench**-shaped cell to sit down — the camera settles to seat height
  and other players see you sitting. Stand up with **E**, jump, crouch or any movement key.
- **Beds are two cells long:** placing a bed writes the head end where you aim and the foot end in the cell you are
  facing (the preview shows both); the foot cell must be free. Mining either half takes the whole bed back, and **E**
  on either half sets your home spawn. Beds placed before this change stay one cell.
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
- **Forms over several blocks** — a wardrobe two blocks high, a table two blocks long — are designed in the
  **Form Editor of the main menu** (§6), not with the small editor here (it tells you so when you open one). In
  the world they behave like this: the form is **one item**, made from **one block of material per block it
  spans** and giving them back when you turn it into cubes again; the placement ghost shows **every block** it
  will take and the game refuses the place when one of them is not free; it can be **turned but not tipped
  over**; and it comes down **in one piece**, whichever of its blocks is removed and whatever removes it. Not
  aboard ships and stations, where a self-made form becomes a plain cube as before.

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
  everywhere on the face you are on); an armed tool colours its own button and label, and the line under the
  buttons says what your next click will do. **Fill everything** needs no click on the canvas at all: it
  paints the whole face you are on in the current colour, whatever was there. **Pick colour** takes the colour
  under the cursor for one click — **Alt+click** or the **middle mouse button** do the same at any time.
- **Undo / Redo** — **32 steps**, forwards and backwards, with `Ctrl+Z` / `Ctrl+Shift+Z` / `Ctrl+Y` and **RB**
  on the pad. The history covers the whole screen: it survives switching tabs, brings the tab back with the
  step, and it takes back a **base colour** you changed by accident as readily as a brush stroke (one sweep of
  the colour wheel is one step). Both buttons grey out when there is nothing left to take back or put back.
- **Colours** — 32 paint colours plus the eraser, arranged so each hue has a lighter and a darker partner for
  shading. The colour wheel picks by hue (outward = more saturated) with a **brightness column** beside it, and
  snaps to the closest palette entry. Faces drawn at the old 16×16 size still work — they are scaled up
  automatically the first time you open them.
- **The body parts** are painted **one face at a time at full size**. The part's faces are stacked as small
  labelled **live tiles** beside the canvas (Front / Right / Back / Left; arms and legs get one tile column per
  limb, headed Left/Right) — click a tile to paint that face, and the tiles update while you draw so you always
  see the whole part. **Clear** wipes only the face you are on, and a fill never runs over onto another face.
  The helmet's front stays open, so your face always shows.
- **Outfits** — the column on the right keeps up to **eight named looks** (the four body colours, your pixel
  face and all four paintings). Type a name and press **Save outfit** to store what you are wearing (a new
  outfit, or an update of the one that already has that name); **click a row to put that outfit on** — every
  part and every colour at once; **Rename selected** renames the highlighted one, ✕ deletes it. Deleting the
  outfit you are wearing does not undress you: what you wear is a copy. The same shelf is in the main-menu
  **Avatar Designer**, over the same eight saved looks — there, loading one only puts it on the figure in the
  designer and its **Apply** is still what makes it yours.
- Everything here appears on **your figure** and **on your avatar for every other player** — it is
  server-persistent, so your look follows you to any world. When you put a whole outfit on in the game your own
  figure changes at once; the other players' screens catch up over the next few seconds, one part at a time.

### Death & respawn
- At 0 health you respawn at the ship's **Medbay heal-tank** (vitals restored); a salvage capsule may drop
  at the death site to recover cargo, per the active rules.

---

## 6. Editors (main menu → Editors)

All editors are menu tools that **export a JSON bundle**; a developer folds it into the game data with the
matching Python merge tool (review the diff, translate locale placeholders, commit). Shared build-room
controls (Ship/Station/Town/Material 3D editors): **hold Right-mouse** to look, **WASD** to fly, **Q/E**
(or Space/Ctrl) up/down, **Mouse wheel** zoom (dolly along your view), **Shift** faster, **F** frame the
build (or the floor centre when nothing is placed yet), **Left-click** place, **Middle-click** remove,
**Esc** to exit. A translucent ghost shows where a click lands **and what it leaves there**: the block in the
form it will take (a bed as head + foot), the **door** the game will hang (turned by the wall beside the cell,
as wide as the gap), or a marker's silhouette (a figure on an NPC post, a board, a chest, a terminal, a plate on
the floor for an area marker, the ship hatch's frame, a ship station's decor). Green = fine; red = occupied,
out of bounds, or a door with no wall beside it / no two free cells above it — the status line says why.
Placed doors, silhouettes and decor are drawn in the build and re-fit when a neighbouring cell changes. The
floor grid marks every cell with a brighter line every 8 cells. The palette on the left has a search box
(typing there never moves the camera) and a scrollbar.

**Form brush.** The block brush's *Form* row opens the same form grid as the in-game Shape action. *Automatic*
(the default) gives a block its own form, exactly as the game stamps it when you place it in a world: a bed is
placed as head **and** foot (removing one half removes both; refused where the foot would not fit), a campfire
as a slab, a rug as a sheet, a flower pot as a pot, a ladder as a plate against the wall you clicked (a pole
with no wall), stairs and the stretcher in their forms. *Cube* is an explicit choice. **Doors** are placed as
door **markers** (Station / Town editor) or door **elements** (Ship editor): click the doorway's floor cell —
the game finds the wall and the width. Door *blocks* are no longer in the block palette (they stamped as a
solid wall); only the Station editor keeps them as **hull airlock** blocks, the airtight block in a station's
outer hull.

**The three editors that need no developer** — what you make in them is yours at once, on any install:

- **Texture Editor.** Every texture of the game on a 64×64 pixel canvas: block tiles, the picture tiles of
  furniture (bed, campfire — with the areas each face uses drawn in), plants, torches, creature hides, avatar
  fabrics, the leaves and frames of **doors**, the parts of **factory machines** and, in **icon mode**, the item
  icons. A block texture with several frames **plays in the world** — a campfire can really flicker. Brush, eraser, fill, pipette, line, rectangle, brush size,
  mirror symmetry, flip / shift / brightness, undo and redo, up to eight **animation frames**, a 3-D preview on
  the real shape and a 3×3 view that shows how a tile repeats. **Use for me** puts the texture into *your*
  texture pack (a folder next to your settings; PNG files you can also edit in any paint program — *Open
  folder* / *Reload folder*); **Remove mine** brings the shipped one back; **hold "Before"** to compare;
  **Copy / Paste code** shares a texture as text. A block tile can never get a hole — the editor fills it, so
  nobody can paint themselves an X-ray view. The editor also opens **inside a world** (Settings tab →
  *Textures*), where a **world admin** additionally gets **Publish for everyone** / **Take back**.
  - **World textures:** what an admin publishes is shown to everyone in that world and **wins over your own
    pack** there. Two switches in the Settings tab are yours alone: *Use my textures* and *Show this world's
    textures* — the second one is the way out if you do not like what a world shows. The world option *World
    textures: Admins / Off* decides whether admins may publish at all. `/reporttexture <key>` flags a world
    texture for the operator; admins remove them with `/texturewipe <key | player | all>`.
  - **Submit to the developers:** offers your texture for the game itself. You give it a name and a
    **nickname** (please not your real name — or choose *No name*), may add a note, and tick three boxes: you
    painted it yourself, the developers may use, change and distribute it with the game, and you are at least 16
    or your parents agreed. Sent are the texture, those texts, the game version and an anonymous id for the
    answer — **no e-mail, no location**. The developers look at every submission by hand and answer you **in the
    game**; a submission that is not adopted is deleted after twelve months at the latest.
- **Form Editor.** Design the forms your shaping tool places — with a large layer canvas, undo, mirror, shift,
  a turntable preview in **any material and dye**, and a library that lists every form (duplicate, rename by
  saving under a new name, delete). The left column holds *My forms* above and the **material list** below —
  every material with its picture, with a **search field** on top; the material only dresses the preview (in
  the world a form takes the material you make it from), and the editor remembers it. The *Size in blocks* steppers make a form span **several blocks** (up to
  3 × 3 × 3, at most eight); the canvas then shows the whole form from above with neighbouring blocks shaded
  differently, and the budget line counts the most detailed block. It writes the same *My forms* library the
  shaping tool reads, so everything appears in the world as before.
- **My Tools.** Give your drill, pistol, blade or scanner **a look of your own**: a small coloured voxel model
  from fifteen colours, each of which can glow. The look belongs to **you**, not to the tool — everyone sees
  it in your hand from the next time you enter a world, it changes nothing about what the tool does, and a
  tool you hand over shows the new owner's look. Up to sixteen tools; *Back to the standard look* undoes it.

All three work with a **gamepad**: the stick moves a cell cursor, **(A)** paints or adds, **(X)** erases,
**(Y)** picks a colour, the **d-pad up / down** walks layers or frames, **RB** undoes, **Start** switches
between the canvas and the buttons, **(B)** steps back.

**LOAD — start from an existing design.** The LOAD button in the Ship, Station and Town editors opens a
sectioned list: **Built-in ships** (every shipped ship type that has a layout — scout, corvette, courier,
hauler, thunderbolt, deathblock, hammerhead; the starter is a code-built box and is not listed) or
**Built-in templates** (the shipped stations / settlements), **Your templates** (what you already saved to the
user-content folder) and **Your designs** (your export bundles). Loading fills the build room *and* the side
panel (name, stats, craft cost, tier, pack …) and frames the camera on it; if something is already placed the
editor asks before replacing it. A built-in template is loaded as a **copy** (key `<name>_2`) — the original
stays in the game, your copy is added next to it. Cells the palette does not know are counted and named in the
status line instead of vanishing.

**Ship editor — interior frame.** A translucent cyan box marks the **cabin**: the volume the game walks,
floors and roofs. Set its width / length / height with the *Interior* steppers; wings, engines, nav lights
and antennae may sit anywhere outside the box (that is how the shipped ships are built).

**Station / Town editor — procedural starting point.** Pick a size tier (stations up to *Colossal*; the line
under the stepper tells you how big the game's own generator builds that tier), set a **Seed** (or **Reroll**)
and press **Generate**: the same generator the world uses builds a station / settlement of that tier into the
room, markers included, ready to edit and save as a template. Villages use the **Surface block** field as their
material (e.g. `grass`, `sand`, `stone`).

**Town editor — whole settlement or building module (Use as).** Every settlement template is either a **whole
structure** (the default: world-gen may drop the complete settlement onto a planet exactly as you built it) or a
**building module**: set **Use as** to *House*, *Market*, *Mission board* or *Greenhouse* and the procedural
settlement builder uses your building for one plot of that kind — in hamlets and villages when the tier is
*Hamlet* / *Village*, in towns and cities when it is *Town* / *City*. The size line under the tier then shows the
plot envelope it must fit (6 × 6 blocks, up to the tier's storey height). The *City …* roles are 32 × 32
districts of the G.D.S. city (tier *Metropolis*). Place the markers the role needs (an inhabitant, a vendor, a
mission board, a door); a missing vendor / board / inhabitant is added automatically over the module's centre.
Put a **Room** marker on the floor of any room you want furnished: the game floods that floor and places a bed,
a table and chair, crates, a plant and a light in the settlement's style, keeping the door lane and the
residents' spots clear — procedural buildings are furnished the same way. How often modules appear follows the
world option *Settlement templates* (*Off* disables whole templates and modules alike); a world keeps the
layout it was created with. The option is the **share of hand-built settlements** in a new world: *Very rare* 5 %,
*Rare* 15 %, *Normal* 40 %, *Frequent* 75 % — every other settlement is put together from a kit (*Station templates*
works the same way for stations).

**Station / Town editor — kit modules, ports and the seal.** Set **Use as** to *Kit module* to build a piece of a
modular structure. Type the **Kit** it belongs to (or pick one with **Kits…**) and step the **Function**: a
station module is a *Hub*, *Corridor*, *Crew cabins*, *Canteen*, *Bar*, *Market hall*, *Mission office*,
*Medbay*, *Hydroponics*, *Storage*, *Hangar* or plain *Room*; a town module takes the plot / district roles
above. Mark where another module may dock with the **port brushes** at the top of the palette — *Port: door*,
*Port: wide* and *Port: ladder* (floor / ceiling, the composer drops a ladder into the shaft). Left-click paints
the port onto an existing wall block, middle-click with a port brush clears it again (the block stays); the
**Port door** stepper chooses what fills the opened joint (*Slide*, *Energy*, *Hinge* or *Open*). A port is a
rectangle of wall blocks on one outer face with air behind it; two ports dock when their tag and rectangle match
on opposite faces. Put a **Cabin** marker where each resident sleeps (one cabin per crew member) and **Lounge**
markers on canteen / bar seats. **Check seal** flood-fills from outside: every cell where open space would reach
the inside turns red — a station module with a leak or a broken port is never saved. **Assemble** runs the real
composer with the kit named in the field and the current **Seed** and loads the result as a whole structure to
walk through (your own templates are included), so you can see how the pieces fit before you start a new world.

**Town editor — planet materials, style, taverns and workshops.** The *Planet materials* section of the palette holds
*Wall*, *Accent*, *Roof*, *Floor* and *Path*: build with them and the game puts the planet's own material there when the
module is stamped — a village wall becomes sand, ice or grass, a town wall iron; the accent is crystal for aliens. With
*Use as* on *Kit module*, the **Built for** stepper marks a module for *Humans* or *Aliens*; a kit only puts the
settlement's own kind into its plots. *Tavern* and *Workshop* are functions of their own (they take a house plot): put
the **Innkeeper** or **Craftsman** marker into the room and it is furnished as a tavern (counter, tables and chairs — the
residents' evening seats) or a workshop (workbench, forge, crates). Every building people live in needs a room with a
**Room** marker and space for a bed. Kit modules of the shipped kits are 8 × 8 blocks; the size line shows the storey
height they may use.

**Kits…** (on the *Use as* row, in both modes) opens the kit panel: every shipped kit of the editor's kind plus your own.
The **…** button beside a module row opens the module picker — every module of the kit's kind with its function, tier,
size and style, filterable. A kit has a key, name, kind
(*station*, *settlement* or *city*), size tier, weight and planet types, how many modules it uses (min / max)
and one row per module: minimum and maximum copies, *Required*, draw weight and whether the composer may rotate
it. Station kits also name a start module and a maximum extent, and how many **Solar wings**, **Antennas** and
**Domes** the station gets on the outside (0 = none; **Assemble** shows them, blue solar cells included); settlement kits set the plot grid (columns,
rows, plot stride, building size, storeys, *Modules only*); city kits set the district grid, district size,
street width, height and the district map. **Save kit** writes it to the user-content folder (live in the next
new world, drawn from the same weighted table as the complete templates) and an export bundle for
`tools/merge_structure.py`. Saved worlds keep the exact composition they were created with — reloading never
rebuilds a station, village or city differently.

**Textures.** Placed blocks show their real block textures (dye and glow tint them like in-game); station tiles,
ship elements and interaction markers are drawn as plain colour swatches so they stand out.

| Editor | Designs | Export → merge tool |
|---|---|---|
| **Ship Editor** | Custom ship types (hull, viewports, lights, engine, hatch, station tiles) | `ship.json` + `layout.json` → `tools/merge_ship.py` |
| **Station Editor** | Space stations or station kit modules (hull/glass/light + hangar/vendor/mission/heal/quarters/console/cabin/lounge markers, docking ports) and station kits | `structure.json` + `layout.json` (+ `kit.json`) → `tools/merge_structure.py` |
| **Town Editor** | Settlements/villages or their modules (walls, windows, ladders/stairs, lamps + vendor/mission/NPC markers) and settlement / city kits | `structure.json` + `layout.json` (+ `kit.json`) → `tools/merge_structure.py` |
| **Avatar Editor** | Player skin (per-part colours + gear preview) and up to eight saved **outfits** | `skin.json` → `tools/merge_avatar.py` (Apply also saves locally) |
| **Item & Recipe Editor** | Items (stats, tool/weapon properties, worn + eaten effects) + recipes (station, inputs, market vendor theme) + optional blueprint gating | `content.json` → `tools/merge_recipe.py` |
| **Material Editor** | Block materials: paint a 64×64 tile, set mining (hardness/tool/drops), palette section, dyeable/shapeable, look (gloss/metal/glow/colour), world spawn (frequency/depth/world-type) | `material.json` + `texture.bytes` → `tools/merge_material.py` |

**Avatar Editor outfits:** the **Outfits** panel beside the controls keeps up to eight named looks
(colours + pixel face + body paint). Type a name in **Skin name** and press **Save outfit** to store the
look shown on the figure (a new outfit, or an update of the one that already has that name); click an
outfit to load it back onto the figure, **Rename selected** renames the highlighted one to the name field,
✕ deletes it. Only **Apply** changes the avatar you wear in the game — loading or deleting an outfit never
does — so the same look stays on your avatar until you apply another one. The same eight outfits are also
reachable **in the game**, in the Appearance screen's outfit column, where clicking one puts it on straight
away (see §Appearance).

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
  brings the recent lines back). **Long answers scroll:** while the chat box is open, the **mouse wheel** or
  **PageUp/PageDown** pages back through the recent lines — a dim ▲/▼ row says when more is out of view. The
  answer to a command you typed keeps the whole chat column for a moment, even while VEGA is talking. Prefer it always visible — or never? **Settings → Comfort → Chat
  display** offers *Fade out / Always on / Off*, and **J** hides/shows it on the spot for the session.
- **Kept friendly by default.** The server screens every line before it is relayed: swear words are
  replaced by `***` (you are told once per session), slurs and hate terms are not sent at all (you are
  told), and phone numbers, e-mail addresses and links are masked — on *Safe* worlds (the family
  presets) a line carrying them is not sent. The filter understands l33t, s-p-a-c-e-d letters, repeated
  letters and look-alike foreign characters, but matches whole words, so everyday German words like
  "Assistent" or "Klasse" are never touched. It never acts silently. The world admin picks *Open /
  Filtered / Safe* per world (`--chat-mode`); the server operator can switch screening off for a private
  family LAN or force Safe everywhere (`BBS_CHAT_FILTER`, see SELF_HOSTING.md §12). Slash commands are
  never filtered.
- **A pause if the chat gets out of hand.** Sending **more than 6 lines within 10 seconds**, or tripping
  the filter **more than 3 times within 5 minutes**, pauses the chat for you for **10 minutes**. You are
  told right away and how long it lasts — nothing else changes, you can keep playing normally, and it
  ends by itself. Nothing is written down: reconnecting or a server restart clears it. The world's
  operator gets a note so a grown-up can have a look.

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

### Player feedback (**F1**; **F2** in the browser) — and the developers' answers
- Press **F1** (F2 in browser builds) during play to send a bug report or a wish: a title, a description
  and an optional e-mail. A screenshot and a small technical snapshot (player name, version, position,
  stats) travel with it to the developers' inbox. No connection? The report is queued and sent on a
  later start.
- **The developers can answer you in the game.** When they reply to one of your reports, a line shows
  on the HUD and a window opens with the conversation — what you reported, their answer, and
  "Fixed in version …" when the fix has shipped. **OK** (or Esc) closes it; you see each answer once.
- When the developers **ask you a question**, the same window has an answer box: type your answer and
  press **Send answer** — it is stored with your report (up to three answers per report). Never put
  personal data in there.
- Answers are only fetched while the game remembers a report you sent in the last 90 days; a game that
  never sent feedback contacts nothing. In the browser (play.bbts.de or the glitch.fun arcade) this
  works the same way; the arcade ties answers to your Glitch install, so they follow you across releases.

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

### `/report` — report a player (everywhere, no account needed)
- **Syntax:** `/report Player [what happened]`
- Files a player report, exactly like the report button in the ship UI's Alliance tab — one command, no
  menu digging. Either way the report automatically attaches the reported player's **last 10 chat lines**
  as evidence and the **world** it happened on, so the people who read it know what was said and where.
- **With an account** (an official hosted world joined via the Official Worlds menu) it goes to the worlds
  portal. **Without one** — an arcade guest on glitch.fun, a LAN game, someone else's server — the
  **server** files it instead and answers in chat. Nothing is needed for either: no radio, no equipment,
  no account. Reporting is exactly the moment when "first go and craft something" would be the wrong
  answer.
- The reported player is **never told** who reported them, and nobody else sees it either.
- Reports are **reviewed by humans** — nobody is punished automatically. A few in a row is plenty; after
  that the command asks you to give the team a moment.
- `/report` is not the same as `/mute Player`: mute hides someone **for you alone** and tells nobody,
  while a report asks a human to look.

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
| `/tpp Player` | Teleport to a player on the body you are on — you land **beside** them, never inside them (#1055) |
| `/settime day\|night\|…` | Set the world time of day |
| `/setweather clear\|storm\|…` | Set the world weather |
| `/giant colossus\|sandworm` | Summon this world's colossus or sandworm near you (a sandworm needs a sand sea) — for testing |
| `/arachnid` | Summon this world's arachnid near you (rolls one into the world's fauna first if it has none) — for testing |
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
| `city` | A city or town — the G.D.S. metropolis too (`stadt` also works) |
| `village` / `ruin` | An inhabited village or hamlet / a ruined settlement (`settlement`, `dorf`, `siedlung` also work) |
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
| `/basewalls` | Why animals do (not) spawn in a yard: the nearest base core, the enclosure fill at your feet level (cells reached, budget, fail-open), whether your own cell reads enclosed / sealed / open, and the rules (2-high walls, hinge doors leak, unloaded chunks read as open; walking Guardian robots step up two blocks and are stopped by a wall of three or more, hovering scan-drones by four or more; fliers are not) |
| `/creatures` | Creature footing within 48 blocks: per animal its species and motion class, feet height, the real ground of its column, the delta between them, the generator's noise surface and a verdict (ok / floating / BURIED) — the readout for "animals standing in the ground" reports |
| `/silence Player [Minutes]` | Pauses that player's chat for a while (10 minutes by default, a day at most). They are told how long, and can keep playing — see below |
| `/unsilence Player` | Ends the pause right away |
| `/kick Player` | Ends that player's session right now. **Momentary** — they can come back; to keep someone out for good, block them in *Manage world → Manage players* (below) |
| `/paintwipe Player` (or `#designId`) | Removes that player's painted block designs **everywhere at once** (or a single design by id, taken from the report log). Wiped designs stay wiped across restarts |
| `/mode Player survival\|creative\|world` | Per-player game mode — see *Per-player mode* below |
| `/gamemode explorer\|creative\|sandbox` | The whole world's mode — see *World mode* below. Alone it names the current mode |

#### `/silence` — a pause instead of a kick

Between "say something" and "end their session" there used to be nothing. `/silence Player 5` gives the
channel five minutes of quiet: the player is **told how long** it lasts, keeps playing normally, and their
chat comes back on its own — you do not have to remember to undo it. `/unsilence Player` ends it early.

It is the same pause the server applies by itself when someone floods the channel, so a player only ever
sees one kind of "chat is paused for you" message, whether a person or the anti-spam decided it.

**Note the name.** It is deliberately *not* `/mute`: **`/mute Player`** is every player's own command for
hiding someone from their own screen (§Chat), it never reaches the server and nobody is told. One word
must not mean two different things depending on who types it.

#### Per-player mode — one world, mixed Survival and Creative

A shared world normally has ONE mode. As **world admin** you can give a single player their own: with
`/mode Player creative` (or the **Player modes** rows on the Settings tab, listed there only for admins)
that player plays **Creative** — free crafting and research, creative flight (double-tap **Space**), no
oxygen/hunger/temperature, and machines, bandits and aggressive creatures ignore them — while everyone
else keeps playing the world as it is. The classic family setup: the kid builds carefree in the shared
survival world, the parent keeps the survival challenge. It also works the other way round
(`/mode Player survival` in a creative world), `/mode Player world` puts them back on the world's mode,
and the setting **persists** — it survives rejoins and restarts until an admin changes it. Like the other
tools in this section it is moderation, not a cheat: it works even when admin cheats are off. The world's
own difficulty sliders (oxygen/hunger rates, hazards) still apply to a survival-playing player, and world
options like PvP or structure damage are never per-player.

#### World mode — Explorer, Creative or Sandbox on a running world

The three modes of the new-world screen can be switched later: **`/gamemode sandbox`** (also `/mode sandbox`,
`/modus`, and the German words `entdecker`, `kreativ`, `sandkasten`). **Explorer** is the normal survival game;
**Creative** keeps survival but everybody flies and gets every blueprint, every ship and the creative kit;
**Sandbox** adds free crafting, no oxygen or hunger and no planet enemies — and the inventory's **All items** page.
The change is saved with the world and reaches everyone online at once. Going back to Explorer takes nothing away:
unlocked blueprints, ships and kit items stay. World admins only; it works with cheats off.

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
