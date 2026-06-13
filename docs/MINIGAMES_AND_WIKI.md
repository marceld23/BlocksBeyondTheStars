# In-Game Wiki + Data-Cube Arcade Minigames

Two browser-backed features, sharing one embedded-browser layer:

1. **Codex (Wiki)** — an always-available in-game reference. The Tech/Ships/Blocks/Items/Recipes/Planet-Type
   chapters are generated live from the content JSON; the **Systems & Worlds** chapters are discovery-gated
   (only places the player has actually visited appear). Reached from a **Codex** button in the in-game menu
   header.
2. **Arcade** — the player's personal collection of small bundled HTML5/JS minigames (Snake, 2048, Memory,
   Breakout). Games are **downloaded from "data cubes"** scattered on planets, then played from an **Arcade**
   button in the menu header. Highscores are local-only (no leaderboard).

## How it fits together

```
Data cube on a planet (server entity, deterministic per body, some bodies have none)
   └─ press E ─→ UnlockGameIntent(cubeId, gameKey)  ─→ server validates proximity
                                                       ─→ PlayerState.UnlockedGames += key (persisted, SP+MP)
                                                       ─→ GameUnlocks (server→client) ─→ Arcade collection

Menu "Codex"/"Arcade" button
   └─ EmbeddedBrowser (one shared UWB surface)
        ├─ LocalContentServer  (127.0.0.1, serves StreamingAssets/{wiki,minigames,data,locales})
        ├─ Wiki  → wiki/index.html?lang=..   (+ dynamic wiki/wiki-state.json = discovered systems/worlds)
        └─ Game  → minigames/<key>/index.html?lang=..&hi=<best>&game=<key>
                     └─ on game-over: uwb.ExecuteJsMethod("reportScore", key, score) → local best in ClientSettings
```

### Server (verified, built into the bundled server)
- `PlayerState.UnlockedGames` + `PlayerSnapshot.UnlockedGames` — persisted like `UnlockedBlueprints`.
- `GameServerDataCubes.cs` — `StampDataCubes()` scatters 0–N cubes per body (≈45% none), deterministic from
  the world seed; cubes are server entities (not blocks). `HandleUnlockGame()` validates proximity.
- Net messages (`MinigameMessages.cs`, registered in `NetCodec` tags 118–120): `DataCubeList`,
  `UnlockGameIntent`, `GameUnlocks`.
- Gated by `ServerConfig.PlaceDataCubes` (default on).

### Client
- `LocalContentServer.cs` — loopback static server for StreamingAssets, plus the dynamic
  `wiki/wiki-state.json` route (discovered systems/worlds + language).
- `MinigameCatalog.cs` — loads `StreamingAssets/minigames/catalog.json`; maps a cube **seed → game**.
- `EmbeddedBrowser.cs` — owns the single shared browser surface + content server. UWB code is behind the
  **`BBS_UWB`** define (see below); without it, `Available == false`.
- `DataCubeView.cs` — renders the glowing cube (texture `Resources/props/data_cube`, point light, pulse),
  proximity hum (`data_cube_hum`), and the E label. `PlayerController` E-interact sends the download +
  plays `data_cube_download`.
- `WikiUI.cs` / `ArcadeUI.cs` — full-screen menu screens opened from the header (`GameMenu.OpenWiki/OpenArcade`).
- `GameBootstrap` mirrors `UnlockedGames` + builds `WikiStateJson`.

### Content (source vs runtime)
`client/Assets/StreamingAssets/` is **git-ignored and generated** (like `client/Assets/Plugins`). The tracked
**source** for the browser content lives in repo-root **`web/`** and is copied into StreamingAssets by
`scripts/sync-client-libs.ps1` (run it after editing, then refresh Unity):
- `web/minigames/catalog.json` + `web/minigames/<key>/index.html` (+ `_shared/bridge.js`, `_shared/arcade.css`).
- `web/wiki/index.html` + `wiki.js` + `wiki.css` + `articles.json`.
- Bilingual via the existing locale files (`data/locales/{en,de}.json`, also synced).

### Generated assets (via `tools/ai-assets`, committed to the project)
- `Resources/props/data_cube.png` (OpenAI) — cube texture.
- `Resources/audio/data_cube_download.mp3`, `Resources/audio/data_cube_hum.mp3` (ElevenLabs) — SFX
  (ClientAudio loads clips by filename).

---

## ⚙️ Manual step: enable the embedded browser (UnityWebBrowser)

Everything above compiles and runs **without** the browser package — the Wiki/Arcade then show a
"browser not installed" placeholder while data cubes, downloads, the collection list and highscores all work.
To turn on the real browser:

