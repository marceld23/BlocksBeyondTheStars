# In-Game Wiki + Data-Cube Arcade Minigames

Two in-game features, both rendered **natively in Unity** (no embedded browser):

1. **Codex (Wiki)** — an always-available in-game reference. The Tech/Ships/Blocks/Items/Recipes/Planet-Type
   chapters are generated live from the content JSON; the **Systems & Worlds** chapters are discovery-gated
   (only places the player has actually visited appear). Reached from a **Codex** button in the in-game menu
   header.
2. **DataQubes** — the player's collection of "data fragments": 20 bundled minigames (Blockfall,
   Asteroid Breaker, Circuit Weaver, Signal Tuner, Drone Rescue, Cargo Sorter, Blueprint Scramble, Orbit
   Slingshot, Laser Mirror Grid, Micro Miner, Star Map Memory, Alien Glyph Decoder, Reactor Balance, Oxygen
   Loop, Comet Courier, Docking Simulator, Data Fishing, Nanobot Repair, Planet Scanner, Void Solitaire). They
   are **recovered from "data cubes"** scattered on planets, then run from a **DataQubes** button in the menu
   header. All games share one C# **host** (`MinigameHost` + `Canvas2D`): a uniform shell
   (start/help/pause/result), an abstract input model and the blue-line theme. Finishing a run grants
   **knowledge points** (server-side, rating-scaled, repeatable). Highscores are local-only.

   A Creative world's "unlock all" option also recovers **every** data fragment up front (for testing).

> **History:** these features originally ran as HTML/JS inside an embedded browser (UnityWebBrowser/CEF).
> That whole layer was removed in PR #58 (native rewrite) — all 20 games were ported to C# and the Wiki now
> renders natively. Removing CEF also unblocked the Linux/macOS builds. The per-game `/// Port of web/...`
> doc-comments record where each game was ported from; the old `web/` source folder no longer exists. See ADR
> `docs/developer/adr/0009-embedded-browser-wiki-arcade.md` (superseded).

## How it fits together

```
Data cube on a planet (server entity, deterministic per body, some bodies have none)
   └─ press E ─→ UnlockGameIntent(cubeId, gameKey)  ─→ server validates proximity
                                                       ─→ PlayerState.UnlockedGames += key (persisted, SP+MP)
                                                       ─→ GameUnlocks (server→client) ─→ Arcade collection

Menu "Codex"/"DataQubes" button
   ├─ WikiUI      → renders articles natively from articles.json via WikiMarkup
   └─ ArcadeUI    → MinigameHostUI runs MinigameRegistry.Create(key) — a pure C# IMinigame
                      └─ on game-over: MinigameHost reports {score, rating} → server grants knowledge,
                         local best stored in ClientSettings
```

### Server (verified, built into the bundled server)
- `PlayerState.UnlockedGames` + `PlayerSnapshot.UnlockedGames` — persisted like `UnlockedBlueprints`.
- `GameServerDataCubes.cs` — `StampDataCubes()` scatters 0–N cubes per body (≈45% none), deterministic from
  the world seed; cubes are server entities (not blocks). `HandleUnlockGame()` validates proximity. The cube
  → game mapping reads the keys from the bundled `data/minigames/catalog.json` (resolved as
  `<DataDir>/minigames/catalog.json`).
- Net messages (`MinigameMessages.cs`, registered in `NetCodec` tags 118–121): `DataCubeList`,
  `UnlockGameIntent`, `GameUnlocks`, `MinigameResultIntent` (finished run → server grants knowledge points,
  rating-scaled, repeatable, in `GameServerDataCubes.HandleMinigameResult`).
- Gated by `ServerConfig.PlaceDataCubes` (default on). A Creative world (`CreativeUnlockAllBlueprints`) also
  unlocks every minigame via `UnlockAllGames`.

### Client / shared
- `MinigameRegistry` (`Client.Core`) — the authoritative list of native games, keyed and **ordered** the same
  as `data/minigames/catalog.json`. `Create(key)` yields a fresh `IMinigame`; `ForSeed(seed)` backs the
  cube → game mapping. Pure (Unity-free), unit-tested headless.
- `MinigameHost` (`Client.Core`) — the engine-free heart of the Arcade (the C# successor to the old shared JS
  framework): drives the start/help/pause/result shell, the input model and the result→knowledge bridge,
  drawing into a `Canvas2D`.
