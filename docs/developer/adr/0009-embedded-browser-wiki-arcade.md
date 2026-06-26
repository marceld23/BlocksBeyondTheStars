# ADR 0009 — In-game Wiki and Arcade via an embedded browser

- **Status:** Superseded (2026-06-26) — the embedded browser was replaced by native Unity UI.
- **Date:** 2026-06-19
- **Context source:** `MINIGAMES_AND_WIKI.md`

> **Superseded (2026-06-26).** The "Stream D" refactor removed the embedded browser
> (UnityWebBrowser/CEF) entirely. The Codex/Wiki is now a native uGUI screen (`WikiUI.cs`) and the
> Arcade runs a pure, engine-free C# host (`Client.Core/Minigames/MinigameHost`) that renders a
> `Canvas2D` to a texture (`MinigameHostUI.cs`). No UWB/CEF packages, no `BBS_UWB` define and no
> `LocalContentServer` are shipped anymore; `web/` is retained only as the authoring source the C#
> games were ported from. Because the native UI carries no browser-engine dependency, it also builds
> cross-platform (Linux) and removes the WebGL blocker noted in *Consequences* below. This record is
> kept for historical context; the decision it describes is no longer in effect.

## Context

The game needs a rich in-game reference (Codex/Wiki) and a collection of data-cube minigames
(DataQubes Arcade). Building these as native Unity UI would be costly and inflexible. We must
decide how this content is authored, served and rendered.

## Decision

1. **The Wiki and Arcade are HTML/JS rendered in an embedded browser.** The browser is
   UnityWebBrowser (UWB), wrapped behind `EmbeddedBrowser` and gated by the `BBS_UWB` scripting
   define, so the project compiles and runs before UWB is installed (the screens then show a
   "browser not installed" placeholder while collection/highscores still work).
2. **`web/` is the tracked source, synced to StreamingAssets.** The Wiki and the 20 minigames
   (sharing `_shared/framework.js` + `theme.css`) live in `web/` under version control and are
   synced into `StreamingAssets/{wiki,minigames}`.
3. **Content is served locally over loopback.** `LocalContentServer` is a tiny HTTP server on
   `127.0.0.1` serving the bundled `wiki/`, `minigames/`, `data/` and `locales/`, plus dynamic
   routes (e.g. `wiki/wiki-state.json` = discovered systems/worlds + language).
4. **Game truth stays server-side.** Data cubes are server entities; unlocks and knowledge-point
   rewards are server-validated and persisted; only highscores are local.

## Consequences

- Wiki/minigame content is authored and iterated as plain web files without rebuilding the Unity
  client, and the games run in a standard browser environment.
- The single `BBS_UWB` `#if` block is the only integration seam to validate against a UWB version;
  installing UWB (registry + packages + define) is a manual step per `MINIGAMES_AND_WIKI.md`.
- This browser/UWB dependency is a key reason a WebGL/browser client would lose the Wiki/Arcade
  (see `WEBCLIENT_FEASIBILITY.md`).