1. **Add the scoped registries + UWB packages** to `client/Packages/manifest.json`. UWB lives on the VoltUPR
   registry and depends on UniTask (`com.cysharp.unitask`, on OpenUPM). Merge these `scopedRegistries` and
   `dependencies` into the existing file (don't replace it). Verify the latest versions on the
   [UWB releases page](https://github.com/Voltstro-Studios/UnityWebBrowser/releases) — 2.2.x at time of writing:

   ```jsonc
   {
     "scopedRegistries": [
       {
         "name": "Voltstro UPM",
         "url": "https://upm-pkgs.voltstro.dev",
         "scopes": [ "dev.voltstro", "org.nuget" ]
       },
       {
         "name": "OpenUPM (UniTask)",
         "url": "https://package.openupm.com",
         "scopes": [ "com.cysharp.unitask" ]
       }
     ],
     "dependencies": {
       "dev.voltstro.unitywebbrowser": "2.2.8",
       "dev.voltstro.unitywebbrowser.engine.cef": "2.2.8",
       "dev.voltstro.unitywebbrowser.engine.cef.win.x64": "2.2.8",
       "com.cysharp.unitask": "2.5.11",
       // …keep all existing deps (com.unity.render-pipelines.universal, com.unity.ugui, …)…
     }
   }
   ```

   Reopen the project (or Window → Package Manager) so Unity resolves and downloads the packages. Confirm
   `UnityWebBrowser` appears under Packages with no resolution errors before continuing. Min Unity 2021.3 (we
   are on Unity 6, fine).

2. **Reference the UWB assembly** from the client asmdef: `BlocksBeyondTheStars.Client.asmdef` must list
   `"VoltstroStudios.UnityWebBrowser"` in its `references` (already done in this repo). Without it you get
   `CS0246: 'VoltstroStudios' could not be found`, because the client asmdef uses `overrideReferences` and
   lists its references explicitly.

3. **Add the `BBS_UWB` scripting define**: Project Settings → Player → Other Settings → Scripting Define
   Symbols (Standalone) → add `BBS_UWB`. This activates the UWB code in `EmbeddedBrowser.cs`. (Only set it
   **after** step 1 resolves — the define makes the code reference UWB types, so without the package the
   client won't compile.)

4. **Integration point** (already validated against UWB 2.2.8): the single `#if BBS_UWB` block in
   `EmbeddedBrowser.cs` creates a `WebBrowserUIBasic` in code and assigns the engine/communication/input
   ScriptableObjects the UWB packages ship in their `Resources` folders (`Cef Engine Configuration`,
   `TCP Communication Layer`, `Old Input Handler` — legacy input), so no Inspector wiring is needed. It uses
   `browserClient.LoadUrl` / `RegisterJsMethod<string,int>` / `jsMethodManager.jsMethodsEnable`. If you bump UWB
   to a version with different names, this is the only place to adjust.

5. **Build notes**: CEF adds ~150–200 MB to the build (auto-copied to `<Game>_Data/UWB/` by UWB's
   post-build step). Sign the build to avoid SmartScreen/AV flags on the spawned browser process. Minigame SFX
   use WAV/OGG/Opus only (CEF ships open codecs).

> Reference: registry `https://upm-pkgs.voltstro.dev` (scopes `dev.voltstro`, `org.nuget`) + UniTask via
> OpenUPM — confirmed from the [VoltstroUPM registry](https://github.com/Voltstro/VoltstroUPM) and the
> [UWB setup guide](https://projects.voltstro.dev/UnityWebBrowser/latest/articles/user/setup/).

## Adding a new minigame (author-only — not user-moddable)

A minigame is a self-contained folder of HTML/JS/CSS under `web/minigames/<key>/`, plus one entry in
`web/minigames/catalog.json`. No C#/Unity changes are needed.

### 1. Create the game folder
```
web/minigames/<key>/
  index.html        # entry point (any extra .js/.css/assets live alongside)
```
- Make it self-contained (relative paths only). It's served over `http://127.0.0.1:<port>/minigames/<key>/`,
  so `fetch()`, ES modules and `<canvas>` all work like a normal site.
- Reuse the shared frame from a sibling folder: `../_shared/arcade.css` (sci-fi theme) and
  `../_shared/bridge.js` (the C# bridge). Both are served too.
- **Sound:** WAV/OGG/Opus only (the embedded CEF ships open codecs — MP3/AAC may not play).

### 2. Use the bridge (`web/minigames/_shared/bridge.js`)
Include it, then use the `BBS` global. The C# host passes `?lang`, `?hi` (the player's local best) and `?game`
on the URL; the bridge reads them for you:
- `BBS.lang` — `"de"` or `"en"` (all in-game text must be bilingual).
- `BBS.t({ en: "...", de: "..." })` — pick the active-language string.
- `BBS.best` — the player's current local high score for this game (show it as "Best").
- `BBS.reportScore(score)` — call on game-over; C# keeps the per-game personal best in `ClientSettings`
  (local only, no leaderboard). Safe no-op in a plain dev browser.

Minimal template:
```html
<!DOCTYPE html><html><head><meta charset="utf-8">
<link rel="stylesheet" href="../_shared/arcade.css"></head>
<body><div class="wrap">
  <div class="title" id="t"></div>
  <div class="hud"><span id="bl"></span><b id="best">0</b></div>
  <!-- your game here -->
</div>
<script src="../_shared/bridge.js"></script>
<script>
  document.getElementById('t').textContent  = BBS.t({en:"My Game", de:"Mein Spiel"});
  document.getElementById('bl').textContent = BBS.t({en:"Best", de:"Beste"});
  document.getElementById('best').textContent = BBS.best;
  // …on game over: BBS.reportScore(finalScore);
</script></body></html>
```

### 3. Register it in `web/minigames/catalog.json`
Append an entry to the `games` array:
```json
{
  "key": "mygame",
  "entry": "mygame/index.html",
  "icon": "🎲",
  "title": { "en": "My Game", "de": "Mein Spiel" },
  "desc":  { "en": "One-line description.", "de": "Einzeilige Beschreibung." }
}
```
- `key` must be unique and match the folder name. It's what gets stored in the player's collection.
- **Order is authoritative:** a data cube maps its seed to a game by index into this array
  (`seed mod games.length`). Appending is safe; **reordering/removing changes which cube grants which game**
  (and would orphan keys already in players' saved collections).

### 4. Sync + test
- Run `scripts/sync-client-libs.ps1` (copies `web/* → StreamingAssets`), then refresh Unity.
- Test fast: open `web/minigames/<key>/index.html` directly in a desktop browser (the bridge no-ops, so it
  still plays). In-game, grab a data cube or temporarily seed `PlayerState.UnlockedGames` to see it in Arcade.

A scoring note: "higher is better" (the stored best is the max). For move-based games (like Memory) convert to
a higher-is-better score before calling `reportScore`.