- `MinigameHostUI.cs` (Unity) — gives the host a uGUI body: a point-filtered `RawImage` fed from the game's
  `Canvas2D` each frame. Needs no UWB/CEF, so it builds on every platform.
- `MinigameCatalog.cs` (Unity) — loads `StreamingAssets/data/minigames/catalog.json` for UI metadata
  (icon/title/desc + cube seed → game). On WebGL this comes from the cached StreamingAssets data folder.
- `WikiUI.cs` + `WikiMarkup` (`Client.Core`) — load `StreamingAssets/data/wiki/articles.json` and render the
  articles natively (discovery-gated Systems & Worlds chapters from the player's visited set).
- `DataCubeView.cs` — renders the glowing cube (texture `Resources/props/data_cube`, point light, pulse),
  proximity hum (`data_cube_hum`), and the E label. `PlayerController` E-interact sends the download +
  plays `data_cube_download`.
- `GameBootstrap` mirrors `UnlockedGames` + builds the discovered-systems/worlds set for the Wiki.

### Content (source vs runtime)
`client/Assets/StreamingAssets/` is **git-ignored and generated** (like `client/Assets/Plugins`). The tracked
**source** lives under repo-root **`data/`** and is copied into StreamingAssets by `scripts/sync-client-libs.ps1`
(run it after editing, then refresh Unity). `data/` is also bundled into the dedicated-server build
(`scripts/publish-server.ps1`/`.sh`), so the server gets the catalogue too:
- `data/minigames/catalog.json` — the game catalogue (order is authoritative; see below).
- `data/wiki/articles.json` — the Wiki articles.
- Bilingual via the existing locale files (`data/locales/{en,de}.json`, also synced).

### Generated assets (via `tools/ai-assets`, committed to the project)
- `Resources/props/data_cube.png` (OpenAI) — cube texture.
- `Resources/audio/data_cube_download.mp3`, `Resources/audio/data_cube_hum.mp3` (ElevenLabs) — SFX
  (ClientAudio loads clips by filename).

---

## Adding a new minigame (author-only — not user-moddable)

A native minigame is a C# class plus one entry in the catalogue. Two files change:

### 1. Implement `IMinigame`
Add `<Name>Game.cs` under `src/BlocksBeyondTheStars.Client.Core/Minigames/Games/`. Implement the
`IMinigame` interface — the mechanic, update/draw against the `Canvas2D` the host hands you, and call the
host's completion API (`{score, rating 1..3}`) on finish. The host owns the shell (start/help/pause/result),
the input model and the result→knowledge bridge, so you don't wire rewards per game. Reusable engines live
alongside (e.g. `FlowPuzzleGame` powers Circuit Weaver + Oxygen Loop). Keep it engine-free (no `UnityEngine`)
so it stays headless-testable.

### 2. Register it in `MinigameRegistry` **and** `data/minigames/catalog.json`
- Append the factory to `MinigameRegistry.Entries` (`Client.Core/Minigames/MinigameRegistry.cs`).
- Append the matching entry to the `games` array in `data/minigames/catalog.json`:
  ```json
  {
    "key": "mygame",
    "icon": "🎲",
    "title": { "en": "My Game", "de": "Mein Spiel" },
    "desc":  { "en": "One-line description.", "de": "Einzeilige Beschreibung." }
  }
  ```
- `key` must be unique and match the registry key. It's what gets stored in the player's collection.
- **Order is authoritative and must match the registry order:** a data cube maps its seed to a game by index
  (`seed mod games.length`). Appending is safe; **reordering/removing changes which cube grants which game**
  (and would orphan keys already in players' saved collections). A unit test guards that the registry and the
  catalogue stay in lockstep.

### 3. Sync + test
- Run `scripts/sync-client-libs.ps1` (copies `data/* → StreamingAssets`), then refresh Unity.
- Headless: `dotnet test` exercises the registry/host and each game's logic without Unity.
- In-game: use a Creative world ("unlock all") to get every fragment in the DataQubes menu.

Scoring note: "higher is better" (rating 1–3 drives the knowledge reward). For move/time-based games convert
to a higher-is-better score + rating before reporting completion.

Bilingual ({en,de}) everywhere; sound WAV/OGG/Opus only.
