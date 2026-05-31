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

### Ships: types, designs, expandable interiors & multiple owned ships — NEW (planned)
Today there is one hardcoded ship. Grow this into a real ship system:
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

### M26 — Audio — **DONE (procedural SFX) / recorded audio + music later**
- Unity **audio module** enabled; `AudioListener` on the player camera; master/SFX volumes from
  `ClientSettings` applied (`ClientAudio`).
- **Procedural SFX** generated in code (no bundled files): mining vs placing (from
  `BlockChanged`), craft success/failure, rejection, ship hit (hull drop). Played via the
  master×SFX bus.
- Later: real **recorded SFX** (ship hums, doors/airlock/docking, alarms, respawn, weapons) +
  **music**, sourced CC0/permissive and recorded in `NOTICES.md`; full music/ambient bus.

### M27 — Art, icons & polish
- **Textures:** finalize a proper **block texture atlas** (all material groups from
  `anf_textures.md`) replacing the flat per-block palette; module colour codes; first-person
  **tool/hand** visuals; mining/placing **feedback** (particles, outline).
- **Icons & symbols (game + menus):** a real icon set replacing the current text/emoji
  placeholders — item/hotbar icons, station/compass/minimap symbols, mission & map markers,
  HUD vitals icons (health/oxygen/energy), and menu button/tab icons. Ship the UI on
  **uGUI/UI Toolkit** with a consistent visual style (the M20 colours/branding), not raw IMGUI.
- **Avatar:** swappable part shapes/cosmetics beyond colours; walk/idle animation.
- **Pause menu**, in-game settings, accessibility flags applied (reduced effects, larger UI),
  animated/main-menu background (optional).
- Every bundled asset (textures, icons, fonts, audio) recorded in `NOTICES.md`, permissive
  licences only.

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
