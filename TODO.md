# Spacecraft — Progress & Next Steps

Resume point for development. Full milestone breakdown lives in `plans/IMPLEMENTATION_PLAN.md`
(local, git-ignored). Tests: **240 passing**. Repo pushed to `origin/main` (private).

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

- **M21 — Playable vertical slice ⭐ — DONE, in-Editor playtest passed ✅.** `WorldRig` builds
  the in-game rig in code (player + camera + chunk material + HUD) from `AppShell.LaunchGame`;
  Singleplayer hosts the bundled server as a child process (Option A); robust join-on-connected
  + spawn snap; curated per-block palette + per-face shading via a built-in vertex-colour shader;
  settings applied (sensitivity/invert-Y/view distance); Esc returns to menu. Real 32×32 texture
  atlas deferred to M27. *Playtest confirmed: menu → Singleplayer → walk/mine/place on a shaded
  blocky world with live vitals → Esc → menu; bundled server hosts via Option A
  (`scripts/sync-client-libs.ps1` + `scripts/publish-local-server.ps1`, scene `testscene.unity`).*
- **M22 — core gameplay UI — DONE, playtest passed ✅.** HUD hotbar (1-9 + scroll, drives the
  placed item via `SelectHotbarIntent`); `GameMenu` (Tab) with Inventory/cargo, Crafting,
  Tech (blueprint unlock) and Ship (module build) tabs, all over existing intents; server
  feedback as a HUD toast — all verified in-Editor (hotbar, Tab menu, feedback). IMGUI for now;
  uGUI polish + item drag-move deferred.
- **Missing client UIs — code DONE, needs playtest.** Filled the client-side gaps over
  already-tested server systems (no protocol/server change). Solo-testable: **Disassemble**
  button in the Crafting tab (`SendDisassemble`); **Scanner** is a held item — select the
  `hand_scanner` (starter hotbar slot 3) and **left-click** to scan (nearest creature → threat,
  else looked-at block → yield; `ScanResult` readout + knowledge), with distinct per-item hotbar
  icons + name labels so tools are tellable apart; **Wreck
  repair** — **R** rebuilds the looked-at breach cell with the held block (`RepairWreckIntent`),
  a HUD progress panel + **Claim ship** button (`WreckRepairStatus`/`ClaimWreckIntent`); **loot
  prompt** ("G: loot (N)") near a container. Multiplayer (needs a 2nd client/LAN to test):
  **Docking UI** (K requests a dock with a nearby player; incoming request → Accept/Decline
  modal; **U** undocks) and a two-column **Trade panel** (T requests; add/remove from inventory,
  Confirm/Cancel, ready states; `TradeUpdate`/`TradeClosed`). New `PlayerInteractions.cs` +
  `NetworkClient`/`GameBootstrap` hooks + en/de `ui.*` keys. *(Re-run `sync-client-libs.ps1` —
  done — so the client sees the new locale keys.)*
- **M23a — the ship as a place — DONE, playtest passed ✅.** Server stamps a walk-in voxel
  ship hull at the landing zone (player starts inside); `AboardShip` derived from being inside
  it (gates cargo/crafting); HUD shows "aboard" + a minimap/compass that always points to the
  ship with distance. `PlaceStarterShip` config flag (on by default). Hull is mining-protected.
  **Stations (M23a-2):** interior markers + "Press E" interaction — heal-tank heals, quarters
  sets respawn, workshop/cargo → Tab menu, cockpit → star map (soon). Server-validated.
- **M23b — player avatar, customization & third-person camera — DONE, playtest passed ✅.**
  Code-built blocky humanoid (`PlayerAvatar`), per-part colours in Settings (Character section),
  first/third-person toggle (V). Networked appearance (others see it) deferred to M24; armor
  overrides + animation later.
- **M23 — navigation, missions & feedback — DONE, playtest passed ✅.** Star-map tab (opened
  from the cockpit), mission-log tab (accept/turn-in), respawn/rules/craft feedback as HUD
  toasts — all over existing protocol.
