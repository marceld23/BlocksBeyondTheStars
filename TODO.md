# SpaceCraft — Project Status

The single source of truth for **what is built** and **what is still open**. Design notes and deep
plans live under [docs/](docs/) (committed); this file is the high-level status. Player-facing operation
(controls, mechanics, editors, commands) is documented in [docs/USER_MANUAL.md](docs/USER_MANUAL.md) —
keep it current when controls/features change. Last consolidated 2026-06-04.

**Build:** `scripts/build-client.ps1` (publishes shared libs + bundled server + Unity Windows player).
**Test:** `dotnet test` — currently **293 passing**. Locale parity (en/de) is enforced by a test.
**Conventions:** English docs/comments; in-game text bilingual DE+EN; commit to `main` with the
`Co-Authored-By: Claude Opus 4.8` trailer; paid/AI asset generation is gated (propose + approve first).

Architecture: Unity 6 (Built-in RP) client + authoritative .NET 8 server, everything built in code (no
scene authoring). One shared world; contractless MessagePack networking; deterministic seed world-gen;
SQLite persistence.

---

## ✅ Done (2026-06-06): world block — terrain archetypes, seas, trees
Done in the user's reconsidered order (terrain shapes the basins → fluids fill them → trees on the land):
- **Regional terrain archetypes** — `SurfaceHeight` modulates the base terrain by a large-scale field that
  blends a seed-picked subset of archetypes (flats / rolling plains / hills / mountains / canyons; canyons
  ridged), so a world varies between flat and rugged and worlds differ. Deterministic + X-seam-safe.
- **Surface seas** — basins fill below a per-world sea level: **water** on atmosphere worlds, **lava** on
  volcanic/airless ones (never both); `PlanetType.WaterAbundance`/`LavaAbundance` (null = auto). Depth/island
  variety falls out of the terrain. Land flora skips underwater.
- **Trees** — new `wood_log` + `tree_leaves` blocks (with **OpenAI tile textures**) + a generator pass:
  multi-block trunk + leaf crown on grass/earth/mud, seam-safe across chunk/longitude edges.
  `PlanetType.TreeDensity` (null = auto). 5 new world-gen tests; 297 pass.
- **Two earlier bugs**: ship-station prompt is now look-based (not "always Workshop"); ships fill caves
  under their footprint + the spawn holds longer so you can't fall into a cave on load.
- Follow-ups noted (für später): aquatic fauna + water flora; mineable water/lava (beam + source logic).

## ✅ Done (2026-06-06): menu/editor/polish wave
- **Craft quantity** stepper (− [n] + / Max), server clamps batch 1..999.
- **Delete singleplayer worlds** from the save picker (with a confirm dialog).
- **Editors load saved designs** — ship + structure editors gained a LOAD button (lists exports, rebuilds
  the chosen design to keep editing).
- **Connect-to-server** dialog (editable IP/port) on the menu's Join button (remote MP was already wired).
- **Round background stars** in space (removed the blocky star cubes; the Starfield dome provides them).
- **Fall damage** on hard landings (client reports impact speed → server applies armor-reduced damage; a
  lethal fall respawns with the death flash). New `FallDamageIntent` (47).
- **Credits** rewritten to the JuMaVe Games family project (+ community call), en/de.

## ✅ Done (2026-06-06): bug-fix wave — glass, space, combat, death feedback
- **Milky glass** (transparent shader: non-emissive glass alpha 0.72 + white frost; fields stay see-through);
  ship-editor glass relabelled **Window**.
- **Space:** planets/moons now never overlap (a relaxation/separation pass de-overlaps the whole set, not
  just a moon vs. its own parent); render planets even when `PlanetType` is missing; **station keep-out** so
  you slide around a station instead of flying through (still dockable with E); radar no longer paints a
  phantom red blip at the rim for an out-of-range enemy; **asteroid fields slowly respawn** over a session.
- **Combat/vendor:** machete 30→12 damage (no longer one-shots most animals, HP≈14–36); pressing **E at a
  vendor opens the market/trade view** (OpenMarket reordered so the category sticks).
- **Death feedback:** planet death → **red screen flash + `player_death` sound**; ship destroyed in space →
  **explosion glare + `space_death` sound** (new `DeathFx`; `RespawnNotice.Died` distinguishes a death from
  the void-rescue). Two new ElevenLabs cues.
- Persistence analysed: planet/moon/station positions are deterministic from the seed (stable across
  land/relaunch); only asteroids weren't replenishing — now they do.

## ✅ Done (2026-06-06): Void-fall fix, ship-editor doors, NPC walls/animation, studio rename
- **Infinite-fall fix (the "PC2 falls forever" bug):** root-caused — the world has no bedrock floor, so a
  player below the terrain with nothing under them falls forever; their position is **persisted and restored
  verbatim on join** ([GameServer.cs](src/Spacecraft.GameServer/GameServer.cs) `LoadPlayer ?? CreateNewPlayer`,
  and `SetupPlayerShip` doesn't reset Position), so one fall **poisons the save** and every launch drops them
  again — machine-specific because each PC has its own `singleplayer-saves`. New `GameServerSpawnSafety`:
  `EnsureSafeSpawn` validates a joining player's position (snaps a void position — and a poisoned respawn
  point — back to safe ground), and `TickVoidRescue` recovers anyone plummeting at runtime before the fall
  can be saved. "In the void" = well below the column's terrain surface with nothing solid within reach.
  2 tests.
- **Studio rename:** Unity `companyName` → **"JuMaVe Games"** (productName stays "Spacecraft"). Note: this
  moves `persistentDataPath` to `…/LocalLow/JuMaVe Games/Spacecraft/` — old singleplayer saves live under the
  former `…/Spacecraft/Spacecraft/` path.
- **Ship-editor doors:** the **ship editor** palette gained a **Door**; designed-ship `door_slide`/`door_hinge`
  cells are opened (3 tall) and registered as sci-fi slide doors by the door registry (now rebuilt from every
  settlement **and** ship stamp in the world). Settlement editor already had slide+hinge doors.
- **NPCs no longer walk through walls:** `MoveNpcs` only checked the ship before; it now also rejects a step
  into a solid block (`BlockedByWorld`) — inhabitants stay inside their building (doorway openings stay
  passable). Tighter wander leash (2.5 → 1.6).
