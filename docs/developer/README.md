# Developer documentation — index

Engineering and design documentation for **Blocks Beyond the Stars**. Player-facing operation lives in
[../user/USER_MANUAL.md](../user/USER_MANUAL.md); the live Done/Open status is [../../TODO.md](../../TODO.md);
the hard contributor rules are in [../../AGENTS.md](../../AGENTS.md).

These are *how it works* / *why it is built this way* references — not pre-implementation checklists (status
belongs in TODO.md). Each doc states its own status near the top. Last reorganised 2026-06-19.

## Start here

- [ARCHITECTURE.md](ARCHITECTURE.md) — the system overview: client (presentation) vs server (truth), the
  solution & project graph, runtime, networking, persistence, and a where-to-find-what map. **Read this first.**
- [DEVELOPER.md](DEVELOPER.md) — build, test and build-verification guide (the `build-client.ps1` pipeline,
  freshness checks, "works in the Editor, broken in the build" pitfalls).
- [CLIENT_TESTING.md](CLIENT_TESTING.md) — how the Unity client is tested against the **real** game server
  (the `Client.Core` split, the three test tiers, the selectable `run-tests.ps1` runner).
- [SELF_HOSTING.md](SELF_HOSTING.md) — run and host a dedicated server, config keys, the web portal & updates.

## World & worldgen

- [MULTIWORLD_AND_SYSTEM_FLIGHT.md](MULTIWORLD_AND_SYSTEM_FLIGHT.md) — multiple resident worlds, per-player
  location, system-scale flight and hyperjumps.
- [STATION_AS_LOCATION.md](STATION_AS_LOCATION.md) — ships and space stations as first-class "void" worlds
  entered via real world transitions.
- [WORLD_WRAP.md](WORLD_WRAP.md) — torus world topology (both axes wrap; periodic noise).

## Client & rendering

- [CLIENT_COMPLETION.md](CLIENT_COMPLETION.md) — how the client is wired (AppShell/WorldRig, mesher/atlas,
  hosting model, SpaceView, presence, audio).
- [CLIENT_SHELL_AND_ASSETS.md](CLIENT_SHELL_AND_ASSETS.md) — branding, splash, menu, settings and asset strategy.
- [URP_MIGRATION.md](URP_MIGRATION.md) — the URP rendering setup (custom unlit voxel shaders, post, shadows).
- [ADVANCED_GRAPHICS.md](ADVANCED_GRAPHICS.md) — graphics: what shipped + the remaining roadmap.
- [UI_AND_RENDER_CONCEPT.md](UI_AND_RENDER_CONCEPT.md) — UI + render concept, done vs. open.
- [PROFESSIONAL_LOOK_IMPLEMENTATION.md](PROFESSIONAL_LOOK_IMPLEMENTATION.md) — the professional-look pass (WP-1…16).
- [PROFESSIONAL_LOOK_GAP_ANALYSIS.md](PROFESSIONAL_LOOK_GAP_ANALYSIS.md) — gap analysis (closed vs. still open).
- [ART_BIBLE.md](ART_BIBLE.md) — **normative** visual style reference (palette, materials, room identity).

## Gameplay systems

- [SPACE_COMBAT_CONCEPT.md](SPACE_COMBAT_CONCEPT.md) — space-combat MVP concept + what has since landed.
- [CRAFTING_TECH_SHIP_UI.md](CRAFTING_TECH_SHIP_UI.md) — the crafting / tech / ship management screen.
- [SHIP_REPAIR.md](SHIP_REPAIR.md) — own-ship repair (hull + EVA-carved cells).
- [CREATURE_TAMING.md](CREATURE_TAMING.md) — taming wild creatures into companions.
- [NPC_TRADER_SHIPS.md](NPC_TRADER_SHIPS.md) — peaceful ambient NPC trader traffic.

## Story & content

- [STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md) — the story engine/mechanics (beats, triggers, finale).
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

## Forward-looking

- [AI_MISSION_BACKEND.md](AI_MISSION_BACKEND.md) — the optional, offline-safe LLM service (design + contract).
- [WEBCLIENT_FEASIBILITY.md](WEBCLIENT_FEASIBILITY.md) — browser/WebGL client feasibility (decided, not built).

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
- [0009 — Embedded browser for the in-game Wiki + Arcade](adr/0009-embedded-browser-wiki-arcade.md)
- [0010 — Velopack distribution + self-host portal](adr/0010-velopack-distribution-and-self-host-portal.md)
- [0011 — CodeQL code scanning strategy](adr/0011-codeql-security-scanning-strategy.md)