- **M24** — multiplayer presence (render other players + nameplates), docking UI, join/host polish.
- **M24 — multiplayer presence — presence DONE (needs LAN playtest); docking-UI + join-polish
  pending.** Server broadcasts PlayerPresence (pos + colours) + PlayerLeft; clients send colours
  on join; `RemotePlayers` renders other players as coloured avatars with nameplates. Still to
  do: docking request/accept UI, protocol-mismatch/disconnect handling in the shell.
- **M25 — space flight & combat client — DONE, playtest passed ✅.** Ship hull/shield HUD,
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
- **M25b — real space view + launch/landing — core flow playtest passed ✅ (extras planned).** `SpaceView` shows an actual
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
  clear/overcast. **Clouds DONE**: a textured cloud layer — a dome of drifting cloud billboards on
  the surface (`Clouds.cs` + `Spacecraft/Cloud` shader) **and** a spinning cloud shell over planets
  **seen from space** (`SpaceView`); **per-planet colour + density** (`CloudColor`/`CloudDensity` in
  `planets.json` → `WorldEnvironment`), thickened + **darkened in storms**. **Rain DONE**: wet wash +
  harder slanted rain in storms vs a light shower (`WeatherFx`) + lightning. Planned: **view-distance**
  scaling; 3D in-world rain/splashes + storm **sound**; AI cloud textures (procedural puffs for now);
  biome-flavoured weather.
- **Hyperspace travel between systems — DONE.** Travelling to a planet in **another star system**
  is now a hyperspace jump gated by a **`jump_generator` ship module** (blueprint + build cost +
  bilingual locales); the server (`HandleTravel`) compares the origin vs destination `SystemId`,
  rejects a cross-system jump without the module, flags `WorldReset.Hyperjump`, and reports
  "Hyperjumped to …". In-system hops stay normal travel (no module needed). The client star map
  labels cross-system destinations **"Hyperjump"** with a requirement hint, and `HyperspaceWarp`
  plays a **uGUI warp animation** (star streaks rush outward over a dark wash → white flash → clear)
  while the new world streams in. 2 new tests (254 total). Reuses star map (M23) + ship modules +
  universe data (M11). Later: jump charge/cost/range from `jump_range`, fuel, in-space jump from the
  cockpit instead of the menu.
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
- **Held tools/weapons + use animations — DONE (local player).** The selected hotbar item is now
  **visible in the hand**: a procedural blocky mesh (`HeldItem`: block/drill/gun/blade/scanner/tool)
  on the **third‑person** avatar's right hand (swings with the existing arm chop) **and** a
  **first‑person `Viewmodel`** in the lower‑right that bobs with movement and jabs forward on
  mine/place/attack. Driven from the hotbar selection in `PlayerController`. Still planned: show
  remote players'/NPCs' held items (needs the held item in presence), per‑tool use gestures
  (lamp/scanner toggles), muzzle/beam from the hand.
