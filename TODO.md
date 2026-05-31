# Spacecraft — Progress & Next Steps

Resume point for development. Full milestone breakdown lives in `plans/IMPLEMENTATION_PLAN.md`
(local, git-ignored). Tests: **132 passing**. Repo pushed to `origin/main` (private).

## Done (committed & pushed)

| Milestone | Summary |
|---|---|
| M0–M8 | .NET solution; Shared data model + data-driven content + bilingual i18n; seed world gen; SQLite persistence; networking (LiteNetLib + loopback + MessagePack codec); authoritative game server (tick loop, mine/place/craft/blueprint validators); admin API; self-hosting publish scripts; Unity client scaffold |
| M9 | Game modes (Survival/Creative) + server rules + presets |
| M10 | Heal-tank respawn in Medbay + death penalty + salvage capsule |
| M11 | World description + deterministic procedural universe + star map |
| M12 | Admin roles + server-authoritative logged cheats |
| M13 | Mission system (no AI): system + player missions, reward depot |
| M14 | Admin extension editor + content packs (missions) |
| M15 | WebSocket gateway + composite transport + web portal + WebGL feasibility doc |
| M16 | Optional Python AI mission backend (off by default) + decision doc |
| M17 | Personal landing zones + extended space settings (rules) |
| M18 | Ship docking: request/response/undock intents, handshake gated by `ShipDocking` rule, `docking_module` requirement, guest access, undock-on-disconnect |
| M19 | Free space flight + combat (PvE slice): concept doc, ship hull/shield, ship-weapon blueprints/modules, local space instances, NPC drones + asteroids, planet enemies — all rule-gated, no permanent ship loss; ship-module build flow |
| M20 | Client shell, assets & UX: decisions doc + NOTICES, bilingual shell locale strings, Unity scaffold (AppShell splash→menu→settings→loading→in-game, ClientSettings local persistence) |

Also done: MIT license, father-son README, all spec docs moved to local `plans/`,
commit author = `marceld23 <marcel.duetscher@gmail.com>`.

### M18 notes (implementation details)

- Messages: `DockRequestIntent`/`DockResponseIntent`/`UndockIntent` (client→server, codec
  tags 14–16); `DockRequestNotice`/`DockStatus` (server→client, tags 64–65).
- [GameServerDocking.cs](src/Spacecraft.GameServer/GameServerDocking.cs): `RequestDock` /
  `RespondDock` / `Undock` / `AreDocked` / `HasGuestAccess`; gated by `Rules.ShipDocking`
  (Off→reject, Free→auto-accept, RequestRequired/FriendsOnly→handshake); requires the shared
  ship's `docking_module`; `ClearDocking` on disconnect. FriendsOnly currently behaves like
  RequestRequired until a friends system exists.
