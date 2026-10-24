# Blocks Beyond the Stars — User Manual

The central reference for **how to play**: controls, game mechanics, the in-game editors, and the
chat/admin commands. This is a living document.

> **Maintainers:** keep this file current. Whenever a control changes, a feature is added, or a command
> is introduced, update the relevant section here in the same change. This manual is the single source of
> truth for player-facing operation. (Written in English per project doc policy; in-game text itself is
> localized — English, German, French and Spanish are complete, further community translations such as
> Italian are in progress.)

Last updated: 2026-10-04.

> **Parents and teachers:** what the game contains, who your child can meet online and which switches you
> hold are summarised on the [parents page](PARENTS.md) ([Deutsch](PARENTS.de.md)).

---

## 1. Starting the game

- Launch the client. **Windows:** `BlocksBeyondTheStars.Launcher.exe` (shows a loading splash then starts the game)
  or `BlocksBeyondTheStars.exe` directly. **Linux:** `./BlocksBeyondTheStars.Launcher.Console` (prints "Loading..."
  to the terminal then starts the game) or `./BlocksBeyondTheStars.x86_64` directly.
  From the main menu: **Singleplayer** → pick an existing save (newest first; a long list scrolls with the mouse
  wheel, the scrollbar or the gamepad; **✕** deletes a save after a confirmation) or
  start a **New world**: type a name and press **Create / Play**, or press **Random world** for a surprise
  world — under the name you typed, or as `world_<number>` when the name box is empty, still says
  `new_world` or holds the name of a world you already have. **Host Game** → host a world for friends, **Join Server**, or
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
  password. Worlds are private by default and only visible to their owner until listed. On an official
  world **the owner is its world admin** — a friend who happens to arrive first is a normal player. The shared
  **glitch.fun arcade worlds** have no player admins at all; the game's operators look after them. A world
  marked **"needs a server update"** (amber) was saved by a newer version of the game than the official
  servers run — it opens again once they are updated.
- **Join Server:** enter your **player name**, the server address, port and (if the server has one) the
  password. **Name verification:** the first join under a name claims it for your installation — later
  joins under that name from other machines are rejected ("name belongs to another player"), and a name
  that is currently online can't join twice. So pick your name once and keep it; it also keys your
  inventory/progress on each server.
- **World options** ("Weltoptionen") at world creation: pick a preset (**Friedlich / Standard /
  Feindselig**) or tune sliders — life & threats (creatures, planet enemies, enemy ships, UFOs),
  survival (oxygen, hunger, hazards, death penalty), generated world (flora, ore, settlements,
  wrecks, vaults, stations, wormholes, exotic worlds, universe size), plus an **advanced page** with a frequency
  slider per planet type. The world *owns* its rules from then on; the world admin can live-edit the
  creature/enemy activity later in-game (Character tab → "World rules" / "Weltregeln").
- **Stories** ("Geschichten") at world creation: the row **"Stories: VEGA · Tutorial Auto ›"** at the bottom of the
  world options opens its own page with three choices:
  - **Main story** — the story this world tells: **The VEGA Protocol** (default; a line under the list says what it is
    about) or **None (sandbox)** for free exploring and building. More stories appear in this list once they are installed.
  - **Story pace** — *Slow / Normal / Fast*: how quickly the story's chapters unfold as you play.
  - **Tutorial** — four rows, each with a line saying what it means: **Auto** (default: every new player starts with the
    basic training and can skip it at any time), **Short courses only** (no basic training at the start — the story
    begins right after VEGA's opening, and VEGA offers the short courses when they fit; a player can still start the
    training in the Journal), **On request** (it does not start by itself; players start it in their Journal) or **Off**
    (no basic training and no short courses in this world). A world without a story still has the tutorial when it is on.

  The summary row shows your choice (e.g. "VEGA (Fast) · Tutorial Off ›"); the presets reset it to VEGA · Normal · Auto.
  The world admin can change all three later in-game (Character tab → world rules): the **Main story** row (VEGA /
  None, above *Tutorial* and *Story pace*), the **tutorial** and the **story pace**. Switching the main story **keeps
  the progress**: switch VEGA off and the story takes a break for everyone — VEGA tells no story, the Journal has no VEGA
  part, the story's fragments and the Guardian system are hidden — and switch it on again and it carries on exactly
  where it stopped, the world's progress and every player's chapters alike. (On a world where the Guardian was beaten,
  the calmer machines of that ending come back with the story too — without a story the world's enemy rules apply as
  they are.) Switching a story off asks first; switching
  it on does not. Everyone online reads a short line about the switch. A world that never had VEGA starts it from the
  beginning. Official (hosted) worlds always start with VEGA and the tutorial on Auto.
- The **Editors** submenu (main menu) holds the creation tools — see §6.
- **Window mode** (Settings → Graphics — from the main menu or the pause menu): the **"Window mode" / "Fenstermodus"** option cycles **Windowed → Borderless →
  Exclusive** and applies immediately. The default is **Windowed** (a normal movable, maximizable window);
  Borderless fills whichever monitor the window currently sits on; Exclusive is classic full-screen.
- **Language** (the language button at the bottom of Settings): switch the whole game between **14 languages** — English, German, French,
  Spanish, Italian, Portuguese, Dutch, Polish, Turkish, Russian, Ukrainian, Chinese, Japanese and Korean.
  A language appears in the picker once its translation clears **45 % coverage**; a text a language does
  not have yet is shown in English (want to help? See `docs/developer/TRANSLATION_GUIDE.md` in the
  repository). The Codex's guide articles follow the language as well; an article your language does not have
  yet reads in English.
- **What's new?** (main menu, bottom bar): the release notes of every version, newest first. The dialog
  also opens by itself once after an update. The notes are shown in your language as soon as a release
  has been translated; until then — and while you are offline — they are in English (in German when the
  game is set to German).
- On the **very first start** a short (~28 s) **intro cinematic** plays between the title splash and the
  menu — any key, pad button or tap skips it, and the Credits screen's **"Watch intro"** button replays it any time.
- On a **new world**, the ship AI **VEGA** boots up and walks you through the **basic training** (six short parts,
  see §5 → VEGA; right after the welcome card she asks whether you want it — **I know the game — skip** if you do,
  and on a world set to *Short courses only* or *Off* there is no training at the start) — her opening narration is staged like a scene (letterbox, an orbit shot of your landed ship, scaled
  to your ship's size). Veteran saves get a one-line "systems online" instead.
- **Skipping a cutscene works on every device:** **Esc** on the keyboard, the pad's back button **(B)** (or
  whatever you bound to *Close / back*), or a click / tap on the **Skip** button in the corner — this goes for
  the intro, VEGA's opening narration and the story's ending. The Skip label names the control for the device
  in your hand. While a cutscene plays, your character holds still and the HUD, the on-screen touch controls,
  the chat and the name tags step aside; the subtitles follow your **UI size** setting, and its flashes follow
  **Reduce flashes**.