- **M27 — art, icons & renderer pass — in progress.** Done this pass (all code/procedural, Built-in
  RP): **block texture atlas** (17 AI tiles, runtime-decoded) + UV mapping; **lit block shader** shading
  by `normal · sun` in the **per-system sun colour** (+ tinted ambient floor); **per-material
  reflections** (gloss/metal Blinn-Phong sun highlight + Fresnel **sky** reflection); a **visible sun
  disc** (per-system colour, terrain-occluded); a **lit + textured menu backdrop**; **space-view planets
  coloured by their world/biome**; the **Tab menu scaled up** (clamped for small/low-DPI screens); 14 AI
  **UI icons** + bundled SFX (M26). New always-included shaders `Spacecraft/{BlockAtlas, LitColor,
  SunGlow}` (persisted in GraphicsSettings always-included). **Distance fog** done (sky-coloured,
  weather-scaled; block shader fog-enabled). Also done: **creatures/NPCs no longer enter the ship**
  (server); the standalone Windows build is fixed + verified (raw-RGBA textures via
  `LoadRawTextureData`, `build-client.ps1` now fails loudly on a bad build). **Post-FX DONE** — a
  code-only stack (`PostFx.cs` + `Spacecraft/Post{Bloom,Composite,AO}`): **bloom** + **ACES tonemapping**
  + vignette + **SSAO**, preset-gated (Potato off → Low tonemap → Medium +bloom → High +SSAO). Still
  **this pass**: **planet-side ship = space-ship model** (engines off,
  hatch open), **per-ship speed + manoeuvrability** (`ships.json`). Standing planned (tracked above, do
  not drop): **player/NPC/equipment-use animations** (see "Held tools/weapons + use animations" +
  creatures), **weapon/equipment VFX** (see Player weapons + Equipment), and a **texture audit**
  (items/icons, creatures/NPCs, planet surfaces, ship-hull variants — generate more as needed). **DONE
  this pass:** clouds + rain (weather), **post-FX** (bloom/tonemap/SSAO/vignette), and the **suit
  lamp + emissive glow + ship lights** (headlamp toggle L; emissive ores/crystals/lava/lights; ship nav
  position lights + front headlights; new emissive light blocks placeable in the ship editor).
- **Toggleable world map / planet overview — DONE** (`b54a1f5`). Full-screen planet map (key **M**),
  separate from the star map: player-centred **top-down render of the streamed terrain** (surface colour +
  height shade; only loaded chunks → **fog-of-war**), **player (heading) + ship** markers, **click-to-set
  waypoint** (the HUD compass now points to it), legend + coords + scale, zoom −/+. uGUI, bilingual.
  *Later: POI markers (stations/settlements/wrecks/mission targets), pan, a mini embedded version.*
- **Local time-of-day indicator — DONE** (`b7393bc`). HUD day/night widget (top-right under the compass):
  current phase + time until next dawn/dusk + a cycle bar with a marker (from `WorldEnvironment.TimeOfDay`
  + `DayLengthSeconds`). Bilingual. The visible sun already wanders across the sky by time of day.
- **Space travel lands on origin — FIXED.** Real inter-planet travel with **per-planet persistence**
  (`SwitchActiveWorld` keyed by celestial-body `LocationId`, `HandleTravel`/`ResetWorldRuntimeState`,
  `WorldReset` message); landing zones, block edits + containers persist per body. Tests cover it.
- **World alive on entry — DONE.** Fauna is now **seeded immediately** on join + on travel arrival
  (`PopulateCreaturesNear`), spread around the player in a ring on the ground (no longer trickling in
  one every 6 s onto a single spot); flora already generates with the chunk (present on entry). Barren
  worlds stay empty. Tests cover the spread spawn + arrival population.
- **Space stations + NPCs — DONE.** Stations are anchored/placed and reachable, populated with NPCs
  (`SpawnStationNpcs`) and rendered on the client (`NpcView`). Stations are now also procedurally
  enriched (furnished interiors, exterior detail, module shapes — see P5 above).
- **(NEW, design question) More professional look with effects:** a polish pass to lift the overall
  look — clarify target tier + ordering first (preset-gated). Directions: post stack (bloom on
  emissives/sun, tonemap + per-biome colour grading, AO, subtle vignette/film grain), anti-aliasing,
  god rays from the sun, soft shadows, animated emissive water/lava, atmospheric scattering + nicer
  skies/denser stars, particles (dust/sparks/muzzle/engine trails), camera feel (head-bob, FOV kick,
  impact shake), UI motion (transitions, holographic scanlines/glow).
