# Spacecraft — Progress & Next Steps

Resume point for development. Full milestone breakdown lives in `plans/IMPLEMENTATION_PLAN.md`
(local, git-ignored). Tests: **229 passing**. Repo pushed to `origin/main` (private).

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
- **(NEW, planned) Visible ship engines/thrusters:** ships show **visible engine nozzles** (one or
  more per ship type, from the `ships.json` design) with a **glowing thruster VFX** in space (engine
  glow + exhaust trail, scaling with throttle, flare on launch/jump). Shared by third-person/cockpit
  + planet launch. Part of the renderer/VFX pass. See CLIENT_COMPLETION_PLAN (M25b).
- **(NEW, planned) Space HUD radar:** in ship mode, a **minimap + screen-edge arrows** for nearby
  asteroids/planets/enemies/other players, **colour-coded** white=neutral, blue=friend, red=hostile.
  Uses `SpaceState` entities (carry `Hostile`) + planets; other players in the instance need their
  positions broadcast. Client HUD over the authoritative list. See CLIENT_COMPLETION_PLAN (M25b).
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
  clear/overcast. Planned: **blocky voxel clouds** (cube clumps that float + slowly drift,
  **pass-through/non-solid**, **per-planet colour** white/yellow/blue/…, cover scales with
  weather), rain/storm **particles + lightning + sound**; biome-flavoured weather.
- **(NEW, planned) Hyperspace travel between systems:** travel to other star systems via a
  **hyperspace jump** — every ship has a **jump-generator module** (required + later charge/cost/
  range); pick a destination on the **star map**, server validates + switches the system; client
  plays a **warp animation** (starfield stretches into streaks → drop out into the new system).
  In-system hops keep launch/landing; hyperspace is system-to-system only. Reuses star map (M23)
  + SpaceView + ship modules + universe data (M11). See CLIENT_COMPLETION_PLAN "Hyperspace travel".
- **Space asteroid mining — breakable asteroids DONE.** Space asteroids have a size tier
  (`AsteroidTier` 2/1/0) and **split on destruction**: large → smaller chunks (no loot) → smallest
  → mineral drops, via the existing `FireWeapon`/`AsteroidDestruction` path. 2 tests.
- **Space collision — server foundation DONE.** The ship has an authoritative `ShipPosition` in
  the instance (`ShipMoveIntent`/`ShipMove`); `TickSpace` damages hull/shield by impact speed when
  it overlaps an asteroid and bounces it. 2 tests. Remaining: wire SpaceView to fly in server
  coords + report position (`SendShipMove`) + hit effect.
- **Tractor beam — DONE (server).** A buildable `tractor_beam` module: with it fitted, destroyed
  targets' loot floats as a `ResourceDrop` (incl. NPC-ship salvage) instead of instant loot, and
  `CollectSalvage` pulls drops within range into the cargo hold until full. 1 test. Remaining:
  client beam VFX + cargo-fill HUD.
- **Combat loot — lootable containers DONE.** The death-drop salvage capsule is now a **lootable
  container** (`GameServerContainers`): tracked + persisted, `LootContainer` transfers contents to a
  nearby player (proximity-checked, partial if full, despawns when emptied); `ContainerList`/
  `NetContainer` + `LootContainerIntent` + client hook (**G** loots nearest). 3 tests. Planned:
  **PvP corpses** with the full inventory (needs PvP damage) + **ship-salvage** drops + tractor.
- **Atmosphere & breathability — slice DONE (server + HUD).** `PlanetType.Atmosphere`
  (breathable/toxic/none) per planet; the oxygen tick regenerates on breathable worlds, drains on
  toxic/airless (global oxygen rule still gates). `WorldEnvironment.Breathable` → HUD marks oxygen
  "(breathable)". 4 tests. Planned: atmosphere-driven view-distance/fog.
- **Landable asteroids — content slice DONE.** An `asteroid` body type: small, crystalline
  surface, no life (`creatureAbundance` none) / no flora, **airless** (oxygen drains), permanent
  **space sky** (`PlanetType.SpaceSky` → `WorldEnvironment.SpaceSky`; client keeps camera
  space-black on the surface). Excluded from the random universe pool (`Selectable=false`).
  Playable as a start planet. 5 tests. Planned: fly-to-and-land via star map, surface starfield,
  visible sun disc.