- **Smoother NPC motion + walk animation:** NPC position broadcast 0.5 s → 0.2 s and the client interpolates
  without fully catching up (lerp 8 → 5), so NPCs glide instead of stop-start jerking; the shared avatar
  walk cycle reaches a full stride a little sooner so slow strolls read as walking (helps the player avatar
  too).

## ✅ Done (2026-06-06): Doors (settlements) — D1–D5
Settlement doorways now hold real, server-authoritative doors (rendered + collided client-side, since
movement is client-side). Three bespoke **ElevenLabs** SFX: `door_slide_open`, `door_slide_close`,
`door_hinge`.
- **D1 — server `GameServerDoors`:** a per-world `ServerDoor` registry built from `door_slide`/`door_hinge`
  markers on stamp; the wall axis + gap width are **inferred by probing the surrounding blocks**, so doors
  line up regardless of facing. `TickDoors` auto-opens slide doors for a player within range and auto-closes
  them a moment after the last one leaves; `HandleDoorInteract` toggles a hinge door a player stands at.
  Cleared in `ResetWorldRuntimeState`; sent on join/travel (`SendDoors`) + broadcast on change. Messages:
  `DoorInteractIntent` (46), `DoorList` (93).
- **D2 — client `DoorView`:** renders each door from `DoorList` (slide = two panels swoosh apart; hinge =
  a leaf swings ~96°), with a `BoxCollider` that blocks the `CharacterController` while closed and lifts
  while open. Plays the slide/hinge SFX on state change; shows an "E" hint over a reachable hinge door.
- **D3 — generator places doors:** the settlement generator emits a door marker at each (non-ruined)
  building's doorway — **slide** for towns/cities, **hinge** for villages/hamlets; the opening stays air.
- **D4 — editor:** the settlement editor palette gained **Slide door** + **Hinge door** markers (they flow
  through `StructureTemplate` cells → the D1 registry).
- **D5 — localization + tests:** `ui.door.hint` (en/de); 3 server tests — a slide door auto-opens on approach
  + auto-closes, a hinge door toggles on interact (and not from afar), doors register at real doorways.