- Dockings are in-memory only (they can't outlive a session → no `docking` table needed).
- `GameServer.AddLocalPlayer(name)` adds a joined session with a synthetic negative
  connection id, so 2-player docking is testable despite the single-client `LoopbackLink`.
- Tests in [DockingTests.cs](tests/Spacecraft.Tests/DockingTests.cs): request→accept,
  reject, Off-rejects, Free auto-dock, undock, missing-module, disconnect-undocks.

### M19 notes (implementation details)

- Concept-first: [docs/SPACE_COMBAT_CONCEPT.md](docs/SPACE_COMBAT_CONCEPT.md) — a small
  server-authoritative **PvE slice**; PvP ship combat + per-player ships deferred (one shared
  ship today). No permanent ship loss (§8.5).
- `ShipState.Hull`/`Shield` (persisted); maxima derived from modules (`hull_plating`,
  `shield_generator`) in [GameServerSpaceCombat.cs](src/Spacecraft.GameServer/GameServerSpaceCombat.cs).
- Data: `hull_plating`, `shield_generator`, `asteroid_breaker`, `ship_cannon_1`,
  `laser_cannon_2` blueprints + modules + en/de locales. Weapon modules carry `weapon_*` stats
  (`weapon_class` 0=tool, 1=combat).
- Messages: build-module / enter-space / leave-space / fire-weapon / attack-entity intents
  (tags 17–21); ship-combat-status / space-state / entity-destroyed / space-closed /
  planet-enemy-list / planet-enemy-defeated (tags 66–71).
- [GameServerSpaceCombat.cs]: space instances (orbit) with asteroids + NPC drones/UFOs gated
  by `SpaceCombat`/`SpaceNpcEnemies`/`AlienUfos`; `FireWeapon` gated by `ShipWeapons` +
  `AsteroidDestruction` (tools may mine asteroids even when weapons are off, §7.4); shield-then-
  hull damage; `DisableShip` recovers the ship to base + respawns the player (no loss). Also the
  `BuildShipModule` flow (workshop + blueprint + materials), reusable for all modules.
- [GameServerEnemies.cs](src/Spacecraft.GameServer/GameServerEnemies.cs): planet enemies gated
  by `PlanetEnemies` + Survival (off in Creative, §12.4); timed spawn near surface players,
  proximity damage, `AttackEntity` to kill (tool-tier scaled) with loot.
- Tests in [SpaceCombatTests.cs](tests/Spacecraft.Tests/SpaceCombatTests.cs): free-flight
  gating, asteroid/drone spawn, asteroid breaker loot, weapons-off rejection, cannon kill,
  ship-defeat recovery, build-module flow, planet-enemy spawn/damage/kill.
- **Deferred** (see concept doc): PvP ship combat, per-player ships, large cruisers/bosses,
  module-level damage, ammo/energy/overheat, salvage/boarding, real flight physics, Unity
  flight/combat UI (only `NetworkClient` hooks added).

### M20 notes (implementation details)

- Decisions doc [docs/CLIENT_SHELL_AND_ASSETS.md](docs/CLIENT_SHELL_AND_ASSETS.md) answers the
  `anf_textures.md` question catalogue (branding, splash, menu, settings, texture/audio style,
  asset folder layout, MVP-vs-later) + [NOTICES.md](NOTICES.md) (no bundled third-party assets
  yet; placeholders only; library attribution).
- Bilingual shell locale strings (`ui.splash.*`, `ui.menu.*`, `ui.settings.*`, `ui.loading.*`,
  `ui.credits.*`) in `data/locales/{en,de}.json`.
- Unity scaffold (IMGUI, presentation-only): `AppShell` (splash→menu→settings→loading→in-game
  state machine; owns localizer + settings; spawns `GameBootstrap`), `SplashScreen`, `MainMenu`,
  `SettingsScreen`, `LoadingScreen`, and `ClientSettings` (local JSON persistence; quality
  presets incl. Potato/Pi, audio buses, mouse, language, accessibility flags).
- Client-only settings never touch authoritative server rules. Real textures/models/audio,
  animated background, controller support and uGUI polish are deferred (asset-production later).

## Pending — finish the client into a playable game

Full roadmap: [docs/CLIENT_COMPLETION_PLAN.md](docs/CLIENT_COMPLETION_PLAN.md). The server is
feature-complete (M0–M19); the remaining work is client-side UI + rendering + scene wiring that
consumes protocol messages that already exist.

- **M21 — Playable vertical slice ⭐ — code DONE, needs in-Editor playtest.** `WorldRig` builds
  the in-game rig in code (player + camera + chunk material + HUD) from `AppShell.LaunchGame`;
  Singleplayer hosts the bundled server as a child process (Option A); robust join-on-connected
  + spawn snap; curated per-block palette + per-face shading via a built-in vertex-colour shader;
  settings applied (sensitivity/invert-Y/view distance); Esc returns to menu. Real 32×32 texture
  atlas deferred to M27. *Next: publish the local server + press Play to playtest.*
- **M22 — core gameplay UI — code DONE, needs playtest.** HUD hotbar (1-9 + scroll, drives the
  placed item via `SelectHotbarIntent`); `GameMenu` (Tab) with Inventory/cargo, Crafting,
  Tech (blueprint unlock) and Ship (module build) tabs, all over existing intents; server
  feedback as a HUD toast. IMGUI for now; uGUI polish + item drag-move deferred.
- **M23a — the ship as a place — code DONE, needs playtest.** Server stamps a walk-in voxel
  ship hull at the landing zone (player starts inside); `AboardShip` derived from being inside
  it (gates cargo/crafting); HUD shows "aboard" + a minimap/compass that always points to the
  ship with distance. `PlaceStarterShip` config flag (on by default). Hull is mining-protected.
  **Stations (M23a-2):** interior markers + "Press E" interaction — heal-tank heals, quarters
  sets respawn, workshop/cargo → Tab menu, cockpit → star map (soon). Server-validated.
- **M23b — player avatar, customization & third-person camera — code DONE, needs playtest.**
  Code-built blocky humanoid (`PlayerAvatar`), per-part colours in Settings (Character section),
  first/third-person toggle (V). Networked appearance (others see it) deferred to M24; armor
  overrides + animation later.
- **M23 — navigation, missions & feedback — code DONE, needs playtest.** Star-map tab (opened
  from the cockpit), mission-log tab (accept/turn-in), respawn/rules/craft feedback as HUD
  toasts — all over existing protocol.
- **M24** — multiplayer presence (render other players + nameplates), docking UI, join/host polish.
- **M24 — multiplayer presence — presence DONE (needs LAN playtest); docking-UI + join-polish
  pending.** Server broadcasts PlayerPresence (pos + colours) + PlayerLeft; clients send colours
  on join; `RemotePlayers` renders other players as coloured avatars with nameplates. Still to
  do: docking request/accept UI, protocol-mismatch/disconnect handling in the shell.
- **M25 — space flight & combat client — code DONE, needs playtest.** Ship hull/shield HUD,
  Space console tab (launch/return + fire at entities), planet enemies rendered as blocks +
  attack with F. Singleplayer enables free flight + PvE via launcher flags. 3D flyable cockpit
  deferred.
- **M25b — real space view + launch/landing — in progress (code).** `SpaceView` shows an actual
  space scene (starfield + planet + blocky ship + entities) with third-person/cockpit camera
  (V cycles) + launch/landing fades; on-foot control frozen while flying. Still: board-and-walk-
  inside-in-space, real flight controls, nicer models, and **identical ship appearance in space
  and on the planet** (build the space ship model from the same `ships.json`/`StampShip` design,
  not a separate placeholder, so both views match and update together).
- **Ships: types/designs/multiple owned + switching — slice DONE.** `ships.json`
  (starter/hauler/scout), owned-ships registry + active, `CraftShip`/`SwitchShip` +
  `OwnedShips` protocol, Ship-tab craft/switch UI, hull size from design. Planned extras:
  per-ship fleet persistence, expandable interiors, richer designs.
- **World systems — fluids + day/night/sun/weather slices DONE; rest planned.** Water/lava flow
  ✅. **Day/night clock + sun colour + weather state** (server `WorldEnvironment`; client `Sky`
  tints the unlit world via a global shader colour, drives sky + sun) ✅; planets can be fixed
  clear/overcast. Planned: rain/storm **particles + lightning + sound**; biome-flavoured weather.
- **(NEW, planned) Hyperspace travel between systems:** travel to other star systems via a
  **hyperspace jump** — every ship has a **jump-generator module** (required + later charge/cost/
  range); pick a destination on the **star map**, server validates + switches the system; client
  plays a **warp animation** (starfield stretches into streaks → drop out into the new system).
  In-system hops keep launch/landing; hyperspace is system-to-system only. Reuses star map (M23)
  + SpaceView + ship modules + universe data (M11). See CLIENT_COMPLETION_PLAN "Hyperspace travel".
- **(NEW, planned) Space collisions + asteroid mining + tractor beam:** ship takes hull/shield
  damage flying into asteroids; weapons split big asteroids → chunks → resource drops; a tractor-
  beam ship add-on collects drops into cargo. Needs authoritative ship position in space. See plan.
- **(NEW, planned) Combat loot:** destroyed ships (player/NPC) drop (part of) their cargo as
  tractor-collectable salvage; planet PvP leaves a **lootable corpse** with the victim's full
  carried inventory (victim respawns without it) that persists until emptied. Rules-gated. See plan.
- **(NEW, planned) Atmosphere & breathability:** per-planet atmosphere — breathable (no suit
  oxygen drain), toxic/non-breathable (drains), or none/airless; the oxygen tick keys off it.
  Small slice on the existing oxygen system. See CLIENT_COMPLETION_PLAN.
- **(NEW, planned) Landable asteroids:** big asteroids you can land on — airless (suit oxygen),
  space sky + visible system sun (its colour tints the surface), no weather/day-night; almost no
  life except a rare crystal biome/creatures. See CLIENT_COMPLETION_PLAN.
- **(NEW, planned) Space stations:** boardable stations near planets, small→huge, with landing
  hangars, rooms, NPC traders/aliens and mission boards. Reuses docking + ship-as-place +
  missions + a new trading system. See CLIENT_COMPLETION_PLAN.
- **World variety — slice DONE.** Biome-aware `WorldGenerator`: single-biome planets + multi-biome
  worlds (surface per column from noise; **biome count randomised per world from the seed**); new
  blocks sand/mud/grass/crystal + new planet types (desert/jungle/crystal/swamp/varied);
  `WorldRadius` field. 2 tests. Planned: enforce size bounds, smooth blending, biome-matched life.
- **(NEW, planned) Lighting:** suit/helmet lamp (light cone via a player-light term in the block
  shaders), later placed light blocks + ship exterior lights, and emissive glow (crystals,
  bioluminescent creatures). See CLIENT_COMPLETION_PLAN "Lighting".
- **Procedural flora — slice DONE.** `WorldGenerator` seeds **surface flora** on suitable
  surfaces by per-planet `FloraDensity` (flora_plant on grass/dirt/mud, flora_crystal on
  crystal/stone/basalt; barren planets none). New flora blocks + `plant_fiber`/`plant_seed`/
  `crystal_seed` items + locales + atlas colours. **Harvest drops the material** (fibre/crystal);
  **bounded regrowth** — regrows on its cell only while the host block below survives
  (`GameServerFlora`), capped at one plant per cell (no spread); **seeds replant** on a valid
  host only (`HandlePlace` host check). 6 flora tests. Planned: procedural form/appearance,
  water/lava flora, effects (poison/heal/food), maturity→produces-seeds. See plan.
- **(NEW, planned) Planet settlements & NPC towns:** some worlds have settlements — **primitive
  villages** (single-storey huts) or **modern towns** (multi-storey buildings) — populated by
  **human or alien NPCs**, with **mission boards** + **NPC traders** (like space stations, but
  planet-side). Also **ruins of abandoned settlements**: the same templates in a decayed state
  (collapsed/overgrown, no living NPCs), explorable for **salvage/loot** rather than services.
  Reuses voxel stamping + station interactions + missions + trading + loot + the avatar/creature
  renderer. See CLIENT_COMPLETION_PLAN "Planet settlements & NPC towns".
- **(NEW, planned) Crashed ship wrecks:** **rare** abandoned/crashed ships on planet surfaces —
  derelict voxel hulls (from `ships.json`) in a crashed pose (tilted/half-buried, breached via a
  decay pass), explorable for **salvage/loot** (containers, cargo, modules, data/lore), with
  hostile scavengers and human/alien variants. **Repairable into a flyable owned ship** —
  rebuild the design's missing/broken blocks + required modules, then the server **claims it
  into your owned-ships registry** (a mid-game way to gain a bigger/alien hull). Reuses ship
  stamping + the ruins decay pass + loot + creatures + missions + the ship build/registry. See
  CLIENT_COMPLETION_PLAN "Crashed ship wrecks".
- **(NEW, planned) Hunger & eating (survival):** a `PlayerState.Hunger` vital that drains over
  time (not aboard ship), **damages health at 0** (starvation), and is refilled by **eating** —
  `creature_meat` and **edible flora** (a plant is eatable only if `edible` and not `poisonous`);
  **poisonous** items harm instead. Survival-only. Builds on the consume system (ConsumeItemIntent
  + item consume-effects) added with creatures; adds a hunger-restore value + drain/starvation
  tick. See CLIENT_COMPLETION_PLAN "Hunger & eating".
- **(NEW, planned) Detoxifier ship module:** a craftable part built into the ship (if a slot is
  free) that converts **poisonous** plants/creature meat into **safe food** (e.g. toxic_gland →
  creature_meat). Reuses ship modules + crafting + the consume/hunger system. See plan.
- **(NEW, planned) Atmosphere-based view distance:** a planet's atmosphere sets a **fog/
  visibility range** (hazy/thick → see less far; thin → farther; airless → clearest), scaled by
  weather intensity, server-supplied via `WorldEnvironment`; the client applies it as camera fog
  (the view-distance setting stays a client cap). Folded into Atmosphere & breathability. See plan.
- **(NEW, planned) Player weapons (melee & ranged):** craftable personal weapons — **melee**
  (machete, vibro-knife, plasma sword) extending the `ToolKind.Weapon` tool model, and **ranged**
  (gauss pistol, laser pistol, plasma blaster) with server-authoritative hit resolution + per-
  weapon damage/range/fire-rate and ammo or `SuitEnergy` cost. Crafted/blueprinted with a tier
  progression; drives the same attack path as creatures/PvP. See CLIENT_COMPLETION_PLAN
  "Player weapons: melee & ranged".
- **Procedural creatures & aliens — slice DONE (server + client render).** Seed-derived **species roster** per
  world (`CreatureGenerator`, sized by `PlanetType.CreatureAbundance` none/few/many) — each species
  has stats, **temperament** (mostly **non-hostile**), **activity** (diurnal/nocturnal/… + sleep),
  **habitat** (land/water/lava/air), parametric appearance, and a **drop** tagged food/poison/
  material-substitute. `GameServerCreatures` spawns fauna near surface players (habitat-gated);
  **only hostile + awake** creatures damage, and only where hostility rules allow (peaceful = safe).
  Kills drop the species item (shared `AttackEntity`); a **consume system** (`ConsumeItemIntent` +
  `ItemDefinition.ConsumeHealth`) makes **food heal / poison harm**. Sent via `CreatureList`/
  `NetCreature`. 10 tests. **Client render done** (`CreatureBuilder` blocky body from the
  descriptor — segments/legs/wings/tail/colour/glow + hostile tint + sleep dim; `CreatureView`
  syncs them; F attacks creatures too). **Movement AI done** (`CreatureBehaviour.Step`): hunters
  approach, skittish flee, the rest wander, sleepers rest; server moves them per tick + re-syncs
  positions ~2 Hz for the client to interpolate. Planned: pack/flock, territorial retaliation,
  pathing, fluid-volume spawning, nameplates, hunger loop. *(Client needs the refreshed Networking
  lib — re-run `scripts/sync-client-libs.ps1`.)*
- **M26 — audio — procedural SFX DONE.** Audio module enabled; `ClientAudio` plays code-generated
  tones for mine/place/craft/reject/ship-hit via the master×SFX bus. Recorded SFX + music later.
- **AI asset tools — scaffold DONE (`tools/ai-assets/`).** Two uv Python tools: `gen_sound.py`
  (ElevenLabs text→SFX) and `gen_image.py` (OpenAI text→image/texture; `gpt-image-1-mini` low ≈
  $0.005, downscaled locally). One file per run, keys in a git-ignored `.env`; Claude proposes the
  command + estimated cost for **approval before any paid call**. Generating the real asset set is
  a later opt-in step. See `tools/ai-assets/README.md`.
- **M27 — art, icons & polish — in progress.** Procedural **block texture atlas** done
  (`BlockTextureAtlas` + UV mapping + `Spacecraft/BlockAtlas` shader, no image files). Still:
  UI icons/symbols (can also be procedural), tool/hand visuals, mining feedback, uGUI polish.
- **M28** — Windows player build + optional WebGL Lite *(needs a Unity batchmode build script)*.
  Note: real hand-/AI-authored art & audio files remain an asset task; everything so far is
  generated in code (textures, avatars, SFX) — no bundled binary assets.

Later/optional: Option B true in-process SP server (retarget to netstandard2.1); per-player ships
+ PvP ship combat.

## How to resume

```powershell
dotnet build Spacecraft.sln
dotnet test                      # expect all green (132)
git log --oneline -5             # latest = M20 client shell, assets & UX
```
All milestones from the local plan (M0–M20) are now implemented on the server/shared side
with a Unity scaffold. Next work is open scope (see Pending). Note: this sandbox blocks real
sockets, so live WebSocket/2-player network tests are exercised via in-process/loopback, not
real ports; the Unity client scripts are not compiled by `dotnet` (Editor required).
