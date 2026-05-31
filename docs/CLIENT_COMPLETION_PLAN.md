# Client Completion Plan — from scaffold to a playable game

Roadmap for finishing the Unity client. **Goal: a playable game** — launch the client,
start singleplayer (or join a server), and actually play the loop the server already
supports: explore, mine, craft, build/upgrade the ship, take missions, fly into space,
fight, dock with friends.

The headline fact that shapes this plan: **the server is essentially feature-complete and
fully authoritative (M0–M19).** Almost everything below is *client-side UI + rendering +
scene wiring* that consumes protocol messages **that already exist**. We are not inventing
gameplay; we are giving the player eyes, hands and menus for systems the server already runs.

## Definition of "playable"

A first **vertical slice** (M21) is the bar for "it's a game you can actually play":

> From the main menu, click **Singleplayer** → a world loads → you walk around a textured
> blocky planet, mine and place blocks, and your inventory/vitals update — all validated by
> the authoritative server. Quit back to the menu cleanly.

Everything after M21 deepens that loop (crafting/ship/space/multiplayer/audio/art) until it
is a *complete* game.

## Current state (what already exists)

- **Server:** world gen, mining/placing, crafting + blueprints, ship modules + build flow,
  cargo, missions, star map, docking, free-space flight + PvE combat, planet enemies, admin —
  all unit-tested (86 tests). Talks an authoritative intent→state protocol (`NetCodec`).
- **Client scaffold:** `NetworkClient` (transport + codec + typed events for every message),
  `ClientWorld` (chunk cache), `ChunkMesher` (per-face culling, **placeholder colours**),
  `GameBootstrap` (connect/join, mesh chunks, own vitals), `PlayerController` (WASD + look +
  jump + raycast mine/place, `PlaceItem` hardcoded), `Hud` (vitals IMGUI), and the M20 shell
  (`AppShell` splash→menu→settings→loading→in-game, `ClientSettings`).

## Gaps to "complete"

1. No assembled **in-game scene** (AppShell spawns a bare `GameBootstrap` with no player rig,
   camera, light, chunk material or HUD).
2. **Singleplayer** currently means "connect to 127.0.0.1" — no server is actually hosted by
   the client (see the architecture decision below).
3. **Placeholder colours**, no texture atlas.
4. No gameplay **UI**: hotbar, inventory, crafting/tech tree, ship-module build, cargo, star
   map, mission log, docking prompts, space-combat HUD.
5. **Settings not applied** (view distance, mouse sensitivity, fullscreen, language→already
   wired; the rest not).
6. No **other-player** rendering (multiplayer presence), no nameplates.
7. No **space-flight/combat** or **planet-enemy** rendering (server M19 hooks exist in
   `NetworkClient`, nothing draws them yet).