- **(NEW, planned) Advanced graphics techniques — see [docs/ADVANCED_GRAPHICS_PLAN.md](docs/ADVANCED_GRAPHICS_PLAN.md).**
  Research + roadmap for **shell shaders** (grass/fur/turf on ground, fauna, flora), **parallax occlusion
  mapping** (fake depth on rock/ore/metal/panel block faces), **normal maps** (highest bang-for-buck for
  the voxel look), a **post-processing stack** (bloom/tonemap/SSAO/per-system+biome colour grade/vignette),
  **emissive glow** ✅ (done), **normal maps** ✅ (done — Sobel-derived per-block, tangent-space lighting),
  **post-FX stack** ✅ (done), **HDR nebula skies + atmospheric scattering + god rays**, a **water/lava shader** with
  planar reflections, **GPU detail scatter**, translucency, decals, reflection probes, AA/camera feel.
  Phased + preset-gated; **Phase 0 = decide Built-in RP vs URP migration** (gates SSR/TAA/volumetrics).
- **Crafting/Tech/Ship menu redesign — DONE** (`1439713`, `ac31c1e`; plan in
  [docs/CRAFTING_TECH_SHIP_UI_PLAN.md](docs/CRAFTING_TECH_SHIP_UI_PLAN.md)). A **uGUI 3-pane** screen
  (`CraftingTechShipUI`, DPI/resolution-independent) replacing the three flat lists: category sidebar +
  searchable card list + detail. **Crafting** (categories, search, "craftable now" filter, ingredient
  have/need pooled from inventory + cargo, required station/blueprint, why-blocked reason). **Tech**
  (tiered progression tree by prerequisite depth, status colours, prereqs + unlock cost in detail).
  **Ship** (module cards, fleet switch, craftable-ship cards with full stats). **Location-bound**
  (workshop/lab/console — lab + console station tiles added to the ship). Backend syncs
  `UnlockedBlueprints` to the client; 16 AI category icons; bilingual. **Then migrated the WHOLE shell to
  uGUI** (`de9d39a`, `cec75ff`): the remaining Tab-menu tabs (Inventory/Map/Missions/Character/Space) plus
  **Settings + Credits** — no IMGUI menus left (only the splash animation + HUD overlays stay IMGUI).
  *Remaining polish: drawn node-edge graph for Tech, "missing material → where to get it" popover, success
  animations.*
- **(NEW, planned) Avatar overhaul:** appearance **reflects equipped gear** (armor chest/legs/helmet,
  suit, lamp, jetpack visible on the body + to other players); **improved avatar model + textures**
  (better proportions, skinning); **avatar animations** (idle/walk/run/jump/mine/attack/place + tool-use);
  and a **dedicated avatar texture/skin designer** (menu-based, exports skin JSON/texture, merges in like
  the other content editors). Builds on the existing `PlayerAvatar` + per-part colour settings.
- **Animation pass — DONE (first pass).** Player/remote/NPC avatars already self-animate (speed-scaled
  walk cycle, idle sway, tool-swing chop via `PlayerAvatar`). This pass added the **held tool/weapon in
  hand + first-person viewmodel** (above) and **procedural creature animation** (`CreatureAnimator`:
  leg-swing while moving, wing flap for flyers, tail sway — `CreatureBuilder` now builds legs/wings/tail
  on pivots). Still planned: creature idle breathing + per-temperament gestures, NPC work/idle gestures,
  equipment-use poses (lamp/scanner), networked held items for remotes, flock/path movement.
- **(NEW, planned) Player chat via radios:** a **radio/comm device** in **3 tiers** (blueprint-unlocked,
  each harder to craft) gates **player-to-player chat**, with range by tier: **T1 same planet → T2 same
  system → T3 all players**. Needs a `ChatIntent`/`ChatMessage` protocol + client chat UI (open/type/
  scrollback), server-authoritative recipient resolution by range (reuses travel/galaxy data),
  rate-limit + length cap, bilingual strings.