- **Space stations — generator + boardable server slice DONE.** `StationGenerator` assembles a station from
  **module rooms joined on a grid** (hub + random-walk-grown rooms + stacked floors) into one voxel
  structure (`iron_wall`+`glass`); the solid hull enclosing hollow rooms makes **outer = inner** by
  construction. Doorways between adjacent modules, floor shaft between stacked, a hangar opening, and
  markers (hangar/vendor/mission_board/heal_tank/quarters) scaling small→huge. **Boarding slice
  DONE:** station contacts appear in `SpaceState`, `BoardStation` validates range from the ship,
  stamps the voxel interior into a reserved station instance area, moves the player inside, protects
  station blocks, and enables vendor market barter + station-board missions. 10 station tests across
  generator + boarding. Still planned: client docking/boarding UI + station rendering polish, station
  NPC population, and the **named station on the ship radar + the player's location readout**.
- **World variety — slice DONE.** Biome-aware `WorldGenerator`: single-biome planets + multi-biome
  worlds (surface per column from noise; **biome count randomised per world from the seed**); new
  blocks sand/mud/grass/crystal + new planet types (desert/jungle/crystal/swamp/varied);
  `WorldRadius` field. 2 tests. Planned: enforce size bounds, smooth blending, biome-matched life.
- **Scanners + knowledge research — slice DONE (server).** `GameServerScanning`: `ScanSubject`
  (creature → threat / block → yield) + `ScanSpaceEntity` (asteroid resources); **first scans grant
  `KnowledgePoints`** (persisted; `Scanned` ledger so re-scans don't), and **blueprints additionally
  cost knowledge** (`BlueprintDefinition.KnowledgeCost`, validated + deducted in `HandleUnlock`).
  `ScanIntent`/`ScanEntityIntent` → `ScanResult`. 5 tests. Remaining: client scanner readout panel +
  aiming, Tech-tab knowledge display, upgraded scanners (see Equipment).
- **Equipment & upgrades — slice DONE (server).** Data-driven gear effects from carried items
  (`ItemDefinition.ArmorResistance`/`OxygenBonus`/`ScanKnowledgeMultiplier` + `GameServerEquipment`):
  **stealth_suit** (`ToggleStealth` → `Stealthed`; creatures + enemies ignore you, drains energy),
  **advanced_scanner** (×2 knowledge), **mining_beam** (tier-3 drill), **armor_chest/legs/helmet**
  (damage resistance, capped 0.75, applied to creature/enemy/lava), **oxygen_tank_2** (+50 MaxOxygen),
  **oxygen_extractor** (lowers toxic-atmo drain by `0.6 × PlanetType.OxygenExtractability`),
  **suit_teleporter** (recall to ship; device + not-in-space + 30 s cooldown + 10 energy),
  **emergency_ration** + **ration dispenser** (`RationStore`, auto-feed). Items/recipes/blueprints
  (knowledge-gated) + DE/EN locales. ~18 tests across equipment/atmosphere/hunger/teleport.
  Client-side pending: **suit_lamp** light cone (shader) + **radar_scanner** HUD tiers + visual
  armor/stealth fade. Still planned: **jetpack** (short flights/boosted jumps; burns SuitEnergy,
  recharges on ground/aboard — needs the movement system, mostly client).
  See CLIENT_COMPLETION_PLAN "Equipment & upgrades".
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
- **Market / NPC trading (resource barter, no credits) — DONE (server).** `CraftingStation.Market`
  + `market_*` give→get trade-offer recipes (e.g. 5 iron→1 titanium, carbon+silicate→medpack) run
  through the craft path; available at the ship's trade console (aboard, `MarketAvailable`). No
  currency — pure resource barter. 3 tests. Stations/settlements will host NPC vendors using the
  same market station later.
- **Player-to-player trading — DONE (server).** `GameServerTrade`: `RequestTrade`/`RespondTrade`
  open a `TradeSession`; each side stages an offer (validated) + confirms; any offer change voids
  both confirms; on mutual confirm the server **atomically swaps** items (re-validated, leftover
  returned). Range-gated at request + commit; cancel + disconnect close it. `TradeRequest/Respond/
  Offer/Confirm/Cancel` + `TradeUpdate`/`TradeClosed`. 6 tests. Remaining: client trade panel.
- **Gear disassembly / recycling — DONE (server).** A workshop `Disassemble` action
  (`DisassembleIntent`/`Disassemble`) breaks a crafted item back into ~half its recipe components
  (`DisassemblyRecoveryRate`, server-tunable); needs a workshop + the item, rejects raw/un-craftable
  items. `SendDisassemble` exists; crafting-tab button pending. 3 tests.
- **Planet settlements & NPC towns — generator DONE (server).** `SettlementGenerator` lays out
  buildings on a plot grid into one voxel structure: **villages** (single-storey, biome material),
  **towns** (multi-storey iron/glass with **ladders** between floors), plus a **ruined** variant
  (decay pass, no NPCs, loot). Markers: vendor / mission_board / npc; inhabitants human or alien.
  New `ladder`/`stairs` blocks (player-craftable). Deterministic (stable hash). 6 tests.
  **Stamping DONE:** `StampSettlement` (gated by `ServerConfig.PlaceSettlements`) places one per
  planet+seed (airless/lifeless worlds get none) with a flattened foundation, offset from the
  landing zone; intact = mining-protected (`IsSettlementBlock`), ruins scavengeable; generated name
  + world-space markers. **Vendor → market barter** near a vendor marker (`NearSettlementVendor`);
  **ruin loot** markers spawn scavengeable containers; **mission board** offers local gather
  missions accepted/turned-in only at the board (`NearSettlementMissionBoard`). 15 tests. Still
  planned: NPC render/behaviour.
- **Crashed ship wrecks — generator + stamping + repair/claim server slice DONE.** `WreckGenerator` builds a wreck
  from a `ships.json` hull (iron/glass, or crystal for alien wrecks), keeps the intact hull as a
  **repair mask**, then decays it (breaches + scorch + a guaranteed crash gash so it's enterable).
  `GameServerWrecks.StampWreck` (gated by `ServerConfig.PlaceWrecks`) places one **rarely** per
  planet+seed, half-buried, offset from the landing zone + settlement; **not protected** (freely
  scavengeable); generated name + world-space loot/module/data_terminal markers. **Loot DONE:**
  those markers spawn scavengeable containers (salvage / components / data) via the shared loot
  flow, guarded by `WorldMetadata.GeneratedLoot` so they don't respawn on reload. **Repair/claim
  DONE:** `RepairWreckIntent` validates each repaired cell against the intact mask, consumes the
  matching block item, restores the hull block, reports `WreckRepairStatus`, and `ClaimWreckIntent`
  adds a fully repaired wreck to the owned-ships registry as a switchable ship. 16 wreck tests.
  Still planned: client repair UI/VFX, persisted multi-wreck repair ledgers, module-level repair.
- **Hunger & eating (survival) — slice DONE (server + HUD).** `PlayerState.Hunger` (persisted)
  drains outside the ship, sates aboard, **starves health at 0**; gated by `Rules.Hunger`+Survival.
  **Eating** refills it (`ItemDefinition.ConsumeHunger`): `creature_meat` (+30) and **edible plants**
  — `flora_plant` now also drops **`berries`** (+18); poison still harms. Rides on `PlayerStateUpdate`;
  HUD shows a Hunger vital. 6 tests. Planned: detoxifier module, poison status-effect over time,
  sprint-drain, hunger icon. *(Client needs the refreshed Networking lib — re-run sync-client-libs.)*
- **Detoxifier ship module — DONE.** A buildable `detoxifier` module (blueprint + build cost) adds
  a `Detoxifier` crafting station; the `detoxify_gland` recipe converts `toxic_gland` →
  `creature_meat`, gated by having the module aboard. Reuses crafting + module build. 3 tests.
- **(NEW, planned) Atmosphere-based view distance:** a planet's atmosphere sets a **fog/
  visibility range** (hazy/thick → see less far; thin → farther; airless → clearest), scaled by
  weather intensity, server-supplied via `WorldEnvironment`; the client applies it as camera fog
  (the view-distance setting stays a client cap). Folded into Atmosphere & breathability. See plan.
- **Player weapons (melee & ranged) — slice DONE (server + data).** Six craftable weapons as
  `ToolKind.Weapon` tools with `Damage`+`Range`: melee (machete/vibro_knife/plasma_sword) and
  ranged (gauss_pistol/laser_pistol/plasma_blaster; energy ones draw `SuitEnergy`). Shared
  `AttackEntity` is weapon-aware — range gates reach, damage decides the hit, energy weapons spend
  suit energy (rejected empty). Recipes + blueprints (tiered) + DE/EN locales, so they appear in
  the crafting/tech UI and work via the hotbar + F with no client change. 3 tests. Planned: ammo,
  projectile/hitscan VFX, PvP + WeaponMode gating, damage types vs resistances, reload/overheat.
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
  positions ~2 Hz for the client to interpolate. **Territorial retaliation done:** attacking a
  retaliating species (territorial / hostile) provokes it (`ProvokeTimer`) → hunts + bites back
  ~12 s then calms; pack-hunters rally nearby kin; hooked into AttackEntity so weapons trigger it.
  Planned: flock movement, pathing, fluid-volume spawning, nameplates, hunger loop. *(Client needs
  the refreshed Networking lib — re-run `scripts/sync-client-libs.ps1`.)*
- **M26 — audio — procedural SFX DONE.** Audio module enabled; `ClientAudio` plays code-generated
  tones for mine/place/craft/reject/ship-hit via the master×SFX bus. Recorded SFX + music later.
- **(NEW, planned) Full sound design:** menu SFX (hover/click/confirm/error), **ship ambient loop**
  (aboard), **creature** calls (idle/alert/attack/hurt, varied by species), **NPC** non‑verbal
  vocalisations (grunts/chirps, **no speech**), **equipment** use clicks (tools/weapons/lamp), **ship
  systems** (stations/docking/hyperspace), and **background MIDI music** on a dedicated music bus
  (menu/planet/space/combat cross‑fade). Generated via `tools/ai-assets/gen_sound.py` / procedural /
  CC0; spatialised; volumes from `ClientSettings`. See CLIENT_COMPLETION_PLAN (M26).
- **AI asset tools — scaffold DONE (`tools/ai-assets/`).** Two uv Python tools: `gen_sound.py`
  (ElevenLabs text→SFX) and `gen_image.py` (OpenAI text→image/texture; `gpt-image-1-mini` low ≈
  $0.005, downscaled locally). One file per run, keys in a git-ignored `.env`; Claude proposes the
  command + estimated cost for **approval before any paid call**. Generating the real asset set is
  a later opt-in step. See `tools/ai-assets/README.md`.
- **(NEW, planned) Unified sci‑fi UI + renderer concept — [docs/UI_AND_RENDER_CONCEPT.md](docs/UI_AND_RENDER_CONCEPT.md).**
  Consistent futuristic UI (deep‑blue translucent menus, **white** line icons + text, holographic
  frames, one uGUI/UI‑Toolkit component kit + theme replacing IMGUI); **splash overhaul** (rendered
  starfield/warp intro + logo reveal); **renderer overhaul** (lit block shader + per‑material
  reflectivity + emissive glow + post stack: bloom/tonemap/AO/fog, better skies/water/lava,
  particles), preset‑gated. Part of M27 + a render milestone.
- **(NEW, planned) Held tools/weapons + use animations:** the selected tool/weapon is **visible in
  the hand** — a **first‑person viewmodel** and on the **third‑person** avatar — with **use
  animations** (mining swing/drill, weapon swing/fire, scanner/lamp toggle, place gesture) driven by
  the actions and synced to other players via presence. Part of M27 + avatar animation.
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
dotnet test                      # expect all green (229)
git log --oneline -5             # latest = M20 client shell, assets & UX
```
All milestones from the local plan (M0–M20) are now implemented on the server/shared side
with a Unity scaffold. Next work is open scope (see Pending). Note: this sandbox blocks real
sockets, so live WebSocket/2-player network tests are exercised via in-process/loopback, not
real ports; the Unity client scripts are not compiled by `dotnet` (Editor required).