- **Backups and game versions.** Every singleplayer world keeps its own safety copies in
  `singleplayer-saves/<world>/backups/`: one after every 60 minutes of play (the newest five are kept) and one
  right before a game update changes the world's block set or first opens a world saved by an older version
  (`pre-upgrade_…` — after that, the older version no longer opens it). To go back, close the game and copy a
  backup over `world.db`. A self-built ship that an older update left unable to launch ("needs a helm / an engine
  / a door" although they are all there) is repaired once when the world is opened, after a copy
  (`pre-hull-repair_…`); if the game cannot tell for sure how the hull looked, it leaves the ship as it is. A world
  that was last saved by a **newer** version of the game is not opened by an
  older one — the game says so and leaves the world untouched. On the **Official Worlds** the same holds: a
  save from a newer game version than the server's is refused at upload, and a world the server cannot open
  yet says so when you join it (the worlds page on the portal and the in-game *Official Worlds* list mark it
  **"needs a server update"** in amber; **Play** still works and tells you the same) — it opens
  again once the server is updated. And a server whose game
  version has a different set of blocks refuses the join with "This server runs a different version of the
  game" — update the game (or the server).
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
  particular moment (loading, mining, flying). If you have turned **VSync off** in Settings → Graphics, set a
  **frame-rate cap** (e.g. 120) there — uncapped, the GPU runs at 100 % even in light scenes, which is exactly
  the load that exposes a flaky driver or power supply.

---

## 2. On-foot controls

| Key / input | Action |
|---|---|
| **W / A / S / D** | Move |
| **Mouse** | Look |
| **Space** | Jump — **hold in the air to fire the jetpack** (if you **wear** one — the back slot on the Inventory's **Suit** tab); with a **glider** on your back instead, **hold it while falling to glide** (needs air — not on airless worlds or in space); **in water: swim up / surface**; **jump at a wall while pushing towards it and hold Space to hold on and climb** — on the wall you hold on as long as you hold Space; let go and you slide down (see §5 → Climbing walls) |
| **Space ×2** | **Creative/Sandbox worlds only:** toggle free flight — then Space rises, Ctrl/C sinks, and you keep colliding with the world (so you can still land and build). Touching down turns it off |
| **Ctrl / C** (hold) | Crouch/sneak — walk slower, stop at ledges instead of walking off (corners included — on a parked ship's decks and roof and in a monorail wagon too); climb **down** ladders; **drop off a wall you are climbing**; descend in zero-g. Other players see you **squat** (and their heads follow where they look) — your own avatar squats in the third-person view too |
| **Left-click** | Mine the targeted block (or **scan** it when a scanner is selected; **shoot** the energy rope with the rope gun selected, see §5) |
| **Right-click** | Place the selected hotbar block (or **use** the selected gadget, e.g. the terrain scanner; with the **suit teleporter** selected it opens the destination picker — back to ship / to an ally, see §5; with the **energy rope gun** selected, **hold** it to reel yourself in along the rope) |
| **Mouse wheel** | Cycle hotbar slot |
| **1 – 9** | Select hotbar slot |
| **Middle mouse** | **Hotbar slot actions** on the selected slot: swap it against any backpack item, and for a building material also colour it (dye / glow / own pattern) or re-form it — see §5 → Hotbar slot actions (rebindable) |
| **F** | Attack with the held tool/weapon — hits what's **under your crosshair** (the reticle turns red over a target; with **auto-aim** on, the nearest enemy in front of you is acquired automatically) |
| **R** | At your own **cockpit / ship console** — inside the parked ship, or at the helm after **F** stepped you into the floating ship — while the repair panel is up: repair the ship (see §5 → Repairing your own ship; pressed elsewhere while the panel is up, a hint says where to go); otherwise repair the targeted wreck breach with the selected hotbar block (see §5 → Wrecks); with a **shaped block, furniture, ladder, stairs or a block with a front** (machines, counters, devices) selected: rotate its placement orientation (**Shift+R** cycles backwards — see §5 → Craftable block shapes) |
| **L** | Toggle the suit headlamp (requires a **worn** `suit_lamp` — a module slot on the Suit tab; without one a hint says so) |
| **B** | Switch the **stealth suit's** cloak on / off (requires the stealth suit **worn** in the chest slot; drains suit energy) — rebindable |
| **G** | Loot the nearest container — take everything out of the nearest storage crate; at an **empty** crate a short message says *The crate is empty.* |
| **Q** | **Feed** — with food in your hand and a **begging herd** nearby, throw the animals one piece (see §5 → Creatures); does nothing otherwise, so it never wastes food — rebindable (Settings → Controls → *Feed a begging animal*) |
| **H** | Store your loose materials and blocks in the nearest storage crate / wood box (the one-key sweep leaves tools, weapons, food and equipment with you — put those in by hand on the crate screen, **E**; with nothing of that kind in your bag a message says so); **aboard your ship** with no crate in reach: **stow them all into the cargo hold** (the stack in your hand stays) |
| **E** | Use a nearby ship/station tile (cockpit, workshop, **cargo — opens the cargo hold page**, medbay, …); **at a vendor: trade or talk** (a small question — **E** again trades, *Talk* opens the conversation); **board your hover speeder**; **beam** from a teleporter pad you're standing on; **open the crate screen** of a storage crate you're aiming at — its contents above, your backpack below, click to move (see §5 → Storage crates); **open or close a wooden or hinged door** — the one you're looking at, else the nearest (sliding and energy doors open by themselves; see §5 → Crafting → Doors) |
| **X** | Pack up (stow) a nearby deployed hover speeder or boat back into its item; at your own landed ship's **cockpit / console**: **recall** every speeder / boat you left out on this world straight into your inventory (parked beside the ship, with a marker, only when no slot is free; see §5 → Hover speeder) |
| **T** | Send a trade request to a nearby player — also while your ships are docked (a visiting friend stands right beside you) |
| **K** | Send a dock request to a nearby player (at the helm and on a spacewalk too — see §3) |
| **U** | Undock from a player / leave a boarded space station. As a **guest** in a docked friend's ship: **back to my ship** (the hint reads "Visiting Lena's ship · U: back to my ship"). As the **host** while a friend visits: **send the guest home** (it undocks; the hint reads "Tom is visiting · U: send home") |
| **O** | Aboard your own (or any player-built) station: toggle **zero-g construction mode** for yourself — the suit floats over the decks (Jump rises, Crouch sinks) so you can extend the hull without walking; press again to walk. Not saved; off whenever you board |
| **V** | Toggle first / third-person camera |
| **I** | Toggle **thermal vision** while looking through the thermal binoculars (see §5 → Binoculars) |
| **N** | Advance the current **VEGA** dialogue line (also fast-completes the typewriter; also opens the folded VEGA tab) — rebindable; gamepad **View** (Back), touch **NEXT ▶** |
| **Tab** | Open / close the gameplay menu (Inventory, Crafting, Blueprints, Ship, Map, Missions, Journal, …, Character); also closes full-screen menu screens such as the Codex |
| **M** | Toggle the world map (top-down planet view; click to set a waypoint) — rebindable; touch **MAP** |
| **Enter** | Open the chat box (Esc cancels) |
| **J** | Hide / show the chat scrollback for this session (rebindable; see also Settings → Comfort → Chat display) |
| **Caps Lock** (hold) | Push-to-talk voice (if the server enabled voice; needs a **worn** radio; key is configurable — see §7 → Voice chat) |
| **Esc** | Close the current screen; if no game screen is open, open the pause menu (Resume · Settings · **Back to my ship** while on foot · Quit) |

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
| **LB** | Place the selected hotbar block / use the held gadget (hold: reel in on the energy rope) |
| **D-pad ◄ ►** | Cycle hotbar slot |
| **D-pad ▲** | Open the chat (with the on-screen keyboard) |
| **D-pad ▼** | Turn the building block you are holding |
| **(A)** | Jump (hold in air = jetpack, or glide while falling with a glider; in water = swim up; jump at a wall, push the stick towards it and hold (A) to hold on and climb — let go of (A) to slide down, **(B)** drops you off, see §5 → Climbing walls) |
| **(X)** | Use / board / interact |
| **(Y)** | Toggle first / third-person camera (only that — it no longer jumps as well) |
| **R3** (click the right stick) | **Hotbar slot actions** on the selected slot (see §5) — stick navigates the menu, **(A)** picks, **(B)** closes |
| **L3** (click the left stick) | **Actions** — a list of everything you can do right now (rotate the held block, trade / dock with the player beside you, undock, back to my ship from a friend's ship, dock / undock / go over to the docked ship at the helm and on a spacewalk, loot / stash, repair, lamp and stealth (only while worn), thermal vision, feed a begging herd, deploy a station in EVA, leave / refuel the speeder, …); stick navigates, **(A)** picks, **(B)** closes |
| **View** (the two-rectangles button left of the Xbox logo; "Back" on a 360 pad, Share on PlayStation, − on Nintendo) | **VEGA: continue** — advance or dismiss the ship AI's line (the same as **N** on the keyboard); also opens the folded VEGA tab |
| **Menu** (☰ — the three-lines button right of the Xbox logo; Unity and 360-era pads call it Start. The Xbox-logo button itself belongs to Windows' Game Bar and never reaches the game) | Open / close the gameplay menu — its top strip has the **Pause menu** button (Resume / Settings / Quit, the same dialog **Esc** opens on the keyboard); **(B)** resumes |

In menus, the left stick / d-pad navigates, **(A)** confirms and **(B)** goes back — that includes **every
tab of the gameplay menu** (Inventory, Crafting, Blueprints, Ship, Map, Missions, Character, …), the Codex, the
Arcade, the landing-pad chooser (pick a pad with the stick, **(A)** lands, **(B)** cancels), trade and
docking requests, the bandit demand, the blueprint / beacon / transporter windows, the bio lab, crystal devices,
a storage crate's filter, the train cab, an NPC interview, and both maps. **(B)**
also closes the gameplay menu itself and backs out of the main menu, Settings, the world picker and the
editors. The right stick also steers the ship in flight; the d-pad cycles the **ship-systems bar**
(laser ↔ tractor beam) at the helm. At the helm **LB** is the **target lock** (tap = next target, hold =
let go) and **R3** locks the **nearest enemy** (again = the next nearest); *Target ahead* — lock the rock on
your nose — is in the **L3 Actions** list. See §3 → *Target lock*. Direct
hotbar number-key picks remain keyboard-only. Verbs without a
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

**Crystal Net on a pad:** **(X)** on a crystal switch flips it, on a button presses it, on a lift stop calls the
lift, and on a device with
settings opens its menu (a picture grid or a list — navigate with the stick / d-pad, **(A)** picks, **(B)**
closes). Step plates, sensors and the Device Eye need nothing pressed.

**Controller settings.** *Settings → Controller* has the pad's own rows: the **stick dead zone** (raise it
if a worn stick drifts on its own), separate **look speeds left/right and up/down** as a multiplier on your
normal sensitivity, **invert up/down for the pad only**, **mine and place on the triggers** (extra to
LB/RB — switch it off again if your controller mines by itself, which some pads do because they report
their triggers differently), the **button names** shown in hints (Xbox, PlayStation or Nintendo — the
buttons themselves do not change, only what we call them), and a **vibration** switch: with it on, the controller
rumbles when you are hit, fire your own weapon or swing, something explodes nearby, your ship is hit or jumps through
hyperspace, you land hard, and a block you mine breaks. It only rumbles while you are playing on the controller and the
game window has the focus, never in a menu, and it stops at once when you die or leave the world. It is separate from the
camera-motion comfort switch. Vibration works on Windows (also in the Windows build on Linux via Proton) with Xbox-style
controllers; in the browser and in the native Linux build it is not available, and the row says so. The controls lines at the bottom of the HUD — on foot, in flight and
on a spacewalk — follow these settings: they use the chosen button names (a PlayStation pad reads "R1 fire ·
(Square) land/dock"), show your rebound buttons, and add the triggers to mine, place and fire once you switch
them on. A control you leave without a pad button simply drops out of the flight and spacewalk lines. Walking
with the keyboard while a pad is plugged in keeps the keyboard hints.

**Rebinding:** every control row in **Settings** has two buttons — the keyboard key and the pad button.
Tap the pad button and press any controller button to rebind it (actions marked **—** have no pad button
by default but can be given one). *Reset controls* restores both keyboard and pad defaults. The **Menus**
group at the bottom (close/back, open the menu) shows its keyboard keys greyed out: **Esc** and **Tab**
stay fixed so nobody can bind away the key that leaves a window — the pad buttons **(B)** and **Menu**
are rebindable as usual. The hints on screen name **your** key: the controls lines at the bottom (on foot,
flying, in EVA), prompts like "G: loot" or "Launch (E)", the crate buttons and the server's messages follow
your rebinding, and on a pad they name the pad button — or **LS** (the Actions list) for a verb without a
button of its own. So do the item, block and blueprint descriptions (in the crafting, inventory and Blueprints
panes and the Codex lists): "Press E on it" names your use key, "Use RMB to magnify" reads "Use LB …" on a pad and
"Use PLACE …" on a tablet. The Codex's guide articles do the same ("press E", "hold Space", "the planet map (M)").

### Touch controls (experimental — tablet / touch browser)

On a touch device (tablet, or a touch-capable browser) on-screen controls appear automatically, and every
hint names the on-screen button from your first tap on. The buttons swap with what you're doing (during a
cutscene they step aside — tap its **Skip** button to leave it, or the **Continue** button on VEGA's line):

| On-screen control | Action |
|---|---|
| **Left stick** (bottom-left) | Move / thrust / steer |
| **Drag** anywhere on the right | Look / steer the ship |
| **◄ ►** | Cycle hotbar slot (ship-systems bar — laser ↔ tractor beam — at the helm) |
| **…** (beside ►) | **Hotbar slot actions** on the selected slot (see §5); shown only when the menu can open |
| **ACT** (beside ◄) | **Actions** — a list of everything you can do right now: rotate the held block, trade / dock with the player beside you, undock, back to my ship from a friend's ship, dock / undock / go over to the docked ship at the helm and on a spacewalk, loot / stash, repair a wreck, lamp, thermal vision, deploy a station in EVA, leave / refuel the speeder, … Tap an entry to do it. Shown only when something applies. A hint on screen that reads "ACT: undock" or "Store all materials (ACT)" means: find it in this list |
| **NEXT ▶** (top-centre) | **VEGA: continue** — advance or dismiss the ship AI's line; also opens the folded VEGA tab; shown only while a line is up |
| **≡** (top-right) | Open / close the gameplay menu |
| *On foot:* **JUMP · MINE (hold) · PLACE · USE · DOWN · CHAT · VIEW · MAP** | Jump (at a wall, with the stick pushed towards it: hold JUMP to hold on and climb, let go of it to slide down) · mine · place · use/board · descend (on a wall: drop off) · open chat · camera · planet map |
| *On foot, when it applies:* **ROTATE · ATTACK · FEED** | Rotate the held block's placement (appears while a rotatable block is selected) · swing / fire the held weapon (hold on the Guardian core to breach it) · throw one piece of the held food to a begging herd (appears while you hold food and an animal begs nearby) |
| *Flying:* **FIRE (hold) · LAND · SHIP · AUTO · TARGET · MAP · VIEW · USE · UP · DOWN** | Fire · landing pads · walk the ship · autopilot · target lock (tap = next target, long press = let go; "nearest enemy" and "target ahead" are in **ACT**) · system chart · camera · dock/board · float up/down |
| *EVA (spacewalk):* **FIRE (hold) · PLACE · DEPLOY · VIEW · USE · UP · DOWN** | Mine · place the selected block · deploy a station core · camera · board · float up/down |
| *Speeder:* **BOOST (hold) · JUMP · EXIT · FUEL** | Boost · hop · dismount · refuel |

**Crystal Net on touch:** **USE** while looking at a crystal switch flips it, at a button presses it, at a lift
stop calls the lift, and at a
device with settings opens its menu (tap a picture or a list entry; **Close** or ≡ leaves it). Step plates,
sensors and the Device Eye need no button at all.

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
| **Caps Lock** (hold) | Push-to-talk voice, as on foot (needs a **worn** radio; see §7 → Voice chat) |
| **W/A/S/D** | Fly through the **system** — every planet/moon is out there at its real position |
| **L** | Land — on the body you've flown up to (the HUD shows "land on <name>") or, if none is near, back where you launched. Opens the **landing map**: the planet's real terrain, the day/night band and — switchable with **☁ Weather: on/off** (remembered) — its **live weather**, with drifting fronts marked "◀ front ▶". Every pad says the weather waiting there (e.g. "⚡ Storm", "☀ Clear"), so you can pick a sunny pad. Click a free pad (or its number key) to land; **Esc** cancels |
| **E** | Board a nearby space station (within range of its hull; the ship flies round to the station's hangar mouth and docks there before you board) — or, next to a **wormhole**, fly through it (see *Wormholes* below). **Docked with a friend** whose ship floats parked within 48 m (they walk inside it, or are out on a spacewalk): **go over** into their ship as a guest — "E: go over to Lena's ship" (whichever is nearest — the friend's ship, a station or a planet — takes **E**). While the friend sits at the helm the line reads "Lena is flying"; **E** then asks them to step inside, and they get a message saying so |
| **K** / **U** | **Dock** with the nearest other pilot within 48 m (never an NPC trader; they answer the usual docking request) / **undock**. Docked, the line under the crosshair reads "Docked with Lena · U: undock". Pad and touch: both are in the **Actions** list (L3 / ACT), together with *Go over to the docked ship* |
| **Last hotbar slot → hold fire** | The **ship scanner** (every ship has one): point the nose at a planet, moon, asteroid, station, wreck, life pod, anomaly, wormhole or machine and **hold fire** until the ring fills. See *Ship scanner* in §5 |
| **T** | **Target lock — next target** (pad **LB**, touch **TARGET**): attacking enemies first, then other enemies, then stations / wrecks / debris fields / life pods / anomalies / wormholes, then other pilots and traders. With a **mining laser** selected and nobody attacking, the three nearest asteroids or wreckage fragments in its reach come right after the enemies. **Hold T** (~½ s) to let the lock go. See *Target lock* below |
| **R** | **Nearest enemy** (pad **R3**, touch: in the **ACT** list) — press again for the next nearest |
| **Right mouse button** | **Target ahead** (pad: **L3 → Target ahead**, touch: **ACT → Target ahead**): lock what is under the crosshair — also asteroids, salvage and planets, which the cycle otherwise skips. Pointing at empty space lets the lock go |
| **F** | **Step inside your ship** while it floats: walk its cabin, build, sleep. The **helm** (**E** at the cockpit; the menu is on **Tab** as always) takes you back to the flight, and walking out through the **hatch** starts a spacewalk — either way the ship is exactly where you left it, pointing the same way, and a landing afterwards comes down on the world as usual. Other pilots keep seeing your ship meanwhile — engines off, its nameplate says *parked*. On the spacewalk, float up to your ship (a few metres from its hull) and press **E** to climb back in — that works next to a station too: a station only takes **E** when you look at it, or when your ship is out of reach (the prompt at the bottom always says which one **E** boards). Docked with a friend, their parked ship is the third choice: out of reach of your own hull, "E: board Lena's ship" takes you over as a guest (a station whose hull is nearer still wins). **K** and **U** dock and undock on a spacewalk as at the helm. Docking a station from a spacewalk and leaving it again puts you back beside your ship, where it floated |
| **P** | **Autopilot** (needs an `ai_core_mk2`+ module): flies to your nav waypoint if one is set, else the nearest station / landable body; any manual input takes the helm back |
| **M** | **System chart**: a top-down map of the current system — every planet shows its current weather as a small glyph (☁ ☂ ⚡ ❄ …). Click a body/station to target it or empty space for a free **nav waypoint** — it shows on the radar with a distance readout, and the autopilot flies to it. The ship holds position while the chart is open. Space distances (radar, chart) read in **km**; only on a spacewalk is the way back to your ship given in metres. The chart's **Hyperspace** tab (LB/RB on a pad) shows the whole galaxy as stars in their real colours: the ringed star is where you are, named stars are systems you have visited, a **?** is one you have never entered, lines are relay jump lanes. Click a star to read about it and — with a jump generator aboard or a lane — **hyperjump to it straight from the chart** |
| **Tab → Map** | Hyperspace **jump to another system** (needs a `jump_generator` module) — flying is within one system |

### Target lock

A **lock** marks one target and keeps it marked while you fly. On screen, a frame sits on it with its name,
what it is and how far it is (in km, with ▲/▼ when it is well above or below you); for an enemy — and for a
rock while a mining laser is selected — a line says **In range** or **Too far — fly closer** for the weapon you
have selected. The frame's colour **and shape**
tell you what it is, so you never need the colour alone:

| Target | Colour | Frame | Arrow |
|---|---|---|---|
| **Enemy** (drone, saucer, cruiser, a raider that fights) | red | a diamond with **!** over it | filled, double (») |
| **Demands cargo** (a raider still talking) | orange | a hollow diamond | filled |
| **Neutral** (wreck, asteroid, anomaly, wormhole, salvage, trader, planet) | white | square corners | hollow |
| **Friendly** (station, life pod, another pilot) | cyan | a ring | hollow |

When the target is **off screen — or behind you** — an **arrow** on a ring inside the screen points the
way, with the distance next to it; it pulses gently while that enemy is shooting at you (just bright with
*Reduce flashes*). Other enemies attacking you get a small **red tick** on the same ring (up to four), and
your **nav waypoint** from the chart (**M**) gets an **amber arrow** with its distance too.

- **What you can lock:** enemies and traders within your **radar** range (1 300 km, 3 000 km with a
  radar array) — what the radar shows, you can lock — stations, wrecks, life pods, anomalies, wormholes and
  **your friends** anywhere in the system, and everything while the Quantum scanner's system sweep lasts. A lock
  that drifts out of range shows **Target lost** and lets go after a moment.
- **Finding a friend:** another player is always lockable while you both fly in the same star system — the arrow
  points the way and shows the distance (a friend is only a navigation target; your weapons ignore the lock). A
  friend who took off from **another planet or moon** of the system appears as a **dimmer pink dot** on the radar,
  and their lock reads **Flying near …** with that body's name: land there and take off again to fly together.
- **Asteroids** are locked with *Target ahead* — and, with the asteroid breaker or the
  starter laser selected while nobody is attacking, the three nearest within that laser's reach are in the **T**
  cycle too.
- **By itself:** with nothing locked, the ship locks onto an enemy the moment it starts attacking — VEGA
  explains it the first time. It never swaps a lock you chose. After a kill the lock moves on to the next
  attacking enemy (or lets go).
- **Mining:** with nothing locked, your first shot at a rock (or the wreck) locks it — the frame shows its
  remaining hull and **In range** / **Too far — fly closer** for your mining laser. When the rock breaks, the
  lock moves on to the nearest rock your laser reaches (an attacking enemy always comes first), or lets go when
  there is none. With the tractor beam or the scanner selected, none of this happens.
- **The weapon helps:** with the **Auto-aim** world rule on, your laser prefers the locked target while it is
  in range and roughly in front of you (within about 40°). With Auto-aim off the lock only shows the way —
  you aim yourself. The aiming dot turns **red** while the locked enemy is in your sights. The tractor beam
  pulls a locked salvage drop, and the **scanner** reads a locked object in its range without careful aiming
  (a locked planet from anywhere) — as long as nothing else is right in front of your nose: what you point at
  is always scanned first.
- The lock lets go when you land, dock, step inside your ship, start a spacewalk or jump.

Ship classes differ in **speed** and **handling** (`data/ships.json`): e.g. the scout is fast and agile,
the hauler slow and heavy. Hull + shield are shown on the HUD; shields recharge, hull does not.
Hosted/default servers allow free manual space flight so players can fly through a system without needing a
separate unlock; admins can still disable it through server world rules.

---

## 4. Menus & HUD

- **Tab menu** — tabs for Inventory, Crafting, Blueprints (research), Ship (modules/build), Map, Missions,
  plus **Journal** (your **stories** — the basic training, VEGA's story and any other story of the world, see
  "The Journal" in §5 — then **VEGA's notes**, and a **Notes** category: up to 20 titled notes of your own,
  saved with your character — the buttons above the text make it **bold** (B), give it one of **eight colours** or
  make it **plain** again: select text first to style just that, or click without a selection to style what you type
  next; *Preview* shows the result. Behind the buttons are codes you may also type yourself: `§0`–`§f` colours a span,
  `§l` makes it bold, `§r` resets), **Creatures** (your tamed
  companions, see §5), **Alliances** (see §5), **Achievements** and **Photos**, with **Character** (your look, the people
  you know, and the in-world options — world rules, VEGA hints, tutorial, save; called "Settings" before) pinned far right.
  Graphics, sound, controls and language are not here but in **Settings** (main menu, or the pause menu). On a pad the menu opens with the **Menu** button, on a tablet with the **≡** button. The **Achievements** tab opens with a **Progress** block —
  research N of M blueprints, Codex discoveries (with a button straight into the Codex chapter, where every entry
  now names the planet and system it was first scanned on), story %, achievements done — and a **Journey** grid of your
  lifetime tallies (worlds visited, systems entered, blocks mined/placed, subjects scanned, missions …); the
  goals below run from the first blocks to the late game (thousands of blocks, dozens of worlds, every
  blueprint, your own station or ship, the Guardian finale) and each pays an item reward. The **Blueprints** tab
  header shows how much of the tree you have researched and which blueprint you could research next; its filter
  row shows your **knowledge points** and data fragments and has a **"Researchable now"** toggle (*Jetzt
  erforschbar*; it was "Enough knowledge" before) that keeps only the blueprints you can research right now —
  prerequisites done, enough knowledge **and** all research materials at hand, so every card left reads
  *Unlockable* (each card also shows *Knowledge have/need*). Within each tier the blueprints are listed A–Z. The
  **Crafting** list puts what you can craft right now on top, then the recipes that only lack materials, then the
  factory recipes with no terminal in reach, then the locked ones — each group **A–Z in your game language** (Ä next
  to A in German, not after Z); its **"Craftable now"** toggle (*Jetzt baubar*) keeps only the top group, which is
  then simply alphabetical. The market's barter offers keep the trader's own order. Crafting/Blueprints/Ship are **location-bound**: Crafting to a station **block**
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
  valleys, seas and lava seas out to the chosen distance (a frozen sea, pond or river as ice, and cold land under its
  snow and ice, as the chunks show it), plus cities, settlements, ruins and player builds as they
  stand. You cannot walk or mine it; as you get closer, the real chunks replace it. It applies at once, even from the
  pause menu. Defaults: **1024** on the desktop client, **512** in the browser and on tablets.
- With the far view on, the haze reaches farther: worlds with thin air show a long, clear horizon, airless moons are
  crisp to the edge, dense atmospheres still close in — and fog, sandstorms and ash storms still pull the view in hard.

### Effects you can read (and the comfort settings for them)

- **Every tool and weapon looks like itself.**
  - The scrap pistol fires a glowing brass slug.
  - The gauss pistol charges and leaves rings along its rail.
  - The laser draws a red beam with a glowing hot spot.
  - The plasma blaster throws a wobbling violet ball that lights up a cave as it flies.
  - Blades sweep a slash ribbon.
  - The shock gloves send a cyan ring of pushed air out between your palms; the energy gloves crackle with gold
    sparks at the punching fist.
  - Shots leave the barrel of the gun you hold.
- **Hits and defeats.**
  - A creature or robot you hit flashes white.
  - Little stars circle over a creature or bandit the shock gloves made dizzy; a dizzy robot fizzes with sparks.
  - When something hurts you, a red marker around the crosshair points toward the creature, robot or bandit that
    hit you, and it follows that attacker as it moves. A small red arrow beside the marker shows when the attacker
    is clearly above you (a flier overhead) or below you. The attacker itself blinks red for a moment. Damage
    with no attacker (lava, hunger, no air) points the marker at the closest threat instead, if there is one.
  - A creature that bites you lunges at you and makes its attack sound, whether it is right next to you or a few
    blocks away. Big creatures reach further.
  - Damage shows as red at the edge of the screen, as strong as the share of your health it took. Steady small
    damage (a scan drone, a bite, lava) pulses the edge about twice a second. Only a real hit — 5 % of your health
    or more — flashes the whole screen red and gives the view a light shake.
  - Beaten creatures break apart into sparkles, robots fall to parts, and bandits beam away.
- **Mining.**
  - A thin, softly shimmering blue frame marks the block you aim at — exactly the one a click would mine or fill — and
    turns orange while you mine it. *Reduce flashes* or *Reduced effects* keep it still.
  - Cracks spread over the block as it weakens.
  - Chips in the block's own colour fly off the face you work.
  - The block splits into little cubes, and the resource flies to you as a glowing gem.
  - Each drill has its own sparks; the mining beam is a real beam that makes the block glow white-hot.
- **Scanning.**
  - The terrain scanner sends a wave rolling over the ground. Ores light up as glowing ghost cubes *through the
    rock* when the wave reaches them.
  - The hand scanner draws a holographic box around its target.
  - The binoculars show the distance to what you look at.
- **In flight.**
  - Each ship weapon has its own look.
  - Your shield appears as a hexagon bubble where it is hit, and shatters when it breaks.
  - A damaged ship smokes, and destroyed enemies and asteroids burst apart.
  - Space dust streaks past with your speed.
  - The hyperjump is a tunnel of light.
  - Landing builds a glow of heat around the ship.
- **Other players** see your shots, swings, drilling and scans, and you see theirs. Their arm really swings (a
  blade's chop, a punch, both arms for the shock gloves) and you hear the swing where they stand. Gadgets (medkit,
  stasis, blaster, terrain scanner) only show their effect once the game has accepted the use.
- **Comfort settings:**
  - **Settings → Comfort → Screen shake** (0–100 %) sets how hard hits, weapon kicks and explosions rattle the view.
    **Settings → Comfort → Camera motion** off still stops all of it.
  - **Settings → Comfort → Reduce flashes** turns big full-screen flashes into a soft wash, for light-sensitive eyes.
    That includes lightning in a storm (the screen and the landscape only brighten softly), and flickering lamps —
    the red emergency light of a badly damaged ship, engine and wormhole light — glow steadily instead.
  - **Settings → Comfort → Reduced effects** draws fewer particles and calmer screen effects; it applies at once,
    also from the pause menu.
  - **Mute alarm sirens** (Settings → Audio) silences the Crystal Net sirens and your ship's hull alarm.
  - **Settings → Comfort → Story text: wait for me** (on by default): the texts of story scenes wait until you turn the
    page — **N** (pad **Back**), the **Continue** button, or a tap on the text box. Off, they turn the page by themselves
    after a reading time — and **N** (or a tap on the text) still skips ahead to the next page. Either way, once you have
    read a scene's text, the picture moves on about a second later instead of making you wait. Titles always run on
    their own.
  - Lower graphics presets use fewer particles automatically.

### Atmosphere effects (Settings → Graphics → Atmosphere effects)

The air and the weather leave their mark on the world (atmosphere package, October 2026):

- **Mist in the valleys** gathers in the low ground around dawn and dusk and in fog weather, and burns off toward
  noon; looking toward a low sun, the haze glows golden. Caves and interiors stay clear.
- **Cloud shadows** drift over the land with the wind.
- **Eyes adapt to the dark**: a cave starts dark and resolves after a few seconds; stepping back into daylight
  dazzles for a moment. Nothing flashes — it is a slow ramp.
- **Flickering torchlight**: the light of torches, campfires and lanterns breathes; lamps and crystals stay steady.
- **Wind in leaves and grass**: crowns, plants and grass tufts sway — gently on a calm day, hard in a storm. Plants
  out of the wind — in caves, indoors and under a roof or a thick canopy — only stir in a faint draught. Kelp,
  seagrass and other plants under water keep swaying with the current. Torches and lanterns never sway.
- **Glowing plants breathe**: glowing mushrooms and plants slowly brighten and dim, one breath every few seconds, so a
  glowing forest moves like a slow wave at night. Crystals, ores and machines stay steady; **Reduce flashes** holds
  the plants steady too.
- **Wet ground and snow caps**: rain darkens and glosses the open ground and dries again over minutes; snowfall caps
  the upward faces (roofs too) and the snow melts when it warms up.
- **Rain splashes** where drops meet the ground near you; rain, snow, hail and ash are soft drops and flakes.
- **Shooting stars** cross the night sky now and then (often in a meteor shower); very rarely a distant ship blinks by.
- **Underwater haze and light patterns**: under water the view closes in to a few metres in the water's colour,
  sunlight dances on the sea floor, and bubbles rise past your visor.
- **Light shafts** reach into cave mouths, pits and canopy gaps when you stand in the shade and the sun is up.
- **Lava and crystals light caves**: lava, crystal blocks and the glowing flora cast a quiet light of their own — a
  lava lake glows at its shores, a crystal cavern in its colours. (The light index is built as chunks load, so
  switching this applies as the world streams in.)
- **Soft particles**: dust, mist and smoke no longer cut a hard line into the ground (Medium and up).

The row **Atmosphere effects** sets them all at once — **Off**, **Some** (the cheap ones, what the browser's Low
preset also gets), **All** — or **Custom**, which opens one switch per effect. The expensive effects (cloud shadows,
light shafts, the emitter light, soft particles, caustics) need the **Medium** preset or better regardless of the
switch. **Reduce flashes** keeps the torchlight steady; **Reduced effects** makes the eye adaptation gentler. From
orbit, planets with air show a lit atmosphere rim with a warm terminator and a dark night side.

### Cosiness and home (Settings → Graphics → Cosiness)

Your ship is your home, and it looks like one: when you step aboard — or under a roof inside your own base — the
picture turns warm over a second or two. The planet's own colours stay outside, shadows soften, and the cabin lights
glow warm white by day and amber in the evening (on a planet the evening follows the planet's sun, out in space your
own clock). The living rooms — quarters, medbay, workshop, cargo bay — have warm ceiling lamps; the cockpit keeps its
cool light. Torches, lanterns and campfires now light in their real amber instead of a pale yellow.

The **Cosiness** slider (0–100 %, default 50 %) sets how strong this is: warmer colours, softer shadows and contrast,
and towards 100 % a little more glow around lamps. It is strongest at home, gentle outside, and switched off wherever
danger is close. At 0 % you see the plain picture. It changes only how the game looks, never how it plays, and it
costs no frame rate.

### Arcade (minigames)
- The **DataQubes Arcade** holds 20 built-in minigames. Locked cabinets unlock through data cubes you find
  in the world. Each **star** you earn on a completed run pays **knowledge once**: 5 per star on the first
  game you play, then 4, 3, 2 and finally 1 per star for every further game — a replay pays only for stars
  you didn't have yet.
- **A data cube stays where it is** after you extract it, so other players on a server can extract it too.
  Once its fragment is in your collection it **goes quiet** for you: it rests on the ground, its light is off,
  it no longer hums, and its label reads **"✓ Fragment already in your collection"**. A cube you have never
  visited looks the same when it holds a fragment you already have — there are 20 fragments and many cubes.
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
  each distance. Beacons and saved markers show as smaller blips, a landed trader ship as a gold ship icon, and a
  goal VEGA marked for you (see *Help when stuck* under the basic training) as a **violet flag** with its own caption
  ("Goal 85 m"). Turn
  until the arrow points to the top of the dial and walk; VEGA reminds you of this once you are a long way from
  the hull.
- **VEGA panel** — the ship AI speaks through a typewriter speech panel with a persistent **objective
  chip** directly below it (live progress, e.g. "mine 1/3") during the basic training. Advance lines with **N** or the **Continue** button in
  the panel (clickable whenever the mouse cursor is free, tappable on touch) — a line stays until you do (no
  auto-dismiss), and further lines wait in the queue. Leave a line for about 45 seconds and the panel **folds into a
  small VEGA tab** beside the objective, showing how many messages are waiting; press **N** (pad **View**, touch
  **NEXT ▶**) or click/tap the tab to open it again — nothing is lost. The training's next instruction goes ahead of
  tips that are still waiting. A tip about what you are doing right now (a high wall to climb, a screen you just
  opened, something your ship's scanner could read) waits at most about 30 seconds: if it has not come up by then,
  VEGA drops it, because the moment has passed, and gives it the next time it fits. Story lines are never dropped and
  always come in their order. Some lines ask you something and show **two
  buttons** instead of *Continue* (VEGA's first-start question, a short-course offer): the right one is what **N**
  (pad **View**, touch **NEXT ▶**) picks; the left one takes a click or tap, or **holding** N (View, NEXT) for a moment —
  it fills brighter while you hold. **Esc** never picks a button. Advisor hints can be muted (Character tab → VEGA
  hints); the tutorial is switched on or off in the Character tab (**Tutorial & courses: on/off**), and its parts are
  skipped, restarted and replayed in the **Journal** (Character tab → **Open the Journal**).

---

## 5. Game mechanics

### Survival
- **Health** (max 100): regenerates aboard ship / in breathable air; drained by suffocation (−5/s at 0
  oxygen), lava (−15/s, reduced by armor), fire (−10/s, reduced by armor), and starvation (−3/s at 0
  hunger). At 0 → death (see below). **In Sandbox nothing hurts** (a Sandbox world, or a player an admin gave
  `/mode Player creative`): no oxygen runs out (not even on a spacewalk), and lava, fire, the gas sea, toxic water,
  falls, crashes, bad food and hostiles leave your health alone — angry animals, machines and bandits ignore you, space
  hostiles neither hunt you nor shoot your ship and raiders never hail you — and a speeder or boat you own or drive takes
  no damage either (so it is never wrecked and lost).
- **Oxygen** (max 100 + tank bonuses): refills aboard ship / on breathable worlds; drains outside on
  toxic/airless worlds. An `oxygen_extractor` cuts the drain on extractable atmospheres.
- **Hunger** (max 100): drains off-ship — at your base too: the base gives you air, not food (a **Heal Tank**
  feeds you there, and VEGA reminds you once when you get hungry at home); aboard your ship or in a station it fills
  back up. It drains at a **steady rate** set by the world's hunger setting: a full bar lasts about **9 minutes on
  Slow, 5½ on Normal** (the default) **and 3⅓ on Fast** (Off and Sandbox: no hunger). Nothing you do speeds it up —
  running, jumping, digging, climbing, the cold or a deep cave cost no extra food; only a bio-lab preparation changes
  the rate (*Satiety* slows it, a *Hungry* catch speeds it up). Below ~15 the suit auto-eats stored/loose rations. Food that hurts
  (contaminated meat, spoiled food, toxic berries) never goes into the suit's ration dispenser — eat it by hand, if at all.
  Food sources: hunt creatures (meat), harvest berry flora (replantable via seeds), pick the **fruit** that hangs
  under tree crowns on newer worlds (it grows back; toxic exactly when its tree is — scan first), pick the berries under
  a **Fifi plant** (Sophie's tree-sized plant from the school club: green trunk, yellow leaves, glowing pink blossoms that
  light up the night; it stands in groups on green worlds created from 2026-09-27 on — on worlds created from
  2026-10-20 on only on about every third planet — and its berries are always edible and grow back after two minutes),
  pick the glowing golden **lantern fruit** on a luminous world (always edible, grows back), raid a settlement's
  **greenhouse** (see below), craft emergency rations — or build an **algae tank** (workshop, no blueprint)
  at a base: standing next to it grows 2 algae rations from 1 water (melt 2 snow or 2 ice into water by
  hand if there is no lake). **To eat**, select the food in the hotbar and press the place button (right mouse
  button). When you get hungry while carrying something edible, VEGA names it and tells you that key.
- **When the world waits:** the pause menu (**Esc**), the feedback form (**F1**; **F2** in the browser), reading a
  reply to your report and story cutscenes **hold the world** — hunger, oxygen, the day and every creature wait, for up
  to 30 minutes. Inventory, crafting and the map do not: the world runs on behind them. On a shared server the world
  only waits while **every** player has such a screen open, and for up to 10 minutes.
- **Suit energy** (max 100, **150 with a worn suit battery**): powers the stealth-suit cloak (**B**), the
  **jetpack** (hold Space in the air to thrust up) — and the suit's **climate control** (below); consumers
  stop when it hits 0. Recharges aboard the ship and refills fully at a heal-tank. The **glider** needs none.
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
  hazards** (world creation, or live in the in-game Character tab as world admin) scales this from Off to
  Hard; Creative/Sandbox worlds are always exempt — and in Creative/Sandbox the **suit battery never drains** either
  (powered tools, the beam, the teleporter, jetpack and cloak are free), like oxygen and hunger.
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
- **Toxica-Maxima** (Justus' planet — at most one per galaxy, never in the start system; an exotic type): the poisoned
  landmark. Its air is **always corrosive** and its water **always toxic** (the same rules and liners as on the toxic
  worlds), and a **green acid thunderstorm never ends** under a light-green sky: lightning, acid rain that drains the
  suit and then your health in the open, a roof stops it. The **ground itself is tainted** — soil, stone, the dead
  trees' wood and the ores: diamond, titanium, iron and copper lie right under the surface, more than anywhere else,
  but every piece is *tainted* and matches no recipe. **Decontaminate** it: the **Decontaminator** (blueprint after
  the detoxifier; a block for your base or a ship module) washes **2 tainted + 1 carbon → 2 clean** (ore, stone, soil,
  wood, and the **contaminated meat** of the planet's animals), and the planet's abandoned **factories** wash ore in
  bulk at their terminals (6 tainted → 4 clean, no carbon). Every animal there is **contaminated**: it hunts on sight,
  never sleeps and has **glowing green eyes** — the scanner says "Contaminated". Nobody settled the planet (ruins
  remain), but **6–10 factories** stand there, one near the landing pad — and **every factory hall breathes**: under its
  roof you have air, no corrosion and no rain, on this planet and on every other world (the HUD says "factory life
  support"). Carbon and silicate are clean there, so the wash loop closes on site.
- **Gas giant** (Justus' idea, new galaxies since generation 18): a world with **no ground at all**. Below is a bottomless
  sea of cold **gas** — a thick amber-and-violet haze that the storm drags slowly along; fall in and you sink and die within
  seconds, armour or not, there is nothing to breathe. A few blocks down the gas turns into **dense gas** — dark, heavy and
  churning: it pulls you down (holding Jump only slows you a little) and burns even faster. Above it float **islands of bare rock** (there is no water on this
  world), up to three tiers of them; on the lowest ones stand the **sky cities**, and every inhabited
  city holds a **pocket of breathable air** over its lanes (the HUD says so; an abandoned one has lost it). Everything else
  is **−120 °C and toxic**, and the storm never stops — the suit will not last long out there. Ships land on **metal decks**
  hanging over the gas (a rail and lights around the rim); nothing can be pumped out of the gas. Every animal here **flies**,
  and so does the **sky giant** (see *Giants*). The tiny ambient critters are the world's own too: drifting **gas-bags**
  (always there), **wisps** and, after dark, **spore-drifters** over the gas, and the cold **frostmite** crawling only on
  the decks and islands. The lone giant of a star system is one of these; in galaxies created
  since generation 23 **almost every system has one** on its outermost orbit (a big system often a second one on the orbit
  inside), only the start system and one-planet systems go without — older galaxies keep their rarer roll. Most wear
  rings. From space it shows its banded cloud tops, and on the **system chart** it is the big amber dot, visited or not.
- **Arena Nigra** (Theo's planet — at most one per galaxy, never in the start system; an exotic type): the black
  landmark. The air is **breathable** but it is **hot** (58 °C — the suit's climate drains, then your health; rock islands,
  the ship and a roof shelter you). Two thirds of the world is a **sea of black sand** over basalt, with sharp needles,
  rare deep gorges filled with **lava** (there is no water — the lowest ground is lava), a **red sky** under **black
  clouds** and storms that fall as **ash**. **Nothing grows** there and only a few animals live there, because the
  **Ignivermis** hunts them: Theo's red-and-black sandworm — **three** of them — with the sharpest hearing of any worm.
  Everything a sandworm does (see *Giants*) it does here on black sand, and it **eats the animals**: a herd walking the
  dunes shakes the sand like you do, the worm comes, and whatever stands where it strikes is swallowed — a piece or two of
  **meat** stays on the sand (pets and companions are never eaten). Ores are rare: obsidian veins, iron, copper, sulfur,
  carbon under the rock. VEGA warns you on landing; a few ruins stand on the rock, no settlements.
- **Field medkit** (workshop, blueprint-gated; the doctor sells it, derelict crates sometimes hold one): a reusable
  first-aid pulse. Select it and press **right-click** (pad: **LB**) — you don't aim it at anyone: it heals **you and
  every player on foot within 6 blocks** by 45 health, costs 18 suit energy and is ready again after 4 seconds. When
  everyone in reach is already healthy it is not used and tells you so ("Everyone near you is already healthy") — no
  energy spent. It can't help a player who is down, and it gives you no air.
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
- **Resting in a bed.** Press **E** on a bed or a crew bunk, or at your ship's quarters, to lie down — it still
  becomes your home spawn, as before. Your view settles onto the pillow and looks up and out over the foot of the bed,
  the screen dims to a warm glow, the music turns quiet and VEGA wishes you a good rest. Resting is just for the
  cosy moment: time does not pass faster and there is no bonus (a bed still heals you slowly while you are near it).
  To get up, move, jump or press **E** again (on a gamepad: the left stick, Jump or Use; crouch works too). Dying, a
  teleport, travelling, or the bed being removed gets you up automatically. With *Camera motion* off or *Reduced
  effects* on, the view cuts to the pillow instead of gliding there.
- **Warning tones at home.** Aboard your ship or under the roof of your own base, a vital that runs low beeps
  once instead of every few seconds; its bar keeps blinking.
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
  **biofuel**, **plant fibre** and **polymer** from water and rations; the detoxifier **washes toxic berries and toxic fruit**
  back into safe ones (with carbon), filters **mud to water** and turns giant-mushroom parts into **forage
  bait**. **Archaeology** (after *Terrain Scanner*) reworks **ancient bricks** into concrete and **obsidian**
  into glass; researchers at any market buy **rune stones** for data fragments.
- **Wood box** (hand-crafted from logs): early-game storage sharing the crate's keys and screen, but it
  only holds a few kinds of item (8 stacks) — the workshop's iron crate stores everything.
- **Storage crates — the crate screen:** aim at a placed crate or wood box and press **E**. The screen shows
  **what is in the crate at the top and your backpack (quick-bar included) at the bottom** — click a stack to
  move exactly that stack across, hold **Shift** while clicking to move every stack of that kind. **Anything** goes in by hand:
  blocks, ores, food, medpacks, tools and weapons alike. The buttons on the screen do the bulk moves — *Store all
  materials* (the same sweep as **H**: loose materials, components and blocks, never your tools or food) and *Take
  everything out* (**G**) — and *What belongs in here?* opens the filter picker.
- **Storage crates — choose what goes in:** that filter picker (an ore crate, a food crate, a tool crate, …) makes
  **H** store only the chosen items there — walk your loot past a row of dedicated crates and it sorts itself — and
  the crate screen refuses anything else. The HUD prompt shows **Filter on** at such a crate; select nothing in the
  dialog (or hit *Allow everything*) to go back to accepting it all. Dyed or re-formed variants of a chosen material
  count as that material.
- **Armor**: each worn piece (chest/legs/helmet/boots) adds resistance, summed and capped (~75%); worn boots also
  soften a hard landing by about a third.
- **Water meets lava**: lava hardens wherever water touches it — a lava **pool** (a source) turns to
  **obsidian**, a flowing lava **tongue** cools to **basalt**. Place water onto a lava pool and the pool is
  quenched to obsidian in place; place it beside lava and the neighbouring lava crusts over while the water
  stays. Water flowing into lava still chills to obsidian at the contact face.
- **Lava is slow, water is quick**: placed water runs about seven blocks in a second, while lava creeps one
  block every half second — a quarter of the water's pace — so a breached crater gives you time to step back.
- **Where water comes from**: the **sea** (and everything at or below the sea level), **rivers**, natural
  **waterfalls** and the **waterfall block** never run dry, and neither does a water block you place yourself.
  **Ponds and lakes above the sea level hold only what they hold**: open one and it drains slowly through the
  gap — down to the gap's height, with a little stream running out — and the water seeps away; put a block
  into the gap and the draining stops. Dig a hole under a pond and the pond fills it from its top. Wall in a
  pond and fill it with sand, and the water is gone for good.
- **Flowing water stays near its source**: a stream runs at most about **seven blocks** from where it starts,
  however many steps, shafts or cave floors lie below — it no longer floods valleys and cave systems. For a
  tall cascade that keeps pouring, use the **waterfall block**.
- **Doors keep water out**: water and lava never run, seep or settle through a doorway — shut **or open**, any
  kind of door. A door at the bottom of your base holds back the sea, a river or a bog, and opening it lets
  nothing in. A stream an older version let run through a door dries up by itself; water that already stands
  still in a room stays until you clear it (next point).
- **Drying a flooded room**: first close every hole the water came in through (a gap in a wall, a broken
  window). Then put a block into **every** water cell — sand or dirt is cheap — before you mine any of them
  out again: a placed block takes the water's place, and once no water is left the room stays dry. The
  **fluid pump** (see *Scanning & knowledge*) does the same job one cell at a time.
- **Lava and fire burn everybody**: animals, robbers and Guardian machines take contact damage exactly as
  you do (lava −15/s, fire −10/s), so a flooded fire trench really defends a base. Land animals are kept out
  of a lava column anyway and mostly never reach it; what burns is what flies in, what was inside when you
  flooded it, and what gets driven in. Tame companions never burn, creatures that live in lava are at home
  in it, and with **environmental hazards off** (or on a Creative world) nothing burns at all.
- **Walking Guardian robots stay out of deep water**: like land animals they wade through water **one block**
  deep, but water **two or more blocks deep** is a wall to them (kelp, seagrass and coral under a lake count as
  the lake) — a robot chasing you stops at the shore, and a moat two blocks deep keeps them out (hovering
  scan-drones fly over it). A robot that ends up in deep water anyway — you dug or flooded a pond around it, or
  pushed it in — walks out to the nearest dry ground; one stuck in a flooded pit with no way out shuts down and
  is gone. Robots never appear standing in water.
- **Scan-drones stay over the real ground** (#2950): a drone appears over the **top** of the ground as it is now —
  over a base you raised, never in a tunnel under it — and it needs room for its whole body: a **roof** or a **low
  tunnel** is a wall to it (it still weaves through tree crowns and crosses water). A drone that ends up inside a block
  (you built a roof over it) is lifted back over the ground.
- **Aim at water and lava**: holding a block, the crosshair stops at a fluid's surface and the block goes
  *into* that cell (the fluid makes way) — so you can bridge a lake or a lava field from its edge. Holding
  a tier-3 drill (mining beam, diamond drill) the surface is mineable too. While you are swimming the aim
  still passes through the water to the bed.
- **Sand, ash and snow fall.** Mine the block under a sand column and it settles onto the next floor;
  place sand over a pit and it lands at the bottom. Dropped onto lava it sinks one cell at a time and
  replaces the lava — a slow, safe way to fill a lava lake. Loose ground you never touched stays where the
  world made it, and a carved sand form stays a built thing.

### Fire
- **What burns:** plants, bushes and leaves. Grass and the ground itself never catch, so a fire stays in
  the vegetation it started in — and aquatic plants (kelp, seagrass, coral, water lilies) don't burn at
  all. **Tree trunks don't burn either** — not the ordinary ones, not a giant tree's: a brush or crown
  fire burns the leaves and plants around them and leaves the trees standing (and a log house stays up).
  Only the dead, tainted wood of a poisoned world still burns. Furniture with a job — the wood crate, the
  bed, the stretcher — never catches; a rug does. A burnt block leaves **ash** (collect 64 of it to compact
  into matter dust).
- **Clearing ground for a garden:** a plant you cut grows back on its cell after about 30 seconds — unless
  something stands on that cell at that moment or the ground under it is gone. Cut it and put any block on
  the spot for half a minute, and it stays away. (Burning it off works too: the ash blocks the cell.)
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

### Above the atmosphere on foot
- Every planet's air ends at an **atmosphere line** — a fixed height that depends on the planet type (thin air ends
  low, thick air high). Build a tower up past it — or climb one of the rare giant mountains whose summit reaches
  into space — and you are **in space on foot**: a toast says so and names the controls, VEGA explains it the first
  time, and the HUD's location line shows **ZERO-G**. The sky turns black and starry.
- **There is no gravity up there — you float.** Hold **Jump** to rise, hold **Crouch** (Ctrl/C, pad B, touch
  DOWN) to sink faster; let go of both and the suit **sinks slowly back down** on its own. Walking off a summit
  edge therefore no longer leaves you hovering in the air. Climbing walls does not work in zero-g.
- **Air:** above the line there is nothing to breathe, even over a breathable world — the suit's oxygen tank
  drains until you are back below it, and your health does not regenerate up there.
- **The safe way back:** a few blocks below the line gravity returns ("Re-entering the atmosphere"). From then
  until you land, the suit's **brake thrusters** keep your fall slow, and that first landing never hurts — however
  high the summit or tower was. The next fall counts normally again.
- On a player-built station, floating out of its gravity box works like zero-g too, but there the suit simply
  hovers (see *Stations*).

### Climbing walls
On planets, moons and asteroids you can **climb any solid wall** — a cliff, a canyon wall, a tree trunk, the side
of a house. Ladders work as before (walk into one and you go straight up); climbing a wall is slower and takes
effort.

- **Grab:** jump at the wall, keep pushing towards it (**W**, the left stick, or the touch stick) and **hold Jump**
  (**Space**, pad **(A)**, touch **JUMP**). You hold on — at the top of your jump, so the jump still carries you up
  first. Walking along a wall never sticks you to it — you have to be in the air — and a one-block step is still
  simply jumped onto. Holding Jump at a wall also catches a fall: no fall damage. **With a jetpack**, holding Space
  keeps you flying; if its suit energy runs out at a cliff, the held Space grabs the wall instead of letting you fall.
- **On the wall you hold on as long as you hold Jump.** Push towards the wall to climb up, pull away from it to climb
  down, move along it sideways — or hold only Jump to hang still. **Let go of Jump and you slide down** the wall
  (4 m/s, never fast enough to hurt; you can still steer sideways); press it again and you stop. **Crouch drops you
  off** the wall (Ctrl/C, pad **(B)**, touch **DOWN**) — from high up, that fall hurts. Reaching the ground puts you
  back on your feet. You stop at the side edge of a wall and under an overhang — there is no climbing along a
  ceiling. Mining, building and tools keep working while you hang there, so a vein of ore in a cliff is in reach.
- **At the top** you pull yourself over the edge by yourself. If there is no room up there (something above the edge
  or above your head) or the edge gives no hold (glass, ice without claws), a message says so — let go of Jump to
  slide back down.
- **A two-block wall needs no climbing:** run at it, jump and keep pushing, and you pull yourself over it on the way
  up. A one-block step you just jump onto, as always.
- **Your grip tires** — there is no bar for it, you feel it: after a while you climb slower, then your view
  trembles and you breathe hard, and when it is gone you **slide down slowly** (never fast enough to hurt).
  Standing on the ground — or on a ladder rung — refills it in a moment. How far you get depends on the world's
  gravity: about 20 blocks up on a normal planet, roughly twice that on an asteroid, less on a heavy world.
- **Surfaces:** rock, earth, wood and building blocks hold you. **Glass** is too smooth. **Ice** holds only with
  climbing claws. **Sand, snow and ash** crumble under your hands — your grip tires twice as fast.
- **Gear:** the **climbing gloves** (Suit research, crafted at the workshop) make your hands tire much more
  slowly; the **climbing claws** (made from the gloves) last longer still and let you climb ice, sand and snow
  like rock. Wear them in a **module** slot; other players see them on your hands.
- There is no climbing in space, in zero-g, in water, in your ship or on a station.
- **In first person you see both hands climb:** whatever you held goes down, your hands reach up hand over hand
  in the rhythm of your climb, reach shorter and shake when you get tired, drag down the wall when your grip is gone
  and press on the edge as you pull yourself up — with the orange pads when you wear climbing gloves. Back on your
  feet, your tool comes back up.
- Other players see you climb — on walls and on ladders — facing the wall. The first time you stand in front of
  a tall wall, VEGA tells you how it works.

### Energy rope gun
A pistol that shoots a glowing **energy rope** which sticks to any solid block up to **24 blocks** away — and then
reels you in. Research it in the **Suit** branch after the climbing gloves (knowledge 45) and craft it at the
**workshop** (iron plates, cable, polymer, an energy cell). It does **no damage** and never sticks to creatures or
players.

| | Mouse / keyboard | Gamepad | Touch |
|---|---|---|---|
| **Shoot** | left-click | RT | MINE |
| **Reel in** | **hold** right-click | hold LB | hold PLACE |
| **Hang** (the winch holds you) | let go of the button | let go | let go |
| **Drop the rope** | crouch (Ctrl / C) | B | DOWN |
| **Hop and let go** | Space | A | JUMP |

- **Where you end up:** aim at the **top of a block** and you land on it. Aim at a **cliff face whose top is free** and
  the rope winds you up to the edge, and you pull yourself over — no climbing gloves needed. Aim **mid-wall** or at a
  **ceiling** and you hang there until you drop or hop.
- **Across a gap:** shoot the far side and hold the button; while reeling you can lean a little left and right.
- **A slack rope** (shot, not reeled) lets you walk and jump as usual — but if you fall more than a block and a half,
  it catches you.
- The rope **lets go by itself** when you walk out of its reach, when terrain blocks the line, when you switch the
  hotbar, enter water, a vehicle, a train, a seat or your ship. It never causes fall damage by itself; dropping it
  high up is an ordinary fall, as with dropping off a wall (crouch).
- Costs **3 suit energy** per shot; hanging is free. Works on planets, moons and asteroids with gravity — not in zero-g
  or in space. A bio-lab **range** modification makes the rope longer. Other players see your rope.
- The crosshair turns **mint green** while a block the rope can hold on to is in reach. The first time a rope holds,
  VEGA explains the buttons once.

### Mining & tools
- Tools have a **kind** (drill/scanner/…) and **tier** (1–5). A block has a **hardness** and may require a
  minimum tool tier; mining accumulates the tool's power until it exceeds the hardness, then the block
  breaks and yields its **drops**. Powerful drills can clear a small radius — the sweep only takes blocks
  the drill could mine directly (same tier rules) and leaves every protected block standing, just as a single
  swing would (towns and the rail line, stations, factories, other players' bases).
- **The game tells you which tool a block wants.** Scanning a block lists a `Needs:` line naming the cheapest
  tool that opens it, and a swing the held tool cannot land is refused by name rather than a bare "wrong tool".
  A drill that is too weak gets "not strong enough … Needs: Titanium Drill". With the wrong thing in hand — a
  block, a weapon, nothing at all — the message says "You need this tool in your hand for that: Basic Drill",
  and if you carry a fitting tool it adds where it is (select it in the quick-bar, or move it there from the
  backpack first). Only a real tier gate (the right kind of tool, too weak) makes VEGA explain it, once.
  The slot you select is the hand you mine with: the game tells the server your selected quick-bar slot every
  time you join a world, so after a restart the tool shown in hand is the tool that swings.
  Tier-2 blocks (machine housings, factory terminals, titanium/platinum/tungsten and the rare ores) will not
  budge for the starter drill — and since titanium ore is itself tier 2, your first titanium comes from a
  wreck, from loot or from **trade**, never from digging. The reliable road is open from the start: the **trade
  console** aboard your ship (or any vendor) swaps **5 iron ore for 1 titanium ore**, and a **forge** — a
  workbench craft that needs no blueprint — smelts **2 titanium ore into a titanium plate** on a world (aboard, the
  ship's **refinery module** does it; a forge built into the ship is only decoration). Six plates (plus
  cable, energy cells and steel) and the cheap **Titanium Drill** blueprint, researched at the cockpit, get you the
  drill that opens tier 2. VEGA's tip at the first tier gate names this route.
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
  visiting), you just cannot take the building apart. **What you build in a village stays yours:** a floor you lay
  in a lane or a wall you put up in front of a hut can be mined again like any block of yours. The village's own
  houses, fences and the ground it stands on stay protected — you may paint them, but painting never makes them yours
  to mine.
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
  the ship across launches, landings and the walk-in interior. The hull is dented only out in space (combat,
  asteroid rams — see *Repairing your own ship* below) and ship modules (medbay, cockpit, …) cannot be removed.
  Step or hop up through the hatch to enter.
- **Grow your ship**: on a spacewalk you can build onto its hull (and cut hull cells out of it); from inside you
  can keep building outward — up to **15 × 15 × 15 blocks** in all. A **door** you build into the ship is a real
  door (you see its frame and door while aiming — in the cabin and on a spacewalk alike —, it opens and closes, it
  shows on the ship in flight, and mining it gives it back; on a spacewalk aim at the door itself). Out in space a
  closed **energy door** shows its **blue field** (on the ground the field shows only while it is open); sliding,
  hinge and wooden doors show their closed panels. The blue field of your **rear hatch** in the flight view sits
  exactly on the hatch, whatever leaves or blocks you build around the stern.
  An extension counts as ship and has **air only when it is sealed** — airtight walls (glass counts) and a door in
  every opening; step into an open one and your helmet goes on. Walking through your hatch into a sealed extension
  keeps you inside the ship; a spacewalk starts only when you leave the ship's blocks.

### Inventory & cargo hold
- Your **inventory** is your personal backpack — **36 slots** since 2026-09 (a nine-wide grid: the
  **quick-bar** row of nine, the on-screen hotbar, under a line, and 27 backpack slots above it). It travels
  with you everywhere. Older saves are simply widened. On the **Backpack** tab, click a slot to pick its item
  up and a second slot to put it down or swap — the same by touch and with a gamepad (A picks and places).
  **How much one slot holds:** building blocks, ores and materials **1024**; **food, baits and data fragments 99**
  (20 before 2026-10 — an old stack of 20 fills up as you pick more up); medpacks 20; VEGA's memory fragments and
  SPS access codes 10 (they are meant to be rare); tools, weapons and suit gear one each. Machines, energy cells and a
  few other parts have smaller stacks of their own.
- **Suit gear works only while you WEAR it** (2026-09; before, a piece worked anywhere in the pack). The
  Inventory's **Suit** tab shows the worn slots as a **figure** (2026-10): **head** (helmet or titanium helmet),
  **chest** (chest armour, titanium chest plate *or* the stealth suit), **legs**, **feet** (boots or **spring
  boots**), **back** (the jetpack *or* the **glider**), **tank** (one oxygen tank), **liner** (one suit liner)
  and **four modules** (lamp, extractor, a radio, the radar scanner, the climbing gloves or claws, the suit
  battery). **Click a slot** — empty or not — and a list shows every piece that fits it, with what it does and
  where it lies: in your **backpack** or, while you are aboard, in the ship's **cargo hold** (you can wear
  straight from the hold). Pick one to put it on (whatever was worn there goes to your backpack), or **Take off**.
  Nothing fitting yet? The list says so and its button jumps to the recipe. Worn gear **stays with you** when
  you stow, stash or die. The status line above the figure shows what the worn gear gives you: **armour**
  (pieces add up, capped at 75 % — the full titanium set reaches the cap), **maximum oxygen** and **insulation**;
  the HUD oxygen bar's full mark is your real maximum. Below the figure, *Can be used actively* lists the worn
  gear you switch or fly yourself, with its key. The first time you open an old save, your best helmet, tank,
  liner, jetpack, armour and modules are put on for you; a new pilot starts with the suit lamp already worn.
- **Active gear in the HUD:** right of the quick-bar a small **gear strip** shows each worn piece you use
  yourself — the **lamp** (on/off, **L**), the **jetpack** (lights up while thrusting, shows the suit energy;
  hold Jump in the air), the **stealth suit** (on/off, **B**) and the **glider** (lights up while gliding; hold
  Jump while falling). On touch, tap the lamp or the stealth icon to switch it. The strip takes none of the nine
  quick-bar slots — the tool in your hand stays where it is — and it is hidden while you wear none of them.
- **The suit gear at a glance:**
  - **Titanium helmet / chest plate / leg plates** (tier 2, workshop; blueprints after the helmet, the chest
    armour and the leg armour): more armour and warmth than the iron set.
  - **Spring boots** (feet; blueprint after the boots): jump about 60 % higher and land 50 % softer — the coils
    under the boots stretch when you jump and squash when you land.
  - **Glider** (back, instead of the jetpack; blueprint after the jetpack): while falling, hold Jump — the
    wings unfold (once you drop more than about a block and a half below where you left the ground, or at once
    if you press Jump again in the air, so an ordinary hop with Jump held stays a hop) and you glide forward slowly, steering where you look (forward goes a little faster, back
    brakes, left/right drift sideways; strong wind carries you along), using **no** suit energy. Let go of Jump
    and the wings fold. It needs air: on airless worlds, above the atmosphere, in space and under water it stays
    folded. Landing from a glide does no fall damage, and opening a menu mid-glide keeps you gliding.
  - **Suit battery** (module; blueprint after the jetpack): suit energy 150 instead of 100.
  - **Radar scanner** (module): while worn, the round compass also shows **creatures** within ~48 m (hostile
    red, the others green) and **other players** (cyan), with a soft blip when a hostile comes into range.
  - **Stealth suit** (chest): **B** switches the cloak on and off — hostile creatures stop noticing you while it
    drains suit energy; it ends by itself when the energy runs out. Other players cannot see you while it is
    on; in the third-person view you see yourself as a faint shimmer.
- Your ship's **cargo hold** is bulk storage that belongs to the ship (48 slots, growing with cargo-hold
  modules) and is shared by everyone aboard that ship.
- **What goes where:** mined and crafted items fill your inventory first and only spill into the cargo hold
  once it's full (and only while you're aboard). Salvage you scoop up while flying in space goes straight
  into the cargo hold. While you're aboard the ship, crafting draws from **both** at once. Asteroid ore that
  finds no room **floats at the rock** instead of vanishing; a tractor beam pulls it in from 16 m, without one
  you fly through it. Floating salvage survives a landing and a server restart — it is still there when you
  launch again.
- **Stow everything in one go:** aboard, press **H** (anywhere in the ship with no storage crate in reach) — or
  **E** at the cargo station, which opens the **Cargo Hold** page. "Stow all" moves your loose **materials,
  components and blocks** into the hold; your tools, weapons and suit gear stay with you (a spare helmet too), and
  so does **the stack in your hand** (the selected quick-bar slot) — the rest of the quick-bar is stowed, because
  what you pick up lands there first. A line says what happened: how many stacks went in, "the hold is full"
  (the rest stays in your backpack) or "nothing to stow".
- **Moving things by hand:** open the **Tab menu → Inventory**. It opens on your **backpack** grid, which has a
  **"Stow all materials in cargo"** button. The **Cargo Hold** page shows the hold's **used/total** capacity,
  **"Stow all materials in cargo"** and **"Take all out"** side by side, and lists the hold stack by stack — each
  with a **"Take"** button that moves **exactly that stack** into your backpack (hold **Shift** while clicking it
  to take every stack of that kind). Select an item — a backpack slot, or a stack's card in the hold — and the
  detail pane offers the finer moves, in the direction of the page you're on (backpack → hold, hold → backpack):
  **"This stack"**, **"All of this kind"** (when there is more than one stack) and **"How many?"** with **1**,
  **5** and **half** — so with 20 portions of food in the hold you can take 5 along and leave the rest. All of it
  works with mouse, gamepad and touch alike. Only what fits moves; a full hold or backpack says so. These only
  work while you're aboard (in flight or standing in the landed cabin); on foot the cargo page says so.
- **Auto-stow (optional):** turn on *Settings → Comfort → "Auto-stow into cargo on boarding"* to have loose
  materials moved into the hold automatically each time you board (it says what it stowed, and nothing when
  there was nothing to stow). Off by default.
- **Sandbox: All items.** In a Sandbox world (or while an admin gave you the Creative mode) the Inventory has an
  **All items** page: every item of the game, searchable (tools first, raw materials last, A–Z in your game language
  within each group) — pick one and **Take 1** or take a full stack. Nothing has
  to be crafted.
- **Sandbox: crafting is free.** In the same mode every recipe can simply be crafted — no materials, no blueprint,
  no workbench in reach, a full stack per order — and the craft panel says so instead of "Materials missing". The
  only refusal left is a full inventory.
- **Every tool looks like itself in your hand:** the titanium and diamond drills, the mining beam, each pistol and
  blaster, the machete, vibro knife and plasma sword, the wooden pickaxe, club and spear and the advanced scanner all
  have their own model. The shock and energy gloves sit on both hands. Every blade — the plasma sword too — is swung;
  only the guns kick back when they fire.
- **Every gadget looks like itself, too:** the binoculars (the thermal ones with glowing orange lenses), the camera,
  the field medkit with its glowing cross, the stasis projector with its ring, the terrain blaster, the fluid pump,
  the terrain and weather scanners, the creature translator, the paint brush, the shaping tool, the form stencil, the
  blueprint tablet, the rail linker, the sampler, the remote control and a little toy cab or wagon for the monorail
  items — no more one emitter for all of them.
- **Tools are made of something:** the handle of a wooden tool shows its grain, metal its brushed surface, rubber
  grips, glass lenses and glowing energy parts each have their own look. In the texture editor the group *Tools in
  your hand — materials* holds these twelve surfaces; a texture pack can repaint them.
- **Everything else is a little model in your hand, too:** the speeder and the boat show a miniature of the vehicle,
  seeds, saplings, leaves, torches and lanterns show the plant or lamp as it stands in the world, ores (and silicate
  and carbon) are a small lump of their rock — each ore always the same lump, resting on your open hand — and every
  other item — materials, components, food, bait — is held as its inventory picture made solid, pixel by pixel. Other
  players see the same in your hand. Only an item without any picture leaves you with the bare hand.
- **A block looks in your hand the way it will look once placed:** a dyed block shows its colour, a glowing block its
  light colour, a block with a form (slab, stairs, sphere, your own forms …) shows that form from the side, and a
  painted block wears its picture. Beds, campfires, rugs, flower pots, ladders, staircases, stretchers and trapdoors are
  held in their placed form too. Turning with **R** still shows only in the placement preview, not in your hand.
- **Your suit hand holds everything:** in first person the hand and forearm of your suit (with your own arm painting)
  hold every item — a tool, gadget or weapon in the fist round its handle (both scanners have one), a block, vehicle or ore on the open hand, a material or
  some food at its lower edge, a plant by its stem. In third person your figure carries what it holds in front of it
  instead of letting it dangle; other players see that too.
- **Switching takes a moment to look at, never to use:** when you pick another quick-bar slot, the old item goes down
  and the new one comes up into your hand (a quiet sound plays). Scrolling quickly through the slots only takes out the
  last one. Using the item while it is coming up skips the rest of the animation — nothing ever waits for it. With
  *Reduced effects* (Settings → Comfort) the item changes at once.
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
  - **Swap** — a grid of all 36 backpack slots; pick one and it exchanges with the hotbar slot (an empty slot
    simply receives the item). *Remove from quick-bar* stows the slot into the first free backpack slot.
  - **Colour** — only for a dyeable building material: the familiar swatch palette (20 colours, among them a warm
    white and an amber for cosy room lights) recolours the **whole stack** in place. **Dye** is free; **Glow** turns the stack into coloured light sources and costs **one
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
  menu tab it powers. The Codex guide *Stations & menu tabs* has the full table.
- **Aboard a ship, stations are ship modules** (#2585). In the ship only the module counts: a station block built
  into a ship — say a forge right beside the cabin's workbench — is only decoration, and VEGA tells you once per
  block kind which module does its job. Modules are built in the **Ship** tab (research the blueprint first if it
  is still locked). Aboard, the crafting menu therefore names the module (*Refinery module*), and a station no module
  provides (the campfire, the algae tank) works on a world only. A refused craft says what is missing — the module,
  the station, or the materials with what you have and what you need (*Missing: Titanium Ore (0/2)*).
- **Crafting by hand — no station needed.** The simple things are made anywhere, straight from the crafting menu,
  without a workbench or your ship: **torches**, the **bed**, the **campfire**, the **wood crate**, the **lantern**
  (it needs a piece of glass), the **wooden door**, **ladders** (from wood or carbon), the **wooden boat** (10 logs
  + 6 plant fibre — the crafting list shows it as **Wooden boat** next to the workshop's **Boat**; both make the
  same boat), the **wooden tools** and all three **baits** (forage bait, meat bait, nectar lure). The wooden
  tools (#2536) are the **wooden pickaxe** (3 logs + 2 plant fibre + 1 stone — mines what the basic drill mines,
  only slower, and needs no energy), the **wooden club** (2 logs + 1 plant fibre — close combat, a little weaker than
  the machete) and the **wooden spear** (2 logs + 2 plant fibre + 1 stone — close combat with a longer reach, swings
  a little slower).
- The **workshop** is your everyday bench: it smelts the common ores (iron, copper, gold, silver, …) into
  ingots and fabricates parts, tools, weapons and building blocks.
- The **refinery** (the *forge* block — a workbench craft from stone and iron plates that needs **no blueprint** —
  or the refinery ship module, which is a blueprint) is the high-heat metallurgy station. It alone smelts the **rare Tier-2 metals** (titanium, cobalt, tungsten,
  platinum, uranium, neodymium) and refines diamond, carbide and reactor fuel. It also offers **higher-yield
  smelts** of the common ores (e.g. iron and copper) than the workshop — handy for bulk metalwork. You can
  never need it before you can build it: its metals are also the ones that need a titanium-tier drill to mine.
- **Every metal has a job:** each ore family (aluminium, tin, nickel, cobalt, platinum, lead, zinc, tungsten,
  lithium, neodymium, plus light alloy, biofuel and magnets) feeds at least three recipes across two stations —
  refinery variants of bronze, brass, steel, carbide, power cells and magnets out-yield the workshop ones, lithium
  triples a cell batch, and biofuel makes torches and lanterns where no tree grows.
- **Oil products (2026-09):** the refinery turns **crude oil** (pumped from the pockets under living worlds, see
  the fluid pump in §5) into **polymer** (3 per cell — cheaper than the carbon-and-sulfur road) and, with a piece of
  carbon, into **lubricant** (2 per cell). Oil is **never fuel**. Lubricant is the gate of the drill and vehicle
  tier: the diamond drill, the mining beam, the auto-drills Mk2/Mk3, the drill laser, the speeder, the clone tank,
  the fabricator, and the jump generator and asteroid breaker modules need it. Polymer now also seals the beam
  block, the sentry post and the matter sender/receiver. The starter tier (basic and titanium drill, the base core,
  the first machines) stays oil-free, so you can always reach a living world first.
- **Bio-lubricant (2026-10):** no oil in sight — or a world from before oil existed? The refinery also makes
  lubricant from **3 biofuel + 1 carbon → 1 lubricant**. Biofuel comes from berries and plant fibre (by hand) or
  from water and plant fibre in the algae tank, so this works on every world. Crude oil stays the cheaper road
  (1 oil + 1 carbon → 2).
- **The bio lab is a station without recipes:** it analyses samples, mixes them into preparations and changes
  tools and gear — what comes out is computed from what goes in. Four blueprints open it up: **Bio Lab** (a starting
  blueprint — no other one needed, 40 knowledge), then **Synthesis** and **Lab Tuning** side by side right after it,
  and **Crossing** (needs the clone tank). See § The bio lab.
- **Interior decor is craftable:** the lights, light strips, force field, medbay/lab/cargo/engine panels, engine
  nozzle, factory terminal, pipe and machine housing that ship interiors, stations and factories are built from all
  have workshop recipes (lights: crystal in a glass housing — no power needed; the force field needs the energy-door
  blueprint). Only the data cache stays loot-only.
- **Doors:** the **wooden door** (four logs, by hand) and the **hinged door** (workshop) swing by hand — stand at
  the door and press **E** (it works the door you're looking at, else the nearest; the label over the door names the
  key). The **sliding** and **energy doors** open by themselves when you walk up to them. A right-click at a closed
  door places nothing — it only reminds you how that door opens — and nothing can be built into a doorway (no block,
  no second door: *"A door is standing there."*). A door hangs along the wall beside it; flowers or grass in front
  of the doorway don't count as wall. **Every door holds air** at a base and on a station (see *Bases (Grundstein)*): a closed
  room behind any door breathes, and a door is a bulkhead — a hole in one room never empties the rooms behind it.
- The **transmuter** (the *matter forge* block or ship module, both unlocked as a blueprint) compacts spare terrain
  (sand, dirt, stone, …) into *matter dust* and synthesises it back into ore — a sink for surplus digging.
  With the **matter resynth** blueprint it also rebuilds titanium, silver and cobalt ore and lithium from dust
  plus a power cell. The endgame ores (tungsten, platinum, neodymium, uranium, diamond) stay **mining-only**.
- **Blueprints** form **chains**: most advanced nodes build on a cheaper one (the stasis projector on the field
  medkit, the beam pad on the energy door, the suit teleporter on the jump generator, …), so the Blueprints tab
  reads as a tree rather than a flat list — a node lights up once its prerequisite is researched, and it is
  never cheaper than what it builds on. Anything you had already unlocked stays unlocked. A blueprint's detail pane
  shows both directions: its **prerequisites**, and under **Unlocks** what researching it opens — the items you can
  then build, the ship modules, the blueprints it leads to, and functions such as the bio lab's Change tab.
- **Blueprints** gate advanced recipes — research them at your ship's **cockpit** (Blueprints tab; the helm counts
  while flying) with **knowledge points** (earned by scanning) plus
  research materials; some require prerequisite blueprints.
- **Tiered upgrades consume their predecessor:** where an item is a straight upgrade of another —
  oxygen tanks II/III, the advanced scanner (it takes your hand scanner), weapon upgrade chains, the AI cores — the recipe **requires
  and consumes the previous tier**, so you never end up carrying a redundant Mk1 after building the Mk2.
  The piece you are **wearing** counts too (tank II can take the worn tank I), and so does one the bio lab changed —
  the recipe card reads craftable then.
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
- **Ship scanner** (every ship, 2026-10): the **last slot of the flight hotbar** is the scanner — select it like the
  laser or the tractor beam, point the nose at something and **hold fire until the ring is full**. Four corner
  brackets lock on, an empty ring appears and the line under the target says what to hold (*Hold LMB: scan*, RB on a
  pad, FIRE on touch). Holding fills the ring, and the scan lands with a wave over the target. **Let go early and
  nothing is scanned** — the line then says *Keep holding until the ring is full* (and VEGA explains it once). While
  the ring fills, the target stays locked even if something else drifts through the reticle. Every result opens as a
  **card on the right** (about 18 s):
  - **planets and moons** — the **overview card**: gravity, weather, lava, plants, animals, machines, the terrain
    and an overall **danger** level, so you know what waits down there before you land. The same report opens from
    **Map tab → pick a body → Planet scan** and from the Ship tab, for every ship; VEGA explains the colours once;
  - **asteroids, stations, wrecks, life pods, bandits and machines** — a short readout of what it is (a wreck lists
    its contents, a machine says how dangerous it is), its threat and the knowledge it paid;
  - **anomalies** (only the scanner reads them — knowledge and a field record) and **wormholes** (where they lead).
  Every first scan pays a little **knowledge**. Out of range or while the scanner recharges, the card says so briefly.
  The hand scanner's panel at the bottom left belongs to walking around: it is hidden in flight and never shows an
  old surface scan there.
- **Scanner upgrades** — three tiers, each one replaces the one before:

  | Tier | How you get it | Reach / speed | Adds |
  |---|---|---|---|
  | 1 — Ship scanner | built into every cockpit | short, 1.2 s hold | overviews, objects, anomalies, wormholes |
  | 2 — **Deep scanner** (`planet_scanner`) | blueprint *Deep Scanner* + a **sensor lens** (workshop) | twice the reach, 0.8 s | the **ores** of every world in the system |
  | 3 — **Quantum scanner** (`quantum_scanner`) | blueprint after the Deep scanner and the radar array + a **quantum sensor** (workshop) | longest reach, 0.5 s | **rare ores** and **data caches** — and holding fire at **empty space** sweeps the whole system: for a minute the radar pins everything scannable in it |

  The ore report (tier 2+) lists the world's **ore richness** (lean / average / rich), every **ore vein** — how
  **common** it is here, from **which depth**, and whether it needs a tier-2 drill — and the extras: **oil pockets**,
  **data caches**, **rare ore lying on the surface**, **metals in deep craters**, and for a gas giant that its ore sits
  only in the floating islands. The numbers come from the very rolls the terrain is generated with, so what it
  promises is really down there. Without the Deep scanner the overview says what it would take to see the ores.
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
  via menu (Tab) → Map → *Launch into space*.
- **It flies the way you built it:** hull strength grows with the hull size, speed and handling come from
  engines versus weight — more engines fly faster, a heavy brick turns slowly. You can keep editing your
  ship on foot afterwards; the launch check re-runs every start (no engine → grounded until you add one).
- **Changed it while floating in space?** If you step inside your ship in flight and take out the engine, the
  helm or the door, or open a gap in the hull, it can no longer fly: the helm and the hatch then keep you
  **aboard** and say what is missing — put it back (or seal the gap) and the helm takes you out into space again.

### Repairing your own ship
- Combat dents your ship's **hull** (it never regenerates on its own), and hull cells lost in a wreck stay missing
  until you refill them. Cells **you** cut out on a spacewalk (to build onward) and doorways you hung a door in are
  your ship's new shape, not damage — the repair leaves them alone. Whenever your ship needs work, a **"Repair ship" / "Schiff reparieren"** panel sits on
  the right of the HUD (a hull bar plus the materials still needed and the bench that makes them, e.g.
  *Iron plate ×10 · Workbench*; it greys out and says so while you are short). To repair, stand **inside the
  ship** at the **cockpit** (the terminal at the front) or the **ship console** and press **R** — one action fixes
  hull and cells together. The panel's first line always says how from where you are: on foot ("inside the ship,
  at the cockpit or the console: press R"), at the helm of the floating ship, or in the pilot seat (**F** steps
  you inside first — R in flight is the target lock). The station prompt at the terminal names the key too, and
  pressing R anywhere else while the panel is up answers with where to go. On a **gamepad** the Actions list
  (L3) offers *Repair* at the terminal; on **touch** tap the panel's button — that button is touch-only, on the
  desktop the cursor stays locked in play. The panel refreshes on every landing and login and when you step inside
  the floating ship, so it is never stale.
- Hull is bought with **`iron_plate`** (10 hull per plate — pressed from 2 iron ingots at any **workbench**, which
  your ship's workshop module is; no factory needed); each missing cell costs the item that originally
  placed it (or `iron_plate` for structural blocks like lights/engine, so they never block a repair). The
  repair is **greedy/partial** — it fixes the hull first, then as many cells as your materials stretch to, and
  never hard-blocks when you're short. Only the **ship's owner** can repair it. There is no passive hull regen.
- **Lost ships (if "Keep ship on death" is off):** a destroyed ship becomes a **downed wreck on your landing
  pad**. Repair it through this same flow — restore the hull and refill every missing cell and it can fly again.

### VEGA — the ship AI
- **Basic training (new players):** VEGA walks you through six short parts — every lesson is *explain → try it →
  the game checks it*, one at a time, with the current task on the objective chip — **directly below VEGA's text**:
  1. **First steps** — walking and looking around, jumping and crouching, VEGA and the goal right below her text, the
     quick bar (slot 1 drill, slot 2 scanner), the menu and the Codex.
  2. **Staying alive** — health, air and food, eating, berries from bushes, the suit's ration dispenser, the blue
     ship marker on the compass (top right) and the way back: walk **40 m** away — the compass's "Ship … m" counts
     along, and VEGA says when you are far enough — then find your way back to the ship (lost? pause menu → *Back to my
     ship*), the lamp, cold and heat, and that fainting only wakes you up again.
  3. **Mining and carrying** — mining, iron ore a few blocks down, rock that is too hard for your drill, the
     backpack, gear that only works while worn (take the lamp off and put it back on), the cargo hold.
  4. **Crafting and building** — iron ingots and a plate at the ship's workshop, placing blocks, turning shaped
     blocks, a campfire.
  5. **Scanning and research** — scanning, knowledge from new things, your first blueprint at **your** cockpit, data
     fragments for the bigger ones.
  6. **Lift-off** — launching from the cockpit, flying and the chart, the ship scanner, docking at a station, trading,
     a first job from a mission board, landing on a new world, and travelling home via the map.
  At the end VEGA congratulates you and you earn the achievement **Graduate**. There are no items or knowledge for the
  lessons — they teach, they don't pay. Three **short courses** (below) then wait in your Journal.
  - **"Want me to show you the basics?"** Right after the welcome card on your very first start, VEGA asks whether you
    want the training, with two buttons in her panel: **Let's go** (press your continue key **N** — or the pad's VEGA
    button, NEXT on a tablet, or click the button) and **I know the game — skip** (click or tap that button, or **hold**
    the continue key for a moment; **Esc** never skips). Doing nothing is fine: the training simply runs. If you skip,
    VEGA tells you the training waits in your **Journal** — start it there any time (**Start basic training**). She asks
    only once, and only a brand-new player on a world whose tutorial rule is *Auto*.
  - **Cards:** each part opens with a short title card ("Basic training · Part 2 of 6 — Staying alive"; the first one
    welcomes you right after the prologue) and ends with a small "Part complete!" banner. Your first launch gets a
    "Lift-off!" title over the climb, your first landing on a new world a short look around your ship with the world's
    name, and finishing the training a card with your **Graduate** achievement. Cards never stop you — you can keep moving
    while they show; only the look around after the landing rests the controls for a few seconds — and each one plays only
    once (skip it with **Esc** or its **Skip** button; the Journal's **Watch again** has the bigger moments).
  - **No dead ends:** a lesson your world cannot offer is skipped or swapped — with **no station in your system**
    you trade with a settlement vendor instead of docking; with **space flight switched off** the flight lessons are
    skipped; without hunger there is no eating lesson; in **Creative** crafting is free and the research lesson is
    skipped; a lesson like the berries or the campfire is skipped where no bush or tree grows nearby.
  - **Help when stuck:** after about three minutes on one task VEGA gives a more concrete hint; for "where?" tasks
    (iron ore, for example) she later marks the nearest spot (for the berries already after two minutes) — only for you — with a **violet goal flag** on your planet map
    (**M**), labelled with the task, and on your **compass** ("Goal 85 m"). The flag stays until you have done or
    skipped the task (on another world it waits for your return); during the basic training she marks only the task
    shown on the objective bar. For docking she sets the nearest
    **station as your flight target**, so the radar and the arrow at the screen edge lead you there (in flight, or
    already before you launch; a target you set yourself is kept); after ten minutes she offers to skip the step in the
    Journal. Nothing is ever timed and you can never fail a lesson.
  - **Skipping and replaying** — all in the **Journal** (Tab menu → Journal → the training): **Skip step** ("I know
    this"), **Skip part**, **Start part over**, **Replay** for a part you finished, **Skip all — I know the game**,
    **Restart from the beginning** (runs it again from part 1 — for anyone, experienced players included, and without
    the opening narration) and **Switch off**. Some lessons you already did early simply tick off by themselves. **Skip
    all** puts the two flight courses (*Pilot* and *Stations, trade and jobs*) on offer in your Journal straight away,
    quietly — the other courses come when they fit. If you skipped the whole training without doing any of it, the
    restart button reads **Start basic training**; play it through and its parts count as done (with the ★).
  - **Tutorial & courses: on/off** in the Character tab switches the training and the short courses off for you (VEGA
    stops the lessons and the course offers) and back on — on an *On request* world "on" starts it. Next to it, **Open
    the Journal**.
  - **Tutorial on/off for a world:** the world rule *Tutorial* is **Auto** (every new player starts it), **Short courses
    only** (new players skip the basic training without a word — no welcome card, no lesson, no achievement — VEGA's
    opening leads straight into chapter 1, the two flight courses wait in the Journal and the other courses are offered
    when they fit; someone already in the training when the rule is switched keeps it), **On request** (only on request)
    or **Off** — chosen on the world-creation **Stories** page (§1), and changed later by the world admin under Tab →
    Character → world rules (*Tutorial*, with a line saying what the choice means) or with
    `/tutorial policy auto|courses|optin|off`.
  - VEGA's lessons — and the prompts at the ship's cockpit, workshop, cargo hold and console, the crafting
    stations, the bed, the heal tank, the transporter pad and a factory terminal — name the control of the device
    you are using: the key you bound, the pad button, or on a tablet the on-screen button (USE, MAP, JUMP, MINE,
    ◄ ►, ≡ …).
  - A **new player on someone else's world** gets the full introduction even when that world is already deep
    into the story. **Players from before** keep what they learnt: lessons from the old nine-step tutorial count
    as done, a finished old tutorial counts as a finished basic training, and a save that played before VEGA's
    lessons existed gets a one-line "systems online" instead.
  - While the basic training runs, VEGA's matching tips wait for their lesson, so you never hear the same thing
    twice (the flight chart and ship-scanner explanations, for example, come with part 6). A few one-time hints about
    things that come later wait until the training gets there, so the simple things come first: what crystal is good
    for and that ruins or wrecks are nearby wait until part 5 is done, rock too hard for your drill until part 3 — and
    then come the next time it happens (the next crystal, the next ruin, the next hard rock). Without the training
    (switched off, skipped or finished) VEGA tells them right away as before.
  - A game version from before the new tutorial still follows the nine old lessons on a new server.
- **Short courses (after the basic training — or after skipping it, and on a *Short courses only* world):** deeper
  lessons you choose yourself — VEGA offers one when it fits what you are doing (at most one offer every minute and a half, never during the basic training), and the Journal lists them
  all with **Start** or **Later** (a course you put off stays there; VEGA does not offer it again). VEGA's offer has the
  same two buttons right in her panel: **Later** (your continue key **N** — so reading on never starts a course by
  accident) and **Start now** (click or tap it, or **hold** the continue key for a moment). Each opens with a short
  "Short course" card and ends with the "Part complete!" banner; your docking in the stations course ("Docked — …") and your
  first hyperjump in the navigator course ("New system — …") get a short card of their own, your first base and your first
  own ship a short look from outside ("Your home", "Your own ship"), your first working circuit and your first tamed
  animal a banner. You can skip a step or the whole course in the Journal. Like the training they pay nothing. When a
  course fits the moment, VEGA's offer *is* her tip — she does not explain the data cache, the hard rock or the new Crystal
  Net first and then offer the course too; and while you take a course, the quick explanations it gives itself (the planet
  card, climbing, wormholes, the bio lab's tabs, the flight chart …) do not pop up a second time.
  - **Workshop and titanium** (suggested when you finish the training, and offered when your drill bounces off rock that is
    too hard): why titanium ore is out of reach at first, swapping iron ore for titanium ore at your ship's trade console
    (5 for 1), a forge (6 stone, 4 iron plates — no blueprint needed) and your first titanium plate smelted at it.
  - **Knowledge and research** (offered when a data cache is close by, or when you dig a lot without a terrain scanner):
    scanning three new things, mining a data cache (two data fragments), what a blueprint costs, a DataQube game, reading
    a rune stone, the terrain scanner (or, before you have its blueprint, what it needs) and the Achievements tab.
  - **Pilot** (suggested when you finish or skip the training; needs space flight): setting a waypoint on the chart, scanning a
    planet before you land, breaking an asteroid with the ship's laser, walking around inside your flying ship and a
    spacewalk through the hatch — plus target lock where enemies fly, and what to do when you float above a world's air.
  - **Stations, trade and jobs** (suggested when you finish or skip the training): docking at a station, the vendors and their
    trades (researchers sell data fragments), taking a job from the mission board, undocking, and handing the job in
    where you took it. Without a reachable station the course runs at a settlement.
  - **Navigator** (offered once the pilot course is done): markers and pings on the planet map, map travel between worlds,
    a forge (no blueprint needed) and lubricant, researching the Radar Array and the Jump Generator, fitting the jump
    generator, your first hyperjump — and wormholes and wrecks where your world has them.
  - **Ship builder** (offered when you could build a ship module or your hull is damaged): building a module, repairing
    the hull at the console, building onto your parked ship — and then a ship of your own: the Shipwright blueprint (it
    needs titanium plates — without the workshop course the course tells you so and waits for it), a keel, the hull with
    a helm, an engine and a door, commissioning it at the helm, and switching between your ships.
  - **Homestead** (offered when you craft or carry a base core, or out on foot at night with no base on that world): a
    base core, building in its zone, two lights, a wooden door, a sealed room that holds air (on airless worlds; elsewhere
    only explained), a closed wall ring that keeps animals out (stand inside it under the open sky — a roofed room or a
    cave does not count), and a bed to wake up in. A friend's base on the same world counts as yours when you are allies
    or in one crew.
  - **Station builder** (offered when you have a station core or its materials, or after the ship builder course; needs
    the pilot course and space flight): deploying a station core on a spacewalk, building its hull with a door as the
    airlock until it is commissioned, and floating while you build aboard (zero-g building).
  - **Crystal workshop** (offered once you have researched the comm radio): the Crystal Net blueprint, then VEGA's six
    workshop jobs from your Missions tab — a switch lights a lamp, a step plate rings a chime, a night light, an airlock
    door, a machine started by a signal, a secret wall of phase blocks. You take and hand in the jobs in the Missions tab
    as always, and they pay their usual reward; the course just walks you through them and tells you which blueprint the
    next one needs. Jobs you handed in before count at once, and VEGA does not offer the course if you did them all.
    Handing in the first one shows a short "First circuit — It works!" banner.
  - **Factory and automation** (offered near a factory, or once you have researched the fabricator): crafting in bulk at a
    factory's terminal (or, where your world has none, what one is good for), what an SPS code does (claiming a factory
    makes it your base — the course never asks you to spend a code), and a fabricator that crafts by itself on a switch —
    once you know machine circuits (the workshop's fifth job) and have a forge; until then the course waits and says so.
    Last, what machines do while you are away.
  - **Animal friends** (offered when a begging herd comes to you, or a tameable animal is near while you carry the
    creature translator): scanning an animal (its readout says how it behaves and what it loves), feeding a begging herd
    with **Feed**, taming one — its favourite food a few times, or the translator — and naming or feeding your companion
    in the Creatures tab. Your first tamed companion gets a short "Tamed — A new friend!" banner; a companion you already
    have counts at once. Not listed on a world without animals.
  - **Gardener and lab** (offered when you carry a seed or a sapling, or once the bio lab is researched): planting a seed
    or sapling, crafting at a campfire (a hot meal — or torches and water where hunger is off), a sample in your sample
    case from something you harvest, researching the bio lab and analysing a sample in it.
  - **Neighbours** (offered when a settlement is close by): talking to a settler (walk up and press **E**), answering in a
    conversation (kind answers make friends), the "People you know" list in the Character tab, and who helps with what.
    Not listed on a world without settlements.
  - **Defence** (only on a world whose rules have machines, robbers or hostile ships — a peaceful or family world never
    lists it, and it is never suggested; offered when a robber stops you or something hostile comes close): it is always
    your choice to walk away or to fend off what attacks you, what robbers want (pay, or chase them away), and — where
    hostile ships fly — chasing one away in space.
  - **On the move** (offered the first time you climb after the training, or once climbing gloves or the glider are
    researched): climbing a wall 3 m, the rope gun (or what it will do), the jetpack or the glider if you have one, a boat
    (no blueprint) or a speeder, a train where one runs (or how you build one), beam blocks and lifts.
  - **Together** (only on a world for more than one player; offered when another player is close by — an invisible
    admin never counts): chatting (with a worn radio — bigger radios reach the system or the galaxy), trading face to
    face, becoming allies or joining a crew, pinging a spot for your allies, and muting or `/report`ing someone unkind.
  - **Creative tools** (offered once the paint tool or the shaping tool is researched): painting a picture on a block,
    giving a stack a new form and a colour from the quick bar's slot actions, pasting a build copied with the blueprint
    tool, and where you change your look and find your photos.
  - **Explorer** (offered near a ruin, a revealed chest or a wreck's signal): emptying a chest or a cache, walking up to
    a buried ruin or a crashed wreck (or, where your world has none, where else chests hide), settlers' tips and data
    cubes, and a wreck in space.
  - What you have already done counts: a forge you crafted before, blueprints you own, a jump drive already fitted, a
    base on the world, a ship or station you built, a companion at your side tick their step off by themselves; lessons
    your world cannot offer (no wormholes, no wrecks, no space combat, an undamaged hull, breathable air, no hunger …)
    are skipped or only explained, and a course your world cannot offer at all is not listed.
- **Advisor:** one-time contextual hints (full inventory, first nightfall, "ruins detected", world-type
  flavour) plus **context tips** that notice your situation and may come back — rarely: gear you carry
  but aren't using (a suit lamp switched off in the dark, torches underground, food when hungry, a medkit
  when hurt, a drill in the pack while digging by hand, an idle terrain scanner), rare ore or a missing
  ingredient right in front of you, a recipe or blueprint you can afford now, a settlement / ruin /
  factory / trader / other player close by, and in space minable asteroids, a station, a low hull or a
  ready jump generator. Low oxygen/energy/hunger and temperature warnings repeat with a long cooldown.
  Every tip waits a few seconds, VEGA says at most one every couple of minutes, each repeats at most two
  or three times per save (and stops for good once you react to it). Mute via Character tab → **VEGA hints**.
  A hint VEGA keeps back while the hints are off is **not used up**: switch them on again and it still comes the
  next time the situation arises (before, a muted hint counted as told and never came back).
- **VEGA notices you** (after the basic training; the **VEGA hints** switch mutes it): she says *"Welcome back
  aboard"* when you board the ship after at least **ten minutes outside** (at most every half hour, never after
  waking up in the ship after a fall), welcomes you back when you log in after **18 hours or more** away, and
  **praises** an achievement you just earned — *"Your builds keep getting better!"*; several at once become one line
  (*"3 achievements at once!"*), at most one praise every minute and a half. Achievements handed out when you log in
  and the story's achievements stay quiet. She also passes on **news from your base** when your radio doesn't reach
  (see *People you know & NPC radio calls*).
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
- **Who shoots first:** hostiles open fire from about **70** units away; the starter laser reaches **60**. So when the
  lock frame says *Too far — fly closer*, a short dash towards the enemy is all it takes to shoot back.
- **Target lock** (**T** / **R** / right mouse button, pad **LB** / **R3**, touch **TARGET**): mark an enemy or
  any other target — a frame shows it, an arrow at the edge points to it when it is off screen, small red
  ticks point at further attackers. With Auto-aim on the laser prefers the locked target, and the centre dot
  turns **red** while the locked enemy is in your sights. Details in §3 → *Target lock*.
- **Flying together:** every hostile hunts **one** pilot — the nearest — and sticks with them for a while; it only
  switches to another pilot who comes clearly closer. VEGA warns the pilot it has spotted.
- **Finding each other:** the space radar shows every other pilot in your orbit as a **pink** blip with their name —
  flying, on a spacewalk or walking inside their parked ship. A friend beyond radar range stays on the radar's rim in
  their direction, like a station, so you can always fly over (NPC traders are not on it). You can **lock** a friend
  from anywhere in the star system (**T**) — the arrow shows the way and the distance. A friend who took off from
  another planet or moon of the system is a **dimmer pink** dot, and the lock says **Flying near …** that body: land
  there and take off again to fly in the same orbit.
- **A friend on a spacewalk** floats in their own spacesuit, with their name above them, and their ship stays where
  they left it — engines off — until they climb back in. So you can build a station side by side.

### Aiming & enemy health bars
- Damaged enemies (and the one under your crosshair) show a small **health bar** that ramps
  green → amber → red; tamed companions show a friendly cyan bar. Turn bars off under
  **Settings → Comfort → "Enemy health bars"**.
- Health bars, nameplates, the sleepers' *z z z* and the little "press to use" hints only show where you could
  actually **see** the thing: a hill, a cave wall or a house wall hides them (glass, water and leaves don't).
  Things that are *meant* to show through walls — beacons, thermal-vision contacts, ore-scan markers and other
  players' names — still do.
- When one of **your** shots lands, the crosshair flashes a **hit marker**.
- **Auto-aim** is a **world rule** (default **on**): weapons pick a target in a forward cone by
  themselves — kid- and gamepad-friendly. The world admin can turn it **off** in the in-game
  **world rules** panel (or create the world with `--auto-aim false`): then only what is actually
  **under the crosshair** (on foot) or **on the ship's boresight** (in space) can be hit — misses
  really miss. Shots are server-validated either way, including line-of-sight (no shooting through
  walls).
- **Fists are the weakest option.** With an empty hand (or a block, food or material in it) **F** throws a punch:
  **5 damage, at most one every 1.2 s** — even the starter machete does better, and every crafted weapon more so.
- **Tools dig, weapons fight.** A drill, a scanner or a gadget in your hand hits exactly like the bare hand (the same
  5 damage, the same one punch every 1.2 s, no suit energy) — only a crafted **weapon** brings its own damage, cooldown
  and reach. Want to win a fight? Switch to the machete, not the drill. No machete at hand? A **wooden club** or
  **wooden spear** is made by hand anywhere (see § Crafting) — a little weaker than the machete, but better than fists;
  the spear reaches further.
- **Companions and pets can't be attacked** — not your own, not a friend's, not a tamer's. The crosshair and
  auto-aim pass them by (they never turn the reticle red), and a swing at one is refused with a friendly note.

### Glove weapons (shock gloves and energy gloves)
Two weapons you wear on **both hands** — hold them in the hotbar like any weapon; in first person you see both
gloves. Research both in the **Weapon** category and craft them at the workshop; the energy gloves need the shock
gloves' blueprint first, but crafting them does **not** use up your shock gloves — keep both.

| | Shock gloves | Energy gloves |
|---|---|---|
| What they do | **push** creatures, robots and bandits about **5 blocks** away and leave them **dizzy for a second** | **fast punches**, left and right in turn |
| Damage · cooldown | 3 (barely hurts) · 1.2 s | 22 · 0.5 s |
| Suit energy per hit | 0.5 | 0.25 |

- **A dizzy target** (little stars over its head; a robot fizzes with sparks) neither moves nor bites, and a robot's
  or bandit's attack pauses. Right after that it can't be made dizzy again for a moment — you can still push it.
- **Who flies how far:** small animals the full distance, big ones less, a titan about a block. **Giants** (the
  colossus, sandworms, the leviathan, the sky giant) are far too big to push. **Players and companions/pets are never
  pushed.** An animal frozen by a **stasis** field stays where it is (it still gets dizzy).
- **No cheap tricks:** a push stops at walls, ship hulls, energy fences and shut doors, and never throws anything over
  a cliff, into lava or (a land animal) into water. A pushed robot stops before it would bump into another player.
- A timid animal you push runs off; an angry one comes back for you; pushing a bandit counts as saying "no" to its
  hold-up.
- With no suit energy left the gloves do nothing (recharge aboard your ship or at a heal tank). The bio lab can change
  them like any weapon — more power also pushes farther.
- Wearing **climbing gloves** too? Their pads hide under the fight gloves while you hold them.

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
- **Traders also come to you on the ground.** You don't have to fly to meet one: while you are on a planet or moon,
  a trader ship now and then comes in to land by itself — roughly every **15–25 minutes** in a busy system and
  **30–45 minutes** in a quiet one (a bit sooner while you are home at your base), and even in a system without
  any traffic about every **45–60 minutes** if you have a base there and are home. *"A trader ship is coming in to
  land — look up!"* — about 20 seconds later it sets down on the free landing pad **nearest your base** (or nearest
  you, away from home): *"Trader … has landed nearby — come and trade!"*, and the base owner gets the trader's radio
  hail (or VEGA passes it on if you wear no radio).
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
  over your start planet. Other systems may have none — if the basic training's docking lesson finds no station in
  your system, VEGA has you trade with a settlement vendor instead of sending you to a star you cannot reach yet.
  A **derelict station** (see *Mysteries* below) never counts as that station: nobody lives or trades there.
- **Every world has its own station people** (worlds created since generation 27): a station's quartermaster and
  what its extra traders sell are rolled per world, so your start station in a new world greets you with a new face.
  The first trader always sells the station's usual goods. Older worlds keep the crews you know.
- **Build your own**: deploy a **Station Core** on a spacewalk (press **B**), build a hull + an airlock door
  around it, and it commissions into a boardable station on the star map. An ally may help and even set the last
  block — the station, its default name, the "commissioned" message and the achievement stay the owner's (an owner who
  was offline hears it, and gets the achievement, the next time they join). **Docking puts you beside the station
  core** — in a sealed room with air when the station has one (the core's own room first), however far its wings
  reach.
- **Doors on your station are real doors** (2026-10): a door you build onto your station on a spacewalk hangs in
  its doorway like on the ground — you see its frame and door while aiming, it is drawn as a door from outside (an
  energy door with its blue field), it opens and closes aboard, and mining it on a spacewalk gives it back. It is the
  airlock that commissions the station. Doors that were built as blocks before stay blocks (and still count). A door
  you build **aboard** shows on the hull from outside too.
- **The inside follows the outside**: every time you board, your station's inside is brought up to date with what
  you built or took away on a spacewalk — a wall you closed from outside is there, and the room holds air, the next
  time you dock (while a friend is aboard, they see your changes at once).
- **In through the door**: on a spacewalk, float up to one of your station's doors, look at it and press **E** — you come in
  right behind that door when a sealed room lies behind it (else beside the core). Docking with the ship still
  puts you beside the core.
- **Air fills every closed room** in a station you built, however big and however many: walls, glass and doors
  are airtight. A hole in the hull means **helmet on** in that room (the suit tank takes over, and you get a
  warning) until you patch it — the rooms behind its doors keep their air. A **force-field block** seals an
  opening you want to keep. **Crew** (two civilians) moves in once you build a **trading post** or a **mission
  board** aboard — in a closed room; a post open to space stays unstaffed until you seal it (a hole elsewhere
  only takes the keeper of that room). Visiting traders still dock regardless. Windows show the planet you orbit,
  the sun and a moon. The crew keeps the **station clock**: at station night the deck lights dim and the crew
  sleeps in a bed on the station — one in the room of its post if there is one, else the nearest free bed
  anywhere aboard; a bed you build later is taken within seconds (see *Daily routines & jobs*).
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
  outer hull from outside. Drift very far away and you are set back beside the station core. **U** always returns you to
  your ship.
- **Name it**: rename a station you built from **Tab → Map** (select it → **Rename**), or by pressing **E**
  on the **station core** while standing inside your own station. Only the owner can rename it.
- **SPS relay** (late game): any commissioned player station can be converted into a relay of the old
  Scout and Pioneer Service — select it on **Tab → Map** and **deliver** the bill of materials in person
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
  modal and gated by server rules + a `docking_module`. **K** and **U** work at the helm and on a spacewalk
  too: **K** asks the nearest other pilot within 48 m (never an NPC trader), and while docked the flight view
  reads "Docked with Lena · U: undock". **Trading (T)** works while docked as well.
- **Visiting a friend's ship**: docked in space, you can **go over** into your friend's ship while it floats
  parked — your friend walks inside it or is out on a spacewalk — within 48 m: **E** at the helm ("E: go over
  to Lena's ship"), or **E** on a spacewalk ("E: board Lena's ship"). While your friend sits at the helm the
  line reads "Lena is flying", and **E** asks them to step inside (they get a message). Aboard you are a
  **guest**: the hint reads "Visiting Lena's ship · U: back to my ship"; the host's cockpit, console, cargo
  hold and medbay are just furniture for you (no prompts — they belong to your friend's ship), your own repair
  panel waits until you are back, and the Map tab's top button reads **Back to my ship** instead of taking the
  helm. **U** (or that button) takes you back into your own ship. The host sees "Tom is visiting · U: send
  home" — **U** undocks, which sends the guest home. Pad and touch players find all of it in the **Actions**
  list. The server checks everything again (docking, range, that the host left the helm) and says why when
  it refuses.
  - **A guest looks around, but does not build**: walking, doors, sitting, chat and trading with the host
    work; building, mining, painting and crates in the friend's ship — and in the space around it — do not
    ("You are a guest here"), nor the ship's stations or a bio lab. To travel or teleport, go back to your
    own ship first. The friend's cabin gives you air like your own; an open, unsealed room they built onto
    the hull does not.
  - **Back home on your own**: walking out of the friend's hatch also brings you back into your own ship,
    and your own helm (or a launch) takes you straight to your own flight — your ship floated where you left
    it the whole time (other pilots see it *parked*). You are sent home, with a message, **before the
    friend's ship moves**: when they take the helm, switch ships, leave, die or quit, or when either of you
    undocks. If your friend only steps out through the hatch for a spacewalk, you stay aboard and wait for them.
    Docking also works with a friend who is already walking inside their parked ship ("step inside, I'm coming
    over").

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
  as a soft **blue shield dome** — the **start zone** where you breathe before you have built a room. As soon as
  one closed room of your base has air, the dome fades away (every closed room breathes, see below); it comes back
  when your base has no room with air left.
- **Every closed room breathes** (#2649): rooms built at the base out of **airtight materials** — stone, metals,
  concrete, bricks, glass (natural rock counts too: dig a cave!) — get life support **as long as they are closed**,
  **no matter how many rooms and how big they are** and however far you build. **Every door holds the air** —
  wooden, hinged, sliding or energy door, open or closed — so a room behind any door breathes, and so does an upper
  floor up the stairs. Loose stuff (dirt, sand, snow, plants), geyser vents and shaped blocks (ramps, spheres,
  stairs, slabs) leak — ice seals. A cave ceiling of **sand** with the open sky above it lets the air out: build a
  layer of stone, metal, ice or glass **under** the sand (don't mine the sand — it slides down and opens a hole).
  The **base core is the air machine**: a room belongs to your base when you reach it from the core or through one
  of the doors you built at the base — even a long chain of rooms far out. A closed hut far away from every base
  core has no air. Holding any door shows a **hologram of it in the target cell**, turned by the wall beside it, so
  you see which way it will face before you place it.
- **Doors are bulkheads**: if a wall is mined or burns away, **only the room with the hole** loses its air — the
  rooms behind its doors and the core zone keep theirs. Everyone in or at that room gets a **"no longer airtight"**
  warning, and the room falls back to suit oxygen until the hole is closed (nobody who lives there moves out because
  of it). The **Energy Door** (workshop, blueprint-gated) is the bulkhead you walk straight through: its blue field
  is an **air curtain**. **Check it:** aim at your own core — the prompt shows how many cells and closed rooms
  currently have air and whether the spot you stand on does (*here: air*; when other rooms breathe but your spot
  does not, it adds *· /basewalls tells you why*), and how many residents live at the base (*Residents: 2/32*; press
  **E** there to see who they are and where). VEGA explains the rules once when you found a core on a world
  without breathable air.
- **Why no air?** Walking from air into a room that **leaks** names **where the air gets out**: the block or the
  opening, its coordinates and its height (e.g. *"the air escapes through Sand at 30/130/0, 10 blocks above you"*,
  or the hole in a wall at your height). Each reason is told at most once every five minutes. Type **`/basewalls`**
  in the chat (owners may) for the air at your spot and the reason, plus the air rules. **Allies share their air:**
  the bases of you and your allies on the same body count as one.
- **Sentry post** (workshop, blueprint-gated after the Heal Tank): a small automatic turret you place inside
  your own base zone. It fires at hostile machines, at **hostile animals**, and at robbers who have
  **already started a fight** — never at players, never at tame animals, and never at somebody walking up to
  talk (you always get to answer a hold-up yourself). It needs no ammunition, and you can build as many as
  you like. Three things worth knowing: it runs on your base's power, so it has to stand **within 8 blocks of
  your base core or within 8 blocks of a power relay chained back to it** — 8 blocks in every direction, up and
  down included (each relay feeds posts and further relays the same 8 blocks around it; a base can run a chain
  of up to **64 relays** — enough for a spoke out to the rim of a 95 × 95 spaceport and a ring all round it;
  place a post beyond all of that and the game tells you on the spot that it will not fire), it reaches
  **14 blocks**, and it only works **while you are home** on that world. Machines appear 35–50 blocks away
  from you — so a sentry is the thing that covers your back while you build, not a fence that clears the
  neighbourhood. It aims from its muzzle, so a roof or block placed **right on top of the post** does not blind
  it — only something between the muzzle and the target does. Scan a post to read its range and the zone it needs back. On **Creative** or **Peaceful**
  worlds it stays quiet, like everything else.
  A sentry's kill **counts for you**: bandit and machine bounty steps progress, a scout it finishes still
  counts towards *Guard the homestead* and the base-defended tally, and the drops land on the ground where
  the target fell. Only the plain "defeat" achievement stays yours to earn by hand.
- **Visitors at the base** (Character tab → world rules, world admin, **off by default** — on in the `dangerous`
  preset): with it on, and only while **you are home**, two bandit **scouts** occasionally walk up to the
  **edge of your zone**, stand there for about a minute, and wander off again. They **never step inside, never
  demand anything, never damage a block and never take a thing** — they look, and that is all; your sentry
  leaves them alone too, exactly as it leaves a robber who is still talking. Hit one and it fights like any
  robber. Driving two of them off at your own base is the **"Guard the homestead"** bounty (on any board,
  only offered while the option is on) and the *Not On My Doorstep* achievement. Needs **both** the
  **Bandits** slider and **Planet enemies** on, plus Survival — with hostiles off the sentry post is silent,
  so no scouts come either, and the switch is not even offered. An update never turns it on for you: on
  worlds from before the option existed it stays off until you flip it yourself.
- **Friendly visitors** (Character tab → world rules: *Friendly visitors*, world admin, **on by default** in every
  preset; or launch with `--friendly-visitors false`): about once every one and a half in-game days, while you are
  **home at your base by day** (on foot, near the core, nothing hostile around), a guest walks up to you —
  *"… is coming to visit you!"*:
  - a **neighbour** from the nearest village — the very villager you know, with the same name, face and
    friendship (while they visit you, they are not in the village). They bring a small **present** (berries; a
    lantern for a friend) — or, ask them *"Can I help you with anything?"*, one of their **wishes**. Their **"!"**
    and **"?"** come along: if you already carry what they wished for, press **E** at them and they take it right
    there — no need to walk to the village;
  - a **travelling merchant** who opens a little market at your base for a few minutes (press **E** → *Trade*);
  - a **lost traveller** who asks the way to the village and thanks you with a few torches.

  They wave hello, wait a couple of minutes (longer while you stand with them), and then walk off and are gone. Each
  guest gives its present once per player. A neighbour tells you where they are going — after their present or a wish
  they brought along: *"I'm heading home to Testville soon — come and see me there!"*, and when they leave: *"Tomas is
  walking home to Testville."* That is where you find them again (and hand over their wish). If they cannot reach
  you — you are walled in or in a sealed room — they wait at the edge of your base: *"… is waiting outside your
  door!"*. Going aboard or leaving the world sends a guest home too: they walk off (you can watch them go from the
  ship) and disappear only once nobody is near. Guests never fight, are never a target and never touch a block.
- **Residents, posts and jobs**: beds bring people, and a trading post or mission board at home is staffed by
  one of them — see *People you know* and *Daily routines & jobs* below.
- On **Tab → Map**, a world where you have a base (or a station orbiting it) is **marked** and its details note
  *"You have a base/station here"*; you can also rename the base from there.

### Alliances (shared access + no friendly fire)
- Two players can form an **alliance** from the **Alliances** menu tab. It has three views: **Allies**
  (accept/decline incoming requests, end an alliance), **Find players** (propose an alliance to any online
  player), and **Radio / Funk** (a live chat scrollback with a transmit box, the radio you wear and its reach, the
  push-to-talk key — see §7).
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
- **Multiplayer hosts:** the world rule **Starter teleporter for everyone** (Tab → Character → world rules,
  world admin; or launch with `--starter-teleporter true`) hands every player who joins without one a suit
  teleporter — switching it on also gives one to everybody online. It stays an ordinary item (unlike the
  protected starter kit) and is off by default, so singleplayer progression is unchanged.

### Back to my ship (stuck on foot)
- Lost, trapped in a cave you can't dig out of, stranded on a summit above the air? Open the pause menu (**Esc**; pad
  **Menu** → *Pause menu*) and press **Back to my ship**: you are put back aboard your ship at its heal tank — no
  device, no suit energy needed.
- It only works **on foot** (not aboard, not in space or EVA, not in a speeder or a train) and only when your **own
  ship is landed on the world you are standing on**. It refuses while you are **in a fight** (you hit something or
  something hurt you in the last 10 seconds, or a robber is still waiting for your answer) and while you are
  **falling** — land first. Hunger, cold, gas or a hard landing hurt, but they are no fight. The 10 seconds also run
  while the pause menu is open: open it, wait, and the button lights up — the paused world stops every attacker
  anyway. During a fight the button is greyed out and counts the seconds down (*fight, 7 s left*); while a robber waits
  for your answer it says so instead, without a countdown — answer the robber first. One use every
  **3 minutes**; the button shows that time too and is greyed out meanwhile (this wait pauses with the world). The
  menu stays open until you are aboard: if the trip is refused, the reason appears right under the buttons. The
  suit teleporter stays the quicker way once you have crafted it (30 s cooldown, 10 energy).
- World rule **"Back to my ship" in the pause menu** (Tab → Character → world rules, world admin; or launch with
  `--return-to-ship false`): **on by default**, **off on the `dangerous` preset** — a hard world keeps the walk back.

### Crystal Net (signals, sensors, automation)
- **What it is.** **Crystal conduits** carry a simple signal — **ON or OFF**, nothing else. Lay a line of
  conduits from a switch to a lamp and you have a circuit: everything the line touches is one **network**. While
  the signal is ON, **a glow runs along the wire, away from the switch** (or the sensor or logic block that sends
  it), so you can see where the signal comes from and where it goes; when it is OFF the line is dark. A network is
  ON as soon as any one of its **sources** says ON. Think of it as a little bit of redstone, made for kids: no
  strengths, no colours, nothing hidden — you can see the whole state by looking at the wire.
- **Getting started.** Research **Crystal Net** in the Blueprints tab (Crystal Net category, after the comm radio) and
  craft at a workshop — every part needs a **crystal**. The first blueprint gives you the conduit, the
  **switch**, the **button** and the **step plate**; the sensors (together with the **Device Eye**), the logic
  blocks, the sound devices and each machine have blueprints of their own in the same tab. VEGA's six **Crystal
  workshop** jobs (Missions tab) — or the short course of the same name in your Journal — teach the net step by step.
- **Using things.** Everything is the normal **Interact** action — press **Interact (E / X / USE)** while
  looking at the block. A **switch** flips ON/OFF and stays where you left it, even after a reload. A
  **button** gives one half-second pulse. A device with settings opens a small **menu** (pictures to pick a
  mode, or a list to pick a partner, a recipe or an animal); close it with Esc / pad (B) / its Close button.
  Only the owner and the owner's **alliance** may use a device or change its settings — anyone else sees "only the
  owner's alliance" instead of the key. **Pre-built circuits** (the light switch in a station's arrival hall, the
  lamp in a settlement flat, the puzzle in a crystal vault) belong to the world: everyone may use them, only an admin
  re-wires them. Step plates, sensors and the Device Eye need no pressing at all.
- **Who talks, who listens.** Only **sources** switch a network ON: the **switch**, the **button**, the **step
  plate**, the **sensors** and the **watcher** — and the **logic block**, the **timer block** and the **Device
  Eye**, which send their answer into the wire their arrow points to (the eye into the wire behind it).
  Everything else — lamps, doors, sirens, chimes, the machines, beam blocks, beacons — only **listens**: it does
  what its network says and never switches its own network ON, not even when it has news. Its news shows as a
  small **amber light** on top of the block, and a **Device Eye** puts that news on a wire.
- **Reading a circuit.** The glow runs away from whatever sends the signal. A small **arrow** on a logic block,
  timer block, dice block, watcher, Device Eye, piston or bridge motor shows which way it sends, looks or moves,
  and it lights up **cyan** while the block's own answer is ON.
- **Pointing up, down and around.** A block with an arrow points the way you look while placing it — look
  **steeply up or down** and it points up or down. Placed it wrong? Open its menu and press **Turn**: it steps
  through the four sides, up and down. An **amber light** on top of any other device means it reports ON right now — a
  flipped switch, a sensor that sees something, a blocked sender, a full drill, a growing clone tank. A door on a
  wire has a small lamp above the doorway: **red** = locked, **green** = held open.
- **Sources (things that say ON):**
  - **Step plate** — ON while someone stands on it. Its menu picks who counts: anyone, players, you and your
    alliance, or animals.
  - **Proximity sensor** — ON while something is near: anyone, players, you and your alliance, wild animals, tame
    animals, hostiles or settlers, at near / mid / far range (4 / 6 / 8 blocks).
  - **Environment sensor** — watches where it stands: ON when there is **no air**, it is **too hot** or **too
    cold**, a **storm** blows, the air is **toxic** — or, inside your base, when an **enemy is in the base** or
    your **alliance is at home**.
  - **Signal receiver** — repeats the signal of the **signal sender** you pick in its menu, anywhere on the same
    world, no wire in between. Give it a name: the **remote control** shows it. Use the remote while looking at a
    receiver to pair it, then anywhere on that world to switch the receiver ON and OFF.
  - **Ship sensor** (only in your own ship) — ON when the hull is damaged, the hull is low, the shield is empty,
    the ship has landed or is docked.
  - **Daylight sensor** — ON by day, or by night if you flip it in the menu. The night light for the whole
    square.
  - **Storage sensor** — reads the crate beside it: ON when it is full, when it is empty, or when it holds
    enough of its filter item.
  - **Watcher** — one pulse whenever the block in front of it changes: mined, placed, a crop that ripened. It
    looks the way **you** were looking when you placed it, so place it against the thing it should watch while
    you look at that thing; the arrow shows where it looks.
  - **Device Eye** — tells the wire what a machine is doing. Place it against a device while you look at that
    device (it looks the same way you do; the arrow shows it) and lay a conduit **behind** the eye, on your
    side: that network is ON while the device reports ON. What the devices report: a **matter sender** that
    cannot send, a **matter receiver** where something just arrived (a short pulse), an **auto-drill** whose
    crate is full or whose pit is done, a **fabricator** that is stuck, a **clone tank** that is growing (and a
    short pulse when the clone is ready), a **hydro tray** with something ripe, a **sentry post** with a
    target, a **radio beacon** whose owner is within 12 blocks, and a **beam block** someone just arrived at (a
    short pulse). It reads a switch, a sensor or a logic block in front of it too — and placed beside a doorway,
    looking into it, it is **ON while that door is open** (a lamp or a chime that tells you someone opened the
    door). It comes with the sensors' blueprint (*Crystal Senses*): 1 crystal, 1 glass and 1 circuit board at a
    workshop.
- **Logic (thinking blocks).** A **logic block** combines signals — AND (all inputs ON), OR (any), NOT (none —
  an unconnected NOT is ON, handy for an airlock) or XOR (exactly one). A **timer block** shapes a signal —
  **delay** (whatever goes in comes out exactly that many seconds later; even a short button pulse comes out as
  the same short pulse), **clock** (a tick every few seconds), **counter** (fires every Nth pulse) or **toggle**
  (each pulse flips it); its menu sets the seconds (0.5 to 10) and the count. Both **send their answer the way
  you were looking when you placed them** — out of the side facing away from you, where the arrow is — so lay
  the output wire on that side; the other sides are inputs. They never pass a signal straight through, and each
  one adds a tiny delay. A **dice block** passes a pulse only now and then — one in 2, 3, 4 or 6 (its menu) — and
  sparkles when it does: a button, a dice block and a lamp make a little lucky game.
- **Sound & words.** An **alarm siren** wails while its network is ON (three sirens to choose from; a radio
  beacon on the same network turns its map marker red) — give it an OFF switch, everyone hears it. A
  **chime** rings once per pulse (a step plate outside + a chime inside = a doorbell; on a half-second clock it
  rings every half second), a **horn** blasts once and carries farther, a **melody block** plays one note per
  pulse (eight notes, four instruments), and an **announcer** shows a short line to you and your allies: one
  of six preset lines or one you type yourself. A **display** shows its answer above itself: one **symbol** while
  ON and another while OFF (pick them in its menu), its own short **text** while ON, or a **counter** of how often
  its wire switched on (reset in its menu).
- **Playing a tune.** Several melody blocks on **one** network play together, as a chord. For a tune, give every
  note its own wire and its own moment: a **clock** into melody block A, the same clock through a timer block
  set to a **0.5-second delay** into melody block B, through a **1-second delay** into C, and so on — each
  melody block on a network of its own.
- **Things that already react** the moment a conduit lies beside them (no new blocks needed) — they only
  listen: every **lamp** (OFF = dark; mine the conduit and it lights up again), every **door** (ON = held open,
  OFF = locked — it will not open by hand; no conduit = a normal door), a **beam block** (a pulse beams whoever
  stands on it to the partner pad chosen in its menu, free of suit energy; whoever just arrived stays put for a
  few seconds, so two wired pads never throw you back and forth), a **radio beacon** (ON turns its map marker
  red), a **sentry post** (OFF = holds its fire), the **thumper** (a pulse starts its run), a **water spout**
  (OFF stops the water), an **energy gate** (ON lets animals through) and a **hydro tray** (a pulse harvests the
  crop into the crate beside it). Also a **force field** (ON = open; a whole field wall switches together), an
  **energy fence** (ON lets animals through), a **campfire** and a **forge** (they burn only while ON), a **heal
  tank** (OFF = it does not heal), a **flower pot** (a pulse harvests into the crate beside it), a **bed** and a
  **seat**. They all keep doing their normal job — a wired forge is still a forge. What they have to say — the
  beacon's "my owner is near", the sentry's "I have a target", the tray's "ripe", the beam block's "someone
  arrived", "someone heals / lies / sits here" — shows on their amber light; a Device Eye puts it on a wire.
- **Moving blocks** (they never hurt anyone and never close onto anyone — they wait):
  - **Phase block** — a wall block that turns into shimmering air while its network is ON: a secret door. Phase
    blocks side by side open together; dye them to match your wall.
  - **Trapdoor** — a hatch in the floor. ON folds it open and you drop through (the fall never hurts), OFF closes it.
  - **Bridge motor** — while ON it lays a bridge the way its arrow points, one plank at a time, 2 to 12 long (its
    menu); OFF takes it back in.
  - **Piston** — on ON it pushes up to four blocks in front of it one step; set it to **sticky** and on OFF it
    pulls the first one back. It never moves crates, devices, doors or someone else's base.
  - **Lift** — a **lift motor** at the bottom of a 3×3 shaft, a **lift stop** beside the shaft on each floor.
    Press a stop (or send it a signal) and the platform comes to that floor; a signal on the motor sends it to the
    next stop. Stand on the platform and it carries you; it waits while anything stands under it.
- **Matter link (beaming crates).** A **matter receiver** gets a name when you place it. A **matter sender**
  picks one of your (or an ally's) receivers in its menu, and every pulse takes one stack (up to 16 items)
  from the crate beside the sender and beams it into the crate beside the receiver — anywhere on the same
  world, no conduit in between, and it respects the far crate's filter. Held ON it sends a stack every two
  seconds. The sender's **amber light** turns on while it cannot send (nothing fits, no crate, no partner); the
  receiver's light flashes when something lands — a Device Eye puts either on a wire.
- **Auto-drill (a quarry in a box).** Three tiers: Mk1 digs the 5×5 square below itself 8 layers deep, one
  block every two seconds, with a tier-1 drill; Mk2 7×7, 16 layers, one block a second, tier-2 ores; Mk3 9×9,
  32 layers, two blocks a second, everything a mining beam could take. It stays where you put it, works while
  its network is ON, and drops everything into the crate beside it. Its menu chooses **only ore** (the ground
  stays standing — the default) or **everything** (a real pit). It stops in front of water and lava, never
  touches anything a player built, and pauses with its **amber light** on when the crate is full or the pit is
  done (a Device Eye on the drill and a siren behind the eye = "come and empty me").
- **Drill laser (a shaft, not a pit).** Place it on the ground with a crate beside it and switch its network
  ON: it lasers a **one-block shaft straight down** from where it stands, a block every half second, **up to 128
  deep**, and puts the **ore and the oil** it hits into the crate — the way to reach a deep oil pocket the scanner
  showed you without digging. **Only ore** (the default) vaporises the plain rock, **everything** keeps it. It stops
  for good at water or lava (in the shaft or right beside it), at bedrock, at anything a player built, when the
  crate is full or at its full depth — the **amber light** comes on (a Device Eye reads it); empty the crate and
  start it again and it continues from where it stopped, even after a reload. Two per player. Needs lubricant.
- **Fabricator (an automatic workbench).** Pick one recipe in its menu; every pulse crafts it once, taking the
  parts from the crates beside it and putting the result back into one. Put a **station block right beside it**
  and it can make that station's recipes too: next to a forge it smelts, next to a campfire it cooks. It needs the
  recipe's blueprint, like a hand craft. Every machine takes from and fills **any crate on any side** of it. Its amber light shows when it is stuck
  (parts missing, no room, you are away).
- **Caller and clone tank (planets, moons and asteroids only).** A pulse on the **caller** brings the peaceful
  animals within 24 blocks and your own companions to the block for about 20 seconds — a dinner bell for the
  herd. The **clone tank** grows a **wild** animal (never a hostile one): its menu lists exactly the species
  you may clone here — the ones you have **scanned on this world** or tamed here (a scan on another planet does
  not count). Pick one, pay **one bait** the species likes and **two matter dust** (from the crate beside the
  tank or your own pocket), wait a minute, and the animal walks out beside the tank — at once, or when its
  network is ON if you set it to release on a signal. Its amber light is on while the clone grows and flashes
  once when it is ready. Clones stay with their tank across reloads; mine the tank and they are simply free.
  Two tanks and six living clones per player, sixteen clones per world. With a **bio lab** the tank also grows
  animals from the **samples** in your sample case — from any world — and **crosses** two species into a new one:
  see § The bio lab.
- **Where it works.** On planets, moons, asteroids, **your own space stations** and **your own ship** — and only
  **while someone is on that world**: your base wakes up with you. **Machines that were running when you left
  catch up** for a while when you come back (one hour by default — a world rule, Tab → Character → world rules,
  *Machines catch up*), and VEGA tells you what they did; timers, sounds and moving blocks just start again.
- **In your ship.** Conduits, switches, buttons, step plates, proximity sensors, logic, timer and dice blocks,
  Device Eyes, the sound blocks, lamps, phase blocks, trapdoors, displays, signal senders and receivers and the
  **ship sensor** work in your own ship — and the ship's own doors follow its wires. The ship's circuits run while
  the ship is parked (landed, or with you inside out in space) and rest in flight. Everything else — drills,
  machines, bridges, pistons, lifts, the radio beacon, beam pad, sentry post, thumper, water spout, energy gate,
  hydro tray and the bio lab block — is **decoration only** in a ship: you can build it in, it just does nothing
  there, and VEGA tells you so the first time. (Aboard, the bio lab is the **Bio Lab ship module** instead — see
  § The bio lab.) On a station, build devices **aboard on the deck**, where they work; from a spacewalk the station
  takes ordinary blocks, lamps, the sentry post, the energy gate, the hydro tray and the bio lab only.
- **Limits, in plain words.** A network can hold 256 blocks, a world 64 networks and 32 sensors — and one player
  may use half of each, so nobody gets locked out — 8 sound devices can play at once, and each player gets 4
  auto-drills, 4 matter senders, 4 fabricators, 2 clone tanks, 6 living clones, 8 bridge motors, 8 pistons, 4
  lifts and 8 signal senders. In a ship: 16 networks of up to 128 blocks and 8 sensors. Place something beyond a limit and it simply does nothing — VEGA tells you, and
  mining something back frees the slot.

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
  a different factory. In the list a factory recipe is labelled **· Factory terminal**; while no terminal is in
  reach it sorts into its own group further down the list, and in the same group it comes right after its
  same-named workbench twin, so two cards like *Iron plate* read apart. Factory
  crafts **cannot be disassembled** back into their inputs.
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
  Leaves, needles and fronds drop as blocks now — every leaf kind as its own block (#2958) — so you can build and
  shape a canopy by hand — and a sapling
  planted on the dirt floor of a sealed station hall grows just like one on a planet, so an arboretum in orbit
  is a matter of dirt, saplings and headroom. A sapling you pick up stays a sapling.
- **Plants and lamps need something to stand on.** Dig away the block under a plant, a sapling, a torch or a
  lantern — or the block a hanging plant or a fruit hangs from — and it falls: a wild plant is simply gone, while a
  fruit, a sapling, a bred plant (as its seedling), a torch or a lantern lies on the ground for you to pick up. A torch or
  lantern falls with the block it is fixed to: a torch on a wall when you dig away **that wall**, a standing one when
  you dig away its floor, a lantern hanging from the ceiling when you dig away the ceiling. (Torches and lanterns placed
  before they could hang on walls stay where they are as long as a wall beside them or a block above them could hold
  them.) A picked plant whose ground you dig away does not grow back.
- **Bred plants and seedlings.** A **bio lab** raises a **seedling** from any plant sample — also from a plant you
  crossed yourself. It grows on natural plant ground, on a hydroponic tray or in a flower pot, on any world whose
  air is not corrosive: see § The bio lab.
- **You walk through tree crowns** — leaves, needles and fronds — just like through grass and ferns; only the
  trunk stops you. So you can't stand on top of a tree: you drop through the crown to the ground (or onto the
  trunk). A hedge you build from leaves is decoration, not a wall. Crowns still hide you from view.
- **What you walk through, you shoot and hit through** — leaves, grass and other small plants, torches, lanterns and
  ladders don't stop your weapons, laser or melee, even while you stand inside a tree. Walls and **glass** still do.
  It only works one way: drones, robots, animals, bandits and sentries **can't** see you through foliage, so a bush
  or a crown is still cover against them. Your drill still hits the leaves you aim at.

### Ruins & treasure chests
- **Ruins** are the collapsed remains of fallen settlements — mostly surviving ground walls, one
  half-standing tower, and rubble overgrown by flora. Unlike bases and stations they are **not protected**:
  every block is **freely mineable**, and what you clear stays cleared. VEGA may hint at *"structural
  echoes nearby — ruins or wreckage"*; bring a scanner, there's often something worth digging out.
  Only ruins hide **scavenge caches** — a village where people live has none. On an older world you may still find
  such a cache inside a lived-in village: it belongs to the villagers, so the screen says *"That belongs to the
  people of …"* instead of offering **G**, and nothing can be taken out of it.
- **Abandoned stations.** On some newer worlds you may come across the ruin of an old **monorail station** out in
  the open: the roof has partly caved in, the lamps are dark, plants push through the cracked platforms, and on the
  track bed sits an **old wagon** that sank down when the power died — broken windows, a bush growing on its roof, its
  benches still inside (you can sit on them). Beyond the hall the dead line runs on with two broken pylon stumps. Look
  for the **salvage** in the wagon and on a platform: old line parts, and sometimes salvaged **rail pylons** or a
  **stop block** — enough to start a line of your own. VEGA mentions an abandoned station when you are near one.
  Like every ruin it is not protected — mine whatever you like.
- **Field records:** ruins, wrecks, buried vaults and data terminals carry **readable texts** — logs,
  notes and plaques that surface while you scavenge them. Each opens in a reader panel, is kept in the
  Journal (*Field records*) and the Codex **Lore** chapter, and some only appear once the story has
  come far enough. Data terminals and monument relic caches may also hold a **net fragment** of the
  story itself — VEGA occasionally picks up a **fragment signal** and marks it on your map.
- **Treasure chests** are standalone lootable caches scattered away from settlements. Each is looted
  **once** and holds richer salvage than ordinary drops — and they are the main world source of a rare
  **SPS access code**.
- **Nothing hides under a frozen sea** (worlds created since generation 27): villages, camps, ruins, chests and the
  other buildings stand on dry land — and where one has to go onto a frozen sea, it stands on stilts **above** the ice,
  never on the seabed under it. A **crashed wreck** near your landing spot lies on dry land too, or on top of the ice
  (or floating at the water line) when there is no dry ground close by. Your ship may still land on thick sea ice.
- **SPS access codes** (`access_code`) are the rare item used to claim a factory as your base. You get them
  two ways, both uncommon: as loot from a **treasure chest**, or by **buying** one from a trader's **Market**
  (a steep barter recipe, in stock only every third day). Keep one if you find it — it's your key to turning a factory into a home.

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
- **Fossils** (worlds created from terrain generation 21 on): some relics are not built but *died* —
  a **sauropod skeleton** lying on its side, a **giant skull** you can walk into, a **serpent's spine**
  winding over the ground. They are made of **bone** and carry no runes: **scan the bones where they
  lie** and the find pays like reading a monument's runes (Codex → Discoveries → Monuments); a bone
  pile elsewhere is only a material.

### Mysteries: one-of-a-kind places & space surprises
- Three places exist **exactly once per galaxy**: the **Singing Shrine** (a ring of rune pillars with
  quiet keepers living around it), the **Sealed Observatory** (a glass dome of the old Service), and
  **The Long Quiet** — a named derelict drifting in one system's space, boardable like any station and
  full of salvage and its ship's manifest. Where they stand, only a **Friend**-tier NPC will tell you
  (and mark your map); the derelict shows on the star map of its system. Step off its torn corner and your suit
  floats; drift too far and you are brought back aboard.
- Space holds two friendly surprises: a **life pod** sometimes drifts through a system — a small orange capsule
  with a blinking beacon and someone waving behind the porthole (orange on the radar). **Fly close** — within
  about 14 flight units; no tractor beam is needed — and the survivor comes aboard: you rescue them (a small
  thank-you, a new person in *People you know*, and a radio call later). And an **anomaly** no catalogue
  explains — a shimmering bubble with glitching blocks around it (a teal blip that shifts colour on the radar):
  only the **ship scanner** reads it, for knowledge and a field record. It is **not a wormhole** — you cannot fly
  through it. It is labelled **Anomaly** on the radar, the chart and the target lock, and when you fly close to it
  (or press **E** next to it) a short line says so and how to scan it — once you have scanned it, that you have
  read it already. Both sit still where they were found, show on the system chart, and stay pinned at the radar's
  rim.
- **Wormholes** are rare rifts in space-time — a glowing tear with a bent, shimmering edge (violet zigzag on
  the radar). Each one is one end of a pair linking **two star systems**, and it works **both ways**: fly up to
  it and press **E** — no jump generator needed. You come out next to the other end, which takes you straight back.
  Where one leads reads **???** until you have flown through it or scanned it with the ship scanner; known pairs
  show as violet zigzags on the galaxy chart. Wormholes never lead into the story's special places. How many a
  galaxy has is a world option (**Wormholes**: off · very rare · rare (default, about one pair in a standard
  galaxy) · normal · frequent).
- Many systems also hold a **space wreck**: the travel screen lists it with its ship's name and "Wreck",
  it shows amber on the radar and on the system chart (**M**, click it to set a waypoint), and it is a
  **fly-to**, never a quick-travel destination. Coming close reads its manifest (a field record, and the
  wreck counts as visited); the **mining laser** carves it up like an asteroid for plating, cabling, a metal
  and now and then a data fragment — an EVA pick works too. Salvage pays **once**: what you carve off stays
  carved when you come back (even after a restart), and a wreck stripped to its last plate is gone for good —
  the travel screen then says *salvaged*.
- Many systems hold a **debris field** too: a cloud of wreckage — torn plating, scorched panels, now and then
  crystal — around a still-ticking **flight recorder**, listed on the travel screen with a coined name and
  "Debris field", copper on the radar and the system chart (**M**, click it to set a waypoint). It is a
  **fly-to** as well. Coming close reads the recorder (a field record and a short story of what broke here —
  a freighter's spilled cargo, a shipyard's scrap, a broken satellite, alien wreckage, an old battle); the
  **mining laser** or an EVA pick breaks the fragments for scrap metal, plating, cabling and the like (they
  drift back in on your next visit, like belt rocks), and the **tractor beam** pulls in the one or two sealed
  **salvage capsules** — energy cells, circuit boards, data fragments — which pay **once per galaxy**, like the
  wreck. Inside the field, drifting rubble **taps your shield** every few seconds while you move (never the
  hull — a ship without shields just hears the clank), so take it slow. The scorched box with the beacon in the
  middle is the recorder itself — the target lock calls it **Flight recorder** — and it cannot be salvaged; once a
  field is carved out, VEGA says so. Loot floating free after a break shows as **Loot** on the lock. How many
  systems hold a field is a world option (**Debris fields**, advanced page: off · very rare · rare · normal
  (default) · frequent).
- **Derelict stations** are wrecks of the old stations from the early days of the game — the boxy kind: a cube hub
  with a dark collar and a band of windows, four arms with a pod at each end, two big blue solar wings and a mast on
  top. Every one of them is broken: one solar wing is half torn off, the mast is snapped (its dead beacon floats
  beside it), the hull is scorched and rusty, with torn panels, holes and loose bits drifting around it — sparks fly
  now and then, and the old beacon still flickers. They are real stations, not pictures: you can walk through the hub,
  the tube arms and the pods (the hangar, an old trading corner, a medbay and quarters). Every new world has **one in
  its start system**; elsewhere a system only rarely holds one to three. They float above the flight plane behind the
  launch point, show amber on the radar and on the system chart as *name · Derelict station* (**M**, click to set a
  waypoint), and VEGA mentions one the first time you come close. (A derelict you already boarded or mined before this
  update keeps the shape it had.)
  **Dock** like at any station (fly up and press **E**, or from a spacewalk) — you arrive on the command deck in the
  hub. Aboard there is **no crew, no trading and no mission board** — nobody lives there, the old trading posts are
  broken. The **life support is dead: there is no air anywhere aboard**, not even in a room whose hull is still closed
  or one you close yourself. Your **suit tank is your air** in there — it drains the whole time, and your health drops
  once it is empty — so keep an eye on the oxygen bar and fly back to your ship in time: aboard the ship it fills up
  again. The first time you board a derelict VEGA reminds you, and a warning line says so each time you come aboard.
  If you **fall out through a hole**, you are on
  a real **spacewalk** right outside that hole (VEGA tells you): your ship waits beside the derelict (or where you left
  it floating, if you came over on a spacewalk), and your suit
  tank is your air. Float back in through a hole or a door, look at it and press **E** — you come aboard right there (by a door:
  on its inside) — or fly back to your ship. A derelict is **nobody's**: you may mine it — inside, and from outside on a spacewalk (like salvage: with a
  drill, block by block) — and patch it. What you mine stays mined (also after a restart), and the hull seen from space
  shows every hole you made. Mining a broken trading post gives you a working **Trade Post** block. Old **supply
  crates** stand against the walls: walk up and press **G** to take what the crew left behind — medpacks, rations, a
  little scrap, sometimes a field medkit and sometimes **spoiled food** (you can eat it, but it gives you a small tummy
  ache and hardly fills you up; the suit's ration dispenser won't take it). Every crate also holds one or two **old log
  entries**: notes from the people who lived there — who they were, what happened to the station and how everyone got
  out. Select one in your inventory to read it (**Read** opens it in the reader); the Codex keeps a copy under
  **Discoveries → Station logs**, so you can read it again even after you dropped the note. Each crate fills only once,
  for whoever opens it first. In one of the bigger rooms stands an old **Guardian Bay** — a broken maintenance dock
  that still glows red. About a minute after you come aboard it wakes up and sends out a **Guardian robot** or a
  **scan-drone**, then another one about **every 5 minutes** while you are aboard — never more than **three** of its
  machines at a time (VEGA warns you when the first one shows up). They fight like the machines on planets: shoot or
  hit them with any weapon, and each drops a little carbon. **Mine the bay** (any drill) and no more machines come —
  for good, also after a restart — and you get circuit boards, copper wire and a power cell out of it. The guardians
  never leave the station, and on **Peaceful** or **Creative** worlds (or with hostile machines switched off) the bay
  stays quiet. You cannot make a
  derelict your home spawn. How often systems hold one is a world option (**Derelict stations**, advanced page:
  off · very rare · rare · normal (default) · frequent; off also removes the start system's one).
- Every **asteroid** listed for a system stays a visible dot from anywhere in that system, and the green
  bearing blips on the radar's rim carry the body's name — the chart's waypoint is still the quickest way there.
- Everything in this section appears under every preset and is peaceful — nothing here fights back, except a
  derelict's guardian machines (above), which follow the hostile-machines setting like the machines on planets; the
  debris field's rubble only taps the shield.

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
  refuse or open fire and it fights. Raiders fly **real ships** — one of the known ship classes in
  near-black plating with a rust-red band, mostly the small ones, now and then a big hauler — and
  turn toward where they are going; the bigger the hull, the more hits it takes. Shoot one down and its
  ship **breaks apart**, leaving wreckage you can carve for scrap like any debris. Raiders only appear
  where the rules let you shoot back.
- The **"Bandits"** world option (world options / at creation) scales all three — Off disables them
  entirely (peaceful/family presets default to Off).

### Missions & bounties
- **Mission boards** stand in settlements and aboard space stations; vendors and board givers hand out
  gather/delivery jobs. Accepted missions are tracked in the **Tab → Missions** tab; report back to the
  giver for the reward. A short message names the mission and says what happened — **accepted**, **complete**
  (only when you really hand it in) or **posted** (your own mission) — in your language. Crystal Net jobs name
  what the circuit must do ("Circuit · a switch turns on a lamp 0/1").
- **Starter jobs:** *First Iron* waits in your Missions tab from the start. The repeatable **Cable Supply Run** is a board
  job: it is offered at any settlement or space-station mission board (stand at the board to take it), and taking it
  counts as your first job from a board. Being repeatable, it does not move the story.
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
  little later if you wandered off. Finish all four parts and the village throws a **festival** for you (see
  *A festival for you* under *People you know*). After **three turn-ins at one board** a quartermaster who knows you
  posts a two-part **big order** — a doubled delivery with the pay to match, then a large build or survey.
  Later parts can't be taken early (the server checks), and they stay where you started them.
- **VEGA's Crystal workshop:** six circuit jobs VEGA hands out herself (offered anywhere once you have researched the
  Crystal Net; each waits for the blueprint its circuit needs). When you hand one in, **VEGA** tells you right away in her
  panel which job comes next — or which blueprint it still needs — no radio needed. The short course **Crystal workshop**
  in your Journal walks you through the same six jobs (see *Short courses*).

### NPC professions: doctor, shopkeeper, arms dealer, sage, tamer, blockfarmer, streamer, reporter
- **Where you meet them.** Newly generated villages and towns may have a **clinic, shop, armory, library, stable, quarry,
  studio or newsroom** — each with its keeper. Newly generated **space stations** may have a clinic, shop, armory, archive,
  studio or newsroom (no tamer, no blockfarmer in space), and the city of **Ember Reach** has a services quarter with the
  same six. You can also get them at home: research **Profession posts** (Station tree)
  and build a **clinic post, shop counter, arms rack, sage's terminal, tamer's post, quarry post, streamer set or press desk**
  inside your base or on your own station — a resident takes the job (like the trading post).
- **Doctor:** medpacks, field medkits and — expensive — a detoxifier; a bed and a stretcher only **every other day**.
  Hurt? **Talk** to the doctor and ask *"Can you treat me?"* — they heal you to full health, **free, once per in-game
  day** (ask again the same day and they tell you to come back tomorrow; if you are already healthy, the visit is not
  used up).
- **Shopkeeper:** food — but only while you stand **inside the shop** with them.
- **Arms dealer:** cables, plates, energy cells, and finished laser pistols or plasma blasters for diamonds and gold.
- **Sage:** data fragments and AI memory fragments at a very high price.
- **Animal tamer:** baits and the creature translator; a tame animal of the planet walks at their side.
- **Blockfarmer:** quarries outside the settlement by day and sells stone, sand, dirt and basalt.
- **Streamer:** a huge fan — walks by once a day asking for a **photo together**; say yes and a photo is taken, say
  "never" and they stop asking.
- **Reporter:** gives you an **interview** — write what you have been up to (at most 300 characters; in Safe chat mode
  you pick a ready answer). Everyone can read the place's latest stories by asking the reporter "What's in the news?".
  The news also tells **what players did** around here: *"Justus chased the bandits out of their camp near
  Riverford!"* — a bandit camp cleared, a giant or the Sreekmakra defeated, someone landing on the world for the first
  time, a village helped with everything and the festival it threw. The newest four stories come first.
- **Innkeeper:** every tavern's innkeeper knows the same news — ask *"What's the news?"*, even in a village without a
  newsroom.

### People out in the wild: huts and wanderers
*(new worlds only — created with version 2026.10.17 or later)*
- **Huts.** Out in the open land, away from the villages, you may find a small **hut** with one or two people living there.
  By day they work around it, in the evening they sit by their campfire, at night they sleep in their bed (visit at night
  and you find them asleep). A hut shows up on your map (a brown log-cabin marker, *"Hut"* in the legend) once you come
  within about 40 m — or when a villager tells you about it (*"Tilda the hunter lives in a hut out in the wild …"*).
- **What they do:**
  - **Gatherer** — picks wild berries, fruit and herbs and tends a little garden. The plants grow back.
  - **Miner** — chips ore and stone from a rock face beside the hut. The rock fills up again after about 10 minutes of play
    while nobody is close by.
  - **Woodcutter** — fells a tree with an axe (you hear it come down) and always plants a sapling, which grows back into a
    tree in about 2½ minutes.
  - **Hunter** — carries a bow and hunts common, calm wild animals: a small arrow flies, the animal breaks apart into
    sparkles like when you hunt, and its herd runs away from the hunter. Hunters **never** shoot pets, companions, the
    animal you are taming, giants, animals in a base, a village, a pen or next to you.
- **Their crate.** What they gather lands in the crate in their hut. You may **look** inside, but not take, put in or sort
  anything — *"That's Tilda's — ask them!"*.
- **Talk and trade.** Press **E**: they trade their own goods (gatherer: berries, fruit, grain, fibre, seeds; miner: the
  miners' ore deals; woodcutter: logs and saplings; hunter: cooked meat and baits), tell you what they do, and share a tip —
  a wreck or a bandit camp not on your map yet, the way to the next village, and, once they know you, a hidden cache. The
  gatherer warns you about this world's poisonous plants. Once you are **friends**, they give you a small present from their
  crate — once per in-game day, never anything poisonous.
- **Protected.** You cannot dig up a hut, its yard or the miner's rock face. The trees around it and the hunting grounds are
  free; you may still pick the plants in a garden.
- **Wanderers.** Now and then by day, out in the open land — away from villages, bases and huts — you meet a **wanderer**: a gatherer, prospector, hunter or
  woodcutter on the road. They work for a while near you, stop to talk and trade when you walk up, and then move on. Every
  world has its own few wanderers, so you meet the same people again — and they remember you.

### Pioneer outposts
*(worlds created with version 2026.10.17 or later, on a planet nobody has built on yet — and only where you can breathe)*
- **What they are.** Some worlds have one or two **pioneer outposts**: a small closed base someone built all by themselves —
  walls of stone, earth, iron or steel, a door, a bed, a table, a few crates and a glowing **base core** (only decoration: it
  founds no base). When the outpost stands by the water, a little rowboat lies beside it. An outpost shows up on your map
  (*"Mira's outpost"*, *"Outpost"* in the legend) once you come within about 40 m — or when a villager or a wanderer tells
  you about it.
- **The pioneer.** One **pioneer** lives there. They look like a player with a self-painted pixel skin, but their nameplate
  says *"Pioneer"* — and they are always friendly. By day they hunt, mine or gather near the outpost, in the evening they sit
  at their table and at night they sleep. On about one day in three they are **off exploring**: then the outpost stands empty
  until the evening. If you watch, you see them walk off — and come back.
- **Talk.** Press **E**: they tell you who they are and about their outpost, and share a tip like the hut people do. Once you
  are **friends**, they give you a small present from their crates (once per in-game day) — and may let a secret slip.
- **Look, don't take.** You may open their crates to look, but not take anything — *"That's Mira's — ask them!"*. You cannot
  dig up an outpost, its cellar or its boat.
- **The cellar.** Every outpost has a cellar with a small box, down a ladder. Sometimes the ladder is behind a little door in
  a corner of the room; sometimes it is **hidden** — pick the plant in the corner planter and dig away the soil under it (that
  soil is the one block of an outpost you may dig).

### People you know & NPC radio calls
- **NPCs remember you.** Trading at a stall or taking a board job raises your standing with that vendor or
  quartermaster: **Stranger → Acquaintance → Friend**. The stage shows on their **nameplate** when you walk
  up, and everyone you know is listed under **Tab → Character → People you know** (name, role, stage, where
  they live).
- **Talk to people.** Walk up to any NPC and press **E** (away from station blocks, which keep their own E). At a
  **vendor**, E asks *Trade or talk?* — **E** once more opens the Market, **Talk** the conversation.
  The trade screen belongs to **the vendor you picked**: it shows their name, what they deal in (*Medicine and first
  aid*, *Monorail parts*, …), **their own goods first** and the two ore swaps every trader has at the end. While a
  customer stands near, a trader stays at the counter; if they still walk too far away the screen says *Step closer
  to … to trade* until you do. Traders who **visit** bring goods of their own: a trader ship docked at a station or
  landed on a pad sells something different each visit, and the **travelling merchant** at your base has a bit of
  everything — a few offers from different trades, new every day.
  Settlers chat with anyone. Vendors and quartermasters talk to newcomers too — *What do you sell?*, *Any news?* —
  and open up with favours and smalltalk once they know you. **Talk always gets an answer**: someone with nothing to
  discuss at least says hello, even right after you traded with them. Some conversations offer **choices**
  ([1]/[2]/[3] or click) — your pick is remembered and can deepen a friendship, hand you something, reveal a
  piece of the story, or make someone **call you on the radio later**. Talking itself counts toward your
  standing. A few **recurring faces** exist out there — the same person at more than one place, and they
  remember you everywhere. While you talk, the person **stands still and looks at you** (for up to a minute — then
  their day may call them away). **Esc** or *Leave* ends the conversation, and so does walking a few steps away:
  the panel closes by itself and they go back to what they were doing.
- **Friends with names.** Every villager is their own person: talking to someone counts for **them** (every
  ten minutes), the first meeting already helps a little, and a friendly hello in passing too (up to
  *Acquaintance*). Become a **Friend** of a villager and now and then — at most every half hour of play — they
  hand you a **small present** (berries, a fruit, a lantern …); with a full bag they keep it for next time. The
  stall and the board keep one standing for the place. From older saves, the person "People you know" named
  keeps the friendship, everyone else in that village starts as an *Acquaintance*, and the village itself is
  listed as "*Place* · Villagers".
- **People greet you.** Walk past someone who is standing or sitting nearby (within a few steps, in sight) and they
  **wave** and say hello — "Hello!" while you are strangers, **your name** once they know you, a big "great to see you"
  from a friend. Everyone greets you at most **once per in-game day**, and only one person every few seconds (in a busy
  tavern or on a station deck even fewer), so it never turns into a chorus. Sleepers, people walking somewhere, the
  guard on patrol and the G.D.S. guardians don't greet. A friendly vendor or quartermaster also greets you by name
  when you open their stall or board.
- **Welcome back.** Someone who knows you — or one of your own residents — and hasn't seen you for **18 hours or
  more** says so: *"You're back! It's been a while."*
- **At home.** Your own residents greet you with *"Welcome home!"* and talk about home and their job (the gardener's
  plants, the craftsman's workbench, the guard's watch) instead of treating you like a visitor; a gardener who is your
  friend may even save you the first berries.
- **Villagers talk to each other.** Stand near two people who are standing or sitting together (by day or in the
  evening — never at night) and now and then they have a little chat, turned towards each other: the rain on the
  gardens, the stars, a trader ship that just landed, bandits in the hills — and **what you did**: *"Did you hear?
  Justus chased off the bandits!"* (you hear it most often when it is about you). Everyone reads it in their own
  language. Only one conversation at a time per village or station, with quiet breaks in between — longer where many
  people stand together. Talk to one of them and they turn to you instead. Clearing a bandit camp near a village also
  makes the villagers there who already know you a little friendlier.
- **A festival for you.** Finish a village's four-part **"Settlement needs"** chain and the quartermaster (and VEGA)
  tell you: *"When evening comes, we'll throw a party for you!"* There is **no hurry** — the festival waits for you, as
  long as it takes. It starts when you are **in the village in the late afternoon or evening**: up to two dozen
  villagers gather in a **ring around the village square**, cheer for you one after another (arms up!), a little
  **bell tune** plays, and the quartermaster hands you a **present** — 6 lanterns, 2 flower pots and a gold ingot
  (with a full bag: talk to the quartermaster again once you have room). Everyone at the party likes you a bit more,
  and the village talks about it for a while. The party lasts about three minutes; then everyone goes back to what
  they were doing. Each village celebrates **once** — whoever finishes its chain later still gets the thanks and the
  present.
- **Wishes.** Villagers have **personal wishes** — for you alone (your brother or sister gets their own). A villager
  with a wish shows a gold **"!"** over their head (readable from about 30 m). Press **E** and they tell you: *bring
  me* something (5 berries, wood, stones, torches, glass …), *show me* something (a crystal, iron ore — **you keep
  it**), or *check on the animals* (scan one with your scanner). Under their words it always says exactly what you
  need: *"You need: 5× Berries"*. **"Sure, I'll help!"** puts the wish into **Tab → Missions** ("A wish from Tomas");
  **"Maybe later."** is fine too — they just chat normally until your next visit. There is **no time limit**. The
  mission says where the villager lives (*"Tomas lives in Testville"*, with **Village: Testville · 120 m** and a
  **Navigate** button while you are on that planet), and their village is marked on the **planet map (M)** with a gold
  heart (*"Wish · Testville"*, also in the marker list) and on the **compass** — until you hand the wish over. Once
  you have everything, the "!" turns into a gold **"?"** and you hear *"You have everything Tomas wished for — bring it
  to Testville!"* — walk up and press **E**: they take it (or just look at it), say thank you and give you a little
  **present**, and they like you a lot more. If you already carry everything when you say yes, they tell you right
  away (*"Oh, you already have everything with you! Just talk to me again."*) and the "?" shows at once — press **E**
  once more to hand it over. With only part of it they tell you how far you are (*"You have 3 of 5
  Berries so far — no hurry!"*). With a full bag, make some room first. A small hamlet has **one** wish at a time,
  bigger villages **two**; after you fulfil one, the next wish comes from someone else. Wishes are asked **in
  person** — the Missions tab never offers one, and its hand-in button only works next to the villager. When that
  villager **visits your base**, their "!" or "?" floats over them there, and you can hand the wish over at home.
  Nobody asks for a new wish while celebrating at a festival. A wish is a kindness, not story progress.
- **Favours for friends.** A vendor who **knows** you will, in conversation, ask for a favour — the
  three-part *"A favour for a regular"* chain (crystals → a plant survey → titanium for a trusted friend).
  Saying yes puts the job straight into **Tab → Missions**; hand it in to the **same vendor, in person**.
  "Not today" is fine — they ask again on your next visit, and the ordinary smalltalk stays available.
- **The world calls you.** People you know reach out over the radio — "☎ Name (Place)" in chat: a
  quartermaster with bandits dug in **near their settlement** points you at the bounty (clearing a camp you
  were called about earns extra gratitude), a refilled mission board gets a mention, a trader landing near
  your base hails you, raider ships sighted in your star system draw a warning, a settlement running short
  on food asks for a delivery, and a friend may pass along a **story rumour**. The most recent calls are
  also listed at the top of **Tab → Missions**, so nothing drowns in chat. Calls need a **radio you wear** in a
  module slot of your suit (comm = same world, system = same system, galaxy = anywhere), come at most every few minutes, and never
  repeat themselves. The **Settings → Comfort → NPC radio calls** option switches them to *missions only*
  or *off*.
- **News from home without a radio.** News from **your own base** never gets lost: when your radio doesn't reach (or
  you wear none), **VEGA** passes it on — a new resident moved in (only the first time), a trader ship landed near your
  base (while you are in the same star system), or your guard spotted bandit scouts. The *NPC radio calls* setting
  applies here too (*off* silences it; *missions only* still lets the guard's warning through).
- **Your base attracts life.** Trader ships prefer worlds with a founded base. Once your base holds **a bed or
  three machines** (workbench, forge, …) **anywhere inside it**, a **settler moves in** — they know you from day one
  and count toward your people, and they stay for good. **Without beds only that one settler lives there** — **every
  bed** you place inside your base brings **one more resident**, up to **32** people — as many as a city (the first
  settler's news says so too). "Inside" is the same for beds and machines, on every floor: the core zone, a
  sealed room, inside your walls, or any **closed room** (walls, a roof and a door) at the base — a bed out in the
  open field brings nobody. A **village's own** beds, shops and machines never count for your base, even when your
  walls take the village in — the villagers keep their homes; what you build there yourself counts as usual.
  **Placing a bed tells you what it did**: *"This bed counts — residents 2/32"*, that it is
  outside your base, or that your base is full; aiming at your own core shows *Residents: 2/32* too. Residents
  **take stairs, not ladders** — build a staircase to the upper floors (one-block steps; the hole in the ceiling only
  has to be open above the upper steps). **Everyone keeps their own bed and job**: a new
  bed never makes the others swap, and the newest resident waits for a free bed until the next one stands (whoever
  has waited longest gets it). Take a bed away and **exactly the one who slept in it** moves on — you get a message
  (*"Mila moved out — there was no bed left for them"*); build a bed again and the same person comes back; moving a
  bed keeps its sleeper. A hole in a bedroom wall takes the air, not the sleeper. Who sleeps where survives a reload,
  and your residents stay the same people (name, face, what they remember about you) even when you take away the core
  of another base. Mine your core and put it back on the same spot, and the same people come back. No visitor ever
  damages a block.
- **Who lives here?** Press **E** at your own base core: under the rename field a list shows **everyone who lives at
  the base** — name and job, what they are doing right now (working, resting, asleep …), **where they are** from the
  core (*"12 m to the northeast"*, or *"not here right now"*) and whether they **have a bed**. While there is room it
  also reminds you that every bed brings one more resident. A long list scrolls (mouse wheel or the scrollbar).
- **E to talk.** Standing next to someone shows *"Talk to … (E)"*.

### Daily routines & jobs
- **People keep the hours of the sun above them.** By day they are at work, in the evening they sit down on a
  **chair or bench** near home (every chair belongs to one person — when the tavern runs out of chairs, the others
  use a chair at home or spend the evening standing), and at night they walk to **their bed** and sleep in it (*"asleep"* on the
  nameplate, a soft *z z z* above them — only up close, and never through a wall). Everyone keeps their bed while it
  stands; someone without one takes the next free bed within seconds of it being built — preferably in the room
  where they work, else the nearest. Without a bed they rest where they live. Talk to a sleeper and you get
  a mumbled *"come back in the morning"*. This goes for your residents, for **villagers** (every house has a
  bed) and for **station crew** — a station keeps its own clock, and its deck lights **dim at station night**
  (the HUD clock shows it). On a modular station every crew member has a **cabin of its own** and the evening is
  spent in the **canteen or bar**.
- **They find their way.** People walk around walls, up single steps and stairs (your residents up to about
  sixteen blocks above or below where they are — two or three storeys — but never up a ladder) and **through doors**:
  sliding doors open for them like for you, and they swing a wooden or hinged door open and it **closes behind them**
  (a door you opened yourself is left as you left it). Only when there truly is no way — you walled someone in — and
  **nobody can see it** do they turn up at the other side: someone close by who could see them or the spot they are
  going to makes them wait (glass hides nothing); a floor, a ceiling or a wall between you does not.
- **Jobs come from what your base holds** — one job per resident, in this order:
  - **Trading post** (the *Trade Post* block): a resident becomes your **trader** — barter right at home.
  - **Mission board**: a resident becomes your **quartermaster** — board jobs at home (take them and hand
    them in at the board).
  - **Walls around the base** (or a sentry post): the **guard** walks the inside of the wall on the **night
    shift** and sleeps by day. Bandit scouts watching your base or a robber closing in get a warning to you
    over the radio (through VEGA if you wear no radio that reaches) and are **sent on their way** — the guard never fights; a robber who already attacks is the
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
  again) to open that vendor's **trade screen** (*you give → you get*). A vendor's themed goods only trade while that
  vendor stands right beside you (the grocer only inside his shop). Barter trades there swap your raw resources for
  goods. **Aboard your ship** the Crafting tab's **Market** page lists what the ship's trade console itself trades
  (iron ↔ titanium) — the vendors' themed goods are not offered there; its cards read *Can trade* and the button
  *Trade*. Vendors have **themes**: miners sell iron,
  copper and lead ore for silicate, traders buy crystal, gold and silver, researchers buy refined uranium and
  sell data fragments and circuit boards, settlers trade food.

### The Journal: your stories and the objective display
The Tab menu's **Journal** (it grew out of the old Story tab) keeps every story you follow — in the game they are
called **stories**:
- **On the left:** one entry per story — the **basic training** (*Training*), **VEGA's story** and any other story of
  the world — then **VEGA's notes** and your own **Notes**. A **★** marks a story you finished by playing it (a skipped
  training gets no badge), a **•** that something waits there (a short course on offer, a story you can start).
- **The training's page** shows its status, **What to do now** — the task of the running part ("Step 4 of 8: Dock at
  a station", with its counter), **Skip step** and **More in the Codex** where there is more to read, and for the part
  **Skip part** and **Start part over** — then every part with its state (*done*, *skipped*, *now*, *coming up*; short
  courses *offered* with **Start** / **Later**, *available*, or *locked* with what they still need) and **Replay** for a
  part you finished. A course with nothing left to practise reads **✔ done — nothing left to practise** and has no
  button — for example the *Crystal workshop* once every workshop job is handed in, also when you did them on the radio
  before the course came. (Pressing **Start** or **Replay** there in an older version of the game makes VEGA say "You've
  already done everything in …" instead of nothing.) At the top: **Track**, **Skip all — I know the game**, **Restart
  from the beginning** (**Start basic training** when you skipped all of it) and **Switch off** (**Switch on** brings it
  back). Below: the training's scenes to watch again.
- **A story your game does not know yet:** when a world plays a story that only a newer version of the game has, VEGA
  says so once ("This world plays a story your game doesn't know yet — update the game to follow it"), and that story's
  lines, goals and tasks read *(This entry needs a newer version of the game.)* until you update.
- **VEGA's page** shows the star-network meter (also as a bar), **Track**, **Missions in this chapter** — VEGA's story
  mission with its steps (**✔** done, **●** now, **○** next) and **Skip mission** — then **Leads in this chapter** — the
  chapter's tasks: **✔** done, **●** the one on your chip now, **○** still open (see "Story: finding the thread") — the chapter
  list with the story moments you heard, fragments, memories and field records, **Start the story now** before your story
  has started, **The story so far**, the crew's chapter on hosted worlds, and the story's scenes to watch again
  (*Prologue — Awakening* among them).
- **VEGA's notes** keep everything VEGA told you that is not a story moment: her lessons and tips, the reveal of the
  Guardian's system, her insights, the relay notes — also after rejoining.
- **Coming back after a while:** if the world's story moved on by several moments while you were away, VEGA says it once
  ("I have news for you: 3 new entries in your Journal") instead of reading every one out — they are waiting in the
  Journal, and its tab shows the badge.
- **The objective display** (the chip under VEGA's panel) names the story in front of the goal: "Training: Dock at a
  station", "VEGA: A signal on Kepler-3 b". It shows one goal — the order is: the finale while you are in the Guardian's
  system, then the story you **track** (pin one in the Journal), the basic training, VEGA's story, a short course or a
  mission, other stories. VEGA's story goal hides when you switch the **VEGA hints** off; the training, the finale and a
  mission always show.
- **Show all objectives** (Character tab, also in the pause menu's Settings under Comfort): up to three goals — one per story —
  instead of one; the extra lines hide while you type in the chat.

### Story: how VEGA's story starts
- **The prologue** is the first thing you see in a new world with VEGA's story: you wake in the pilot's seat of a small
  ship, the camera moves around it on its own, the console blinks awake — and VEGA speaks her first words in a box on the
  screen. Her last page is the call to adventure: her memory lies scattered out there, and you can help her find it.
  Turn the pages with **N**, pad **Back**, the **Continue** button or a tap; skip it with **Esc**, pad **(B)** or the
  **Skip** button. It plays once — restarting the tutorial never brings it back — and the Journal can show it again
  any time (**Watch again → Prologue — Awakening**).
- **Then the basic training**, undisturbed. VEGA's story waits for you meanwhile: whatever the world's story reveals
  while you train (also when others in a multiplayer world move it on) is kept for you.
- **The story starts** when you finish or skip the basic training — at once if the tutorial is switched off for the world
  or for you, if you have played this world's story before, or if you fly to the Guardian's system. Don't want to wait?
  Open the Journal and press **Start the story now**.
- **When it starts** you hear what was kept for you in one go: the knowledge of every story moment you missed, a short
  card **"The story so far"** with the chapters behind you (titles only — the full texts are in the Journal), then the
  chapter you are in. If the Guardian's system is already on the map, VEGA tells you where it is and that you need a
  **jump generator**; if the world's story is already finished, the ending plays for you then.
- **Chapters.** VEGA's story has nine chapters: *Awakening* (the prologue), *A Familiar Signature*, *The Service*,
  *The Guardian*, *The Verdict*, *What You Are*, *It Still Sleeps*, *The Guardian Core* and *Resolution*. The chapter
  follows the world's story — everyone on a world is in the same chapter. The Journal lists them: the chapters you have
  reached with the story moments you heard in each, the ones still ahead as **???**. From chapter 2 on, **The story so
  far** shows the recap card again.
- **Chapter scenes.** Every chapter opens on screen: **"Chapter n"** over the chapter's name with a short sound, then
  VEGA's words in her box. Several chapters are bigger scenes where the camera moves for a few seconds — *The Guardian*
  shows the sleeping Guardian core (a column of red light in an iron shell), *The Verdict* a calm view of a living world
  from space, *What You Are* is told at **your own heal tank** in your ship (away from your ship it plays as a card),
  *It Still Sleeps* drifts across VEGA's memory of the star chart to its edge, and *The Guardian Core* shows the new star
  on the star map and the way to it. Chapter 2 shows VEGA's memory of the star chart. On the **Low** and **Potato**
  quality presets the core, the view from space and the memory chart are left out — those chapters then play as cards.
  Every other story moment is a **card** — "VEGA remembers" with the moment's title, then her words — and you can keep
  moving while it shows. In multiplayer, *Many minds* shows the star map with the **other players' names** on the stars
  they are at (alone it is a card like the others). Skip any of them with **Esc**, pad **(B)** or the **Skip** button; the
  Journal keeps every text, and if you skip *The Guardian Core* before VEGA mentions the **jump generator**, she says it
  in her panel afterwards. Back after a while away? VEGA says how many moments the Journal now holds, and if the story
  has moved on a chapter, **The story so far** and the new chapter's scene play.
- **Hosted and arcade worlds:** on a world many players have passed through, a newcomer starts with their own
  chapter 1; the Journal says which chapter **the crew of this world** has reached, and **The story so far** fills you
  in whenever you like.
- On a world **without a story**, VEGA still teaches and helps — her old memory fragments (data cards you redeem aboard)
  then read as plain archive entries, with the same knowledge and the same Mk3 parts at the end.

### Story: finding the thread
- **Leads.** Once your story has started, the **objective chip** shows the current chapter's **lead** — what VEGA
  thinks is worth doing next, with the way to VEGA's **next story beat** as its counter (e.g. "(2/8)": 2 of the 8
  points until VEGA tells you more — not a number of fragments; once every beat is told, the counter disappears, and
  the Journal's story meter keeps the whole story): "VEGA: Find the net fragment on Kepler-3 b",
  "VEGA: Visit the Sealed Observatory on …", "VEGA: Read the runes of a monument on this world", "VEGA: Help a
  settlement: do a job from its board", "VEGA: Go to the heal tank aboard your ship", "VEGA: Research the blueprint:
  Radar Array", … Each chapter has one or two; the chip shows the first one you can follow from where you are, and
  when it has a place, a marker only you see points at it (a **violet goal flag** on this world's map and your compass,
  labelled with the lead and there until you follow it, or your flight target for a body in this star system; a fragment
  keeps its signal on the map). Follow it and the chip moves on to the next — the Journal
  lists the chapter's leads with **✔** for done. When nothing here fits, the chip suggests a fragment or a settlement job
  on this world, else **"Explore …"**. Leads never hold the story back: the story moments still come with the world's
  progress, whatever you do.
- **Story missions.** In each chapter VEGA also gives you a short **story mission** — right after the chapter's scene
  (never during the basic training), with a few words from her: *Follow the Signal* (find a net fragment), *The Sealed
  Observatory* (fly there and read the survey post's last log), *What the Stones Remember* (read a monument's runes, then
  help a settlement), *The Long Quiet* (board the old derelict and find her cargo manifest), *Eyes on Far Stars* and *The
  Jump Generator* (research them, then build them into your ship) and, after the finale, *Survey Orders* from a station
  board. While a mission runs, the chip shows its current step instead of the lead ("VEGA: Read the observatory's last
  log"), and the Journal lists it under **Missions in this chapter** with **Skip mission** if you would rather not. If a
  world cannot offer what a mission needs (no observatory in reach, every fragment already found …), VEGA picks another
  one. A mission never holds the story back: when the world reaches the next chapter, an unfinished mission is simply let
  go and the next chapter brings its own. The finale's chapter has none — the Guardian core is that chapter's mission.
- **The chapter bonus.** The first time anyone on the world follows one of a chapter's leads — or finishes its story
  mission — the story moves on a little (once per chapter and world, before the Guardian's system appears). In
  multiplayer everyone has their own leads and missions.
- The chip respects the **VEGA hints** setting — turn hints off and the story's chip (and its markers) clear once the
  tutorial is done. On a world without machines (planet enemies *Off*, no hostile space) it never asks you to drive
  machines back.
- **Peaceful worlds.** On a world without machines (the peaceful and family settings) the story needs the same effort as
  on one with combat — the missing machine share is made up — and there following a lead or reading a monument's runes
  brings VEGA's personal memories that machines would drop elsewhere. A peaceful world saved with an older version may
  move on one or two story moments when it is first loaded; you hear them as news in the Journal.
- **The Crystal workshop** (VEGA's circuit course) is learning, not story: its steps no longer move VEGA's story on.
- Once the Guardian's system is on the map, the chip says **"Fit a jump generator, then fly to the Core"** while your ship
  has none, then **"Confront the Guardian core"** — this one is never hidden by the hints setting. A player still in the
  tutorial keeps their current lesson on the chip and only sees the finale there while actually in the Guardian's system.
  After the win the chip suggests linking the stars with relays, then clears.
- The Journal's **Chapters** list shows the story beats you have heard under their chapter — also after rejoining a
  server. VEGA's other story lines (insights, the reveal of the Guardian's system, relay notes, her old memories) are
  re-readable under **VEGA's notes**. An entry your game version has no text for reads "needs a newer version".
- **Joining doesn't move the story.** The star system you start in is not a discovery, so a new player's
  arrival adds nothing to the world's shared story progress — only mapping systems beyond it does.
- **Joining late.** A player whose story starts after the Guardian's system has appeared on the star map hears VEGA's
  reveal (the only line that tells you a **jump generator** gets you there) once, when their story starts.
- Net fragments, personal memories and field records open in a **reader panel** and stay re-readable in
  the Journal (Read buttons) — and they survive rejoining a server.
- Scanning **runes** at a monument now also reveals their inscription. Settlement folk who know you
  (trade with them!) may share what they know — one of them keeps a page of the settler legend.
- **The finale on screen.** Warping into the Guardian's system shows **"Arrival — The Guardian Core"** as the jump
  clears. Landing on the Guardian Core, the camera flies over the **iron-ringed shaft** while VEGA tells you to walk
  there instead of digging (the same words stay in her panel if you skip early), then shows what sleeps below. When your
  hack breaks the core open, the camera moves close to the red column — **"The core wakes"** — and the **argument duel**
  begins.
- **The duel.** The core speaks in its own red box at the top ("Argument 2 of 4"); your answers are **big buttons** below
  it — click them, tap them, or pick one with the pad's stick and **(A)**. The number keys no longer answer (they are
  your hotbar). A wrong answer is never lost: the core replies (in italics) and asks again. In singleplayer the world
  waits while the core does. **Esc**, pad **(B)** or **Step back** leaves the duel waiting; walk back to the core and press
  the **use** key (**E**, the **USE** button) to face it again. Only players **on the Guardian Core** can answer — the
  others see a short banner that the duel has begun. In multiplayer the duel steps aside if you take damage and comes
  back once it is safe.
- **Finishing the story is a moment now**: winning the duel shows the machines **standing down** — VEGA's words over the
  sleeping core — and then the **ending**: the resolution, the credits roll, and an epilogue that opens the door to what
  comes after. It's skippable (**Esc**, pad **(B)** or the **Skip** button), it plays its own music every time you watch
  it, and the Journal keeps a **"Watch the ending again"** button once you've earned it — it plays the stand-down and the
  ending again (so they are not listed a second time under **Watch again**). A player who had heard
  the story to its last chapter but was offline for the win gets the stand-down and the ending — and the finale
  achievement — once on their next join; someone who first arrives on an already finished world is not played the ending
  at the door: it plays when their own story starts after the basic training (on hosted and arcade worlds it stays in
  the Journal).
- **When the Guardian falls, every world goes quiet** — the machines on all worlds players are on stand down at once.
- The hint at the Guardian core ("walk to the marked shaft, don't dig here") shows even with **VEGA hints** off.
- **Story scenes.** Big story moments can play as short scenes: a **banner** at the top (it never stops you), a
  **card** with a title or VEGA speaking in a box (you can keep moving — only the world behind it darkens a little; your
  HUD, crosshair and VEGA's objective stay bright), or a **staged scene** where the camera shows your
  ship, the sky, a view from space or a small memory scene, and the controls rest for a moment (in singleplayer the world
  waits meanwhile). A scene never starts in
  a fight, while you fall, swim, drive, fly or have a menu open — it waits for a calm moment. Taking damage ends it, and
  its text goes back to VEGA's panel ("Story moment saved"). In multiplayer the camera comes back after 30 seconds.
  A few moments only make sense right then ("Lift-off!", arriving at the Guardian system): if danger is near, they show
  just their title as a short banner at the top. During "Lift-off!" the flight HUD steps aside for a few seconds — you
  keep the controls.
  - **Sound:** while VEGA's box types she "speaks" with the same robot babble as in her side panel (no words — it follows
    the **Master volume** and **Effects volume** sliders). The narration at the very start stays silent. While a scene plays, rain,
    wind and other background sounds get quieter (a little under a card, more under a staged scene) and come back after it.
  - **Skip** a scene with **Esc**, the pad's **(B)** or the **Skip** button (on a card, where you can move, Esc or the
    button). Turn the text pages with **N**, pad **Back**, the **Continue** button or a tap on the text — once the last
    page is read, the scene moves on about a second later.
  - **Watch again:** the Journal lists every scene you have seen under **Watch again** — press **Watch**, close the
    menu, and it plays again (nothing in the game changes). A scene with its own camera waits until you have landed if you
    press **Watch** while flying; a short banner says **"Plays when you land."**
- **After the ending the galaxy is calmer, not empty.** The Guardian's machines don't vanish for good: the
  hunter robots are gone and only a thinned-out remnant of scan-drones keeps roaming (half as many, spawning
  half as often), space stays hostile only where it is dangerous by nature — **pirate havens** and the
  opt-in *Frontier danger* tier — and raiders keep their havens, so the station **raider bounty** can still
  be earned. Worlds with planet enemies *Off* (the family presets) stay peaceful as before.

### Scanning & knowledge
- **Which scanner does what:** the **hand scanner** (starter kit, quick-bar **2**) identifies the one thing you aim
  at — what a block yields and which tool it needs, a plant's species, a creature's traits; it finds no ore. The
  **terrain scanner** (below) makes ore near you glow through the rock; the **ship scanner** (§ Ship, modules) gives
  you an overview of a whole world from orbit — and, upgraded to the Deep scanner, which ores it holds.
- With a scanner selected, **left-click** a creature or block to scan it. Scans award **knowledge points**
  used to unlock blueprints; the readout shows subject/info/threat/knowledge (first-time scans highlight
  the "new discovery" bonus).
- **Plants and trees scan as named species.** A scanned plant (flora) or tree reads as this world's coined
  species name with an **edible/toxic** classification, not just a block. A tree's trunk and its leaves are
  the same species, so scanning either one counts as a single discovery. On worlds created from 2026-10-20 on, the
  leaves of a wood's other tree kinds (and the birch's and bottle tree's pale trunks) carry names of their own, while
  the common brown trunk keeps the common tree's name; edible or toxic is the same for every tree of a world, and so is
  their fruit.
- **What it carries.** A scanned plant, tree or creature also gets a line about its **substance**: unknown until
  you have analysed a sample of that species in a **bio lab** (§ The bio lab) — afterwards the scan says so.
- **Micro-fauna scans too.** Stand near a butterfly, firefly, wisp or any other ambient critter with the
  scanner selected and left-click (when no larger creature is in reach): the kind enters the Codex's
  **Micro-fauna** discoveries chapter and awards a little knowledge. Thermal vision (see §5 → Binoculars)
  also picks critters up as small named contacts.
- **Terrain scanner** (`terrain_scanner`, workshop recipe + blueprint): a **right-click** gadget that
  pulses once (10 suit energy, 10 s cooldown) and reveals ores, crystal, data caches — and **oil** — within
  20 blocks as through-wall glow markers for 8 s, tinted by ore type; the toast names the finds, most common first
  ("12 deposits — iron ore ×8 · copper ore ×3 · …"). An `ai_core_mk2` extends the radius. Digging a lot without
  one, VEGA points you to its blueprint once. **Oil echo (2026-10):** on a living world every pulse also listens
  for oil — the nearest oil pocket within **800 m** gets a **ping** on your compass and map, a violet marker, and
  a second toast tells you how far away and how many blocks down it lies ("Oil echo: a pocket about 420 m away,
  about 70 blocks down"). A pocket pumped dry at its heart no longer answers; without a pocket in range the
  toast says so.
- **Fluid pump** (`fluid_pump`, workshop recipe + the `fluid_pump` blueprint after the titanium drill): a
  **right-click** gadget that pulls **one cell of liquid** into your pack (2 suit energy, 0.4 s): **oil**,
  **water** or **lava**. **Oil** (new worlds since 2026-09) lies in **sealed, tar-rimmed pockets 40–120 blocks
  under living worlds** — worlds with plants *and* animals; the terrain scanner's **oil echo** points you to the
  nearest one. On worlds created from 2026-10 on, about **one pocket in three seeps**: a black **tar patch** on the
  ground with a small **oil puddle** in the middle, and under it a column of soft tar straight down to the pocket —
  dig along the black trail. From then on the pockets are also taller (up to ten cells of oil). It is a **deposit**:
  what you pump is gone, the pocket never refills and never floods a cave; a drill cannot mine it. Water and
  lava flow back as they do for a tier-3 drill — but still water in a pond or a flooded room above the sea level
  only fills the hole from its top, so every cell you pump out is gone for good: the pump empties a flooded
  room cell by cell. Oil **burns** (a torch or lava sets a pocket alight), and the
  **refinery** turns it into polymer (3 per cell) — see §crafting. You can place oil again; you sink into it
  and cannot breathe in it, like water.

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
- **VEGA's goal flag:** when VEGA marks the goal of a story task you are stuck on, it shows as a **violet flag**
  labelled with the task (also listed with its distance under *Points of interest*) and on your compass. Only you
  see it, and it goes away by itself once the task is done or skipped.

### Travel & the star map
- Open **Tab → Map**. The system list is grouped: **Current system** at the top (its reachable worlds, plus
  the **Launch into space / Leave space** button), then **Hyperspace** for the other systems. Selecting a
  system you've visited shows its worlds and a small chart of the system on the right — the real layout, like the
  flight chart (**M** in space): the star in the middle, every planet and asteroid on its orbit ring where it really
  is, moons and stations right beside their planet, a belt as one faint band. The world you are on is the bright,
  larger dot; the one you picked has a white ring.
- **Quick-travel** ("Travel" / "Hyperjump" on a world) is gated by the **Instant Travel** world option
  (Character tab, world admin):
  - **Off (default):** you can only quick-travel to worlds you've already **landed on manually**. To reach a
    new world, **launch into space and fly there**, then land (pick a pad). A never-visited star system shows
    only as a single **"Hyperjump to this system"** entry — jumping there drops you into its flight space, and
    you fly to its worlds and land. Once you've been somewhere, quick-travel to it works from then on. Every
    other system keeps its **"Hyperjump to this system"** entry above its worlds, so a system you have jumped
    into but never landed in stays reachable — a locked world in another system offers the same jump in its
    detail pane (#1638).
  - **On:** quick-travel works for any world immediately, except for unvisited planets in another system (which always require landing there first).
- **Travelling from a parked ship plays the whole trip** (#1614): the ship **takes off**, then — for a world in
  another system — the **hyperspace warp** carries you there, and the ship **lands** on the world you picked.
  You don't steer; the controls come back once you stand on the new world. **"Hyperjump to this system"** from a
  parked ship takes off first too, then warps and drops you into the new system **in flight**. A jump you start
  while already flying (system chart, Hyperspace tab) still warps straight away.
- Jumping to **another star system** requires a fitted **`jump_generator`** module — or a **wormhole**, if the
  system has one (§ Mysteries).
- A star system you have **never entered** shows as **"Unknown system"** — its name is part of what you
  discover. A fitted **`radar_array`** module decodes the beacon signals and reveals all system names.
- **Your own names** (#2952): you are the explorer, so a **planet or moon you have landed on** and a **star system
  you have been to** can get a name of your own. On **Tab → Map**, select it and type into **"Your own name — only you
  see it"**, then **Name it**; an empty field gives it its real name back. Only **you** see the name — on the map, the
  flight chart, the radar, the scanner, the HUD's location line and the loading screen — and the **real name** stays
  in small type beside or under it, so you can still tell a friend where to meet. Your friends keep the real name and
  can give the place a name of their own. Stations keep the name their owner gives them for everyone.
- Your **first landing on a world** records it in the Codex under **Discoveries → Places** and pays
  **+5 knowledge** — exploring the galaxy is itself a way to learn. (Your starting world gets its Places
  entry too, but pays nothing — home isn't a discovery.)
- **Space stations** appear in the world list too (yours show their owner; others show *"Station of …"*).
  Selecting one offers **Board** — but only if you've **docked there at least once** before (just like landing
  gates worlds); a never-visited station shows *"visit it once to unlock"*. Boarding takes you straight inside.

### The frontier — why flying far pays
- Systems far from your home system carry a **"Frontier"** tag on the star map (shown even before you know
  their name — it's the reason to go). Out there worlds generate **richer rare-ore veins** (titanium, cobalt,
  uranium, platinum, tungsten, neodymium, diamond — everything the late blueprints want), roll an **extra
  buried vault and monument**, and structure caches carry a bonus late-game find. Your starter ores (iron,
  copper, …) are the same everywhere — the frontier is the better place to *return to*, never the better
  place to begin.
- **Frontier danger** (Character tab → world rules, world admin, off by default): when switched on, the machines
  out in the frontier hit like the toughest world settings — richness and risk scale together. On
  peaceful/family worlds there are no machines at all, so those stay "richer, never more dangerous". The
  `dangerous` preset ships with it on.
- **Visitors at the base** (Character tab → world rules, world admin, off by default): bandit scouts look at a
  founded base from its edge while the owner is home — never inside, never destructive; see § Bases. Gated
  on the **Bandits** slider (no robbers, no scouts); the `dangerous` preset ships with it on.
- **Friendly visitors** (Character tab → world rules, world admin, **on** by default in every preset): neighbours,
  travelling merchants and lost travellers now and then visit your base while you are home; see § Bases.
- **Growing galaxy** (world creation → Universe size → **"Growing"**): the galaxy starts at the normal 12
  systems — but every time someone hyperjumps into one of the current **outermost** systems, deep-space
  telescopes report a **brand-new system beyond it**. The galaxy literally grows at your frontier, up to a
  generous cap ("the frontier is quiet"). Growth is permanent: new systems survive save/reload like any
  others. Worlds created with a fixed size never change.

### Day/night & weather
- **The world wraps east–west** — the X axis is a longitude, so walking continuously east (or west) brings
  you back to where you started, as if the planet were round. The seam is invisible (terrain, biomes, caves
  and structures line up exactly). North/south (latitude) does not wrap.
- **Every body has its own size** — asteroids are a quick stroll around, moons small, planets large. In worlds created
  since generation 26 the planets of one system really differ in size (and two worlds differ too), and neighbouring
  planets wear more different sky, plant and water colours; older worlds keep the sizes and colours they always had.
- **Day/night is by location** — because X is a longitude, a planet has a real day/night terminator: one
  player can be in daylight while another, far away, is in night, and one lap around the world is one day.
  The clock still advances. Animals and people keep the hours of the sun above **them**: on the far side of the
  planet the day-active animals are asleep while yours are grazing.
- **Weather comes in episodes** — it swells, holds and fades rather than switching on and off, and every
  world has its own temper: some flip between squalls, others brood under one sky for minutes. Storms
  build through the afternoon, mist gathers around dawn, and a slow wet/dry season rides on top.
- **Weather is per position** — a stormy biome can rain while a neighbouring clear one stays sunny,
  mountain tops sit in cloud and snow while the valley below is clear, and **fronts drift across the
  world**, so you can watch a storm arrive and move on. Weather is hidden in caves/underground — but not silent:
  under any roof (aboard your ship, in a house, in a cave) you still hear the rain, the wind and the thunder,
  muffled and quieter. Admins can override time/weather (see §7).
- **Nights have their own sound** — after dusk a quiet layer that fits the world: crickets where it is green and
  mild, frogs by the water on warm humid nights, a soft wind on cold or barren worlds, a low hum among fungus, soft
  chimes among crystal; lava, ash, poisoned and airless worlds stay silent, and rain or storms take over. A lit
  campfire crackles, and your ship's hatch closes behind you with a soft pressure hiss.
- **Beyond rain and snow** — drizzle, sleet, hail, ground fog and whiteout fog, gales, blizzards,
  heatwaves, and the genuinely alien: **acid rain** on toxic worlds, **ember fall** on volcanic ones,
  **spore blooms** in jungles and swamps, and **ion storms** and **meteor showers** that even airless
  moons and asteroids get — those used to have no weather at all.
- **Weather has consequences** — corrosive and falling weather drains your suit out in the open, so a
  roof is a real answer; rain waters planted flora so it regrows faster; scanners lose range in blown
  grit and charged air; animals hunker down in violent weather; snow settles on the ground and melts
  again when it warms up. Rain also wets the open ground (darker, glossy, drying over minutes) and snowfall caps the
  upward faces — see *Atmosphere effects* under §4.
- **…and opportunities** — an **ion storm charges an exposed suit**, a **spore bloom** fattens what you
  harvest. Sometimes the right move is to walk into the bad weather. Craft the **weather scanner** to
  read what is coming before you set out.
- **Weather from space** — the planets of the flight view are the worlds you land on: their real ground
  (each region's own soil, snow and ice on cold peaks and seas, the world's own water and plant colours) and
  their **live weather**. A storm reads as a dark cloud mass over the region where it rages (lightning flickers in
  it on the planet you are close to), a front as a band of cloud, a blizzard white, acid rain sickly green, ash
  dark; an ion storm makes the atmosphere's rim shimmer. The lit side of a planet is its real day side. And it is
  the **same weather you land in**: every planet keeps its weather running while you fly, it does not start fresh
  when you arrive.
- **Weather on the maps** — the **landing map** shows it per pad (see *Space-flight controls*, **L**), the
  **system chart** (M in flight) and the travel screen's system view show a glyph per planet, and the **planet map**
  (M on foot) has a **weather layer** (☁ button, remembered) with the fronts crossing your area, plus the weather
  where you stand.
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
  tundra, an ice world, a snow-capped mountain) where every other plant gives up — and, on newer worlds, frostberries.
- **Mixed meadows** (worlds created from 2026-10-19 on, terrain generation 28): twelve more wild plants — the
  **redberry bush**, **blossom bush**, **feather bush**, **weeping shrub**, **needle bush**, **frostberry**, **clover**,
  **daisy**, **thistle**, **wild herb**, **foxtail grass** and **lupine** — and a meadow is no longer one kind of plant:
  several species grow side by side, while a glade still has a plant that dominates it. The two berry plants give
  berries (toxic on a world whose species is toxic — scan before you eat), the others plant fiber. Older worlds keep
  the plants they always had.
- The glowing plants' colour (and every other wild plant's) is this world's own; harvested cave plants regrow on
  their rock like any other flora.
- **Every planet's plants have their own shape, too:** the leaves of the trees, the conifers' needles, the palm
  fronds, bushes, plants, tall grass, ferns and flowers each come in several looks (broad or feathery leaves, round or
  spiky bushes, daisies, tulips or poppies …), and each planet grows one of them. On every world, old saves included.
- **Blossoms and berries are colourful:** flowers, berry bushes, orchids, lilies, mushrooms and the other flowering
  plants show their blossoms, berries and caps in a colour of their own, apart from the leaves — and plants of the same
  kind mix two or three colours across a meadow. The grass tufts on the ground take the planet's grass colour.
- **Many more trees** (worlds created from 2026-10-20 on, terrain generation 29): ten new tree shapes — the **birch**
  with its pale bark, the wide **oak**, the slim **column tree**, the flat **umbrella tree** of the savanna, the
  **blossom tree** in pink, white, magenta or violet, the **tree fern**, the fat **bottle tree**, the alien **spiral
  tree**, the round **ball tree** — and the **lantern tree** (see below). A wood is no longer one kind of tree: one kind
  leads, others grow in between, and even trees of one kind differ (crooked trunks, lumpy crowns, twin tops). On one
  planet the kinds wear **different leaves in different colours** (up to three leaf kinds plus the blossoms), and the
  scanner names each leaf kind on its own (edible or toxic is the same for all trees of a world). The
  jungle planet finally has its tall jungle trees, deserts grow palms, saguaros (now a real cactus) and bottle trees
  instead of mostly dead snags, fungal worlds grow mushroom trees and crystal worlds the odd crystal tree. Older worlds
  keep the trees they always had.
- **Bark and leaves vary on every world** (old saves included): trunks come in several barks (rough, smooth, striped,
  shaggy, plated), the leaves of a planet's trees are picked from a bigger set of shapes, and neighbouring groves differ
  gently in colour instead of every tree being the same copy.
- **Every tree gives you its own wood and leaves** (#2958): the pale trunk of a birch or bottle tree drops a **pale wood
  log**, the second and third leaf kind of a planet drop **leaves (kind 2)** and **leaves (kind 3)**, a blossom tree
  **blossom leaves** — placed again, they look like the tree you cut (a leaf kind takes the look of that kind on the
  planet you place it on). Every recipe that asks for wood takes **any log**: the crafting card lists it as **Logs (any
  kind)** and counts all of them, and your common logs are used up first, so the pale ones you cut to build with stay.
  The same goes for a blueprint's research cost and for a villager who wants firewood. The giant tree's trunk still
  gives two common logs.
- **Not every planet glows** (terrain generation 29): on about four in ten planets no glowing plant grows on the surface,
  on three in ten you find one glowing kind, on two in ten a few — and one in ten is a **luminous world** full of glowing
  plants, where **lantern trees** hang their glowing golden fruit (always edible, it grows back after two minutes).
  Caves keep their glowing moss and threads on every world. The prism bloom still grows in every cave, but its clusters on
  the surface only on planets that glow.
- **Glowing plants glow at night** (every world): by day their glow is faint, at dusk it rises, at night and in caves it
  is full. Frost flowers no longer glow, and the light a glowing plant throws has the plant's own colour on that planet.

### Creatures
- **Every planet's animals look like their own species:** their hide, their build, a coat pattern in a second colour
  (stripes, spots, rosettes, a giraffe's net, a dalmatian's dots, ladybird dots, a saddle, an eye mask, tail rings,
  socks, a dark back fading toward the belly …), sometimes a different hide on the head or the legs, and every animal of
  a herd a touch lighter or darker than the next, with its spots in other places. Some wear bold marks: black and white
  like a zebra, or warning colours like a wasp. On every world, old saves included.
- **Every planet has its own kind of animals:** on cold worlds most animals wear fur, wool or down, in deserts scales
  and leather, in swamps slimy and scaly skins, on crystal worlds crystal and armour — and every world also has a
  favourite of its own, so two desert worlds still look different. A pet or an animal from the bio lab keeps the look of
  the world it comes from.
- **Glowing animals shine in a pattern:** rows of light spots, freckles, veins, rings, a line along the flanks, patches,
  or glowing fins and crests — best seen at night and in caves.
- **Eyes and horns differ, too:** round eyes, cat slits, goat bars, insect eyes, big cute button eyes and more; horns
  in ivory, bone, black, amber or slate, ridged, grained, twisted or crystal.
- Fauna spawn near players (habitat-gated), with temperaments; hostile creatures show visible attacks.
  Flora regrows when its host block survives. Nothing spawns inside a **sealed room** of a founded base
  (the same airtight rooms the base core supplies with air), no single species fills the whole area, and
  an animal you wall or floor in while it sleeps wakes up and steps clear.
- **Animals and your base.** No ground or cave animal spawns within 24 blocks of a base core. Farther out, wild
  animals stay out of a base's sealed rooms and of a **closed ring of walls** (at least 2 blocks high, closed all
  round at feet level) as far out as you have built around the core — at least 48 blocks, at most 192. **No ground
  animal ever spawns on a floor you built** (any solid block you placed, base or no base — water you poured and plants
  that grew back don't count). A wild animal that gets in
  anyway — through an open gate or a gap, or before a far wall had loaded — **leaves again while nobody is looking
  at it**: one standing on your floor or inside your walled yard or sealed rooms simply isn't there any more when
  you turn back. That includes wild animals in a pen on your base floor: they wander off when nobody looks — tame
  them to keep them. Your pets, bio-lab clones and an animal you are busy with (fighting, feeding, calling with the
  caller, taming it) always stay. Fliers and cave dwellers are not stopped by walls or floors, and fish and other
  water or lava animals are never sent away.
- **Every animal moves the way its body says.** Walkers jump one-block ledges (like you) and are stopped by
  two; crawlers (legless, serpentine or many-legged things) haul over one block and never jump; giants
  step but never jump; fliers land to rest and to sleep and take off again when disturbed — a skittish bird
  flushes when you get close; gas sacs and medusae just hover. Everything on the ground falls if you dig
  its floor away. The scan readout names the type ("Walks and jumps", "Crawls", "Flies, perches to rest"…).
  Big animals keep their **whole body** out of walls: a big walker stops with its head in front of a wall (hills and
  stairs are fine), and a sky ray glides up over a roof next to it — or turns away from a high wall — instead of
  pushing its wings in. **Only animals that fly sit on tree crowns**; a land animal never climbs into one, and one
  that got up there drops back to the ground.
- **Animals and water.** Land animals wade through water **one block** deep; deeper water is a wall to them —
  and the kelp, seagrass and coral on a pond's floor count as the pond, so a land animal never walks along a lake
  bed between the plants, and none ever appears on one. Flying animals keep their height above a pond's
  **surface**: they never sink into the water or its floor, and they appear at their flying height over it. Fish
  swim in among the water plants; amphibians go in and out as before.
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
- **Favourite food + taming by feeding** (worlds created from terrain generation 16 on): every begging species **loves
  one food** — usually a fruit that grows on that world's trees, otherwise berries. The scan shows it ("Loves:
  Banana") and a herd smells it from a little further away. Throw a herd **its favourite food** with **Feed** (**Q**)
  **twice** and the animal that eats the second piece **stays with you as a companion** — no translator needed (VEGA
  explains it after the first piece). After a favourite snack the herd comes right back for more instead of losing
  interest. The pieces count for the whole herd, whichever animal wins the squabble; other food doesn't count, and
  poisonous food never does (wash a toxic fruit at the **detoxifier** first).
- **Bipeds** (worlds created from terrain generation 16 on): small **upright two-leggers with two arms and a big
  head**, from knee-high to child-high. They are **always peaceful** (some are shy), never alone, and many of them beg:
  they come running with **both arms up** and chatter something that sounds like **"meins, meins!"** If you hurt one,
  **the whole herd runs away** and wants no food for a while. The scan says "Walks upright on two legs".
- **Worms** (worlds created from terrain generation 18 on): knee- to hip-high **legless slitherers** — a head and a
  long chain of links that runs a wave along the body. On these worlds nothing "slithers" on legs any more; a worm is
  slow, lives in a small group, and can be as peaceful or as bitey as any other rolled animal.
- **Spectacle terrain** (worlds created from terrain generation 21 on): the dry rock worlds grow **fields of crossing
  arches** (some fallen, their rubble between the pillars), **table mountains joined by rock bridges** and tables with
  **roofs that reach past their walls**, a second table on top, a tilt or several gates, and **wave rocks** that curl
  over the ground; karst and butte worlds grow **pillar islands** — thin stone towers, alone or in pairs, with
  **balconies** on the way up and a **meadow with a pond and an endless waterfall** on top (climb them with the gloves
  and the rope gun — the data caches lie on the balconies and crowns); karst mountains open in **portals** you can fly
  into and hide **daylight halls** — forests and lakes under a hole in the ground; dry worlds show **bone layers** in
  their cliffs and **skeletons** surfacing from the sand, now and then one with ribs forty blocks tall. Four new
  planet types carry one of these as their whole landscape — **Arch Lands**, **Pillar World**, **Hollow World** and the
  **Ossuary World** — and every galaxy created from now on has each of them at least once, outside the start system.
- **Mini-Michi-Paul** — invented by **Paul and Ben** of the school club: a **knee-high, yellow biped** with a big head
  and two big eyes that lives in herds of ten on the **tropical worlds** (jungle, karst, archipelago, coral sea,
  rainbow sea — worlds created from terrain generation 16 on). It begs for food and **loves bananas**: **two bananas
  tame one**. On those worlds the palms and jungle trees always carry bananas (pick them by hand; if the world's trees
  are poisonous, so are the bananas — Mini-Michi-Paul won't touch them until the detoxifier has washed them).
- **Arachnids** are eight-legged animals as big as a speeder that some worlds roll into their fauna (about every
  third or fourth world has one species). Their heads are boxes or pyramids of several kinds, their eyes come in
  clusters of 2, 4, 6 or 8, and their temper is rolled like any other animal's: many graze or run away, but a
  hunting or territorial arachnid **lies in wait** — it sits perfectly still like a rock until you come within
  about six blocks, then rushes. The **scanner** tells you which kind you are looking at ("Eight-legged", "Lies
  in wait"), and VEGA points out the first one you get near. You bump into its body, your shots land where you
  aim on it, and it can be tamed like any other animal.

### Giants (the colossus, the sandworm, the leviathan and the sky giant)
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
- **Worms hunt animals** (new worlds since generation 17, and every Ignivermis of Arena Nigra): the animals walking the sea
  sand shake it like you do, so a herd on the dunes brings the worm — and whatever stands where it breaches or strikes is
  **swallowed**; a piece or two of **meat** is left on the sand. Companions and pets are never eaten. On an older sand-sea
  world the worm keeps its old manners.
- **Sound devices as lures:** a Crystal-Net **chime** or **horn** standing on sea sand shakes it once each time it sounds,
  and an **alarm siren** keeps shaking it every two seconds while it wails — wire one to a switch, a sensor or a clock and
  the worm comes to it (or stays away from you). On rock they are as silent to the worm as everything else.
- **A leviathan** (new worlds since generation 18, Justus' idea) is the sea's giant — the largest form of water life, a
  60–120 block body with fins, a fluke and two jaws — and lives under the **deep sea of a warm, living water world** (oceans,
  coral seas, archipelagos, river lowlands: plenty of water, not frozen over, at least a few percent of the world at least
  twelve blocks deep). It does everything the sandworm does, in water: it hears **swimmers, boats and the fish** through the
  water (a step on a pier, a raft or the shore is silent, and so is mining), a **wake** rolls toward you and VEGA warns you,
  then it **breaches** in an arc or rears up and **strikes** the spot — a boat under its head takes heavy **hull damage** (a
  parked one too; a hover speeder out on the water counts as a boat), whoever is on it is hurt, and every fish in reach is **swallowed**, leaving a little meat. You can only
  hit it while it is above the water. Get to the shore, or hold still.
- **A sky giant** (new galaxies since generation 18, Justus' idea) drifts over every **gas giant**: a sailer 40–80 blocks
  long — a row of tall sails along its back, glide fins, a fluke — that circles slowly between the islands 40–70 blocks
  over the gas, wandering to a new spot every couple of minutes, and calls now and then. It is **passive**: it never lands,
  never strikes and eats nobody — a spectacle. You can still fight it (it is always in the open; hit it anywhere along the
  body); a hit only makes it climb out of reach for a while.
- A defeated giant is gone for a few in-game days, then another one comes. None of them ever changes a block.

### Taming creatures (companions)
- Craft a **Creature Translator** (`creature_translator`, workshop recipe + blueprint) and some **bait** — all three
  are **crafted by hand** in the Tab menu: **Meat Bait** from creature meat, **Forage Bait** from plant fibre and
  berries, **Nectar Lure** from berries and salt. Select the translator and **right-click** a wild creature to start
  a **taming ritual**.
- A HUD panel shows the creature's **mood** and what it **wants now** — offer the bait it craves, **calm** it,
  **approach** slowly, or **give it space**. When it is hungry, the panel names the exact bait and how to make it.
  Each correct response builds **trust**; reach the threshold and it becomes your **companion** and is named. A first
  tame of a species also grants **research knowledge**.
- **Answering:** while the ritual runs the mouse cursor is free and you stand still. Click a button, or press
  **1** (offer bait), **2** (calm), **3** (approach), **4** (give space) — the keys are printed on the buttons — and
  **Esc** stops the ritual. With a **gamepad**, the stick moves between the buttons, **A** answers, **B** stops; on a
  **touch screen**, tap the buttons. **Tab** still opens the menu (for example to craft the bait the creature wants);
  the panel steps aside and comes back when the menu closes.
- **Raw food works a little:** without the crafted bait, a meat-eater also takes **raw creature meat**, and a forager
  or nectar-lover takes **berries or raw fruit** (never the toxic kind). Raw food only counts half — the first bite
  takes the edge off, it stays hungry, and a **second bite** completes the step that one crafted bait completes on its
  own. Crafted bait is always used first when you carry it.
- **Harder creatures are harder to tame:** skittish animals **bolt** at the first wrong move, territorial and
  aggressive ones turn on you; placid grazers forgive mistakes. Exotic (cave/lava/flying), glowing and oversized
  creatures need more steps — and two animals of the same kind can behave differently.
- A companion **lives on the world you tamed it on**: it follows you there (friendly green-cyan tint + a floating
  name), re-appears whenever you return, and is hidden elsewhere. Manage them in the **Creatures** menu tab
  (rename, **feed**, release — each with a **bond bar**). Companions are peaceful and can't be hurt — nobody can
  attack one, yours or anyone else's (a tamer's pet included).
- **Feed & bond:** every companion has a **bond** (0–100; a fresh tame starts around 40–60). **Feed** it from the
  Creatures tab — any bait (forage, meat or nectar) will do, one meal a minute, **+5 bond** each. Feeding
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

### The bio lab (samples, preparations, changed gear, breeding)
Every plant, every animal and every ore vein carries something of its own — and it depends on **where and how it
lives**. What survives beside a lava lake tends to protect from heat, what climbs tends to help you climb, what
lives in water tends to help you breathe. The bio lab lets you find out, and use it. There are **no recipes** to
look up: what a mix does follows from what goes in, and the same things always give the same result.

- **Samples.** Whenever you harvest a plant, fell a tree, mine ore, crystal, salt or sulfur, or defeat an animal,
  you get what you always got — **plus one sample** of that species (for ore: of that world's deposit; always from
  the first block, afterwards from about one block in eight). Samples go into your **sample case**, a separate
  compartment of the inventory (24 kinds, 20 of each), so they never clutter your backpack. A block you placed
  yourself gives no sample. If the case is full the harvest still works — you just get no sample. To make room,
  **throw a kind away**: *Inventory → Samples*, pick the sample, **Throw away** (click twice — the second click
  confirms), or the same button on the bio lab's *Analyse* page. Every sample of that kind goes; what you learned
  about it stays in the Codex.
- **Keeping the overview.** The sample case lists **plants, animals and deposits** under their own headings — the
  ones you have not analysed yet first, then by effect. Every row shows what an analysed sample holds (an effect
  like *Speed II* in its colour, a deposit its strongest traits) or *Not analysed*. Above the list sit the
  **filters**: one chip per kind (*All · Plants · Animals · Deposits*, with how many each holds) and the **Effect**
  button, which steps through the five **effect families** — *Movement* (speed, jump, feather fall, grip),
  *Protection & healing* (shield, regeneration, heat, cold and toxin ward), *Strength & work* (strength, mining,
  reflex, gathering), *Survival* (breath, satiety, energy), *Senses* (night sight, perception, stealth) — and
  *Not analysed*. The filter is the same in the bio lab and on *Inventory → Samples* and lasts until you quit the
  game. On the lab's *Change* page the case shows only deposits (nothing else changes a tool) and marks the ones
  that would change the chosen piece with **✓ works**.
- **The sampler** (`bio_sampler`, workshop, comes with the bio lab blueprint): a **right-click** gadget that takes
  a sample from a **living** animal without hurting it — 6 blocks reach, 48 for a giant. A hostile animal
  must be held in stasis first — except a giant, which cannot be frozen: there the long reach is the hurdle.
  The same animal gives a sample only every five minutes. A **companion** also
  brings a sample of its species along with its regular gift.
- **The bio lab block** (`bio_lab`, workshop; blueprint **Bio Lab** — a starting blueprint, no other one needed).
  Place it on the ground, in your base or on your own station. **One lab is enough**: stand within 3 blocks of it
  and use **Interact** on it.
- **The bio lab aboard** (`bio_lab` ship module, built in the **Ship tab** with the same Bio Lab blueprint): with the
  module fitted, **Interact at your ship's workshop station** asks *Workshop or Bio lab?* (E keeps crafting one key
  press away) — in the parked ship and in its interior while it floats in space. It never works from the pilot seat,
  on a spacewalk or in somebody else's ship, and heat- or cold-sensitive effects feel the cabin air there. A lab
  **block** built into a ship stays decoration; VEGA says so once and names the module. The lab has three pages:
  - **Analyse.** Uses up one sample (in Sandbox nothing is used up, but a sample of the species must still be in
    your case) and shows what the species carries: its **substance** with one **effect**, a
    **strength** from I to XV, a **rarity** (common → legendary), how long it lasts, whether it has a **catch**
    (a side effect), whether it is **toxic** and whether it is **heat- or cold-sensitive**. The first analysis of a
    species pays **knowledge**. Until then the scanner only says "substance: unknown". For a mineral sample you see
    the material's **fixed traits** (copper conducts on every world) and the **origin values** of this world's
    deposit: its purity and what is a little stronger, weaker or extra here.
  - **Mix.** Put in a sample and get a **preparation**. A plain extract needs nothing else. With the
    **Synthesis** blueprint you add up to three more things:
    - a **carrier**, which decides the form: water → **injector** (full strength, short), plant fibre or polymer →
      **gel** (weak, lasts very long), berries, grain or fruit → **bar** (half strength, long, and it feeds you),
      salt → **capsule** (in between), oil or lubricant → **coating** (not for you — for your tools, see below);
    - a **stabiliser** — a mineral sample or any ingot: it steadies the mix, and the *right* material takes the
      catch away for a bit of strength (the lab shows which trait it wants);
    - a **second sample** that reacts with the first: it can **amplify** it, **weaken** it, **add its own effect**
      as a second one, or **turn** the effect into a related one. Which groups do what to each other is the same in
      every world — once you know that group A amplifies group B, that holds forever.

    The lab shows the **stability** of a mix. A shaky mix can **fail**: everything you put in is gone and you get
    nothing — but your handbook remembers it, and a mix you have tried once is shown with its result *before* you
    mix it again. Toxic samples make a mix shaky. With a **detoxifier** within 3 blocks of you and one carbon the
    lab **washes** them as part of the mix, and it says beforehand which it will do. A washed and an unwashed
    mix of the same things are two different experiments with two handbook entries.
  - **Change** — this is where you **improve** tools and gear (the Codex article *Improving Items* explains it step
    by step). With the **Lab Tuning** blueprint (right after Bio Lab; VEGA announces the tab once it is researched):
    put in a **drill, a weapon or a piece of suit gear**, a **material** (needed: a mineral sample, an ore, an ingot,
    steel, a crystal …) and optionally a **coating** (the slot reads *None — optional*). Once a piece is chosen, the
    lab lists the materials you carry that **work on it**; a material that changes nothing says so, and VEGA explains
    once what kind of material works. If your backpack holds nothing changeable but you **wear** a changeable piece,
    the tab says to take it off first. The material's traits decide *what* changes — hard
    makes a drill hit harder or armour tougher, conductive saves energy, light speeds a tool up, magnetic adds
    reach or grip — and the purity of the deposit and the coating decide *how much*. A big change always has a
    **price**: the tool gets a bit slower or hungrier, gear gets a bit heavier (unless the material is light). The
    lab never changes a tool's **tier** or its mining radius. Nothing wears off; you can change the item again or
    **wash it off** for free. A changed item carries "changed" in its name and shows its new values in the tooltip.
    The item must lie as a single item in your **backpack**: take a suit piece off first and put it back on
    afterwards.
- **Taking a preparation.** Like food: hold it and **right-click**, pick **Take** in the hotbar slot actions, or
  press **Take** on its card in the inventory. Every running effect is listed in the vitals panel under your bars
  with its strength, the time it has left and its catch; a shield shows as "+n Shield" behind your health. You can
  run **three effects at once**; taking the same effect again refreshes it, and a weaker one is refused (and not
  used up). In a list a preparation reads like "Injector · Speed III". The effects:

  | Effect | What it does (at most) |
  |---|---|
  | Speed · Jump | walk up to 25 % faster · jump up to 40 % higher |
  | Feather Fall · Grip | fall protection · tire more slowly when climbing — both join your boots and gloves |
  | Shield | up to 30 extra points that take a hit before your health does |
  | Regeneration · Energy | heals up to 1.5 health per second · recharges the suit |
  | Strength · Mining · Reflex | up to 30 % more melee damage · mining power · up to 25 % shorter cooldowns |
  | Breath · Satiety | up to 40 % less oxygen · hunger use |
  | Heat Ward · Cold Ward · Toxin Ward | protection that joins your suit's insulation and corrosion resistance |
  | Night Sight · Perception | the dark gets brighter · living things are marked through walls |
  | Stealth | hostile creatures ignore you — a minute at most |
  | Gathering | plants you harvest give one or two extra |

  Effects never go past what the suit formulas allow — a preparation helps a lot early on and a little on top of
  the best gear. A **catch** is always mild: more hunger or oxygen use, a little slower, a lower jump, slower
  healing or cooldowns — never damage. A heat-sensitive effect runs out twice as fast above 40 °C, a
  cold-sensitive one below −5 °C — judged by the air you are really in: aboard your ship or a station that is a
  comfortable 22 °C, outdoors the real temperature, in every game mode.
- **Your handbook** is in the Codex: the chapters **Substances** (every species you analysed, grouped by effect
  family), **Compounds** (every
  mix you tried, also the failed ones) and **Materials** (every deposit you analysed and every item you changed).
  In the lab, the sample case is on the left; a click on a sample puts it into the fitting slot of the open page.
- **In Sandbox** nothing is used up and no blueprint is needed — but samples still have to be collected: the
  *All items* page offers no samples, seedlings or preparations, because those only exist with a species or a
  substance in them.
- **Rarity is found, not rolled.** Common worlds and easy animals give common substances (strength I–III). The
  strong ones live where it is hard to get: rare planet types, caves, lava, dangerous animals, giants (always
  legendary). So travel pays — the best heat ward is not in your back garden.
- **Cloning from samples** (clone tank, see § Crystal Net). The tank's list also offers every **animal sample**
  in your sample case, marked "(sample)" — so you can grow an animal **on another world** than the one it comes
  from, also on an airless moon or an asteroid. The list follows your sample case by itself. It costs one
  sample and two matter dust instead of bait (nothing in Sandbox). A water animal needs a block of water within
  8 blocks of the tank, a lava animal lava. Hostile animals and giants are never grown. A world holds at most 16
  clones (six per player, as before). When you come back to a world, every clone stands beside its tank as the
  species it was grown as; a clone that was defeated or tamed does not come back. A tank that was still growing
  when you left starts its minute over.
- **Crossing** (blueprint **Crossing**, needs the clone tank). Pick a **partner** in the tank's menu: two
  animal species become a **new species** — the body of one parent, the colours and ornaments of the other, size
  and speed in between, and now and then a trait neither parent had. The same two parents always give the same
  child. A cross is never hostile. It costs one sample of each parent and four matter dust; you get the animal
  **and a sample of the new species**, so you can clone it, analyse it and cross it again (up to three generations).
  The child's substance comes from one parent and is usually about as strong as the stronger one — a little
  stronger when the parents come from **different worlds**. The animals from the school club cross like any other.
  If your sample case has no room for the new species when the tank is done, the tank **waits** with its light
  on and tells you once — make room and the result comes out.
- **Breeding plants.** Two **plant samples** in the tank give a new plant species with a **new form** — the stem of
  one parent, the crown of the other, its own colour, sometimes glowing. You get two samples of it. At the bio lab
  the **Analyse** page turns any plant sample into a **seedling**. Plant it on any natural plant ground, on a
  **hydro tray** or in a **flower pot** — on any world whose air is neither toxic nor corrosive, in your base or on
  a station;
  tainted ground does not work. A harvested bred plant grows back after a minute and a half and gives its sample
  again. A world holds up to 256 bred plants.

### Hover speeder (surface vehicle)
- Craft a **hover speeder** (`speeder`, blueprint-gated workshop recipe: titanium_plate ×8, cable ×10,
  energy_cell_1 ×4, circuit_board ×2, crystal ×2, light_alloy ×2, lubricant ×2). It's a single-seat hover vehicle for
  crossing a planet fast — over land **and water**.
- **Deploy** it from its hotbar slot (**right-click**), then **board** with **E**. While driving: **W/A/S/D** to
  steer (arcade hover handling), the camera follows in a chase view, **R** refuels it from an `energy_cell_1`,
  and **F** dismounts. Press **X** near a parked speeder you own to **pack it back up** into the item.
- The speeder has its **own energy tank** (driving drains it) and a **voxel hull** that takes collision damage —
  hard impacts dent it and enough damage destroys it (not in Sandbox: there a speeder or boat takes no damage). It
  persists with you across reloads (like a companion).
- It **hovers over water** too: drive off the shore and it keeps its hover height above the surface, across lakes and
  seas (out on a deep sea it is as loud as a boat to a **leviathan** — see Giants). It still **unfolds only on dry
  ground** (deploying towards water is refused). **Lava, oil and a gas giant's gas sea** are a stop: any of them ahead
  caps the throttle, and
  if it does get pushed over them it floats and can only back out; keep driving anyway and after a few seconds it is
  set back onto the last safe spot.
  A speeder wedged against a lip hops over it by itself after a moment; a hull that ends up inside a block lifts out.
- **Dismounting** puts you on the ground **beside** the hull — out on the water, into the water beside it, like
  getting off a boat (swim back and press **E** to board again). A parked speeder or boat is **solid** — you can
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
- The **boat** (`boat`) is the water kind of the same system — an early-game craft with **no
  blueprint**: **by hand, anywhere**, from logs ×10 (any kind) + plant_fiber ×6 (#2536 — listed as **Wooden boat**,
  #2544), or at the workshop from logs ×8, iron_plate ×2, cable ×1 (listed as **Boat**). Both make the same
  boat. On an **ocean** world you start with one.
- Stand at the shore (or swim) and **use** it from the hotbar: it needs **open water in front of you** (2–5
  blocks ahead, a little to either side) and is set onto the **waterline**; on dry land you get "you need open
  water" and keep the item. **E** boards it from the bank or while swimming, **F** gets off, **X** packs it up.
- While driving: **W/A/S/D** as in the speeder but slower (9 m/s, **Shift** 13), lazier steering and the hull
  **drifts** through turns; there is **no hop** and **no energy** — the boat never runs dry and the HUD shows no
  gauge. It keeps your **head above water**. (The later hover speeder crosses water too, faster — but it needs a
  blueprint and runs on energy cells.)
- Leave the water and the boat **runs aground**: it settles onto the sand, forward speed bleeds off, you can
  reverse off or step out — no damage. Keep driving over land anyway and after a few seconds the server sets you
  back onto the last spot that floated. Shallow water lets you nose onto a beach to get out.
- Hull damage, destruction and persistence match the speeder; the boat cannot be refuelled (nothing to fill).

### Monorail hover train (Justus' idea)
- **The line.** Craft **rail pylons** (blueprint *Monorail*, or buy them from a **rail dealer** — place a *Rail Dealer's
  Post* at your base). Place a row of pylons: each one **links itself** to the last one you placed within **32 blocks**, as
  long as the line does not bend more than about **30°** — and a glowing blue **energy band** appears between them, with
  pulses of light travelling along it. **Stack**
  pylons to lift the line over things. A line that would run **through solid blocks is refused** ("Obstacle") — give it
  room. A pylon carries **at most two links** (no forks). The **rail linker** couples any two pylons by hand (use it on
  the first, then the second): close a **loop**, join two lines, or — on two linked pylons — uncouple them.
- **Stops.** A **rail stop** placed beside the line (within four blocks) halts a train on autopilot for a few seconds. It
  is a Crystal-Net device: a signal on its port (a switch, a button, a sensor) sends the halted train off at once. The
  band glows amber where a stop catches it.
- **The train.** Use the **monorail cab** on a line and it appears there, hovering. Use a **seat wagon**, **sleeper
  wagon** or **bar wagon** just behind the last wagon to couple it on (six wagons at most). **E** near a wagon boards it
  — and so does simply **stepping in**: walk through a door or jump into a wagon, even a moving one, and you ride along;
  inside, **you walk around while it moves** — the wagon is your ground, wagon to wagon through the open ends — or sit
  down on a seat (E beside it; jump or move to stand up). **E in the cab** opens the panel: **speed 1–3**, **halt / go**,
  **autopilot** (the train runs by itself, halts at every stop, and turns round at the ends of an open line — on a
  loop it just keeps going). When it turns round, the wagons stay exactly where they are and simply run the other way,
  so nobody aboard is thrown about — on the way back the cab pushes the train from behind, and at a stop the wagon in
  front halts at the stop. **F** or walking out of the open side leaves the train; you are set down beside it.
  Allies may ride your train. The owner **packs it up** from the cab panel or with the stow key beside it: the cab
  and every wagon come back as items. A fall taken aboard is never a fall.
- Other riders on the same train are placed inside the wagons exactly as your game draws them, so nobody slides
  through a wall on a curve or across the world seam.
- **Intercity trains.** On some newer worlds, two towns or cities are joined by a monorail line. You'll find a
  **station** at the edge of each town — look for the amber station marker on the world map ("… Station"). A
  **public train** runs back and forth between the two stations on its own and waits about **30 seconds** at each one.
  Walk up to a wagon and press **E** to board (E beside a seat sits down, **F** leaves), then enjoy the ride. The train
  belongs to everyone: nobody needs a ticket, and nobody can drive, stop, pack up or add wagons to it. The stations and
  the line's pylons can't be mined.
- **Abandoned stations.** Some newer worlds have the ruin of an old station instead — no working line, but an old
  wagon and salvage to scavenge (see *Ruins & treasure chests*).

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
  and other players see you sitting. **One seat, one sitter:** a seat somebody already sits on — another player or a
  villager/crew member — can't be taken (*"Someone is already sitting there."*), and people won't sit down on your
  chair either: they wait beside it and sit down once you get up. Stand up with **E**, jump, crouch or any movement key.
- **Faces:** everyone has a face of their own — some are clean-shaven, others wear a moustache, a goatee, a beard or a
  full beard in their hair colour; androids have a small speaker grille instead.
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
- **Blocks that face you:** machines, counters, screens and Crystal Net devices have a **front, sides and a top of
  their own** (a forge shows its fire on the front and bricks on the sides, a crate its lid on top, a workbench its
  tools on top and drawers on the sides). When you place one, its **front turns toward you** — a watcher and a Device
  Eye are the exception: their eye looks the way you are looking. While you hold such a block, the preview shows an
  **arrow** on the side that will be the front; press **R** (**Shift+R** backwards) to turn it before you place it.
  Mining the block gives back the normal item.
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
- **Torches and lanterns hang where you put them.** Click the **side** of a block and a torch hangs on that wall —
  leaning out from it on a little holder, flame in front — and a lantern hangs on a small iron bracket. Click the
  **top** of a block and it stands, as always. Click the **underside** of a block and a lantern hangs from the
  ceiling on a short chain; a torch can't hang there (the game tells you to try a lantern). There is nothing to
  rotate — the face you click decides, and the see-through preview shows how it will hang before you click.
  Torches and lanterns placed before this update, and those in villages and lodges, keep standing upright.

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
- **Tab → Character → Appearance** opens one screen for how your figure looks. Along the top are tabs for
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
  labelled **live tiles** beside the canvas (Front / Left / Back / Right — your figure's own left and right, in the
  order you walk round it; arms and legs get one tile column per limb, headed Left/Right) — click a tile to paint
  that face, and the tiles update while you draw so you always see the whole part. **Clear** wipes only the face
  you are on, and a fill never runs over onto another face. Every face shows on your figure exactly as you paint
  it — the left of the canvas on the left as you look at that face, so writing reads the right way round. The
  helmet's front stays open, so your face always shows.
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
  When you join a world, the others see your whole look straight away — colours, face and every painted part.

### Death & respawn
- At 0 health you respawn at the ship's **Medbay heal-tank** (vitals restored); a salvage capsule may drop
  at the death site to recover cargo, per the active rules.
- Alive but stuck? You never have to wait to suffocate — see *Back to my ship (stuck on foot)* above.

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
  nobody can paint themselves an X-ray view. The editor also opens **inside a world** (Character tab →
  *Textures*), where a **world admin** additionally gets **Publish for everyone** / **Take back**.
  - **World textures:** what an admin publishes is shown to everyone in that world and **wins over your own
    pack** there. Two switches in the Character tab are yours alone: *Use my textures* and *Show this world's
    textures* — the second one is the way out if you do not like what a world shows. The world option *World
    textures: Admins / Off* decides whether admins may publish at all. `/reporttexture <key>` flags a world
    texture for the operator; admins remove them with `/texturewipe <key | player | all>`.
  - **Submit to the developers:** offers your texture for the game itself. You give it a name and a
    **nickname** (please not your real name — or choose *No name*), may add a note, and tick three boxes: you
    painted it yourself, the developers may use, change and distribute it with the game, and you are at least 16
    or your parents agreed. Click a sentence to tick its box. If you press *Submit* while a box is still empty,
    nothing is sent: the dialog tells you to tick all three and marks the empty boxes in amber. Sent are the
    texture, those texts, the game version and an anonymous id for the
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
- **My Tools.** Give your drill, pistol, blade, scanner or gadget (binoculars, camera, medkit …) **a look of your own**: a small coloured voxel model
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
- Press **Enter**, type, **Enter** to send (scrollback in the chat panel). Normal chat requires a **radio
  worn** in a module slot of your suit (**Inventory → Suit**) — in your hand, on the quick-bar or in the backpack it
  does nothing, and the server says so ("Your radio must be worn …"). Messages are rate-limited and length-capped.
- **Tab → Alliances → Radio** sends a line too and shows your radio: the one you wear and how far it reaches, or a
  warning when none is worn — with a radio in your backpack (or, aboard, in the hold) a **Wear …** button puts it
  into a free module slot right there.
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

Craft the upgrades at the workshop (each tier builds on the previous one; research it under **Blueprints**).
A radio works only while it is **worn** in a module slot of your suit; the **widest** radio you wear sets your
reach. Without a worn radio you can't transmit: with one in your backpack, hand or hold you are told to put it on,
with none at all that you need one.

### Voice chat (push-to-talk)
- Hold the **push-to-talk key** (default **Caps Lock**, configurable in Settings → Voice chat) to talk to your
  radio audience; a "● Talking…" indicator shows while you transmit. Release to stop. (Caps Lock also switches
  the keyboard's caps-lock light each time you press it — that changes nothing in the game, but text you type
  next may come out in CAPITALS; press it once more, or pick another key.)
- Voice is **on by default**. When you **host a world for friends** it works out of the box; it needs the
  same **worn radio** as text chat and carries to the same tiered reach (planet / system / galaxy). Holding
  the key **without a worn radio** sends nothing and shows "No radio worn — nobody can hear you" instead.
- Where to find it: **Tab → Alliances → Radio** shows "Hold Caps Lock: talk" (with your key) while voice works
  and a radio is worn — or "Voice chat is disabled on this server" — and the **actions list** (pad **LS**,
  touch **⋯**) shows the same hint on foot.
- Settings → Voice chat: a master **on/off** switch, **Voice volume**, **Transmit mic** (turn your own mic
  off while still hearing others), and the **push-to-talk key**. The key picker offers only keys **no other
  control uses** (Caps Lock, Z, Y, Left Alt, the two mouse side buttons — and V, B, T, G or H once you have
  rebound their action elsewhere). Players who still had the old default **V** (the camera view's key) are
  moved to Caps Lock once.
- Audio is relayed **live through the server and never recorded**. Use a **headset** to avoid echo.
- *On a standalone **dedicated** server, the admin must enable voice (`voiceChatEnabled` / `--voice true` /
  `BBS_VOICE=true`); local hosting enables it automatically.*

### Player feedback (**F1**; **F2** in the browser) — and the developers' answers
- Press **F1** (F2 in browser builds) during play to send a bug report or a wish: a title, a description
  and an optional e-mail. A screenshot of the moment you pressed the key — HUD, crosshair and on-screen
  hints included, without the form — and a small technical snapshot (player name,
  version, position, stats) travel with it to the developers' inbox. No connection? The report is queued and sent on a
  later start.
- **F1 works in the main menu too** — and on the world list, Settings, Credits and the Editors overview —
  so a problem you hit before a world is even loaded (say, while creating one) can be reported right
  there. The menu form asks for the same title, description and optional e-mail; it attaches a
  screenshot of the menu and basic technical data (player name, version, your device), but no world
  data, because there is no world yet. It does not open while you are typing in a text field or while
  Settings waits for you to press a key. A report queued offline from the menu goes out with your next
  world. (Not in the editors themselves — their keys are busy.)
- **The developers can answer you in the game.** When they reply to one of your reports, a line shows
  on the HUD and a window opens with the conversation — what you reported, their answer, and
  "Fixed in version …" when the fix has shipped. **OK** (or Esc) closes it; you see each answer once.
  The window also opens in the main menu (there without the HUD line), or else in your next world.
- When the developers **ask you a question**, the same window has an answer box: type your answer and
  press **Send answer** — it is stored with your report (up to three answers per report). Never put
  personal data in there.
- Answers are only fetched while the game remembers a report you sent in the last 90 days; a game that
  never sent feedback contacts nothing. In the browser (play.bbts.de or the glitch.fun arcade) this
  works the same way; the arcade ties answers to your Glitch install, so they follow you across releases.

### `/bump` — debug snapshot + screenshot (any player, no radio needed)
- **Syntax:** `/bump Description of the problem`
- Writes a detailed JSON snapshot of your current situation (player state **and inventory**, environment,
  nearby blocks/creatures/players + a wider block/flora census, the Guardian robots, scan-drones and bandits
  around you with what they are doing and whether they stand in water, your training progress (the active
  steps plus the tutorial and VEGA tip milestones), ship status, and a 30-second history)
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
unless the operator starts it with `--admin-cheats true`. Command replies that carry information (target lists,
what `/give` handed out, refusals such as an unknown time or weather) appear in the **chat scrollback**; a plain
confirmation is the brief HUD toast.

| Command | Effect |
|---|---|
| `/give Item [Count] [Player]` | Give an item to yourself or a target player; the answer says what was given and what did not fit. A sample, seedling or preparation needs its content in the key (a plain one is refused) |
| `/tp X Y Z` | Teleport to coordinates |
| `/tp Target` | Teleport to a landmark **on the body you are standing on** — see *Named teleport targets* below |
| `/tp` | List every named target here, with the exact word to type and its distance |
| `/tpp Player` | Teleport to a player on the body you are on — you land **beside** them, never inside them (#1055) |
| `/settime day\|night\|noon\|midnight\|dawn\|dusk\|18:30\|6.5\|0.5` | Set the local time of day of the world you stand in: a word, a clock time, an hour from 1 to 24 or a part of the day below 1. Not out in space |
| `/setweather clear\|clouds\|rain\|storm\|fog\|blizzard\|heatwave\|…` | Set the weather of the world you stand in (every weather key works; `cloudy` is accepted for `clouds`) |
| `/giant colossus\|sandworm\|leviathan\|sky` | Summon this world's colossus, sandworm, leviathan or sky giant near you (a sandworm needs a sand sea, a leviathan a deep sea) — for testing |
| `/arachnid` | Summon this world's arachnid near you (rolls one into the world's fauna first if it has none) — for testing |
| `/biped` | Summon a begging herd of this world's bipeds near you (Mini-Michi-Paul on a tropical world; rolls a begging biped into the world's fauna first if it has none) — for testing |
| `/wanderer` | Call one of this world's wanderers to you right now — at any hour, without waiting (new worlds only; you must be on foot in the open, 60 m from a village, base, hut or camp, with no hostile near) — for testing |
| `/fly` | Toggle free flight for yourself (no gravity). In **Creative/Sandbox** worlds everybody can already fly — double-tap **Space**; this is the per-player admin cheat for the other modes |
| `/god` | Toggle invulnerability |
| `/instant` | Toggle instant building: blocks, ship and station parts are placed without materials. Crafting is only free in Sandbox |
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
| `railruin` | An abandoned monorail station (`bahnhofsruine` also works) |
| `vault` | A buried vault's surface pillar ring |
| `wreck` | The crashed ship — even before an NPC has pointed you at it |
| `factory` | A factory's production terminal |
| `camp` | A bandit camp (`bandit` also works) |
| `lodge` | A hut in the wild — `lodge 1` is the nearest, `lodge 2` the next (`hut`, `hütte` also work; new worlds only) |
| `outpost` | A pioneer's outpost — `outpost 1` is the nearest (`pioneer`, `pionier`, `außenposten` also work; new worlds only) |
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
| `/basewalls` | Why animals do (not) spawn in a yard: the nearest base core, the enclosure fill at your feet level (cells reached, budget, fail-open), whether your own cell reads enclosed / sealed / open **for animals**, and the rules (2-high walls, hinge doors leak, unloaded chunks read as open; walking Guardian robots step up two blocks and are stopped by a wall of three or more and by water two or more blocks deep, hovering scan-drones by four or more; fliers are not). Since #2542 also the **air at your cell** and why there is none (leaky — since #2649 every closed room of a base breathes, however big, and every door holds air) and the air rules; since #2614 a leak names where the air gets out (the block or the opening, its coordinates and its height) |
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
`/mode Player creative` (or the **Player modes** rows on the Character tab, listed there only for admins)
that player plays **Creative** — free crafting and research, creative flight (double-tap **Space**), no
oxygen/hunger/temperature, no damage of any kind, and machines, bandits, aggressive creatures and space hostiles
ignore them — while everyone
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
**Sandbox** adds free crafting, no oxygen or hunger (not even on a spacewalk), no planet machines or bandits and **no
damage at all** — no lava or fall hurts you, aggressive animals and space hostiles leave you and your ship alone — and the
inventory's **All items** page.
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

An observer also leaves the **story** alone: a `/goto` to another star maps nothing for the world's story and
grows no frontier, VEGA stays silent (no tips, no story lines — the lines you missed arrive when you leave observer
mode), and VEGA's "another pilot is nearby" tip never names you to the players you are watching. A `/goto` made
**without** observer mode is booked the same quiet way: it is a teleport, not a voyage — no "first hyperjump", no new
system for the story, no frontier, no explorer achievement.

The fleet admin panel's per-world page lists the same players and structures plus **build hotspots** (clusters
of changed blocks — how you find a house built without a base core), each with a `/goto` line to paste here.

#### Story / finale testing (skip ahead to the endgame)
These fast-track the story so you can read the full arc and reach the **Guardian finale** (the dialogue-duel
boss) without grinding the whole playthrough. Same admin + `CheatsAllowed` gating as above.

| Command | Effect |
|---|---|
| `/story [Player]` | Print the story status (fragments, kills, milestones, beats revealed, finale revealed/defeated, the player's story track and chapter) and the player stories of you or the named player (state, running part, current step) — admin role only, works with cheats off |
| `/advance [n]` | Advance the shared arc by `n` milestones (default 1), revealing any beats you cross |
| `/revealfinale` | Drive the arc to completion: reveal all narrator beats **and** place the Guardian finale system on the star map |
| `/lore` | Reveal **every** story fragment and personal memory to you (fills the Journal); also completes the readable beats |
| `/jumpdrive` | Fit a **jump generator** (`jump_generator`) to your active ship so you can hyperjump between star systems |
| `/gotocore` | Reveal the finale (if needed) and drop you straight into the Guardian core chamber — **skips the flight and the orbital gauntlet** |

Two ways to test the finale:
- **Full run:** `/revealfinale` → `/jumpdrive` → (optionally `/god`) → open the star map and hyperjump to
  *Guardian Core* → survive the orbital gauntlet → land → descend the aperture shaft → hold to hack the core →
  win the argument duel.
- **Shortcut:** `/lore` (to read everything) → `/gotocore` → hack the core and win the duel.

The duel cannot be lost — wrong rebuttals are dismissed and you stay on the node — so just keep trying each
option (the answer buttons; only a player on the Guardian Core can answer) until the core concedes and shuts down.

**Tutorial controls** (admin role only — works with cheats off): `/tutorial skip [Player]` skips the basic training of you
or the named player, `/tutorial restart [Player]` runs it again from part 1, `/tutorial chapter <id> [Player]` jumps to a
part (e.g. `/tutorial chapter b6` — earlier parts count as skipped, a finished one is replayed), and
`/tutorial policy auto|courses|optin|off` sets the world's tutorial rule for everyone (`courses` = *Short courses only*).

**Story scene preview** (admin role only — works with cheats off, changes nothing): `/cutscene <pack> <id>` plays a story
pack's scene for you alone, e.g. `/cutscene vega_protocol prologue`; `/cutscene <pack> __recap` shows the "story so far"
card. Developers can also start the game with `-devSeq` (or use a development build) and press **F9** for a list of every
scene to play locally.

The client parses these slash-commands and sends an `AdminCommandIntent`; `/bump` is special — the client
captures a screenshot and sends it as a `BumpReport` (falling back to a plain chat message the server
intercepts if the screenshot fails). Non-admins typing a command just get a rejection toast.

---

## 8. For maintainers
When you change a keybind, add a feature, or add a command/cheat, update the matching section above in the
same commit. Keep the tables accurate against the code (controls live in `PlayerController*.cs` — the on-foot
key polls in `PlayerController.cs`'s `Update`, the actions in its `PlayerController.<Concern>.cs` files — and
`PlayerVehicleDrive.cs` (speeder), `PlayerInteractions.cs`, `SpaceView*.cs` (flight in `SpaceView.Flight.cs` and
`SpaceView.Targeting.cs`, EVA in `SpaceView.Eva.cs`), `GameMenu.cs`, `WorldMap.cs`, `ChatUi.cs`; commands in
`GameServer.HandleChat` / `HandleAdminCommand`, the admin verbs in the `AdminCommandTable` of
`GameServerAdminCommands.cs`).