- **Remaining (follow-up):** auto-doors for **orbital stations + the ship** (their doorways are cut as air
  but don't yet emit door markers — the registry already accepts them once they do); station/ship editor
  door palette entries.

## ✅ Done (2026-06-06): JuMaVe Games studio splash + moon-overlap fix
- **Studio splash:** a new `StudioSplash` (+ `ShellPhase.Studio`, now the first phase) shows the **JuMaVe
  Games** developer-studio screen for **5 s** right after the "Made with Unity" screen, before the SPACECRAFT
  title splash. Code-built uGUI: an assembling block-cluster emblem inside a sweeping orbit ring (a little
  rocket circling it + twinkling stars + a glow pulse), the gradient wordmark (**Ju** cyan · **Ma** white ·
  **Ve** orange) with "GAMES", the slogan **"Built from imagination."**, and a reveal flash. Skippable after
  a moment. A whoosh→tada sting lands on the reveal (`AppShell.PlayStudioSting`) — falls back to the intro
  sting until the bespoke **ElevenLabs** `audio/jumave_sting` is bundled (proposed for approval).
- **Moon overlap fixed:** the compact `SystemViewScale` (0.16) had sunk moons *inside* their planets (moon
  orbit 90 system-units × 0.16 = 14 flight units < a planet's 23-unit radius). `BuildSystemBodies` now places
  each moon relative to its **parent planet** (nearest in system space) and pushes it **out past the planet's
  surface**, so moons orbit clear of their planet at any view scale.

---

## ✅ Done (2026-06-06): Closer planets, radar bearings, ship-systems quick-bar
- **Planets closer together:** flight distance = orbit spacing (~520 system units between adjacent orbits)
  × `SystemViewScale`. That was 0.30 → ~156 flight units (~11 s cruise) between neighbours; lowered to **0.16
  → ~83 units (~6 s)** so the system is a short hop, not a slog. Client-only (no universe regen).
- **Radar bearings to planets:** the cockpit radar (`SpaceRadar`) now reads `SpaceView.Landables` and draws a
  **green marker per planet/moon**, pinned to the rim when far so it reads as a **direction arrow** ("head
  that way"); the readout under the radar shows the nearest dockable station or, failing that, **➜ nearest
  planet · distance**.
- **Ship-systems quick-bar:** a HUD bar in flight built from the active ship's fitted modules
  (`ShipCombatStatus.Modules`, new) — **Laser** (any weapon module) + **Tractor** (tractor beam). Pick with
  **1–9**, **use with LMB**: the laser auto-locks the best target ahead (mines + fights), the tractor does a
  **manual wide sweep** to pull in salvage (new `TractorPullIntent` → wider-range `CollectSalvage`). The
  starter ship now carries a tractor beam too. Bottom controls hint reworded (en/de).

---

## ✅ Done (2026-06-06): Machete actually hits + in-game Settings tab
- **Melee attack fixed:** equipping the machete *reduced* your reach (the server used the weapon's short
  range 3.5 while the client targets within the 6-block swing reach, so hits at 3.5–6 silently rejected). A
  weapon now never reaches *less* than the default swing (`reach = Max(weaponRange, EnemyAttackReach)`), and
  **left-click attacks** when a weapon is held (same swing as F). Test
  `Machete_HitsCreature_WithinDefaultReach_EvenBeyondItsShortRange`.
- **In-game Settings tab:** the Tab menu's old "Character" tab is now **Settings** — keeps the appearance
  rows and adds **master volume** (− / +, applied + persisted live) and an explicit **Save game now** button
  (new `SaveGameIntent` → server `SaveAll()`). en/de.

---

## ✅ Done (2026-06-06): Starter weapons + ship weapons finished (W1–W5)
- **W1 — starter melee weapon:** new players spawn with a **machete** (the existing melee attack +
  slash-arc VFX already worked). Test `StarterKit_IncludesASimpleMeleeWeapon`.
- **W2 — dual ship laser:** a new **`ship_laser_basic`** module (`weapon_class: 2`) fitted on every starter
  ship (starter/scout/hauler). A new dual class means **one laser both mines asteroids AND fights hostiles**
  (`WeaponSpec` now carries `IsCombat` + `CanMine`; `weapon_class` 0=mining, 1=combat, 2=dual). Tests
  `StarterShip_HasADualLaser_ThatMinesAsteroids`, `…DualLaser_AlsoDamagesHostiles`.
- **W3 — fire in flight:** hold **LMB / Space** in the space view to fire — it auto-locks the best target
  in range ahead (a centre crosshair brightens cyan on lock), rate-limited by the weapon cooldown. (Was only
  fireable from a tech-UI list before.)
- **W4 — SFX + VFX:** a laser **bolt** from the ship to the target + an **impact flash** (amber for mining,
  cyan for combat), with new procedural **`ship_laser`/`ship_mine`** sounds (the old `ship_weapon` cue never
  existed, so firing was silent).
- **W5 — craft + editor:** weapon modules are built/fitted from the ship tech UI (craftable; cannons keep
  their blueprints, the starter laser is always buildable) and the **ship editor palette** now carries a
  Laser Cannon + Ship Cannon. Controls hint updated (en/de).
- **Follow-ups:** fire-in-flight always uses the starter laser (could auto-pick the best fitted weapon);
  cooldown/energy are client-side only (no server-side rate/energy gate yet).

---

## ✅ Done (2026-06-06): New-world spawn fall-through — nearest-first chunk streaming
Spawning on a fresh planet dropped you **below the surface**. Cause: `StreamChunks` streamed a fixed
bottom-up column (`dy=-3` first), so with a large view distance + a new world's slow first-gen the **surface
chunk (your floor) arrived only after many ticks** — past the client's settle-freeze timeout, which then
released and let you fall through. Fix: stream **nearest-first** (sort the view column by distance to the
player), so your own chunk (its floor) loads before anything else and the freeze releases onto solid ground
immediately; the freeze timeout was also raised 8s→12s as a margin. Same spirit as the station floor pad.
Test `StreamChunks_SendsThePlayersOwnChunkFirst_SoSpawnGetsGroundFast`.

---

## ✅ Done (2026-06-06): Land on moons + real textured station/enemy models
- **E-to-land did nothing on some bodies because moons were rejected.** `HandleTravel` only allowed
  `CelestialKind.Planet`, but the space view offers landing on **planets and moons** — flying to a moon
  showed "Press E to land" and the server silently rejected it. Now both are accepted. Test
  `Travel_LandsOnAMoon_NotJustPlanets`. (Worlds generate on demand in `LoadWorld`, so an unvisited body was
  never the blocker.)
- **Asteroids/stations/enemies — textures.** Asteroids were already stone-textured; stations + space enemies
  were flat colour cubes. They're now **real textured multi-cube models** (mirrors the ship), built from the
  bundled block textures: a station (iron hull hub + glass viewport collar + docking arms/pods + solar wings
  + beacon, slow spin), a drone (carbon body + glowing eye + pods), a UFO (titanium saucer + glass dome +
  underside lights, fast spin) and a cruiser (iron hull + glass bridge + twin glowing engines). `Spin` got a
  `Configure(axis, speed)` for the fixed rotations. No external art needed.

---

## ✅ Done (2026-06-06): Only real bodies in space — every planet is landable
You couldn't land on a planet you flew to because it was a **decorative** sphere (`planet2`) not in the
landable set — the land prompt never appeared. Removed the two decorative planets entirely; the space view
now renders **only real celestial bodies**: the planet you launched from (rendered "below" you, landable →
E returns home) and the system's actual planets/moons at their scaled orbit coords (from the star map), all
lit + textured + landable via a shared `SpawnBody`. `EnterSpace` now also `SendStarMap` so the system's
bodies are always available when the space scene builds. Per-body land approach already shows "Press E to
land" just outside each body's keep-out.

---

## ✅ Done (2026-06-06): Safe launch + reach the system — combat was killing you on spawn
The real cause of "I take continuous hull damage then suddenly respawn on the planet":
- **Hostile drones/UFOs spawned ~25u from the launch point**, inside `ShipEngageRange` — so a combat preset
  (coop-survival/dangerous/pvp) hammered the ship the instant it launched, destroyed it, and `DisableShip`
  respawned you at base. They now spawn **far** (≥150u) → launching/docking is safe, combat is opt-in.
- **Ship weapon range was measured from the origin** (0,0,0), not the ship — so you literally couldn't
  shoot anything you flew out to. Now measured from `instance.ShipPosition`; engage range tightened 85→70 to
  sit near the weapon ranges (40–70).
- **System flight reachable:** with combat no longer spawn-camping you, the system's other planets (placed
  at their scaled orbit coords, ~156u+ out) are flyable + landable again. Per-body land approach (radius +
  margin + band) shows "Press E to land" just outside each body's keep-out, and the **launch planet is now
  landable too** (E returns you home) so there's always a planet to land on, not only the station.
- Tests updated (drones now spawn far): `NpcDrones_DisableShip…`, `ShipCannon_DestroysDrone…` fly the ship
  to the drones first; `DistantHostile_DoesNotDamageShip_UntilWithinRange` covers the range gate.

---

## ✅ Done (2026-06-06): No fly-into-planet / auto-land; E-land prompt actually shows
Follow-up to the E-landing change:
- **Keep-out barrier around every body** (landables + the two decorative planets): the ship slides along a
  body's keep-out sphere instead of flying into it (radial push-out each frame in `UpdateCruise`). No more
  "flew into the planet / dropped onto it" — you stop at the approach distance and press **E** to land.
- **Prompt + E priority by distance:** the land/dock prompt now shows whichever you're **closest** to (was
  station-always-wins, so the planet prompt never appeared when a station sat near the body cluster). E acts
  on the closer of the two.
- **L = return to the launch body only** (`SendLeaveSpace("")`) — it no longer lands you on a nearby body
  (that's E now), so there's no surprise drop. Confirm reworded to "Return to the surface…".
- Note: a likely extra cause of "auto-landing near planets" was the ship being **disabled by off-screen
  drone fire** (now range-gated) → `DisableShip` forces you out of space onto the base.

---

## ✅ Done (2026-06-06): Land on a planet with E (like docking a station)
Flying near a planet/moon now shows **"Press E to land on \<name\>"** and **E lands** there — the same
proximity → E flow as station docking (was "Press L" + an Enter/Esc confirm). In `SpaceView.UpdateCruise`,
E is the context action: dock a station you're next to, else land on the body you've flown up to
(`SendLeaveSpace(_landTargetId)`, server flies the descent). **L** stays as the "return to the body you
launched from" shortcut. Reworded `ui.space.land_prompt` + `ui.space.controls` (en/de).

---

## ✅ Done (2026-06-06): Space follow-ups — no relentless shake, a real sun disc
- **The "ship being shaken" + red screen was continuous damage feedback.** `incoming` ship damage summed
  **all** hostiles in the instance with **no range check**, so a distant/off-screen drone plinked the ship
  every tick → permanent `_shake` + red `_hit` overlay. Now hostiles only fire within `ShipEngageRange`
  (85u), so flying clear stops the damage + recharges the shield. Client feedback is now **proportional and
  set-not-accumulated**: chip damage is a faint rumble, a real hit is a sharp jolt (no more max-pinned
  shake/flash). Test `DistantHostile_DoesNotDamageShip_UntilWithinRange`.
- **The sun now reads as a sun.** The single additive billboard in the raw star colour looked like a vague
  red/blue wash (and an orange-red star is in the palette). It's now layered — a coloured corona + a
  **white-hot core** — so the star is a clear bright disc in any colour; the lens flare is much subtler +
  desaturated (no red screen wash).

---

## ✅ Done (2026-06-06): Station/space polish wave — undock, NPCs, sun, windows
- **Undock returns to flight:** leaving a station now relaunches you into the **space instance (ship view)**
  around the orbited planet instead of dropping you onto the surface (`LeaveStation` restores the planet
  world underneath, then `EnterSpace`). Test `LeaveStation_UndocksBackIntoSpaceFlight`.
- **Station crew stands on the deck:** station NPCs snapped their feet to the floor grid (the marker sits
  +0.5 centred in the air cell) — they no longer float. Settlement NPCs already snapped (verified). Test
  `StationCrew_StandsOnTheDeck_NotFloating`.
- **Ship no longer "wobbles" in space:** the only continuous motion on the ship was the thruster exhaust
  flicker (`Sin(t·28)·0.12`, ~4.5 Hz) — tamed to a slow gentle shimmer; the launch animation itself is a
  clean ease.
- **The system sun, in its colour:** space view now renders the star as a bright additive billboard tinted
  by `Environment.SunColor`, plus a **screen-space lens flare** that blooms as you turn to look into it.
- **Real station windows + a view out:** the viewport band is now 2 blocks tall (proper windows, see-through
  via the transparent pass); a new `StationBackdrop` shows the **orbited planet + the sun** outside while you
  walk the station, so looking out a window shows the solar system (stars already showed through). Editor
  already carries the glass "Viewport".

---

## ✅ Done (2026-06-06): Station safety + see-through fields + a twinkling starfield
- **Stations are peaceful:** void worlds now hard-skip `TickEnemies`/`TickCreatures`/`TickFlora`, and
  `ResetWorldRuntimeState` clears the species roster on every world switch — so no hostile aliens or
  wandering wildlife ever spawn aboard a station; only the peaceful crew NPCs live there.
- **No more holes to space:** the hangar mouth is glazed with a new **`force_field`** block (unbreakable,
  `mineable:false`) instead of an open air gap — you can't walk out into the void.
- **Real see-through rendering:** glass + force fields render in a second, alpha-blended chunk submesh
  (`Spacecraft/BlockAtlasTransparent`) with transparency-aware face culling, so station **viewports and the
  hangar energy field actually show space** (and stars) through them. Force fields glow cyan. Added to the
  station editor palette ("Energy field").
- **Twinkling starfield:** `Starfield` + `Spacecraft/Starfield` shader draw an additive star dome behind the
  world that follows the camera and fades in for space, airless skies, station interiors (seen through the
  windows) and **planet nights**, fading out toward noon. Each star pulses on its own phase.

---

## ✅ Done (2026-06-06): Station boarding fall-through — the real root cause
The "dock and fall **immediately** into space" bug survived every prior fix because the settle-freeze was
being released by a **ghost collider**. `OnWorldReset` removed the old world's chunks with `Destroy(go)`,
but Unity defers `Destroy` to frame-end — so the old planet chunk colliders still existed the same frame the
player snapped to the station spawn. The freeze's downward raycast hit one, decided "ground is here",
released instantly; then frame-end destroyed those colliders and the player dropped through into the void
before the station floor had streamed. Fix: `OnWorldReset` now `SetActive(false)` on each chunk **before**
`Destroy` (deactivation is immediate), so the freeze raycast can't see stale colliders and holds the player
until the real station floor chunk streams in. Also hardens normal planet-to-planet travel.

---

## ✅ Done (2026-06-05): Space stations as their own locations
Boarding a station is now a real world transition (the proven `WorldReset` path): each station is its **own
void world** (space sky, no weather/clouds, lit interior, life support, NPCs), so you land **inside** the
station floating in space instead of falling through to the planet. Implemented S1–S7 of
**[docs/STATION_AS_LOCATION_PLAN.md](docs/STATION_AS_LOCATION_PLAN.md)** (`PlanetType.Void` + all-air gen,
`orbital_station` type, `LoadWorld` skips surface content for void worlds, `BoardStation`/`LeaveStation`
travel in/out, station NPCs per-world). Tests: `BoardStation_PutsPlayerInOwnVoidWorld_OnSolidGround_…`,
`LeaveStation_TravelsBackToThePlanet`, `VoidPlanet_GeneratesEmptySpace`.

---

## ✅ Done

### Foundation & server (M0–M20)
- .NET solution; shared data-driven content model + bilingual i18n; deterministic procedural universe
  (systems → bodies) from seed; SQLite persistence; LiteNetLib + loopback + MessagePack codec.
- Authoritative game server: tick loop, mine/place/craft/blueprint validators, admin API, self-hosting
  publish scripts.
- Game modes (Survival/Creative) + authoritative `GameRules` + presets; death/respawn at Medbay
  heal-tank + salvage capsule; admin roles + logged cheats.
- Mission system (system + player missions, reward depot); per-world content packs (missions/blueprints).
- WebSocket gateway + composite transport + web portal; optional Python AI mission backend (off by default).
- Personal landing zones; ship docking (request/accept/undock handshake, guest access, undock-on-disconnect).
- Space flight + PvE combat slice: ship hull/shield, ship-weapon blueprints/modules, local space
  instances, NPC drones + asteroids, planet enemies — all rule-gated, no permanent ship loss.
- Client shell: AppShell phase machine (splash → menu → settings → loading → in-game), local settings.

### World & exploration
- Seed world-gen: terrain, caves, ores (depth-banded veins), flora, multi-biome planets, 8 planet types
  (`data/planets.json`); atmosphere/oxygen rules; per-system suns.
- **Hyperspace travel** between systems (gated by a `jump_generator` module).
- **Station + settlement template world-gen** — generated worlds pick hand-designed station/town/village
  templates from a pool (~35% chance) when present, else stay procedural.
- Space stations: boarding, interiors + NPC populations (scaled by size tier), tractor beam + cargo,
  radar scanner tiers + named stations + location readout.

### Gameplay systems
- Mining/placing/crafting/blueprints; tech progression; trade (player↔player atomic swap) **with client
  panel**; scanning (handheld + ship) **with HUD readout**; wreck repair (server + progress UI).
- Survival: health/oxygen/hunger/energy; suit lamp; flora harvest drops (e.g. berries); creatures
  (habitat-gated spawns, temperament, visible attacks).
- **No building inside the ship**; ship interior stays a fixed hollow structure on landing.

### Client / UX
- In-game HUD + Tab menu (Inventory/Map/Missions/Character/Space), modern uGUI theme, UI sounds.
- **Chat** (open/type/send + scrollback) → `ChatIntent`/`ChatMessage`; `/bump` debug snapshot command.
- **World map / planet overlay** (M): top-down fog-of-war terrain, player/ship/station markers, waypoints.
- **Day/night clock** on the planet HUD; weather (IMGUI rain + lightning, density-scaled), silenced in caves.
- Singleplayer **save selection / new world** picker.
- First-person viewmodel + held tool; networked gear/held items; **avatar reflects equipped gear**
  (helmet/chest/legs/pack/lamp); procedural player + creature animation.

### Editors & tooling (full suite)
- **Ship editor**, **Avatar/skin designer**, **Station editor**, **Town editor**, **Item & recipe editor**,
  **Material editor** — each in the Editors submenu, each exporting a JSON bundle.
- Python merge tools fold bundles into `data/`: `merge_ship.py`, `merge_structure.py`, `merge_recipe.py`,
  `merge_material.py`. Material editor paints a 64×64 tile, sets mining/look/spawn (world-type targeting),
  and its look is **data-driven** via optional `BlockDefinition` fields (Gloss/Metal/Emission/Color).

### Audio & graphics
- Fully procedural audio (synthesised cues/ambiences/loops; the game is audible with zero recorded assets);
  hyperspace + boarding hooks; spatial creature voices.
- Lit block shader (per-material gloss/metal/emission, normal-mapped atlas); **per-face skylight** so caves
  + interiors go dark except lamp/emissive light; camera feel (head-bob, FOV kick, landing shake); denser
  starfields + drifting cloud shell on the menu planet; ship + station window panes.

---

## 🔧 Open / pending

### ✅ Done (was "partial — client polish/VFX")
1. ✅ **Jetpack (done).** Craftable item + blueprint + recipe (`jetpack`, workshop, gated). Hold Space in
   the air to thrust up (`PlayerController`); server-authoritative suit-energy drain + force-off when empty
   (`SetJetpackIntent` → `HandleSetJetpack`/`TickJetpack`), suit energy recharges aboard ship. VFX: twin
   thrust flames + a looping thrust hiss (`ClientAudio.JetClip`), shown on remote players too (presence
   `Jetpacking`). Test `Jetpack_DrainsSuitEnergy_WhileActive_AndRejectsWithoutOne`.
2. ✅ **Weather (done).** IMGUI rain wash + lightning (`WeatherFx`), storm/rain ambience bed + thunder
   (`ClientAudio`, cave-silenced), 3D in-world rain falling around the player + storm fog/view-distance
   scaling (`WeatherFx3D`). All gated on open sky + intensity-scaled.
3. ✅ **Animation pass (done).** NPCs now play ambient **work gestures** (theme/role-paced arm swings —
   miners chip often, settlers/builders place, vendors gesture occasionally; `NpcView.WorkCadence`).
   Creatures got a **head pivot** with **per-temperament idle gestures** — passive graze (head dips to the
   ground), skittish alert (head snaps up + looks around), hostile lunge (sharp aggressive thrust), asleep
   rests low; plus idle breathing and a quicker tail on hostiles (`CreatureAnimator`/`CreatureBuilder`).
4. ✅ **Weapon/equipment VFX (done).** Beam/tracer, muzzle flash, impact sparks, scanner pulse — plus
   **kinetic projectile bolts** (gauss/slug fly to the target, trail sparks, burst on arrival), **melee
   slash arcs** (short-range weapons/fists sweep a fading arc, whiff included) and a **visible suit-lamp
   cone** (a faint warm translucent light shaft on the `Spacecraft/Cloud` shader, parented to the camera).
   Weapon effect picked by held item in `PlayerController.HeldWeaponFx` (`WeaponFx.Projectile`/`MeleeArc`).

### ✅ Playtest fixes (2026-06-04)
A large wave of playtest bugs, all fixed + committed:
- **Space view:** asteroids textured + slowly tumbling; reddish tint (planet day/night + biome grade leaked
  in) neutralised; on-foot hotbar + hand viewmodel hidden in space.
- **World/feel:** crushed-dark shadows lifted; monsters spawn on the surface 9–13 blocks away (not buried /
  on top); mining slowed; blocks can't be placed in the cell you stand in.
- **Ship:** the −X side was mineable (wrap-canonicalisation mismatch in the ship-stamp protection) — fixed;
  respawn snaps you to the ship heal-tank; **boarding a station no longer drops you on the planet** (client
  never moved to the station spawn).
- **NPCs/structures:** NPCs got faces (eyes) + stand on the ground; settlement + station doorways widened
  from 1×2 to 2×3.
- **UI:** Esc shows a DE/EN quit confirmation and returns to the **main** menu (was the editor submenu);
  hotkeys (M/Tab/T/K/U) no longer fire while typing in chat; scanner shows the localized material name;
  "suit" inventory filter includes suit gear.
- **Vendor trading (new feature):** E next to a vendor (or aboard ship) opens the **Market** category to
  barter resources for goods. Possible extension: vendor *selling* (goods → resources) + a priced shop UI.

### Landing + docking — reviewed (2026-06-04)
End-to-end trace done (launch/land, same-system travel, hyperjump, space-station boarding, player↔player
docking). Findings:
- **Fixed:** boarding a space station was a one-way trip — the client never sent `LeaveStationIntent`
  (server handler existed). Added `SendLeaveStation` + a **U = leave station** prompt while boarded.
- **Done:** **landing confirmation** (L opens an Enter/Esc prompt instead of dropping instantly) and a
  **station dock-approach animation** (`Phase.Boarding`: the ship flies in + fades before boarding).
- **Remaining polish (cosmetic):** **player↔player docking** is still an instant logical transition with
  no animation (a dock-approach there would match the station boarding feel).

### Recently shipped (was partial → now done)
- **Disassemble button** — Inventory detail pane shows a Disassemble button + recovered-parts preview,
  gated on a workshop (`CraftingTechShipUI.DetailInventory`).
- **Wreck repair hint** — the HUD wreck panel now tells the player to aim at a breach + press **R** and
  lists the blocks still needed (`WreckRepairStatus.Needs`).
- **Menu closes on launch/jump** — the gameplay menu auto-closes when a launch/landing flight sequence
  begins (planet or station → `SpaceViewActive`) or a hyperspace jump starts (`HyperjumpStarted`), so the
  launch/warp animation is visible (`GameMenu`).
- **In-game admin console** — admin cheats are now typed in chat (`/give /tp /tpp /settime /setweather
  /fly /god /instant /ai`, `/help` lists them). The client parses them → `AdminCommandIntent`; the server
  still gates on `IsAdmin` + `CheatsAllowed`. `/bump` stays a chat message.

### Multi-world + system-scale flight — planned (not started) ⭐
**Firm requirement:** in multiplayer, players can be on **different planets / different star systems**
simultaneously, plus **fly between planets in a system and land on any of them**. This makes the
multi-world core (per-player worlds + per-player ship) mandatory, with a system-scale flight layer on top.
Full phased design (P1 body positions → **P2 WorldManager indirection, the keystone** → P3 multi-world +
per-player location → P4 per-player ship → P5 system flight + land-anywhere → P6 inter-system → P7
cross-world MP polish) in **[docs/MULTIWORLD_AND_SYSTEM_FLIGHT_PLAN.md](docs/MULTIWORLD_AND_SYSTEM_FLIGHT_PLAN.md)**.
Key enabler found: persistence is already **location-scoped** (the save DB can hold many worlds; only the
in-memory single `_world` blocks it). **Decision: one ship per player, no crew.**

**Progress:**
- ✅ **P1** — seeded system-space coordinates on every body (`CelestialBody`/`NetBody`/`UniverseGenerator`),
  deterministic, existing universes unchanged (`0e4162c`).
- ✅ **P2** — `WorldManager`/`LoadedWorld` seam; the active world is routed through it, behaviour-preserving
  (`f45bd41`).
- ✅ **P3a** — relocated the per-world runtime state (fauna/enemies/npcs/flora/fluids/containers/
  structures/landing zones) into `LoadedWorld` via forwarding properties to `_worlds.Active` (`e4e251a`).
- ✅ **P3b** — relocated the remaining per-world stragglers (settlement/wreck stamp scalars, creature/
  enemy/npc/fluid sim timers) into `LoadedWorld`. Weather + time-of-day stay global for now (all resident
  worlds share the sky — a known temporary limitation, refined in P7). Behaviour-preserving (`be5d48e`).
  **Every per-world gameplay system now has isolated state** — the foundation is complete.
- ✅ **P3c-1** — multi-world cache scaffolding: `WorldManager.GetOrCreate/Loaded/IsLoaded/Unload` + settable
  Active cursor; per-session `CurrentLocationId` (set on join + travel). Behaviour-preserving (`c0474ae`).
- ✅ **P3c-2a/b** — relocated weather/environment state per-world; per-world init reads the Active world,
  not global `_meta` (`bf8be4c`, `b450491`).
- ✅ **P3c-2c** — restructured the central `Tick` to iterate occupied worlds with the Active cursor; added
  `JoinedInActiveWorld`/`BroadcastToWorld`/`OccupiedLocations`/`SetActiveWorld`; scoped chunk streaming +
  presence + entity + block-change broadcasts per world; `OnPayload` sets Active to the sender's world
  (`e283a88`).
- ✅ **P3c-2d** — **per-player travel** (`HandleTravel` moves only the requester via cached `LoadWorld`,
  per-player `WorldReset`, unload-on-empty); join/disconnect world-scoped; test
  `TwoPlayers_OnDifferentPlanets_HaveIsolatedWorlds` (`3849ccb`).

**✅ P3 DONE — two players can now be on different planets / systems at once with isolated terrain, edits,
fauna and weather (261 tests).**

**✅ P7 DONE — cross-world MP polish (263 tests).** Four parts:
- **Per-player ship-stamp** (`a*`) — two players on one planet get **separate ships at distinct start
  points** (ship structure is per-player in each world; `StampShip` anchors at the served player's own
  landing zone; protection/interior cover everyone's ships). Test
  `TwoPlayers_GetSeparateShips_AtDistinctStartPoints`.
- **Position-based day/night** (`d1debb0`) — world X is a longitude: `GameBootstrap.LocalTimeOfDay` shifts
  the global day fraction by `playerX / 6000`, so one player can be on the **day side** while another is on
  the **night side** of the same planet (sky/clouds/HUD clock use local time).
- **Per-biome weather + larger biomes** (`a6a88dd`) — weather is per **biome** (a stormy biome rains while a
  clear one stays sunny), shifted by a persistent per-biome offset; the env broadcast is per-player. Biome
  noise scale 140 → 360 so each biome is a large region.
- **Star map shows the party** (`afd4ad9`) — `StarMapData.Players` lists who is on each body ("◈ Alice, Bob").

**🎉 The whole multi-world + system-flight plan (P1–P7) is complete.**

**✅ P6 DONE — inter-system travel via hyperspace jump.** Jumping between systems is the existing
`TravelIntent` + `jump_generator` from the star map (Tab → Map), reachable mid-flight. Fixed the rough
edge: jumping *from* flight no longer plays the old planet's landing descent under the warp — `SpaceView`
tears down on `HyperjumpStarted` and the full-screen warp covers the transition; the ship holds position
while the map is open. So you **fly within a system** and **jump between systems** (`ec59a31`).

**✅ P5 DONE (262 tests) — system-scale flight + land anywhere in the system.** In space you now fly
between the system's planets/moons (rendered at their P1 system coordinates, relative to the body you
launched from; the flight clamp spans the system). The nearest body in approach range is the land target —
the HUD prompts "Press L to land on <name>" and the confirm names it; `LeaveSpaceIntent.DestinationBodyId`
makes the server land you there (per-player travel; same-system = free). With nothing in range, L returns
you to where you launched. **Inter-system travel stays the hyperspace jump** (star map + `jump_generator`),
per the requirement. (`8fdfcdc` server, `c582afb` client.)

**✅ P4 DONE (merged to `main`, 261 tests) — one ship per player, no crew.** Each player owns their own
**fleet (multiple ships) with exactly one active ship**, created/loaded on join, stamped into their world,
persisted per player. Implemented with a single-threaded **ship cursor** (`_current`): `_ship`/`_ships`/
`_activeShipId` resolve to the served player; `OnPayload` + the public entry methods (`HandleTravel` top,
`CraftShip`, `Craft`, `RequestDock`) `Serve(session)` first; combat-stat caches recompute on cursor set.
Persistence is per player (`ship_<playerId>`). Built on branch `p4-per-player-ship-wip` then merged.
**Remaining edge (→ P7):** two players on the *same* planet share that world's ship-stamp state (anchor/
heal-tank); fine for different-planet play (the requirement), needs per-player ship-stamp for shared
worlds.

**Original P4 plan:** the fleet (`_ships`/`_activeShipId` in `GameServerShips`) is
currently global; make it per-player via a **session cursor** (`_current`) so `_ship`/`_ships`/
`_activeShipId` resolve to the served player's ship (mirrors the world Active cursor; single-threaded).
Sub-steps: **P4a** add per-player fleet to `PlayerSession` + the `_current` cursor + route `_ship`/`_ships`/
`_activeShipId` through it (recompute the combat-stat caches `_shipHullMax/Shield/Regen/Radar` on cursor
set); **P4b** move the ship lifecycle from Start (one shared ship) to per-join (each player loads/creates
their own ship; persistence keyed by player id); **P4c** set `_current` in `OnPayload` + before per-player
StampShip in join/travel + per-player in the space-combat tick; **P4d** untangle the test/public accessors
(`server.Ship`/`OwnedShips` → first joined player) + a two-player fleet-isolation test. Ship-stamp state
stays per-world (fine while each occupied world has one player; shared-world multi-ship is P7).

_(Done 2026-06-06 — see the "Starter weapons + ship weapons finished (W1–W5)" entry above.)_

### Orbital bodies in the planet sky — planned (not started) ⭐ (2026-06-06)
On a planet's surface, the **other nearby bodies of the system** (neighbouring planets/moons) and the
**space stations orbiting this planet** should be visible as **small objects in the sky** — like a moon you
can see by day + night. Cosmetic, client-side (mirrors `Sky`/`Starfield`/the sun disc):
- A `SkyBodies` component that, only while on a planet surface (not in space / not boarded), places small
  **lit sphere billboards** for the system's neighbours and small **station icons** for stations orbiting
  the current body, in the sky dome — direction derived from the star map's relative system coords
  (`Game.StarMap`, already client-side), distance scaled to the far plane, following the camera like the
  starfield. Tint/size from each body's planet type; stations read as tiny metallic specks/cross shapes.
- Visible day + night (a touch brighter at night); a very slow drift so they feel like they orbit.
- Phasing: **O1** render neighbours + stations from the star map; **O2** slow orbital drift; **O3** per-type
  look + stations-of-this-body shown nearer/bigger; **O4** optional labels on look/scan.

### Doors — ✅ shipped for settlements (see the Done entry above); stations/ship still open
**Settlement doors are done** (D1–D5 — slide for towns/cities, hinge for villages/hamlets, with the three
ElevenLabs SFX). **Still open:** auto-doors for **orbital stations + the ship** — their doorways are cut as
air but don't yet emit `door_slide` markers; the `GameServerDoors` registry already accepts them once the
station/ship stamp adds the markers (plus station/ship editor palette entries). Original plan kept below for
that remaining work:
- **Sci-fi sliding doors** — auto open/close: the server opens them when a player is within range and
  auto-closes them after a short delay. For **stations + cities/towns** (and the **ship**).
- **Hinged "normal" doors** — manual: press **E** to toggle. For **villages/hamlets**.

Doors are **markers**, not voxel blocks (a 2×3 doorway opening stays air; the door entity fills it; its
collider closes the gap when shut). Phased plan:
- **D1 — server `GameServerDoors`:** a per-world `ServerDoor { Id, Type(slide/hinge), Pos, Facing, Open,
  AutoCloseTimer }` registry built from structure markers on stamp; `TickDoors` auto-opens slide doors near
  players + auto-closes after a delay; `HandleDoorInteract` toggles a hinge door the player faces;
  broadcast `DoorList`/`DoorStateChanged` per world (via `BroadcastToWorld`). Cleared in
  `ResetWorldRuntimeState`.
- **D2 — client `DoorView`:** renders each door from `DoorList`; slide = two panels that swoosh apart,
  hinge = a leaf that swings ~90°; a `BoxCollider` enabled while ~closed, disabled while open; sci-fi
  swoosh vs. wood creak SFX (`ClientAudio`). Mirrors `NpcView`.
- **D3 — generators place doors at doorways:** `StationGenerator.CutDoor` → a slide-door marker; settlement
  generator picks **slide** for city/town, **hinge** for village/hamlet; `StationGenerator`/ship hull
  doorways → slide. Keep the opening air so the entity shows through.
- **D4 — editors:** add door markers to the palettes — **station editor** (slide), **settlement editor**
  (both slide + hinge so the designer picks; generator still auto-picks by tier), **ship editor** (slide).
  Markers flow through `StructureTemplate` cells (kind="marker", id="door_slide"/"door_hinge") → D1 registry.
- **D5 — localization + tests:** en/de names; tests: a slide door opens when a player steps within range +
  auto-closes; a hinge door toggles on interact; the collider blocks passage while closed.

### Planned — requested 2026-06-06 (für später)
- **Mineable water/lava (with the mining beam) + source logic** — water/lava should be **harvestable**, but
  only with the **mining beam** (not the drill); and once the **source block / last cells** are removed it
  must **stop reflowing** (don't infinitely regenerate). Ties into the fluid sim (`FluidLevel`/`ActiveFluid`)
  and tool gating (`ToolCanMine`). Requested 2026-06-06.
- **Multiplayer player-name reservation** — a player name must be **reserved on the server** so two clients
  can't collide on the same name/identity. (Today join takes any name.) Requested 2026-06-06.
- **Creature movement: terrain/water-following + swim/dive** — creature roam keeps the **spawn Y fixed**
  (`CreatureBehaviour.Step` moves X/Z only), so as they wander they don't follow the ground (land creatures
  float/clip), fliers don't hover above terrain, and swimmers don't track the water surface. Plan: recompute
  Y per step by habitat (land/lava → surface+1; air → surface+altitude; water → stay submerged at the sea
  level), add a proper **swim undulation** (body on a pivot, not just tail-sway) and an optional **dive**
  behaviour for aquatic species. Requested 2026-06-06. (Fliers already flap; water hides + per-species
  voices shipped.)
- **Underwater sound filter** — when the player is submerged, run audio through a **low-pass / muffled
  filter** so it sounds underwater (`ClientAudio` + an AudioListener low-pass; detect the player is in a
  water block). Requested 2026-06-06.
- **Aquatic fauna + water flora** (the water/lava *blocks* shipped; this is the life part) — add **water
  creatures** (species that live in/under water — spawn in the new water bodies, swim) and **water flora**
  (reeds / kelp / lilies in or at the edge of water). Builds on the seas now generated by the world block.
  Requested 2026-06-06.
- **Flora re-tint per species × biome × planet** — flora should get a **random final colour applied on top
  of the texture** (texture-independent), **uniform per flora species within a biome/planet** (not per
  individual). Creatures already do this (grayscale hides × species colour); flora can't yet because the
  opaque block atlas shader has **no free albedo-tint channel** (vertex colour = gloss/metal/AO; UV1 =
  skylight). Plan: (1) regenerate the flora tiles **grayscale** (OpenAI) so they tint cleanly; (2) add a
  **tint UV channel** (e.g. TEXCOORD2 RGB) in `ChunkMesher` — white for normal blocks, a per-(species,
  planet[, biome]) colour for flora; (3) the `BlockAtlas` shader multiplies albedo by it. Needs the planet
  seed (+ a client-side biome-index for multi-biome worlds) plumbed to the mesher. Requested 2026-06-06.
- **Faces / eyes** — the player avatar and NPCs have **no face**, and creatures have **no eyes**. Give the
  avatar + NPCs a simple face (eyes), and give creatures **optional, procedurally-random eyes** (some species
  none, others 2+ — random count/placement) that are actually visible. Requested 2026-06-06.
- **Creature death drops + rare material substitute** — analyse how far creature loot is implemented: dying
  creatures should drop **edible meat** (or **toxic** flesh the player must detox, for toxic species), and
  **rarely** a drop should substitute for a random crafting material. (`CombatEntity.Loot` exists +
  `creature_meat`/`toxic_gland` items — verify it fires + add the rare-material roll.) Requested 2026-06-06.
- **More procedural creature variety** — analyse how fauna/flora species are organised + generated
  (`CreatureGenerator`, `CreatureSpecies`, the species roster per planet) and add **more procedural
  randomness so creatures look visibly different** (body proportions, limbs, colours/patterns, size). Today
  species are fairly templated; want wider visual variance per species/instance. Requested 2026-06-06.
- **Ship is always oxygenated** — being inside the ship should keep the air breathable and **steadily
  refill the player's suit-oxygen reserve** (like a breathable atmosphere / a heal-tank for O2). Analyse the
  vitals tick (`GameServer` oxygen drain/regen around the heal-tank + `AboardShip`) and ensure aboard-ship =
  no drain + active refill. Requested 2026-06-06.

### Not started / larger future work
- **World wrap (walk around the planet)** — ✅ **W0–W4 shipped**: X is a wrapping longitude (cylinder
  world), so you can walk east and arrive back at the start with a **seam-free** edge (terrain/biomes/caves/
  ore/structures continuous across X = 0 ≡ X = 6000). Seam-free generation via circular-domain noise; server
  + client + persistence + interaction all route X through one wrap helper. **Remaining: W5 (poles)** — bound
  latitude (Z) with an ice-wall/barrier biome. Full plan + progress in
  [docs/WORLD_WRAP_PLAN.md](docs/WORLD_WRAP_PLAN.md).
- **Advanced graphics roadmap** — Built-in RP vs URP decision, god rays, reflection probes, LUT grade.
  Full research in [docs/ADVANCED_GRAPHICS_PLAN.md](docs/ADVANCED_GRAPHICS_PLAN.md).
- **Texture audit** — review/expand item & icon art and creature/NPC texture variety.
- **uGUI theme polish** — remaining icon/symbol pass on the sci-fi theme.
- **Deferred by design** (see [docs/SPACE_COMBAT_CONCEPT.md](docs/SPACE_COMBAT_CONCEPT.md)): PvP ship
  combat, large cruisers/bosses. (Per-player ships shipped in P4.)

---

## Reference docs (committed, under docs/)
Concept/design detail for the larger systems: `STATION_AS_LOCATION_PLAN`, `WORLD_WRAP_PLAN`, `MULTIWORLD_AND_SYSTEM_FLIGHT_PLAN`, `SPACE_COMBAT_CONCEPT`,
`CLIENT_COMPLETION_PLAN`, `CRAFTING_TECH_SHIP_UI_PLAN`, `STATION_SETTLEMENT_EDITOR_PLAN`,
`SHIP_TYPE_EDITOR_PLAN`, `ADVANCED_GRAPHICS_PLAN`, `SOUND_DESIGN`, `SELF_HOSTING`, `AI_MISSION_BACKEND`,
`CLIENT_SHELL_AND_ASSETS`.