- **(NEW, in progress) Ship-type editor — see [docs/SHIP_TYPE_EDITOR_PLAN.md](docs/SHIP_TYPE_EDITOR_PLAN.md).**
  A main-menu **ship designer**: an empty 3D build room (move like in-game) to place hull/glass/all ship
  **stations** + **hatch** + **lights** + **engine**, name the design and set its stats + blueprint cost
  + craft cost, then **save it as a ship type**; a `merge_ship.py` folds the export into `data/ships.json`
  + a voxel layout that `StampShip` stamps. Phased (editor MVP → new elements → integration → polish).
- **(NEW, planned) Station + town/village editors + template world-gen — see [docs/STATION_SETTLEMENT_EDITOR_PLAN.md](docs/STATION_SETTLEMENT_EDITOR_PLAN.md).**
  Like the ship editor: a **space-station editor** + a **town/village editor** (free-fly build room,
  block palette + interaction markers, metadata, export bundle + merge), and **world-gen picks from pools
  of hand-designed station/village/town types** in addition to procedural (a seed roll at the two placer
  call sites; empty pools → today's behaviour). **Includes an assessment of the current procedural output:**
  villages are a fixed 17×17 of 3–4 *identical* huts, towns 25×25 of identical 2-storey boxes (variety LOW,
  only 2 sizes); stations vary in silhouette (random walk) but every module is the same empty 7×6×7 shell
  (variety MEDIUM). Improvable via (1) the templates here and (2) a **procedural-enrichment** pass (P5).
  **P5 slice 1 DONE** (`b08e7d6`): settlements got per-instance size jitter, per-building footprint/height/
  roof/door/accent variety, a central feature (well/monument/plaza), street paths, lamps + gardens, an
  optional fence, and alien theming; stations got **type-specific furnished interiors** (consoles, counter,
  heal tank, bunks, crates) + ceiling lights. **P5 slice 2 DONE** (`c6f0c2c`): stations got **exterior
  detail** (solar-panel wings, roof antennae, a hub command dome) and settlements got **four size tiers**
  (hamlet→village→town→city). **P5 slice 3 DONE** (`3af0f86`): station **module shapes** (round command
  hub, octagonal modules, glass observation domes, roof connector conduits) + settlement **biome theming**
  (biome flora/paths/roofs). **P5 complete** — different module *footprints* (long corridors) were
  considered and **dropped** (higher risk, low marginal payoff). The **editors + template pools**
  (P1–P4 of the plan) remain to be built.
- **(NEW, planned) Equipment/recipe editor + merge:** a menu-based editor for new items + crafting
  recipes (stats, costs, blueprint gating), exporting JSON, plus a **merge script** into `data/*`. Pairs
  with the ship-type editor above.
- **M28 — Windows player build — tooling DONE, needs a real build run.** `BuildScript.BuildWindows`
  (editor, generates a minimal `Launcher.unity` with one `AppShell`, builds StandaloneWindows64 →
  `Build/Windows/Spacecraft.exe`, auto-includes StreamingAssets = data + bundled server) +
  `scripts/build-client.ps1` (one command: runs sync-libs + publish-server prereqs, then the
  headless Unity batch build; `-SkipPrereqs` to skip). *Next: run `./scripts/build-client.ps1` with
  Unity installed → a self-contained .exe Justus can double-click.* Optional WebGL Lite later.
  Note: real hand-/AI-authored art & audio files remain an asset task; everything so far is
  generated in code (textures, avatars, SFX) — no bundled binary assets.

Later/optional: Option B true in-process SP server (retarget to netstandard2.1); per-player ships
+ PvP ship combat.

## How to resume

```powershell
dotnet build Spacecraft.sln
dotnet test                      # expect all green (240)
git log --oneline -5             # latest = M20 client shell, assets & UX
```
All milestones from the local plan (M0–M20) are now implemented on the server/shared side
with a Unity scaffold. Next work is open scope (see Pending). Note: this sandbox blocks real
sockets, so live WebSocket/2-player network tests are exercised via in-process/loopback, not
real ports; the Unity client scripts are not compiled by `dotnet` (Editor required).
