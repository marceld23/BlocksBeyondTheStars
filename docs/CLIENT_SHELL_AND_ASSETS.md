# Client Shell, Assets & UX — Decisions

Decision record for the client front-end (`anf_textures.md`): splash screen, main menu,
client settings, texture/audio/asset strategy, and what ships now vs later. The spec is a
**catalogue of open questions**; this document answers them so the client can be built
without further back-and-forth. The Unity client stays **presentation + input only** — the
.NET server remains the source of game truth.

## Decision summary

**Build a thin, functional shell with placeholder assets now; defer art/audio production.**
M20 delivers the *structure* (splash → main menu → settings → loading → in-game) and a
persisted local-settings model, all bilingual (DE/EN). Real textures, models, music and
sound effects are a later, asset-production milestone — the shell loads them through a
documented folder layout so dropping in real assets needs no code change.

## Branding (answers §3.2)

| Question | Decision (MVP) |
|---|---|
| Name | **Blocks Beyond the Stars** (renamed 2026-06-12 from the former working title; solution/namespaces/binaries renamed to `BlocksBeyondTheStars` too). |
| Logo | Text logo "BLOCKS BEYOND THE STARS" + a simple blocky-ship glyph placeholder. No commissioned art yet. |
| Style | **Clean, friendly sci-fi** — it's a father-and-son game; not dark/gritty. |
| Colours | Primary deep space blue `#0B1E3B`, accent cyan `#39C0ED`, warning amber `#FFB300`. |
| Build badge | Show **"PROTOTYPE / DEV BUILD"** + version on splash and main menu. |

## Splash screen (answers §3.2)

- Shown **every start**, **skippable** (any key / click), max ~2.5s, also bridges real
  content-load time. Static logo + tagline + version + build badge; **silent** until the
  user has set audio (no sound before settings exist).
- Animation/sound: **deferred**. The scaffold is static IMGUI.

## Main menu (answers §4.3)

MVP entries (the spec's first-version list, §4.2):

```
BLOCKS BEYOND THE STARS
  [ Singleplayer / Local World ]   -> starts an in-process server (SP = local server)
  [ Join Server ]                  -> host:port entry (LAN/self-hosted)
  [ Settings ]
  [ Credits ]
  [ Quit ]
```

- **Singleplayer = local server in-process** (confirmed; reuses the same authoritative
  `GameServer`, no duplicated logic — same model as the loopback transport).
- Background: **static colour + starfield placeholder** for MVP; an animated orbiting blocky
  ship is deferred.
- Input: **mouse + keyboard**. Controller support deferred.
- Version is shown; a **client/server protocol-mismatch warning** reuses the existing
  `JoinRejected` "Protocol mismatch" reason (the server already sends it). Auto-update and
  server favourites/last-server are deferred.
- **Web client parity:** the same shell structure is intended to run in the WebGL "Lite"
  client (see `WEBCLIENT_FEASIBILITY.md`); MVP targets the native Windows client first.

## Client settings (answers §6.3)

Client settings are **local only** (display/audio/input/comfort) and never affect the
authoritative rules (PvP, aliens, weapons stay server-decided). Persisted as JSON in
`Application.persistentDataPath/client_settings.json`.

First-version settings (a deliberately small subset of §6.2):

| Group | MVP settings |
|---|---|
| Graphics | Quality **preset** (Low/Medium/High + **Pi/Potato**), fullscreen, view distance (chunks), UI scale |
| Audio | Master, music, SFX volumes; "audio on start screen" toggle |
| Controls | Mouse sensitivity, invert-Y |
| Language | **German / English** (drives the `Localizer`) |
| Accessibility | Reduced effects, camera-shake off, larger UI (flags wired; effects applied later) |

- **Presets** map to view distance + later to shadow/particle/postprocessing quality. A
  **Pi/Potato** preset exists for Raspberry-Pi-hosted / weak machines (spec §6.3). Auto-detect
  of sensible defaults is deferred (we default to Medium).

## Textures & visual style (answers §7.2)

| Question | Decision |
|---|---|
| Style | **Blocky voxel, clean & friendly** — Minecraft-adjacent but its own palette; not photoreal. |
| Resolution | **32×32** base atlas; web client may down-sample to 16×16. Textures are swappable (atlas in `StreamingAssets`). |
| Material groups | Natural blocks, ores, metals, ship walls/floors, cockpit, medbay/heal-tank, workshop, cargo, glass, doors/airlocks/docking, planet surfaces, asteroids, enemies, NPC ships, tools/weapons, UI icons (per spec list). |
| Module colour codes | Medbay blue/green, cockpit blue/orange, workshop yellow/grey, cargo industrial grey. Server-side recolouring deferred. |

**MVP placeholder strategy:** blocks render with **flat placeholder colours** (already the
case in `GameBootstrap`/`ChunkMesher`); no texture atlas is bundled yet. The art pipeline
and atlas are a later milestone.

## 3D models & audio (answers §8.2, §9)

- Models (tools, heal-tank, consoles, ship weapons, NPCs, aliens): **blocky/quader-based**,
  a few rounded hero props allowed later. **No models bundled** in MVP — deferred.
- Audio (UI, ship hums, tools, doors/airlocks/docking, medbay/respawn, alarms, combat):
  categories and volume buses are defined in settings now; **no audio files bundled** — the
  whole audio layer is deferred. The shell stays silent.

## Asset folder layout (so real assets drop in without code changes)

```
client/Assets/BlocksBeyondTheStars/
  Art/Textures/blocks/        # 32x32 atlas + per-block tiles (later)
  Art/Textures/ui/            # icons, logo, splash art (later)
  Art/Models/                 # tools, props, ships (later)
  Audio/ui/  Audio/ship/  Audio/tools/  Audio/ambient/  Audio/music/  (later)
  StreamingAssets/data/       # data-driven definitions + locales (exists, synced)
```

## NOTICES / attribution

`NOTICES.md` (repo root) tracks third-party asset/library attribution. **Today it lists no
bundled third-party art or audio** — all visuals are runtime-generated placeholders. Every
future asset must be added there with its licence before bundling; copyleft/▒NC-only assets
are rejected for this MIT-licensed project (consistent with the licence choice).

## What ships in M20

- This decisions doc + `NOTICES.md`.
- Bilingual UI locale strings for the shell (`ui.menu.*`, `ui.settings.*`, `ui.splash.*`,
  `ui.loading.*`).
- Unity scaffold (IMGUI, presentation-only, matching the existing `Hud` style):
  `AppShell` (splash → menu → settings → loading → in-game state machine),
  `SplashScreen`, `MainMenu`, `SettingsScreen`, `LoadingScreen`, and `ClientSettings`
  (local JSON persistence). Wiring to launch the in-game `GameBootstrap` is included.

## Deferred (explicitly not in M20)

- Real textures/atlas, 3D models, music and sound effects (asset-production milestone).
- Animated splash/menu background, controller support, server favourites/last-server,
  auto-update, in-menu 3D scene.
- uGUI / UI Toolkit polish (MVP uses IMGUI like the current HUD).
- WebGL build of the shell (architecture-ready; needs the Unity Editor).