8. No **audio** (the audio module isn't even referenced; shell is silent by design).
9. No **player build** (.exe) / distribution.

## Key architecture decision — Singleplayer hosting

`Spacecraft.Shared` / `WorldGeneration` / `Networking` are **netstandard2.1** (already loaded
in Unity). But `Spacecraft.GameServer` and `Spacecraft.Persistence` are **net8.0** and
Persistence uses **native SQLite**, so they cannot simply be dropped into the Unity (Mono)
runtime. Two ways to make "Singleplayer" host a server:

- **Option A — bundle the server, launch as a child process (recommended for MVP).** Ship the
  published `Spacecraft.GameServer` executable inside the client (e.g. `StreamingAssets/server/`).
  On "Singleplayer", start it bound to `127.0.0.1` on a free port, then connect the normal
  `NetworkClient` to it; stop it on quit. **Pro:** uses the real server unchanged, works today,
  identical behaviour to multiplayer. **Con:** a child process + a few hundred ms startup.
- **Option B — true in-process server (later optimization).** Retarget `GameServer` +
  `Persistence` to `netstandard2.1`, add a Unity-safe persistence backend (in-memory or a
  managed SQLite / file store, since native `e_sqlite3` is awkward under Unity), and run the
  server in-process over the existing `LoopbackServerTransport`/`LoopbackClientTransport`.
  **Pro:** no child process, instant, single binary. **Con:** real porting work + a second
  persistence implementation.

**Plan: ship Option A in M21**; revisit Option B only if the child process proves
problematic. Multiplayer ("Join Server") already works against the standalone server today.

## Phased roadmap

Each phase is independently shippable and leaves the game in a working state. Phases are
ordered for the **fastest path to fun**; M21 is the milestone that makes it "a game".

### M21 — Playable vertical slice ⭐ — **DONE (code) / pending in-Editor playtest**
Implemented:
- **`WorldRig`** builds the whole in-game rig in code from `AppShell.LaunchGame` — server link
  (`GameBootstrap`), chunk material, first-person player (CharacterController + camera +
  `PlayerController`), `Hud`. The launcher scene needs only an `AppShell` GameObject.
- **Singleplayer via Option A**: `ServerConfig.ApplyCommandLine` (`--port/--saves/--data/--world/
  --name/--max-players/--view-distance`, unit-tested); `LocalServerLauncher` starts/stops the
  bundled server; `scripts/publish-local-server.ps1` bundles it into `StreamingAssets/server/`.
- **Robust connect**: `GameBootstrap` sends the join only once the transport is Connected, with
  a few retries (handles the local server still starting up); snaps the player to the server's
  authoritative spawn once.
- **Curated per-block palette + per-face shading** baked into vertex colours via the built-in
  `Spacecraft/VertexColorOpaque` shader (a clean blocky look without a texture atlas yet).
- **Settings applied**: mouse sensitivity + invert-Y → `PlayerController`; view distance →
  the local server; fullscreen/quality via `ClientSettings.Apply`.
- **Clean return to menu** (Esc): tears down the world, disconnects, stops the local server,
  unlocks the cursor.
- **Outcome:** menu → Singleplayer → walk/mine/place on a shaded blocky world → Esc → menu.

Remaining before declaring M21 fully closed: an **in-Editor playtest** (publish the local
server, press Play, verify the loop) and any fixes it surfaces. A real **32×32 texture atlas**
(replacing the palette) is deferred to M27.

### M22 — Core gameplay UI (the survival/build loop) — **DONE (code) / pending playtest**
- **Hotbar** in the HUD (1–9 keys + scroll select; drives the placed item; sends
  `SelectHotbarIntent`) — replaces the hardcoded place item.
- **`GameMenu`** (Tab) with tabs: **Inventory + cargo** (`InventoryUpdate`), **Crafting**
  (`CraftIntent`/`CraftResult`), **Tech** = blueprint unlock (`UnlockBlueprintIntent`), and
  **Ship** = module build (`BuildShipModuleIntent`, incl. M19 weapons/defense). Opening the
  menu frees the cursor and pauses the player; server feedback shows as a HUD toast.
- Bilingual UI keys added (`ui.tab.*`, `ui.action.*`, `ui.hud.hint`).
- IMGUI for now (consistent + testable without the Editor); **uGUI/UI Toolkit polish deferred**
  to M27. Item-move/drag between slots needs new server intents — also deferred.

### M23a — The ship as a place (visible on the planet + enterable) — **DONE (code) / pending playtest**
Implemented: the server stamps a hollow, walk-in **voxel ship hull** (`iron_wall` + a `glass`
viewport + a door) at the start landing zone (`StampShip`, gated by `PlaceStarterShip`); it
renders via normal chunk streaming and the player **starts inside it**. `AboardShip` is derived
authoritatively from standing inside the hull (`UpdateAboard`), which gates cargo crafting /
module build / oxygen regen; `PlayerStateUpdate` now carries it and the HUD shows "aboard ship".
A `ShipPlacement` message drives a **HUD minimap/compass** (top-right) that always points to the
ship with the distance in blocks. The **hull is mining-protected** (`IsShipBlock` → mine
rejected), so the ship can't be dismantled.

**Stations:** the logical stations already exist as **ship modules** (cockpit, reactor,
life-support, medbay, workshop, quarters, cargo hold) and drive behaviour today — the workshop
module gates workshop crafting (when aboard), the **medbay heal-tank is the respawn point**,
the cargo hold is the shared cargo inventory. The interior now also has **visible station
markers** (heal-tank, cockpit console, workshop bench, cargo crates, bunk).

**Station interactions (M23a-2 — DONE, code):** the server sends the station positions
(`ShipStations`); standing next to one shows a **"Press E: <station>"** prompt and `E` sends a
`UseStationIntent` the server validates by proximity + module: **heal-tank** heals to full,
**quarters** sets the respawn point, **workshop/cargo** point you to the Tab menu, **cockpit**
acknowledges (full star-map travel in M23). Later: proper 3D props, bigger module-driven
interiors, and lift-off / star-map travel from the cockpit.

Later: per-player ships, modules shown as interior props, lift-off / star-map travel from the
cockpit, explicit "press E to enter".

Original design notes (for reference):

- **Anchor:** the player's `LandingZone` (already has `CenterX/CenterZ`) at the surface height
  is the ship's world position. The server owns it.
- **Body (MVP):** on world setup, the server writes a small **voxel ship** at the landing zone
  — a hollow hull of `iron_wall`/`glass` blocks with a floor, a door gap and interior module
  markers (cockpit/medbay/workshop/cargo tiles). It streams like any other chunks, so the
  existing mesher renders it and the player can physically enter through the door. The landing
  zone is already protection-gated so others can't grief it.
- **Enter/exit + "aboard":** the server sets `AboardShip` from whether the player is inside the
  ship's bounding box (authoritative), which already gates cargo crafting / module build /
  oxygen regen. Optional explicit `EnterShipIntent`/`ExitShipIntent` for a teleport-to-cockpit
  convenience. New protocol: `ShipPlacement { x,y,z, bounds }` sent on join so the client knows
  where the ship is (HUD marker / "you are aboard" indicator).
- **Client:** render is automatic (it's blocks); add an "aboard" HUD indicator and, when near
  the door, a "Press E to enter" prompt (drives the optional intent). The respawn heal-tank
  (Medbay) sits inside, so death already returns the player into the ship.
- **Later:** modules visually reflected inside (M19 weapons/defense as props), ship lift-off /
  star-map travel from the cockpit, multi-block ship building, per-player ships.

This depends only on existing systems (landing zones, chunk streaming, `AboardShip`,
protection) plus one small placement message. Target: implement right after M22/M23.

### M23b — Player avatar, customization & third-person camera — **DONE (code) / pending playtest**
Implemented: a code-built blocky humanoid (`PlayerAvatar`: head/torso/2 arms/2 legs from
cubes, colliders stripped), per-part **colours** in `ClientSettings` with a **Character section**
in Settings (palette cycling + swatches), and a **first/third-person toggle** (key **V**;
`PlayerController` moves the camera between eye and a behind-the-player offset and shows/hides
the avatar). The avatar is parented to the player so it turns with movement. Deferred:
networked appearance so **other players** see it (with M24, via a `PlayerAppearance` message),
**armor overriding** parts, walk/idle **animation**, and third-person **camera collision**.

Original notes:

- **Avatar model:** a blocky humanoid built from cubes (head, torso, two arms, two legs) — a
  small code-built rig (like the ship/world) so no art asset is needed for the MVP; later a
  proper skinned/segmented model.
- **Customization:** a character screen (from the main menu and/or in-game) to set per-part
  **colours** first (head/torso/arms/legs/skin), then later swappable shapes/cosmetics. Stored
  in `ClientSettings` locally; for multiplayer the appearance is sent to the server on join and
  broadcast so **other players see it** (new `PlayerAppearance` message + field on the player
  state). Server stays authoritative over identity; appearance is cosmetic.
- **Armor reflected:** when an equipment/armor system exists, equipped pieces **override** the
  matching body part's look (helmet → head, chestplate → torso, etc.). Design the avatar so
  parts are individually re-skinnable to make this drop-in later.
- **First/third-person toggle:** a key (e.g. **V**) and a setting switch the camera between the
  existing first-person view and a third-person follow camera that frames the avatar (with
  collision so it doesn't clip terrain/ship). In first person the avatar is hidden (or arms
  only); in third person it's fully shown and animated (idle/walk later).
- Depends on: the player rig (done), `PlayerStateUpdate` for others (M24 multiplayer presence)
  to show other players' avatars. Customization colours can ship before multiplayer.

### M23 — Navigation, missions & feedback — **DONE (code) / pending playtest**
- **Star map** tab in `GameMenu` (`RequestStarMap`/`StarMapData`): lists systems + bodies and
  marks "you are here". Opened from the **cockpit** station (E).
- **Mission log** tab (`RequestMissions`/`MissionList`): available missions (Accept) + active
  missions with progress (Turn in) via `Accept/TurnInMissionIntent`; result as a toast.
  (Player-created missions deferred.)
- **Feedback:** `RespawnNotice`, `ServerRules` (mode/PvP summary on join) and
  `CraftResult`/`ActionRejected`/`ServerMessage` all surface as HUD toasts.
- Data-driven tabs re-request on open. All over existing protocol — no server change.

### M24 — Multiplayer presence — **partly DONE (presence) / docking-UI + join-polish pending**
Implemented:
- Server **broadcasts presence** (`PlayerPresence`, ~10 Hz) of each player to the others with
  position + heading + **avatar colours**, sends existing players to a newcomer, and announces
  `PlayerLeft` on disconnect. Clients send their colours on join (`SetAppearanceIntent`).
- Client `RemotePlayers` renders each other player as a coloured blocky avatar with a floating
  **nameplate**, interpolated to the latest position. (Verified by server-side presence tests.)

Remaining in M24 (still pending):
- **Docking UI**: request/accept/decline/undock buttons (`DockRequest*`/`DockResponse*`/`Undock`,
  `DockRequestNotice`/`DockStatus`) — server M18 already enforces it.
- **Join/host polish**: protocol-mismatch warning (`JoinRejected`), disconnect/reconnect and
  server-unreachable handling in the shell.
- Not playtestable in singleplayer (needs 2 clients / LAN).

### M25 — Space flight & combat (client for M19) — **DONE (code) / pending playtest**
- **Ship hull/shield HUD** (`ShipCombatStatus`) shown in the vitals panel + Space tab.
- **Space console** (GameMenu "Space" tab): launch/return (`EnterSpace`/`LeaveSpace`,
  `SpaceState`/`SpaceClosed`), lists entities (asteroids/drones/UFOs) with **Fire** buttons
  (`FireWeaponIntent`; asteroid-breaker vs ship-cannon by target). "IN SPACE" HUD indicator.
- **Planet enemies**: rendered in 3D as blocks (`WorldEntities` from `PlanetEnemyList`),
  attacked with **F** (nearest within reach → `AttackEntityIntent`).
- Singleplayer enables free flight + PvE NPCs via `LocalServerLauncher` flags (new
  `--free-flight/--space-combat/--space-npcs` overrides) so it's reachable solo.
- Deferred: a true 3D flyable cockpit/flight camera (the server models space abstractly);
  weapon selection UI (uses asteroid_breaker/ship_cannon_1 by target kind for now).

### M25b — Real space view + launch/landing sequences — **in progress (code)**
Implemented: `SpaceView` builds a space scene on entry — **black sky** + starfield + two planets
+ a code-built **flyable ship** + the server's entities from `SpaceState` — and takes over the
camera (sets solid-black clear so the blue sky is gone). The ship **flies** with WASD + mouse in
**third-person**/**cockpit** (V cycles); on-foot control is frozen (`SpaceViewActive`). A
**launch sequence** (ship rises + fade) plays on entry; **L** (or the Space-tab "Return to
surface") flies you home with a **landing sequence**. Combat still uses the M25 console +
authoritative messages.
Still planned: the third option — **board & walk inside the sealed ship in space** (interior
scene); real flyable controls; nicer ship/entity models with the art pass.

**Space HUD radar — minimap + edge arrows (planned):** in ship/space mode the HUD shows nearby
**asteroids, planets, enemies and other players** as a **radar minimap** (top-corner blips) **and
off-screen arrows at the screen edge** pointing to each contact, **colour-coded by allegiance:
white = neutral** (asteroids, planets, neutral NPCs), **blue = friendly** (allied/docked players),
**red = hostile** (enemy drones/UFOs, hostile players). Each contact carries a position + a
neutral/friend/hostile flag; blips on the minimap and arrows for anything outside the view frustum
(arrow clamped to the screen border, optionally with distance). Data comes from `SpaceState`
entities (which already carry `Hostile`) + planets in the scene; **other players in the same space
instance** need their positions broadcast (small addition to the space instance, like surface
presence). Allegiance: neutral by default, hostile from `Hostile`/PvP, friendly from docking/teams
(once teams exist). Client-side HUD over the authoritative entity list; sequence with M25b + the
space-flight deepening.

**Consistent ship appearance (important):** the player's ship must **look identical in space and
on the planet** — same hull shape, size, colours and module props. Today the planet-side ship is
the **voxel hull** (`StampShip` from the active `ships.json` design) and the space-side ship is a
separate code-built placeholder, so they differ. Fix: build the **space ship model from the same
ship design** (the same block layout `StampShip` uses) — e.g. mesh the design's voxel blocks into
the flyable space object (and reuse it for the third-person/cockpit views) so both views render
one consistent ship. When the design changes (switch/upgrade/repaired wreck) both views update
together. Server already owns the design; this is a client rendering change shared by the
ship-as-place (M23a) and `SpaceView`.

Original design (three modes the player can switch between):
1. **Cockpit view** — first person from inside the ship looking out (HUD overlay, viewport).
2. **Third-person ship** — the ship rendered from outside, floating in a space scene (starfield
   skybox, the planet below, asteroids/drones/UFOs as objects at the server's entity positions).
3. **Board & walk inside** — switch to the on-foot character *inside* the sealed ship (hatch
   closed) and use the interior stations (cockpit to fly, etc.).

Build a lightweight **space scene** on entry: a dark starfield backdrop + a planet sphere +
blocky placeholder models for the ship and entities (code-built, like the avatar), positioned
from `SpaceState`. Combat (fire/hull/shield) drives the same authoritative messages; the view
is presentation only.

**Launch & landing sequences:** short scripted animations — **launch** (engine flare, the ship
rises off the landing pad and the planet recedes into the space scene) when entering space;
**landing** (descent, dust, settle onto the landing zone) when returning. Skippable; purely
visual, gated by the same `EnterSpace`/`LeaveSpace` round-trip. A "Settings → reduced effects"
toggle shortens them.

Scope: client-side presentation over the existing M19/M25 protocol; no server change required
(the server already tracks in-space + entities). Sizeable Unity work — schedule with the art
pass.

### Hyperspace travel between star systems — NEW (planned)
Travel to **other star systems** happens via a **hyperspace jump** (builds on the M11 universe +
star map):

- **Jump generator (per ship):** every ship has a **hyperspace jump generator** — a ship module
  (in `ships.json` / module list, like the reactor/cockpit) required to jump. A ruined/under-
  built ship (see wrecks) without a working generator can't jump until it's repaired/built. Later
  knobs: a **charge/cooldown** and an **energy/fuel cost** per jump, and a **range/tier** (better
  generators reach farther systems) — all server-authoritative.
- **Initiating a jump:** from the **star map** (cockpit → M23) the player selects a **destination
  system** and confirms the jump. The server validates (generator present + charged + in space /
  cleared to leave), then moves the player/ship to the target system (swaps the active space
  instance / star-system context) — authoritative; the client never decides the destination.
- **Hyperspace jump animation:** a short scripted sequence in `SpaceView` — the ship aligns, the
  generator **charges**, then on jump the **starfield stretches into streaks** (stars elongate
  along the travel axis, a speed-tunnel/warp-streak effect, brief bloom/flash) for the duration,
  then **drops out** into the destination system's scene (new sun colour, planets, entities).
  Purely visual over the authoritative travel; a "reduced effects" setting shortens it. Local
  in-system hops (planet ↔ orbit) keep the existing launch/landing sequences — hyperspace is only
  for the **system-to-system** leg.
- Reuses: the **star map** (M23) for selection, **`SpaceView`** for the scene + the new warp
  animation, the **ship-module/build system** for the generator, and the universe/star data (M11)
  for systems + their sun colours. Server owns the generator requirement, jump validation and the
  system switch; the client renders selection + the warp streaks + arrival. Sequence with M25b +
  the star-map travel work.

### Space collisions, asteroid mining & tractor beam — **breakable asteroids DONE / rest planned**
Implemented (server): space asteroids now have a **size tier** (`CombatEntity.AsteroidTier`:
2 large / 1 medium / 0 small) and **split on destruction** — shooting a **large** asteroid yields
**smaller chunks** (no loot), which split again, and only the **smallest** break into **mineral
drops** (iron/titanium), reusing the existing `FireWeapon` + `AsteroidDestruction` gating. Space
instances spawn large asteroids. 2 tests (large → smaller chunks, no loot; breaking all the way
down eventually drops ore). Still planned (below): **collision damage** (flying into an asteroid
hurts hull/shield) and the **tractor beam** add-on (pull floating drops into the cargo hold) —
both need an authoritative ship **position/velocity** in the space instance (abstract today).

Original notes (remaining work):

Make free-space flight (M25/M25b) tactile and rewarding (server-authoritative):

- **Collision damage — server foundation DONE:** the ship now has an authoritative
  **position** in the space instance (`SpaceInstance.ShipPosition`, set via `ShipMoveIntent` /
  `ShipMove`). `TickSpace` computes impact **speed** from the per-tick position delta and, when the
  ship overlaps an asteroid, applies **hull/shield damage scaled by speed** (capped) and **stops/
  bounces** the ship; ship-defeat still recovers via `DisableShip`. 2 tests (flying into an
  asteroid damages the ship; drifting in open space doesn't). The client sender
  (`NetworkClient.SendShipMove`) exists but isn't wired yet — **remaining:** SpaceView must fly in
  the **server's entity coordinate space** (M25b real-flight) before reporting position, plus the
  client hit effect.
- **Breakable asteroids:** shooting an asteroid with ship weapons damages it; a **large asteroid
  first splits into several smaller chunks**, which split again, and the smallest **break into
  (non-organic) resource drops** (ores/metals/ice). Reuses the M19 `FireWeapon`/entity-hull path
  with a size tier + split-on-destroy.
- **Tractor beam (ship add-on):** a new ship equipment module (like weapons — built/blueprinted)
  that **pulls in nearby resource drops** in space and stows them in the **cargo hold until it's
  full**. Server resolves pickup (range + cargo capacity); client shows the beam + cargo filling.
- Depends on giving the ship an authoritative **position/velocity in the space instance** (M25 is
  abstract today) — schedule with the M25b flight deepening. Tractor/weapons fit the existing
  ship-module + build system.

### Combat loot: salvage & corpses — **lootable containers DONE / ship salvage planned**
Implemented (server + protocol + client hook): the M10 death-drop salvage capsule is now a
**lootable container**. `GameServerContainers` tracks world containers (loaded at start, persisted),
the death path drops one with the victim's carried (non-tool) items, and **`LootContainer`** lets a
nearby player transfer the contents into their inventory (proximity-validated; partial loot if the
inventory is full; the capsule **despawns once emptied**). Sent via `ContainerList`/`NetContainer`
(on join + on change); `LootContainerIntent` + a client hook (**G** loots the nearest container).
This generalises the salvage capsule into the **lootable corpse** others can open. 3 tests (death
drops a lootable capsule, looting transfers + despawns, out-of-range rejected). Suite 161 green.
Still planned (below): **PvP corpses** carrying the *full* inventory (needs PvP damage), and
**ship-salvage drops** in space collected by the tractor beam.

Original notes (remaining work):

Reward winning fights (only where the world's rules permit combat — `SpaceCombat`/`ShipWeapons`/
PvP settings; never in protected zones):

- **Destroyed ships drop cargo:** when a **player's ship** or a **hostile NPC/alien ship** is
  destroyed in space, it spawns **salvage** — (part of) its cargo as resource drops floating in
  the instance, collectable with the **tractor beam** into your hold. The fraction salvageable
  is a server/rules knob (harder/PvP servers drop more). Ties into the M19 ship-defeat path
  (which already recovers the owner's ship without permanent loss) — the salvage is the *cargo*,
  not the ship.
- **Planet PvP corpses:** defeating another **player on a planet** (where PvP is enabled) leaves
  a **lootable corpse** at the death site holding the **entire carried inventory**; the killer
  (or anyone) can loot it. The defeated player **respawns at their heal-tank without that
  inventory** (subject to `KeepInventoryOnDeath`). The corpse **persists at the spot until it's
  emptied** (then despawns). This generalises the existing M10 salvage capsule into a lootable
  container others can open.
- All server-authoritative: who may attack whom, what drops, who may loot (zone/PvP rules), and
  the respawn. Clients render salvage drops / a corpse container and use the tractor / an open-
  container interaction.

### Landable asteroids — **content slice DONE / travel-to-land planned**
Implemented (server + data + client sky): an **`asteroid`** body type — small (`worldRadius` 220),
**crystalline** surface (crystal/stone, crystal-rich ores), **no life** (`creatureAbundance` none →
empty roster) and **no flora** (`floraDensity` 0), **airless** (`atmosphere` "none" → oxygen
drains), with a permanent **space sky** (`PlanetType.SpaceSky` → `WorldEnvironment.SpaceSky`; the
client `Sky` keeps the camera space-black on the surface, sun still tints). Excluded from the
random universe planet pool (`PlanetType.Selectable = false`). Playable now by setting it as the
start planet; 5 tests (airless/space-sky/non-selectable flags, crystal surface + no flora, no
creatures, not in the random pool, drains oxygen). Still planned (below): **fly to it and land**
via the star map / space view, a starfield on the surface, and the visible system sun disc.

Original notes (remaining work):

Big asteroids you can **land on** and walk around — tiny airless worlds:

- A new celestial-body kind **large asteroid** in systems; you fly to it (free flight / star map)
  and **land** like on a planet. Generated as a small body (low `WorldRadius`, rocky surface).
- **No atmosphere:** outside the ship the suit's oxygen drains (no regen), so it's a hazard run.
  **No weather**, **no day/night atmosphere** — the **sky is always space (black + stars)** even
  on the surface, but the **system's sun is visible** and its **colour tints the asteroid**
  (reuse `_Sc_Light` + the system sun colour; a fixed/slow sun, no cycle).
- **Life:** essentially none — **at most rare crystal creatures** and a **rare crystal biome**
  (crystalline growths). Mostly a mining/exploration spot.
- Server marks the body `Airless` + `SpaceSky` + crystal-biased generation; the client skips the
  day/night sky there and keeps the starfield + sun.

### Space stations — NEW (planned)
Boardable **space stations** that exist in a system, **near planets**:

- Stations of **varying size — small to huge** — placed in the system (orbit / near a planet),
  reachable via free flight / the star map; you **dock** (M18) and **board** to walk inside.
- **Interiors:** landing **hangars**, multiple **rooms/corridors**, with interactive points —
  **NPC traders** (buy/sell resources & gear), **aliens/other NPCs**, **mission boards** (take
  station missions), repair/refuel, maybe shared storage. Built as voxel/prefab interiors and
  scaled by station size; bigger stations have more hangars/rooms/vendors.
- Reuses existing patterns: docking (M18), the **ship-as-place** interior + station-interaction
  system (M23a-2), missions (M13/M23), and a new **trading** system (vendor inventories + prices,
  server-authoritative). NPCs tie into the creature/AI work.
- Server owns station placement, interiors, vendor stock/prices and mission boards; clients
  render the station + interior and use dock/board/trade/mission interactions.

### Planet settlements & NPC towns — NEW (planned)
Some planets are **inhabited** — settlements you can walk into, populated by NPCs (the
planet-side counterpart to space stations), server-authoritative:

- **Where & whether:** only **some** worlds have settlements (seed + planet/biome derived,
  admin-overridable) — barren/hostile worlds none, hospitable ones one or more. Placement is on
  buildable surface near the surface height, on a flattened/foundation footprint so buildings sit
  cleanly on the terrain. The settlement is **protected** like a landing zone (can't be griefed/
  mined).
- **Two tiers:**
  - **Primitive villages** — small clusters of simple **single-storey** huts/shelters (a few
    blocky buildings, basic materials matching the biome: wood/mud/stone), a handful of NPCs.
  - **Modern settlements** — larger towns with **multi-storey buildings** (towers/blocks with
    floors, interiors, lifts/stairs), streets, more services and a denser population.
- **Inhabitants:** NPCs are **humans or aliens** (alien look reuses the parametric creature/
  avatar builder + palettes; humans reuse the avatar rig). Mix and density follow the tier and
  the world's biodiversity/abundance level. NPCs idle/wander within the settlement (not full AI);
  some are interactive.
- **Services (like stations):** **mission boards** (take local missions — M13/M23), **NPC
  traders** (buy/sell resources & gear — the same trading system as stations), and later
  repair/refuel, quartermaster/respawn-friendly points, lore/dialogue NPCs. Bigger/modern
  settlements have more vendors and mission boards than primitive villages.
- **Generation:** procedural building layout from a small set of **building templates** per tier
  (footprint + height + door/window pattern), stamped as voxel structures (like `StampShip`),
  scaled by settlement size; deterministic from the seed so it streams like normal chunks.
- **Ruins of abandoned settlements:** some worlds instead (or also) have **derelict settlements**
  — the same village/town building templates generated in a **ruined** state: partial/collapsed
  structures (missing blocks, holes, rubble, overgrown with flora, weathered materials), **no
  living NPCs** (or only hostile scavengers/creatures squatting them). They're **explorable loot
  spots** rather than service hubs: scattered **salvage/loot containers** (resources, data caches,
  rare gear), occasional **derelict mission/lore terminals**, and a creepier, abandoned mood.
  Generated deterministically from the seed by taking a settlement layout and applying a
  **decay/damage pass** (remove a fraction of blocks, scatter rubble, swap intact→worn blocks,
  let flora reclaim it). Unlike live settlements they're **not protection-gated** — the player can
  mine/scavenge them freely. Reuses the same building templates + voxel stamping; ties into loot
  containers (combat-loot system), creatures (squatters) and flora (overgrowth).
- Reuses: voxel structure stamping (ship/station), **station-interaction** prompts (M23a-2) for
  boards/vendors, missions (M13/M23), trading (with stations), and the avatar/creature renderer
  for NPCs. Server owns settlement placement, buildings, NPC roster, vendor stock and mission
  boards; clients render them and use the walk-up interactions. Sequence with **space stations +
  trading + creatures** (shared systems).

### Crashed ship wrecks — NEW (planned)
**Rare** abandoned/crashed spaceships scattered on planet surfaces — small landmark dungeons:

- **Rarity & placement:** a **rare** seed-derived feature (much rarer than settlements) — most
  worlds have none; occasionally one (or a few on large worlds). Stamped as a **derelict voxel
  ship hull** (reuse the ship designs from `ships.json` / `StampShip`) in a **crashed** pose:
  tilted/half-buried in the terrain, with a **decay/damage pass** (breaches in the hull, missing
  blocks, scorch/scattered debris and a small impact crater/scrape around it).
- **Explorable & lootable:** no living crew (or only **hostile scavengers/creatures** nesting in
  it); the player walks into the broken hull to **salvage** — **loot containers** (resources,
  components, rare gear), a **cargo hold** to empty, recoverable **ship modules/blueprints**, and
  occasional **data caches / distress-log lore terminals**. Not protection-gated — free to
  scavenge and dismantle.
- **Variety:** different **ship types/sizes** (matching `ships.json`) and origins (human or
  **alien** wrecks, alien ones using alien block/palette variants) so wrecks feel distinct;
  bigger wrecks hold more loot and more squatters.
- **Repairable → a usable ship (important):** a wreck can be **restored** into a working,
  flyable ship the player **owns**. Because a wreck is a known `ships.json` design with a
  decay/damage mask, repair = **rebuilding the missing/broken blocks** of that design: the player
  brings materials and **fills the breaches** (place the design's hull blocks back, reusing the
  normal build/place + ship-module build flow), and once the hull is **complete enough** (and the
  required modules — cockpit/reactor/etc. — are present/repaired) the server **claims it into the
  player's owned-ships registry** and it becomes flyable (lifts off, star-map travel). Drives a
  meaningful mid-game goal: find a rare wreck, clear the squatters, invest materials, **gain a new
  ship** (often a bigger/alien hull than you could craft yet). A partially-repaired wreck shows
  **progress** (% hull restored / which modules still missing). Server-authoritative: it owns the
  per-wreck repair state, validates each placed block against the design, and the claim/ownership
  transfer. Ties into **Ships: types/designs + multiple owned ships** (the registry, switching)
  and the **module build** system.
- Reuses: ship voxel stamping + the **decay pass** shared with settlement **ruins**, **loot
  containers** (combat-loot system), **creatures** (squatters), missions (a wreck can be a
  mission objective — "investigate the distress signal"), and the **ship build/registry** for
  repair-and-claim. Server owns wreck placement, the decayed layout, loot tables, squatter spawns
  and the repair/claim state; clients render + use loot/interact/repair. Sequence with
  **settlements/ruins + combat-loot + creatures + the ship registry** (shared systems).

### Ships: types, designs, expandable interiors & multiple owned ships — **slice DONE / extras planned**
Implemented (server + data + minimal UI): data-driven `data/ships.json` (starter/hauler/scout)
+ `ShipDefinition` in content; an owned-ships registry with an active ship; `CraftShip`
(blueprint + cost validated) and `SwitchShip` (re-stamps the design, applies base hull/shield);
`CraftShipIntent`/`SwitchShipIntent`/`OwnedShips` protocol; the hull size is derived from the
active design; the Ship tab lists owned ships (switch) + craftable types. 5 fleet tests pass.
Still planned: per-ship **persistence** of the whole fleet (owned ships are in-memory this
slice), **expandable interiors** (rooms grow with modules), and richer designs/props.

Original notes:
- **Ship types & designs (craftable):** a data-driven `data/ships.json` — each type has base
  hull/shield, module slots, a **design** (the hull block layout `StampShip` builds) and a
  craft cost / blueprint. The current hull becomes the default "starter" design. New ships are
  unlocked + crafted like modules/blueprints.
- **Expandable interiors:** `StampShip` lays out **rooms per built module** so adding modules
  visibly enlarges the ship (more/larger rooms), not just stats.
- **Multiple owned ships + switching:** the server tracks a player's **owned ships** with one
  **active**; a `SwitchShipIntent` swaps the active ship (re-stamps its design at the landing
  zone, swaps modules/cargo). Only the active ship is simulated/flown.
- **Client:** a **Hangar** UI (tab/screen) to view owned ships, craft new types/designs and
  switch the active one. Server stays authoritative over ownership/active selection/craft.
- Builds on M23a (ship-as-place) + the module system; needs a ship registry + persistence of
  owned ships. Sizeable — schedule after the core client (M26–M28) or interleave per appetite.

### World systems: fluids, weather, day/night & star light — NEW (planned)
Bigger world-simulation features (server-authoritative; the client renders the result):

- **Fluids (water & lava) — slice DONE (server):** `GameServerFluids` runs a cellular-automaton
  flow (down, then sideways with level decay; sources persist; per-tick cap), broadcasts
  `BlockChanged`; water/lava blocks + placeable items + atlas colours; lava burns players on
  contact. Tests cover fall + sideways spread. Still planned (the bullet below): worldgen
  lakes/lava pools, swimming/buoyancy, transparency, buckets, water+lava→stone.
- **Fluids (water & lava):** flowing liquids like Minecraft — source blocks spread to lower/
  adjacent cells with a flow level, settle, and stop; lava damages, water can be swum through
  and the two interacting (lava + water → stone/obsidian) is optional later. Server owns the
  cellular-automaton flow (a `fluid` block layer + a tick that updates changed cells and
  broadcasts `BlockChanged`); the client renders fluid blocks with animated/translucent
  textures. Needs: fluid block types, a flow tick (rate-limited, bounded per tick), and
  buoyancy/damage in the movement/vitals rules.
- **Day/night, sun colour & weather — slice DONE (server + client tint):** a server world clock
  advances time of day (per-planet `DayLengthSeconds`); a weather state machine cycles
  clear→clouds→rain→storm biased by `StormChance` — and planets can be fixed `clear`/`overcast`
  (no weather) via `PlanetType.Weather`. Sun colour comes from the active star system. Broadcast
  via `WorldEnvironment` (join/change/periodic). Client `Sky` drives a **global shader tint**
  (`_Sc_Light`, multiplied into the block shaders so the unlit world responds), the sky colour
  and a rotating sun light; time advances locally between updates. Still planned (below): **blocky
  voxel clouds** (see next bullet), rain/storm **particles + lightning**, ambient weather sound,
  biome-flavoured variants.
- **Blocky voxel clouds (planned):** clouds are **made of cubes** (klötzchen, matching the voxel
  look) — not flat sprites — assembled into soft cloud clumps that **float above the surface and
  slowly drift** across the sky (wind direction/speed, faster/denser in worse weather). They are
  **non-solid / pass-through**: the player (and ships) **walk and fly straight through them** — no
  collision, render-only — and they don't block mining/placing. **Per-planet colour:** clouds are
  tinted by the world (white on Earth-like worlds, **yellow** on a sulphur/desert world, **blue**
  on an ice/ocean world, ashen-grey on a lava world, …) — a `cloudColor` on the planet/environment
  (or derived from the atmosphere + sun colour), carried in `WorldEnvironment`. Cloud **cover/
  density** scales with the weather state + intensity (clear → few/none, overcast/storm → many/
  dark). Client-side, code-built cube clusters (like the avatar/creatures, no art asset);
  server stays authoritative over the weather state that drives cover. Sequence with the weather
  particles + art pass.
- **Day/night & weather, per planet:** a server world-time clock drives sunrise → day →
  sunset → night; **each planet type** has its own day length and **weather profile**. Weather
  states: **clear, clouds, rain, thunderstorm** (with lightning), plus planet-flavoured variants
  (dust storm, snow, ash). **Intensity varies per planet** (a desert rarely rains; an ocean
  world storms often; a lava world has ash) and a storm's strength scales effects + the M9
  `EnvironmentalHazards` ties. The server owns the weather state machine (per planet, seed +
  time driven) and broadcasts time + weather + intensity; the client drives the skybox, cloud
  cover, a directional "sun" light, rain/snow/dust **particles**, lightning flashes and ambient
  sound. Heavier weather = lower visibility / stronger particles.
- **Star light colour:** each star system's sun has a colour (white / yellow / blue / reddish)
  from the universe generator; it tints the planet's directional light + ambient, so worlds in
  different systems *look* different. Carried in the world/star data → sent to the client →
  applied to the sun light colour. Ties into the M25b space-scene sun as well.

These are sizeable; sequence them with the art pass and the space-view work. All authoritative
on the server (a client must not decide fluid spread, time of day or hazards).

### World variety: size & biomes/habitats — **slice DONE / extras planned**
Implemented: `PlanetType` gains a **biome list**, a **WorldRadius** and the weather fields; the
`WorldGenerator` is biome-aware — single-biome planets use one surface, multi-biome planets pick
a surface per column from low-frequency noise, and **how many biomes a multi-biome world uses is
randomised per world from the seed** (2..pool). New biome blocks (sand/mud/grass/crystal) +
items + atlas colours; new planet types (desert/jungle/crystal/swamp + a multi-biome "varied").
2 generation tests. Still planned: enforcing **world size** bounds, smooth biome blending/
transition blocks, biome-specific ores/caves, and habitat-matched flora & creatures (below).

Original notes:

- **World size:** planets vary in size — small moons to large worlds — affecting the playable
  surface extent / world bounds, chunk count and how much there is to explore. The world
  descriptor (seed-derived, admin-overridable) carries a size; generation + streaming respect it.
- **Single- vs multi-biome worlds:** a planet is either **single-habitat** — all **ice / lava /
  forest / jungle / crystal / rock / sand / mud** — or **multi-biome**, with several regions
  blended across the surface. The biome drives the **surface/sub-surface blocks**, fluids
  (oceans on water worlds, lava seas on lava worlds), the **weather** flavour, and which **flora
  & creatures** spawn. Extends the current planet types (`planets.json`: rocky/ice/lava) into a
  biome system.
- **Habitat-matched life:** creatures and flora (if any — per the abundance level) **match the
  biome/habitat** they're in: ice fauna on ice worlds/regions, lava-dwellers in lava seas,
  jungle creatures in jungle, crystalline growths on crystal worlds, etc. A multi-biome world
  has different life per region; a barren world has none.
- Server owns size + biome layout + matched spawning; clients render the varied surface,
  flora and creatures. Foundational for the creatures/flora systems below — sequence the biome
  generator first, then habitat-matched life.

### Atmosphere & breathability — **slice DONE / view-distance extra planned**
Implemented (server + HUD): `PlanetType.Atmosphere` ("breathable" / "toxic" / "none", default
toxic) per planet (jungle/swamp/varied breathable; rocky/desert toxic; lava/crystal airless). The
oxygen tick keys off it: on a **breathable** world the suit **regenerates** oxygen on the surface
(no drain), while **toxic/airless** worlds **drain** as before (the global `OxygenEnabled` rule
still gates it; aboard always regenerates). Broadcast via `WorldEnvironment.Breathable`; the HUD
marks oxygen "(breathable)". 4 tests (breathable no-drain, toxic drains, airless drains, reported).
Still planned (below): the atmosphere-driven **view-distance / fog** range.

Original notes (remaining work):

Each world's atmosphere determines whether the suit consumes oxygen outside the ship:

- A per-planet **atmosphere type**: **breathable** (no suit oxygen drain — the player can roam
  freely), **non-breathable/toxic** (suit oxygen **drains** outside the ship, as today), or
  **none** (airless — drains; used by the landable asteroids). Add `PlanetType.Atmosphere`.
- Server oxygen rule (in the environment tick) keys off the **current planet's atmosphere**:
  breathable → regenerate/no drain even on the surface; toxic/none → drain at the configured
  rate (the existing `OxygenConsumption`/`OxygenEnabled` rules still gate it globally). Aboard
  the ship always regenerates (life support).
- Client: show the atmosphere/breathable state in the HUD (e.g. the oxygen icon dimmed when
  breathable). Small server slice on top of the existing oxygen system — implement with the
  atmosphere/asteroid work.
- **View distance varies with atmosphere:** a planet's atmosphere also sets a **visibility /
  fog distance** — a thick/hazy atmosphere (e.g. swamp, dense weather) sees **less far** (near
  fog), a thin one sees **farther**, and an **airless** body (asteroid/no-atmo) has the
  **clearest** view (space-like, far fog). Carried as a per-planet **fog/visibility range** in
  the atmosphere descriptor (`WorldEnvironment`), scaled further by current **weather intensity**
  (storms cut visibility). The client applies it as camera fog distance/colour (tinted by the sun
  colour); this is presentation, but the range is **server-supplied** so all clients agree. The
  existing **view-distance setting** stays a client cap — the atmosphere can only reduce it,
  never force-stream more chunks.

### Hunger & eating (survival) — **slice DONE (server + HUD) / extras planned**
Implemented (server-authoritative): a `PlayerState.Hunger` vital (0..100, persisted) that **drains**
outside the ship (`Rules.HungerDrainPerSecond`), **sates** aboard the ship / when disabled, and
**starves health** at 0 (health loss until you eat). Gated by `Rules.Hunger` + Survival
(`HungerEnabled`); GodMode and respawn refill it. **Eating restores hunger** via the consume
system (`ItemDefinition.ConsumeHunger`): `creature_meat` (+30) and **edible plants** — `flora_plant`
now also drops **`berries`** (+18 hunger), a consumable food — while **poison** (`toxic_gland`)
still harms. Hunger rides on `PlayerStateUpdate`; the client HUD shows a **Hunger** vital. 6 tests
(rule gating, drain outside / no-drain aboard, starvation, meat + berries restore). Still planned
(below): the **detoxifier** module (poison → safe food), poison **status effect** over time,
sprint-faster drain, and a real hunger HUD icon.

Original notes (remaining work):

A survival need that makes food matter (server-authoritative; builds on the consume system):

- **Hunger vital:** add `PlayerState.Hunger` (0..100, like health/oxygen). It **drains slowly**
  over time (faster when sprinting/active), shown in the HUD next to the other vitals. While
  **aboard the ship** (life support) it doesn't drain (or refills). Survival-only — disabled in
  Creative and gated by a `Hunger`/survival rule.
- **Starvation damage:** when hunger hits **0** the player takes **health damage over time** (and
  maybe reduced stamina/speed) until they eat — it can lead to death/respawn like any hazard.
- **Eating restores hunger:** consuming **edible** items refills hunger (and some also heal). Food
  comes from **creatures** (e.g. `creature_meat`) and from **edible flora** — a plant can be eaten
  **only if it has the `edible` property and is not `poisonous`**. **Poisonous** items (toxic
  glands, poison flora) instead **harm** the player (damage / a short poison effect), so the
  player must learn what's safe. This extends the flora **effect tags** (poison/heal/food) and the
  creature **drop kinds** (food/poison/material) already planned.
- **Consume system (foundation, partly here):** the `ConsumeItemIntent` + `ItemDefinition`
  consume-effect added with the creature slice currently affects **health**; the hunger system
  adds a **hunger** restore value to edible items (food restores hunger primarily, medicine heals)
  and the drain/starvation tick. Plants become eat-consumable when harvested (the material drop is
  edible if the species is food-type).
- **Detoxifier (craftable ship module) — DONE:** a buildable `detoxifier` ship module (blueprint +
  build cost, via the existing module-build flow) that adds a **`Detoxifier` crafting station**.
  At it, the **detoxify recipe** converts poison into safe food (`toxic_gland` → `creature_meat`),
  reusing the whole crafting + station-gating system: it only works aboard a ship that has the
  module installed (server-authoritative). 3 tests (recipe wired, converts with the module,
  fails without it). Still planned: a poison-flora input once that exists, and an optional energy
  cost/throughput.
- Server owns the hunger tick, starvation damage and what each item restores; the client shows the
  hunger bar and sends the eat/consume intent. Sequence with creatures + flora effects.

### Procedural creatures & aliens — **slice DONE (server + client render) / extras planned**
Implemented (server + worldgen + data): a seed-derived **species roster per world**
(`CreatureGenerator`) sized by the planet's **biodiversity** (`PlanetType.CreatureAbundance`:
none → 0, few → 3, many → 6). Each `CreatureSpecies` carries **stats** (size/health/speed/attack),
**temperament** (passive/skittish/territorial/aggressive/pack-hunter), an **activity cycle**
(diurnal/nocturnal/crepuscular/cathemeral), a **habitat** (land/water/lava/air), parametric
**appearance** (legs/wings/tail/segments/colour/glow) and a **drop** tagged food / poison /
material-substitute. The roster skews **non-hostile** (most species don't attack). Server
(`GameServerCreatures`): live creatures spawn near surface players within a biodiversity cap,
habitat-gated (water/lava species only spawn in that fluid); **only hostile + awake** creatures
deal proximity damage (they **sleep** in their off-phase via the day/night clock) and **only where
the hostility rules allow** (peaceful servers keep wildlife harmless, §12.4). Defeating one
(`AttackEntity`, now shared with planet enemies) drops its **species material** — a building
resource (substitute), `creature_meat` (food) or `toxic_gland` (poison). A **consume system**
(`ConsumeItemIntent` + `ItemDefinition.ConsumeHealth`) makes **food heal** and **poison harm**
(also wires `medpack`). Sent to clients via `CreatureList`/`NetCreature` (full descriptor for the
future parametric renderer). 10 creature tests; suite 127 green. **Client render DONE:**
`CreatureBuilder` assembles a blocky body from the descriptor (segments/head/eyes/legs/wings/tail,
species colour, hostile tint, dimmed when asleep, a point-light glow for bioluminescent species)
and `CreatureView` syncs/interpolates them from `CreatureList`; **F** attacks the nearest creature
too (shared with enemies). **Movement AI DONE:** a pure, tested `CreatureBehaviour.Step` drives
the server — hunters (aggressive/pack) **approach** a player in aggro range, **skittish flee**,
the rest **wander**, and **sleepers don't move** (off their activity phase); the server moves
creatures each tick (capped per step) and re-broadcasts positions at ~2 Hz for the client to
interpolate. **Territorial retaliation DONE:** attacking a creature that **retaliates** (territorial,
or already-hostile) **provokes** it (`CombatEntity.ProvokeTimer`) — for ~12 s it acts aggressive
(hunts + bites back, reads as hostile/red to clients), then calms down; a **pack-hunter rallies
nearby kin** of its species when provoked. Hooked into the shared `AttackEntity` path, so crafted
weapons trigger it. Still planned (below): true **flock** movement coordination, pathing/terrain-
follow, water/lava-volume spawning beyond the player's cell, nameplates, and the hunger/eating
survival loop (separate plan).

Original notes (remaining work):

Make planets feel alive with **procedurally generated lifeforms** — each world deterministically
derives its own species from the world/planet seed, so different planets have different,
surprising aliens. Extends the M25 planet-enemy system into a full creature system (passive +
hostile), server-authoritative.

- **Per-species procedural descriptor** (generated server-side from the seed): randomized
  **stats** (size, max health, speed, attack damage), **temperament** (passive / skittish /
  territorial / aggressive / pack-hunter), **diet/behaviour** (grazes, flees, hunts, ambushes,
  flocks), and **appearance** — a blocky body assembled from parts (body segments, head, legs
  count, optional wings/fins/tail) with a colour palette. The descriptor is sent to clients so a
  `CreatureBuilder` renders the *same* creature everywhere (like the avatar, but parametric).
- **Activity cycle:** each species is **diurnal / nocturnal / crepuscular** and can **sleep** —
  it rests (idle, reduced perception, won't wander/hunt) during its inactive phase and is active
  in its phase. Ties into the day/night clock (World systems): nocturnal predators come out at
  night, diurnal grazers sleep then, etc. Sleeping creatures are easier to avoid or surprise.
- **Habitat (where it lives):** each species is **aquatic / lava-dwelling / land / airborne**.
  Habitat governs **spawning** (aquatic in water bodies, lava-dwellers in lava, fliers in the
  air volume, land on the surface), **movement** (swim / fly / walk), and **survival** (a
  lava-dweller is immune to lava, an aquatic one suffocates on land, etc.). Depends on the
  fluids system (water/lava) for aquatic/lava habitats.
- **Abundance per world (none / few / many):** a world **biodiversity** level — derived from the
  planet type + seed and overridable via server settings (ties into `PlanetEnemies`,
  `PassiveCreatures`, `AggressiveAliens`). Barren worlds have no life; lush ones teem with it.
  Controls how many species exist and total spawn caps; hostility split follows the alien/enemy
  rules (peaceful servers → passive only, §12.4).
- **Server-authoritative:** species generation, spawn placement (habitat + biome + abundance),
  AI/behaviour and combat all live on the server; clients render descriptors + positions and
  send `AttackEntityIntent` as today. Scales the existing `PlanetEnemyList`/`AttackEntity` path
  to carry the species descriptor + habitat.
- Sequence after fluids (for water/lava habitats) and with the art pass (for nicer creature
  models); the parametric blocky renderer works without bundled art.

### Player weapons: melee & ranged — **slice DONE (server + data) / extras planned**
Implemented (server + data): six craftable **personal weapons** as `ToolKind.Weapon` tools with new
`ToolProperties.Damage` + `Range` — **melee** `machete` / `vibro_knife` / `plasma_sword` (short
reach, rising damage) and **ranged** `gauss_pistol` / `laser_pistol` / `plasma_blaster` (long
reach; energy ones draw `SuitEnergy` per shot). The shared `AttackEntity` path is now weapon-aware:
the held weapon's **range gates reach** (so ranged hits far, melee only close), its **damage**
decides the hit, and **energy weapons consume suit energy** (rejected when empty). Each has a
**recipe** (workshop) + **blueprint** (category "Weapon", tiered prerequisites) + bilingual
locales, so they appear in the existing crafting/tech UI and the hotbar + **F**-attack use them
with no client change. 3 tests (stats, ranged-hits-beyond-melee, energy-empty rejection); suite
141 green. Still planned (below): **ammo** for kinetic guns, projectile/hitscan **VFX**, PvP
damage + WeaponMode gating, damage types vs armor/creature resistances, reload/overheat.

Original notes (remaining work):

Craftable **personal weapons** for the player character (distinct from ship weapons), to fight
creatures and — where PvP is enabled — other players (server-authoritative):

- **Melee weapons:** e.g. **machete**, **vibro-knife**, **plasma sword** — short reach, swing on
  click, scaling damage by tier. These extend the existing **tool** model: a weapon is an item
  with `ToolKind.Weapon` (the attack path already grants a weapon-kind damage bonus, see
  `AttackEntity`); melee tiers raise the damage/reach and feed the same hit resolution.
- **Ranged weapons:** e.g. **gauss projectile pistol**, **laser pistol**, **plasma blaster** —
  longer range, fire a shot at the aimed target. Server-authoritative hit resolution (raycast /
  target-id within range + line of sight), per-weapon **damage, range, fire-rate/cooldown**, and a
  cost — **ammo** (projectile) or **`SuitEnergy`/energy cells** (energy weapons). Reuses the
  `EnergyPerUse` idea already on tools.
- **Crafting & progression:** all are **crafted/blueprinted** like other gear (`data/items.json`
  weapons + `recipes`/blueprints, gated by tech unlocks and materials) — a clear melee→ranged and
  low→high-tier progression (machete → plasma sword; gauss → laser → plasma blaster). Damage types
  could interact with creature/armor resistances later.
- **Combat integration:** drives the same authoritative damage path used for creatures (and PvP
  corpses / loot where rules permit); the held weapon + its stats decide the outcome — the client
  only sends the attack/fire intent (extends `AttackEntityIntent`, plus a ranged fire intent) and
  plays the swing/shot effect. Bilingual item names/descriptions (DE/EN).
- Reuses: the **tool/inventory/hotbar** system, **crafting + blueprints**, the **attack** path
  (creatures/PvP), and `SuitEnergy`. Server owns damage, range, cost and hit validation. Sequence
  with creatures + combat-loot.

### Lighting: suit lamp, placed lights & glow — NEW (planned)
Make the dark side of the day/night cycle (and caves) playable and atmospheric:

- **Suit/helmet lamp:** the player (always suited) has a toggleable **head lamp** casting a
  **light cone** in look direction. Because the world uses unlit shaders + a global day/night
  tint (`_Sc_Light`), a real spotlight won't brighten blocks — so add a **player-light term to
  the block shaders**: pass the player position + a lamp direction/range as shader globals and
  brighten cells within the cone/radius (combined with `_Sc_Light`). A real `Light` also lit
  props/avatars. Toggle key + battery/`SuitEnergy` drain optional.
- **Placed & ship lights (later):** craftable **light blocks** the player places, and **ship
  exterior lights**, add local light (extra shader light points, or baked brighten radii).
- **Emissive materials & creatures:** some blocks **glow** (e.g. a faint crystal shimmer) and
  some creatures are **bioluminescent** — an emissive term added in the shader (per-block flag
  in the atlas / a creature glow colour) so they stay visible at night and read as alive.
- Server stays authoritative over light *sources that are world state* (placed light blocks,
  glowing ore); the suit lamp + render are client-side. Sequence with day/night (done) and the
  art pass.
- **Reflective materials (planned, render upgrade):** give materials **different reflective
  properties** — matte (mud/dirt), glossy (ice/glass), metallic (iron/titanium), sparkly
  (crystal). This needs moving blocks from the current **unlit** shaders to a **lit** shader
  (vertex normals + the sun + suit lamp), with **per-block material params** (a smoothness/
  metalness/emissive value, supplied via a parallel data channel or extra atlas/UV2). Then the
  sun colour, day/night and lamp produce specular highlights that differ per material. Bigger
  change — schedule with the lighting + art pass; until then the global tint approximates
  lighting without true reflections.

### Procedural flora — **slice DONE / extras planned**
Implemented (server + worldgen + data): **surface flora** seeded by the `WorldGenerator` on
suitable surfaces of flora planets — `flora_plant` on grass/dirt/mud, `flora_crystal` on
crystal/stone/basalt — one plant per column, gated by a per-planet `FloraDensity` roll (jungle
densest, rocky sparse; barren planets get none). New `flora_plant`/`flora_crystal` blocks
(+ atlas colours) and `plant_fiber`/`plant_seed`/`crystal_seed` items + bilingual locale keys.
**Harvest** drops the species **material** (fibre / crystal), not seeds. **Bounded regrowth**
(`GameServerFlora`): a harvested plant **regrows on its cell after a delay only while its host
block underneath is intact** — mine the ground and it won't return; growth is capped (one plant
per host cell, never spreading). **Seeds replant** flora on a **valid host** only
(`HandlePlace` rejects flora on unsuitable ground; `CanPlantFlora`/`IsValidFloraHost`). 6 flora
tests (worldgen seeds on flora planet / none on barren, host validation, regrow-with-host /
no-regrow-without-host). Still planned (below): per-species procedural **form/appearance**,
habitat **water/lava** flora (needs submerged/lava placement), **effects** (poison/heal/food),
and a **maturity → produces-seeds** state (seeds harvestable only from a matured, producing
plant — today seeds are a separate craftable/found item).

Original notes (remaining work):

- **Habitat types:** **land, water, crystalline, and lava** flora — each only grows on/in the
  matching substrate (land plants on dirt/stone, water plants submerged, crystalline on rock,
  lava flora in/around lava). Depends on the fluids system for water/lava flora.
- **Procedural form & appearance:** a species has a **growth form** — tree-like, vine, bush,
  grass, crystal cluster, fungus — assembled procedurally (trunk/stem + branches/fronds + a
  colour palette), so each planet's flora looks distinct. Rendered with the parametric blocky
  builder (or as block clusters); no bundled art needed.
- **Properties & effects (on harvest/consume):** poisonous, healing, food/nutrition, or a
  **material substitute** — e.g. a fibrous plant yields a `cable`-substitute, a woody one a
  building material — so flora feeds crafting. Encoded as drops + effect tags on the species.
- **Growth & regrowth:** species have **growth rates**; harvested flora **regrows over time**
  *as long as the block it spawned on is intact* — mine the host block and it won't return.
  Server tracks flora instances + their host cell + a growth timer; regrowth is a rate-limited
  tick that re-places the plant block and broadcasts the change.
- **Abundance** follows the same per-world biodiversity level as creatures (barren ↔ lush).
- Server owns spawning, growth/regrowth and effects; clients render flora blocks/models and
  send harvest via the normal mine/interact path. Sequence with creatures + the art pass.

### M26 — Audio — **DONE (procedural SFX) / recorded audio + music later**
- Unity **audio module** enabled; `AudioListener` on the player camera; master/SFX volumes from
  `ClientSettings` applied (`ClientAudio`).
- **Procedural SFX** generated in code (no bundled files): mining vs placing (from
  `BlockChanged`), craft success/failure, rejection, ship hit (hull drop). Played via the
  master×SFX bus.
- Later: real **recorded SFX** (ship hums, doors/airlock/docking, alarms, respawn, weapons) +
  **music**, sourced CC0/permissive and recorded in `NOTICES.md`; full music/ambient bus.

### M27 — Art, icons & polish — **in progress**
- **Unified sci‑fi UI + renderer concept — see [docs/UI_AND_RENDER_CONCEPT.md](UI_AND_RENDER_CONCEPT.md) (planned).**
  A design concept for a **consistent futuristic UI** (deep‑blue translucent menus, **white** line
  icons + white text, holographic frames, one uGUI/UI‑Toolkit component kit + theme replacing raw
  IMGUI), a **splash‑screen overhaul** (rendered starfield/warp intro + logo reveal), and a
  **renderer overhaul** for a fancy sci‑fi look (lit block shader with normals + sun + lamps,
  per‑material reflectivity/metalness, emissive glow, a post‑processing stack — bloom/tonemap/AO/
  fog, better skies + water/lava, particles/VFX), all gated by the quality presets. Ties together
  the lighting / reflective‑materials / weather plans. Server stays authoritative (presentation
  only).
- **Block textures — DONE (procedural):** `BlockTextureAtlas` generates a 32×32-per-block atlas
  in code (grain, ore speckles, metal panel+rivets, ice/glass streak, circuit grid, dark edges);
  `ChunkMesher` UV-maps faces into it and the `Spacecraft/BlockAtlas` shader samples it × per-face
  shade. No image assets bundled. Hand-authored/AI art can replace the atlas later (same UVs).
- **Still to do:** procedural (or authored) **UI icons & symbols** (hotbar/items, station,
  compass, vitals, menu tabs); first-person **tool/hand** visuals; mining/placing **feedback**
  (particles, outline); module colour codes.
- **Icons & symbols (game + menus):** a real icon set replacing the current text/emoji
  placeholders — item/hotbar icons, station/compass/minimap symbols, mission & map markers,
  HUD vitals icons (health/oxygen/energy), and menu button/tab icons. Ship the UI on
  **uGUI/UI Toolkit** with a consistent visual style (the M20 colours/branding), not raw IMGUI.
- **Avatar:** swappable part shapes/cosmetics beyond colours; walk/idle animation.
- **Pause menu**, in-game settings, accessibility flags applied (reduced effects, larger UI),
  animated/main-menu background (optional).
- Every bundled asset (textures, icons, fonts, audio) recorded in `NOTICES.md`, permissive
  licences only.
- **AI asset tools — scaffold DONE (`tools/ai-assets/`):** two uv Python tools turn a text
  description into a real asset — **`gen_sound.py`** (ElevenLabs text-to-sound-effects) and
  **`gen_image.py`** (OpenAI image API; `gpt-image-1-mini` low @ 1024² ≈ $0.005, downscaled
  locally to a small/pixel-art texture). They feed the art pass (replace procedural placeholders;
  log each in `NOTICES.md`). **Cost-gated:** one file per run, keys only in a git-ignored `.env`,
  and Claude proposes the exact command + estimated per-file cost for **approval before any paid
  call**. Building/running the actual asset set is a later, opt-in step.

### M28 — Build & distribution
- Windows **player build** (.exe) bundling the server (Option A), data and assets; first-run.
- Smoke-test checklist; optional **WebGL "Lite"** build (per `WEBCLIENT_FEASIBILITY.md`).
- Document the build steps; add a `scripts/build-client.ps1` if Unity CLI batchmode is used.

## Cross-cutting principles

- **Server stays authoritative.** The client only sends intents and renders state — never
  decides outcomes. Every new screen maps to existing intents/messages.
- **Bilingual UI** (DE/EN) via the `Localizer`; add `ui.*` locale keys as screens land
  (en/de parity is enforced by the content tests).
- **Asset discipline:** only permissive-licensed assets, each logged in `NOTICES.md`;
  synced/generated content stays git-ignored, project files stay versioned.
- **Testing:** server logic is already unit-tested; for the client add PlayMode/EditMode tests
  where they pay off (e.g. `ChunkMesher`, `ClientSettings`, UI view-models), plus a manual
  playtest checklist per phase. Keep the .NET suite green.

## Fastest path to "fun"

M21 → M22 → M24 gives a textured, build-and-mine multiplayer game with a real survival loop.
M23/M25 add depth (missions, space combat); M26/M27 add polish; M28 ships a build. If time is
tight, M21 alone is already a demoable, genuinely playable slice.

## Risks / open questions

- **SP hosting (A vs B):** child-process is fast to ship but means packaging the server with
  the client and managing its lifecycle; confirm acceptable before M21.
- **Native SQLite under Unity** is the main blocker for Option B — decide later.
- **UI tech:** IMGUI is fine for the shell/HUD but not for inventory/crafting; commit to
  uGUI or UI Toolkit at M22 and stay consistent.
- **Art scope:** a full atlas + audio is real production work; M21 ships a minimal atlas so the
  game is presentable without blocking on full art.
