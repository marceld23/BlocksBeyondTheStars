# Developer documentation — index

Engineering and design documentation for **Blocks Beyond the Stars**. Player-facing operation lives in
[../user/USER_MANUAL.md](../user/USER_MANUAL.md); the live Done/Open status is [../../TODO.md](../../TODO.md);
the hard contributor rules are in [../../AGENTS.md](../../AGENTS.md); the project strategy
(vision · mission · roadmap) is in [../strategy/](../strategy/vision.md).

These are *how it works* / *why it is built this way* references — not pre-implementation checklists (status
belongs in TODO.md). Each doc states its own status near the top. Last reorganised 2026-06-19; index
refreshed 2026-10-23.

## Start here

- [ARCHITECTURE.md](ARCHITECTURE.md) — the system overview: client (presentation) vs server (truth), the
  solution & project graph, runtime, networking, persistence, and a where-to-find-what map. **Read this first.**
- [DEVELOPER.md](DEVELOPER.md) — build, test and build-verification guide (the `build-client.ps1`/`build-client.sh`
  pipeline, freshness checks, "works in the Editor, broken in the build" pitfalls), plus the recipes for adding a
  block, adding a server option, extracting a GameServer subsystem and adding an in-world panel.
- [LINUX_PORT.md](LINUX_PORT.md) — how the Linux port works: cross-platform changes, the console launcher,
  bash build scripts, CI/CD and known limitations.
- [MACOS_BUILD.md](MACOS_BUILD.md) — the experimental macOS build: `StandaloneOSX` cross-built on Linux (Mono),
  the CI job pair, the artifact execute-bit fix, and running an unsigned/un-notarized `.app`.
- [CLIENT_TESTING.md](CLIENT_TESTING.md) — how the Unity client is tested against the **real** game server
  (the `Client.Core` split, the three test tiers, the selectable `run-tests.ps1` runner).
- [SERVER_TESTING.md](SERVER_TESTING.md) — **writing tests** for the server/shared .NET suite: exemplar
  tests to copy from, the content/locale fixtures and shared test fakes, what "meaningful" assertions look like,
  and the CI traps (analyzers, the code-health ratchet). Start here for a first test contribution (open tasks
  carry the `good first issue` label).
- [SELF_HOSTING.md](SELF_HOSTING.md) — run and host a dedicated server, config keys, the web portal & updates.
- [HOSTED_WORLDS.md](HOSTED_WORLDS.md) — the hosted-worlds ("Official Worlds") service: control plane
  (WorldHost), one container per world, routing, portal and operations.
- [TRANSLATION_GUIDE.md](TRANSLATION_GUIDE.md) — contributing a translation: locale files, coverage
  tooling, the 45 % settings-picker gate, and the machine-first-pass workflow.

## World & worldgen

- [WORLD_GENERATION.md](WORLD_GENERATION.md) — the full procedural pipeline: seed → galaxy → system →
  body → PlanetType → terrain/biomes/water/flora/fauna, and how variety and determinism are achieved.
- [MULTIWORLD_AND_SYSTEM_FLIGHT.md](MULTIWORLD_AND_SYSTEM_FLIGHT.md) — multiple resident worlds, per-player
  location, system-scale flight and hyperjumps.
- [STATION_AS_LOCATION.md](STATION_AS_LOCATION.md) — ships and space stations as first-class "void" worlds
  entered via real world transitions.
- [WORLD_WRAP.md](WORLD_WRAP.md) — torus world topology (both axes wrap; periodic noise).
- [FLUID_ROUTING.md](FLUID_ROUTING.md) — rivers, waterfalls and lava: one fluid-agnostic routing
  network (downhill-into-sinks, animated surfaces, lavafalls + embers).

## Client & rendering

- [CLIENT_COMPLETION.md](CLIENT_COMPLETION.md) — how the client is wired (AppShell/WorldRig, mesher/atlas,
  hosting model, SpaceView, presence, audio).
- [CLIENT_SHELL_AND_ASSETS.md](CLIENT_SHELL_AND_ASSETS.md) — branding, splash, menu, settings and asset strategy.
- [INPUT_AND_CONTROLLER.md](INPUT_AND_CONTROLLER.md) — the input abstraction (`IInputSource`): keyboard/mouse,
  gamepad (bindings + glyphs), on-screen touch controls and WebGL text entry.
- [URP_MIGRATION.md](URP_MIGRATION.md) — the URP rendering setup (custom unlit voxel shaders, post, shadows).
- [ADVANCED_GRAPHICS.md](ADVANCED_GRAPHICS.md) — graphics: what shipped + the remaining roadmap.
- [UI_AND_RENDER_CONCEPT.md](UI_AND_RENDER_CONCEPT.md) — UI + render concept, done vs. open.
- [UI_PANELS.md](UI_PANELS.md) — the UiPanel base for modal in-world panels + the ClientContext panel registry, and
  the RigViews registry for the rig's other views.
- [PROFESSIONAL_LOOK_IMPLEMENTATION.md](PROFESSIONAL_LOOK_IMPLEMENTATION.md) — the professional-look pass (WP-1…16).
- [PROFESSIONAL_LOOK_GAP_ANALYSIS.md](PROFESSIONAL_LOOK_GAP_ANALYSIS.md) — gap analysis (closed vs. still open).
- [ART_BIBLE.md](ART_BIBLE.md) — **normative** visual style reference (palette, materials, room identity).
- [VFX.md](VFX.md) — the effect system (FxKit, FX lights, scan wave, data-driven `fx` looks) and the catalogue of
  weapon, mining, scanner, gadget, defeat and flight effects; budgets, accessibility, multiplayer relay (#2159).
- [PLANET_VIEW_AND_WEATHER.md](PLANET_VIEW_AND_WEATHER.md) — planets from orbit as the worlds you land on: the planet
  map bake (off the main thread), the atmosphere from data, server ambient weather + the `SystemWeather` snapshot, the
  shared per-position weather formula, cloud shells, landing/M-map weather layers and the time-of-day spin (#2179).

## Gameplay systems

- [SPACE_COMBAT_CONCEPT.md](SPACE_COMBAT_CONCEPT.md) — space-combat MVP concept + what has since landed.
- [CRAFTING_TECH_SHIP_UI.md](CRAFTING_TECH_SHIP_UI.md) — the crafting / tech / ship management screen.
- [SHIP_REPAIR.md](SHIP_REPAIR.md) — own-ship repair (hull + EVA-carved cells).
- [BASE_LIFE_SUPPORT.md](BASE_LIFE_SUPPORT.md) — base and station air: the core zone and the shared air engine (one rule
  for bases and stations, every door a bulkhead, no size limit, vertical spans, time slicing, the emergency brake,
  unknown is not a leak, the leak named at its opening).
- [CREATOR_SUITE.md](CREATOR_SUITE.md) — what players make themselves: the texture editor and the three-layer
  texture source, world textures, animated tiles, the main-menu form editor and forms over several blocks,
  tool models as data and player tool looks, texture submissions (#1950).
- [CUSTOM_SHAPES.md](CUSTOM_SHAPES.md) — player-designed block forms: the micro-voxel format, the per-save
  form registry, the box budget, and how forms are shared; also the texture slots of built-in forms (#1900) and of
  plain cubes — face tiles in the atlas's extras band and the stored front of a block that faces you (#2124).
- [HELD_ITEMS.md](HELD_ITEMS.md) — the item in your hand: what each kind is built from, a block's own look (dye,
  form, design, #2776), the first-person suit hand and its grips (#2777), carrying in third person, and the holster /
  draw of a quick-bar switch (#2778).
- [CLIMBING.md](CLIMBING.md) — wall climbing (#2195): where the client-side climb sits in the movement chain, the
  pure `ClimbProbe` / `ClimbGrip` rules, block `climb` data and climbing gear, the pull-up, and the pose on the wire.
- [CREATURE_RIG.md](CREATURE_RIG.md) — how a blocky animal is built and animated: the gait, jointed
  limbs, fins, foot planting on real blocks, and the distance LOD tiers.
- [ENTITY_MOTION.md](ENTITY_MOTION.md) — how animals, NPCs, machines and bandits move smoothly on screen (#2664–#2670):
  the per-tick server velocity, the 7.5 Hz creature motion stream, the server-clock sync, buffered Hermite
  interpolation, the animators' speed filter and the server rules that keep paths free of kinks.
- [CREATURE_TAMING.md](CREATURE_TAMING.md) — taming wild creatures into companions.
- [BIO_LAB.md](BIO_LAB.md) — the bio lab (#2212): species seeds and samples, derived substance and material
  profiles, the lab (analyse, mix, change tools and gear), status effects, cloning from samples, crosses and bred
  plants; the register and research blobs, the wire, limits, and how to add an effect or a lab material.
- [SHIP_SCANNER.md](SHIP_SCANNER.md) — the ship scanner (#2243): every ship's hold-to-scan slot in the flight
  hotbar, server-checked readouts for every space object, the planet overview card, the three data-driven tiers
  (cockpit · Deep · Quantum) with module replacement, and the life-pod and anomaly models.
- [WORMHOLES.md](WORMHOLES.md) — wormholes (#2242): seed-pure pair placement beyond the outer orbit, the story-system
  lock, the world option, the transit gates and pose, the scanner reading, and the rift shader and transit effect.
- [DEBRIS_FIELDS.md](DEBRIS_FIELDS.md) — debris fields (#2351): seed-pure placement after the generator, the field in
  flight (marker, fragments, capsules), the once-per-galaxy salvage ledger shared with the space wreck, the shield taps,
  combat debris, raiders with real hulls, and the `data/space_salvage.json` data file. The hostiles' stats (drones, UFOs,
  cruisers, raiders, Guardian machines, robbers) are `data/enemies.json` (#2971) — see SPACE_COMBAT_CONCEPT.md.
- [DERELICT_STATIONS.md](DERELICT_STATIONS.md) — derelict stations (#2632–#2636): one broken old station guaranteed in
  every start system, rarely 1–3 elsewhere; the decay pass (sealed and breached rooms, doors, broken trading posts),
  vacuum, mining with the hull seen from space following, seed + deltas persistence, the old supply crates with their
  seeded logs, the guardian bay, and falling out into a real spacewalk.
- [NPC_TRADER_SHIPS.md](NPC_TRADER_SHIPS.md) — peaceful ambient NPC trader traffic.
- [NPC_ROUTINES.md](NPC_ROUTINES.md) — living NPCs: base residents, the daily routine, pathfinding with doors,
  jobs with yield, and the station night.
- [WILDERNESS_FOLK.md](WILDERNESS_FOLK.md) — people outside the settlements (epic #2639, generation 25): lodges (a hut,
  its residents, the store crate), the shared work cycle and its four cores (gathering, mining with the refill registry,
  woodcutting with replanting, hunting — and animals that notice hunters), wanderers, and the client side.
- [VILLAGE_LIFE.md](VILLAGE_LIFE.md) — "the village lives" (epic #2589): the shared rules (data, AI two-mode lines,
  no sound jumble, no FOMO), the village festival (#2604) and the villagers' personal wishes (#2601).
- [MATTER_CONVERTER.md](MATTER_CONVERTER.md) — the Transmuter station: craft scarce ore from spare
  terrain (lossy `matter_dust` intermediate, no Tier-3 output).
- [CRYSTAL_NET.md](CRYSTAL_NET.md) — the Crystal Net (#2045): crystal conduits carrying a visible ON/OFF
  signal between switches, sensors, gates, lamps, doors, sound devices and machines (matter link, auto-drill,
  fabricator, caller, clone tank); the network index, the beats, caps, persistence, wire, and how to add a device.
  §17: Crystal Net 2 (#2251) — new ports and devices, six directions, deltas, pre-built world circuits, the net
  aboard the own ship, catch-up on return, hints, missions and achievements.
- [MOVING_BLOCKS.md](MOVING_BLOCKS.md) — phase blocks, trapdoors, bridge motors, pistons and lifts (#2264–#2266):
  twin swaps, step-wise block moves, the server-owned lift platform the client draws and rides, the kid rules.
- [MONORAIL.md](MONORAIL.md) — the monorail hover train (#2113): pylons and energy lines, authored wagons on the
  spline, riders walking inside the moving frame, the intercity line and the abandoned stations.
- [FACTORIES_RUINS_AND_CLAIMING.md](FACTORIES_RUINS_AND_CLAIMING.md) — factories with roster-limited
  production terminals, ruins/treasure chests, and access-code claiming.
- [VOICE_CHAT.md](VOICE_CHAT.md) — push-to-talk voice chat + the tiered radio reach (planet/system/galaxy).

## Story & content

- [STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md) — the story engine/mechanics (beats, triggers, finale), the game
  events and conditions, and the per-player runtime of the player story packs.
- [TUTORIAL_PACK.md](TUTORIAL_PACK.md) — how a player story pack (the tutorial, side stories) is written: manifest,
  chapter and step reference, key tokens, conditions, per-player state, the tutorial world rule, validation — and the
  shipped basic training: its content, teaching verbs and UI flags, gates, the hint hold, the migration of old
  onboarding state, older clients, how to add a chapter.
- [CUTSCENES.md](CUTSCENES.md) — data-driven cutscenes: the `cutscenes.json` format (tiers, shots, cameras, captions,
  options), the pack bindings, validation, the wire messages, the per-player seen ledger, pacing and SP/MP rules.
- [STORY_PACK_AUTHORING.md](STORY_PACK_AUTHORING.md) — **writing a story pack** (a side story, a tutorial chapter) as
  data only: folder layout, kinds and scopes, the manifest, several storylines at once, conditions, events, cutscenes,
  texts and languages, the WebGL list, validation, what still needs C#, the limits of v1 — with the pilot side story
  `side_pilot` (a test pack) as the worked example.
- [STORY_VOCABULARY.md](STORY_VOCABULARY.md) — **generated** reference tables of everything a pack may name (events,
  condition forms, world predicates, camera types, anchors, caption styles, budgets …), rendered from the code constants
  and their doc comments; `StoryVocabularyDocTests` fails when it drifts.
- [STORY_VEGA_PROTOCOL_CONCEPT.md](STORY_VEGA_PROTOCOL_CONCEPT.md) — the story's design rationale.
- [LORE_STRUCTURE.md](LORE_STRUCTURE.md) — the lore bible + content schema.
- [MUSIC_TRACKS.md](MUSIC_TRACKS.md) — music track library reference.
- [SOUND_DESIGN.md](SOUND_DESIGN.md) — sound architecture + catalogue.
- [MINIGAMES_AND_WIKI.md](MINIGAMES_AND_WIKI.md) — in-game Wiki + data-cube Arcade via the embedded browser.

## Tooling & editors

- [SHIP_TYPE_EDITOR.md](SHIP_TYPE_EDITOR.md) — the in-game ship-type editor.
- [STATION_SETTLEMENT_EDITOR.md](STATION_SETTLEMENT_EDITOR.md) — the station/settlement structure editor.
- [EDITORS_WORLDGEN_AND_DEV_LABELLING_ANALYSIS.md](EDITORS_WORLDGEN_AND_DEV_LABELLING_ANALYSIS.md) —
  editor output → worldgen integration + dev/test labelling.
- [PLAYER_FEEDBACK.md](PLAYER_FEEDBACK.md) — the in-game F1/F2 feedback + crash-report pipeline.
- [REPORT_HOST.md](REPORT_HOST.md) — ReportHost, the bug-report inbox the feedback pipeline uploads to.

## Forward-looking

- [AI_MISSION_BACKEND.md](AI_MISSION_BACKEND.md) — the optional, offline-safe LLM service (design + contract).
- [WEBCLIENT_FEASIBILITY.md](WEBCLIENT_FEASIBILITY.md) — browser/WebGL client feasibility and the hosted
  Lite lane (local WebSocket/PostgreSQL play works; production hardening/deployment remains).
- [AGE_RATING_CHECKLIST.md](AGE_RATING_CHECKLIST.md) — age-rating readiness (IARC / PEGI / USK / ESRB): the
  groundwork for the first storefront questionnaire (#1226).

## Architecture Decision Records

Numbered, append-only decisions in [adr/](adr/). New significant decisions get the next number; superseded
ones are marked, not deleted.

- [0001 — Authoritative .NET server, Unity client](adr/0001-authoritative-dotnet-server.md)
- [0002 — Build everything in code, no Unity scene authoring](adr/0002-build-everything-in-code.md)
- [0003 — URP with custom unlit shaders + baked flood-fill block light](adr/0003-urp-custom-unlit-shaders-baked-lighting.md)
- [0004 — Ships and stations as void worlds](adr/0004-ship-and-stations-as-void-worlds.md)
- [0005 — Bundled single-player runs the server as a child process](adr/0005-bundled-singleplayer-child-process-server.md)
- [0006 — Torus world topology](adr/0006-torus-world-topology.md)
- [0007 — Multi-world resident instancing](adr/0007-multi-world-resident-instancing.md)
- [0008 — Optional, offline-safe LLM backend](adr/0008-optional-offline-safe-llm-backend.md)
- [0009 — Embedded browser for the in-game Wiki + Arcade](adr/0009-embedded-browser-wiki-arcade.md) — *superseded (now native UI)*
- [0010 — Velopack distribution + self-host portal](adr/0010-velopack-distribution-and-self-host-portal.md)
- [0011 — CodeQL code scanning strategy](adr/0011-codeql-security-scanning-strategy.md)
- [0012 — CalVer date-based versioning](adr/0012-calver-date-based-versioning.md)
- [0013 — The Crystal Net: a binary, visible signal network without power](adr/0013-crystal-net-binary-visible-signal-network.md)
- [0014 — Moving blocks: twin swaps, step-wise bridges and pistons, server-owned lift platforms](adr/0014-moving-blocks-twins-and-server-owned-lifts.md)
- [0015 — Unity 6.6, the Supported-release cadence, and WebGPU first in the browser](adr/0015-unity-6-6-supported-releases-and-webgpu-first.md)
- [0016 — Lava, crystals and the glowing flora light their surroundings (quietly)](adr/0016-natural-emitters-light-their-surroundings.md)
- [0017 — Endless water by class, finite still water, and falls that keep their level](adr/0017-water-classes-finite-still-water-and-bounded-falls.md)
- [0018 — Every closed room breathes: one air engine for bases and stations, doors are bulkheads](adr/0018-every-closed-room-breathes-one-air-engine.md)
- [0019 — GameServer subsystems: top-level classes behind compiler-derived host interfaces](adr/0019-gameserver-subsystems.md)
