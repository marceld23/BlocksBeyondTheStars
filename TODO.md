# Blocks Beyond the Stars — Project Status

The single source of truth for **what is built** and **what is still open**. Design notes and deep
plans live under [docs/](docs/) (committed); this file is the high-level status. Player-facing operation
(controls, mechanics, editors, commands) is documented in [docs/user/USER_MANUAL.md](docs/user/USER_MANUAL.md) —
keep it current when controls/features change. Last consolidated 2026-06-04.

**Build:** `scripts/build-client.ps1` (publishes shared libs + bundled server + Unity Windows player).
**Test:** `dotnet test` — currently **683 passing** (2026-06-19). Locale parity (en/de) is enforced by a test.
**Conventions:** English docs/comments; in-game text bilingual DE+EN; commit to `main` with the
Claude `Co-Authored-By` trailer; OpenAI texture + ElevenLabs sound generation is blanket-approved
(no per-batch gate).

Architecture: Unity 6 (URP since 2026-06-10) client + authoritative .NET 8 server, everything built in
code (no scene authoring). One shared world; contractless MessagePack networking; deterministic seed
world-gen; SQLite persistence.

---

### ★ Early-game defence: starter sidearm + line-of-sight hiding (2026-06-23) — ✅ done (708 green; NEEDS Unity build)
New players were attacked with only a melee machete (whose 3.5 reach is *shorter* than an enemy's 4-block bite aura) and
no way to break off an attack — enemies tracked through walls/earth (no line-of-sight anywhere). Option C: give a weak
ranged sidearm **and** make cover/caves actually work.
- **Starter sidearm** `scrap_pistol` (10 dmg, range 16, 0.8s cd, no energy) added to the start kit (`CreateNewPlayer`
  slot 5) + de/en locale + OpenAI icon + ElevenLabs `weapon_scrap` shot sound + kinetic-bolt fire FX/recoil.
- **Ranged weapons actually fire at range**: the client hard-capped attack reach at 6 blocks, ignoring the weapon —
  so every gun (gauss/laser/plasma too) only fired point-blank. `PlayerController.AttackNearestEnemy` now uses the
  held weapon's `Tool.Range`.
- **Line-of-sight gate** (server): new 3D `HasLineOfSight` sampler. A hostile can't bite a target it can't see, so
  ducking behind cover or into a cave **stops the damage** (creatures + planet machines). Machines also drop the hunt
  after ~6s blind; creatures tire of the chase ~2× faster while they can't see you.
- Tests: new `LineOfSightTests.cs` (clear / horizontal wall / vertical cave / end-to-end damage gate). Suite **708**.

### ★ Keep wildlife (and planet enemies) out of the parked ship (2026-06-22) — ✅ server-only done (700 green, 0 warns; NO Unity build needed)
A wild creature could end up **inside** the landed ship. The ship is an object with AABB checks (no voxel collision),
and movement can't enter (it freezes at the hull) — so the cause was **placement**: `PlaceLandedShip` never evicted a
creature the ship parked on/over (or that a ship redesign grew over), leaving it sealed in the cabin.
- New `TryPushOutsideShip(p, out outside)` helper (`GameServerShipStructure.cs`) — same bounds as `EntityBlockedByShip`,
  pushes a point just past the nearest horizontal hull face (keeps height).
- **P1** `EvictWildlifeFromShips()` runs at the end of `PlaceLandedShip` (companions are left alone — they may follow
  their owner aboard). **P2** `MoveCreatures` ejects any wild creature whose current position is inside, every tick.
- **P3** creature spawn-reject now tests `EntityBlockedByShip` (the movement barrier) instead of the tighter interior box,
  closing the shell where a spawn was allowed but immediately frozen against the hull.
- **P5** planet enemies (`MovePlanetEnemy`) get the same eject-if-inside + block-the-move-into-the-hull guard.
- **P4** 2 new tests in `CreatureTests.cs` (park-over-creature sweep + per-tick eject). Suite **700 passing**.

### ★ Tiered radio reach + live voice chat (2026-06-21) — ✅ server+tests done (8 new, suite green); voice SHIPPED ON by default; NEEDS Unity build
- **Tiered radio reach** (text **and** voice): `comm_radio` = same world, new `system_radio` = same star system, new
  `galaxy_radio` = whole game. New items/recipes/blueprints (`data/items.json`/`recipes.json`/`blueprints.json`, de+en
  locale) + 2 OpenAI item icons. Server `RadioAudience()`/`HasAnyRadio()` pick recipients by the widest tier held;
  `HandleChat` rewired from flat `Broadcast` to the tiered audience. Tests: `RadioTierTests.cs`.
- **Live voice chat** (opt-in, push-to-talk): thin **opaque server relay** — `VoiceFrame` (NetCodec 169, Unreliable),
  `GameServer.HandleVoice` stamps the sender + relays Opus bytes to the same tiered audience (except self), never decodes.
  Gated on `ServerConfig.VoiceChatEnabled` (default **off**; `--voice`/`BBS_VOICE`) + a radio, surfaced via
  `ServerRules.VoiceChatEnabled`. Audio relayed live, **never recorded**.
- **Client:** `NetworkClient.SendVoice`/`VoiceReceived` (Client.Core, always compiled) + `VoiceChat.cs` (mic capture →
  Opus 20 ms frames → per-speaker jitter buffer → streaming `AudioSource`, flat 2D) + PTT/voice settings (master on/off +
  volume + transmit + key) + WorldRig wiring + "● Talking…" HUD. Native client only (no WebGL mic); no echo cancellation
  (headset + PTT in v1).
- **Shipped ON by default:** Opus codec via **Concentus** (2.2.2, BSD-3-Clause, pure C#) referenced by `Client.Core` so it
  vendors through sync→Plugins→Unity→installer (`Concentus.dll` = only new DLL); `BBS_VOICE` define on + asmdef ref added.
  The bundled host launcher passes `--voice true` (local hosting just works); dedicated servers stay opt-in
  (`voiceChatEnabled`). Players can switch voice off in Settings. Docs: [VOICE_CHAT.md](docs/developer/VOICE_CHAT.md),
  USER_MANUAL, NOTICES (Concentus license). Follow-ups: per-player mute UI, spatial/positional voice.

### ★ Optional Docker image for the dedicated server (2026-06-21) — ✅ done (built + pushed to GHCR on each release)
Ship the headless server **and** the admin/portal/download UI (and the optional AI backend) as one optional Linux
container — runs on any OS (Linux/macOS/Windows-WSL2/NAS/VPS); the game client stays Windows-only.
- **One image, asymmetric processes** under `tini` (PID 1): [`Dockerfile`](Dockerfile) + [`docker/entrypoint.sh`](docker/entrypoint.sh) +
  [`docker-compose.yml`](docker-compose.yml). Game server = critical foreground process (gets the signal, saves the world);
  admin API = best-effort auto-restarting sidecar. `docker stop` (SIGTERM) is translated to the **SIGINT** the server's
  clean drain+save path listens for.
- **AI backend bundled, opt-in start:** the Python `ai-backend` is baked into the image (own venv) but only **starts when
  configured** — an `ai-backend/.env` mounted at `/app/ai-backend/.env` or `BBTS_AI_BASE_URL` set (override `BBS_AI_BACKEND=1/0`).
  Listens on in-container `127.0.0.1:8077` (= server's default `aiBackendUrl`); enable use with `BBS_AI_LEVEL`. `.dockerignore`
  excludes `**/.env` so local keys never bake into the image. (Adds LangChain/LangGraph → larger image.)
- **Env-var config** (container channel): new `ServerConfig.ApplyEnvironment()` reads `BBS_*` vars; precedence
  `server.json` < env < CLI. Wired into both the GameServer and the Api (`AdminService.LoadConfig`). 3 new tests (689 green).
- **Client download** (`/download`): a Linux container can't build the Windows installer, so the entrypoint pulls the
  latest `*Setup.exe` from GitHub Releases into `clients/` (best-effort, `BBS_FETCH_CLIENT`/`BBS_CLIENT_REPO`).
- **`/bump` bug reports work from the container** unchanged: they fall back to `saves/<world>/bumps/`, which is on the
  persisted `saves` volume (retrievable via the volume or `docker cp`).
- **CI:** [`docker.yml`](.github/workflows/docker.yml) is a reusable workflow (`workflow_call`) invoked by
  [`release.yml`](.github/workflows/release.yml) → tag builds + pushes multi-arch (amd64+arm64) to **GHCR**
  (`ghcr.io/marceld23/blocks-beyond-the-stars-server:<version>`+`:latest`); also `workflow_dispatch` for ad-hoc builds.
  Docs: [SELF_HOSTING.md §10](docs/developer/SELF_HOSTING.md) + `BBS_*` env-var table, README **System requirements**,
  [DEVELOPER.md §CI](docs/developer/DEVELOPER.md), [AGENTS.md §Releases](AGENTS.md).

### ★ Syntax/lint + CodeQL checks across all languages (2026-06-21) — ✅ lint+CodeQL added & green (PR #22); dotnet format DEFERRED
Extends the PR gate beyond build+test (C# is already syntax/static-analysis-checked by the `-warnaserror` build):
- **[lint.yml](.github/workflows/lint.yml)**: ruff (Python ai-backend, already clean), `node --check` (web JS parse), actionlint 1.7.12 (workflows, ShellCheck off). All verified green in CI.
- **[codeql.yml](.github/workflows/codeql.yml)**: CodeQL security+quality for csharp (build-mode none → also covers Unity client C#), javascript-typescript, python, actions. Advanced-setup workflow (GITHUB_TOKEN has security-events:write; API default-setup PUT 404'd on token scope).
- **Fixed a latent config bug:** `.editorconfig` had `end_of_line = crlf` while `.gitattributes` enforces `eol=lf` → any `dotnet format` would fail on every fresh (LF) checkout. Set `.editorconfig` to `lf`. Also fixed 3 using-order violations (NetCodec.cs, SqliteWorldRepository.cs, InventoryTests.cs).
- Docs: [DEVELOPER.md](docs/developer/DEVELOPER.md) §CI (format/lint/CodeQL), [AGENTS.md](AGENTS.md) (LF policy + verification chain).

### ★ Codebase reformat + activate `Format (dotnet format)` gate (2026-06-21) — ✅ done (follow-up to #22)
- Ran `dotnet format BlocksBeyondTheStars.CI.slnf` over the tree: **60 files, formatting-only** (indentation + object-initializer/argument line breaks; `git diff -w` confirms zero logic change). Resolved the ~1873 pre-existing whitespace violations.
- Re-added the **`Format (dotnet format)`** job to [ci.yml](.github/workflows/ci.yml) (gated by the `changes` job like build-test). Now green because the tree is clean.
- **All six checks now required** on `main` (2026-06-21): `Build + test (.NET, headless)`, `Format (dotnet format)`, `ruff (Python ai-backend)`, `web JS syntax (node --check)`, `actionlint (workflows)`, `CodeQL`. `strict` off, `enforce_admins` off (owner can `--admin`-override), 1 approval kept. Docs reflect this in [DEVELOPER.md](docs/developer/DEVELOPER.md) §CI + [AGENTS.md](AGENTS.md) §Git workflow.

### ★ Pull-request CI gate: build + headless tests, warnings-as-errors (2026-06-21) — ✅ merged to main (PR #16 c86858e); required-check guard added
New [.github/workflows/ci.yml](.github/workflows/ci.yml) runs on every `pull_request` + push to `main` (separate
from the tag-only `release.yml`). On `ubuntu-latest`, **no Unity / no license**: restores, builds and tests the
two headless suites directly — `tests/BlocksBeyondTheStars.Tests` (Dotnet) + `tests/BlocksBeyondTheStars.Client.Tests`
(ClientCore) — the same default set as `run-tests.ps1`.
- **Targets the test projects, not the `.sln`** — the solution's WinForms launcher (`net8.0-windows`) can't build on Linux.
- **Warnings fail the PR** via `-warnaserror` (local build keeps `TreatWarningsAsErrors=false`; tree is currently 0-warning). `.trx` results uploaded as an artifact.
- **Docs-only PRs skip the build but stay green** — a `changes` job (`dorny/paths-filter`) gates the `build-test` job; on a docs-only PR (`**/*.md`, `docs/**`, licences, issue/PR templates) `build-test` is skipped and a skipped *required* job counts as a pass, so it never blocks. `data/**`/`web/**` count as code (they feed tests). This makes the check **safe to mark required** (unlike a plain `paths-ignore` skip, which would stall a required check).
- **Unity tiers (`UnityEdit`/`UnityPlay`) stay local** (need the Editor) — run `./scripts/run-tests.ps1 -Suites All` before client-affecting changes.
- **Follow-up (manual, repo settings):** mark **`Build + test (.NET, headless)`** as a required status check in `main` branch protection (the `Detect code changes` helper does NOT need to be required). Docs: [DEVELOPER.md](docs/developer/DEVELOPER.md) §Continuous integration, [CLIENT_TESTING.md](docs/developer/CLIENT_TESTING.md), [CONTRIBUTING.md](CONTRIBUTING.md).

### ★ Multi-planet marketing screenshots (planet-type variety) (2026-06-20) — ✅ code + .NET test green, NEEDS Unity client build + GPU capture run
Extends the screenshot automation to shoot one **surface shot per planet type** (`surface_<key>.png`) so the gallery shows the world variety, not just the home world.
- **New `--start-planet <key>` server CLI arg** ([ServerConfig.cs](src/BlocksBeyondTheStars.Shared/Configuration/ServerConfig.cs)) forces the spawn planet's type (the field existed; only the CLI plumb was missing). Covered by the existing CLI-parse test ([WorldOptionsTests.cs](tests/BlocksBeyondTheStars.Tests/WorldOptionsTests.cs) `CommandLine_ParsesWorldOptionOverrides`, green).
- **Client plumb**: new `StartPlanetType` field on [WorldCreationOptions.cs](client/Assets/BlocksBeyondTheStars/Scripts/WorldCreationOptions.cs) → emitted by `ToArgs()` as `--start-planet`.
- **[ScreenshotDirector.cs](client/Assets/BlocksBeyondTheStars/Scripts/ScreenshotDirector.cs)**: new `-planet <key>` flag → surface-only path (`CapturePlanetSurface`) that starts a fresh world pinned to that type and shoots only `surface_<key>.png` (skips the planet-agnostic menu/cockpit/flight shots). No `-planet` ⇒ the full existing sequence is unchanged.
- **[scripts/capture-screenshots.ps1](scripts/capture-screenshots.ps1)**: new `-Planets` list (default curated 8: jungle, lava, ocean, ice, desert, fungal, skylands, crystal) loops the player once per type × language; `-SkipMain` captures only surfaces, `-Planets @()` only the main sequence.
- **Open / first-run tuning** (deferred per plan): ocean (spawn in water), skylands (fall risk) and other flat types may need per-type pose tweaks (offset/yaw/pitch); verify on the first real GPU run.

### ★ Player feedback ("Spieler Feedback") in-game button → website API (2026-06-20) — ✅ code + .NET tests green, NEEDS Unity client build; website API NOT built yet
The **F1** hotkey opens a modal where any player sends a bug report or a feature wish (one form: title, description, optional e-mail, privacy hint). F1 is listed in the on-foot HUD controls hint (`ui.hud.hint`). _(2026-06-22: the earlier bottom-right HUD button was removed — it was only shown during normal play, when the cursor is locked/hidden, so it could never be mouse-clicked; F1 is the single entry point.)_ On Send it grabs a full-frame screenshot **with the HUD but without the dialog** (captured at open, before the screen dims) and fires **both** paths: a client-direct HTTPS POST to `www.blocksbeyondthestars.com/_functions/bugreport`, and the existing `/bump` message so the server still writes its rich local snapshot on own/SP servers.
- **Testable core** in `Client.Core`: [FeedbackUploader.cs](src/BlocksBeyondTheStars.Client.Core/Feedback/FeedbackUploader.cs) + [FeedbackReport.cs](src/BlocksBeyondTheStars.Client.Core/Feedback/FeedbackReport.cs) use `HttpClient` + `System.Text.Json` (same code in Unity and tests). **7 new xUnit tests** ([FeedbackUploaderTests.cs](tests/BlocksBeyondTheStars.Client.Tests/FeedbackUploaderTests.cs)) drive it against a local `HttpListener` ("simulierte lokale Schnittstelle"): accept/201+id, camelCase JSON + base64 screenshot roundtrip, wrong key→403, empty/oversize/offline all handled without throwing. **12 client + 683 server tests green.**
- **Unity**: [FeedbackUi.cs](client/Assets/BlocksBeyondTheStars/Scripts/FeedbackUi.cs) (F1 → dialog + capture + dual send), wired in [WorldRig.cs](client/Assets/BlocksBeyondTheStars/Scripts/WorldRig.cs). Icon [btn_feedback.png](client/Assets/Resources/icons/btn_feedback.png) (OpenAI, speech-bubble+pencil) now only decorates the dialog's Send button.
- **API key** is a spam gate only (ships in client): [BugReportBuildSecrets.cs](client/Assets/BlocksBeyondTheStars/Scripts/BugReportBuildSecrets.cs) holds an empty key (dev builds never post to prod → dialog shows `sent_local`); CI injects the real key via a git-ignored partial. See [docs/developer/PLAYER_FEEDBACK.md](docs/developer/PLAYER_FEEDBACK.md).
- **Menu text**: the "Mach mit" overlay now lists **player feedback first** (highlighted) before the GitHub/dev paths (`ui.contribute.feedback` + intro reworded; new `ui.feedback.*` keys de+en, locale parity test green).
- **Open**: stand up the Wix/Velo endpoint per the requirements doc, then add the `WIX_BUGREPORT_API_KEY` Environment secret + the CI step; confirm the website privacy note matches `ui.feedback.hint`. **NEEDS Unity build to verify the F1 dialog + that `System.Net.Http` resolves in the player (first client HttpClient use).**

### ★ Open-source "Mach mit" invite + menu/splash polish (2026-06-20) — ✅ code done, NEEDS Unity client build
Preparing the repo to go open source and recruit contributors (repo still PRIVATE; in-game GitHub links go live only once public).
- **Studio logo** ([StudioSplash.cs](client/Assets/BlocksBeyondTheStars/Scripts/StudioSplash.cs)): new "Beitragende: Dein Name könnte hier stehen" line below the slogan (fades in with it); the previously hardcoded slogan is now localized too (`ui.studio.slogan` / `ui.studio.contributors`).
- **Title splash** ([SplashScreen.cs](client/Assets/BlocksBeyondTheStars/Scripts/SplashScreen.cs)): one-line teaser "Open Source — mach mit auf GitHub" (`ui.splash.contribute`).
- **Main menu** ([UiMainMenu.cs](client/Assets/BlocksBeyondTheStars/Scripts/UiMainMenu.cs)): the bottom-right "Auf Steam merken" text is replaced by a clickable **"Mach mit"** button (`ui.menu.contribute`) opening a participate overlay (3 tiers: play / report bugs as a GitHub issue / open a PR + repo URL, keys `ui.contribute.*`). GitHub-only for now (the /bump→website upload idea is parked).
- **Shared modal dim** ([UiKit.cs](client/Assets/BlocksBeyondTheStars/Scripts/UiKit.cs) `AddModalDim`): one consistent dark scrim + click-swallow behind overlay dialogs; used by the new participate panel (existing overlays can adopt it later).
- **CONTRIBUTING.md** (new, root, English, GitHub-focused) + linked from README. README/AGENTS/TODO renaming notes removed (game was never published).
- **Menu/splash blur** reduced ([UrpScenePost.cs](client/Assets/BlocksBeyondTheStars/Scripts/UrpScenePost.cs) new `ShellMode`: tighter bloom threshold 1.1 / intensity 0.28 / scatter 0.4; [MenuBackground.cs](client/Assets/BlocksBeyondTheStars/Scripts/MenuBackground.cs) nebula menu-brightness 0.75→0.6; URP asset Adaptive Performance off) — it was a *look* issue (bloom over an emissive-dense attract scene), not resolution (renderScale 1, direct render). Tune by eye after a build.
- Locale parity kept (en+de); localization + content tests green. **Follow-up: rebuild the Windows player, eyeball the blur/menu, then re-capture marketing screenshots (`scripts/capture-screenshots.ps1`) since the splash changed.**

### ★ GitHub Actions release build (public-repo safe) — ✅ working end-to-end (2026-06-20, v0.2.0 = version-SoT + installer trio)
`.github/workflows/release.yml` builds a Windows release and publishes a GitHub Release on a pushed `v*` tag
(or the manual *Run workflow* button). **Validated: v0.1.0 published a 450 MB `BlocksBeyondTheStars-v0.1.0.zip`
(player + bundled server + launcher).** The repo can be public without exposing the Unity key: GitHub masks
secrets in all logs and never hands them to fork PRs, and the workflow runs **only** on tags / manual dispatch
(never on `pull_request`), so no foreign code can reach the Unity secrets.
- **Version single-source-of-truth (2026-06-20):** the git tag `vX.Y.Z` is now the only version source.
  CI feeds it to GameCI `versioning: Custom` → `PlayerSettings.bundleVersion`, so the in-game UI shows it via
  `AppShell.Version` (now `=> Application.version`, no longer a hardcoded const); the launcher gets `-p:Version`
  and Velopack `--packVersion` the same value. Committed `bundleVersion` = `0.1.0-dev` for local builds
  (Velopack needs packVersion ≥ 0.0.1). A CI guard (BuildScript writes `version.txt`) fails the build if the
  baked version ≠ the tag. `Protocol.Version` stays separate.
- **Windows installer trio (2026-06-20):** the release no longer ships a hand-rolled zip — the windows job runs
  `publish-client-installer.ps1 -Msi` (vpk pinned 1.2.0) and attaches **Setup.exe** (per-user, no admin),
  the **WiX MSI** (machine-wide/IT) and **Portable.zip**. (Linux/macOS client installers intentionally skipped
  — blocked by the UWB/CEF cross-platform wall; see the version-SoT memory.)
- **Gotchas hit + fixed during bring-up:** (1) GameCI v4 Personal license needs **three** secrets —
  `UNITY_LICENSE` (.ulf) + `UNITY_EMAIL` + `UNITY_PASSWORD` (a Google-SSO Unity account must set a password via
  "Forgot password" first). (2) `ClientUpdater.cs` references Velopack in the **player** build (`#if !UNITY_EDITOR`),
  so CI must run `sync-velopack-libs.ps1` too (build-client.ps1 doesn't — it relies on the gitignored Plugins
  DLLs persisting locally). (3) The ~6 GB GameCI image + restored Library cache overflow the runner disk →
  a "Free disk space" step (rm android/ghc/dotnet/CodeQL) is required. (4) The publish step is guarded with
  `if: startsWith(github.ref,'refs/tags/')` so manual dispatch builds the artifact without erroring on the
  missing tag.
- **Job 1 (ubuntu):** `setup-dotnet` → `sync-client-libs.ps1` + `publish-local-server.ps1 -Runtime win-x64`
  (the gitignored `StreamingAssets`/`Plugins` are generated content, so they must be produced first) → cache
  `client/Library` → **GameCI** `game-ci/unity-builder@v4` builds the `StandaloneWindows64` player via the
  existing `BuildScript.BuildWindows` (`-buildOut` passed through `customParameters`). Mono backend
  cross-compiles the Windows player from Linux — **switching to IL2CPP would break this** (needs a self-hosted
  Windows runner). Player uploaded as an artifact.
- **Job 2 (windows):** builds the self-contained WinForms loading-splash launcher (Windows-only SDK), merges it
  with the player, zips, and attaches to the release via `softprops/action-gh-release@v2`.
- **One-time setup:** obtain the Unity Personal `.ulf` via the GameCI activation flow
  (https://game.ci/docs/github/activation), then add its full contents as the repo secret `UNITY_LICENSE`.

### ★ Unity-client tests against the real game server — ✅ infra + fully validated (2026-06-20, Unity-built)
The Unity client is now testable against the **real** authoritative server, not a mock, at three tiers. The
Unity-free client logic (`NetworkClient`, `ClientWorld`) was extracted into a new netstandard2.1 assembly
`src/BlocksBeyondTheStars.Client.Core/` so the *same* code runs in the Unity player and in headless tests; the
Unity layer keeps `UnityEngine.Vector3` call-sites working via `NetworkClientUnityExtensions` (the Core
`NetworkClient` speaks Shared's `Vector3f`).
- **Tier 1 — `tests/BlocksBeyondTheStars.Client.Tests/`** (xUnit, net8.0): the real `NetworkClient` drives the
  real in-process `GameServer` over a `LoopbackLink` (no Unity, no sockets) via `ClientServerHarness` — join,
  chunk streaming, mine→block-change+inventory, craft success, craft-rejected. **5 tests green.**
- **Tier 1.5 — `client/Assets/Tests/EditMode/`**: in-Editor unit tests — `ClientWorld` round-trips (Unity-safe
  logic). **3 tests green.**
- **Tier 2 — `client/Assets/Tests/PlayMode/`**: a `[UnityTest]` launches the **published** server exe over
  loopback UDP and drives a real `NetworkClient` through connect→join→chunks (the shipping path), plus the real
  `BlockTextureAtlas` build. **2 tests green.** (Atlas runs in PlayMode — its builder calls `Object.Destroy`,
  illegal in edit mode; and the PlayMode asmdef must be `includePlatforms: []` or Unity treats it as EditMode.)
- **Runner**: `scripts/run-tests.ps1 -Suites Dotnet,ClientCore,UnityEdit,UnityPlay,All` (+ `-Coverage`) —
  selectable, defaults to the fast .NET suites; the Unity suites are opt-in. `com.unity.test-framework` added;
  `sync-client-libs.ps1` + the client asmdef now carry `Client.Core.dll`. Docs: new
  `docs/developer/CLIENT_TESTING.md` (+ AGENTS.md, DEVELOPER.md, README, index). **All suites green
  (683/5/3/2); full Windows player build verified with `Client.Core.dll` bundled (no CS0246).** CI intentionally
  deferred (local only).

### ★ Block-selection outline no longer flickers in mid-air — ✅ client (2026-06-20, NEEDS Unity build)
The black aim/mining wireframe box flickered in the air while walking (noticed moving backwards): it was placed
by a forward `Physics.Raycast`, so it snapped onto whatever collider the ray hit — a creature / parked ship /
speeder hovering in the air, a just-mined cell whose `MeshCollider` had not re-baked yet, or it blinked off for a
frame while a chunk collider was mid-rebuild (the same collider-desync the drill already avoids, B32). `MiningFx`
now targets the cell with an Amanatides & Woo voxel march mirroring `PlayerController.AimTarget` (solid world
block **or** a parked-ship cell, fluids passed through), reading the authoritative block data directly — so the
box highlights exactly the block a mine/place click would hit and never sits where there is no targetable block.
Client-only; no server / NetCodec change; .NET tests unaffected. NEEDS Unity build.

### ★ Micro-fauna ("Kleinstlebewesen"): tiny ambient critters that make planets feel alive — ✅ client + art (2026-06-20, NEEDS Unity build)
A new category of *tiny, purely-cosmetic* creatures populates planets: fluttering **butterflies / moths /
fireflies / bees / dragonflies / flies**, crawling **beetles / ants / caterpillars / worms / snails / spiders**,
schooling **small fish / tadpoles / water-beetles / water-striders**, and **glow-worms** clinging to cave
ceilings. They never attack and are entirely **client-side** — no server, netcode, save or test impact (the same
philosophy as the ambient dust motes); each client renders its own and nothing is synced. No sound (by design).
- **Render** — `MicroFaunaView` (rigged beside `AmbientParticles` in `WorldRig`) keeps a pooled set of
  camera-facing sprite billboards around the player on one shared atlas material (cheap batching). Sprites come
  from one runtime atlas (`MicroFaunaAtlas`) built from generated `microfauna_*.bytes`, with a procedurally
  painted silhouette fallback so it works even without art. Alpha-blended for most kinds, additive glow for
  fireflies / glow-worms (a sprite glow, **not** a real light). Per-instance colour tint for variety.
- **Behaviour** — three light motion models: airborne flutter (bob + weave + perch), surface crawl
  (ground-height following, foraging pauses, turns at ledges) and in-water schooling (cohesion, stays inside the
  water volume). Grouped kinds spawn as swarms/schools that cohere; others appear singly.
- **Gating** — biome richness + day/night (fireflies/moths by night, butterflies/bees by day) + habitat
  (surface vs. nearby water vs. cave) drive what spawns where. Suppressed in space / stations / airless worlds;
  thinned out under reduced-effects. Underground shows cave **glow-worms** instead of surface critters.
- **Art** — 17 transparent pixel-art sprites via new `tools/ai-assets/gen_microfauna.py` +
  `bundle_textures.py --microfauna`. Client-only feature; .NET tests unaffected (683 green). NEEDS Unity build.

### ★ "Du bist gestorben" / "Das Schiff wurde zerstört" confirmation screen before respawn — ✅ client + locale (2026-06-19, NEEDS Unity build)
After dying on a planet (or having the ship destroyed in space), the death/explosion animation now plays first,
then a dark overlay with a title and a single **Weiter** button appears; the player / ship only re-appears once
it is clicked. Entirely client-side — the server still respawns instantly + authoritatively (no server, NetCodec
or message change); the client just holds the existing respawn presentation behind a new
`GameBootstrap.AwaitingRespawnConfirm` gate.
- **Prompt** — new `RespawnPrompt` (rigged next to `DeathFx` in `WorldRig`): subscribes to the same
  `RespawnNotice{Died}` / `SpaceClosed{ShipDisabled}` events, sets the gate immediately on receipt, and after a
  short delay (so the red death wash / orange explosion glare is seen) fades in a full-screen modal with the
  localized title + Weiter button. Clicking releases the gate and re-locks the cursor.
- **Hold** — while the gate is set, `PlayerController` keeps the on-foot settle frozen and skips the world reveal
  (player stays at the heal-tank), `SpaceView` holds the landing-fly-away teardown (explosion + hidden hull stay
  on screen) and freezes flight input, and `GameMenu` ignores the Tab toggle — only Weiter proceeds. The ship
  then recovers "wie bisher" (whole or as a wreck per `KeepShipOnDeath`).
- Bilingual DE/EN strings (`ui.death.title`, `ui.death.ship_title`, `ui.death.continue`). 683 .NET tests green
  (UI is Unity-only; verified by the player build).

### ★ Craftable camera: HUD-free photos saved to disk + an in-game Photos gallery with editable notes — ✅ client + data (2026-06-19, libs synced + Unity-built)
A new **camera** tool item lets players photograph the world and keep an annotated album — entirely client-side
(no server round-trip, no codec change); only crafting goes through the normal server-validated path.
- **Item + recipe** — `camera` item (`tool`/`gadget`, no energy) crafted at the workshop behind a cheap new
  `camera` blueprint (Tools, knowledgeCost 2). Full-colour OpenAI inventory icon (`item_camera.png`).
- **Capture** — right-clicking the held camera runs `CameraTool`: it renders a throwaway clone of the view
  camera (which has neither the visor composite nor the screen-space HUD) into a RenderTexture and JPG-encodes
  it, so the shot is exactly the view **without the HUD**. Pipeline-agnostic (Built-in + URP), short anti-spam
  cooldown, an ElevenLabs **shutter** cue + a brief white flash. Saved under
  `…/LocalLow/<company>/<product>/Photos/world_<seed>/photo_<utc>.jpg` (one folder per world, keyed by seed).
- **Gallery** — new **Photos** menu tab (`CraftingTechShipUI`): a thumbnail list (newest first) + a detail pane
  with the full preview, capture time, an **editable note** (saved per photo) and delete. Backed by `PhotoStore`
  (per-world `index.json` sidecar of `{file, takenUtc, note}`; lazy, cached texture loads).
- **Engine fix** — added the `imageconversion` + `screencapture` built-in modules to the Unity package manifest;
  they were missing, which also means this unbreaks the previously-uncompilable `/bump` screenshot code in
  `ChatUi`. Bilingual DE/EN strings (`item.camera.*`, `blueprint.camera.*`, `ui.photos.*`, `ui.tab.photos`).
  683 .NET tests green (capture/gallery are Unity-only; verified by the player build).

### ★ /bump bug reports now carry a screenshot + land in the repo when run from source — ✅ server + client + tests (2026-06-19, libs synced, NEEDS Unity build)
The `/bump <text>` debug command already persisted a rich JSON snapshot (player/env/ship/surroundings + 30 s
history); it gained a screenshot and a smarter output location:
- **Screenshot** — new `BumpReport` message (NetCodec id 168: description + JPG `byte[]`, ReliableOrdered).
  `ChatUi` intercepts `/bump`, captures the screen at end-of-frame with the **chat overlay hidden** (HUD kept),
  downscales to ≤1600 px and JPG-encodes (q70), then `NetworkClient.SendBumpReport`. If capture fails it falls
  back to the old text `/bump` so a JSON-only snapshot is still written. Server `HandleBumpReport` writes the
  `.jpg` beside the `.json` (same stem, referenced via a `screenshot` field), caps images at 2 MB, rate-limits
  like chat.
- **Output location** — new pure `BugReportPaths.Resolve`: when the server runs from **inside the repo**
  (client build under `./client/Build/Windows/…` or the Unity Editor → its exe sits in the working tree), it
  walks up from `AppContext.BaseDirectory` for a `.git`/`.sln` marker and writes reports to
  `<repoRoot>/bugreports/server/`; otherwise it keeps the old per-world `<world>/bumps/` (under %appdata%).
  Filenames are now `bump_<world>_<utc>_<NNN>.{json,jpg}` (collision-safe in the shared dev folder).
  `/bugreports/` added to `.gitignore`.
- New `BugReportPathsTests` (repo detection by `.git` + `.sln`, both branches) and an extended `BumpTests`
  (screenshot variant writes the JPG + references it in the JSON). 683 tests green.

**Follow-up (2026-06-19, server-only, NO client build):** the snapshot is now complete across contexts.
- **Inventory + rations** of the reporter (slot/item/count) — was missing entirely.
- **Context flags** `currentLocation`, `inEva`, `aboveAtmosphere`, `inSpace`, `selectedHotbarSlot` so
  on-planet / ship-interior / station / space can be told apart.
- **Space-aware:** when the reporter is flying, `p.Position` is stale, so the bump now writes a `space`
  block — the real `instance.ShipPosition` (+ yaw/EVA) and the 40 nearest space entities (asteroids/
  hostiles) from their space instance — and skips the (misleading) planet block/creature scan.
- **Flora/terrain census:** a compact block-type→count histogram over a wider ±12 box (`surroundingsCensus`)
  captures voxel flora (trees/leaves/planted) + terrain composition without dumping every cell. (Client-side
  billboard undergrowth is procedural, not server voxels, so it inherently can't appear.) Still 683 green.

### ★ Tree-trunk bark colour: per-world random, guaranteed darker than the leaves — ✅ client (2026-06-19, libs synced, NEEDS Unity build)
Trunks (`wood_log`) were never tinted — they always showed the plain bark texture while leaves recolour per
(species × world), so a brown/amber leaf could read close to the bark. Now the trunk also gets a **per-world
random** colour, but one that can never be confused with the foliage:
- **`FloraTints.ForWood(seed, location)`** (Shared, pure fn like `For`) — full-circle random hue, but forced
  into a **DARK band** (sat 0.35–0.70, **val 0.30–0.58**). Leaves always sit bright (val 0.85–1.15), so the
  trunk is guaranteed clearly darker than **any** leaf species on the world regardless of hue (one bark colour
  must contrast up to three crown blocks at once → brightness separation, not hue pairing). Orbit/other bodies
  agree with zero traffic; no server/codec/`WorldEnvironment` change.
- **Client wiring** — `GameBootstrap.RebuildFloraTints` adds `wood_log` (→ `ForWood`) to the per-block tint map
  **and** closes a pre-existing gap: `pine_needles`/`palm_frond` now get a per-species leaf hue too (were
  falling back to the planet's uniform hue). `ChunkMesher` adds `IsWoodBlock` + tint **mode 4** (cube + shaped
  paths); `BlockAtlas.shader` gets a bark branch checked **before** the dye branch (mode 4 > the dye `>2.5`
  cutoff) with a gentler blend than leaves so the bark grain survives; black tint (ship mesh / no resolver) →
  natural bark.
- New `WoodTintTests` (determinism, per-world variety, dark-band, and the "bark always clearly darker than
  every leaf on the same world" guarantee). 678 tests green.

### ★ Backlog reconciled against the memory audit — deferred plans + missing entries logged (2026-06-19)
A full pass over the project memory notes against this file surfaced work that had been analysed/decided or
even shipped but never written into the backlog. Logged here so nothing is lost. (Pure lessons/reference and
already-tracked features are not repeated; a few stale memory notes — music 20-track, sci-fi-look "not
implemented", movement-overhaul body — are TODO-correct and only need memory cleanup, not a TODO entry.)

**Deferred plans — analysed/decided (all now ✅ implemented as of 2026-06-19):**
- **Per-world seeded gravity** — size-class base + jitter (f≈0.35–1.6) scaling jump (always ≥1 block via a
  targetH floor), walk speed (6/√f), jetpack thrust and fall damage. Client-side `PlayerController` scaling,
  no save migration; mirror the `SkyColor` server→client wiring. *(IMPLEMENTED 2026-06-19, 671 tests, libs
  synced, NEEDS Unity build: `WorldEnvironment.GravityFactor` seeded per world in `GameServerWeather`
  (`GravityFor` band by `WorldSizeClass`); `PlayerController.RecomputeGravity` scales gravity/jump/walk/
  jetpack-net-thrust/fall-threshold; HUD shows "0.6 g" via `ui.hud.gravity`)*
- **Optional playtime display + break reminder** — client realtime session timer + per-save cumulative
  playtime (`WorldMetadata`, shown in the saves list) + a repeating Vega "take a break" reminder; 3
  `ClientSettings`/`UiSettings` toggles. *(IMPLEMENTED 2026-06-19, 674 tests, libs synced, NEEDS Unity build:
  server accumulates `WorldMetadata.CumulativePlaytimeSeconds` in `GameServer.Tick` only while a player is
  joined + mirrors it into a `world.meta.json` sidecar (client has no SQLite, reads it in the save picker);
  `JoinAccepted` carries the total; `HudUi` shows session + total (toggle `ShowSessionTime`); `VegaPanel`
  fires an escalating, repeating break reminder client-side every `ReminderMinutes` (NOT via ShipAiLine, so it
  stays out of the Story Log); `UiSettings` "Comfort & wellbeing" section; new `ui.hud.playtime*`/
  `ui.settings.comfort`+toggles/`ui.reminder.break*` locale keys (de+en); new `PlaytimeTests`)*
- **Center the on-foot controls hint under the hotbar** — `HudUi.cs` hint anchor `MiddleLeft`→`MiddleCenter`
  (the space HUD is already centered). One-line UI fix. *(✅ DONE 2026-06-19 — `_hint` is built at
  `TextAnchor.MiddleCenter`, `HudUi.cs:377`.)*
- **Auto language detection (game side)** — pick DE/EN from the OS on first run in `ClientSettings.Load`
  (`Application.systemLanguage`, first-run only, no retroactive flip). The launcher already does this; only the
  game is open. *(✅ DONE 2026-06-19 — `ClientSettings.cs` reads `systemLanguage` on first run.)*
- **Localize the launcher splash** — read the "Loading…"/"Lädt…" string (`ui.loading.title`) from the game's
  own locale file driven by the in-game chosen language (`client_settings.json`), instead of the hardcoded
  `SplashForm.cs` text / OS culture. Pairs with auto-language-detection. *(✅ DONE 2026-06-19 —
  `SplashLocalization.cs` drives the launcher splash string from the game locale.)*

**Shipped but never logged here:**
- **Tab availability dimming** — ✅ code (2026-06-19, NEEDS Unity build): menu tabs whose context isn't met
  (Map=aboard, Crafting=workshop, Tech=lab, Ship=console) are dimmed-but-clickable via `IsTabAvailable` +
  `BuildHeader`; Map travel buttons disabled when not aboard (client-only gate; server still allows on-foot
  travel). `ui.map.need_ship` key; ContentTests 10/10.
- **Landing fly-away animation** — ✅ committed (`a6da709`, fixed `fefc6e5`): the "ship sinks down" landing
  replaced with a fly-toward-planet shrink in `SpaceView` (`BeginLandingFlyAway` + landing branch of
  `UpdateSequence`, re-timed fade). The follow-up fix corrects the "flew straight down" report: the target body
  resolves to `_choosePadBody`, else the landable most directly ahead of the nose (never defaults to down); the
  ship orients its nose onto the body; and the camera now holds its position but **turns to follow** the receding
  ship so it stays centred while it shrinks toward the planet/asteroid ahead. LookRotation up-hints guard the
  near-vertical-dive flip for both ship and camera. Build-verified.

**Small follow-ups surfaced:**
- **Station flora see-through — P4 hardening — ✅ DONE.** The residual grass-into-space is closed: the structure
  stamp path (`FromTemplate` / `StampPlayerStation`) is hardened with flora-enclosure checks + ledge-hole closure,
  and persisted pre-fix station worlds are re-cleared.
- **Graphics quick-wins — commit/playtest status — ✅ DONE.** The URP MSAA/HDR/shadow/SSAO tuning is committed +
  playtested (SSAO After-Opaque behaviour confirmed; dye/coloured-lights still correct).
- **River floating-water fix** — ✅ committed (`0ea6175`, server-only, 648 tests): rivers carved flush with no
  slope gate via shared `SurfaceSlope` + `RiverDepthAt` (previously only folded under the water-life entry).

### ★ Cloud overhaul: varied/realistic shapes + per-world cloud colour that stands out from the sky — ✅ client+server (2026-06-19)
Surface clouds were four repeated Gaussian-blob billboards drifting in lockstep, flat (no volume), and their
colour varied only by planet **type** — every ice world had identical clouds, while the sky hue is seeded
per individual world. Reworked shape, lighting and colour:
- **Shapes/variety** (`Clouds.cs`) — 12 noise-broken textures across 3 archetypes (puffy **cumulus**, broad flat
  **stratus**, thin wispy **cirrus**) with fractal (fBm) edges + internal density; shuffled no-repeat assignment;
  varied size **and** aspect. Two-layer dome (low + high toward the zenith), **per-cloud** drift (near clouds
  faster), and a lifecycle (slow swell/fade + bob) so clouds form and dissipate instead of riding a rigid ring.
  Weather drives storm towers (low clouds grow tall) and hides the high cirrus in storms.
- **Directional sun lighting** (`Cloud.shader`) — opt-in `_SunShade`/`_Bulge`/`_CloudSunDir`/`_ShadeColor`: each
  puff fakes a volume normal from its UV and is lit from the real sun direction (`_Sc_SunDir`) → a bright and a
  shadowed side, warm at sunset. No extra passes, stays depth-texture-free; existing flat users (atmosphere haze)
  unchanged via the `_SunShade=0` default.
- **Per-world cloud colour** (`GameServerWeather.CloudTint`, the colour analogue of `SkyHue`) — the type base
  colour gets a per-world seeded jitter (`LocationId ^ Seed`, salted so it doesn't track the sky), so two same-type
  worlds differ. **Contrast guarantee:** if the tint lands within ~64 RGB of THIS world's sky colour it is pushed
  apart in brightness (bright sky → greyer/darker cloud, dark sky → brighter) so **clouds always stand out from the
  sky, including colour-wise**. No client change for the surface (reads `env.CloudColor`); 671 tests stay green.
- **Orbit cloud shell** (`SpaceView`) — fBm per-body shell texture (seamless, per-body seed) + day/night terminator
  shading (real sphere normal × the body's sun dir) + a per-body colour jitter so planets differ from space too.

### ★ Temperature screen FX: cold frost + rain-on-glass beads + hot-world heat shimmer — ✅ client (2026-06-19, Unity-built, pushed `01ce012`+`b3255ec`)
The display now reacts to the world's air. All client-side, driven by the authoritative `WorldEnvironment.Temperature`
+ atmosphere flags already broadcast every 5 s (no server/network change), atmosphere-gated like the auroras
(`!SpaceSky`, sentinel `−999` guarded, `ExposedToSky`, not in space/menu):
- **Frost** (`WeatherFx`) — on cold AIR worlds, ice creeps in from the screen edges, alpha eased from 0 °C (none) to
  −25 °C (full, capped). Drawn as a tinted edge-vignette: AI texture `Resources/frost_overlay.png` (generated, alpha
  rebuilt to a clear centre) with a procedural crystalline fallback so it works even before art lands.
- **Rain-on-glass beads** (`WeatherFx`) — during **real rain only** (never ashfall/"lava rain", snow, hail or
  sandstorm) water beads cling to the display, shimmer, then run down leaving a faint streak; storms bead bigger and
  run faster. Procedural glass-bead sprite (faint body + dark rim + highlight), `Resources/rain_droplet` override
  supported. Pure IMGUI — robust in both pipelines.
- **Heat shimmer** (`HeatShimmer` + `HeatHaze.shader`) — on hot worlds (>38 °C, full ≥75 °C) a camera-parented
  full-screen quad re-displays the scene with a faint rising UV warp, **depth-faded so only the far field boils**
  (samples `_CameraOpaqueTexture` + `_CameraDepthTexture`, both on in the URP asset). Global `_HeatAmp` driven from
  temperature; the quad switches off when there's no heat so normal worlds pay nothing. URP only (Built-in subshader
  is a no-op). New shader registered in the always-included list.
  - **Verified the gating holds:** plenty of worlds get hot enough — lava base **115 °C** (always full), ashen 70,
    desert 44, salt_flats 40, savanna 32 on hot seeds. AND the distance fade is **tied to the actual fog/view
    distance** (`_Sc_Fog` global), not a fixed metre count — the client default render distance is only
    `ViewDistanceChunks=2` → ~32 m (fog by ~42 m), so a hard 56 m fade had buried the shimmer behind the fog wall
    (`b3255ec`). Now it peaks where terrain is still visible at any view-distance setting. General rule for future
    distance-keyed screen effects: scale to `_Sc_Fog`/renderDist, never hard metres.

### ★ Waterfalls: mist spray + visible streaming cascade where water falls > 3 blocks — ✅ client (2026-06-19, Unity-built)
A tall drop of water looked flat and dead: the column's side faces were culled and there was no spray. All
client-side, inferred from block ids (no server/network change, works on old saves like the water-surface look):
- **Falling-water detection** (`WaterfallDetect`) — a cell reads as *falling* when it's water, has water directly
  above (fed from above) and ≥2 horizontal neighbours open to air (a thin hanging stream, not a filled pool).
  `ImpactDrop` measures the drop height above a landing cell.
- **Mist at the impact** (`WaterfallMistView`, mirrors `GeyserView`, wired in `WorldRig`) — scans around the player,
  and at every drop **taller than three blocks** keeps a continuous puff of pooled pale-spray particles rising and
  arcing off the landing point (capped, gravity-arced, shrink-fade; build-safe `Unlit/Color`). Lava left alone.
- **Visible cascade** (`ChunkMesher`) — falling-water flanks are no longer culled and are tagged TEXCOORD2 **mode 4**;
  the transparent shader (`BlockAtlasTransparent`, both URP + Built-in passes) streaks them downward (procedural on
  world height + `_Time`), and the screen-space depth/refraction/SSR block is skipped for mode 4 (it would blue out
  the vertical sheet). No new shader (existing file edited → no always-included list change).

### ★ Settlements overhauled: many per world, hospitality/size-scaled, collision-free + weather gated on atmosphere — ✅ server (2026-06-19, libs synced; client build only needed for the new lava look)
Villages/cities were near-invisible: exactly **one** settlement per planet, anchored at a fixed pad offset with no
water/flatness check and no terrain carve (so it sank into hills / drowned), and compounded gating made cities ~5%/planet.
Reworked end-to-end (server + shared + worldgen + data):
- **0..N settlements per world** — `WorldManager.SettlementInstance` list replaces the single-settlement fields;
  every consumer (NPCs, doors, missions, NPC-memory keys, greetings, map POIs, mine-protection) now iterates the
  list. Single-settlement public API kept as "primary (first inhabited)" shims for back-compat.
- **Count = hospitability × world size × base density × frequency × per-world character roll** (`GameServerSettlements`):
  only worlds **with an atmosphere** (airless ⇒ 0); a `Hospitability()` score (atmosphere/fauna/climate/water); a
  `sizeFactor` from circumference (a big planet holds more than a small moon); and an overdispersed character
  multiplier (lonely ×0 … boom ×2.6) for **high variance** — some worlds empty, some crowded.
- **Harsher worlds ⇒ more ruins** — per-settlement ruin chance scales `0.15 + (1−H)·0.7`; tier roll skews to
  town/city on liveable worlds, hamlet/village on harsh ones.
- **Collision-free placement** — a candidate-scan allocator keeps every settlement clear of all **landing pads**,
  the **ship-wreck** zone, **vaults**, **data cubes** and **each other** (wrap-aware footprint reservation); wreck/
  vault/cube stampers also avoid settlements (`OverlapsAnySettlement`).
- **No more burial/drowning** — each settlement is placed on a **dry, reasonably flat** spot, its footprint is
  **carved clear** above the foundation, then a flat foundation slab is laid.
- **Floating-island settlements** — on `FloatingIslands` worlds a share of settlements sit on the **sky islands**
  (new shared `WorldGenerator.FloatingIslandBand/Top`, single source for chunk-gen + placement).
- **Weather gated on atmosphere** (`InitWeather`) — clouds/changing-weather/fog now require an atmosphere, not just
  a non-space sky. **lava** was retyped `atmosphere: "toxic"` (keeps its ash storms, `waterAbundance: 0` so it still
  pools lava) and **crystal** (airless) now correctly gets no clouds/weather/fog.
- New `SettlementOverhaulTests` (multi-settlement, pad/inter-settlement no-overlap, ruin skew, island, weather gating)
  + retargeted loot/door/respawn/flora tests. **670/670 green.** Server/shared/worldgen only; client libs synced.
  A Unity build is only needed for the new lava (toxic) sky/weather visuals.

---

### ★ Main-menu backdrop now reflects the real in-game space look — ✅ code (2026-06-19, NEEDS Unity build)
The shell/menu attract scene (`MenuBackground`) was a standalone hand-built scene (420 cube "stars", primitive cube
ship, no nebula/sun/post) that no longer matched the game. It now shares the **same** rendering systems the live
space view uses:
- **Real starfield + nebula:** the actual `Starfield` (1500-star twinkling dome) and `NebulaField` (coloured gas)
  now run in a new **menu mode** — a `MenuBrightness ≥ 0` (and `MenuSeed` for the nebula) lets them render at a
  fixed strength **without a live `Game`** (they previously early-returned on `Game == null`).
- **Sun + god-rays:** a glowing `SunGlow` disc + additive `SunRays` fan, with the radial textures extracted into a
  new game-independent `SkyVisuals` helper (`Sky` now delegates to it — no duplication).
- **Real voxel ship:** the menu builds the block atlas + chunk materials (same recipe as `GameBootstrap`) from the
  already-loaded `AppShell.Content`, authors a compact real fighter as a `SpaceShipDesign` (iron_wall hull, glass
  canopy, cyan-glow engine nozzles) and meshes it through `ShipMeshBuilder` — so the menu shows the actual atlas-
  textured, hull-painted voxel ship (with a thruster particle plume). Falls back to the old cube ship if the atlas
  isn't available. Space-sky shader globals (`_Sc_Light` white, `_Sc_SunDir`, grade/fog off) are set so the atlas
  shader lights it (no live `Sky`).
- **Cinematic post:** the menu camera gets the same `UrpScenePost` stack (bloom + ACES tonemap + vignette + lens
  flare; URP post enabled on the camera), or `PostFx` under Built-in RP.
- Files: `MenuBackground.cs` (rewrite, builds in `Start` so `AppShell` can inject `Shell`), `SkyVisuals.cs` (new),
  `Sky.cs` (delegate the two generators), `Starfield.cs` + `NebulaField.cs` (menu mode), `ShipMeshBuilder.cs`
  (game-less `BuildVoxelShip` overload; existing `GameBootstrap` overload forwards), `AppShell.cs` (inject `Shell`).
  Client-only; no `.NET`/codec change. NEEDS a Unity client build to verify.

### ★ Creatures sleep + wake when you near or hit them — ✅ code (2026-06-19, NEEDS Unity build)
Off-phase fauna already rested in place (each species is Diurnal/Nocturnal/Crepuscular/Cathemeral; `SpeciesActive`
gates movement). That rest was purely clock-based — proximity and attacks did nothing. Now a sleeping creature
**rouses**: a new `CombatEntity.AwakeOverrideTimer` (server-only) is set when a player comes within
`CreatureWakeDistance` (4 blocks, checked in `MoveCreatures` before the sleep gate) **or** when the creature is hit
(set in the attack path for *any* creature, before `ProvokeCreature`, so even a non-retaliating skittish one wakes).
While the override runs (9 s) it ignores the day/night gate and falls through to normal **temperament-driven**
behaviour — skittish flees, hunters seek, others wander — then settles back to sleep. `NetCreature.Asleep` now
reflects the override (a roused sleeper is not "asleep"). Client `CreatureView` finally uses `Asleep`: a resting
**breathing bob** + floating **"z z z"** (billboarded, distance-faded) and a quiet, low **snore** instead of the
full idle call. Companions never sleep (unchanged).
- Files: `GameServerCreatures.cs` (consts, decay, wake-on-proximity gate, `ToNetCreature`), `GameServerSpaceCombat.cs`
  (`AwakeOverrideTimer` field), `GameServerEnemies.cs` (wake-on-hit), `CreatureView.cs` (sleep pose/Zzz/snore).
  No codec/`Register` change (`Asleep` already on the wire). +3 tests in `CreatureTests` (wake-on-near, wake-on-hit,
  re-sleep decay); 663 tests green.

### ★ Landing-pad chooser: marker fix + day/night terminator — ✅ code (2026-06-19, NEEDS Unity build)
Three fixes to the pre-landing pad-chooser map (`SpaceView.ShowLandMap`):
- **Marker "1" no longer hangs off the left.** Markers are now clamped to stay **fully inside** the map rect
  (`Clamp(mx, pad, pad+mapW-marker)` + the same for `my`), not centred-then-clamped. The home pad is always at
  longitude 0 (the strip's left seam), so its marker used to sit half outside the map's left edge.
- **Day/night terminator drawn on the map.** `DrawDayNightOverlay` shades the night half of the equirect strip
  (1–2 rects, longitude-wrap aware) and draws warm (sunrise) + indigo (sunset) terminator lines. A pad shown on
  the dark band = a night-side touchdown. Non-interactive overlay (markers stay clickable on top).
- **Landing day/night matches the spot — now visible + accurate.** It already worked (`Sky` uses
  `LocalTimeOfDay = global + playerX/circ`, and landing places you at `pad.CenterX`); since `InitWeather` resets a
  world to `InitialDayFraction=0.35` on entry, arrival time is deterministic. The server now sends that arrival
  day fraction in `LandingPadList.TimeOfDay` (live time for the active body, else the 0.35 default) so the map's
  terminator is drawn for the body you're actually landing on, not the stale orbit-view environment.
- Files: `Messages/LandingPadMessages.cs` (+`TimeOfDay`), `GameServerSpace.cs` (`BodyArrivalTimeOfDay`, both pad
  senders), `GameServerWeather.cs` (`InitialDayFraction` const), `GameBootstrap.cs` (`LandingPadsTimeOfDay`),
  `SpaceView.cs`. No codec/`Register` change (new field only). 660 tests green; libs synced.

### ★ Sun-lit celestial-body phases + per-system orbital periods — ✅ code (2026-06-18, NEEDS Unity build)
Analysis+plan: `[memory] celestial-phases-and-orbits-analysis`. Nearby same-system bodies in the sky now show a
real **terminator/crescent/gibbous/full phase** (like the Earth's moon), and each system has its own **orbital
rhythm** that makes the phase drift visibly within a play session. Vantage-based sizing (planet→small moons→tinier
asteroids; moon→huge planet; asteroid→huge nearby bodies) was already implemented and is unchanged.

- **Server/Shared (authoritative).** `CelestialBody`/`NetBody` gain `OrbitPeriodDays` (signed; planet 6–40d /
  moon 0.4–2.5d / asteroid 0.6–3d, ~20% retrograde) + `ParentId` (moon→planet). Seeded in `UniverseGenerator` via
  the **separate `Hash01`** stream (not `rng`) so existing universes stay byte-identical. `SystemX/Z` remain the
  t=0 reference the flight/travel/docking layer uses — the orbit is a purely visual/phase driver. `WorldEnvironment`
  gains `SystemTimeDays` (monotonic, fixed 600s reference day) advanced in `GameServerWeather.TickWeather` — the
  shared clock. No codec/`Register` change (contractless MessagePack, new fields only).
- **Shader.** New `BlocksBeyondTheStars/SkyBodyPhase` (both pipelines) lights a sphere from a per-material
  `_PhaseSunDir` → the phase emerges; soft terminator + dim earthshine night-side floor + limb darkening. Added to
  `m_AlwaysIncludedShaders` (code-loaded via `Shader.Find`).
- **Surface sky (`SkyBodiesView`).** Phase-shaded, fed the local sun's sky direction (`_Sc_SunDir`). The per-pair
  hashed sky-cycle is replaced by a coherent clock: bodies cross ~once/day (rotation) and drift by their orbital
  rate → rise a little later each day and their phase waxes/wanes once per `OrbitPeriodDays` (visible per session).
  Bodies are now textured with their **real baked world map** (`WorldMinimap.Bake`, cached + shared with the space
  view) + a light star-hue wash — so a body reads the same from the surface as from orbit (seas/ground/vegetation);
  unknown types fall back to the flat data-driven `GroundColor`. Mipmaps collapse a tiny disc to its average colour.
- **Space view (`SpaceView`).** Planets/moons lit from the **true** direction to the system star (`-currentPos·scale`
  in the scene); the visible sun billboard is aimed the same way so phases agree. Body **positions** stay static
  within a visit (no travel/landing desync); phase is the correct snapshot. Live orbital position drift in the
  space view is a possible follow-up.
- **Tests.** New `UniverseTests.OrbitParameters_AreDeterministic_InBandPerKind_AndParentedCorrectly`; byte-identity
  + full suite green (**660**). `UniverseGenerator.cs`, `Galaxy.cs`, `Messages.cs`, `GameServer.cs`,
  `GameServerWeather.cs`, `SkyBodyPhase.shader`, `SkyBodiesView.cs`, `SpaceView.cs`.

### ★ Multiple space stations per system — ✅ worldgen-only + test (2026-06-18, NO Unity build needed)
A system that rolls a station (per the `SpaceStations` frequency gate) now gets **1–3** instead of always one.
A second station is rare (~25%) and a third very rare (~7.5%); the count is capped at the system's planet count.
Each station hangs over a **distinct** planet (deterministic hash-shuffle, never two on one) at `StationOrbit=500`
just beyond the planet's clearance, and is named `"<planet> Station"` so it's attributable. Count + planet picks
use the separate `Hash01` stream (not `rng`), so wrecks/planets stay byte-identical for existing universes — only
stations change. First station keeps the legacy `-st` id; extras are `-st1`/`-st2`. Runtime already supported
multiple stations per system (`GameServerSpaceStations` iterates all `SpaceStation` bodies). `UniverseGenerator.cs`;
new `UniverseTests.Stations_AreOnePerThreeMax_OverDistinctPlanets_NamedAfterThem`.

### ★ Server hardening + optimization pass — ✅ server-only + tests (2026-06-18, NO Unity build needed)
Analysis+plan: `[memory] server-hardening-and-optimization-plan`. Four critical fixes + perf + anti-cheat from a
full server code analysis. All server/shared-side; the only client-facing change is the `NetCodec` decode guard
(synced via `sync-client-libs`).

- **K1 — malformed-packet DoS closed.** `NetCodec.Decode` now swallows a corrupt MessagePack body (returns null
  instead of throwing); `GameServer.OnPayload` wraps the dispatch in try/catch and logs+drops a throwing handler.
  A single bad packet could previously kill the single-threaded tick (whole-server freeze). Test in `NetworkingTests`.
- **K3 — unbounded block-Y DoS closed.** New build-height band (`MinBuildY=-512 … MaxBuildY=1024`, far wider than
  any legit build — terrain ≈64, highest atmosphere ≈320). `HandlePlace`/`HandleMine` reject out-of-band edits
  before touching the world; `StreamChunks` clamps the streamed column. Stops spoofed-position place/mine spam from
  growing RAM/disk without bound. Tests in `ServerHardeningTests`.
- **K2 — clean shutdown.** Ctrl-C now only `RequestStop()`s; the run loop drains + saves on the tick thread (no
  cross-thread `SaveAll` race against a live `Tick`). `Stop()` still saves synchronously when no run loop is active
  (tests/manual drivers).
- **K4 — bulk writes batched.** New `IWorldRepository.RunInTransaction` (reentrant, manual BEGIN/COMMIT). Station/
  settlement/space-station stamping and `SaveAll` now commit once instead of one WAL commit per voxel/row — removes
  multi-hundred-ms tick stalls. Commit/rollback tests in `ServerHardeningTests`.
- **H1 — encode-once broadcast.** `BroadcastToWorld` + presence fan-out serialize the message once and reuse the
  bytes for every recipient (was O(recipients) re-encodes; presence was O(players²)).
- **H2 — reuse per-tick target lists.** Creatures/Enemies/NPCs/Doors fill reused field lists instead of a fresh
  `Where(...).ToList()` each tick; the companion-ward `HashSet` is reused too.
- **Anti-cheat caps:** player name ≤24 chars on join; player-created missions capped (≤10/creator, ≤8 objectives+
  rewards, title/description truncated); `/bump` debug command now rate-limited like chat.
- **Deferred:** per-weapon fire-rate cooldown (M1) — backed out because it conflicts with the rapid-fire
  asteroid-mining test contract and is a gameplay-balance decision (what cadence? mining feel?). Needs a design call.

### ★ Ship-loss rule (KeepShipOnDeath) + space combat fightability — ✅ server + tests + client wiring + locale (2026-06-18, NEEDS Unity client build)
Analysis+plan: `[memory] ship-loss-setting-and-enemy-kill-analysis`. Two asks: a host/SP setting for what happens
when the player ship is destroyed in space combat (analogous to `KeepInventoryOnDeath`), and "I can't destroy
space enemies".

- **`KeepShipOnDeath` world rule (default true) now wired** (was a dead field). In `DisableShip`
  (`GameServerSpaceCombat.cs`): **true** = the existing free full-restore-to-base casual safety net; **false** =
  the lost ship is left a **WRECK** — hull stays at 0 and a scattered ~40% of non-critical hull cells are carved
  away as durable edits (`WreckShipHull`, skips station/module + medbay cells so the heal-tank respawn survives),
  the ship is re-parked on the owner's home pad, and the ship is **grounded** (new `ShipState.Downed`) until
  repaired through the **existing own-ship repair flow** (`SendShipRepairStatus` clears `Downed` once hull is full
  and no cells are missing). Launch is gated in `EnterSpace`. This reuses the repair system, so the wreck is the
  player's **exact** ship, **owner-only** to repair, and **occupies the landing pad** — all for free.
- **Setting surfaces everywhere:** `ServerRules` msg + `SendRules`; CLI `--keep-ship` (+ added the missing
  `--keep-inventory`); world-creation UI toggle (`UiWorldOptions`/`WorldCreationOptions.ToArgs`); admin live-edit
  (`SetWorldRulesIntent` + `HandleSetWorldRules`, toggle row in `CraftingTechShipUI`). en/de locale keys.
- **Why enemies couldn't be destroyed:** `WeaponAllowedAgainst` needs `SpaceCombat∈{PvE,Both}` **and**
  `ShipWeapons∉{Off,MiningOnly}` — **both default Off**, while enemy spawns are governed by separate rules, so
  worlds show enemies you can't damage. Fixes: new **`--ship-weapons` CLI** (only `--space-combat` existed); a
  **world-creation "Space combat" toggle (default on)** that sets BOTH `SpaceCombat=PvE` + `ShipWeapons=NpcsOnly`,
  so new worlds are fightable. (The reject already surfaces client-side via `LastMessage`.)
- **Tests:** `KeepShipOff_DefeatLeavesWreck_AndGroundsRelaunch`, `KeepShipOff_RepairingWreck_RestoresFlight`,
  `ApplyCommandLine_OverridesShipWeaponsAndKeepRules`; existing keep=true defeat test extended. 652 green.

### ★ Sci-fi / professional look overhaul (URP) — 🚧 in progress (2026-06-17, NEEDS Unity client build)
Pushing the render look toward stylised-clean sci-fi (analysis + roadmap: `[memory] scifi-render-look-analysis`). Phased; each phase is preset-gated and individually shippable. Voxel shaders stay custom-unlit — the wins come from screen-space post, atmosphere and VFX, not from changing block lighting.
- **Phase 0 — depth + opaque textures ✅:** `BlocksBeyondTheStarsURP.asset` `m_RequireDepthTexture`/`m_RequireOpaqueTexture` → 1 (the gate for fog/SSR/water-depth). Per-preset at runtime in `ClientSettings.Apply` (Potato/Low force both off → dependent effects early-out for free).
- **Phase 1 — post polish ✅:** `UrpScenePost` gains screen-space **lens flare** (Medium+), a cinematic **teal-orange** `ShadowsMidtonesHighlights` baseline grade, and flight-only camera **motion blur** (High+, driven by `SetMotion`). `BlockAtlas.shader` foliage passes get **AlphaToMask** (free MSAA edge AA on grass/leaves). New settings `LensFlare`/`MotionBlur`/`VolumetricFog`/`Reflections` + UI rows + en/de locale.
- **Phase 3 (water) — depth-aware water + refraction ✅ (URP path, in-game verified above + underwater):** `BlockAtlasTransparent.shader` reads scene depth → shallow→deep colour gradient (deep sea opaque/blue) + shoreline/object foam; **refraction** composites the bed from the opaque texture at a wave-distorted screen UV so the surface bends what's beneath (outputs opaque, reuses depth-`alpha` as bed-hiding). Commits 933c5ad (depth) + 1bafcd1 (refraction).
- **Phase 2 — nebula + atmosphere + ambient air ✅:** new `Nebula.shader`+`NebulaField` (coloured gas backdrop in space, additive dome), `Atmosphere.shader`+`AtmosphereDome` (additive horizon brightening + warm dawn/dusk sun scattering on planets — does NOT replace Sky's clear colour, can only brighten), and `AmbientParticles` (slow biome-tinted dust/pollen/ember motes via a Unity `ParticleSystem`). Enabled the built-in **Particle System module** in `Packages/manifest.json` (the project shipped without it — that's why all legacy FX were cube meshes; now the VFX migration can use real particles). New `Particle.shader` (additive, vertex-coloured) is the basis for that migration. All wired in `WorldRig`, registered Always-Included, **Unity-build-verified** (player builds clean).
- **Fog + per-world atmosphere + fog weather ✅ committed 933c5ad (in-game verified):** the volumetric full-screen pass DARKENED the frame (forces an intermediate target) → DISABLED (kept in-tree for a later after-post redesign). Real fog is now an **explicit in-shader distance haze in `BlockAtlas.shader`** (Unity's MixFog never engages on the unlit voxels) toward the sky colour, driven by the `_Sc_Fog` global from `Sky.cs`, **scaled to the render distance** (so it engages within the streamed terrain + masks chunk pop-in), faded out indoors. New `PlanetType.AtmosphereDensity` (seeded per world, 0 airless) → DTO → per-world haziness; new **"fog" weather** state in `GameServerWeather`.
- **Per-world sky colour ✅ committed e9d4785 (NEEDS Unity client build):** atmosphere worlds (`!SpaceSky`) no longer all share one fixed blue sky. A seeded `SkyHue()` weighted palette (blue→green→yellow→red, blue-dominant + rarer exotics) in `GameServerWeather` picks a per-world daytime sky hue from `LocationId ^ Seed`; new `SkyColor` on `WorldEnvironment`/`WorldState`; `Sky.cs` uses it as the daytime sky base (star-hue tint cut 0.5→0.2 so it reads through; night nudged toward the hue). The in-shader distance haze (`BlockAtlas.shader`, `hazeCol = _Sc_Sky`), ambient and reflections all derive from the sky colour, so they follow for free. Mirrors the `FloraTint` pipeline; 649 tests green.
- **Two darkness/blur fixes ✅ (933c5ad):** Phase 0's depth texture silently switched on **SSAO** (was "wasted", Intensity 1.2 AfterOpaque → darkened everything, worst in the cabin → 0.45) and made the **visor composite** finally render (at far-too-strong 0.6 → washed/blurred the frame → 0.22 + curvature/reflect down). Lesson recorded in `[memory] scifi-render-look-analysis`.
- **VFX migration off the cube-FX ✅ (first pass, all in-game verified):** ParticleSystem **engine-flame thruster** (24c0f9c), **explosion** fireball+sparks (fe910a8), **mining/place debris puff** (97e76b5), **weapon muzzle flash + impact/drill sparks** (2a82592), **footfall/landing/hover dust** (bd0e46e). Toolkit: `Particle`(additive) + `ParticleAlpha`(alpha) shaders; every burst is one-shot + self-destroys (stopAction=Destroy) with a Unlit-cube fallback if a shader is missing.
- **God-rays ✅ committed a05d9bd (in-game verified):** darkening-safe sun shafts — an additive ray-fan billboard at the sun (`SunRays.shader`, ZTest LEqual) occluded by foreground terrain → shafts above ridgelines; off in space/airless, fades with sun height + air density. Replaces the disabled volumetric full-screen pass (that one darkened the frame; still in-tree, inactive).
- **Water SSR ✅ committed fdde39f (in-game verified):** water-localised screen-space reflections (mirror the view ray about the wave-rippled normal, march the depth buffer, reflect opaque-colour-on-hit / sky-otherwise, Fresnel sheen→mirror + sun glint). Done in the water shader (no full-screen pass → no darkening), reflects only the sea not matte walls.
- **⏭ Open (all minor/optional):** global metal/hull SSR (needs the risky full-screen pass + a reflectivity mask — low ROI); **warp** VFX (already a clean UI effect); **weather** VFX deliberately kept as the world-aware `WeatherFx3D`; URP decals. The retired VolumetricFog full-screen pass has been fully removed (089d862); the "Volumetric fog / light shafts" setting now gates the visible distance haze + god-rays.

**Build:** `scripts/build-client.ps1 [-SkipPrereqs]` (Unity 6000.4.9f1). renderPostProcessing is NOT enabled on the camera → the URP Volume post (UrpScenePost tonemap/bloom/grade/lens-flare/SMH) likely never applies; SSAO + visor are renderer-feature/blit based so they DO. Worth checking if enabling URP post is wanted.

### ★ Natural movement overhaul — creatures + NPCs + planet enemies — ✅ server + client + tests (2026-06-17, NEEDS Unity client build)
Fauna, settlement NPCs and planet enemies all moved mechanically (constant speed, never paused, beeline hunts, no facing). A new shared pure controller fixes it; analysis + plan in `[memory] creature-movement-naturalness-analysis` + `movement-overhaul-implementation-plan`. 646 tests green (new `LocomotionControllerTests` + extended `CreatureTests`); client libs synced, needs a Unity build to see it.
- **Shared `LocomotionController`** (`src/BlocksBeyondTheStars.Shared/Definitions/LocomotionController.cs`): pure, deterministic, unit-tested. Stop-and-go (roam↔pause), eased speed (momentum), turn-rate inertia (no instant heading snaps), in-segment weave, and a per-actor vertical-life wave. Carried as a `LocomotionState` on `CombatEntity` + `ServerNpc`.
- **Creatures — per-species variation:** every species rolls a `LocomotionStyle` (Grazer/Darter/Prowler/Hopper/Drifter/Slitherer/Glider/Schooler/Strider) in `CreatureGenerator.MakeSpecies`, biased by its body+habitat+temperament (drawn LAST so existing rosters are byte-identical). Drives a seeded profile (`LocomotionController.ForSpecies`). Pausing finally lets the existing graze/alert/lunge idle gestures fire; pack-hunters flank/encircle; gliders swoop, swimmers porpoise on their own wave, hoppers pop — via `AdjustHabitatHeight`.
- **NPCs — uniform (no variation):** the endless Lissajous loop is gone; one shared `NpcProfile` gives loiter↔stroll (stand, potter to a new spot within the leash, stand again) and real travel-direction facing.
- **Planet enemies — uniform per kind:** walking robots are heavy (slow accel, slow pivots, pause-and-scan); the flying scan-drone orbits/strafes the player at a standoff and hover-bobs instead of ramming. Seam-correct via an `Unwrapped` helper.
- **Client:** `CreatureView` now turns the body to face its smoothed velocity (creatures used to slide/moonwalk — `NetCreature` carries no facing, so it's velocity-derived). Planet-enemy + NPC views already faced movement, so the server changes flow in for free.

### ★ Material variety overhaul — dead-ends fixed, new tiers + metal blocks, depth-banding — ✅ data + server + tests + assets (2026-06-17, NEEDS Unity client build)
A full pass over mineable materials, crafting and ore distribution (analysis: [memory] mineable-materials-analysis). All data/server/tests green (638); 22 new OpenAI tiles/icons generated, bundled and installed; the client must be rebuilt to surface the new blocks/items. **Adds 16 blocks → block numeric ids re-sort, so existing savegames become palette-incompatible** (acceptable pre-release; do future block adds in one batch).
- **P1 dead-ends fixed:** `plant_seed`/`crystal_seed` were defined but unobtainable — now hand-craftable (`2 plant_fiber → seed`, `1 crystal → crystal_seed`). `ash` is now collectable+placeable (the block dropped nothing). The **detoxifier** is now a craftable, placeable station block (planetside counterpart to the ship module; `GameServer.StationAvailable` maps `Detoxifier → "detoxifier"` block). `tin`/`zinc` got functional payoff: new `bronze_gear` (doors) and `brass_fitting` (comm_radio/radar_scanner) sinks beyond the cosmetic bronze/brass blocks.
- **P2 depth-banding:** rare metals (uranium/platinum/tungsten/neodymium/cobalt) pushed deeper; new deep gem tier `diamond_ore` seeded on crystal/crystal_living/lava/ashen/highland/corrupted/asteroid (within each crust's reach, enforced by `EconomyBalanceTests`).
- **P3 new tiers:** `diamond` (refine 2 ore → 1) → `diamond_drill` (tier-3, no energy; new blueprint) + sinks in plasma_sword/plasma_blaster. `polymer` (carbon_composite+sulfur) → alt cable, `insulated_wall`, jetpack/stealth_suit. `biofuel` (berries+fibre) → alt energy cell — gives the large flora-drop set a real sink.
- **P4 placeability:** 13 metal **storage blocks** (iron…titanium) with 9:1 pack + unpack recipes — compact storage, decoration, and a building use for gold/silver (previously circuit-board only).
- **Guards:** `CraftingConsistencyTests` extended (`EveryItem_IsObtainable_OrIntentionallyGranted` catches future dead items; new intermediates must be made+consumed); `DetoxifierTests` covers the placeable block.

### ★ Life in the water — lakes/ponds/seas were lifeless; now planted + populated — ✅ server + tests (2026-06-17, no client build needed)
Players never saw animals or plants in any water body. Two independent systems, both fixed server-side (the kelp/coral/lily/seagrass blocks already exist in `blocks.json`, so no Unity build is required — applies to newly generated chunks; live for fauna):
- **Fauna (the main bug):** water creatures probed `y = surface + 1`, which is the *air* cell above the flush
  pond/sea surface — so `HabitatSuitable` never matched and aquatic life never spawned in the visible lakes.
  New `WorldGenerator.TryGetWaterSurface` reports the real local water column (sea **or** upland pond); the
  spawner now seeks the nearest water near each ring spot (skips the species if none) and places swimmers
  *inside* the column, amphibians at the surface; `AdjustHabitatHeight` keeps swimmers in that local column
  ([GameServerCreatures.cs](src/BlocksBeyondTheStars.GameServer/GameServerCreatures.cs)). `HasWaterLife`
  ([CreatureGenerator.cs](src/BlocksBeyondTheStars.WorldGeneration/CreatureGenerator.cs)) now keys on
  `WaterAbundance > 0.15`, so every world that pools water also hosts aquatic fauna.
- **Flora:** `StampWaterFlora` only ever placed kelp/lily; `flora_coral` + `flora_seagrass` were dead content.
  All four seabed archetypes now plant (coherent patches), the planting band is wider, and lily is a touch
  more common, so lakes read as visibly green ([WorldGenerator.cs](src/BlocksBeyondTheStars.WorldGeneration/WorldGenerator.cs)).
- **Rivers now actually carve (2026-06-17):** Generate's river branch was inert — `columnFluid` started as the
  sea fluid, so its `columnFluid != seaWaterId` gate never opened on water worlds and no channel ever carved.
  Replaced the gate with a `pondHere` flag, so wet worlds (WaterAbundance ≥ 0.4) now get winding rivers. Added
  `WorldGenerator.SurfaceRiverDepth`; trees/props/mushrooms/geysers + `IsSurfaceWater` (ship landings) and the
  aquatic-life helper all now treat river water as water, so nothing stands in a river and fish/plants fill it.

### ★ Taming + shapes woven into the story — companions calm the Wächter, non-cube forms read as anomaly — ✅ server + tests + locale (2026-06-17, needs client build)
Two of the game's systems now pay off the VEGA arc's canon (Guardian protects the *living worlds*, judges humans the threat):
- **Companion ward (mechanic + memory):** a present tamed companion within 12 blocks makes the planet machines
  read its owner as part of the protected biosphere — they neither hunt nor bite while it wards them
  ([GameServerEnemies.cs](src/BlocksBeyondTheStars.GameServer/GameServerEnemies.cs): `WardedByCompanion`,
  consulted by the chase scan + the proximity-damage pass). The first time the player sees a machine stand
  down, VEGA explains it (`story.vega.insight.companion_ward`). The mechanic works regardless of story; only
  the line is gated.
- **Shape anomaly (narrative only):** forming the first non-cube block fires VEGA's recovered memory that the
  Service only ever built in cubes because the Guardian reads any other form as a constructed anomaly
  (`story.vega.insight.shape_anomaly`, hooked in `HandleShapeCraft`). **No** gameplay penalty — the creative
  feature stays free.
- Both VEGA lines are event-driven memories (not threshold beats — they'd break beat ordering), spoken on the
  existing `ShipAiLine` kind-2 channel the `VegaPanel` already renders (**no client C# change**), once per
  player, and **gated behind the Guardian reveal** (beat B5 ⇒ `BeatsRevealed ≥ 6`) so an early tame/shape
  can't pre-empt it — the per-player flag is only consumed once the line actually speaks
  ([GameServerStory.cs](src/BlocksBeyondTheStars.GameServer/GameServerStory.cs): `GuardianMandateKnown`).
- Bilingual keys in [data/stories/vega_protocol/locales/](data/stories/vega_protocol/locales/) (en+de), synced
  to StreamingAssets by `build-client.ps1`. **5 new tests; 632/632 green.** ⏭ Needs a Unity client build.

### ★ Story/finale test commands — read the whole arc + reach the boss without grinding — ✅ server (2026-06-17, needs client build)
Admin chat commands to fast-track late-game testing (all gated by `IsAdmin` + `CheatsAllowed`, logged as
`[CHEAT]`). Wired the existing server-only story hooks into chat and added the missing pieces:
- `/story` (status), `/advance [n]` (advance the arc), `/revealfinale` (reveal all beats + put the Guardian
  system on the map) — these server handlers existed (`AdminAdvanceStory`/`AdminRevealFinale`) but were not
  reachable from the client; now wired in [ChatUi.cs](client/Assets/BlocksBeyondTheStars/Scripts/ChatUi.cs).
- `/lore` → **new** `reveal_lore` (`AdminRevealAllLore`, [GameServerStory.cs](src/BlocksBeyondTheStars.GameServer/GameServerStory.cs)):
  reveals every fragment + personal memory to the caller (fills the Story tab) and completes the beats.
- `/jumpdrive` → **new** `grant_module`: fits `jump_generator` to the active ship (no admin module-grant
  existed before). `/gotocore` → **new** `goto_core` (`AdminGotoCore`, [GameServerStoryFinale.cs](src/BlocksBeyondTheStars.GameServer/GameServerStoryFinale.cs)):
  reveals the finale and lands the player in the core chamber, skipping the flight + orbital gauntlet.
- `HandleTravel` gained a `bool adminBypass` to skip the jump-generator gate for `/gotocore`.
- Docs: [USER_MANUAL.md](docs/user/USER_MANUAL.md) §7 (new *Story / finale testing* table + two test recipes);
  `ui.admin.help` (en/de) extended. **Server build green; 70/70 story/finale/travel/admin tests pass.**
  ⏭ Needs a Unity client build + bundled-server lib sync for the chat commands to be live in-game.

### ★ Planet movement lag — per-frame chunk-mesh budget + off-thread collider bake — ✅ client built (2026-06-16, needs playtest)
Moving fast on a planet stuttered because the client chunk pipeline did synchronous, unbudgeted work in bursts
as the player crossed 16-block chunk boundaries: **every** dirty chunk re-meshed in ONE frame, and each
`MeshCollider.sharedMesh` assignment cooked the collision mesh synchronously on the main thread (the heaviest
per-chunk step). Standing still was smooth; moving fast = a steady stream of new chunks = repeated frame hitches.
Both fixes are client-only (no protocol change), all in `GameBootstrap.cs`:
- **P1 — per-frame mesh budget:** new tunable `MeshChunksPerFrame` (default 4) caps chunk (re)builds per frame,
  **nearest-first** (`ChunkDistSqToPlayer`, seam-aware via `SceneX/SceneZ` so ordering is correct on the torus);
  the rest stay queued in `_dirty` for following frames. Not-yet-streamed neighbours are no-ops and don't spend
  budget. The player's own chunk sorts first, so local edits still re-mesh same-frame (no input lag).
- **P2 — async collider bake:** the collision mesh is cooked off the main thread via `Physics.BakeMesh` on a
  `Task`, then the cached cook is assigned in `DrainBakedColliders()`. `_colliderGen` + `WorldEpoch` fence stale
  bakes (a newer remesh, a world change, or a gone chunk → the throwaway mesh is freed) so a cook never lands on
  the wrong chunk. Safe vs the station-boarding fall: the spawn settle-freeze raycasts for the collider
  (`PlayerController.cs`:184-192), so async just releases control 1–2 frames later, never a fall-through.
- *Tuning: raise `MeshChunksPerFrame` if terrain pops in late under sustained speed; lower it if frames still
  spike. Follow-ups left open: P3 mesh/buffer pooling (GC spikes), P4 `RepositionChunks`/`LightSourcesNear`.*

### ★ Avatar face fix — drawn pixel-face + stock features were buried inside the head — ✅ FIXED, client built (2026-06-16)
The custom pixel-face (FaceEditor) never showed on the avatar or its preview, and the stock eyes/brow/mouth
read as blank. Root cause: every face element is parented to the head — a unit-cube primitive whose FRONT
surface is at head-LOCAL z = 0.5 — but they sat at z ≈ 0.235–0.275, i.e. ~half depth, buried INSIDE the opaque
`LitColor` head and occluded by its front face. The data flow (editor → `ApplyFace` → `SetFace` → Character-tab
preview hash) was already correct; only the geometry was wrong. `PlayerAvatar.cs`:
- **`FacePlateZ 0.27 → 0.5`** so the drawn face plate's front protrudes past the head surface.
- **Visor/Eyes/Pupils/Brow/Mouth shifted forward +0.255** (z 0.235–0.275 → 0.49–0.53), preserving the relief
  (pupils ahead of the whites). Affects every avatar + NPC, not just the opt-in custom face.
- *Pupils now sit ~2 cm proud — nudge their z down a touch if it reads as bug-eyed in a real build. A possible
  separate follow-up: the face texture may be horizontally mirrored (orientation, not visibility).*

### ★ Loading-splash launcher — instant splash over the black startup gap — ✅ launcher built + verified (2026-06-16)
The built client shows nothing for ~12–14 s on first launch (Windows Defender scanning the 162 Mono DLLs),
then the Unity splash appears. Unity can't draw before its engine inits, so a separate process must fill the
gap. New `src/BlocksBeyondTheStars.Launcher` (.NET 8 WinForms, self-contained single-file ~68 MB — same order
as the bundled self-contained server) shows an instant "Loading…/Lädt…" splash, starts the player, and hands
off (closes) the moment the player's window appears (Unity's own splash then takes over — kept visible per the
free-license terms).
- **`Program.cs`:** runs `VelopackApp.Build().Run()` first (so the launcher can be the Velopack `--mainExe`
  and handle install/update/uninstall hooks), then `Process.Start`s `BlocksBeyondTheStars.exe` from its own
  folder and shows the splash.
- **`SplashForm.cs`:** borderless top-most procedural splash (dark space gradient + stars + title + sweeping
  indeterminate bar; loading text localized by OS culture; a `splash.png` next to the exe overrides the art).
  Polls the child and closes on `MainWindowHandle != 0` (350 ms grace + fade), with a 45 s safety timeout and
  early close if the child exits.
- **Build/dist:** `build-client.ps1` publishes the launcher (compressed single-file) and drops it next to the
  player; `publish-client-installer.ps1` sets `--mainExe` to the launcher (shortcut target + hook host).
- **Verified** with a deterministic dummy-GUI test: launcher starts the child, hands off while the child window
  stays open; a console-only child correctly waits. *Cosmetic only — does not speed up startup; combine with
  code-signing + Defender-exclusion / IL2CPP / shrinking `Resources/` for actual speed. Not committed yet.*
- **Icon + readability:** the launcher exe/window share the game's rocket icon — `scripts/make-launcher-icon.ps1`
  builds a multi-size `.ico` from `Icon/app_icon.png`, embedded via the csproj `<ApplicationIcon>`. The splash
  layout now derives from the live client size (title auto-fits, pixel-unit fonts, manual DPI scaling), so it's
  crisp and readable at 100 %/150 % DPI; a `--render-splash` dev hook renders it to a PNG for visual checks.

### ★ Window mode — windowed / borderless / exclusive, movable + maximizable — ✅ client + Unity build (2026-06-16)
The client used to always launch as a borderless fullscreen window on the primary monitor, with no way to move
it to another monitor or maximize it (`resizableWindow: 0` + the legacy `Screen.fullScreen` bool). Now it starts
as a normal **windowed** 1600×900 window that can be dragged to any monitor and maximized.
- **Player Settings** (`ProjectSettings.asset`): `resizableWindow 0→1` (title bar + maximize + resize grip),
  `fullscreenMode 1→3` (Windowed boot), default screen `1920×1080→1600×900`.
- **`ClientSettings`:** removed `bool Fullscreen`; added `WindowMode { Windowed, Borderless, Exclusive }`
  (+ persisted `WindowedWidth/Height`). New `ApplyWindowMode()` uses `Screen.SetResolution(..., FullScreenMode.*)`
  instead of the boolean API. Borderless fills whichever monitor the window currently sits on (Unity 6).
- **`UiSettings`:** the on/off "Fullscreen" toggle is now a 3-way **"Window mode"** cycle that applies immediately.
  Locale key `ui.settings.fullscreen` → `ui.settings.window_mode(.windowed/.borderless/.exclusive)` (DE+EN).
- *Optional follow-up:* remember the last-used monitor across launches via Unity 6 `Screen.MoveMainWindowTo`.

### ★ Bug-fix wave — face editor (+ Avatar Designer), in-game tab bar, editor scroll — ✅ FIXED, needs Unity client build (2026-06-15)
Reported in-game UI bugs + a follow-up, all client-only (no server/test changes):
- **Pixel-face editor unusable + lingering** (`FaceEditor.cs`, `GameMenu.cs`): its canvas was `sortingOrder 30`
  but the in-game menu is `50`, so the editor rendered *behind* the menu; and `GameMenu` never tore it down, so
  the overlay stayed painted on screen after closing the menu. Fix: raise the editor canvas to `sortingOrder 60`
  and add `CloseFaceEditor()` (destroys the modal) on menu close and on any tab switch.
- **"Companions" tab label overflowed the button** → relabelled the tab to **Creatures / Kreaturen**
  (`ui.tab.companions` locale only; the feature, panels and category headings keep "Companions/Begleiter").
- **Menu tab order** (`CraftingTechShipUI.BuildHeader`): the tab labels were positionally bound to the `Mode`
  enum. Decoupled display order from enum value via a `Tabs` descriptor array; reordered so the core
  build/travel loop comes first, then Story → Creatures → Alliances, with **Settings pinned far right**.
- **Ship/Station editor palette scrolled at a crawl** (`UiKit.ScrollList`): the shared scroll helper never set
  `scrollSensitivity`, so it used Unity's default `1.0`. Set it to `30` (matching the rest of the UI) + clamp.
- **Face Apply didn't update the Character-tab preview** (`CraftingTechShipUI`): the preview re-applies the face
  only on a rebuild, but the change-detection hash didn't include the face. Added `Game.FacePixels` to the hash
  so applying a face refreshes the live preview on the next frame.
- **Pixel-face editor now also in the main-menu Avatar Designer** (`AvatarEditor.cs`): generalised `FaceEditor`
  to take host hooks (`InitialFace` / `Localizer` / `OnApply`) instead of a hard `GameMenu` reference, so the same
  editor serves both the in-game Character tab and the Avatar Designer. The designer loads your current face onto
  the preview figure, adds a **Pixel face** button that opens the editor, and its Apply now persists `FacePixels`
  alongside the colours. New locale key `ui.avatar.face`.

### ★ Hover speeder — craftable single-seat surface vehicle — ✅ server + tests + client wiring + icon + sounds, needs Unity client build (2026-06-15)
A craftable **hover speeder**: deploy it from a hotbar item, board it and drive across a planet surface; it
hovers over the terrain, runs on its own energy cell, and can take damage and be destroyed. Locked design:
arcade-hover (car-style) controls · deploy/retrieve lifecycle · own energy tank · voxel hull.
- **Item/craft:** `speeder` (gadget-kind tool, maxStack 1), blueprint-gated workshop recipe (titanium_plate ×8,
  cable ×10, energy_cell_1 ×4, circuit_board ×2, crystal ×2); blueprint `speeder` (knowledgeCost 12). DE+EN locale.
- **Server (`GameServerSpeeders.cs`):** a `ServerSpeeder` entity materialised per present owner from a
  per-player `DeployedSpeeder` record (persisted in the player blob like companions — no DB table; survives reload
  via `Snapshots`). Deploy (via the gadget-use path, consumes the item) / pack-up (returns it) / board / dismount /
  refuel (energy_cell_1) / reconcile. Driving reuses `MoveIntent`: the server slaves the speeder to the driver's
  pose, drains the energy cell by distance, and broadcasts the pose at ~2.5 Hz.
- **Damage:** `Hull`/`HullMax`; collisions are client-reported (`SpeederImpactIntent`, mirrors fall damage) and
  scaled server-side + jolt the driver; hostile wildlife dents the hull while you drive; destruction ejects +
  jolts the driver, drops the record (item lost) and broadcasts an explosion.
- **Net:** `SpeederList` / `SpeederFx` (S→C) + `Enter/Exit/Stow/Refuel/SpeederImpact` intents + `UseGadgetIntent`
  deploy (NetCodec 161–167); `PlayerStateUpdate.InSpeeder` flips the client into drive mode.
- **Client:** `PlayerController` drive branch (W/S throttle · A/D steer · Space hop · Shift boost · hover raycast ·
  chase cam · F dismount · R refuel; E boards / X packs up a nearby own speeder), `SpeederView` (voxel hull via the
  ship mesher, hover dust + engine glow + deploy/destruction bursts), vehicle HUD in `HudUi` (integrity + energy
  gauges, speed, prompt; hotbar hidden while driving), `ClientAudio` engine loop (pitch tracks throttle).
- **Assets:** OpenAI `item_speeder.png` icon; ElevenLabs `vehicle_engine_loop`/`_startup`/`_shutdown`/`_impact`.
- **Tests (`SpeederTests.cs`, 7):** deploy consumes+records, board+drive drains energy, exit parks, stow returns
  item, collision dents + destroys, refuel consumes a cell, deployed speeder survives a reload.

### ★ Craftable block shapes — re-form materials into spheres/ramps/pyramids/… at the workbench — ✅ server + tests + client wiring, needs Unity client build (2026-06-15)
Players can re-form a held building material into a non-cube **shape** (slab, pyramid, dome/half-sphere, sphere,
ramp, stairs, cone, cylinder) that still behaves like a block — analogous to the dye action, and **player-craft
only**: shapes never appear in world-gen, settlements/villages, space stations or ships (those stay plain cubes).
- **Shape model:** a per-voxel **shape descriptor** packs a shape index + a yaw orientation (`Shared/World/BlockShape.cs`,
  `ShapeCode`). Independent of the dye tint, so colour + form compose. 0 = plain cube (the default everywhere, incl.
  all generated/stamped blocks — the only thing that ever writes a real shape is a player placing a crafted form).
- **Item identity:** a shaped item is a composite key `base#s<nn>` (`Shared/State/ItemKey.cs`), so it stacks
  separately and carries the form through place/mine; colour (`t`/`g`) and shape (`s`) coexist on one key.
- **Storage/persistence/net:** `ChunkData` carries a sparse per-voxel shape dict; persisted in `block_edit`
  (+`shape` column, migrated) and synced via `ChunkDataMessage.ShapeIndex/ShapeData` + `BlockChanged.Shape`.
- **Crafting:** always-available `ShapeCraftIntent` → `HandleShapeCraft` (no station, free 1:1, preserves colour);
  only `Shapeable` building blocks qualify (the curated set shared with `Tintable`). UI: a "Formen/Shape" category
  in the Crafting menu with a form-button grid (cube = revert).
- **Placement:** the form comes from the item key; the **orientation is derived from the player's facing** at place
  time (ramps/stairs face where you look). Mining returns the shaped item; orientation is re-derived on re-place.
- **Rendering (surface only, v1):** the chunk mesher emits per-shape geometry (`client/.../BlockShapeGeometry.cs`)
  + a shape-matched collider; a `worldShape` neighbour resolver stops a cube culling the face toward a shaped
  neighbour (no holes). Ship/station meshers pass `worldShape=null` → they always render cubes.
- **Tests (`BlockShapeTests.cs`, 7):** ShapeCode pack/unpack, ItemKey encode + colour compose, ChunkData
  store/clear-on-air, persistence round-trip, networking round-trips, craft forms the item, refuses non-shapeable.

### ★ Creature taming & companions — ✅ server + tests + client wiring + icons, needs Unity client build (2026-06-15)
**Plan:** [docs/developer/CREATURE_TAMING.md](docs/developer/CREATURE_TAMING.md). Wild fauna was kill-for-loot only; now a
player can **tame creatures into named companions** bound to the world they were tamed on.
- **Creature Translator gadget (`creature_translator`):** right-click a wild creature to start a **bonding ritual** —
  the translator decodes its **mood + need**; the player **responds** (offer the bait it craves / calm / approach /
  give space) to earn hidden **trust** until it is tamed. New **bait** consumables (`forage_bait`/`meat_bait`/
  `nectar_lure`), recipes, a knowledge-gated blueprint, and bilingual locales. OpenAI-generated icons for all four.
- **Difficulty + randomization (`GameServerTaming.cs`):** required trust + patience scale with **temperament**
  (Passive→PackHunter), exotic habitat (Cave/Lava/Air), glow + size; a per-individual seed randomises preferred
  bait + shyness, so even same-species animals tame differently. Skittish bolt on a wrong move; territorial/
  aggressive provoke; passive forgive.
- **Companions:** persisted **per-player** as `TamedCreature` (full `SpeciesSnapshot`, since wild fauna is transient/
  per-world); they **follow** the owner on their home world (new `CreatureBehaviour.FollowStep`), are invulnerable +
  harmless, excluded from the wild cap/prune, capped at 6/world, re-spawn on return (`ReconcileCompanions`).
- **Knowledge reward:** a first tame of a species pays **research knowledge** (difficulty-scaled, once per species via
  a `TamedSpecies` set; small trickle after) — feeds the tech tree without farmable grind.
- **Client:** `OwnerId`/`CustomName` on `NetCreature` → friendly green-cyan tint + floating **nameplate**; a HUD
  **taming prompt** (mood + need + trust + four response buttons); a new **Companions/Begleiter menu tab** (roster,
  rename, release) with a "new companion" badge.
- **Tests:** 9 new taming tests (598/598 green) — ritual by temperament, seed variance, persistence round-trip,
  home-body binding, per-world cap, first-tame-knowledge gate.

### ★ Own-ship repair (hull + EVA-carved hull cells) — ✅ server + client wiring, needs Unity client build (2026-06-15)
**Plan:** [docs/developer/SHIP_REPAIR.md](docs/developer/SHIP_REPAIR.md). The player's own ship had no repair path — combat
dented only the numeric hull (never regenerates; only free-restored on destruction) and EVA-carved hull cells were
only refillable by hand. Now one cockpit action restores both.
- **One engine, one material model (`GameServerShipRepair.cs`):** repairs against the ship's **design reference**
  (a pristine `BuildShipStructureFrom(persistEdits:false)` build; its cells == the live structure's `Baseline`).
  Missing cells = baseline cells now air → refilled with the design block. Hull is bought with **`iron_plate`** at
  **10 hull/plate**; each missing cell costs its placing item if one exists, else `iron_plate` (so lights/engine
  blocks never block a repair). Mirrors the wreck repair idea (design reference + matching item) for your own ship.
- **`RepairShipAll` is greedy/partial:** hull first (the common combat case), then cells as far as the materials
  stretch — never a hard block when short. `RepairShipCell` does one guided cell (field/EVA). Material-only:
  **no passive hull regen**; destruction keeps today's free full restore (§8.5) as the soft-lock floor.
- **Surface:** the **cockpit** pushes a `ShipRepairStatus`; HudUi shows a "Repair ship" panel (hull bar + material
  needs + a button → `RepairShipIntent{all}`). Cell fills persist (`SetStructureBlock`) + broadcast
  (`StructureBlockChanged`) so the landed ship re-meshes. New msgs 151/152 registered in NetCodec.
- **Tests (`ShipRepairTests.cs`, 5):** hull restore + plate cost, partial when short, per-cell refill + item cost,
  combined hull+cells, free-build. Full suite **589/589**.
- **Follow-up (not built):** a client field/EVA per-cell highlight UI (the server already accepts `Mode="cell"`).

### ★ Peaceful NPC trader ships (living space) — ✅ P1–P3 server + tests complete, needs Unity client build (2026-06-15)
**Plan:** [docs/developer/NPC_TRADER_SHIPS.md](docs/developer/NPC_TRADER_SHIPS.md). Ambient civilian traffic so space feels alive.
- **Traffic per system:** deterministic **None / Rare / Often** from seed + system id (`TrafficFor`); systems with a
  station lean busier. Drives a per-instance spawn scheduler — traders spawn **only in occupied flight instances**
  (instances are per-launch-body), capped (Rare ≤1, Often ≤3), paced ~25–70 s (Often) / ~90–240 s (Rare).
- **Future-proof ship choice:** picks any non-starter type from `GameContent.Ships`, weighted by cargo — new
  ship types in `data/ships.json` are included automatically, no code change.
- **Lifecycle (`GameServerSpaceTraders.cs`):** warp **in** at the system edge → cruise to a **station** (dock) or
  through the inner system → **dock** (slip into the bay) or warp **out**. Multiple concurrent; transient (no DB).
- **Rendered with zero new flight-view code:** a trader rides the existing **remote-ship** path — a synthetic
  `NetSpacePlayer` pose (`npc:<id>`) + a `"ship_remote"` `SpaceShipDesign` (`BuildNpcShipStructure` reuses the real
  player-ship voxel pipeline), so it shows the actual in-game ship hull of its type.
- **Invulnerable scenery:** a trader is **never** a `CombatEntity` — it can't be locked, shot or damaged, and
  never harms anyone.
- **Pilot becomes a merchant (station):** on docking, the pilot is registered as a **visiting trader** at that
  station; boarding the station spawns it as a `"traders"`-theme **vendor** beside the trade post, so the existing
  station-vendor barter works against it (lingers ~150–300 s).
- **Planet landing (P3):** a trader heading into the inner system **lands on a planet/moon if a pad is free** —
  it **reserves the pad** (players never get assigned it), parks its **real voxel ship** on the pad, and its
  **pilot stands in front** as a `"traders"` merchant you can barter with (`MarketAvailable`/`VendorThemeAt`
  extended to the landed pilot). It lingers ~180–360 s, then lifts off and frees the pad. Survives the body
  world being unloaded (a `_landedTraders` registry is the source of truth; the parked ship + pilot
  re-materialize on world load / per-world tick). One landed trader per body at a time. Reuses the existing
  `LandedShipState`/`ShipTransitFx`/`NpcList` paths → **no new client code** for P3.
- **Warp FX for bystanders:** new `SpaceWarpFx` message (tag 150) → a localized cyan-white hyperspace burst in the
  flight view, so other players **see** arrivals/departures (the first-person `HyperspaceWarp` overlay didn't cover this).

### ★ HUD quick-bar readability + flight/menu consistency — ✅ COMPLETE, Unity build-verified (2026-06-15)
- **Hotbar (HudUi):** larger cells (72px) with the icon nearly filling each cell, a dark backplate + cyan
  keyline, a bright fill + outline ring on the selected slot, outlined hotkey numbers, and the full item name
  on the held slot only — was small/low-contrast with a ~25%-filled icon.
- **Shared cell (`UiKit.QuickSlot`):** the hotbar + the flight **ship-systems bar** now render the same icon
  cells (SpaceView's centred text strip → icon slots on a backplate), so the on-foot and flight HUDs read alike.
- **"New content" menu badges (`UiKit.AddBadge`):** the **Arcade** entry badges on a freshly downloaded
  data-cube minigame (cleared on open), the **Story** tab badges on a new beat/fragment/memory (cleared on
  open), and the **Tech** tab badges when a blueprint is unlockable right now.
- **Tech knowledge cost:** the client now shows + enforces `BlueprintDefinition.KnowledgeCost` in the Tech
  detail and the "unlockable" status (`ui.tech.knowledge`, DE+EN) — it was ignored client-side (server-validated only).

### ★ Story system ("The VEGA Protocol") — ✅ P0–P8 COMPLETE, server + Unity build-verified (2026-06-14)
**Plans:** [docs/developer/STORY_IMPLEMENTATION.md](docs/developer/STORY_IMPLEMENTATION.md) (engineering, phased P0–P8 +
Suno appendix), [docs/developer/STORY_VEGA_PROTOCOL_CONCEPT.md](docs/developer/STORY_VEGA_PROTOCOL_CONCEPT.md) (design rationale),
[docs/developer/LORE_STRUCTURE.md](docs/developer/LORE_STRUCTURE.md) (authoritative canon + content-generation agenda).
A **pluggable, story-agnostic engine** drives swappable **story packs** (the active story is selectable
in-game); the first pack is *The VEGA Protocol* — SPS grundstory, clone twist, three Guardian machine types
(space UFO + planet three-eyed robots + planet scan-drones), text-only **net fragments** + **player memories**,
a **new Story Log tab** (separate from the existing Codex/Wiki), and a multi-stage two-route **dialogue-duel
finale** vs. the dormant Guardian core. Story is data: `data/stories/<id>/`.
- **Status:** **P0–P1 ✅ complete (2026-06-14)** — story-agnostic pacing **engine**
  (`src/BlocksBeyondTheStars.Shared/Story/`), per-save `story_state` **persistence**, `GameServerStory.cs`
  wiring (Record fragment/kill/milestone → threshold beat reveal via `ShipAiLine`, per-player catch-up on
  join, admin story selection + "none"), and `StoryStateMessage`/`StorySelectIntent` networking (tags
  139/147). P1 added the **pluggable story-pack data format + loader** — `data/stories/vega_protocol/`
  (`story.json` + bilingual `locales/{en,de}.json`, the B0–B12 arc authored DE+EN) → `GameContent.Stories`,
  plus the `tools/merge_story.py` validator. P2 (server) added **net fragments**: 6 authored fragments per
  lore category (DE+EN) + `GameServerNetFragments.cs` — deterministic **datacube-style surface placement**
  (weighted, still-needed pool, deduped vs found-set), reach-checked **pickup** → archive text reader +
  `RecordStoryFragment`; tags 140/141/148. P4 (server) added **combat-as-progress**: defeating Guardian
  machines (planet enemies + space drones/UFOs) advances the story (`RecordStoryMachineKill` hooked into both
  kill paths; fauna excluded), with an end-to-end kill→story test. P3 (server) added **milestone hooks**:
  mission turn-in (settlement helped) + new-system-mapped → `RecordStoryMilestone`. **Boss soundtracks** (5
  Suno tracks) integrated into `client/Assets/Resources/music/music_boss_*.mp3`. P5 added the **count-neutral
  machine/wreck coupling** (`GameRules.MachineWreckCoupling`); P4 also added **player memories** (drop on
  machine kills → per-player unlock → `PlayerMemoryRevealed` 142); P6 added the **pacification gating**
  (`MarkGuardianDefeated` → no machine spawns once the core is down) **plus the full finale server backbone**
  ([GameServerStoryFinale.cs](src/BlocksBeyondTheStars.GameServer/GameServerStoryFinale.cs)): arc-complete →
  **reveal** the Guardian system (`GuardianSystemRevealed` 143) → **core hack** channel (`CoreHackIntent` 146 /
  `CoreHackProgress` 149) → **argument duel** (data-driven `coreArguments`; `CoreDialogueMessage` 144 /
  `CoreDialogueChoiceIntent` 145; correct contradiction advances, wrong stalls, never lost) → win calls
  `MarkGuardianDefeated`. P6 also added the **Guardian-system generation** (lazily append a lone landable
  `guardian_finale` core to the galaxy on reveal → appears as a `jump_generator` target; re-appended on restart
  for revealed saves), the **respawn-at-prior-world** rule (a death in the boss system returns the clone to the
  world it launched from — no death-loop), and the **Stage 1 elite space gauntlet** (`SpawnGuardianGauntlet` —
  a heavy cruiser + elite UFOs + a reinforced drone swarm only in the finale system). **All server-side story
  logic is done and tested — 567 total green, 0 failed.**
  ⏭ **Client build-verified so far:** P2 fragment objects + pickup · P3 Story Log tab + progress meter ·
  P4 three-eyed-robot retheme **and the flying scan-drone** (`WorldEntities.BuildDrone` — hovering dark pod +
  red scanner eye, branch on `NetCombatEntity.Kind == "ScanDrone"`; server mix is count-neutral inside
  `PlanetEnemies`, toggled by `GameRules.PlanetDrones`) · **P6 finale encounter UI** (`FinaleView` — hold-`F`
  hack bar + the number-key **argument-duel panel**, driven by 143/144/149 + sends 145/146; bilingual) · **P6
  boss music** (`ClientMusic` 5 finale contexts approach/gauntlet/hack/dialogue/resolution → the `music_boss_*`
  tracks, override every context, cross-fade per phase). ⏭ **Still
  open (Unity + world-gen + playtests):** P6 **two physical routes** (fly-in interior vs. land+dig) + the in-world
  core console, gauntlet HUD, **boss/core visuals**, robotic SFX · P4 robotic SFX +
  a proper Fragment/Memory **reader panel** · P8 story-selection world-option UI + story-density slider · P7
  flavour-line pool + mission threading.
  See [docs/developer/STORY_IMPLEMENTATION.md](docs/developer/STORY_IMPLEMENTATION.md) §P0–P8.

### ★ Flora variety — per-biome themes, lush patchy undergrowth, multi-layer plants, tree archetypes — ✅ IMPLEMENTED (2026-06-14)
**Goal:** every biome reads as its own ecosystem — a forest is not "trees on bare ground" but a carpeted, layered,
varied thicket, and the same idea for all biomes (desert/tundra/swamp/jungle…). Flora is still surface-driven +
deterministic; the gains are richness, clustering, theming and tree shape variety.
- **Themes (data-driven):** `FloraCatalog` species now carry climate `FloraTag`s + a `FloraHeight`; `FloraThemes`
  maps a theme → preferred tags + density/tree multipliers + tree archetypes. Each planet sets `floraTheme`
  (`data/planets.json`); biomes may override it + scale density via `floraTheme`/`floraDensityMul`/`treeDensityMul`
  (`Biome` schema). So jungle-grass ≠ savanna-grass ≠ alpine-grass, and one `varied` world spans temperate meadow +
  arid dunes + rocky highland + swampy bog.
- **Richer placement (`WorldGenerator`):** a low-frequency **vegetation-richness mask** couples undergrowth density
  to the SAME forest mask the trees cluster in (lush forest floors) plus an independent meadow mask (lush/sparse
  patches everywhere); species selection is now **patchy + theme-weighted** (coherent fern glades / flower meadows,
  not salt-and-pepper). `FloraGenerator` activation is theme-biased (coverage still enforced).
- **Multi-layer:** tall species (ferns, reeds, grass tufts, vines…) render as taller cross-billboards
  (`ChunkMesher.TallFlora`) over the low ground cover — still one voxel per cell, so harvest/regrow is unchanged.
- **Tree archetypes (`StampTrees`):** broadleaf / conifer / palm / jungle-giant / dead, chosen per grove by the
  biome theme — conifers on alpine/tundra, palms+jungle on tropical, dead snags on ashen/desert. New foliage blocks
  `pine_needles` + `palm_frond` (flora-hued cutout crowns).
- **New flora (fill thin biomes):** `flora_grasstuft, flora_rockflower, flora_snowbush, flora_icereed,
  flora_saltgrass, flora_cinderbush` (+ the two leaf blocks) — bilingual `block.*` loc (de+en), drops, OpenAI
  textures bundled + leaf-alpha baked.
- **Tests:** `FloraVarietyTests` (8) — catalog/theme consistency, themed resolution, lush-world species variety,
  tree stamping, conifer-only alpine woods. Full suite **521 green**. **Needs:** Unity client build (in progress).

### ★ Beam blocks — craftable named teleporter pads (same-planet teleport) — ✅ IMPLEMENTED (2026-06-14)
**Goal:** craft a **beam block**, place + name it, then step onto it and press **E** to beam to any of your own or an
allied player's beam blocks on the **same world**. Each pad shows its name + coordinates on the planet map and as a
floating in-world label. Modeled on the radio beacon (a real voxel block + a tracked entity carrying owner/name)
plus a teleport action. Beaming costs **6 suit energy** with a **6 s cooldown**; scope is **own + allied** pads.
- **Server:** `GameServerBeam.cs` — `ServerBeam` entity (per-world list on `LoadedWorld.Beams`), place/remove/rename,
  `HandleBeamTeleport` (reach + own/ally scope + energy + cooldown → set position on the destination pad +
  `SendPlayerState` + `BeamTeleported` snap + `BeamFx` broadcast); `LoadBeams` on world activation, `DecayBeamCooldown`
  on the env tick. Hooks in `HandlePlace`/`BreakBlockAt`/gadget-blast (mirrors `radio_beacon`/`base_core`).
- **Networking:** `Messages/BeamMessages.cs` (`NetBeam`/`BeamList`/`SetBeamNameIntent`/`BeamTeleportIntent`/
  `BeamTeleported`/`BeamFx`); NetCodec tags 134–138. **Persistence:** `StoredBeam` + `beam` SQLite table (Save/List/Delete).
- **Client:** `BeamView` (glow column + idle hum + floating names + jump VFX), `BeamPadUi` transporter panel (destination
  list with name/coords/distance + Beam + owner Rename), `PlayerController` E-dispatch + place-naming, `WorldMap`
  markers, `NetworkClient`/`GameBootstrap` wiring (`CanUseBeam` own+ally filter, `RespawnTarget` snap), `WorldRig`.
- **Data:** `beam_block` block/item/recipe (workshop, blueprint-gated, titanium + cable + energy cell + crystal) +
  `beam_block` blueprint; bilingual `block/item/blueprint.beam_block.*` + `ui.beam.*` (de+en).
- **Assets:** `beam_block` texture (OpenAI) + `beam_teleport`/`beam_idle` sounds (ElevenLabs).
- **Tests:** `BeamTests` (7) — place/mine/rename/persist/teleport/energy-gate/ally-scope. **Needs:** Unity client build.

### ★ Bug-fix wave — oxygen, ship hatch, UFO combat, space explosion, asteroid loot, settings scroll — ✅ FIXED (2026-06-14)
Six reported issues, server stays authoritative; in-game text bilingual; all 506 tests green.
- **Oxygen too punishing on planets:** `GameRules.OxygenDrainPerSecond` quartered (Slow/Normal/Fast 1/2/4 → 0.25/0.5/1
  per s) — a full tank now lasts ~200 s on foot at Normal (was ~50 s). The tank upgrade still matters: `oxygen_tank_2`
  `oxygenBonus` 50 → 100 (`data/items.json`). Aboard-ship regen unchanged.
- **Open hatch at the ship's stern in space:** the rear hatch was an air gap you could see through. `SpaceView`
  now detects the rear (-Z) wall's opening from the ship cells and caps it with a CLOSED two-leaf airlock door
  (`AddRearHatchDoor`) that slides open (hides) on an EVA so boarding still works.
- **UFO combat too harsh / ugly / invisible shots / no warning / ship dies fast:** UFO 70→40 hull, 8→4 dps
  (`GameServerSpaceCombat`); every ship now launches with a baseline shield (+30, +2/s regen, restored on
  recovery) so early fights aren't lethal; the UFO model is a darker, taller saucer with a hostile red dome +
  big red threat lights; client now draws each in-range hostile's **red laser tracers** at the ship (server
  damage is an invisible aura); a new bilingual `vega.sys.spotted` warning fires the moment a hostile first
  enters aggro range (all AI-core tiers).
- **No explosion when the ship is destroyed in flight:** `SpaceView` now subscribes to `SpaceClosed` and bursts
  the hull into a debris + fireball explosion (`SpawnShipExplosion`) at the moment of destruction (was a bare
  screen flash).
- **First asteroid's loot not collectible by tractor:** passive tractor pickup radius 8 → 16 (loot spawns at the
  rock's centre, hard to reach at 8), and `FireWeapon` pins the ship cursor to the firing player so the
  tractor/loot path always uses the right ship.
- **Main-menu Settings couldn't scroll / overflowed:** the settings list is now hosted in a clipped vertical
  `ScrollRect` (`UiSettings.BuildScrollViewport`) sized to the rows, so the lower options (updates / language /
  back) are reachable by wheel or drag.
**Needs:** a Unity client build (done in this change) for the client-side fixes (hatch door, UFO visuals, tracers,
explosion, settings scroll).

### ★ Custom pixel face — in-game face editor, shown on your avatar to everyone — ✅ IMPLEMENTED (2026-06-13)
**Goal:** draw a 16×16 pixel face in-game; it appears on your figure, in the live Character-tab preview, and on
your avatar for every other player. Server-persistent (the face follows the player).
- **Format:** `FacePalette.cs` (client) — a 16-colour palette (index 0 = transparent) encoded as a 16×16
  hex string; opaque to the server, owned by the client. Empty = default procedural eyes/mouth.
- **Editor:** `FaceEditor.cs` — uGUI paint canvas (palette + eraser), opened from the **Character** menu tab
  (`ui.face.edit`). Apply persists to `ClientSettings.FacePixels`, shows it locally, and sends it.
- **Avatar:** `PlayerAvatar.SetFace` adds a textured face plate on the head front and hides the stock
  eyes/brow/mouth/visor; transparent pixels composite onto the skin (no transparent shader needed).
- **Networking:** `SetFaceIntent` (client→server) + `PlayerFace` (server→client), NetCodec tags 132–133 —
  out of band from the 10 Hz presence stream (sent on join + on edit). `RemotePlayers` paints received faces.
- **Persistence:** `PlayerState.FacePixels` + `PlayerSnapshot`/`StateMapper` (stored in the player JSON blob,
  no schema migration). **Data:** bilingual `ui.face.*` (de+en).
**Open/follow-ups:** verify face-plate Z/scale + horizontal U orientation in a Unity build (one-line constants
in `PlayerAvatar`); transparency is composited to skin tone, not true alpha; needs a Unity client build.

### ★ Player alliances — shared station/base access, no friendly fire, alliance + Funk tab — ✅ IMPLEMENTED (2026-06-13)
**Goal:** two players form an alliance and gain access to each other's built space stations and planet bases, and
cannot harm one another even with PVP enabled. A new in-game menu tab forms/ends alliances and hosts a radio (Funk)
chat window (read recent messages + write). The player **ship is excluded** — only its owner may board it.
Decisions: pairwise mutual (friend-style, not transitive) · allies = full co-owners (build/mine/use) + **new base
build-protection** · reuse the existing global radio in the tab · **stations become private** (owner + allies board).
**What was built:**
- **Server:** `GameServerAlliances.cs` — symmetric alliance graph + `AreAllied` predicate; request/accept/dissolve
  handshake (mirrors ship docking); server-wide persisted graph (`alliance` SQLite table) loaded at start; pending
  requests cleared on disconnect; roster sent on join + tab open. Access guards: EVA station-structure edits allow
  allied **stations** (never ships — `GameServerSpaceStructure`); player-built stations are now **private to board**
  (`CanBoardStation`: owner + allies + admin; NPC/procedural stations stay open — `BoardStation` + `TravelToStation`);
  **new planet-base protection zone** around `base_core` (`IsBaseProtected`, cube radius 8) wired into mine/place/
  area-mine/blaster — owner + allies bypass, the core itself stays owner-only so an ally can't dissolve the base.
  Non-aggression is a documented `AreAllied` hook at the (not-yet-implemented) player-damage points.
- **Networking:** `Messages/AllianceMessages.cs` (request-list/request/response/dissolve intents + `AllianceList` +
  `AllianceRequestNotice`); NetCodec tags 126–131. **Persistence:** `StoredAlliance` + `alliance` table (Save/List/Delete).
- **Client:** `NetworkClient` events/sends; `GameBootstrap` roster state + a shared recent-Funk feed; a new
  **Alliances** menu tab (`GameMenu.Tab`/`CraftingTechShipUI.Mode`) with three views — Allies (accept/decline/end),
  Find players (propose to any online player), Radio/Funk (live scrollback reusing the global chat + transmit box).
- **Data:** bilingual `ui.tab.alliances` / `ui.alliance.*` / `ui.funk.*` (de+en). **Tests:** `AllianceTests` (6).
**Open/follow-ups:** alliances are pairwise only (no named groups/roles) · base zone is a fixed cube (radius 8), not
the actual built footprint · friendly-fire enforcement is a forward hook (player-vs-player damage isn't implemented
yet) · the "find players" picker lists currently-online players. Needs `sync-client-libs` + a Unity client build.

### ★ Dyeable materials + craftable coloured light blocks — ✅ IMPLEMENTED (2026-06-13)
**Goal:** recolour any building material (e.g. blue mud) via one always-available recipe that works for all
materials and needs no separate dye, and craft glowing blocks whose light colour is chosen before crafting and which
actually illuminate when placed. Decisions: palette + fine picker (palette shipped; fine HSV picker is a follow-up) ·
only building/terrain materials are tintable · **full coloured voxel light propagation** · glow = material + 1 crystal ·
existing sun lighting + procedural flora/fauna tint must stay intact.
**How it works (one per-voxel colour modifier powers both):**
- **Item identity:** a dyed/glowing item is a composite key `base#t<rrggbb>g<rrggbb>` (`Shared/State/ItemKey.cs`),
  so inventory/crafting/networking/persistence stay string-keyed and a coloured stack never merges with the plain
  material. `GameContent.GetItem`/`MaxStackOf` strip the modifier to the base definition.
- **Storage:** `ChunkData` carries a sparse per-voxel `(tint, glow)` modifier; persisted in `block_edit` (+`tint`,
  `glow` columns, migrated) and synced via `ChunkDataMessage` sparse arrays + `BlockChanged.Tint/Glow`.
- **Crafting:** always-available `TintCraftIntent` → `HandleTintCraft` (no station): dye is a free 1:1 recolour, glow
  consumes a crystal; only `Tintable` building blocks qualify (curated set in `GameContent.MarkTintableDefaults`).
  UI: a "Färben/Dye" category in the Crafting menu with a colour-swatch palette (dye + glow sections).
- **Place/mine:** `HandlePlace` stamps the modifier from the item key; `BreakBlockAt` recovers the coloured item.
- **Rendering:** mesher emits dye as tint-mode 3 (luminance recolour, ungated → vivid everywhere); a per-channel
  coloured **light flood-fill** (placed glow blocks + dedicated `light_*`/strip lights as sources; lava/crystals
  unchanged) is baked per-vertex into TEXCOORD3 and added in `BlockAtlas.shader`. `ClientWorld` keeps a light-source
  registry so a lamp's colour propagates across chunk seams. Sun + flora tint paths untouched. Icons (menu + hotbar)
  tint via `IconResolver.Tint`.
- **Directional block-light shading (2026-06-14):** the mesher also bakes a dominant light DIRECTION per vertex
  (gradient of the light field → TEXCOORD4); `BlockAtlas.shader` (both URP + Built-in paths) shades placed lights
  with N·L diffuse + a Blinn-Phong glint (in the light's colour) + normal-map relief instead of a flat wash, with a
  fill floor so wrapped-around faces stay lit and a flat fallback where no direction was baked. No flood-fill/perf
  change; lamps now sculpt surfaces. (Chosen over real URP lights, which would leak through walls + cap the count.)
**Open/follow-ups:** fine HSV colour picker (palette only for now); coloured light is client-derived (not persisted) and
re-floods on chunk rebuild — diagonal-neighbour seams possible for lights right on a chunk corner; light radius = 9.

### ★ Planet bases (Grundstein) + station/base naming + space stations on the travel screen — ✅ IMPLEMENTED (2026-06-13)
**Goal:** the surface analogue of a space station — a player founds a named **base** on a planet/moon/asteroid by
placing a foundation stone, the stone marks the base on the planet map, and bases + stations can be named/renamed.
While there, surface the player's space **stations in the Map-menu body list** (travel = board directly, but only a
*visited* station), and **mark** bodies that carry the player's own station/base (with a description) + tag each own
station with its owner. Decisions: Q1 board directly · Q2 base = marker only · Q3 rename via E *and* the Map button ·
Q4 base core craftable from the start.
**What was built:**
- **Content:** new `base_core` block + item + workshop recipe (`stone`×6 + `iron_plate`×2, ungated); OpenAI-generated
  64×64 tile (`Resources/textures/base_core.bytes`); bilingual `block/item.base_core.*` + `ui.base.*` + `ui.map.*`
  (kind_station, owner, your_station, station_of, station_here, base_here, board, visit_to_unlock, rename[_base]).
- **Server (in the bundled server):** `GameServerBases.cs` — server-wide `ServerBase{Id,OwnerId,Name,Planet,Cell}`,
  founded on placing a `base_core` (one base per body per player; surface-only pre-check before any material is
  spent), removed on mine/blast, owner-only rename (`SetBaseNameIntent`, by body id so the Map button works
  cross-world), `BaseList` broadcast + a new `base_claim` SQLite table loaded server-wide at start. Station naming:
  `HandleSetStationName` (`SetStationNameIntent`, owner/admin) updates the structure + boardable registry + star-map
  body + live dock contact + the persisted row, then re-broadcasts the shared star map. Menu travel: `HandleTravel`
  routes a `SpaceStation` destination to `TravelToStation` → **boards it directly** (shared `EnterBoardedStation`
  refactored out of `BoardStation`), gated by `LandedBodies` (visited); boarding now marks the station visited
  (`MarkArrivedOnBody(stationId)`) + records each station's host body (`_stationHostBody`). `SendStarMap` now carries
  `NetBody.OwnerName` (station owner), `StarMapData.MyStationBodyIds` + `MyBases`. **Discovery persistence fix:**
  `PlayerState.LandedBodies`/`KnownSystems` are now in `PlayerSnapshot` (the visited gate survives a reload).
- **Client:** `NetworkClient` `BaseList` event + `SendSetBaseName`/`SendSetStationName`; `GameBootstrap.Bases` +
  `HasMyStation`/`MyBaseName`/`HasMyBase` + tracked `CurrentStationId`; the Map list/detail (`CraftingTechShipUI`)
  shows stations with a localized kind + owner, badges bodies with your station/base, offers **Board** for visited
  stations and an inline **Rename** field for owned stations/bases; `WorldMap` (key M) draws bases as labelled teal
  markers + a legend entry; `PlayerController` E-renames an owned base stone (and the station core inside your own
  station), both reusing the beacon name overlay.
- **Tests:** `PlanetBaseAndStationMapTests` (9) — base found/mine/one-per-body/rename/persist, station rename
  owner-gate + persist, the menu visited→board gate, and landed-history survives a reload. Full suite **500 green**.
- **Note:** adding `base_core` shifts the alphabetical numeric block ids (no id-stability layer in this project, as
  with every prior block addition) — fine for fresh worlds; a pre-existing dev save's block edits would shift.
- **Pending (real-world):** run `scripts/sync-client-libs.ps1` + a client build so the Editor picks up the new
  Networking DLL types + the `base_core` texture/.meta, then verify in-game (found a base, rename via E + Map, board a
  visited station from the menu).

### ★ In-game Wiki (Codex) + data-cube Arcade minigames (embedded browser) — ✅ IMPLEMENTED (2026-06-13, browser pending manual UWB install)
**Goal:** play small bundled HTML5/JS minigames in-game, and read an in-game wiki — both rendered by an
embedded browser. Minigames are found as "data cubes" on planets (download → personal collection); the wiki
shows general content always but gates Systems/Worlds to the player's discoveries. Highscores local-only,
not user-moddable. Full design: [docs/developer/MINIGAMES_AND_WIKI.md](docs/developer/MINIGAMES_AND_WIKI.md).
**What was built:**
- **Server (verified, in the bundled server):** `PlayerState.UnlockedGames` (+ snapshot persistence, SP+MP);
  `GameServerDataCubes.cs` scatters 0–N cubes per body (≈45% none) deterministically from the seed and
  validates proximity on download; net messages `DataCubeList`/`UnlockGameIntent`/`GameUnlocks` (NetCodec
  118–120); `ServerConfig.PlaceDataCubes`.
- **Client:** `EmbeddedBrowser` (one shared UWB surface, behind the `BBS_UWB` define) + `LocalContentServer`
  (loopback static server + dynamic `wiki/wiki-state.json`); `MinigameCatalog` (seed→game); `DataCubeView`
  (glowing textured cube, light, hum + download SFX, E-label) + PlayerController E-interact; `WikiUI` +
  `ArcadeUI` full-screen screens reached from **Codex**/**Arcade** buttons in the menu header; `GameBootstrap`
  mirrors `UnlockedGames` + builds the discovered-systems/worlds JSON; local highscores in `ClientSettings`.
- **Content:** **20 bundled minigames** ("data fragments": Blockfall, Asteroid Breaker, Circuit Weaver,
  Signal Tuner, Drone Rescue, Cargo Sorter, Blueprint Scramble, Orbit Slingshot, Laser Mirror Grid, Micro
  Miner, Star Map Memory, Alien Glyph Decoder, Reactor Balance, Oxygen Loop, Comet Courier, Docking Simulator,
  Data Fishing, Nanobot Repair, Planet Scanner, Void Solitaire) on one shared framework (`_shared/framework.js`
  + `theme.css`, uniform start/help/pause/result + blue-line theme) + a data-generated Codex SPA, all
  bilingual; assets `Resources/props/data_cube.png` + `Resources/audio/data_cube_{hum,download}.mp3`.
- **Reward:** finishing a fragment grants knowledge points (server, rating-scaled, repeatable, NetCodec 121
  `MinigameResultIntent`). Menu point is **DataQubes** (framed as recovering data fragments, not "games").
  A Creative world's "unlock all" also recovers every fragment for testing.
- **Pending manual step:** install UnityWebBrowser + set the `BBS_UWB` define (see the doc). Until then the
  browser shows a placeholder; everything else (cubes, downloads, collection, highscores) already works.

## ▶ Open backlog — priority order (updated 2026-06-07)
At-a-glance order of everything still open (new items added 2026-06-07 interleaved with the remaining
analysis-first tasks below). **Same workflow** unless noted: analyse → write the plan here → ask questions →
only then implement. Items marked *(analysis only)* must NOT be implemented yet.

### ★ Self-hosting: web portal client download + Velopack installer & auto-update — ✅ IMPLEMENTED (2026-06-13)
**Goal:** a LAN host runs the server and players grab the client from the server's own web page — no manual
zip hand-off. Built on the existing two-process model (`GameServer` UDP + `Api` HTTP); the `Api` already
shipped a `/portal` page with a placeholder download button.
**What was built:**
- **Installer (Velopack, MIT).** `scripts/publish-client-installer.ps1` runs `vpk pack` over
  `client/Build/Windows` → `BlocksBeyondTheStars-win-Setup.exe` + an update feed (`releases.win.json` +
  `*-full.nupkg`) in `artifacts/installer` (verified end-to-end: a 153 MB Setup.exe builds in ~1 min).
  Per-user install (no admin), Start-menu/Desktop shortcuts, uninstaller. `-ServeDir` stages it into a
  server install's `clients/`.
- **In-app auto-update.** `client/.../ClientUpdater.cs` runs `VelopackApp.Build().Run()` at
  `BeforeSplashScreen` and adds a manual "Check for updates" flow; new setting `ClientSettings.UpdateFeedUrl`;
  bilingual `ui.settings.update_*` keys; a **Software update** section in `UiSettings`. Velopack runtime
  vendored into `client/Assets/Plugins` by `scripts/sync-velopack-libs.ps1` (copy-if-absent).
- **Server serves it.** `Api` now hosts the feed at **`/updates`** (static files over `<install>/clients`),
  a **`/download`** route handing out the newest `Setup.exe`, and a redesigned **`/portal`**
  (`PortalPage.cs`) — a polished page carrying **both logos** (JuMaVe Games studio emblem + the game logo,
  recreated as self-contained inline SVG/CSS so no art ships) and the paste-in update URL. Built + rendered.
**Build + packaging done & verified (2026-06-13):** Editor imported the new Plugin DLLs;
`scripts/build-client.ps1` produced a fresh player with Velopack + all its runtime deps bundled
(`Client.dll` 15:59); `scripts/publish-client-installer.ps1` built a 154 MB `Setup.exe` + feed; the Api
serves `/portal` (both logos), `/download` (GET+HEAD, ranged, 200) and `/updates` (feed JSON, 200) — all
hit live. **Build fix:** the client asmdef has `overrideReferences:true`, so `Velopack.dll` +
`Newtonsoft.Json.dll` had to be added to its `precompiledReferences` — without it the Editor compiled but
the player build failed `CS0246` (the `using Velopack` is `#if !UNITY_EDITOR`).
**Remaining (real-world, needs two machines / a cert):**
- Install the Setup.exe on a second PC, join the LAN server, then publish a higher version and confirm the
  in-app **Settings → Software update** pulls + restarts.
- Optional: code-signing cert to remove the one-time SmartScreen prompt; a slimmer "join-only" client
  profile (drops the 68 MB bundled singleplayer server).

### ★ Bug — raw scene flashes before the loading screen on world entry — ✅ IMPLEMENTED (2026-06-13)
**Symptom:** starting a new game (and, in a weaker form, landing on a new/existing world) briefly showed the
star system, then the bare planet surface, and only *then* the loading screen.
**Root cause:** a gap between two decoupled loading screens. The shell `UiLoading` (canvas, sortingOrder 0)
is torn down the instant `AppShell.LaunchGame` flips to `InGame`, but the in-game `WorldLoadingOverlay`
(sortingOrder 75) only *armed* on the network `WorldLoadStarted` (arrives frames later) and, even then,
deliberately held while `SpaceViewActive` — so the freshly-built `WorldRig` rendered its raw scene
uncovered. The landing variant was the overlay's 0.30s **fade-in** letting the surface bleed through after
the in-space descent ended.
**Fix (`WorldLoadingOverlay` + `WorldRig`):**
- New `WorldLoadingOverlay.PrimeForInitialLoad()` raises the veil **opaque before the first in-game frame**,
  called synchronously from `WorldRig.Build` right after the overlay is added — seamless cut from the
  already-opaque shell loading screen. Flagged `_initial`, it bypasses the "hold through the in-space
  descent" rule (no star-system flash) and holds until the join confirms (`_joinSeen`, set on
  `WorldLoadStarted`) **and** `WorldReady` (gate: `WorldReady && (!_initial || _joinSeen)`), then the normal
  fade-out reveals the world. (Join always spawns on a surface, so `WorldReady` reliably fires; `MaxShow=25s`
  is the backstop.)
- `Raise()` now snaps to **opaque instantly** (was a 0.30s fade-in) so a landing/board never flashes the
  surface/old view; only the reveal still fades (`FadeOut`, `FadeIn` removed).
- `[DefaultExecutionOrder(100)]` on the overlay so its `Update` runs after `SpaceView` clears
  `SpaceViewActive` in the same frame — raising the veil before that frame renders kills the 1-frame
  landing flash deterministically.

### ★ Travel rework: current-vs-hyperspace systems, visit-gated quick-travel, Instant Travel option — ✅ IMPLEMENTED (2026-06-13)
**Goal:** the Map/travel screen distinguishes the current system from distant ones, quick-travel is limited
to worlds you've actually visited (unless an "Instant Travel" option is on), and a never-visited system is a
single hyperjump entry until you've been there.
- **Per-player progression (server):** `PlayerState.LandedBodies` + `KnownSystems` (persisted), marked on
  every arrival (`MarkArrivedOnBody`/`MarkSystemKnown`) at all landing sites (travel, manual flight landing,
  join, respawn, station return). Sent to the client in `StarMapData.LandedBodyIds`/`KnownSystemIds` (the
  current body/system always counts, covering legacy saves).
- **Instant Travel world rule** (`GameRules.InstantTravel`, default off) — plumbed through `ServerRules` +
  `SetWorldRulesIntent` (a world-admin toggle in Settings), persisted in the save's rules override.
- **Quick-travel gate (`HandleTravel`, `quickTravel` flag):** the travel-screen path is refused for a body
  you've never landed on unless Instant Travel is on; manual flight landings (`HandleLeaveSpace`) and the
  test/util `Travel()` bypass the gate (you physically flew there) and mark the body visited.
- **Hyperjump to an unvisited system (`HyperjumpToSystem` + `HyperjumpSystemIntent`, NetCodec 117):** arrive
  in FLIGHT in the target system (anchored on its first landable body, `SpaceState.Hyperjump` plays the warp),
  not landed — you then fly to its worlds and land manually. Needs a jump generator.
- **Client Map tab:** sidebar grouped under fitted "Current system" / "Hyperspace" headings (current system
  first); current-system view carries the launch/leave button + reachable targets; a known distant system
  shows its targets; an unknown one is a single "Hyperjump to this system" action with its bodies hidden.
  Per-world travel buttons are gated (locked worlds show a "fly there to unlock" hint). The right pane shows
  an animated 2-D mini star map (`SystemMapWidget`) of the selected known system. Instant Travel toggle added
  to the world-options settings. New locale keys in en/de.
- **Verification:** 491/491 tests green (incl. a new quick-travel gating test + locale parity); client batch
  build green.

### ★ Selectable music: Suno track library + synth, with menu toggle — ✅ IMPLEMENTED (2026-06-13)
**Goal:** add 23 Suno-generated ambient tracks as an alternative to the existing synth music, selectable in the
menu, with fade in/out, fitting per-context placement (random when several fit), and music/SFX kept on separate
volume buses. Splash stings left untouched.
- **Assets:** the 20 supplied tracks + 3 newly-generated gap tracks (cave / verdant / planet-night) copied to
  `client/Assets/Resources/music/*.mp3` with descriptive `music_*` names and **Streaming** import (so the
  multi-minute songs don't sit decompressed in RAM). Kept out of `Resources/audio/` so `ClientAudio` doesn't
  preload them. Prompts + context mapping documented in [docs/developer/MUSIC_TRACKS.md](docs/developer/MUSIC_TRACKS.md).
- **Director:** `ClientMusic` is now a **persistent AppShell-owned** component (spans splash→menu→loading→
  in-game, closing the old main-menu-hook gap; removed from WorldRig, reads `AppShell.CurrentBoot`). Two modes
  (`ClientSettings.MusicMode` Synth | **Tracks** default), toggle row in `UiSettings` (en/de keys). Context from
  shell phase + world state (biome, ship interior, station/`NearVendor`, space, underground, time-of-day);
  random pick within a pool; re-roll at the loop seam for variety; ~2.5 s cross-fade. Combat stays the tense
  **synth** mood (library is all-calm). Music muffles underwater (own low-pass via `ClientAudio.Submerged`);
  single-AudioListener handover managed across menu↔game. Master=`AudioListener`, music=`MusicVolume`,
  SFX/ambience=`SfxVolume` (already separate sliders).
- **Variance pack (B-sides) — 🎵 WIRED, awaiting assets (2026-06-15):** the single-track contexts (each
  biome planet, station, menu, loading, cave) only had one song, so a long stay looped it. `ClientMusic`
  pools now also reference `_2` B-sides — `music_planet_{ice,desert,lava,toxic,ocean,cave}_2`,
  `music_planet_verdant_2`, `music_multiplayer_hub_2`, `music_main_menu_2`, `music_loading_2`,
  `music_idle_default_2`, `music_explore_planet_2`. Each is guarded by the existing
  `RemoveAll(LoadMusic == null)`, so it activates the moment its `.mp3` lands in `Resources/music/` (no
  further code change). **Edit-ready Suno prompts** for all of them are in
  [docs/developer/MUSIC_TRACKS.md](docs/developer/MUSIC_TRACKS.md) → *Variance pack*. **Open:** owner generates the tracks →
  drop in → Unity client rebuild to import + compile.
- **Open:** needs a Unity client rebuild to import the new audio + compile; not yet play-verified in-engine.

### ★ Landed ship as a real object instead of a world stamp — ✅ IMPLEMENTED (2026-06-13)
**Goal:** the landed ship should be its own placed voxel OBJECT (own mesh, own colliders, hull-paint
applies) instead of being STAMPED into the world block grid.

**Shipped (decisions: interior world converted too; pads levelled in worldgen; hull/modules protected
but interior furnishing allowed + persisted; other players' parked ships as painted objects):**
- **Server:** `LandedShip` (WorldManager) replaces `ShipStamp`; `PlaceLandedShip`/`RemoveLandedShip`
  (GameServerShipStructure.cs rewritten) park/remove the per-player structure object on the pad —
  nothing is written into the world grid. `BuildShipStructure` is now the FULL ship everywhere:
  station marker blocks + `StationCells`/`DoorCells`/`MedbayCell`, box-ship interior dressing
  (ceiling lights, wall strips, windows), floor guarantee, room accents, and a `Baseline` snapshot
  (design cells) taken before the persisted player deltas apply. Launch (`EnterSpace`) removes the
  parked object + always rebuilds the flight structure from baseline+deltas (lossless — all ship
  edits persist now); logout removes the object; a repaint while landed re-broadcasts it
  (`HandleSetAppearance`). The walk-in ship interior world (`shipint:`) places the same object
  instead of stamping — one grid everywhere (EVA edits visible inside, furnishing visible outside).
- **Edit rules (on foot, landed + interior):** `HandleLandedShipEdit` — mine only NON-baseline cells
  ("The ship hull cannot be damaged." / "Ship modules cannot be removed."), place only into air
  INSIDE the ship bounds and attached to a cell; inventory/drops mirror the EVA flow; every edit
  persists as the same per-cell structure delta (`SetStructureBlock`) the space EVA uses. Station
  markers are now also protected in the space EVA path.
- **Worldgen:** `WorldGenerator.FlattenLandingPads` levels every planned pad at generation time
  (surface block at pad height, clear above, caves plugged 8 deep) — `BuildLandingPads` hands the
  deterministic pad set to the generator before any pad chunk generates; per-landing terrain
  mutation (keep-out, foundation, doorstep) is gone.
- **Net:** `LandedShipState` (tag 116) place/replace/remove + world-join snapshots; cell edits ride
  the existing `StructureBlockChanged`. Door registry probes the structure grid for ship doors.
- **Client:** `LandedShipView` renders every parked ship (ChunkMesher + hull paint + MeshColliders,
  torus-seam-aware); `PlayerController.AimTarget` marches ship cells too and routes mine/place to
  `StructureEditIntent` (placing inside the cabin = furnishing); `ComputeExposedToSky` counts
  `Aboard` + ship roofs as covered; rain dies on ship hulls (`WeatherFx3D`).
- **Migration:** placing a ship deletes legacy STAMPED-hull block edits in its pad volume
  (`DeleteBlockEdits` + chunk regen + re-stream), so old saves don't show a double hull.
- **Verification:** full suite green (490/490, incl. wreck repair/claim — that mechanic is untouched
  world-stamp machinery; claiming flows into the new placement via SwitchShip). Client batch build green.
**Known limits (playtest checks):** entering the ship is now a 1-block step up (the object sits ON
the pad; jump in — the old stamp was flush); ship cells mine instantly (EVA parity, no hold
progress); the aim marker/scanner don't highlight ship cells; footstep sounds inside the ship read
the world grid (air).

**Original analysis (2026-06-12):**

**How it works today (the stamp):** `StampShip()`/`StampShipLayout()`
([GameServerShipStructure.cs](src/BlocksBeyondTheStars.GameServer/GameServerShipStructure.cs)) writes the
hull cell-by-cell via `_world.SetBlock` at the player's claimed pad — every cell also persists as a
`block_edit` row in SQLite (`ServerWorld.SetBlock` → repo). It mutates terrain on the way: flora keep-out
clear, cave-plugging foundation (depth 6), flush doorstep carving. Called at world load, on landing
(`RelocateToAssignedPad`), on return from a station, on ship switch while landed
(`ClearStampedShip` + restamp + `RestreamShipChunks`), and for the walk-in ship INTERIOR in space (a
per-player void world `shipint:<id>` gets the same stamp). Launching does NOT clear the stamp — the hull
stays parked on the pad as world blocks. Consumers keyed off the stamp:
- aboard detection `ShipInteriorContains` → cargo crafting, oxygen regen, inventory scope
- mining protection `IsShipBlock`/`BlockInStamp` (hull indestructible)
- station markers (`_stations` + `UseStation` reach), heal-tank respawn, doors (`CurStamp.Doors` →
  `RegisterDoors` server door entities), creature/NPC keep-out (`EntityBlockedByShip`), minimap anchor
  (`SendShipPlacement`), restamp materialize FX (client `ShipBuildFx` keyed on `ShipStations` resend)
- client side: the hull renders + collides as ordinary world chunks; on-foot aiming/mining ray-marches
  the WORLD grid; weather (`WeatherFx3D` per-column sky scans), `ComputeExposedToSky`, footstep/swim
  block queries and interior skylight all read the world grid — the stamped hull "just works" for them.

**Target model (ship = object):** reuse what the flight scene already proved. The server already builds a
sparse `SpaceStructure` (`ship:<player>`) from the design for space (item 20 S1/S2) with EVA collision +
`StructureEditIntent`/`StructureBlockChanged` edit routing; the client already meshes it with colliders
(`SpaceView.BuildVoxChunks`) and — since today — with the hull PAINT in the mesh. A landed ship becomes
the same structure anchored at a world position; nothing is written into the world grid.

**Work packages (server):**
1. `LandedShip` state per player per world (structure + anchor + entry pose) replacing `ShipStamp`;
   placement keeps `PadGroundY`; decide terrain policy (keep light pad flattening/foundation as pad
   worldgen, or none). New net message (design + anchor + owner; REGISTER in NetCodec!) sent on world
   join/landing/switch + a removal on launch — launch finally removes the parked ship (today's stamp
   stays behind; old-pad residue also persists in `block_edit` forever).
2. Re-key interior systems to structure-local coords: aboard test, stations + reach, heal-tank/respawn,
   doors (entities at anchor-transformed positions), creature keep-out (already geometric boxes).
3. Edit routing on the surface: aim hits on the structure go to `HandleStructureEdit` (exists for space);
   decide whether the landed hull stays indestructible (today) or becomes editable like an EVA.
4. Ship-interior world (`shipint:`) — keep stamped (void world, harmless; smallest scope) or convert too.
5. Persistence: nothing new — the structure derives from the persisted design at landing; EVA edits
   already live in the space instance structure. Big win: no more `block_edit` pollution per landing.
**Work packages (client):**
6. `LandedShipView`: mesh the structure at the anchor (torus seam-aware like other world entities) with
   colliders + hull paint; materialize FX keys off the new message instead of `ShipStations`.
7. Walking works free of charge — `PlayerController` is a Unity `CharacterController`, the structure's
   MeshColliders carry it (same as world chunks). BUT aiming/mining ray-marches the world grid by design
   (chunk-collider race) → add a structure DDA pass (exists as the EVA `AimShipVoxel`) + route to
   structure edits.
8. Composite "what's above/below me" queries: weather drops would fall through the object hull and the
   lens-wash/`ComputeExposedToSky` would say "exposed" inside the ship. Simplest: treat the
   server-authoritative `Aboard` flag as covered + let the per-column rain scan also test the landed
   structure's bounds. Footstep-material queries inside the ship read terrain → minor, fix or accept.
9. Interior light: the structure mesher already darkens interiors via its own grid (proven by the space
   cabin + `_Sc_Indoor`); the URP `BlockAtlas` ShadowCaster gives the object a real ground shadow.
**Payoffs:** hull paint applies when landed (incl. OTHER players' parked ships in their colours — today
they're colourless stamps); no ghost-block/broadcast class of bugs; no terrain mutation or save
pollution; launch/land lifecycle becomes explicit (parked ship disappears when you fly).
**Risks:** many implicit "ship = world blocks" assumptions (server tests around stamps, spawn safety
when the structure collider streams in late, seam wrap for the anchored object); scope is a staged,
item-20-sized refactor (recommend 3-4 shippable stages mirroring the WP list above).
**Cheaper alternative (if the motivation is mainly the paint):** keep stamping, send the ship footprint +
owner colour to clients, and let the WORLD mesher pass a paint resolver for `iron_wall` cells inside
those bounds — roughly a tenth of the effort, but keeps every other stamp drawback.
**Open questions:** (1) convert the space ship-interior world too, or leave stamped? (2) terrain policy
under the object (flatten pad in worldgen vs none)? (3) landed hull editable or indestructible?
(4) confirm other players' landed ships should appear as painted objects.

### ★ Ship hull colour pick has no visible effect (reported 2026-06-12) — ✅ FIXED (real voxel-hull tint)
**Symptom:** cycling the hull colour in the menu's Ship → paint tab updates the small swatch, but the
ship itself never changes — neither the rotating menu preview nor the ship in flight.
**Root cause (NOT the URP/linear migration):** the hull tint (item 32) only ever applied to the
*hand-built silhouette* ship (the `LitTex("iron_wall", tint)` materials). Since `43ea96c` (2026-06-10,
"menu previews show the real ship") the paint tab renders the player's REAL voxel ship via
`ShipMeshBuilder` with the shared block-atlas chunk materials, which carry no per-ship tint —
`ShipPreviewRig._hullMat` stays null and `SetHullColor()` is a null-guarded no-op. The voxel flight
ship has the same (documented) gap: `SpaceView.BuildVoxelShip` — "_hullMat stays null". Because the
voxel design is sent on every space entry, the silhouette fallback (the only tintable path) is
practically never shown, so the tint is visually dead everywhere. WP-1 + the URP port are exonerated:
`LitColor` kept `_Color` in both SubShaders, it is in `m_AlwaysIncludedShaders`, and the build-time
tints go through `ShaderColor.Srgb()` — only the timing (RP work landed the same days) made the
graphics engine look like the culprit.
**All silently no-op tint sites (each null-guarded):**
1. menu preview — `ShipPreviewRig.SetHullColor` (voxel path → `_hullMat` null)
2. own flight ship — `SpaceView` live retint (~line 199) + `BuildVoxelShip` (no tint by design)
3. remote players' space ships — `SpaceView` sets `av.HullMat = null` on the voxel path (~line 2460)
4. remote landings/launches on planets — `ShipTransitView` voxel path untinted (silhouette fallback tints)
The colour itself still round-trips end-to-end (`ClientSettings.HullColor` → `GameBootstrap.HullRgb` →
`SendAppearance` → `PlayerSession.HullColor` → `NetSpacePlayer.Hull`) — persisted + broadcast but unused.
**Secondary find (a real WP-1 leftover):** `ShipPreviewRig.SetHullColor` assigns `_hullMat.color = hull`
WITHOUT `ShaderColor.Srgb()`, so the one still-working retint path (silhouette fallback) retints in the
wrong colour space (washed out vs the Srgb-converted build-time tint).
**Fix (option a, real voxel-hull tint — implemented 2026-06-12):** the ship meshers now paint
hull blocks per face. `ChunkMesher.Build` takes an optional `paintTint` resolver; a painted face
raises the TEXCOORD1.y tint-mode flag to **2** (1 stays flora) and carries the hull colour in
TEXCOORD2.yzw. `BlockAtlas.shader` (both SubShaders) multiplies that tint into the albedo for
mode-2 faces — same look as the old tinted silhouette (`_Color * tex`), independent of the
`_Sc_FloraTint` global so it works in space too. `ShipMeshBuilder.HullPaint(content, colour)` is
the shared resolver (paints `iron_wall`, Srgb-converted at the mesh boundary) and
`BuildVoxelShip(..., Color hull)` passes it through. Wired at all 4 sites: menu preview
(`ShipPreviewRig` — re-meshes the small voxel model on colour change, silhouette retint got the
missing `Srgb()`), own flight ship (`SpaceView.RebuildShipVoxels`; a live colour change re-meshes
instead of the old null-guarded material no-op), remote ships in space (`SyncRemotePlayers`
rebuilds when `rp.Hull` changes), and remote landings/launches (`ShipTransitView`, with the
`fx.Hull == 0` → default-steel guard). **Known limit (as designed in item 32):** the player's OWN
landed ship is STAMPED into the world grid and meshes via the world chunk path without a paint
resolver — tinting there would tint every `iron_wall` in the world; left as the documented
follow-up.

### ★ Rain invisible when looking out of a cave (reported 2026-06-12) — ✅ FIXED
**Symptom:** during rain, stepping into a cave and looking out shows no rain outside the entrance;
stepping back out, it rains again. **Root cause:** `WeatherFx3D` gated the whole 280-drop pool on
`Game.ExposedToSky` — the *player's* head-up scan — so being covered hid every drop in the world,
including those over open ground in view. **Fix:** drops are now gated per *column*: `TryRespawn`
only places a drop where the sky is open above its spawn point (per-column cache, 1 s TTL, same
scan as `ComputeExposedToSky`), and a falling drop dies on entering a solid block — so rain stays
visible outside the cave mouth and no longer falls through roofs/ceilings/terrain into view.
Storm fog still keys on the player's own exposure (global fog would fill the sheltered cave/room).
Screen wash (`WeatherFx`) + muted rain bed (`ClientAudio`) stay player-gated on purpose: sheltered =
no drops on the lens, muffled audio.

### ★ Professional-look pass (WP-1…16) — ✅ IMPLEMENTED (2026-06-12); PT-1/PT-2 playtests pending
Driven by [docs/developer/PROFESSIONAL_LOOK_GAP_ANALYSIS.md](docs/developer/PROFESSIONAL_LOOK_GAP_ANALYSIS.md) and
[docs/developer/PROFESSIONAL_LOOK_IMPLEMENTATION.md](docs/developer/PROFESSIONAL_LOOK_IMPLEMENTATION.md)
(one commit per WP). Style rules now codified in [docs/developer/ART_BIBLE.md](docs/developer/ART_BIBLE.md).
**Verification:** client batch builds green, 475 tests green (locale parity incl. the new block
names). **Open:** the two human playtest checkpoints — **PT-1** (linear-color-space parity: 8
scenarios + retune candidates flora blend / grade strength / emission ×2 / bloom threshold /
vertex AO) and **PT-2** (one structured pass over WP-2…15: fonts/DE text, camera toggle, O₂ +
damage pulses, mining flash + fly-in, craft/unlock celebration, UI transitions, music contexts,
block variants, ship rooms/cockpit/decor/thrusters/damage/ghost+materialize).
- **WP-1 Linear color space** ✅ — `m_ActiveColorSpace: 1`; `ShaderColor.Srgb()` boundary helper at
  every script→shader colour upload (~25 files); normal atlas `linear: true`; engine-managed colour
  properties intentionally unwrapped; Built-in-RP `PostFx` fallback stays gamma-tuned.
- **WP-2 Sci-fi UI font** ✅ — bundled Rajdhani-Medium (OFL, DE glyphs verified), first in
  `UiKit.Font`; NOTICES updated.
- **WP-3 Camera-motion toggle** ✅ — `ClientSettings.CameraMotion` gates bob/FOV-kick/shake
  (settings row, DE+EN).
- **WP-4 Event post-FX** ✅ — low-O₂ pulsing blue vignette + escalating two-beep alarm, red damage
  vignette kick, `UrpScenePost.Burst()` chroma/grain API; all honour ReducedEffects.
- **WP-5 Mining loop** ✅ — final-hit flash + the mined tile flies into the hotbar (id sampled
  during MiningProgress).
- **WP-6 Craft/unlock celebration** ✅ — card pulse + floating label on craft; tech-node pulse,
  unlock label + `tech_unlock` fanfare on blueprint unlock (inventory-diff detection).
- **WP-7 UI transitions** ✅ — `UiKit.TransitionIn` (fade+rise 0.14 s) on menu/tab + scan/wreck
  panels; hotbar selection tick; instant under ReducedEffects (`UiKit.ReducedMotion`).
- **WP-8 Music contexts** ✅ — menu/planet/space/combat cross-fade; four ElevenLabs ambient loops
  (`music_*.mp3`) + mood-matched procedural fallbacks; combat inferred from hull+shield drops.
  SOUND_DESIGN §11 shipped. **Extended 2026-06-13:** persistent AppShell-level director (main-menu +
  loading music now hooked) + a selectable 23-track Suno library (see the music entry up top).
- **WP-9 Block variants** ✅ — 2 procedural variant tiles per natural block + 90° top/bottom UV
  rotation from a deterministic world-position hash; ship/tech panels excluded.
- **WP-10 Ship room identity** ✅ — 7 new blocks (light strips, panels, hazard floor, engine
  nozzle); `PaintStationAccents` lays per-room 3×3 floor pads (both stamp paths); box ships get
  cyan side-wall light strips; restamps re-stream chunks (no ghost blocks).
- **WP-11/12 Cockpit + station decor** ✅ — `StationDecorView`: cockpit console + animated screen +
  holographic system map (star-map data, proximity-gated); medbay tank pulse; lab/console
  terminals; workshop sparks. Space view gains a flight readout (SPD/THR/HDG + HULL/SHD).
- **WP-13 Thruster/transit FX** ✅ — throttle-scaled exhaust particle stream; pad-dust bursts on
  other players' touchdown/lift-off; `engine_nozzle` glow keeps landed ships alive. (Own-launch
  pad dust deferred — camera sits inside the ship during the sequence.)
- **WP-14 Ship damage** ✅ — hit sparks at the hull in combat; aboard: interior spark bursts
  <50% hull, pulsing red emergency light + `hull_alarm` <25% (hysteresis at 30%).
- **WP-15 Build preview + materialize** ✅ — ship-editor placement ghost (green/red, pulsing);
  restamp materialize sweep (rising holo ring + shimmer, keyed off ShipStations changes).
- **WP-16 Docs** ✅ — `docs/developer/ART_BIBLE.md` (normative style + palette + checklist); stale
  ADVANCED_GRAPHICS_PLAN header fixed; gap-analysis statuses closed.

### ★ In-game multiplayer hosting ("Host Game") — ✅ SHIPPED (2026-06-12; 475 tests green)
**Decisions (2026-06-12):** name verification = **token-based name ownership** (first join under a name
claims it via a per-install client secret; later joins must present it) + online-duplicate rejection;
hostable worlds = **the existing singleplayer saves** ("open to LAN" style, one world list); hosting
port stays **31550** (no clash with a locally running dedicated server on 31415).

**Shipped (all three stages):**
- **S1 — server.** `JoinRequest.Token` (contractless MessagePack → wire-compatible); `HandleJoin` rejects
  a name that is already online (case-insensitive; `PlayerId == name`, so a duplicate would alias the
  same state) and enforces token ownership: `PlayerState.NameTokenHash` (SHA-256, persisted via
  `PlayerSnapshot`) — claimed on first tokened join (legacy/tokenless records adopt the first token they
  see), mismatches rejected with a DE/EN-localized reason. New `--admins "a,b"` CLI →
  `ServerConfig.AdminPlayers`. Tests: `NameVerificationTests` (duplicate-online reject, claim across
  server restarts, legacy adoption, --admins grants Admin).
- **S2 — client identity.** `ClientSettings.PlayerName` (persisted) + `ClientSettings.PlayerToken`
  (GUID, generated on first load); the connect dialog gained **player name** + **server password**
  fields (name saved on connect); `WorldRig`→`GameBootstrap` now pass password + token into the join. A
  refused join no longer strands the player on the loading screen: `GameBootstrap.JoinRejectedReason` →
  AppShell returns to the menu and shows the reason (`MenuNotice`).
- **S3 — host flow.** Main-menu **Host Game** opens the SAME world picker in host mode (any
  singleplayer save or a new world) plus a host bar: **max players** (2–16 stepper) and an optional
  **join password**. `AppShell.StartHostWorld` → shared `StartLocalWorld` →
  `LocalServerLauncher.Prepare(..., maxPlayers, password, serverName, adminName)` (singleplayer defaults
  unchanged: cap 1, name "Singleplayer"); the host's name goes into `--admins`, the host auto-joins
  127.0.0.1:31550 exactly like singleplayer (first join of a fresh world = WorldAdmin). The LAN address
  (`AppShell.LocalLanIp`) is announced once in chat + as a HUD toast (`ui.host.address_line`). Quitting
  the host stops the server (LAN-session semantics). Docs: USER_MANUAL §1 + SELF_HOSTING §3/§7.
  Internet play stays manual port-forwarding (no UPnP).
**Goal:** launch a multiplayer server from inside the game. Host picks a world + max players (+ optional
password), the bundled server starts locally, the host joins immediately and is admin; joining players
need **name verification**.

**Current state — the foundation already exists (this is the singleplayer path):**
- Singleplayer IS in-game hosting already: `AppShell.StartSingleplayerWorld` (`AppShell.cs:217`) prepares
  `LocalServerLauncher` (`LocalServerLauncher.cs`), which spawns the bundled dedicated server from
  `StreamingAssets/server/` as a child process on loopback:31550 with `--max-players 1`, then the client
  connects to it like any remote server (loading screen first; `Process.Start` on a background thread so a
  Defender first-scan can't freeze the menu; server stopped on leave/quit).
- The dedicated server already takes everything hosting needs via CLI (`ServerConfig.ApplyCommandLine`):
  `--port/--world/--name/--max-players/--password/--seed/--view-distance` + all world options. The
  LiteNetLib UDP transport is multi-client; presence/docking/trading multiplayer protocol is shipped.
- Admin roles exist (`PlayerRole`: Player/Moderator/Admin/WorldAdmin, `PlayerState.cs:7`): the FIRST player
  ever to join a fresh world becomes **WorldAdmin** (`GameServer.cs:1349`); names in
  `ServerConfig.AdminPlayers` get Admin on join (`GameServer.cs:1281`). So "host = admin + joins
  immediately" falls out naturally: the host connects to 127.0.0.1 before anyone has the address → first
  join → WorldAdmin. (Hosting an existing singleplayer save: the host is already its WorldAdmin.)

**What's missing (gaps):**
1. **No name verification — the key gap.** `HandleJoin` (`GameServer.cs:1249`) never checks whether the
   name is already online, and `PlayerId == name` (`GameServer.cs:1343`) — a second client joining with
   the same name loads the SAME `PlayerState` → state corruption + identity/admin spoofing (joining with
   the host's name = becoming WorldAdmin). Matches the old backlog note "multiplayer player-name
   reservation".
2. **Client player name is hardcoded** — `AppShell.PlayerName = "Pilot"` (`AppShell.cs:32`); no UI edits
   it and it isn't persisted in `ClientSettings` → all default clients collide on "Pilot".
3. **Join dialog** (`UiMainMenu.cs:61`) has only Host + Port — no name, no password field; `WorldRig`
   never sets `boot.Password` (`GameBootstrap.cs:550` would send it) → password-protected servers are
   unjoinable from the UI.
4. **No "Host Game" UI/flow** — `LocalServerLauncher.Prepare` hardcodes `--max-players 1` + server name
   "Singleplayer"; needs maxPlayers/password/serverName parameters + a menu entry.
5. `AdminPlayers` has no CLI arg (config-file only) — a small `--admins` arg makes "host is always admin"
   robust even on saves where someone else joined first historically.

**Plan (staged, each stage shippable + tested):**
- **S1 — server: name reservation + verification.** (a) Reject a join whose name is already online
  (`JoinRejected("Name already in use")`). (b) Name ownership: the client sends a per-install secret token
  in `JoinRequest` (GUID, persisted in `ClientSettings`; new field → NetCodec registration!); the first
  join under a name stores the token's hash with the player record; later joins must present the matching
  token or are rejected. Protects the host/admin identity from spoofing. (c) `--admins "<names>"` CLI →
  `ServerConfig.AdminPlayers`. Unit tests for all three.
- **S2 — client: identity + password UI.** Editable player name persisted in `ClientSettings` (default
  "Pilot"); join dialog gains name + password fields; `WorldRig` passes the password into
  `GameBootstrap`.
- **S3 — host flow.** "Host Game" menu path (reuse the singleplayer world picker + world options + a small
  host panel: max players, optional password). `LocalServerLauncher.Prepare` gains
  maxPlayers/password/serverName; pass `--admins <hostName>`; after launch the host auto-joins
  127.0.0.1 exactly like singleplayer (same loading flow); show the LAN address (+ copy) so friends can
  join; quitting the game stops the server (LAN-session semantics, like the singleplayer launcher today).

**Feasibility: YES** — hosting reuses the proven singleplayer mechanism; effort ≈ 2–3 days total
(S1 ~1d, S2 ~0.5d, S3 ~1d). Internet play stays manual port-forwarding for now (no UPnP).

**Open questions (asked 2026-06-12):** (1) verification strength — online-collision reject only, or
token-based name ownership (recommended; without it the admin can be impersonated when offline)?
(2) hostable worlds = the existing singleplayer saves ("open to LAN" style, recommended) or a separate
multiplayer world list? (3) hosting port: keep 31550 (current SP port) or the dedicated default 31415?

### ★ Docs sync (2026-06-12) — ✅ DONE
README/AGENTS/manual/AI-backend doc brought up to the shipped state: README status section rewritten
(playable game, torus worlds, 17 planet types, VEGA, multiplayer; 471 tests), repo layout + build now
include `ai-backend/`, `tools/` and `scripts/build-client.ps1`, new "Optional AI backend" section, URP in
the tech stack. USER_MANUAL gains swimming/diving, VEGA (incl. **N** advance + **P** autopilot keys,
AI cores, memory fragments), the terrain scanner and the world-creation options. AI_MISSION_BACKEND.md
now covers L0–L3 + VEGA banter (dead `plans/` reference removed). This TODO header: test count 302→471,
Built-in RP→URP, asset-gen convention updated to blanket-approved.

### ★ Item 21 — World variety: more, stranger, more varied worlds (V1–V5) — ✅ DONE (V1–V5 shipped + pushed, 2026-06-10; 405 tests green, client built). Follow-ups below.
Decisions (asked & answered 2026-06-09): scope **full V1–V5**; priorities **terrain shapes + exotic world
types + flora variety** (fauna/habitats included but secondary); exotic-world frequency **balanced**
(noticeable, not dominating). OpenAI textures + ElevenLabs sounds approved for any new blocks/flora.

Current state (analysis): 8 selectable planet types, all equal weight; every world uses ONE FBM heightmap
blended across the SAME global 5 terrain archetypes (flats→rolling→hills→mountains→canyons), so worlds read
structurally alike. Only `varied` is multi-biome (`ResolveBiomes`), wasting fauna `BiomeAffinity` on the
other 7. Flora (28 archetypes, `FloraCatalog`) is gated ONLY by surface block → same pool on every
same-surface world; per-world *hue* varies, shape doesn't. Fauna has 4 habitats (Land/Air/Water/Lava);
caves are lifeless; water/lava life gated to a few planet keys; no per-planet creature theme.

Plan (staged, each phase shippable + tested):
- **V1 — new world types (data-first): ✅ DONE (2026-06-09, 405 tests green).** Selectable planet types
  8 → **17**. Added `PlanetType.SpawnWeight` (frequency in planets.json; UniverseGenerator uses it) with
  balanced rarity tiers. New types: **ocean, savanna, highland, crystal_living, ashen** (from existing
  blocks) + **tundra, fungal, corrupted, salt_flats** (new blocks). 6 new blocks (snow, salt, mycelium,
  alien_grass, deepslate, granite) with OpenAI textures + bundled .bytes + bilingual locale. Multi-biome
  pools added to rocky/ice/desert/jungle (+ the new types). **Interior variety (per the follow-up ask):**
  per-world cave-frequency jitter, per-world ore-richness, and a per-world deep "mantle" rock chosen from
  basalt/deepslate/granite — so two worlds of one type differ underground, not just on the surface. **New
  materials embedded in crafting (per the follow-up ask):** all 6 new blocks are placeable building
  materials that drop themselves; granite→stairs, deepslate→concrete, salt→glass (flux), snow→water,
  mycelium/alien_grass→plant_fiber (→ emergency_ration) — no dead-ends. New surface blocks added as flora
  hosts so exotic worlds grow thematic flora (fungal→glowing fungi, corrupted→alien meadow, tundra→frost,
  salt→succulents, ashen→ember).
- **V2 — per-planet terrain style + new archetypes: ✅ DONE (2026-06-10).** `PlanetType.TerrainStyle` +
  `StyledHeightOffset` reshape the heightmap per world: desert=dunes, highland/ashen=mountains,
  crystal/crystal_living=spires, rocky=mesa (terraced), corrupted=canyons, ocean/swamp/salt_flats=flats,
  tundra/fungal=hills (others keep the mixed blend). **Rivers**: winding water channels carved flush (linear
  ponds) on wet worlds, kept to low/mid terrain. Deserts/salt-flats set dry.
- **V3 — flora variety + structures: ✅ DONE (2026-06-10).** 5 alien flora archetypes (tendril, glow-bulb,
  gasbloom, alien-fern, crystal shardbloom) with OpenAI textures, hosts (alien_grass/mycelium/crystal),
  drops + render (solid/leafy + glow). **Giant mushrooms** (mushroom_stem + mushroom_cap, new textures) as a
  multi-block worldgen stamp on fungal/mycelium worlds. Flora theme reaches exotic worlds via the V1b hosts.
  (Crystal-spire structures are covered by the V2 "spires" terrain style.)
- **V4 — fauna habitats: ✅ DONE (2026-06-10).** New `CreatureHabitat` **Cave** (subterranean: spawns in real
  underground cave pockets via a cave-floor probe; mostly eyeless + always bioluminescent; crawling/many-legged)
  and **Amphibian** (shoreline: spawns in/beside water, finned, teal, swims). Gated per world (caves need
  CaveThreshold>0; amphibian needs water life); server cave-floor spawn + roam-height + shore check; client
  aquatic hides + swim for amphibians, glow path for cave dwellers. (8-leg already supported; richer morphology
  beyond legs/eyes deferred.)
- **V5 — signature alien terrain: ✅ DONE (2026-06-10, headline shipped).** `PlanetType.FloatingIslands` +
  worldgen sky-island slabs (grass-topped decks on tapered rocky underbellies, scattered by a region mask) high
  above the surface — new rare **skylands** world (low flats ground + islands) and the **corrupted** world also
  floats. Reached by flying up / building.

**Item 21 follow-ups — ✅ DONE 2026-06-10 (geysers + sounds; 422 tests green, client built):**
- ✅ **Geysers + vents:** new `geyser_vent` block stamped sparsely by `WorldGenerator.StampGeysers` on wet
  worlds (water geysers) and volcanic/ashen worlds (steam/lava vents), gated by `WaterAbundance`/`LavaAbundance`/
  key. Client `GeyserView` (wired in `WorldRig`) scans nearby vent blocks and erupts a rising particle plume +
  `geyser_erupt` hiss on a per-vent timer; plume colour follows the world (pale water vs ember/ash). Cosmetic
  (no fluid/damage). Texture (OpenAI) + bilingual locale added.
- ✅ **Creature calls — habitat-flavoured** (`CreatureView.CallForHabitat`): cave dwellers moan/drone/wail,
  amphibians croak/gurgle, water burbles, lava hisses/rumbles, fliers shriek/trill (deterministic per species);
  **cave dwellers' calls get a reverberant echo** (`AudioReverbFilter` Cave preset via `ClientAudio.At(echo:)`).
- ✅ **Cave ambience:** `ClientAudio` now swaps to the existing `amb_cave` bed when the player is underground/
  enclosed (`!ExposedToSky && !Aboard && !InSpace`), back to the sky bed on surfacing.
- ✅ **Per-world ambience:** `BiomeBed` maps the new world keys → new ElevenLabs loops `amb_ocean` (surf),
  `amb_ashen` (heat/bubbling), `amb_fungal` (hum), `amb_corrupted` (murmur), `amb_wind_high` (skylands/highland);
  tundra→ice, savanna→forest, salt_flats→desert reuse existing beds.
- **Still deferred from V3/V4:** richer creature morphology beyond legs/eyes (eyestalks, tentacles, gas-sacs);
  full per-planet creature colour palettes (currently per-habitat tint + vivid exotics only). Chasms/sinkholes
  optional. (Geyser as a gameplay element — launch/scald — left out by design; cosmetic only.)

Key generation changes: FloraGenerator theme gating + new hosts in EnsureCoverage; CreatureGenerator new
habitats + per-planet palette; WorldGenerator per-planet archetype set + new feature passes; PlanetType new
fields (spawnWeight, terrain style, flora/creature theme, feature toggles); UniverseGenerator per-type
weights. New blocks → textures (OpenAI) + atlas + locale (bilingual).

### ★ Multiplayer-after-torus audit — ✅ FIXED/SHIPPED (decisions: both bugfixes + FULL voxel design)
All four gaps below are implemented: G1 wrap-path smoothing (`RemotePlayers` lerps along
WrapDeltaX/Z, settled position re-canonicalised); G2 in-space players are stealth-marked in presence
(test `OrbitingPlayer_IsStealthMarked_SoNoGhostAvatarStaysOnThePad`) AND the client finally honours
`Stealthed` (avatar + nameplate hidden — the stealth suit never actually hid avatars before); G3+G4
other players' ships render as their REAL voxel designs: the server cross-sends every instance
member's ship design (`Kind "ship_remote"`, cached per pilot client-side), `SyncRemotePlayers`
upgrades the generic hull to the voxel ship (same compact flight scale), and the landing/launch FX
builds the mover's actual ship via the shared `ShipMeshBuilder` (design rides ahead of the FX;
generic hull-coloured silhouette stays as fallback, e.g. default ships without a design).

### ★ Multiplayer-after-torus audit (original analysis)
Audit: what multiplayer code still needs adapting after round (torus) worlds, incl. whether other
players' landing/launch animations and space ships use their real ship optics.

**Already torus-correct:** remote avatars render at the wrap-copy nearest the viewer
(`RemotePlayers` via `ScenePos`); the landing/launch FX positions via `SceneX/SceneZ`; presence is
per-world; all server proximity systems (doors/creatures/enemies/NPCs/blocks) are wrap-aware; space
instances use their own flat coordinate space (no wrap needed); dock/trade targeting uses scene
positions. The player's OWN launch/landing uses their real voxel ship (`BuildVoxelShip`).

**Gaps found:**
- **G1 — seam sweep:** `RemotePlayers.Update` lerps the avatar's smoothed position LINEARLY in
  canonical world space. When a remote player crosses a wrap seam their canonical coordinate jumps
  (e.g. X 11990 → 5) and the avatar visibly sweeps across the whole world for a moment. Fix: lerp
  along the shortest wrap path (WrapDeltaX/Z with the world circumference).
- **G2 — ghost avatar while in space:** `TickPresence` does NOT filter `InSpace` (every other
  system does). A player who launches keeps broadcasting their last surface position — others see
  a frozen avatar standing at the pad while that player is in orbit. Cheapest fix: mark presence
  `Stealthed` for in-space players (clients already hide stealthed avatars + nameplates).
- **G3 — landing/launch FX is a GENERIC ship:** `ShipTransitFx` carries only name/pos/landing/hull
  colour; `ShipTransitView` builds a one-size hand-made cube silhouette. Neither the mover's ship
  CLASS (scout/corvette/hauler proportions) nor their voxel design shows — and after touchdown the
  REAL stamped ship appears, a visible model switch. Options: per-class silhouette (add ShipType to
  the message, cheap) … full voxel-design transfer (heavy).
- **G4 — other ships in SPACE are generic too:** `NetSpacePlayer` carries pos/yaw/EVA/hull only;
  `BuildRemoteAvatar` is a fixed small hull+cockpit — correct hull colour (item 32) but identical
  size/shape for every ship class. Same options as G3.

### ★ True-to-world orbit rendering + real pad map + forests + skylands (requested 2026-06-11) — ✅ SHIPPED (tests green, client built)
**Decisions:** M-key map keeps its fog-of-war (untouched); "die Karte" = the LANDING-PAD CHOOSER;
skylands now. Facts found: `Spacecraft.WorldGeneration.dll` ships with the client → the deterministic
generator (seed + type + circumference) can render any body's REAL world client-side, no traffic.
1. **`WorldMinimap.Bake`** (new): equirect map (full circumference × latitude band) sampled from the
   actual generation — depth-shaded seas/lava seas (`SeaLevel` + the water-vs-lava rule), upland
   ponds/lakes (`SurfacePondDepth`), height-shaded ground in the true surfaceBlock atlas colour, and
   a vegetation wash in this world's OWN FloraTints hue. Cached per body+seed+size.
2. **Landing-pad chooser shows the real world:** the old chooser normalised pads into their own
   bounding box on a BLANK panel (distorted positions, no terrain — "pads are not where they really
   are"/"map is empty"). Now it shows the baked 2:1 strip of the whole body with pads as buttons at
   their TRUE longitudes/latitudes (green=free, red=occupied unchanged).
3. **Orbit spheres = the real world map:** `SpawnBody` textures each body with its baked map (what
   you see from orbit IS the world you land on — water, flora colour, material per the follow-up
   ask), plus an **atmosphere haze shell** (breathable = denser/bluer than toxic; airless = none;
   clouds unchanged). Falls back to the data-driven tint for unknown types.
4. **Forests exist now:** a low-frequency forest mask gathers trees into groves (~9× density inside
   a patch, ~bare between); jungle gets explicit `treeDensity 0.045`. Test:
   `Trees_ClusterIntoForestGroves` (dense grove + treeless gaps over a region).
5. **Skylands:** floating islands were ALREADY generated (`floatingIslands` flag — the type is just
   exotic-rare, spawnWeight 3), but bare and single-altitude. Now: more coverage (mask 0.60), ±12
   per-island altitude variation (layered drifting islands) and surface flora on island tops. Test:
   `Skylands_GenerateFloatingIslands_WithAnAirGapBelow`.
6. **Follow-up fix ("purple planet from orbit, green trees on the ground"):** the GROUND seeds its
   per-planet flora hues with `LocationName = "System · Planet"`, but every PREVIEW (orbit bake,
   sky bodies, pad map) keyed `FloraTints` on the bare body name — two different keys, two
   independent colour rolls. New `PlanetOrbitLook.LocationKeyFor(system, body)` mirrors the ground
   rule and ALL preview call sites use it, so the vegetation colour you approach from space is the
   one the leaves actually have after landing.

### ★ Invisible space stations + wrong planet colours (reported 2026-06-11) — ✅ FIXED (tests green, client built)
**A) "No stations visible, but docking offered."** Cause: the bigger-stations round parked stations
at `Y=55` ABOVE the orbital plane (collision safety) — the ship cruises near Y=0, the 70-unit board
sphere still fired the dock prompt while the station hung overhead outside the natural line of
sight, and the radar disc has no elevation. Fixes (decisions: full package):
- Stations now sit at **Y=40, Z=90+i·45** — still above every possible body sphere (bodies at y=0
  with radius ≤ ~31, so y=40 is safe wherever the client's layout puts them) but visibly ~25° up
  ahead from the cruise plane instead of straight overhead.
- The space model scales by size tier (`NetCombatEntity.Scale`, small 1.0 … colossal 2.3; keep-out
  shell scales along) — a colossal station now dwarfs a small one from the cockpit.
- The radar's station readout shows **▲/▼** when the station sits >10 above/below the ship.

**B) "Planet looks green from space but is a flat mud world."** Cause: THREE divergent hardcoded
palette maps (`SpaceView.PlanetLook` — which even matched `"rock"` instead of the real key
`"rocky"`, so rocky + all item-21 V1 types fell to a pseudo-random `HashColor`; `SkyBodiesView.
TintFor`; `StationBackdrop.PlanetColor`). Swamp was hand-tinted olive-green over a mud texture.
Fix (data-driven, one source of truth — incl. the follow-up ask "flora-rich worlds should show
their own flora colour, watery ones water, or a mix"): new `PlanetOrbitLook.GroundColor` =
average colour of the type's real **surfaceBlock** atlas tile (`BlockTextureAtlas.AverageColor`,
cached) → blended toward the world's OWN per-planet flora hue (the same `FloraTints` roll that
paints the plants; weight = the type's `FloraDensity`; mushroom worlds key off the cap colour)
→ toward water-blue by `WaterAbundance` → toward lava-glow by `LavaAbundance`. The space-view
spheres also use the real surface-block texture now. All three call sites share the helper; the
old palettes remain only as unknown-type backstops. A mud flat reads brown, a lush world reads in
ITS vegetation colour, an ocean world blue — current and future types automatically correct.

### ★ "Cannot mine ANY block" — ROOT CAUSE FOUND + FIXED (2026-06-11, tests green, client built)
The recurring "Block ist bereits abgebaut" / silent-heal mining failures finally had a smoking gun:
the new ghost-heal Warn log showed mine intents arriving at **X≈5997 on a world that wraps before
that**. Root cause: `WorldConstants.CanonicalBlock`/`CanonicalChunk` had **no-argument overloads
hard-wired to the legacy 6000 circumference**, but worlds vary in size (asteroids 800–1600, moons
2500–4000, planets 5000–12000). `HandleMine`/`HandlePlace` (and friends) wrapped the client's
unbounded coordinate with 6000 BEFORE `ServerWorld` could wrap it with the world's true size —
mapping every block intent beyond X=6000 (or after lapping a smaller world) onto a column thousands
of blocks away: server saw air there → every mine "failed silently". Fixes:
- `HandleMine`/`HandlePlace`, wreck repair/breach mapping, terrain blaster + ore scanner spheres,
  ship-stamp protection sets (`_shipExtra`, `BlockInStamp` now takes the circumference), and the
  landing-pad longitude pickers now all wrap with **`_world.Circumference`**.
- The no-arg `CanonicalBlock`/`CanonicalChunk` overloads are **deleted** (footgun) — every caller
  must pass the world's own circumference; the compiler now catches the whole bug class.
- Regression test `Mining_AtAnUnwrappedLongitude_BreaksTheRealBlock_OnThisWorldsSize`: mines through
  the full intent path at `x + circumference` on a body whose size is NOT a multiple of 6000 (guard
  asserts that) — fails on the old code, green now. The earlier seam test only used
  `ServerWorld.GetBlock/SetBlock` directly (which always wrapped correctly) and missed this.

### ★ Enemy spawn distance + water effects (requested 2026-06-11) — ✅ SHIPPED (tests green, client built)
**Decisions:** spawn distance **35–50 blocks** (outside the 28-block detection range); water ambient
sounds **yes** (ElevenLabs, blanket-approved).
1. **Planet enemies spawn far away now:** `SpawnPlanetEnemyNear` places fiends 35–50 blocks out (was
   an ambush-like 9–13) — they roam on wander headings and only hunt when the player comes near.
   Tests: `PlanetEnemies_SpawnWellOutsideDetectionRange` (new); the hunt test walks the prey into
   range first.
2. **Water-body classification (client-only, works on old saves):** new `WaterSurface.Classify` probes
   the surface extent around each water top face (±X/±Z runs, cap 12): total width ≤ 5 → **river/
   brook** (flow axis = long axis); one axis ≥ 24 → **open water**; else **calm lake/pond**. Nearest
   shore distance → foam factor (full at the waterline, gone 3 blocks out). Packed per-vertex into
   TEXCOORD2 of water top faces (flora tint is zero on water; only the transparent shader reads them).
3. **Shader effects** (`BlockAtlasTransparent`, URP + Built-in): open water = gentle crossed-sine
   vertex waves (down-only, flattened into the foam band so the shoreline stays flush) + animated
   noise-rippled white foam against the coast + a soft moving sun glint; rivers = fast bright ripple
   bands + thin white streaks racing along the flow axis (procedural on world-pos — atlas UVs can't
   scroll); lakes = barely-there slow shimmer. Waterfall lips read as shore → white water at the edge.
4. **Water ambient sounds:** ElevenLabs loops `water_surf` (rolling coastal surf) + `water_brook`
   (babbling stream); the existing fluid-ambience scan in `ClientAudio` now classifies the nearby
   water with the SAME `WaterSurface` heuristic and picks surf/brook/generic-lap accordingly
   (`WaterBedFor`). NOTICES.md updated (125 sound files).
5. **Follow-up fix (same day):** the surf/foam had hard per-block steps and wave displacement could
   crack at block seams — foam + wave amplitude are now CORNER-smoothed in the mesher (each top-face
   corner averages the 4 cells meeting there, banks counting as full-foam/zero-amplitude shore), so
   shared corners get identical values from every face: smooth foam gradients, seamless waves, exact
   flattening at the waterline. TEXCOORD2 repacked to x=mode, y=foam, z=amp factor, w=flow axis.

### ★ HUD vitals + ghost mining + scanner window (reported 2026-06-11) — ✅ FIXED (tests green, client built)
1. **HUD vitals froze between events:** PlayerStateUpdate was only sent on explicit events (damage,
   eat, respawn, ...) — slow drains (oxygen/hunger/suit energy ticking down in TickEnvironment) never
   reached the client, so the bars looked stuck. The server now syncs vitals every 0.5 s whenever any
   of health/O2/energy/hunger moved > 0.4 since the last send (changed-only, so idle players cost no
   traffic). HUD audit: every element has a live data source and a purpose (location, 6 vitals rows,
   hotbar, compass, ToD+temperature, toast, prompts, scan panel, wreck panel, damage flash).
2. **"Block ist bereits abgebaut" again:** the reject toast is gone — a mine request hitting server-side
   air now SILENTLY heals the client (chunk resync + neighbour re-mesh) and logs a server `Warn` with
   the exact position, so the real desync source can be identified from logs when it next happens.
3. **Scanner detail window:** the scan readout (bottom-left) is now a real detail panel — 360×150 (was
   290×96), scanner icon, wrapped multi-line description, visible 12 s (was 8), and a first-time scan
   shows a highlighted green "Neue Entdeckung! +X" knowledge line instead of just the total.
4. **Held-item textures (question):** held BLOCKS already render with their real atlas texture tile
   (`HeldItem.BlockTileResolver`); TOOLS/weapons/gadgets are procedural blocky meshes with flat tints
   by design (no texture files involved). Open polish idea (backlog): texture decals for tools.

### ★ VEGA pacing + enemy movement (reported 2026-06-11) — ✅ FIXED (466 tests green, client built)
1. **VEGA lines ran into each other (unreadable):** lines now wait for a KEYPRESS — each line types
   out, shows "Weiter · [N]", and only advances on N (N also fast-completes the typewriter). A generous
   25 s timeout still auto-advances an unattended panel; the key is ignored while the menu is open or a
   text field (chat/beacon label) has focus. N was unbound before.
2. **Planet enemies stood rooted at their spawn forever** (no movement code existed): fiends now HUNT
   the nearest detectable player inside 28 blocks (3.1 b/s, tough ones 3.7, stop at biting range) and
   WANDER on re-rolling headings otherwise; terrain-following with a 3-block cliff limit, wrap-aware on
   both axes, position broadcasts throttled to 5/s. Test: `PlanetEnemies_HuntTheNearbyPlayer`.
3. **Space hostiles (drones/UFOs/cruisers) also never moved** — client models existed (real multi-part
   hulls), the server just never updated positions. They now PATROL a slow orbit around their post and
   CHASE the ship inside their per-kind aggro range (drone 190/16/9, UFO 240/24/7, cruiser 260/36/4 =
   aggro/stand-off/speed) with a sideways weave, never overshooting the stand-off ring. Test:
   `SpaceHostiles_PatrolInsteadOfHangingStill`.

### ★ Bug round (reported 2026-06-11) — ✅ FIXED (464 tests green, client built)
1. **VEGA tutorial UI unreachable/sticky:** the skip button sat on the HUD chip where the mouse is
   captured for camera control → moved into the Settings tab; the same button RESTARTS a finished/
   skipped tutorial (`SkipOnboardingIntent.Restart` wipes the stage milestones and re-runs the intro —
   the requested "wieder einschalten"). The chip's overflowing button text is gone with it, and the
   panel canvas (a root-level object) is now destroyed with the world rig, so the objective chip no
   longer floats over the main menu after leaving a world. Test: `Restart_AfterSkip_RunsTheTutorialAgain`.
2. **Ship perched on a high pedestal:** ship stamp + pad heights used the SINGLE centre-column surface
   height — a dramatic-terrain spike there hoisted the hull metres above the surroundings. Now
   `PadGroundY` takes the MEDIAN footprint height (every consumer: stamp, landing spawn, first spawn,
   pad list), and `BuildLandingPads` nudges pads to dry AND flat ground (footprint spread ≤ 5, flattest
   fallback).
3. **See-through holes when mining fast:** the stale-chunk resync (after a fast double-mine's "already
   empty" reject) and the client ghost-heal re-meshed only the chunk itself — the NEIGHBOUR chunks'
   now-exposed wall faces stayed missing. Both paths now mark the chunk + its six neighbours dirty
   (`MarkChunkAndNeighborsDirty`); incoming streamed chunks refresh their neighbours' boundary faces too.

### ★ Bigger space stations — ✅ SHIPPED 2026-06-11 (459 tests green, client built)
**Shipped (decisions: colossal tier YES, double halls YES, distribution 38/30/17/10/5, no old-save care):**
- **Parametric room sizes:** `StationGenerator` rooms are no longer a fixed 7×6×7 — `Layout` returns
  (modules, floors, room W/H/L) per tier and every helper scales: small 3×(7×6×7) / medium 5×(7×6×7)
  UNCHANGED; large 10 modules, 2 floors, **9×7×9** rooms; huge 16, 3 floors, **11×8×11**; new
  **colossal** 24 modules, 4 floors, 11×8×11. `StationStructure` exposes RoomW/H/L.
- **Double halls:** huge+ merges the hangar with a neighbour room (full shared wall removed → one big
  dock hall, mouth force-field on every open face half); colossal also merges a market hall (second
  vendor marker → two stalls). Hangar selection now prefers a cell whose -Z face is open space.
- **Scaling details:** viewport band 3-high on tall rooms, 3×3 floor shafts on big rooms, 2-deep corner
  chamfers on round modules (no dead pockets), relative furnishing (longer counters, second stall row,
  twin heal tanks, bunk rows, centre ceiling light), solar wings span the room. Doorways stay the
  standard 2×3 airlock cut so the sliding-door entities fit every tier.
- **Server:** tier roll 38/30/17/10/5 (`StationTier`); crew count small 2 / medium 4 / large 8 / huge 13
  / colossal 18. **Planet clearance (user follow-up):** stations now park WELL ABOVE the orbital plane
  and staggered outward (`(i%3−1)·70, 55, 80+i·45`) — bodies sit near y=0 with radii ≤ ~35 and the home
  planet hangs at y=−150, so even colossal hulls can never overlap a planet's keep-out sphere.
- **Tests:** StationGenerationTests rewritten size-relative + new coverage (per-tier room dims/floors,
  hangar double hall fully open, colossal market hall); boarding tests fly to the station first (the
  board-range check is real). 459 green.

### ★ Bigger space stations — ANALYSIS + PLAN (2026-06-11, decided + implemented above)
**Request:** generated stations should get SIGNIFICANTLY bigger — larger rooms AND more rooms; small
stations (like today) must keep existing alongside the big ones.

**Findings (StationGenerator + GameServerSpaceStations):**
- Stations are module-grid assemblies: tiers small 45 % (3 modules, 1 floor) / medium 35 % (5, 1) /
  large 15 % (9, 2) / huge 5 % (14, 3), tier rolled from the seed (`StationTier`).
- **Every room is a fixed 7×6×7 shell (5×4×5 interior — genuinely cramped)**: `RoomW/H/L` are consts;
  ALL helpers (room stamp, doors 2-wide, 2×2 floor shafts, viewport band, hangar mouth, furnishing,
  exterior greebles, marker positions) hardcode that footprint.
- Furnishing uses absolute coords (1..5) → must become relative to scale.
- NPC headcount scales by tier (2/4/7/11). Hand-designed templates bypass generation (untouched).
- **Existing-save caveat:** stations re-stamp deterministically each boot and persist as block edits.
  A CHANGED generator overwrites only the new structure's non-air cells — remains of the old station
  would survive around it (hybrid garbage). A clean upgrade needs wipe-old-bounds-then-restamp (station
  meta remembers the stamped dims), or new sizes apply to new saves only.

**Proposal:**
- small/medium: UNCHANGED (3/5 modules, rooms 7×6×7) — the "wie jetzt" stations.
- large: 10 modules, 2 floors, rooms **9×7×9** (interior 7×5×7).
- huge: 16 modules, 3 floors, rooms **11×8×11** (interior 9×6×9) + the hangar as a **double hall**
  (2 merged grid cells — a real dock you fly your eyes through).
- NEW tier **"colossal"** (rare): ~24 modules, 4 floors, rooms 11×8×11, double hangar AND double
  market hall, 4-floor shaft column, ~18 NPCs.
- Tier roll: small 38 / medium 30 / large 17 / huge 10 / colossal 5 (today 45/35/15/5).
- Mechanics that scale with room size: doorways 3-wide/4-tall on ≥9-rooms, viewport band 3-high on
  ≥8-high rooms, 3×3 shafts, furnishing relative + denser on big rooms (double console banks, more
  bunks, market stalls), more ceiling lights, bigger solar wings/domes.
**Decisions (answered 2026-06-11):** colossal tier YES; double halls YES (hangar ≥ huge, market at
colossal); distribution 38/30/17/10/5; **no backward-compat care for old savegames** (the new generator
simply applies; doorways stay the standard 2×3 cut so the sliding-door entities keep fitting). → IN PROGRESS.

### ★ World-creation options ("Weltoptionen") — ✅ SHIPPED 2026-06-11 (455 tests green, client built)
**Shipped (decisions: core 9 + survival sliders; exotic slider + advanced per-type page; gameplay knobs
live-editable by the world admin; presets Friedlich/Standard/Feindselig):**
- **Creation UI** (`UiWorldOptions` overlay from the world picker): preset row + discrete sliders in two
  columns — Leben/Bedrohungen (Kreaturen, Planeten-Gegner, Feindschiffe, UFOs), Überleben (O2, Hunger,
  Gefahren, Todesstrafe), Generierte Welt (Flora, Erz, Siedlungen, Wracks, Tresore, Stationen, Exoten,
  Universumsgröße) + **Erweitert-Seite** with a frequency slider per selectable planet type (fills
  `PlanetTypeFrequencies`, which replaces all data weights once touched). Fully bilingual (~60 keys).
- **Plumbing:** `WorldCreationOptions.ToArgs()` emits only NON-default server CLI overrides
  (`--creatures/--planet-enemies/--ufos/--flora/--ore/--settlements/--planet-wrecks/--vaults/--stations/
  --exotic/--systems/--planets-min/max/--moons-max/--oxygen/--hunger/--hazards/--death-penalty/
  --planet-types k=v,…`) → `ApplyCommandLine` → baked into the save at creation. **The world OWNS its
  rules:** `WorldMetadata.RulesOverride` replaces the launch config's rules on every load.
- **Enforcement:** creature cap × abundance rule (Off ⇒ lifeless, incl. join-time seeding);
  `WorldGenerator.SetWorldOptionFactors` (flora/tree × ore richness, deterministic from meta);
  settlement/wreck/vault stamp chances × `StructureFactor`; `UniverseGenerator` scales EXOTIC planet
  weights (new data flag `exotic` on 7 types) by the exotic-worlds frequency; previously-unwired
  `RareResources` now drives ore. PlanetEnemies/SpaceNpcs/UFOs were already enforced — now exposed.
- **Live edit:** `SetWorldRulesIntent` (NetCodec **115**, world-admin-gated) updates creatures + the three
  enemy activities at runtime, persists into `RulesOverride` and re-broadcasts `ServerRules` (which now
  carries the four values); in-game Settings tab got a "Weltregeln (Admin)" stepper section.
- **Tests:** `WorldOptionsTests` (9) — CLI parsing, rules bake+reload survival, lifeless-at-Off, live edit
  persists, role gate, exotic Off/Frequent galaxy shape, flora-factor-0 barren surface, Settlements=Off gate.

_Original analysis below._

### ★ World-creation options ("Weltoptionen") — ANALYSIS + PLAN (2026-06-11, decided + implemented above)
**Request:** sliders at world creation — creature frequency, world/planet-type frequencies, alien enemies
on/off + frequency, etc. Analyse what CAN be made configurable, propose a sensible option set, ask back.

**Findings — much of the machinery already exists, it just isn't exposed at creation:**
- **Enforced today, hidden from the UI:** `GameRules.PlanetEnemies` / `SpaceNpcEnemies` / `AlienUfos`
  (each `AlienActivity` Off/Rare/Normal/Frequent/Extreme — spawn caps scale with the level, Frequent+
  spawns tougher enemies). Singleplayer hardcodes `--space-npcs Normal` in `LocalServerLauncher`.
  Also enforced: hazards, oxygen, hunger, death penalty, PvP, weapon mode (all GameRules).
- **`WorldDescription` (in ServerConfig + WorldMetadata) already does universe shape:** StarSystemCount,
  PlanetsPerSystemMin/Max, MoonsPerPlanetMin/Max, SpaceStations + (space-)Wrecks `Frequency`, and a full
  **`PlanetTypeFrequencies` dict** — all RESPECTED by `UniverseGenerator` (type frequencies override the
  per-type SpawnWeights). `AsteroidFields`, `RareResources`, `Danger` are defined but **unwired**.
- **Defined but unwired rules:** `AggressiveAliens`, `PassiveCreatures` — sent to the client, enforced
  nowhere (creature aggression/spawning ignores them today).
- **No knob exists yet for:** creature abundance (`WorldCreatureCap` formula has no config factor),
  flora density (worldgen `floraMul` 0.8–1.6 is seed-only), ore richness (per-world jitter, seed-only),
  on-planet POI density (settlements/wrecks/vaults are bool `PlaceX` + fixed moderate density).
- **Flow:** `UiSaveSelect` (name + Explorer/Creative + 3 checkboxes) → `AppShell.StartSingleplayerWorld`
  → `LocalServerLauncher` CLI args → `ServerConfig.ApplyCommandLine` → baked into `WorldMetadata` at
  first creation (the creative-flags pattern — new options follow it; dedicated servers get the same
  knobs via config/server.json `Rules`/`World`).

**Proposed option set (world-creation "Weltoptionen" panel):**
1. *Kreaturen (Tiere)* — Aus/Wenig/Normal/Viel/Sehr viel → multiplier on `WorldCreatureCap` + pacing
   (wires `PassiveCreatures` Off for "Aus").
2. *Alien-Gegner (Planeten)* — direct `Rules.PlanetEnemies` (Off–Extreme). The headline ask.
3. *Weltraum-Gegner / UFOs* — `Rules.SpaceNpcEnemies` + `AlienUfos` (replace the hardcoded CLI value).
4. *Flora-Dichte* — new multiplier 0.5–2.0 onto worldgen `floraMul` (creation-only; baked into chunks).
5. *Erz-Reichtum* — wire the existing unused `WorldDescription.RareResources` into the per-world ore
   richness (creation-only).
6. *Strukturen: Siedlungen / Wracks / Vaults* — Aus/Selten/Normal/Häufig each (replaces bool `PlaceX`,
   adds a density factor to the stamp gates; creation-only).
7. *Universumsgröße* — preset Klein/Normal/Groß/Riesig → StarSystemCount 4/8/12/18 + planets/moons ranges.
8. *Exotische Welten* — single slider scaling the SpawnWeights of exotic planet types (via
   `PlanetTypeFrequencies`), instead of 17 per-type sliders. (Optional advanced per-type page — ask.)
9. *Raumstationen* — `Description.SpaceStations` frequency.
10. *(optional) Survival-Schärfe* — oxygen/hunger/hazards/death penalty exist in GameRules; expose? — ask.

**Decisions (answered 2026-06-11):** scope = core 9 **+ survival sliders** (O2/hunger/hazards/death
penalty — two-page panel); planet types = **both** (one "Exotische Welten" slider + an advanced per-type
page over `PlanetTypeFrequencies`); **gameplay knobs (creatures/enemies) live-editable in-game by the
world admin**, worldgen knobs creation-only; **presets Friedlich/Standard/Feindselig** above the sliders.
→ IN PROGRESS. Stages: S1 shared (fields/enums + CLI + WorldMetadata rules override) → S2 server
enforcement (creature cap factor, flora/ore factors into WorldGenerator, structure-frequency gates,
exotic weights, live rules intent) → S3 client UI (creation panel + presets + advanced page + in-game
admin section, bilingual) → S4 tests + build.

### ★ Item 22 — LLM stages — ✅ ALL SHIPPED 2026-06-11 (L0+L2+L3 + VEGA banter; L1 was 2026-06-10)
**Shipped on top of L1 (same offline-safe pattern everywhere: async off-thread, cached, AiLevel-gated,
static/localized fallback; provider-agnostic backend via LangChain/LangGraph + OpenAI-compatible env):**
- **L0 — real LLM behind `/mission-plan`:** the backend now authors the full MissionPlan via a
  strict-JSON LLM chain (fenced-JSON tolerant parser, shape check), falling back to the deterministic
  template. The server enriches the admin context with the ALLOWED content keys (mineable targets +
  component/consumable rewards, bounded lists — `EnrichMissionContext`) so the LLM can't hallucinate
  keys the validator rejects. Validation/clamping unchanged (Suggest=draft, Auto=publish).
- **L2 — memory/personas in the prompt:** `NpcLineRequest` carries `Persona` (deterministic per-NPC
  voice from a 6-organic/3-android pool, stable across visits) and `RecentEvents` (last 5 interaction-log
  entries as "a trade, took a mission"); the backend prompt includes both plus the relationship tier.
- **L3 — LLM board-mission texts:** new `POST /mission-text` writes Title+Description around the FIXED
  server-coined board job (objective/reward untouched), per player locale. Server: `GameServerMissionTexts.cs`
  — off-thread generation on board open, cache per missionId|locale, live mission-list refresh when a text
  lands, `NetMission.FreeText` flag so the client renders text verbatim vs. localizing keys (also fixes
  player-mission titles rendering bracketed). AI off/declined ⇒ static localized board text.
- **VEGA banter:** same `/npc-line` path with role `ship_ai` + a `Situation` line (world type, day phase,
  fragment progress). Rare smalltalk (~7–12 min, only after onboarding), cached per world|day-phase|locale
  bucket, sent via the new `ShipAiLine.Text` field (client shows verbatim; settings mute applies). With AI
  off the banter is simply absent — the scripted VEGA lines cover everything else.
- **Tests:** `LlmStagesTests` (11) — L0 context keys, L2 persona stability + recent events, L3 text+cache /
  AI-off / provider-declined, banter role+situation+cache / AI-off silence, `/mission-text` HTTP contract.

_Original L1 entry + analysis below._

### ★ Item 22 — LLM-authored NPC greetings (item 15) — L1 DONE (2026-06-10)
Requested: use an LLM to generate NPC display texts (greetings) with game context: NPC name, player name,
relationship, NPC type (quest-giver/trader), trade kind, mission type/kind.

**DONE — L1 (greetings only), per the chosen decisions:** vendor + quartermaster greeting lines.
- **Backend** (`ai-backend/`): new `POST /npc-line` using **LangChain + LangGraph**, provider-agnostic via the
  **OpenAI-compatible** chat API → works with **LM Studio (self-hosted) / OpenAI / Claude**, switched purely by
  env (`SPACECRAFT_AI_BASE_URL` / `_MODEL` / `_API_KEY`, see `.env.example`). With no model configured (or any
  error) it returns a deterministic **bilingual template** line. `app/llm.py` (graph), `app/main.py` (endpoint).
- **Networking**: `JoinRequest.Locale`, `NpcGreetIntent {Role}`, `NpcGreeting {NpcId,Name,Role,Text}` (NetCodec
  109/110).
- **Server** (`GameServerNpcGreeting.cs`): on interaction (vendor open → client `NpcGreetIntent`; mission board →
  server-side in `SendMissionList`) it builds context from the **relationship memory** (value+tier, past visits),
  NPC (name/role/theme/robot), settlement, and the player's **locale**, then sends a greeting. **Server-
  authoritative, non-blocking** (LLM runs on a Task, drained in `TickGreetings`), **cached** by npcKey|locale|
  tier, **proximity-gated**, 25 s re-open cooldown. AI off/unreachable → empty `Text`.
- **Client**: locale flows in `Join`; `NpcView` shows the line as a **speech bubble** over the NPC; on empty
  `Text` it renders a **localized static fallback** (`npc.greet.vendor` / `npc.greet.quartermaster`, DE+EN) so a
  greeting always appears with or without an AI backend.
- **Tests**: `NpcGreetingTests` (provider parsing, fallback-when-off, generate+cache+reuse, context carries
  role+language, proximity gate) — full suite green (414).

**Deferred (not in L1):** L0 real LLM behind `/mission-plan`; L2 richer memory flavour in the prompt (log of past
trades/missions, per-NPC persona); L3 LLM mission-board text. Speech-bubble is uGUI world-label (no wrap) — fine
for short lines; revisit if lines run long.

--- original analysis kept below for the deferred stages ---

**What already exists (good news):** an optional **AI backend over HTTP** (`AiLevel` Off/**TextOnly**/Suggest/
Auto, `AiBackendUrl`; `HttpAiMissionProvider.Generate(context)` POSTs `/mission-plan`, 8s timeout, any error →
null → graceful) with a `MissionPlan` carrying `GiverName`/`StartDialog`/`CompleteDialog`/`Description`. The
Python backend (`ai-backend/app/main.py`) is a deterministic template — an LLM slots in behind the same
contract. A real **player↔NPC relationship system** exists (`NpcRelationship`: Name, Role, Value −100..+100,
last-10 interaction Log of Trade/MissionAccepted/Dialog; key `settle_<hash>:<role>`; `NpcRelationshipFor`).
NPC context is available: Name/Role/Theme/IsRobot, settlement name + trade theme (miners/traders/researchers/
settlers), mission objective types + difficulty. **Missing:** a real LLM; an NPC *greeting* path (today NPCs
only show a "Name · Role" nameplate — no dialogue on approach/interact); bilingual handling of dynamic text.

**Plan (staged, server-authoritative + graceful-degrading + cached):**
- **L0 — real LLM behind the backend:** replace the Python template with an actual LLM call (same `/mission-plan`
  contract) → mission Title/Description/StartDialog/CompleteDialog become LLM-authored. Smallest first step.
- **L1 — NPC greeting line:** new backend endpoint (e.g. `/npc-line`) + a server flow that, when the player
  interacts (open vendor / mission board / accept / complete), builds a context (NPC name+role+theme, player
  name, relationship value+history, trade kind / mission kind, **language**) and gets back a 1–2 sentence
  greeting; sent to the client via a new `NpcLine` message → shown as a speech bubble over the NPC + in the
  trade/mission panel. **Async + cached** (key: npc + relationship-tier + language) so it never blocks the UI;
  **static fallback** greeting when AI is Off/unreachable (game stays fully playable offline).
- **L2 — memory/relationship flavour:** feed the interaction log + a relationship tier (stranger/known/trusted)
  so the NPC "remembers" ("back again, pilot?") and tone shifts with standing; per-NPC persona from name/theme/
  robot.
- **L3 — mission flavour end-to-end:** route settlement-board missions through the LLM for per-mission text
  (today they use static locale keys), with objectives/rewards still server-validated + clamped.

**Decisions needed:** scope (greetings only vs also mission dialog/descriptions vs vendor patter); which LLM
(OpenAI — SDK+key already in tools/ai-assets/.env — vs Claude); **bilingual** approach (generate in the
player's active locale via a language hint — needs the player's locale sent to the server) ; keep it strictly
optional + offline-safe (static fallback) ; caching to bound cost. **Localization caveat:** LLM text is
dynamic, so it bypasses the locale-key system — generate directly in the player's language (DE/EN) rather than
adding keys.

### ★ B55 + B58 — ✅ DONE (2026-06-10; 422 tests green, client built)

**Shipped:**
- **B55** — each **vendor NPC** now gets its own seeded profession (`VendorThemeFor`: vendor 0 keeps the
  location's theme, additional vendors deterministically vary) in both settlement (`SpawnSettlementNpcs`) and
  station (`SpawnStationNpcs`) spawns, with per-NPC `robotic` from that theme. Trade validation now keys off the
  **nearest vendor's theme** (`VendorThemeAt`) instead of one theme per location — so multiple vendors at one
  place sell different goods (and station vendors can finally post *themed* goods, not only the themeless ones).
  The client already shows per-vendor stock via `NetNpc.Theme`. Tests: `QuickbarVendorTests` (vendor-theme seam).
- **B58** — customisable quick-bar: new `MoveItemIntent {FromSlot,ToSlot}` (NetCodec 111) + server
  `HandleMoveItem` (swaps two personal-inventory slots; `ToSlot=-1` stows out of the quick-bar to the first free
  backpack slot), `Inventory.Swap`/`FirstEmptySlot`. Client: inventory detail pane shows a **Quick-bar** row of 9
  slots — click a slot to assign/swap the selected item, ✕ to stow it back; the refresh hash now includes an
  item↔slot signature so a pure swap repaints. Bilingual locale keys added. Tests: `QuickbarVendorTests`
  (Swap/FirstEmptySlot + server move/stow/no-op).

**Original analysis kept below.**

### ★ Graphics/immersion drive (2026-06-10)
Analysis of in-game screenshots → measures to look more professional/immersive (full breakdown in chat).
Baseline is better than it looks: lit block shader (normals/sun/spec/AO/sky-occlusion), an ACTIVE post stack
(`PostFx`: bloom/ACES/SSAO/vignette/grade), world variety V1–V5, item-21 ambience. Key gaps + measures:
- ✅ **HUD visor "dezent + abschaltbar"** (done 2026-06-10): softened the `Spacecraft/Visor` defaults
  (chroma 0.011→0.005, curvature 0.07→0.045, glow 0.9→0.6) + a **Settings toggle** (`ClientSettings.VisorEffects`,
  Settings tab) that composites a clean flat HUD when off (no chroma/curvature/scanlines/glow). Live-toggled.
- ✅ **URP migration — DONE 2026-06-10** (merged to main, dev-verified): real soft shadows (terrain + models +
  creatures + enemies, cast + receive), URP Volume post (ACES/bloom/vignette/grade) + SSAO, diegetic visor via
  a render-graph blit pass, per-system sun tint restored (grade share 0.4), all 12 shaders dual-pipeline,
  Potato preset shadows off. Progress log: [docs/developer/URP_MIGRATION.md](docs/developer/URP_MIGRATION.md).
- **Quick wins (kept, smaller now that URP shadows exist):** held-item real texture (currently a flat cube,
  `HeldItem.cs`), flora as cross-billboards (currently alpha-cutout cube faces), menu backdrop blur (80%
  opaque quad, no blur).
- **Worlds richer (not chosen this round, kept):** more dramatic terrain (amplitude/dunes/overhangs/spires),
  more landmarks/POIs (ruins/dungeons/rewarding cave systems), set-dressing props, ecosystem fauna behaviour,
  weather drama, a guiding progression/onboarding.

### ★ Round worlds (torus) — ✅ DONE 2026-06-11 (T1–T4 shipped; W5 "poles" DROPPED by user decision)
**Goal:** the world should FEEL round — walking in ANY direction loops seamlessly (no invisible pole barrier).

**Shipped (user approved "Ja, Torus umsetzen"):** the world is now a **torus** — Z (latitude) wraps exactly
like X (longitude), with period = `LatitudePeriodFor(circ)` = circ/2 rounded down to a multiple of 32 (so the
period AND the half-domain are chunk-aligned). Canonical Z domain [−period/2, +period/2) is centred on the
equator ≈ the old playable strip, so existing saves' edits stay in-domain.
- **T1 noise:** `Noise.Value5D` (two `Value4D` layers lerped along a 5th axis) + `FbmTorus`/`ValueTorus`
  (BOTH ground axes circle-embedded); all 14 `FbmCylX`/`ValueCylX` call sites in `WorldGenerator` switched to
  `FbmT`/`ValueT` wrappers; all per-column `Value01` hash rolls take `Wz(worldZ)` so stamps match across the seam.
- **T2 shared+server:** `WrapZ`/`WrapDeltaZ`/`CanonicalChunkZ`; `CanonicalChunk`/`CanonicalBlock`/
  `WrapDistanceSquared` are Z-aware; server move clamp removed — `HandleMove` wraps Z (`LatitudeLimitFor`
  kept as legacy; `WorldEnvironment.LatitudeLimit` still sent, now unused by the client).
- **T3 client:** `SceneZ` mirror of `SceneX` + `ScenePos` uses it (all entity views route through it);
  `RepositionChunks` re-anchors on X OR Z; pole-barrier slide removed; `ClientWorld` block lookups canonicalize
  Z; world map renders chunks/markers/distances seam-free on both axes; beacon labels/ship transit FX seam-aware.
- **T4 tests:** `WorldWrapTests` extended to Z (periodic surface/biome/chunk identity across the latitude
  seam, no-cliff at the former barrier, WrapZ/WrapDeltaZ behaviour, chunk-aligned period for any circumference);
  `WalkingTowardThePole_IsBoundedByTheLatitudeBarrier` → `WalkingNorth_WrapsAroundTheWorld_NoPoleBarrier`.
  Determinism fallout fixed (CrateredWorld seed re-pinned; mine test now hits until hard blocks break).
  **428/428 green.**

_Original analysis kept below for the option trade-offs (cube-sphere/pole-fold rejected)._

**Today:** the world is a **cylinder** — X (longitude) wraps seamlessly (`WrapX`, noise on a circle via
`FbmCylX`/`ValueCylX`), Z (latitude) is hard-clamped at ±`LatitudeLimit` (= circumference/4) by an invisible
barrier (server `GameServer.cs:1489` move clamp + client `PlayerController.cs:980` slide).

**Options analysed:**
1. **TORUS (wrap Z exactly like X) — RECOMMENDED.** Every direction loops seamlessly by construction
   (diagonals included). Not a literal sphere (no poles — you loop instead of crossing a pole), but
   indistinguishable in play: "round-feeling" voxel games ship exactly this.
   *Key findings that make it cheap:*
   - The height noise embeds X as a circle in 3D noise — the torus variant embeds BOTH axes as circles
     (cosU,sinU,cosV,sinV) in **`Value4D`, which already exists** (used by `ValueCylX` for caves). Caves/ores
     then need a mechanical **`Value5D`** extension (X-circle 2 + Y + Z-circle 2).
   - **Climate is NOT latitude-based** (checked — no Z-temperature anywhere), so nothing breaks thermally.
   - **Old saves stay compatible:** picking the Z-period = circumference/2 makes the canonical Z domain
     [−C/4, +C/4) — EXACTLY today's playable strip, so every existing block edit remains in-domain (terrain
     near the former barrier regenerates as the new seam; per the W-R decision, worldgen may change).
   *Blast radius (measured):* `Noise` (torus FBM + Value5D; **16 call sites** in WorldGenerator switch to the
   wrap-both variants); `WorldConstants` (`WrapZ`/`WrapDeltaZ`/canonical Z for blocks+chunks, drop
   `LatitudeLimitFor` semantics → Z-period, extend `WrapDistanceSquared`); server (remove the move clamp,
   wrap Z in `HandleMove`; all proximity checks already route through `WrapDistSq` — one helper change);
   client (**`SceneZ` mirror of `SceneX`** — 11 ScenePos/SceneX call sites + `RepositionChunks` reposition in
   Z too; remove the barrier slide; minimap/compass wrap Z); `WorldEnvironment.LatitudeLimit` field retired
   (client keeps reading it harmlessly until removed); tests (extend the 10 `WorldWrapTests` to Z, replace
   `WalkingTowardThePole_IsBoundedByTheLatitudeBarrier`).
   *Effort:* a focused multi-commit package — Noise+worldgen ≈ the biggest chunk, then shared/server, then
   client scene-wrap, then tests. Roughly the size of the original X-wrap work (W0–W4), with that code as
   the template for every step.
   *Risks:* subtle Z-seam proximity bugs (the X-seam bug class — mitigated by routing through the same
   helpers); structures/stamps near the Z seam must canonicalise (stamps already use `CanonicalBlock` — gets
   Z handling); minimap edge cases.
2. **Cube-sphere (6 projected faces) — REJECTED.** The "true sphere" voxel approach: 6 chunk grids, edge
   stitching, per-face bases, cross-face movement/meshing/persistence/networking. Months of work, breaks
   nearly every coordinate assumption in the codebase. Not justified vs. option 1's result.
3. **Pole-fold (sphere topology on the grid: crossing z=L teleports to x+C/2, mirrored) — REJECTED.**
   Mathematically sound quotient space, but the fold line needs mirrored chunk rendering, fold-aware reach/
   interaction deltas and a jarring heading flip when crossing — high bug surface, weird UX, little gain
   over the torus.

**Recommendation:** Option 1 (torus, Z-period = circumference/2), staged like the original world-wrap work:
T1 noise (torus FBM + Value5D + WorldGenerator call sites) → T2 WorldConstants + server (wrap Z, drop clamp)
→ T3 client (SceneZ + reposition + barrier removal + map) → T4 tests + playtest. **APPROVED + SHIPPED — see
the DONE block above.**

### ★ Onboarding/Progression via the SHIP AI "VEGA" — ✅ SHIPPED 2026-06-11 (O1+O2+O4+O5; O3 voice deferred; 435 tests green)
**Shipped (decisions: text+blips first, scripted lines, full scope, dry-laconic persona, name VEGA):**
- **O1 server core** (`GameServerShipAi.cs`, `PlayerState.Milestones` persisted via snapshot): 8-stage
  onboarding chain (mine 3 → craft → scan → unlock → launch → dock → trade/mission → land), each gated by
  the existing server event (hooks in mine/craft/scan/unlock/EnterSpace/StationBoarded/trade-close/vendor-
  barter/AcceptMission/Travel). Out-of-order completions record silently; the chain skips them. Veteran
  saves (knowledge/blueprints/missions present — incl. creative grants) auto-skip with one "systems online"
  line. `ShipAiLine` (NetCodec **113**) carries locale KEY + objective chip + kind (onboarding/advisor/
  story/system); `SkipOnboardingIntent` (**114**) grants the whole chain.
- **O2 client companion UI** (`VegaPanel.cs`, mounted in WorldRig): typewriter speech panel + queued lines
  + `ai_blip` radio chirp (ElevenLabs, procedural Beep fallback), persistent objective chip with live
  progress (mine 1/3) and a skip button; settings toggle **VEGA hints** (`ClientSettings.VegaHints`,
  Settings tab) mutes advisor lines; fully bilingual (~80 locale keys DE+EN, `vega.*`).
- **O4 advisor**: once-per-save persisted hints (low O2/energy/hunger, inventory full, first nightfall,
  "ruins detected" near vaults/wreck, world-type flavour for asteroid/ocean/corrupted/fungal/ice/volcanic)
  via a 1 Hz `TickShipAi` poll.
- **O5 game element**: **AI-core module line** `ai_core_mk2`/`ai_core_mk3` (ship_modules + blueprints +
  OpenAI icons) — Mk2: terrain-scanner radius +6, hostile-contact callouts in space, **client autopilot**
  (P in cruise, flies to nearest station/landable body, manual input takes the helm back; gated by
  `PlayerStateUpdate.AiCoreTier`); Mk3: 12 % evasive-manoeuvre damage negation (`ApplyShipDamage`) with
  callout + aux energy. **Memory-fragment story arc**: `ai_memory_fragment` drops from data terminals
  (wrecks + vaults); VEGA redeems them aboard (paced), +3 knowledge each, 10 beats tell the Meridian-fleet
  backstory (vaults/wrecks/corrupted worlds get lore), beat 10 teaches the Mk3 blueprint.
- **Tests**: `ShipAiTests` (7) — boot+mine stage advance, veteran skip, skip intent, fragment redemption
  pacing+reward, arc completion → Mk3 blueprint, AiCoreTier in player state, milestone persistence.
**Deferred:** O3 ElevenLabs TTS voice (after line tone settles in playtest); LLM flavour for advisor/banter
(L-stages pattern); autopilot path-finding beyond straight-line + keep-out sliding.

_Original analysis below._

### ★ Onboarding/Progression via a SHIP AI companion — ANALYSIS + PLAN (2026-06-11, decided + implemented above)
**Request:** plan the first-hour onboarding/progression as a "ship AI" that accompanies the player — first as
helper/advisor, later as a real game element. (This is the onboarding package deferred from "Welten reicher".)

**Why a ship AI is the right vehicle (codebase facts):** the game already has every building block —
server-side milestone hooks (`OnBlockMined`, `OnPlayerTravelled`, scan `FirstTime`, dock/board/trade/craft
intents), per-player persistence (`PlayerState` + `SavePlayer`), a bilingual locale system, an optional LLM
backend with graceful template fallback (`/npc-line`, `GameServerNpcGreeting` pattern: async + cached +
cooldown), ship modules as data (`data/ship_modules.json` + free-form stats), and story-ready POIs that
currently lack narrative purpose (buried vaults W-R3, wrecks with data terminals, ruined settlements,
`data_cache` blocks). What's missing is exactly what the AI provides: a first-join flow, a hint engine, and
a reason to visit the POIs. There is no tutorial today beyond a static keybind hint line (`ui.hud.hint`).

**Concept — three phases, one character:**
- **Phase A — Onboarding (first hour).** The starter ship's AI boots on a NEW game ("emergency reactivation")
  and walks the player through a staged chain matched to real progression, each stage gated by an EXISTING
  server event: 1 survive/HUD basics + mine (OnBlockMined) → 2 craft (craft handler) → 3 scan + knowledge
  (ScanResult.FirstTime) → 4 first blueprint unlock → 5 board ship/ship tech → 6 launch (EnterSpace) →
  7 star map + dock a station (StationBoarded) → 8 first trade/board mission (TradeClosed/AcceptMission) →
  9 land a second world (OnPlayerTravelled) → 10 send-off pointing at vaults/wrecks. Server-authoritative,
  per-player (`PlayerState` milestone set), **skippable** (settings toggle + auto-grant for veteran saves:
  players with existing blueprints/knowledge only get a one-line "systems online").
- **Phase B — Advisor (ongoing).** Contextual, rate-limited hints after onboarding: low O2/energy with
  advice, first visit to a new world type (flavour + dangers), night/enemy warnings, full inventory, "ruins
  detected nearby" nudges toward unvisited POIs. Each hint fires once (persisted), global cooldown, mute
  toggle. All lines scripted + bilingual (offline-safe); optional LLM flavour via the L1 pattern.
- **Phase C — Game element (own package, after A+B playtest).** The AI becomes an upgradable **ship module
  line** (`ai_core_mk1..3` in ship_modules.json) plus a collectible-driven story:
  *Abilities by tier:* Mk1 (start) hints + basic scan ping; Mk2 **autopilot** (auto-course to a map target),
  ore-vein highlight (extends the Feature-40 terrain scanner), space threat callouts; Mk3 combat co-pilot
  (evasive boost / point-defence assist), energy/fuel optimiser stat, landing assist.
  *Memory fragments:* wreck data terminals, vault loot and data_cache blocks drop **AI memory fragments**;
  restoring them unlocks story beats (where does the AI come from? what happened to its fleet?), new persona
  lines, and occasional POI reveals/blueprint hints — finally giving vaults/wrecks narrative pull.
  *Persona:* relationship-style growth (reuse the `NpcRelationship` pattern keyed `ship_ai`), tone shifts
  with progress; optional LLM banter. Strictly per-player (each player has their own ship AI; lines are
  sent only to that session).

**Technical staging:**
- **O1 server core:** `PlayerState.Milestones` (HashSet<string>) + onboarding stage; new `ShipAiLine`
  message (NetCodec tag — next free, verify ~113 — MUST be Register()'d); `GameServerShipAi.cs` partial
  wiring the stage chain into the existing handlers; lines as locale keys (DE+EN); veteran auto-grant;
  config/settings respect; tests (stage advance per event, persistence round-trip, veteran skip, per-player
  isolation in multiplayer).
- **O2 client companion UI:** HUD companion widget (small avatar chip + typewriter text panel + line queue),
  persistent objective chip for the current stage, soft radio chime (`ClientAudio.Cue`), settings toggles
  (hints on/off), bilingual.
- **O3 voice (decision):** ElevenLabs TTS for the ~30–40 scripted lines, DE+EN, bundled like existing audio
  (Resources/audio); dynamic/LLM lines stay text-only. Radio open/close blips either way.
- **O4 advisor engine:** trigger table (condition + once-flag + cooldown) for Phase B hints.
- **O5 game element:** ai_core module line + autopilot first ability + memory fragments + persona (split
  into its own analysis when A+B have shipped and playtested).

**Decisions (answered 2026-06-11):** TEXT + radio blips first (TTS voice deferred to O3, after line tone
settles in playtest); onboarding lines 100% scripted/bilingual (LLM flavour only later in advisor/banter);
scope = **FULL O1–O5 minus O3** (onboarding + advisor + AI-core modules/fragments in this package); persona
**dry-laconic with humour**; name **VEGA**. → IN PROGRESS.

### ★ "Welten reicher" — ✅ DONE 2026-06-10 (W-R1–W-R4 shipped; onboarding deferred by decision; 423 tests green)
**Decisions:** all four measures in scope (terrain drama, POIs/dungeons, set-dressing, weather drama);
**onboarding later as its own package**; POI density **moderate** (several small + 1–2 large per world);
worldgen changes **may alter existing worlds** (same seed → new terrain; saved chunks/builds stay as stored).

- **W-R1 — terrain drama** (worldgen): per-world seeded **drama factor** (~0.9–1.5×) on the amplitude so some
  worlds roll gentle and others jagged; **ridge sharpening** (power-curve the heightmap on mountains/spires/
  canyon styles → real crests + steeper walls); deepened canyon style.
- **W-R2 — set-dressing** (worldgen stamps, existing blocks only): sparse **boulder clusters** (the world's
  deep rock), **crystal shard outcrops** (crystal/cave-rich worlds), **dead trees** (bare trunks + stub
  branches on dry/ashen worlds) — breaks the flat-grid monotony cheaply.
- **W-R3 — POIs/dungeons** (moderate): several small **monoliths/stone circles** (data cache at the base) +
  **1–2 buried vaults** per suitable world — a surface ruin ring hinting a shaft down to a stone chamber with
  data caches + a loot container. Server-stamped, loot via the existing container system.
- **W-R4 — weather drama** (client): **sandstorm/ash visibility crush** (fog density spikes), **aurora bands**
  on cold worlds at night (additive waving sky ribbon), **dawn valley fog**.
- **Onboarding** — deferred to its own package (per decision).

### ★ Three-pack (doors check / creature morphology / sky rests) — ✅ DONE 2026-06-10 (423 tests green)
- **Auto-doors stations + ship + placeable door — ALREADY IMPLEMENTED, TODO was stale.** Verified end-to-end:
  `StationGenerator` emits `door_slide` markers (L306) → `RegisterStationDoors(station.Markers)` on boarding;
  ship hatches are energy doors from `ShipStamps.Doors`; the **placeable door** exists fully (items + recipes
  `door_slide`/`door_hinge`, `PlaceDoor`/`RemovePlayerDoorAt`, persisted via `_repo.SaveDoor`, covered by
  `PlaceableDoorTests`); both editors carry door palette entries. The stale "stations/ship still open" section
  below is superseded.
- **Creature morphology (item-21 rest):** new species traits **Tentacles** (water 55% → 4–6, cave/amphibian
  2–4, rare land/air oddities), **EyeStalks** (snail-like, amphibian 45%/cave 35%/water 25%), **HasGasSac**
  (air 35%, rare floating land grazers) — generated per species, sent via `NetCreature`, rendered by
  `CreatureBuilder` (staggered eyestalk spheres, shrinking dangling tentacle chains in the belly tone, a
  translucent Cloud-shader buoyancy sac).
- **Per-biome colour palettes (item-21 rest):** biome-native species pull their hue ~45% toward their biome's
  anchor hue (golden-ratio spaced) — region A's fauna reads as one colour family, region B's as another.
- **Sky rests (B37):** `RenderSettings.ambientLight` is now managed (flat, star-tinted, follows time of day)
  so standard-shader props pick up the star tint too; **orbit-view planets + cloud shells** are washed ~35%
  toward the system star's hue (a red sun makes the whole system read warm).

### ★ Feature 40 — Terrain scanner — ✅ DONE 2026-06-10 (423 tests green, client built)
New item-36-style right-click gadget **`terrain_scanner`** (workshop recipe + blueprint, tier 2, 10 suit
energy, 10 s cooldown): a pulse that reveals **valuable blocks through the terrain** — every `*_ore`, `crystal`
and `data_cache` within a 20-block sphere around the player. **Server-authoritative**: validates energy/
cooldown, scans, sends the nearest ≤80 hits as a new `OreScanResult` (NetCodec 112). Client `OreScanView`
renders **through-wall glow markers** (always-included `SunGlow` shader — additive, ZTest Always) for 8 s,
gently pulsing, fading out, **tinted by ore type** (gold/copper/iron/titanium/crystal/data-cache distinct);
amber pulse + new ElevenLabs `terrain_scan` sonar sweep on use; OpenAI icon `item_terrain_scanner`. Bilingual
locale; test `TerrainScanner_FindsNearbyOre_CostsEnergy_AndCoolsDown`. This closes the last open item of the
B41–B59 batch.

### ★ New batch — requested 2026-06-10 — ✅ DONE 2026-06-10 (1, 3, 4; 2 = code-verified, playtest open)
1. ✅ **Planet enemies — real bodies + assets** (was: two-block-tall flat-red untextured cubes in
   `WorldEntities.cs`). Now a procedural blocky **alien fiend**: hunched chitin torso + pelvis, horned skull
   with three glowing amber eyes (bloom catches them), clawed arms, digitigrade legs with talon feet, dorsal
   spikes; skinned with a new AI **`enemy_hide`** chitin tile (OpenAI) on `LitColor` (lit + casts/receives
   shadows under URP). **Animations** (procedural, like the avatar/creatures): speed-driven stalk cycle,
   idle menace-sway + look-around, arms raised when hostile, **claw-swipe attack lunge** (hostile + in melee
   range, throttled), hurt **flinch** on hull drops; faces its walk direction, or the player when hunting.
   **Sounds** (ElevenLabs): `enemy_growl` (periodic idle), `enemy_attack`, `enemy_hurt`, `enemy_die` (on the
   `PlanetEnemyDefeated` event), spatialised + per-enemy pitch/size variation. NOTICES updated.
2. **Sun path / moving shadows / sun colour — code-verified ✅, playtest open.** All three exist:
   `Sky.ApplyLighting` rotates the sun with local time of day (`Euler(time*360−90, 160, 0)`, longitude-shifted
   terminator), under URP the real-time shadows follow the directional light (wander/lengthen across the day),
   and the sun Light + world tint + grade use the per-system random star colour (grade share raised to 0.4).
   **Playtest:** watch one full day cycle (shadow length at dawn/dusk, colour at noon); tune the fixed 160°
   azimuth if the arc reads oddly on some worlds.
3. ✅ **BUG fixed — mining rejected "Out of reach." while standing at the block.** Root cause: three stacked
   measurement discrepancies — the client aims an 8 m ray **from the camera** at a block **face**, while the
   server measured **body position → block centre** (eye offset ~0.8 + centre-vs-face up to ~0.87) off a
   **10 Hz unreliable move stream** (position trails ~1 m while walking). Together they pushed legitimate mines
   past `MaxReach`. Fix in `WithinReach` (`GameServer.cs`): measure to the block's **nearest face**, vertically
   against the player's **body segment** (anchor-agnostic), X wrap-aware as before, plus 1 m slack for the move
   stream. (`HandleMove` fully trusts positions anyway — the check is a sanity bound, not anti-cheat.)
5. ✅ **Lively planets — creature + flora amounts per world (2026-06-10).** The live-fauna cap is no longer a
   fixed `min(4×players, 12)`: `WorldCreatureCap` derives each world's population from its
   `CreatureAbundance` (many 20 / few 10 base), its **size** (circumference/6000, 0.5–1.8×) and a **seeded
   per-world jitter** (0.7–1.3×), scaled by √players — lush big worlds now carry ~25–45 live creatures around
   the players, sparse small ones ~5–9; spawn pacing fills fast (1.5 s) below half the cap, then trickles
   (4 s). **Flora:** worldgen rolls a per-world multiplier (0.8–1.6, seeded — `floraMul`) on the planet
   type's flora + tree density, so the same type is scrub on one world and lush on the next (asteroids/
   barren types stay 0; server + client preview agree). Flora growth stays capped at one plant per cell.
4. ✅ **ANALYSIS done — collision trees: not worth it today.** Findings: **blocks** use direct voxel-grid
   lookups (O(1) per cell — optimal; an octree would only add indirection); **client physics** (chunk
   MeshColliders, raycasts) is already BVH-accelerated inside Unity/PhysX; **server entity proximity** is O(n)
   linear scans per tick (`NearestNpc`, creature/enemy targeting, `WrapDistSq` loops) — but populations are
   tiny by design: creatures cap at `min(perPlayer × players, 12)`, NPCs ~3–15/settlement, enemies similar →
   worst case a few hundred distance checks per tick, far below profiling relevance. **Recommendation:** keep
   the voxel grid for blocks; no octree/BVH server-side; IF entity caps are ever raised ~10× (big settlements,
   herds), add a **uniform spatial hash** (cell ≈ interaction radius) for entities — simpler and faster than a
   tree for mostly-surface, constantly-moving agents. Revisit only with a profiler trace showing tick time in
   the scan loops.

### ★ Menu preview bugs — ✅ DONE 2026-06-10 (client built)
- **Ship preview showed the wrong model** (menu → Ship → colour): the paint preview drew a hand-built cube
  silhouette, not the player's real ship. Now `ShipPreviewRig` renders the **real 1:1 voxel ship** from
  `Game.ShipDesign` via a shared `ShipMeshBuilder` (same atlas/`ChunkMesher` as the flight view + what other
  players see), camera auto-framed to the ship's extent; falls back to the silhouette only when no design exists
  yet (never entered space). `SpaceView` left untouched. (Atlas hull can't be runtime-tinted — same as in space.)
- **Character preview showed "no face"** (menu → Settings/Character): the avatar was built WITH a face (same
  `PlayerAvatar.Build` as remotes/NPCs, which do show faces) but the whole-figure framing left it tiny near the
  top edge. Now `AvatarPreviewRig` frames a **head-and-shoulders portrait** (closer camera, look at head height,
  sway ±20°) so the face is clearly visible. Face geometry/lighting unchanged (already correct + seen by others).

**B55 — vendors all sell the same.** Today a vendor's stock = market recipes filtered by **one theme per
location**: settlements use `SettlementTradeFor(name)` (single theme: miners/traders/researchers/settlers),
**stations hardcode `"traders"`** for every vendor (`GameServerSpaceStations.SpawnStationNpcs` `MakeNpc(role,
"traders", …)`). The client `VendorTradeUI.NearestVendorTheme()` already keys the shown stock off the **nearest
vendor NPC's `Theme`**, and the server validates trades against the location theme (`GameServer.cs` ~1846).
Recipes/theme: ~2–3 market recipes each for miners/traders/researchers/settlers + 2 themeless. So all vendors at
one place share one theme → identical stock.
**Plan:** give **each vendor NPC its own seeded theme** (from settlement/station seed ⊕ vendor index/home), so a
place with N vendors offers N different trade themes (and, since theme drives outfit/robotic/cadence, visibly
distinct crew). The client already reads per-vendor `NetNpc.Theme` → shows the right stock automatically. Change
the **server trade validation to use the nearest vendor's theme** (not the location-wide theme). Deterministic;
no new message needed. (Optional later: per-vendor price/quantity jitter; richer per-theme recipe pools.)

**B58 — customisable hotbar.** The inventory is **slot-based** (`Inventory` = `ItemStack?[]`, personal = 24
slots; hotbar = slots 0–8; `SelectedHotbarSlot` is just an index). `InventoryUpdate` already sends each stack's
**`Slot`** index, so the client knows slot positions (`GameBootstrap.ItemInSlot(i)` reads slot i). The inventory
tab (`CraftingTechShipUI.BuildInventoryList`) is currently a **category list**, not a slot grid. No move/swap
intent exists.
**Plan:** add a server **`MoveItemIntent {FromSlot, ToSlot}`** that swaps two personal-inventory slots (server-
authoritative, validates indices) → `SendInventory`. Then a client affordance to move a stack into a hotbar slot
(0–8). Two UI options (see decision): a proper slot grid w/ click-to-pick/place, or a lighter "assign to
quick-slot 1–9" control on the existing inventory list + a 9-slot hotbar strip. "Customising the hotbar" = just
rearranging the existing slots — no new data model.

1. ✅ **Task 3 — softer shadows + lit cave mouths** (done 2026-06-07). The mesher's hard binary skylight is now
   a **soft sky-occlusion** (5×5 horizontal kernel): cave mouths feather into a soft-lit gradient and overhang
   shadows soften, while deep caves stay dark (lamp needed). Mesher-only; the shader already took a continuous
   sky. See the Task 3 plan below.
2. ✅ **Bug — no stars in the space background** (FIXED + confirmed 2026-06-07 — the `Spacecraft/Starfield`
   shader was stripped from the build; added to `m_AlwaysIncludedShaders`). Analyse: in the space view there are **no background stars** —
   I see the system's sun but no stars behind it; there should be stars. *This was already attempted once* —
   analyse precisely and find the actual cause. Check whether the cause has **further implications** elsewhere,
   e.g. **stars should also be visible at night on the planet**.

   ### Analysis + Plan (2026-06-07)
   The starfield IS wired and the render path is correct: `Starfield` builds a 1500-quad dome (additive,
   `Background` queue, `Cull Off`, `ZWrite Off`) that follows the camera and its quads face inward toward it
   (`Starfield.cs`); `SpaceView` reparents the same camera and clears to near-black (`SpaceView.cs:629-631`),
   so nothing culls or occludes the dome. `TargetBrightness()` correctly returns 1 in space / airless / station
   and the day/night curve on a planet. **Likely causes the stars don't read:** (1) the brightness **fades in
   over ~1.4 s** — `_brightness = MoveTowards(_brightness, target, dt*0.7)` (`Starfield.cs:61`) — so on entering
   space they're near-invisible at first; (2) at full they're still **dim/small** (`MaxBrightness 0.9`, angular
   half-size ~0.005 rad, twinkle dips to 0.10) — easy to miss next to the bright bloomed sun + ACES tonemap.
   No hard occlusion found in code. **Implication:** the same component draws planet **night** stars (same slow
   fade + dimness), so the fix helps there too. **Plan:** snap brightness to target instantly when the sky is
   "space/airless/station" (keep the smooth dusk/dawn fade on planets); raise `MaxBrightness`, lift the twinkle
   floor and bump star size a touch so they're clearly visible without washing the sky. Then verify in a build;
   if stars still don't show it points to a render issue only visible at runtime (revisit with the game running).

   ✅ **ROOT CAUSE FOUND + FIXED 2026-06-07:** the **`Spacecraft/Starfield` shader was stripped from the build**
   — the game builds all materials in code via `Shader.Find` (no `.mat` assets), and a shader not listed in
   `GraphicsSettings.asset → m_AlwaysIncludedShaders` is dropped from the player build, so `Shader.Find` returns
   null → `Starfield` disabled itself → black sky. The sun rendered because its `SunGlow` shader WAS listed.
   This is why earlier code tweaks never helped (the shader wasn't in the build at all). **Fix:** added the
   Starfield GUID (`1fe9729…`) to `m_AlwaysIncludedShaders`. Also found **`BlockAtlasTransparent` (`152a7b5…`)
   was missing too** — the glass/water transparent shader — and added it (very likely the root of the **glass
   "not milky" bug, item 3**). The brightness/size/twinkle tweaks above stay (now the stars actually render).
   Saved as a memory ([[shaders-must-be-always-included]]). *Verify in the new build.*
3. ✅ **Bug — glass is still only transparent, not milky** (FIXED + confirmed 2026-06-07 — **same root cause as
   item 2**: the `Spacecraft/BlockAtlasTransparent` shader (glass + water) was stripped from the build, so in the
   built `.exe` glass fell back to a plain transparent material with no milky frost. Adding its GUID to
   `m_AlwaysIncludedShaders` restored the frosted glass — confirmed by the user). The shader's milky/water logic
   was correct all along; it just wasn't in the build. See [[glass-milky-not-transparent]] +
   [[shaders-must-be-always-included]].
4. ✅ **Bug — landing pop-in; want a loading screen** (done 2026-06-07). When I land on a planet or station I
   can see the planet/station **building up before my ship appears** (for stations, before everything is
   finished). Instead I want a **loading screen that disappears once everything is ready**. Analyse precisely,
   then make a plan; only then start implementing.

   ### Analysis + Plan (2026-06-07)
   **Flow:** land (`SpaceView` descent) / board → server sends **`WorldReset`** + the new world's chunks
   (`GameServer.HandleTravel` / `GameServerSpaceStations.BoardStation`). Client `OnWorldReset` drops old chunks,
   clears the world, **bumps `WorldEpoch`** (`GameBootstrap.cs:511`) and nulls `ServerSpawn`. Chunks then stream
   in + mesh (`OnChunk`→`RebuildChunk`, all dirty chunks meshed per frame). `PlayerController` watches
   `WorldEpoch` (`:116`), re-snaps to `ServerSpawn`, and holds a **settle-freeze** (`_settling`) until a downward
   raycast finds the ground collider below spawn (`:146-157`) or a 20 s timeout. **Why the build-up shows:**
   nothing covers the window between `WorldReset` (old world gone, new streaming) and the ground being ready —
   so you watch chunks/the ship/station assemble. `WorldReset.Hyperjump` already marks jumps (those are covered
   by the `HyperspaceWarp` overlay). There's a clean uGUI overlay pattern to reuse (`HyperspaceWarp`,
   `UiKit.CreateCanvas`).

   **Decisions (user):** (a) **show the destination name** (planet/station) + type + a spinner/progress feel;
   (b) the overlay **also covers the very first spawn** at app launch.

   **Implemented:** new **`WorldLoadingOverlay`** (`client/Assets/Spacecraft/Scripts/WorldLoadingOverlay.cs`,
   pure uGUI on a DPI-scaled canvas, sortingOrder 75, fade in/out) wired in `WorldRig.Build` next to the warp.
   - **Trigger:** `GameBootstrap` raises a new **`WorldLoadStarted`** event on `JoinAccepted` (first spawn) and
     on non-hyperjump `OnWorldReset` (landing / station boarding / same-system travel). Hyperjumps keep firing
     `HyperjumpStarted` instead, so the warp VFX (not this veil) covers those.
   - **Don't veil the descent:** the overlay only *arms* on the event and waits while `SpaceViewActive` (the
     ship's landing descent plays in the space scene, which already hides the surface). It raises the veil the
     moment we're back on the surface — and skips it entirely if the world already streamed in during the
     descent (`WorldReady` already true).
   - **Ready signal:** `GameBootstrap.WorldReady` (reset false on join/reset) is set true by `PlayerController`
     the instant the **settle-freeze releases** (ground collider exists below spawn). Veil fades out then, after
     a **0.7 s min-show**; a **25 s max-show** safety guarantees it can never trap the player.
   - **Content:** big destination title (`LocationName`), localized world-type subtitle (reuses the existing
     `planet.<type>.name` keys, e.g. *Felsplanet* / *Orbitalstation*), a 12-dot comet-tail spinner, and a
     localized footer with animated dots — new keys `ui.loading.world` / `ui.loading.station` (DE+EN, all four
     locale files). Station boardings read *Betrete Station* / *Entering station*.

   No change to the landing/chunk/settle logic itself — purely an overlay + one readiness flag + one event.
5. ✅ **Bug/feature — in-space movement: pilot ↔ inside ship ↔ EVA** (done 2026-06-07; in-editor pass pending).
   Original: the player must **not fall in space** — float in the suit, get back into the ship, dock a station.
   Stages 1–3 shipped a pilot↔EVA float (oxygen drain + 6-DOF + board) but that **skipped a state the user
   wants**.

   ### Redesign (2026-06-07) — three states, requested by the user
   The player wants **three** modes in space, not two:
   1. **Pilot** — flying the ship (the current flight view).
   2. **Inside the ship (on foot)** — walk around inside your ship's interior **while it floats in space**.
   3. **EVA** — spacewalk outside the hull, **started from inside the ship** (not from the pilot view).
   **Transitions (all bidirectional):** Pilot ⇄ Inside ship; Inside ship ⇄ EVA. (Pilot does **not** go straight
   to EVA any more — my stage-2 `G` from cruise must move to the inside-ship state.)

   **Approach (analysis):** there is no walkable ship interior in space today — on a planet the ship is stamped
   voxels you can walk in; in the flight view it's an abstract model. The clean, consistent way to add a
   walkable interior in space is to **board your own ship like a station**: reuse the station-interior infra
   (`GameServerSpaceStations` — load a small world, stamp the ship structure, spawn the player on foot, keep the
   ship's flight-view position in `SpaceInstance.ShipPosition`). From inside: a **helm/pilot console** returns
   to the flight view (skip-launch, no take-off animation), an **airlock** starts an EVA; the EVA float +
   oxygen + board-back from stages 1–2 are reused (only the entry point changes — EVA from the airlock instead
   of from cruise). Needs confirmation of the approach + the control scheme before building (see questions).

   ### Pending follow-up requirements (from the user, 2026-06-07)
   - **R1 — EVA landing targets:** from EVA you may dock **your own ship** and **space stations**, and land on
     **asteroids**, but **not on planets or moons** (those need the ship). *(server guard + client prompt)*
   - **R2 — multiplayer visibility:** an EVA/in-space player must be **visible to other players** flying in the
     same space instance. *(Big — the flight view renders only your own ship today; remote ships/players in
     space aren't broadcast/rendered. Needs new networking + rendering.)*
   - **R3 — the ship stays put:** if you **dock a station while in EVA**, your **ship remains in space** at its
     position (you didn't take it with you); returning puts you back at the floating ship.
   - **R4 — death respawn:** on death, respawn at the **last planet or station, with your ship** (today: the
     heal-tank `RespawnPoint`; verify it already lands you at the last body, and set it on station boarding).
   - **EVA boarding UX (done 2026-06-07, pending the redesign):** the EVA HUD board hint always shows (distance
     to the parked ship when far), the wrong "G ship" text was fixed, board range widened. *Will be folded into
     the redesign (EVA is launched from the airlock, so the cruise `G` hint changes to "enter ship").*

   ### Plan (locked by the user's answers, 2026-06-07)
   (a) **Inside the ship = the real ship interior** — board your own ship as a walkable world (reuse the
   station-interior infra). (b) **Diegetic controls** — a **pilot console** (E → fly) and an **airlock** (E →
   EVA) inside the ship, with on-screen prompts (the discoverability the user was missing). (c) **Multiplayer
   visibility (R2) is in scope now.**

   **Staged build (server-first, testable; client parts need an in-editor pass):**
   - 🔄 **Stage 4 — board your own ship (walkable interior in space).**
     - ✅ **Server (done 2026-06-07).** New `GameServerShipInterior.cs`: `EnterShipInterior` loads a
       **`ship_interior`** void world (new planet type, clone of `orbital_station`) and **stamps the existing
       ship** into it via the same `StampShip` used on planets (the user's point: the interior already exists),
       spawns the player on foot at the heal-tank, `AboardShip=true`. `ExitShipToFlight` (helm) restores the
       planet world + re-enters the flight view with the ship **put back exactly where it was parked**
       (`ShipPosition` saved across the visit, even though the empty instance unloads). Intents
       `EnterShipIntent`/`ExitShipIntent` + dispatch + `SendEnterShip`/`SendExitShip`; cleared on respawn. Tests
       `EnterShipInterior_ThenHelm_RoundTripsThroughTheFlightView`, `EnterShipInterior_OnlyWorksFromSpace`
       (321 pass).
     - ✅ **Client (done 2026-06-07; needs in-engine test).** Flight view: **F** steps inside the ship
       (`SendEnterShip`), cruise hint updated. Inside the ship the **cockpit is the helm** — using it (E, the
       existing station interaction) calls `ExitShipToFlight` (server branches the cockpit on `InShipInterior`);
       the HUD prompt reads **"Take the helm (fly)"** there (`ui.station.helm`). Returning to the helm **skips
       the take-off animation**: new `SpaceState.SkipLaunch` (set by `EnterSpace(skipLaunch:true)` from
       `ExitShipToFlight`), latched on entry in `GameBootstrap.SpaceSkipLaunch`, so `SpaceView.Enter` starts in
       `Cruise` with no roar. Test `UsingTheCockpitInsideTheShip_TakesTheHelm`. *(Airlock→EVA is stage 5; EVA is
       still `G` from cruise for now.)*
   - ✅ **Stage 5 — EVA from the airlock (done 2026-06-07; needs in-engine test).** A new **airlock** station
     marker by the ship's hatch (`StampShip`); using it in space (`UseStation "airlock"` → `StartEvaFromShip`)
     cycles out into the flight view as a floating EVA suit (`InEva=true`, oxygen drains). The cruise `G`→EVA is
     **removed** — EVA is only reached from inside the ship now (hint updated). Client mirrors the server's
     `InEva` (`BeginEvaMode`, no client-initiated EVA); **EVA → board returns you to the ship interior** on foot
     (`BoardShipFromEva` → `SendEnterShip`), not to the helm. New `_enteringInterior` flag tears the flight view
     down at once (no stray landing descent) when stepping inside. New `ui.station.airlock`. Test
     `AirlockInsideTheShip_StepsOutIntoAnEva` (323 pass). **The three-state loop is now complete:** pilot —F→
     inside ship —(cockpit)→ helm/fly, —(airlock)→ EVA —(E at ship)→ back inside.
   - ✅ **Stage 6 — R1: EVA landing targets (done 2026-06-07).** From an EVA you may board your own ship + dock
     stations (already the only EVA actions); landing on a body is now **restricted to asteroids** by a server
     guard: `HandleLeaveSpace` rejects a land while `InEva` unless the target body's `WorldSizeClass` is
     `Asteroid` (`EvaLandingAllowed`). So planets/moons are never landable on foot — board the ship first. Test
     `Eva_CannotLandOnAPlanet_OnlyAnAsteroid` (324 pass). **Note:** walkable **asteroid bodies aren't generated
     into the flight view's landables yet** (`_landables` = planets + moons only), so asteroid-landing-from-EVA
     is enforced-but-not-yet-reachable; making asteroid bodies landable in flight is a separate task — the guard
     is already correct for when they exist.
   - ✅ **Stage 7 — R3 + R4 (done 2026-06-07).**
     - **R4 — death respawn with the ship.** `RespawnPlayer` now recovers correctly from anywhere: on foot on
       the ship's own world it just snaps to the heal-tank (fast path, no loading screen), but dying in the
       flight view / on an EVA / inside the ship / on a station does a full **world transition** to the ship's
       planet heal-tank (`RecoverToShip` → `LeaveSpace` + `LoadWorld` + `StampShip` + `WorldReset`), so you're
       never left stuck in the flight view or a stale world and always come back **with your ship**. Also:
       **stations now sate hunger** (life support — no more starving while docked). Test
       `DeathOnAnEva_RecoversToTheShip_NotStuckInSpace`.
     - **R3 — the ship stays floating.** Docking a station **while on an EVA** records the ship's floating
       position (`_dockedFromEva`); **undocking returns you to the float next to the waiting ship** (InEva
       restored, ship position restored, no take-off) instead of re-launching. The client keeps `InEva` set
       through the dock so the server knows. Test
       `DockingAStationOnAnEva_KeepsTheShipFloating_AndUndockReturnsToEva`. (326 pass.)
   - ✅ **Stage 8 — R2: multiplayer visibility in space (done 2026-06-07; needs an in-engine 2-player test).**
     Additive (visibility only — the shared `ShipPosition` still drives collision). Server: `SpaceInstance.
     PlayerPoses` tracks each player's pose (pos + yaw + EVA flag, from `InEva`), updated on `ShipMove`;
     `SpaceState.Players` (new `NetSpacePlayer[]`) carries the OTHER players in the instance, filled in
     `SendSpaceState` (already broadcast every space tick). `ShipMoveIntent` gained `Yaw`. Client: the flight
     view + EVA now report yaw (and EVA reports the suit pose); `SyncRemotePlayers` pools a remote avatar per
     player — a ship cube when piloting, a small suit cube on an EVA — placed from the broadcast poses. Test
     `TwoPilotsInTheSameSystem_SeeEachOtherInSpace` (327 pass). *(Single shared ship position for collision is
     a pre-existing limitation, untouched.)*

   **✅ Item 5 complete** (stages 1–8 + R1–R4): pilot ⇄ walkable ship interior ⇄ EVA, oxygen, board/dock,
   asteroid-only EVA landing, ship stays put, death recovers with the ship, and other players are visible in
   space. The Unity client parts across these stages still want one in-editor pass.

   **Interim (committed before the redesign):** the pilot↔EVA float (stages 1–3) + the EVA-boarding UX fix ship
   now, so the current build is usable/clearer while the redesign is built on top.

   ### Analysis (2026-06-07)
   **Today there is no "on foot in space" state at all.** "In space" only ever means *piloting the ship* in the
   flight view:
   - **Gravity / falling** — `PlayerController.Move()` does `_verticalVelocity -= Gravity * dt` whenever the
     player is in the air (`PlayerController.cs:~779`, `Gravity = 20f`). Gravity is only skipped while:
     `SpaceViewActive` (on-foot control frozen entirely, `:168-171`), a menu/chat is open (gravity-only,
     `:175-179`), the spawn **settle-freeze** (`:146-164`), or **swimming** (buoyant branch, `:760-766`).
   - **Jetpack** — hold Jump in the air to thrust up; needs the `jetpack` item **and** `SuitEnergy > 0`
     (`CanJetpack()` `:724`); server drains energy in `GameServerEquipment.TickJetpack` (~9/s). Caps rise at
     `JetpackMaxRise`. This is the closest existing "suit thrust" code.
   - **"In space" state** — `GameBootstrap.InSpace` flips true on `SpaceStateReceived`, false on `SpaceClosed`.
     Server-side a player is "in space" iff they have a space-instance id. `EnterSpace` (`GameServerSpaceCombat
     .cs:217`) **requires `AboardShip`** ("Board your ship before launching into space.") — so you can only be
     in space *as the ship*.
   - **Launch** — the **`ui.space.enter`** button in the ship tab (`CraftingTechShipUI.cs:612`) → `SendEnterSpace`
     → server `EnterSpace` → `SpaceState` → client `InSpace=true` → `SpaceView.Update` (`:112`,
     `if (Game.InSpace && !_active) Enter()`) → `Enter()` **always** starts `Phase.Launch` — the rising take-off
     animation + `ship_launch` roar + `SpaceViewActive=true` (`SpaceView.cs:573-619`). There is no "skip the
     take-off" path.
   - **Docking** already exists: in the flight view, E near a station → `Phase.Boarding` dock animation →
     `SendBoardStation` → `GameServerSpaceStations.BoardStation` (range 70). On-planet there's also
     `HandleStations` (E to board a nearby station). `LeaveStation` relaunches you as the ship into space.

   ### Plan (subsystems — exact behaviour pending the questions below)
   1. **New state: on-foot in space (zero-g).** Add a server/player flag (e.g. `InSpaceOnFoot`) distinct from
      the ship's space instance, mirrored to the client (`WorldEnvironment`/a small message). While set:
      - **No gravity** — add it to the list of gravity-skip conditions in `PlayerController.Move()`; replace the
        fall path with a **float/drift** path (zero vertical pull; velocity damps to 0 when no input).
      - **Suit float movement** — *(feel TBD, see Q)* either full 6-DOF (mouse-look + WASD + ascend/descend, like
        the ship's `UpdateCruise`) or on-foot WASD + jetpack-style up/down with no ground. Slow, "floaty" accel +
        drag, not walk speed.
      - **Oxygen/energy** — *(TBD, see Q)* either drains suit oxygen like being submerged (risk → must get back),
        or free.
   2. **Board your ship from space (no take-off animation).** A "board/enter ship" action available while
      floating → transitions straight into the **flight view at `Phase.Cruise`** (skip `Phase.Launch`). Implement
      by passing a flag on `SpaceState` (e.g. `SkipLaunch`/`AlreadyInSpace`) so `SpaceView.Enter()` starts at
      Cruise with no `ship_launch` roar. **Launch button fix:** when the player is already in space (on foot),
      the `ui.space.enter` action must use this no-animation path (and read "Board ship" rather than "Launch").
      *(Where the ship is / how you reach it — see Q.)*
   3. **Dock at a station from a float** — reuse the existing station-boarding path (range-gated `BoardStation`)
      from the on-foot-in-space state, not only from the flight view.
   4. **Reverse (EVA) — leave the ship into a float** — *(only if wanted, see Q)* a "step out / EVA" action from
      the flight view that drops you into the on-foot zero-g state next to the ship.

   No world-gen or persistence changes expected beyond the new flag; mostly `PlayerController` (gravity + float
   movement), `SpaceView.Enter` (skip-launch), one server flag + message field, and the launch-button label/branch.

   **Decisions (user, 2026-06-07):** (a) scope = **both** — build EVA now **and** keep the float logic generic
   so item 10 (build into space) docks on later; (b) movement = **full 6-DOF free-fly** (mouse-look + WASD +
   ascend/descend, like piloting); (c) board the ship in space by **floating up to it** (E to board,
   range-gated like station docking); (d) floating **drains suit oxygen** (time pressure → get back in time).

   **Implementation — staged:**
   - ✅ **Stage 1 — server EVA foundation (done 2026-06-07).** `PlayerState.InEva` + the `SetEvaIntent`
     message/handler (`HandleSetEva`, only honoured while actually in a space instance). The oxygen tick now
     treats `InEva` as "no life support, no atmosphere" so oxygen drains even over a breathable world and the
     extractor can't help; empty → the existing suffocation damage. `InEva` is cleared on leave-space, station
     docking, ship loss and death, and mirrored to the client in `PlayerStateUpdate` (`GameBootstrap.InEva`) +
     `NetworkClient.SendSetEva`. Test: `Eva_DrainsOxygen_EvenOverABreathableWorld`. (319 tests pass.)
   - ✅ **Stage 2 — client EVA mode in `SpaceView` (done 2026-06-07; needs in-engine test).** **G** in the
     flight view steps out into a first-person **6-DOF** spacewalk (`EnterEva` → `SendSetEva(true)`): the ship
     stays parked, mouse-look + WASD + **Space/Ctrl** up/down at `EvaSpeed`, bounds + body keep-out reused.
     Float up to the parked ship and **E boards it with no take-off animation** (`BoardShipFromEva`); E near a
     station docks it (`DockStationFromEva`, reuses the board teardown). EVA HUD: float-controls hint, a
     ship/station board prompt, and an **O₂ readout** (red + pulsing under 25 %). Cruise hint now lists `G EVA`;
     crosshair/flare/systems/engine gated off during EVA. New keys `ui.space.eva_controls` /
     `.eva_board_ship` / `.eva_oxygen` (DE+EN). *Unity client — couldn't be compiled in the sandbox; verify in
     the editor (feel of the float, board ranges, the O₂ drain loop).*
   - ✅ **Stage 3 — launch-button finding + zero-g groundwork (done 2026-06-07).** **Launch button:** already
     correct — the `ui.space.enter` button is guarded by `if (!Game.InSpace)` (`CraftingTechShipUI.cs:609`) and
     shows **Leave** instead while in a space instance, so it never replays the take-off animation in space. The
     EVA→ship board (stage 2) is client-side, so it's animation-free too. (Undocking a station deliberately
     re-launches from the return planet — left as designed.) The remaining no-anim case belongs to item 10
     (launching from a tower already in space); a `SpaceState` skip-launch flag will be added there. **Zero-g
     net:** `GameBootstrap.OnFootInSpace` + a float branch in `PlayerController.Move` — above the atmosphere
     there's no gravity, so the player floats (Jump rises, Ctrl/C sinks, else drifts to a stop) instead of
     falling. Nothing sets the flag yet (a no-op until item 10 flips it) — the "must not fall in space" net.

   ### Item 5 follow-ups (in-space / EVA polish — added 2026-06-07, do before the bigger tasks below)
   These refine the just-shipped pilot ↔ ship-interior ↔ EVA feature (user feedback after testing).
   - ✅ **5b — Player ship exterior textures (done 2026-06-07).** No new assets — `SpaceView.BuildShip` and the
     remote avatars (`BuildRemoteAvatar`) now use the **same block textures as the on-planet/station hull**
     (`LitTex` with `iron_wall` hull, `glass` cockpit, `carbon` engine) + wingtip nav lights, instead of flat
     `Unlit` cubes. Reuses the existing `Lit` material/path (already in the build for stations), so ships read
     as real textured hulls from space.
   - ✅ **5c — Show the ship's entry hatch while on an EVA (done 2026-06-07).** `BuildShip` now has a glowing
     cyan **hatch marker** on the ship's tail (where the voxel hatch is); it glows steady normally and **pulses
     strongly while on an EVA** (`_hatchMat` pulse in `LateUpdate`) so you can spot where to board back in from
     a distance.
   - ✅ **5d — EVA must not fly *into* the ship (done 2026-06-07).** `SpaceView.UpdateEva` now bounces the suit
     off a `ShipKeepOut` (3.5) shell around the parked ship and slides it along the hull (like the station
     keep-out); the board range (11) is larger so you still drift to the hatch and press E.
   - ✅ **5e — "Launch into space" from inside the ship no longer plays the take-off animation (done
     2026-06-07).** In the ship-interior void world (`Game.LoadingPlanetType == "ship_interior"`) the ship-tab
     button now reads **"Take the helm (fly)"** and calls `SendExitShip` (the skip-launch helm path) instead of
     `SendEnterSpace`; on a real surface it stays the normal "Launch into space".
   - ✅ **5f — Ship hatch starts sealed (done 2026-06-07; user feedback).** The hatch's sliding door used to be
     **open on spawn** because you spawn at the heal-tank, which is inside the slide door's 4.5-block open range.
     Added a **per-door `OpenRange`** (`ServerDoor.OpenRange`, used in `TickDoors`); the **ship hatch** now uses a
     tighter **`ShipHatchOpenRange = 1.8`** so it stays **closed where you spawn** and only slides open when you
     **walk right up to it** to leave. Settlement/station slide doors keep 4.5. Test
     `ShipHatchDoor_StaysSealedAtTheSpawn_ButOpensWhenYouWalkUpToIt` (364 green). Server rebuilt.
6. ✅ **Bug — save the player's position per planet** (done 2026-06-07; scope: last planet). When I land on
   another planet, my **position there** should be saved too, so on **loading the save I'm back there** (not
   just the last/home world).

   ### Analysis (2026-06-07)
   **Root cause:** on join/load the player is **always placed on the home/default body**. `HandleJoin`
   (`GameServer.cs:1122,1132`) does `LoadWorld(_meta.DefaultPlanetType, _meta.ActiveLocationId)` and then
   `new PlayerSession(...) { CurrentLocationId = _meta.ActiveLocationId }` — the player's saved **Position** is
   restored, but in the *home* world's coordinate space, so if you saved on another planet you're dropped onto
   the home world at the wrong spot. **Why:** `PlayerSnapshot` (`Snapshots.cs:87`) persists the position
   (X/Y/Z) but **not which body** the player was on — only the **ship** persists `CurrentLocationId`
   (`ShipSnapshot.CurrentLocationId`). The player's body lives on the (non-persisted) `PlayerSession`, and
   the player + ship travel together via `HandleTravel`, so the body is recoverable, but it isn't restored.

   **Plan (pending the scope question):**
   - **Persist the player's body:** add `CurrentLocationId` to `PlayerSnapshot` + round-trip it (saved from
     the session on `SavePlayer`; the session already syncs it). On join, set the session's location from the
     saved value (fall back to the ship's `CurrentLocationId`, then the home body), `LoadWorld` that body's
     type, and place the player at the saved `Position` (still guarded by `EnsureSafeSpawn`).
   - *(If per-planet memory — option B below — also keep a per-body position map and restore it when you travel
     back to a previously-visited body, instead of the landing-zone spawn.)*

   **Decision (user): (a) last planet.** **Implemented:** `PlayerState.CurrentLocationId` is now persisted
   (added to `PlayerSnapshot` + the mappers; `PlayerSession.CurrentLocationId` delegates to it so every
   existing assignment is saved). On join both paths (`HandleJoin`, `AddLocalPlayer`) call `RestoreJoinBody`:
   if the saved body is a real landable galaxy body (planet/moon/asteroid) it loads + places the player there
   at the saved `Position`; otherwise (first join, or a transient station/space save) it falls back to the home
   body. Test `Reload_RestoresPlayerToTheLastPlanet_NotHome` (328 pass). *(Full per-planet memory — option b —
   left for later if wanted.)*

   - ✅ **6b — Auto-save on landing / docking (done 2026-06-07).** New `CheckpointSave(reason)` (wraps
     `SaveAll` + a log) is called when the player lands on a body (`HandleTravel`; `HandleLeaveSpace` return-to-
     surface) and when docking a station (`BoardStation`) — the natural checkpoints, so item 6's per-planet
     position is captured there, not only on the autosave timer / explicit save.
7. ✅ **Bug — creatures chase forever + spawn only at the ship (done 2026-06-07).** Creatures seem to **follow
   the player constantly**. After a while, pursuing/attacking creatures should **leave the player alone**. Also:
   creatures **shouldn't only spawn at the ship** but be **distributed across their biomes**.
   - **(a) Give-up leash (done).** `CombatEntity` gained `ChaseTimer` + `GiveUpTimer`. In `MoveCreatures` an
     aggressor (Aggressive/PackHunter, or a provoked territorial one) within aggro range accumulates `ChaseTimer`;
     past `CreatureChaseGiveUpSeconds` (**7 s**) it gives up — sets `GiveUpTimer` = `CreatureGiveUpCooldownSeconds`
     (**15 s**), during which `Step` gets `null` (it wanders off) **and** the damage loop skips it (no biting).
     `ChaseTimer` decays when not chasing; after the cooldown it can re-engage. New `WithinAggro` mirrors `Step`'s
     2D test. (User pick: "Kürzer & milder ~7 s → ~15 s".)
   - **(b) Spread spawns (done).** `SpawnRing` widened from a tight 7–13 block circle to **~18–45 blocks**,
     scattered across mixed radii/angles (inner/mid/outer bands), so fauna populates the surrounding biomes out of
     immediate sight instead of clustering on the ship. Biome-native gating kept. (User pick: "weiter weg +
     verstreut".)
   - Tested: new `Aggressor_GivesUpAfterChasingTooLong_ThenItsCooldownDecays`; full suite 329 green.

   ### Analysis (2026-06-07)
   - **(a) Chase never ends.** `MoveCreatures` (`GameServerCreatures.cs:290`) feeds the nearest player into
     `CreatureBehaviour.Step`, which makes an aggressor/pack-hunter move **toward** any player within
     `CreatureAggroRange` (10) — with **no give-up timer**. So while you stay within ~10 blocks an aggressor (or
     a provoked territorial one, `ProvokeTimer`) keeps coming + attacking (the damage loop at `:142-177`)
     indefinitely. (Running >10 away already breaks aggro, but staying near = relentless.)
   - **(b) Spawn clusters on you/the ship.** `TrySpawnCreatureNear` (`:198`) spawns each creature at
     `player.Position + SpawnRing[...]` where the ring is a **tight 7–13 block** circle, biome-gated by the spot.
     With a small cap (4/player, ≤12) + 70-block despawn, fauna only ever exists in a tight cluster around the
     player — and since you start/return at the ship, it reads as "they only spawn at the ship".

   ### Plan
   - **(a) Give-up / leash:** add a per-creature `ChaseTimer` + `GiveUpTimer` (on `CombatEntity`). An aggressor
     chasing within aggro range accumulates `ChaseTimer`; past a cap it **gives up** — sets a `GiveUpTimer`
     cooldown during which it ignores the player (wanders off, `Step` gets `nearest = null`) **and does not
     attack** — then can re-engage. `ChaseTimer` decays when no player is near.
   - **(b) Spread spawns:** widen + randomise the spawn placement (farther out, e.g. ~18–45 blocks at a
     scattered angle/rotor) so creatures populate the **surrounding biomes out of immediate sight** and you meet
     them as you explore, instead of popping in 7–13 blocks away. Keep the biome-native gating.

   **Questions:** (1) Give-up feel — roughly how long should an aggressor chase before giving up, and how long
   should it leave you alone after? (default ~12 s chase → ~10 s cooldown). (2) Spawn spread — OK to spawn
   creatures **farther out + scattered** (so you discover them around the biome) rather than right next to you?
8. ✅ **Task 4 — content-styled icons (done 2026-06-07).** Real per-item art everywhere instead of crude
   procedural glyphs / shared category icons.
   - **Client `IconResolver`** (new) — one key-based source for hotbar + crafting/tech/ship menu + space-systems
     bar: a generated full-colour PNG (`Resources/icons/item_<key>.png`) → else the in-game **block atlas tile**
     for a material that places/equals a block → else the old procedural glyph. Toxic consumables
     (`ConsumeHealth < 0`) get a runtime **green tint** (user pick — no extra assets).
   - **60 AI icons generated** via new `tools/ai-assets/gen_item_icons.py` (OpenAI `gpt-image-1-mini`, full-colour
     transparent, downscaled to 128²): 39 non-block items (mats/components, consumables incl. a steak, tools,
     weapons, suit gear) + 21 ship modules; space laser/tractor reuse `ship_laser_basic`/`tractor_beam`.
     Block-backed materials reuse their block tile (no asset). Blueprints reuse their unlocked key's icon.
   - **Wired:** `AddCard(..., contentKey)` in the menu (recipes/blueprints/modules/inventory), the hotbar
     (RawImage path + `PlacesBlock` tile fallback + green tint), and a selected-system icon over the space bar.
   - **Tested:** `IconCoverageTests` (every item/module resolves to an icon; toxic flagged) — 332 green.
     `NOTICES.md` updated; full client rebuilt (icons bundled).

   ### Analysis (2026-06-07)
   **Two parallel icon systems today, neither gives per-item art:**
   - **Hotbar (on-foot)** `HudUi.cs:311-321`: block-category items already draw a **downscaled block atlas tile**
     (`Game.Atlas.TileUv(numericId)` on a `RawImage`). Non-block items fall to `IconFactory.ForItem(key, kind)`
     — a 16×16 **procedural glyph** (ring/diamond/cross/box, colour hashed from the key). Crude, not per-item.
   - **Crafting / tech / ship menu** `CraftingTechShipUI.cs:1287-1301`: every row uses a **category** icon string
     (`cat_tools`, `cat_weapons`, …) via `UiKit.Icon()` → `Resources.Load<Texture2D>("icons/<name>.png")`. So a
     whole category shares one icon; individual items/blueprints/modules are indistinguishable.
   - **Block textures** are real, AI-generated 64×64 tiles in `Resources/textures/*.bytes` (34 of them: ores,
     metals, flora, glass, …) loaded by `BlockTextureAtlas`. **Materials that correspond to a block already have
     usable art** — it just isn't surfaced as an inventory/menu icon.
   - **Space view** `SpaceView.cs`: ship weapons/modules are **text labels** only (`ui.space.sys_laser`,
     `sys_tractor`); the tractor beam is a cyan cube. No icons.

   **Asset pipeline (ready to extend):** `tools/ai-assets/` has `gen_icons.py` (OpenAI `gpt-image-1-mini`,
   cyan-line transparent PNGs → `client/Assets/Resources/icons/<name>.png`, ~$0.005 each, `--only`/`--dry-run`,
   resumable) and `gen_textures.py` + `bundle_textures.py` (full-colour tiles → `Resources/textures/*.bytes`).
   Keys in `tools/ai-assets/.env`. Adding a new `gen_item_icons.py` manifest mirrors the existing pattern.

   **What needs an icon (audited — `data/*.json`):**
   - **60 items** (`items.json`): ~**22 map to a block** (placesBlock or same key → free **atlas tile** icon:
     stone/dirt/ores/glass/iron_wall/sand/mud/crystal/ice/basalt/wood_log/ladder/stairs/seeds…). The other **~38
     have no block art** and need a real icon: processed mats (iron_ingot, iron_plate, copper_wire, cable,
     carbon_composite, energy_cell_1, titanium_plate, data_fragment, plant_fiber), 7 consumables
     (creature_meat, **toxic_gland**, berries, **toxic_berries**, emergency_ration, medpack, oxygen_tank_1),
     6 weapons, 6 tools, 11 suit/wearable components.
   - **23 ship modules** (`ship_modules.json`) + the **space tools** (laser, tractor_beam) — for the builder/space
     UI.
   - **36 blueprints** (`blueprints.json`): each unlocks an item/module of (almost) the **same key** → **reuse
     that icon**, no separate blueprint art needed (fallback to category icon if unmatched).
   - No JSON schema change required: icons resolve **by key** at load (block-atlas tile, else `item_<key>.png`,
     else procedural fallback). Toxic = `consumeHealth < 0` (drives the green-meat variant).

   ### Plan
   1. **Client `IconResolver`** (new) — single source of truth used by BOTH hotbar and crafting/tech/ship menu:
      given an item/module/blueprint key return, in order: (a) **block atlas tile** as a `Sprite`
      (`Sprite.Create(atlas.Texture, tileRectInPixels, …)`) when the key places/equals a textured block; (b) a
      loaded **`Resources/icons/item_<key>.png`** sprite if present; (c) **`IconFactory`** procedural glyph
      fallback. Blueprint → unlocked-key lookup first. This makes the menu show the same real icon as the hotbar.
   2. **Generate the missing icons** via a new `tools/ai-assets/gen_item_icons.py` (manifest of the ~38 items +
      ~21 modules + space tools, each with a short prompt; downscale to 128×128 transparent PNG). **Test-first:**
      one sample icon generated + shown for approval, then the batch (per the asset-approval rule). Copy to
      `Resources/icons/item_<key>.png`; update `NOTICES.md`.
   3. **Meat/toxic:** a steak icon for `creature_meat`; toxic consumables (`toxic_gland`, `toxic_berries`) get a
      **green** treatment (either a dedicated green icon or a runtime green tint of the base icon — see Q).
   4. **Wire space view:** show the laser/tractor (and ship modules) icons in the ship systems bar + builder UI.
   5. **Bilingual + tests:** no new user text (icons are visual); add a small test that `IconResolver` returns a
      non-null sprite for every item/module key (catches missing art) and that toxic resolves to the green path.
   6. Build + client rebuild (icons are `Resources`, picked up by the player build).

   **Open questions for the user → see chat.**
9. ✅ **Feature — holographic visor HUD (done 2026-06-07; confirmed working in-game).** The diegetic HUD reads as
   a hologram projected onto the inside of the suit visor. **Decisions (user):** (a) **direct true HUD
   projection** (B1, not the mild whole-frame option); (b) **always on**; (c) **reflections yes**. *(Needed two
   in-build fixes the player log surfaced: build the composite material lazily — `OnEnable` ran before the shader
   was set — and resolve the HUD layer by index, since a freshly-added layer NAME isn't baked into a batch build;
   see [[unity-layer-index-not-name]].)*
   - **B1 pipeline:** the diegetic HUD canvases (HudUI + Space radar) render through a dedicated **UI camera**
     (`VisorHud`) on a new **`VisorHud` layer (8)** into an ARGB32 **render texture**; the main camera excludes
     that layer. A new `VisorComposite` (after `PostFx`) runs a fullscreen **`Spacecraft/Visor`** pass over the
     post-processed world: **barrel curvature**, **chromatic-edge fringe**, **scanlines + flicker**, a 4-tap
     **glow**, a faux-**fresnel rim glow**, and a faint **world reflection** (the requested reflections). **Head
     parallax** lags the projection slightly via the camera's yaw/pitch delta.
   - **Safe + idiomatic:** menus/dialogs/map/loading/death stay flat screen-space overlays (readability); the
     diegetic HUD is non-interactive so no raycast remap is needed; `HudLayerEnforcer` keeps dynamic children on
     the layer. **Degrades gracefully** — if the layer or shader is missing, `UiKit.HudCamera` stays null and the
     HUD falls back to a normal overlay (never lost). New shader registered in `m_AlwaysIncludedShaders`
     (see [[shaders-must-be-always-included]]). Always on; gentler under the reduced-effects preset.
   - Files: `Shaders/Visor.shader`, `Scripts/VisorHud.cs` (+ `VisorComposite`, `HudLayerEnforcer`),
     `UiKit.CreateDiegeticCanvas`, wired in `WorldRig`; `HudUi`/`SpaceRadar` use the diegetic canvas. *(Space
     overlay systems bar left as flat overlay — it shares the canvas with the full-screen launch fade.)*

   ### Original ask + analysis (kept for reference)
   *(Analysis was: can the UI look like a holographic HUD on a curved visor; which effects — curvature/parallax,
   fresnel/rim glow, scanlines, chromatic fringe, bloom/glow, distortion + reflections.)*

   ### Analysis (2026-06-07)
   - **All UI is `RenderMode.ScreenSpaceOverlay`** (`UiKit.CreateCanvas`), across ~11 canvases ordered by
     `sortingOrder`: diegetic HUD (Nameplates 8, **HudUI 10**, Space radar 10, **Space overlay 12**) vs.
     interactive chrome (Interactions 22, Chat 25, **Crafting/Tech/Ship 50**, Map 60, Warp 70, Loading 75,
     Death 80). **Key constraint:** overlay canvases draw *after* the camera, so the existing `OnRenderImage`
     post-FX **cannot** touch them — a visor look on the HUD needs the HUD rendered through a camera/RT first.
   - **A real post-FX pipeline already exists** — `PostFx.OnRenderImage` (on the main camera, `WorldRig.cs:67`)
     chains SSAO → **bloom** → composite (ACES tonemap + exposure + **vignette**), via `Spacecraft/PostBloom`,
     `PostComposite`, `PostAO`. So bloom/glow + vignette + a final fullscreen blit hook are **already there** to
     build on; the holographic glow is largely free.
   - **Full-screen overlays today:** `WeatherFx` uses IMGUI `OnGUI`/`GUI.DrawTexture` washes (underwater/rain/
     lightning) *behind* the HUD; `DeathFx` uses a high-order canvas `Image`. Both are independent layers.
   - **Single main camera** (reparented only by SpaceView); no UI camera. Custom shaders are `Shader.Find`-loaded
     with fallbacks — **a new visor shader must be added to GraphicsSettings `m_AlwaysIncludedShaders`** or it
     strips from the build (see [[shaders-must-be-always-included]]). Settings tab + a `ReducedEffects` preset
     already exist (accessibility/perf toggle hooks).
   - **Effect feasibility** (all cheap, one fullscreen pass): curvature = barrel UV-warp; chromatic fringe =
     radius-scaled R/G/B UV offset; scanlines = `sin(uv.y*N)`; distortion = animated noise UV nudge; rim glow =
     faux-fresnel radial gradient brightening toward the visor edge; glow = reuse bloom; parallax = offset the
     HUD-sample UV by the camera's frame-to-frame yaw/pitch delta; reflections = a faint additive static
     smudge/streak texture (optionally tinted by the world frame). True 3D fresnel/parallax only comes with the
     world-space-mesh variant (B2).

   ### Plan (when greenlit)
   - **Scope split (important):** apply the visor look ONLY to the **diegetic always-on HUD** (crosshair, vitals,
     hotbar, compass, location, toasts, scan, space systems bar + space overlay) — **NOT** to menus/dialogs/map/
     loading (those stay flat + crisp for readability). Conveniently the diegetic HUD is mostly **non-interactive**
     (hotbar is number-key selected), so routing it through a render texture needs **no raycast remapping**.
   - **Recommended mechanism — HUD → RenderTexture → visor composite (B1):**
     1. Render the diegetic HUD canvases via a dedicated **UI camera** (own "HUD" layer, transparent clear) into a
        screen-sized, **full-res** RT (keep text crisp); menus keep their own overlay canvases untouched. (UiKit
        gains a `CreateHudCanvas` that targets the UI camera in Screen-Space-Camera mode.)
     2. New **`Spacecraft/Visor` shader** — one fullscreen pass over the world frame that samples the HUD RT and
        applies barrel curvature + chromatic fringe + scanlines + noise distortion + faux-fresnel rim glow +
        emissive bloom + faint reflection, then composites over the crisp world. Hooked as the **final step in
        `PostFx`** (or a Blit right after). Uniforms: `_Time` (scanline/flicker), camera angular velocity
        (parallax), `_Intensity` (settings). Register it in `m_AlwaysIncludedShaders`.
     3. **Settings + accessibility:** a "Visor HUD" on/off + intensity in the Settings tab; **auto-off under
        `ReducedEffects`**; subtle by default (legibility first — kids play this).
   - **Lighter alternative (B0, ship-first option):** skip the RT/second camera; add mild **whole-frame** barrel +
     edge chromatic fringe + faint scanlines + stronger vignette inside `PostComposite`. Reads as "inside a
     helmet", touches no canvas, near-zero risk — but warps the world view slightly and isn't a true "projected
     hologram". Could ship as Phase 1, with B1 as Phase 2.
   - **World-space-mesh variant (B2, most immersive, most work):** map the HUD RT onto a curved spherical-section
     mesh in front of the camera with a holographic additive material — gives real curvature + head parallax +
     true fresnel, but adds mesh gen, depth/occlusion handling and is the heaviest. Defer unless B1 isn't enough.
   - **Open decisions for greenlight:** (a) B0 mild-whole-frame vs B1 true HUD-projection (recommended B1, with B0
     as a quick first pass); (b) default intensity / always-on vs only-in-EVA-or-space; (c) include faked
     reflections or keep it clean.
10. ✅ **Feature — build high enough to leave the atmosphere into space (done 2026-06-07).** Build a tower tall
   enough and you climb out of the atmosphere into space on foot. **Decisions (user):** on-foot **zero-g in the
   same world** (not the flight view); **per-planet height by density**; **airless bodies = a short climb**.
   - **Server-authoritative:** new `PlanetType.AtmosphereHeight` (absolute Y, in `planets.json`). `TickEnvironment`
     → `UpdateAboveAtmosphere` flips `PlayerState.AboveAtmosphere` for an on-foot player (not aboard/EVA/ship-
     interior/station) crossing the line, with a 4-block **hysteresis** so it doesn't flicker. Broadcast in
     `PlayerStateUpdate`.
   - **In-space-on-foot:** **oxygen drains** above the line even on a breathable world (extractor gives no
     benefit in vacuum); the client sets the dormant **`OnFootInSpace`** → existing `PlayerController` **zero-g
     float** kicks in; **`Sky`/`Starfield`** switch to a **space sky** (black + stars) regardless of the planet's
     own sky. A bilingual toast on crossing up/down (`hud.atmosphere.left`/`.entered`, DE+EN).
   - **Per-body heights:** breathable jungle/varied 240, swamp 230; toxic rocky/desert 190, ice 200; airless
     crystal/lava 150, asteroid 100 (all well above terrain peaks ~80-98). Void worlds 0 = disabled.
   - **Tested:** climb sets `AboveAtmosphere` + drains O₂ on a breathable world; descend clears it; hysteresis
     doesn't flicker; aboard-ship never counts; per-body heights differ — full suite **337 green**. Client +
     bundled server rebuilt.

   ### Original ask + analysis (kept for reference)

   ### Analysis (2026-06-07)
   - **The world is vertically unbounded** — `ChunkCoord` has a real Y axis, chunks stream in 3D
     (`GameServer` load loop `dy=-radius..radius`), and there is **no build ceiling**: `HandlePlace` only checks
     air/reach (`MaxReach` 8) — no max-Y. So a tall tower is fully representable; terrain tops out ~Y 80-98.
   - **Atmosphere is binary per planet, with no altitude logic anywhere.** `PlanetType.Atmosphere`
     (`breathable`/`toxic`/`none`), `SpaceSky` (always-space sky on airless bodies), `OxygenExtractability`.
     Oxygen in `TickEnvironment` regenerates (breathable + on-surface + not EVA) or drains; **nothing thins air
     with height** and the sky/starfield never fade with altitude.
   - **Groundwork already exists (dormant).** Client `GameBootstrap.OnFootInSpace` + `PlayerController` already
     implement **zero-g on foot** (Jump rises, Ctrl/C sinks, otherwise drifts to a stop) — *"nothing sets it yet,
     so it's a no-op until item 10 lands."* So the float is wired; only a trigger + sky + oxygen are missing.
   - **The transition pattern is established and reusable.** `TickEnvironment` runs a per-tick spatial check and
     flips state — `SteppedOutOfShipHull(pos)` → `StartEvaFromShip` is the exact template. Oxygen already drains
     in EVA regardless of the body. So an `AboveAtmosphere(pos)` check + a state flip mirrors existing code.

   ### Plan (recommended — confirm via questions)
   - **Mechanic (server-authoritative):** add a per-body **atmosphere height** (`AtmosphereHeight` on
     `PlanetType`, from `planets.json`; absolute Y). In `TickEnvironment`, for an on-foot player on a real planet
     (not aboard / not EVA / not ship-interior / not station), when `Position.Y` rises above it, flip a new
     `PlayerState.AboveAtmosphere = true`; drop it when descending below `height - margin` (hysteresis, like the
     hull check). Broadcast the flag in the player-state message.
   - **In-space-on-foot effects (reuse, same voxel world — you stay by your tower, can climb back down):**
     1. **Zero-g** — client sets `OnFootInSpace` from the flag → the existing `PlayerController` float kicks in.
     2. **Oxygen drains** — add `AboveAtmosphere` to the drain branch (you're above the air, even on a breathable
        world), so you need a tank/jetpack; running out suffocates via the existing death/recovery.
     3. **Space sky** — `Sky`/`Starfield` show black + stars + the sun when `OnFootInSpace` (an altitude fade
        into space), independent of the per-planet `SpaceSky`.
     4. **Feedback** — a bilingual toast on crossing up ("You have left the atmosphere — zero gravity") and down
        ("Re-entering atmosphere"); locale keys (DE+EN) per the parity rule.
   - **Per-body heights:** thick/breathable worlds sit high (a big tower); thin/toxic mid; **airless / `SpaceSky`
     bodies very low** (a short climb reaches space — fits "an asteroid barely has atmosphere"). Default sensibly.
   - **Tests:** climbing above the height sets `AboveAtmosphere` + drains O₂ even on a breathable world;
     descending clears it; hysteresis doesn't flicker; airless bodies trip at a low height.
   - **Out of scope (note):** this keeps you **on foot in the same world** (zero-g), NOT the flight/EVA combat
     view — a later "launch pad to flight view" could bridge to item 5's space instance if wanted.

   **Open questions → see chat.**
11. ✅ **Feature — trade knowledge points (done 2026-06-07).** Trade knowledge for materials/equipment; knowledge
   **never goes away** and each point can only be passed to a given player once. **Decisions (user):** unlock is a
   **threshold (knowledge not spent), material cost stays**; anti-abuse rule = **"teach up to your level"**.
   - **Knowledge is now a permanent threshold:** `HandleUnlock` no longer does `KnowledgePoints -= KnowledgeCost`
     — it only checks `>=` (materials still consumed). Knowledge is synced to the client via `InventoryUpdate`.
   - **Knowledge in a trade (giver keeps points):** `TradeSession` gains `KnowledgeA/B`; new `TradeKnowledgeIntent`
     (NetCodec tag **97**), `TradeUpdate` carries each side's offer + my total + my teach-cap. On commit the
     **giver keeps** their knowledge and the **receiver gains** it, alongside the atomic item swap — so you trade
     **knowledge ⇄ materials/equipment**.
   - **Loop-proof give-once ledger:** `PlayerState.KnowledgeGivenTo` (receiverId→points, persisted). The teachable
     amount = `min(myKnowledge − alreadyGivenToThem, myKnowledge − theirKnowledge)` ≥ 0 — you can't lift anyone
     above your own level and can't out-give what you know, so the same points can't be cycled to inflate totals.
   - **Client:** a "Knowledge →N/max" row on each side of the trade panel (− / + / Max), bilingual
     (`ui.trade.knowledge` / `ui.trade.max`).
   - **Tested:** giver keeps + receiver gains for goods; can't exceed giver's level; give-once blocks
     back-and-forth inflation; threshold unlock no longer deducts (materials still spent) — full suite **340
     green**. Client + bundled server rebuilt.

   ### Analysis (2026-06-07)
   - **⚠️ Premise mismatch with the current code.** Knowledge is stored as `PlayerState.KnowledgePoints` (int,
     persisted in `Snapshots`), earned on **first-time scans** (`GameServerScanning` — hostile 5 / creature 3 /
     block 1 / asteroid 4, ×`ScanKnowledgeMultiplier`). **But unlocking a blueprint currently SPENDS it**:
     `GameServer.cs:1783-1785` does `pool.Remove(bp.UnlockCost); KnowledgePoints -= bp.KnowledgeCost;` — i.e.
     knowledge is a *consumed cost today*, not a permanent threshold. The task's "knowledge never goes away / no
     points spent" describes a **different model** → this feature must also change unlock to **threshold-only**
     (check `KnowledgePoints >= KnowledgeCost`, don't subtract) for the premise to hold. (Material cost is
     separate — see Q.)
   - **Trade system is a solid base to extend.** `GameServerTrade` already does a proximity-gated (8 blocks)
     request→accept→offer→both-confirm→**atomic commit** handshake with `TradeSession { A,B, OfferA, OfferB,
     ConfirmA, ConfirmB }`; `MaterialPool.Remove/Add` move items with spill-back. Messages
     (`TradeRequest/Respond/Offer/Confirm/Cancel` Intents, `TradeUpdate`/`TradeClosed`) are registered in
     `NetCodec` (tags 30-34, 80-81). Client UI in `PlayerInteractions` (±buttons per item, two offer columns).
   - **No per-pair ledger exists.** Nothing tracks who gave whom what. Need a persisted, per-pair cumulative
     "knowledge already given to X" record (add `Dictionary<string,int> KnowledgeGivenTo` to `PlayerState` +
     persist in `Snapshots`).

   ### Plan (recommended — confirm via questions)
   - **Make knowledge permanent (threshold-only unlock):** drop the `KnowledgePoints -= KnowledgeCost` deduction;
     keep the `>=` gate. Knowledge becomes a non-spent stat (matches "never goes away"). *(Update the PlayerState
     comment + the unlock path + any test asserting deduction.)*
   - **Knowledge in a trade (no deduction from the giver):** extend `TradeSession` with `KnowledgeA/KnowledgeB`
     and `TradeUpdate`/a new `TradeKnowledgeIntent` (register in NetCodec!). On commit, the **giver keeps** their
     points; the **receiver gains** the offered amount — gated by the give-once rule below. Items still swap
     atomically alongside, so you can offer **knowledge ⇄ materials/equipment**.
   - **Give-once / anti-inflation ledger (loop-proof — see Q for the exact rule):** persist
     `KnowledgeGivenTo[receiver]` on the giver. Recommended rule: a gift may only **raise the receiver up to the
     giver's own current level** (`credit = min(offered, giverKnowledge − receiverKnowledge)`), and the
     cumulative `givenTo` may never exceed the giver's knowledge — so the same knowledge can't be handed back and
     forth to inflate totals (once equal, nothing flows). The giver never loses points.
   - **Client:** add a "Knowledge" line to each side of the trade panel (a number with ±, like an item row),
     show the per-pair remaining you can still teach this partner; bilingual locale keys (DE+EN).
   - **Tests:** giver keeps knowledge + receiver gains; can't raise receiver above giver; cumulative cap per pair
     (no back-and-forth inflation); threshold-only unlock no longer deducts; knowledge⇄items swap is atomic.

   **Open questions → see chat.**
12. ✅ **Feature — NPCs should have names (done 2026-06-07).** Every NPC gets a deterministic coined name,
   mirroring the flora/creature `NameGenerator`; shown as **"Name · Role"** on the nameplate (user pick).
   - `NameGenerator.Person(rng)` (short given name + longer surname, both capitalised — thousands of combos) and
     `NameGenerator.Robot(rng)` ("Vex-42" unit designation) for `IsRobot` NPCs.
   - `ServerNpc.Name` + `NetNpc.Name`, coined in `MakeNpc` from the existing settlement/station-seeded rng (so
     names are stable per NPC without persistence, like creatures). `NpcView.Label` shows "Name · Role" (falls
     back to the role label if absent). Procedural → identical DE/EN; the role label stays localized.
   - Tested: `NameGeneratorTests` (Person/Robot deterministic, two capitalised parts, highly varied, robot has a
     stem + unit number) — full suite **344 green**. Client + bundled server rebuilt.

   ### Analysis (2026-06-07)
   - **NPCs have a stable id + role but no personal name.** `ServerNpc` (`GameServerNpcs.cs`) = `Id, Role
     (vendor/quartermaster/settler), Theme (settlers/miners/traders/researchers), NameKey, IsRobot, Pos…`.
     `NameKey` is only a **role label key** (`npc.role.vendor` / `npc.theme.settlers`). Mirrored to the client as
     `NetNpc` in `NpcList` (NetCodec tag 84 — adding a field is non-breaking).
   - **Deterministic seed already available.** NPCs are made in `MakeNpc(role, theme, robotic, home, rng)`, drawn
     from a settlement/station-seeded `System.Random` (`_meta.Seed ^ StableHash("settlement:"+key)` /
     `"station-npc:"+id`) — the same per-world pattern creatures use. NPCs aren't persisted (regenerated on load),
     so a name coined from this rng is **stable per NPC** without any new persistence.
   - **`NameGenerator`** (`Spacecraft.WorldGeneration`, pure/deterministic) already has `Creature` (two-part
     "Vexilth krool") + `Flora` + a private `Word(rng,min,max)` syllable builder. Easy to add a `Person` (First +
     Surname) and a `Robot` (designation) variant.
   - **Display path:** `NpcView.Label(nd)` returns `loc.Get(nd.NameKey)` → shown as a floating nameplate via
     `ScreenLabelLayer.World(...)`. One place to change to show the name.

   ### Plan
   - Add `NameGenerator.Person(rng)` (e.g. `Word(1,2)` first + `Word(2,3)` surname, both capitalised — distinct
     from the lowercase-epithet creature style) and `NameGenerator.Robot(rng)` (a short stem + a number, e.g.
     "Vex-42") for `IsRobot` NPCs. Thousands of combinations, deterministic from the rng.
   - `ServerNpc.Name` + `NetNpc.Name`; set in `MakeNpc` (`robotic ? Robot : Person`), map in `ToNetNpc`.
   - `NpcView.Label` shows the name (format = the display question), falling back to the role label if empty.
   - Names are procedural → identical in DE/EN (no locale change); the role label stays localized.
   - Tests: `Person`/`Robot` deterministic for the same seed, varied across seeds, non-empty, two-part for Person.
13. ✅ **Feature — mission-giver NPCs that always have a mission on offer (done 2026-06-07).** The quartermaster
   is a mission-giver that never runs dry. **Decisions (user):** refill-on-take; keep **~3** available.
   - **Never dry (deterministic rolling window):** a giver offers an endless sequence of procedural missions
     (slot 0,1,2,…). `SendMissionList` shows a player the lowest **`BoardWindow` (3)** slots they haven't taken;
     accepting one slides the window so a fresh one appears — `EnsureBoardWindow`/`EnsureSettlementWindow`/
     `EnsureStationWindow`. Slots are coined deterministically from `(boardKey, slot)` (`BuildBoardMission`) so
     they **survive a reload without persistence**; `StockBoard` seeds the first window at stamp time. Old
     boards' missions are scoped out of the list (only the board you're at offers its jobs).
   - **Mission ↔ giver NPC:** `MissionDefinition.GiverName` (+ `NetMission.GiverName`); the settlement/station
     **quartermaster** NPC is named via `CoinGiverName(boardKey)` so its name matches its missions. Client shows
     **"Mission from {Name}"** in the detail (bilingual `ui.missions.giver`); mission titles are now localized.
   - Replaced the old fixed 1-2/2 pools (`GenerateSettlement/StationMissions` removed). Foundation for items 14/15.
   - **Tested:** `Board_NeverRunsDry_KeepsMissionsAvailable_AsYouKeepAccepting` (≥3 after 12 accepts),
     `BoardMissions_CarryAGiverName`; existing board accept/turn-in still green — full suite **346 green**.
     Client + bundled server rebuilt.

   ### Analysis (2026-06-07)
   - **Missions CAN run dry today.** `MissionDefinition` (id, source, nameKey/title, objectives[Collect/Mine/
     Deliver], rewards[], `Repeatable`, `Active`) + per-player `MissionProgress`. Settlements mint **1-2** missions
     at stamp time; stations **2** at board time — a **finite pool, default non-repeatable, no refill**. The
     per-player available list hides anything accepted/turned-in, so once a player takes them the board is empty.
   - **No NPC ↔ mission link.** Missions are **board-centric**: `NearSettlementMissionBoard` (a `mission_board`
     marker, reach-gated) gates accept; the **quartermaster NPC is purely cosmetic** (rendering/name only).
     `MissionDefinition` has no giver field (only `CreatorId` for player-made; `MissionPlan.GiverName` is unused
     flavour). Quartermaster NPCs stand at the board, so near-board ≈ near-quartermaster.
   - **Plumbing to reuse:** `HandleAcceptMission`/`HandleTurnInMission`/`SendMissionList` (server),
     `RequestMissions`/`AcceptMissionIntent`/`TurnInMissionIntent` + `MissionList`/`MissionResult` (NetCodec
     10-13 / 62-63), `NetMission` schema, the Missions tab in `CraftingTechShipUI` (Available/Active + accept/
     turn-in). `MissionProgress` is persisted per player; board defs regenerate per load.

   ### Plan (recommended — confirm via questions)
   - **Tie missions to a giver NPC:** add `GiverNpcId` to `MissionDefinition` (+ `GiverName` on `NetMission` for
     display). When generating settlement/station missions, assign them to that location's **quartermaster** NPC.
     Foundation for items 14 (NPC memory) + 15 (AI dialog).
   - **Never run dry (refill-on-take):** keep each giver topped up to **≥1 available** procedural mission. When a
     player accepts a giver's mission, immediately **mint a fresh replacement** for that giver (rolling unique id,
     randomised template + counts), so the giver always shows something to take. (One-shot missions; the refill —
     not repeatability — is what guarantees endless availability.) `EnsureGiverStocked(npcId, target)` called at
     generation + after accept.
   - **Client:** show the giver's name on the mission ("Auftrag von {Name}"); bilingual key. The Missions tab and
     the accept/turn-in flow stay as-is.
   - **Tests:** a giver always has ≥1 available even after accepting repeatedly (never dry); accepting mints a
     replacement; missions carry the giver id/name; turn-in still pays the reward.

   **Open questions → see chat.**
14. ✅ **Feature (plan ahead) — NPCs remember their interactions with a player (done 2026-06-07).** Per-NPC,
   per-player relationship + a log of the last 10 interactions. **Decisions (user):** memory plumbing only
   (record today's trade + mission-accept; "Dialog" wired for item 15); **internal** (no UI standing).
   - **Model (shared):** `NpcInteractionKind {Dialog,Trade,MissionAccepted}`, `NpcInteraction`,
     `NpcRelationship {Name,Role,Value,Log}`; `PlayerState.NpcMemory : Dictionary<NpcKey, NpcRelationship>`
     persisted via `Snapshots` (deep-cloned). **NpcKey = `{locationKey}:{role}`** (e.g. `settle_<hash>:vendor`,
     `station_<hash>:quartermaster`) — stable across reloads since the runtime `ServerNpc.Id` isn't.
   - **Recording** (`GameServerNpcMemory`): `RecordNpcInteraction` raises the relationship by a per-kind weight
     (Dialog +1 / Trade +2 / MissionAccepted +3, clamped) and appends to the **capped last-10** log. Hooked into
     `HandleAcceptMission` (quartermaster) and the market-barter path in `HandleCraft` (vendor; aboard-ship
     console doesn't count). `NpcRelationshipFor(playerId, npcKey)` exposes it for item 15.
   - **Tested:** `Quartermaster_RemembersMissionAccepts_RelationshipRises_LogCapsAtTen`,
     `Player_RoundTripsNpcMemory` (persists) — full suite **348 green**. Bundled server rebuilt.

   ### Analysis (2026-06-07)
   - **No NPC dialog/talk system exists yet.** The only player↔NPC interactions today are **vendor barter** (a
     market trade gated by `NearSettlementVendor`/`NearSpaceStationVendor`) and **accepting a mission** at the
     quartermaster's board (item 13). "Dialog" has **no trigger yet** — that's item 15's job.
   - **NPCs aren't persisted** (regenerated each load), so memory can't live on `ServerNpc`. But their identity is
     stable by **location + role**: the vendor / quartermaster of a given settlement (`settle:<hash>`) or station
     (`station:<id>`). Coined names are deterministic (items 12/13) but not unique → key memory by a stable
     **NpcKey = `{locationKey}:{role}`**, not the runtime `ServerNpc.Id`.
   - **Persistence pattern ready:** `PlayerState` already carries persisted dictionaries (e.g. `KnowledgeGivenTo`);
     a per-player record of each NPC (= the NPC's memory of that player) persists cleanly via `Snapshots` and is
     exactly what item 15's backend needs (relationship + recent log for this player+NPC).

   ### Plan (recommended — confirm via questions)
   - **Data model (shared):** `enum NpcInteractionKind { Dialog, Trade, MissionAccepted }`; `NpcInteraction
     { Kind; }` (optionally a turn/detail); `NpcRelationship { int Value; List<NpcInteraction> Log }` capped at
     **10** (FIFO). `PlayerState.NpcMemory : Dictionary<string, NpcRelationship>` keyed by NpcKey. Persist in
     `Snapshots` (like `KnowledgeGivenTo`).
   - **Recording:** `RecordNpcInteraction(player, npcKey, kind)` — appends to the log (trim to 10) and raises the
     relationship by a per-kind weight (e.g. Dialog +1, Trade +2, MissionAccepted +3; clamped). Hook it into the
     **mission-accept** handler (quartermaster NpcKey) and the **vendor barter** path (vendor NpcKey).
   - **Lookup for item 15:** `NpcRelationshipFor(player, npcKey)` returning value + last-10 log + the NPC's
     name/role, so the dialog backend (item 15) receives name, role, relationship and logs.
   - **Tests:** interactions accumulate + raise the value; the log caps at 10 (oldest dropped); memory persists
     across a reload; distinct NPCs/players keep separate records.

   **Open questions → see chat.**
15. **Feature — AI dialog backend (`./ai-backend/`, Python) for NPC dialog text.** *(Analyse precisely + plan
   first; research online if needed. Do NOT implement yet.)* Under `./ai-backend/` there should be a **Python
   backend** exposing an **API** the game can later call to **fetch NPC dialog texts**. The backend **consumes /
   uses a language model available via an API call** — at the start a **local model running in LM Studio**.
   - **Graceful skip:** if the backend has **no language model available**, the in-game function is **simply
     skipped** (the NPC falls back to its normal/canned dialog).
   - **Request parameters** the game server sends: **name of the planet / station**, the NPC's **function/role**,
     its **name**, the NPC's **relationship status** to the player, the **logs of the last interactions** (item
     14), the **offer** the NPC makes (trader: **what kind of goods**; quest giver: **type of quest**), plus the
     current **weather** and **time of day**.
   - **Flow:** the backend wraps these in a **system prompt**, sends them to the LLM, generates a **short text**,
     and returns it to the game engine, which **displays the text on interaction** with the NPC.
   - **Non-blocking:** generating an LLM response can take a while — the game must **wait but not block** (async;
     show the text once ready, with an instant fallback/placeholder meanwhile).
   - **Toggle:** the LLM feature must be **on/off-switchable in the game settings** (i.e. when **creating a game
     world**).
   - **Separate process:** the backend should be **startable and run separately** from the game (at least at
     first).

   ### Analysis + Plan (2026-06-07) — design only, NOT implemented
   **What's already in place (the inputs exist):** items 12-14 give every NPC a stable name (`CoinGiverName` /
   item 12), a role (`vendor`/`quartermaster`/`settler`), a per-player **relationship + last-10 interaction log**
   (`PlayerState.NpcMemory`, item 14), and the **offer** (a quartermaster's current board missions / a vendor's
   market goods). `GameServerWeather` exposes weather + time-of-day; `ActiveLocationNames()` + the boarded-station
   name give the planet/station. So the **server already holds every request parameter** the task lists.
   **What's missing:** (a) **no "talk to NPC" interaction** exists yet (item 14 confirmed) — needed to trigger +
   show dialog; (b) no outward HTTP call / async path from the server; (c) no world-creation toggle for it; (d)
   the Python backend itself.

   **LM Studio facts (researched):** LM Studio runs a local **OpenAI-compatible** server at
   `http://localhost:1234/v1` — `GET /v1/models` (lists the *loaded* model) and `POST /v1/chat/completions`. The
   **graceful-skip probe** = `GET /v1/models`: connection refused or an empty list ⇒ no model ⇒ skip. `api_key`
   is ignored on localhost (send any non-empty string). (A richer `GET /api/v0/models` reports load state too.)

   **Architecture (chain, each link degrades gracefully):**
   `Game server (C#)` → `./ai-backend (Python/FastAPI)` → `LM Studio /v1/chat/completions`.
   The **C# server** owns the context (it has NpcMemory/weather/offer) and calls the Python backend; the backend
   is a thin **provider abstraction** over the LLM (LM Studio now, others later) that owns the prompt. If the
   backend is down → server skips; if LM Studio has no model → backend replies "unavailable" → server skips.

   **`./ai-backend/` (Python, `uv`, FastAPI + httpx):**
   - `GET /health` → `{ available: bool }` (probes LM Studio `/v1/models`), so the game cheaply knows whether to try.
   - `POST /npc-dialog` body `{ planet, npcName, npcRole, relationshipValue, relationshipBand, recent:[kinds],
     offer:{type, detail}, weather, timeOfDay, language }` → `{ text }` (or 503 when no model). Builds a **system
     prompt** ("You are {name}, a {role} on {planet}; relationship {band}; recent: {…}; weather/time {…};
     offering {…}; reply in {language}, ≤2 short sentences, in-character, no markdown"), calls chat/completions
     (`max_tokens` ~80, temp ~0.8), returns the trimmed line. `.env` for `LMSTUDIO_URL`/model. **Bilingual** via
     the `language` field (matches the player's locale). Runs standalone: `uv run uvicorn app:app --port 8770`.
   - Mirrors the existing `tools/ai-assets` Python/`uv` style; keep it dependency-light + offline-friendly.
   - Optional: a tiny in-memory cache keyed by (npcKey, player, coarse-context) to avoid re-generating on repeat
     talks; a short request timeout; one retry.

   **Game integration (C#):**
   - **Talk interaction (new):** client presses **E** on a nearby NPC → `TalkToNpcIntent{npcId}` (register in
     NetCodec). Server `HandleTalkToNpc`: resolve the NPC + its `NpcKey`, **record a `Dialog` interaction**
     (item 14 — finally wires the dormant `Dialog` kind), and **immediately** send a `NpcDialog{npcId, text}` with
     the **canned line** (placeholder). A small client panel shows name · role + the text.
   - **Async, non-blocking:** if the world's AI toggle is on AND a cached `/health` says available, the server
     fires the `POST /npc-dialog` on a background task (`HttpClient`, ~8-12 s timeout) off the tick loop; when the
     text returns it sends a second `NpcDialog{npcId, text, final:true}` that **replaces** the placeholder. Timeout
     / error / disabled ⇒ keep the canned line. (Never block `Tick`.)
   - **Toggle (world creation):** add an `AiDialog` flag to `ServerConfig` + the world-create UI (alongside PvP /
     space-combat etc.), persisted in world metadata. Off ⇒ never call the backend. Also a backend URL setting
     (default `http://localhost:8770`).
   - **Params source:** name/role/relationship/log from `NpcMemory` + the NPC; offer from the quartermaster's
     board window (mission type) or vendor goods; planet/station from `ActiveLocationNames()`/station; weather +
     time from `GameServerWeather`.

   **Open decisions for greenlight:** (a) does the C# server call the backend, or the client directly? (recommend
   **server** — it holds the context + the toggle); (b) backend framework (FastAPI recommended); (c) cache + cost
   controls; (d) how the backend process is launched (manual `uv run` first; later a launcher script / bundled).
   **Out of scope here:** no code — this entry is the plan; implement only when greenlit.
16. ✅ **Task 5 — crafting / tech-tree / materials overhaul + more metals & rare earths.** (Big, staged — full
   Analysis + Plan below.) **Done so far: Stage 1** (14 metals/rare-earths + alloy tier + soft-lock/dead-station
   cleanups), **Stage 3** (placeable workbench/forge = on-world crafting + decor blocks), **Stage 3b** (placeable
   storage crate), **Stage 3c** (placeable hinge + slide doors), **Stage 4** (ships & ship parts fold onto the new
   materials), **Stage 2** (knowledge-economy + ore depth-tier rebalance). **✅ All stages done** — every Task-5
   sub-stage is complete (in-engine playtest still wanted for the on-world build objects: crate, doors, workbench/
   forge, + the visor HUD).

   ### Analysis (2026-06-07)
   **Current graph** (`data/{items,blocks,recipes,blueprints,ship_modules,ships,planets}.json`):
   - **Raw (mined):** iron_ore, copper_ore, silicate, carbon, titanium_ore, crystal + `data_fragment` (from
     `data_cache` blocks). **Flora drops:** plant_fiber, berries, crystal. **Creature drops:** creature_meat,
     toxic_gland, etc. (so `creature_meat`/`toxic_gland` ARE obtainable — via fauna, not a bug).
   - **Crafted chain (shallow, ~2-3 tiers):** ore→ingot→plate (iron); copper_ore→copper_wire→cable(+silicate);
     carbon→carbon_composite; silicate→glass; titanium_ore→titanium_plate(refinery); cable+carbon_composite→
     energy_cell_1. Everything advanced (weapons/suit/modules/ships) = plate/cable/energy_cell + a blueprint.
   - **Tech tree:** ~36 blueprints, mostly shallow chains (machete→vibro_knife→plasma_sword, etc.), gated by a
     **data_fragment + plate/cable** unlock cost + a knowledge threshold (item 11). 6 stations in the enum.
   **Real inconsistencies to fix:** (1) **`Lab` + `MachineRoom` stations are dead** — in the enum + station
   mapping (`GameServer.cs`) but **no module provides them and no recipe uses them**. (2) **Duplicate market
   recipes are strictly worse than crafting** (`market_buy_medpack` 3c+2s vs hand 2c+1s; `market_buy_oxygen_tank`
   dupes the blueprint craft) → dead / trap entries. (3) **Single-planet soft-lock:** silicate/copper absent on
   lava/jungle/swamp → no `cable` → progression stalls without travel/market (travel-gating is fine, a hard wall
   isn't). (4) **`data_fragment` economy:** ~20× rarer than ore yet needed by nearly every unlock → grind.
   (5) minor: `comm_radio` blueprint vs recipe cost ordering. *(Not bugs / by-design: titanium scarcity =
   travel-gating; pre-built starter ship carries `tractor_beam`/`ship_laser_basic` un-blueprinted.)*
   **Gaps vs the task goal:** few metals (no gold/silver/aluminium/nickel/…), no **alloys/electronics** mid-tier,
   thin "build on a world" object set (mostly walls/lights/ladders/stairs), no real **staged prerequisites**
   beyond plate/cable.

   ### Plan — staged (each stage shippable + tested)
   - ✅ **Stage 1 — New metals & raw resources + textures + base processing (done 2026-06-07).** Added **14 new
     ores** (gold, silver, aluminium, tin, nickel, cobalt, lithium, uranium, platinum, lead, zinc, tungsten,
     sulfur, neodymium) — each a mineable block + material item with a **generated OpenAI texture**, distributed
     across every planet's ore tables (rare metals deeper/rarer). **Soft-lock fixed:** copper_ore + silicate now
     on every ore-bearing world, so all can reach `cable`. **Base smelting** (ore→ingot/refined) for all 14. To
     avoid dead-ends, a **mid-tier of 9 alloys/components** (steel, bronze, brass, circuit_board, power_cell,
     reactor_fuel, carbide, magnet, light_alloy) consumes every new metal and is **folded into existing recipes**
     (armor/comm_radio/scanners/drills/jetpack + radar/tractor/jump modules) + 3 buildable decor blocks
     (steel_wall, bronze/brass block). Removed the **dead `Lab`/`MachineRoom` stations** + the **worse-than-craft
     market dupes**. **Tested:** `CraftingConsistencyTests` (no broken refs, every input obtainable, no dead-ends,
     every planet reaches cable, dead stations gone) — 354 green. ~40 assets generated; client rebuilt.
   - ✅ **Stage 2 — knowledge-economy + ore depth-tier rebalance (done 2026-06-07).** *(The staged alloy/
     electronics intermediates this stage originally scoped — steel/bronze/electronics/alloy plates gating the
     advanced items — were already delivered in Stage 1; Stage 2 became the **economy + depth** rebalance the
     user picked.)* **Eased the `data_fragment` grind** (≈89 fragments unlock everything, ~1–5 per blueprint;
     `data_cache` was ~10–75× rarer than ore): the cache now **drops 2** (was 1), worldgen cache rarity **×1.5
     across all worlds** (floor 0.0012), the market fallback is cheaper (**3 titanium_ore + 1 crystal → 1**, was
     4+2), and the two steepest unlocks (laser_cannon_2, plasma_blaster) drop **5→4** fragments — on top of the
     existing cache/market/mission/combat/structure-loot sources. **Light depth-tiering:** valuable/rare ores now
     sit deeper (gold 16→20, silver/cobalt 12→16, lithium 8→12, tungsten/neodymium 20→26, uranium/platinum 24→30)
     while construction ores stay shallow — rewards deep mining without a soft-lock (every vein still reachable in
     its crust). **Tested:** new `EconomyBalanceTests` (eased cache yield, every vein reachable, valuable-deeper-
     than-construction) — 361 green; data synced to the bundled server.
   - ✅ **Stage 3 — Buildable world objects (done 2026-06-07).** New placeable blocks for base-building on
     worlds: a **workbench** (enables **workshop** crafting when you stand near it, no ship needed) and a
     **forge** (enables **refinery** crafting) — `StationAvailable` now also accepts a placed station block via
     `NearStationBlock` (3-block reach) — plus decorative building blocks (**steel_floor**, **metal_panel**,
     **concrete**). Each = block + item + generated texture + recipe + bilingual name. **Tested:** `WorkbenchTests`
     (workbench→workshop, forge→refinery on a world without the ship) — 356 green. Client + bundled server rebuilt.
   - ✅ **Stage 3b — Storage crate (done 2026-06-07).** A placeable **crate** is a persistent container for base
     storage: placing it registers a container (reusing `StoredContainer`/`GameServerContainers`), **H stashes**
     all your loose materials/components into the nearest crate (tools/weapons stay), **G takes** them back out
     (existing loot path), and **mining** the crate returns its contents + the crate block. New
     `DepositContainerIntent` (NetCodec **98**), `DepositToContainer`, place/mine hooks, a `PlaceBlock` test
     entrypoint, and a crate-specific HUD prompt ("Stash · G take · H store"). Tested: `CrateStorageTests`
     (stash-not-tools + take-back, mining returns contents) — **358 green**. *(Placeable door still a follow-up.)*
   - ✅ **Stage 3c — Placeable doors (done 2026-06-07; needs in-engine test).** Two new placeable blocks —
     **`door_hinge`** (manual: press **E** to swing it open/shut) and **`door_slide`** (sci-fi auto door: opens
     as you approach, auto-closes) — both crafted at a workbench (3 `metal_panel` + 1/2 `circuit_board`). They
     **reuse the existing door entity system end-to-end, so the client needed no changes**: placing one calls a
     new server **`PlaceDoor`** that fills the (air) cell with a width-1 `ServerDoor` (wall axis from the jambs or
     the player's facing), **persists** it by cell (new `door` table + `SaveDoor`/`ListDoors`/`DeleteDoor`), and
     broadcasts the `DoorList` the client already renders/animates/collides. **`LoadPlayerDoors`** re-appends the
     persisted player doors after every deterministic rebuild (`RegisterDoors`/`RegisterStationDoors`) + on world
     load, so settlement/ship stamps never wipe them. **Mining** is intercepted (a door fills an air cell):
     `RemovePlayerDoorAt` removes the door, returns the item, and persist-deletes — to remove an open door, close
     it (E for hinge; step back so the slide auto-closes — `Reach 6` > slide range `4.5`). **Tested:**
     `PlaceableDoorTests` (place→register+persist+consume, E toggles, mine returns the item + forgets it, and a
     **reload** re-loads the door) — **363 green**; client + bundled server rebuilt. *(The collider-to-mine feel +
     the animation want a playtest.)*

     <details><summary>Original analysis + plan (kept)</summary>

     **As-is (mapped):** the game already has a full **door system** — doors are **server-authoritative entities**
     (`ServerDoor` in `GameServerDoors.cs`), *not* voxel blocks: a doorway stays **air**, and a door entity fills
     the gap. The client `DoorView.cs` already renders them as GameObjects, **animates** them (slide panels
     retract / hinge leaf swings), toggles a **BoxCollider** by open-state, plays SFX, and creates/destroys them
     live from the server's `DoorList` — so **the client needs no changes**. Two kinds exist: **slide** (auto —
     opens when a player is within range, auto-closes) and **hinge** (manual — press **E** at it; already wired in
     `PlayerController` via `NearestHinge` + `SendDoorInteract`, NetCodec tag 46 / DoorList 93). Today doors are
     built only from generated **markers** (settlements/ship/stations) via `RegisterDoors()`/`MakeDoor()` (which
     probes the surrounding blocks for wall axis + gap width). **Two gotchas for player doors:** (1) `RegisterDoors()`
     **clears + rebuilds** `_doors` from markers on every settlement/ship stamp + world load → player-built doors
     must be **persisted** and re-appended afterwards (the generated ones are deterministic, player ones are not);
     (2) doors fill an **air** cell (no solid block), so removal can't rely on a normal block-break.
     **Plan (server-only, reuses DoorView):** add a **hinge** door block+item (`door` → category block, `placesBlock`,
     texture, recipe, bilingual names); in `HandlePlace`, after the cell is placed, call a new **`PlaceDoor(pos)`**
     that (a) keeps the cell **air** (not solid), (b) registers a **width-1 `ServerDoor`** centred on the cell
     (axis from the player's facing or probed jambs), (c) **persists** it (mirror the container repo:
     `SaveDoor`/`ListDoors`/`DeleteDoor`), (d) `BroadcastDoors()`. Re-append persisted player doors at the end of
     `RegisterDoors`/`RegisterStationDoors` + on load (so stamps don't wipe them). **Removal:** mining the door's
     cell (its closed collider is raycast-hittable) finds the player-door there → remove + persist-delete + drop
     the `door` item; if it's open, press **E** to close first. Server-unit-testable (place → `DoorCount`/persisted
     → interact toggles → mine removes + drops); the **feel** (collider-to-mine mapping, animation) needs a
     **playtest**. *(Door **kind** + recipe to confirm with the user before building.)*
     </details>
   - ✅ **Stage 4 — Ships & ship parts on the new materials (done 2026-06-07).** Folded the new alloys/electronics
     into ship + module builds: **hauler** craftCost += steel + circuit_board (heavy hull + avionics), **scout**
     += light_alloy + circuit_board (light + sensors); module builds **hull_plating** += steel, **shield_generator**
     + **jump_generator** += circuit_board (on top of the Stage-1 magnet/reactor_fuel folds for radar/tractor/jump).
     So high-tier ships/modules now require the deeper material tier. Tested (updated the hull-plating + scout-craft
     tests to supply the new mats) — 358 green; data synced to the bundled server. *(Intermediate ship tier
     deferred; a placeable door is the remaining Stage-3 follow-up.)*
   - Tests at each stage: every recipe input/unlock cost is obtainable; no dead-end outputs; every planet has a
     path to `cable`; new ores mine + smelt; content-load + locale-parity stay green.

   **Open scope questions → see chat.**
17. ✅ **Task 6 — drastically more flora & fauna variety** (first big batch done 2026-06-07; needs in-engine test).
   Added in one batch (all generated): **+14 flora archetypes** — palm, moss, orchid, succulent, pitcher, puffball,
   lichen, coral, seagrass, sporepod, thornbush, bellflower, ashweed, glowvine (each = `flora_*` block + drops +
   `FloraCatalog` hosts across biomes + OpenAI texture + DE/EN name; glowvine/sporepod also glow via
   `ChunkMesher.GlowFor`); **+10 creature hide textures** — mossy, crystalline, metallic, banded, shaggy, spined,
   mottled, iridescent, barkskin, veined (OpenAI grayscale tiles wired into `CreatureBuilder`'s hide-selection
   pools, incl. diversified glow/winged/water/hostile sets); **a new procedural body feature** — a **dorsal crest**
   (`HasCrest` on `CreatureSpecies`/`NetCreature`, rolled ~⅓, rendered as spine plates) plus **wider generator
   rolls** (body segments 1→4, eyes incl. 1/8, horns incl. 4); **+10 creature calls** — purr, moan, squeak, drone,
   gurgle, yelp, snarl, whistle, cluck, wail (ElevenLabs clips added to `CreatureView`'s call list). 363 green;
   textures bundled + sounds installed; client + bundled server rebuilt. *(Visual/audio variety wants a playtest;
   the pipeline supports further batches — see analysis below.)*

   ### Analysis (2026-06-07) — as-is + plan
   **As-is (mapped):** Three deterministic-from-seed systems, all already fairly rich:
   - **Flora = fixed archetypes.** 15 `flora_*` blocks (`data/blocks.json`) each with a host-surface list in
     `FloraCatalog` + a 64² texture; `FloraGenerator` per world just coins a name + rolls toxic/active per
     archetype (no per-world data). So **more flora variety = add archetypes** (block + `FloraCatalog` entry +
     texture + bilingual name). `FloraTests` enforces every host surface keeps ≥1 active species (adding more is safe).
   - **Fauna = fully procedural.** `CreatureGenerator` builds species from fields (habitat/temperament/size/legs/
     wings/horns/segments/eyes/colour/glow + a **hide texture** from **12** grayscale tiles); `CreatureBuilder`
     renders the blocky body + tints it. So **more fauna variety = more hide textures + richer generator rolls**.
   - **Sound already has real recordings** (~112 clips in `client/Assets/Resources/audio/`): **12 signature creature
     calls** (chirp/croak/growl/…) + **6 size×disposition voice banks × 5 states** + 6 biome ambiences; a procedural
     synth is only the fallback. So **more fauna audio = add new call types** (1 ElevenLabs clip each, listed in
     `CreatureView`) — the most-heard creature sound.
   **Pipeline:** OpenAI textures via `gen_textures.py` (blocks/flora) + `gen_creatures.py` (grayscale hides) →
   `bundle_textures.py` → `client/Assets/Resources/textures/*.bytes`; ElevenLabs via `gen_sound.py` →
   `client/Assets/Resources/audio/*.mp3` (auto-loaded by key). Guards: `FloraTests`, `CreatureTests`,
   locale-parity, content-load.
   **Plan:** (1) **+ new flora archetypes** — new `flora_*` blocks across biomes (palm/moss/succulent/pitcher/
   puffball/lichen/coral/sporepod…), each block + `FloraCatalog` hosts + OpenAI texture + DE/EN name; (2) **+ new
   creature hide textures** — new grayscale tiles + add them to `CreatureBuilder`'s hide-selection pools so the
   procedural creatures actually use them; (3) **+ new creature calls** — new ElevenLabs call clips added to
   `CreatureView`'s call list (and optionally a wider colour/body roll in `CreatureGenerator`). Test + build + commit.
   **Scope/counts to confirm with the user before generating.**
18. **Analysis only — make a world more spherical (vertical wrap too).** *(Analysis only — do NOT implement.)*
   Analyse and estimate: how could a world be circumnavigated **not only horizontally but also vertically** — more
   like a sphere. Assess what's **realistically possible**, weighing **complexity and performance cost**.

   ### Analysis + estimate (2026-06-07) — no implementation
   **Today the world is a CYLINDER (proven by Task 2/2b):** X is a **wrapping longitude** (`WorldConstants.
   Circumference`, per-body, seam-free via circular-domain noise `Noise.FbmCylX`/`ValueCylX`), the server wraps
   the player's X and the client renders the nearest wrapped copy (`SceneX`); **Z (latitude) is NOT wrapped** —
   it's bounded by an invisible **pole barrier** (`WorldConstants.LatitudeLimit`); **Y is unbounded**. Chunks are
   3D, X-canonicalised in the chunk key. The **orbit/star-map view already draws bodies as spheres** (`BodySize-
   Scale`/`OrbitDiameterFor`) — so the *space view is fine regardless*; only the **walkable surface topology** is
   the question. ~30 sites depend on the wrap helpers (`WrapX`/`WrapDeltaX`/`Canonical*`/`WrapDistanceSquared`).

   **Three realistic options (cheap → expensive):**
   - **(A) Torus — wrap Z like X (cheapest, ~days).** Mirror the entire X-wrap machinery on Z: a `CircumferenceZ`,
     circular noise in Z, Z-canonical chunk keys, `WrapDeltaZ`/`WrapDistance` over both axes, client `SceneZ`,
     drop the pole barrier. You can then circle **both** ways seam-free. **Cost:** moderate, well-understood
     (it's the X work again). **Perf:** identical to today. **Catch:** topologically a **donut, not a sphere** —
     no poles; "south" loops straight back to "north". Functionally satisfies "circle vertically too", but it
     isn't really spherical.
   - **(B) Pole-pinch cylinder (moderate, ~1-2 weeks).** Keep the cylinder grid, but at the latitude limit the
     player **crosses the pole** instead of hitting a wall: a coordinate transform flips them to the antipode
     (X += half-circumference, Z direction reversed). Gives genuine **sphere-like traversal** (over the poles and
     back) on the existing grid with **no grid rewrite**. **Perf:** identical (a transform at the seam). **Catch:**
     the grid doesn't converge at the poles (a pole is a *line*, not a *point*), so there's a visible **pole-seam
     line** + mild distortion near it; needs careful handling of movement/rendering/structures crossing the seam.
   - **(C) True sphere — cubed-sphere voxel world (expensive, multi-month rewrite).** Map the surface to a
     **cubed-sphere** (6 face-grids with edge seams) so longitude truly converges at poles. **Touches everything:**
     chunk addressing, meshing, physics/movement, noise domain, persistence keys, structure stamping, the wrap
     helpers — a near-total worldgen/engine rewrite. **Perf:** workable but heavier (face-seam handling, non-
     uniform cells). **Verdict: not worth it** for this game's scope.

   **Recommendation:** if "circumnavigate vertically too" is the goal, **(A) torus** is the pragmatic win
   (cheap, zero perf cost, reuses the X machinery) — accept that it's a donut. **(B) pole-pinch** is the choice
   if it must *feel* like a sphere (poles exist) and a ~1-2 week effort is acceptable. **(C) true sphere is out of
   scope.** Either way the orbit/star-system view needs no change. *(Estimate only — confirm direction before any
   build.)*
19. ✅ **Feature — bigger HUDs + weaker visor + menu matches HUD look (done 2026-06-07; ✅ confirmed good in-game).**
   All four parts shipped (client-only; ~1.25× HUD, visor ~0.6, translucent menu, all chosen with the user):
   **(a) Bigger HUDs** — `UiKit.CreateCanvas`/`CreateDiegeticCanvas` take an optional reference resolution; the
   **HUD canvases** (person `HudUi` + `SpaceView` flight overlay) now use **1536×864** (`UiKit.HudRefW/HudRefH`)
   so ScaleWithScreenSize draws them **~1.25× larger**, while the **menu stays at 1920×1080** (its absolute layout
   intact). Expand match mode keeps it fitting 16:9/ultrawide/4K. **(b) Weaker visor** — base `_Intensity` 1→**0.6**
   (preset 0.5→0.4) plus softer `_Curvature` 0.11→0.07, `_Chroma` 0.022→0.011, `_Reflect` 0.14→0.10, `_RimIntensity`
   0.2→0.13 (glow/opacity kept for readability). **(c) Menu look** — the menu backdrop alpha **0.96→0.6** so it
   reads as a translucent holographic overlay over the world/HUD (panels already use the HUD cyan palette); kept
   flat (not routed through the visor — that would misalign clicks). Client rebuilt. *(Exact magnitudes want a
   playtest — all are single constants, easy to nudge.)*
   - ✅ **19d — Menu "visor glass" helmet look (done 2026-06-07; ✅ confirmed good in-game).**
     The menu was flat (no helmet) because routing it through the visor would **displace clicks** (the shader
     bends the image). Chosen with the user: give the menu the **helmet look without the curvature** — a new
     additive **`Spacecraft/VisorGlass`** shader (cyan fresnel rim glow + faint animated scanlines + top glint,
     no barrel warp; registered always-included) drawn as a **click-through** (`raycastTarget=false`) top-most
     full-screen overlay via **`VisorMenuGlass.Add`**, only when the visor pipeline is active. So buttons stay
     exactly where they're drawn. Client rebuilt; needs a playtest (rim-glow strength is the `_RimIntensity`/
     `_Intensity` knobs).
   ### Analysis (2026-06-07)
   - **HUD sizing.** All UI uses `UiKit` `CanvasScaler` ScaleWithScreenSize, **ref 1920×1080**, match=Expand.
     `HudUi` (person HUD, **diegetic** → visor RT) positions right/bottom elements with **absolute** `W/H=1920/1080`
     consts; `SpaceView` flight HUD uses **screen-edge anchors** (auto-scales); the **menu** (`CraftingTechShipUI`)
     uses **absolute 1920 coords** (full-screen). So a *global* ref drop would enlarge the HUDs **but push the
     menu's absolute layout off-screen**. → Give **HUD canvases a lower reference** (e.g. 1536×864 ≈ 1.25×, bigger)
     and keep the **menu at 1920** (already full-screen). Add an optional ref param to `UiKit.CreateCanvas`/
     `CreateDiegeticCanvas`; set `HudUi.W/H` + the SpaceView overlay to the HUD ref. Expand keeps it fitting on
     16:9/ultrawide/4K.
   - **Visor strength.** `VisorHud.OnRenderImage` sets `_Intensity=1` + `_Curvature 0.11`, `_Chroma 0.022`,
     `_RimIntensity 0.2` (scanlines/rim/reflection scale by `_Intensity`). → lower base intensity + chroma +
     curvature + rim for a subtler bow/fringe, keep glow/opacity for readability.
   - **Menu look.** Menu panels already use the HUD cyan palette (`UiKit.Panel`/`Cyan`), but the menu has a
     near-opaque backdrop (`0.96` alpha) and is a flat overlay. Routing it through the **visor would misalign
     clicks** (the shader warps/curves) — so keep it flat but **drop the backdrop alpha** so it reads as a
     translucent holographic overlay like the HUD. (Magnitudes confirmed with the user before building.)
   - **Loading screens (item 21, bundled in this pass).** `WorldLoadingOverlay` **skips the veil when the world is
     already ready** (fast/cached load) and `MinShow=0.7`. → remove the skip + set `MinShow=3.0` so it **always**
     shows ~3s on planet-landing **and** station-board (shared overlay).
20. ✅ **Feature — build in space + erect a player-built space station (done 2026-06-09; needs in-engine test).**
   **All five staged sub-features S1–S5 are implemented** (see the staged plan + per-stage ✅ notes below): voxel
   ship in flight (S1), free-EVA collision + build/mine (S2), hybrid shoot+mine voxel ore asteroids (S3),
   player-built boardable + persisted stations (S4), protection + reach + far-unload polish (S5). Follow-up bug
   fixes after the first playtest: landing E-confirm, flight ship scale vs 1:1 on EVA, ship silhouette, EVA-held
   tool, loading-veil-from-start, and the seam-spawn freeze (`PlayerPosition` stale during settling). *(Original
   analysis + staged plan kept below.)* Players should be able to
   **build in space** by placing **individual blocks**, and once the structure is **closed and/or has an airlock**
   it should **become a space station**. Such stations must be **persisted in the star system** and **accessible**
   to the player (boardable later). **Required analysis before tackling it:** the **as-is** behaviour — how the
   **station interior vs. exterior** work today (interior = a void-world clone of `orbital_station` stamped from a
   template / `GameServerSpaceStations`, `BoardableStation`, markers; exterior = a body/backdrop in the **space
   flight view** + an entry on the **star map**), and **whether/how this can fundamentally work** or **what would
   need to change** in the **space + star-system view**. Key open problem to call out: **space today is a
   non-voxel `SpaceInstance`** (asteroids/entities + a parked ship), **not a placeable voxel world** — so
   "placing blocks in space" needs either a buildable voxel volume in the flight/EVA view or a different model
   (e.g. build the station in a void world, then register + persist it as a new boardable body in the system).

   **[ANALYSIS — OPTION 1 (real voxel volume in the flight/EVA view), 2026-06-08]**
   - **As-is facts.** Flight view = a Unity scene at `SceneOrigin (0,6000,0)`; EVERYTHING is a float-positioned
     hand-built CUBE MODEL, not voxel: the **ship** (`SpaceView.BuildShip`, ~3.6×0.9×4.4 u, from a silhouette, NOT
     the player's design), **asteroids** (textured cube ~2.4 u, `CombatEntity` hull tiers), **stations/drones/UFOs**
     (multi-cube models). Bodies = solid **spheres** (`CreatePrimitive`, dia ~13 asteroid / ~23 moon / ~46–62
     planet) via `OrbitDiameterFor = 8 + circ/220`. Scale = **system coords × 0.16**; flyable `Bounds = 130` u;
     ship cruise 14 u/s. Collision everywhere is **sphere/keep-out only** (`KeepOutMargin 10`, `StationKeepOut 6`,
     `ShipKeepOut 3.5`, `ShipCollisionRadius 3`); **no block-level collision in space**. EVA = float 6-DOF, sphere
     bounce. Server `SpaceInstance` = float `ShipPosition` + `List<CombatEntity>` — no voxel.
   - **Voxel pipeline is planet-agnostic + cheap (reusable).** `ChunkMesher.Build(chunk, content, worldBlock
     callback, atlas)` needs only a 16³ block grid + a neighbour lookup — **no circumference/wrap/planet coupling**.
     `ServerWorld` is a `Dict<ChunkCoord,ChunkData>` keyed by a per-body `LocationId`, persists **only edits**, can
     **skip generation** + use an arbitrary circumference (no wrap if `WrapX` isn't called). Mesh cost: **ship 1
     chunk <1 ms; station 8–15 chunks ~5–15 ms; asteroid 1 chunk ~1 ms**; a block edit re-meshes the chunk + ≤6
     neighbours, batched per frame (~7 ms worst). So each small structure = its own tiny `ServerWorld`.
   - **THE核心 tension = SCALE.** A planet at 1:1 voxel would be 5000–12000 **units** — impossible in a 130-u zone,
     so planets/moons MUST stay distant scaled spheres (✔ matches the ask). But the flight view is **compressed**
     (planet shown at ~35 u), while 1:1 voxel structures are full size (ship ~9 u, **station 30–50 u**) — so a
     player station could render **bigger than a planet**. Two resolutions (THE design fork to decide):
     **(A) Backdrop planets** — bodies become far, non-to-scale backdrops you *transition* to (not fly into at
     1:1); the near-field is a 1:1 voxel bubble. Clean scale, but changes the "fly up to a body, press E to land"
     UX. **(B) Stylized scale** — keep flying up to bodies, accept that structures look large vs planets (a stylised
     space view). Simplest, keeps current UX, looks gamey. *(Recommend prototyping both; lean B first, it preserves
     today's flow.)*
   - **Sizing.** NOT one giant grid — each structure is a small `ServerWorld`: ship = 1 chunk (~250 blocks),
     asteroid = 1 chunk, **station capped ~48³–64³ (a few chunks)** to bound mesh/persistence/scale. The "voxel
     space" is the union of these, each positioned in the flight scene.
   - **Performance.** Meshing is per-chunk + cheap; the real costs are (i) **EVA/ship collision vs voxel meshes**
     (today sphere-only — the biggest physics change), (ii) **many structures in MP** (dozens of small meshes —
     fine with far-structure unload), (iii) networking block edits per structure. None are blockers.
   - **Implications on the rest of the game.** (1) **Ship rendering overhaul** — the flight ship becomes a voxel
     mesh of the player's **design** (reuse the ship-editor voxel data) instead of `BuildShip`. (2) **Mineable
     asteroids** become voxel ore grids; mining = `BlockChanged` + re-mesh (replaces hull-tier entities + the
     split/loot model). (3) **Place/mine in space** = reuse `HandlePlace`/`HandleMine` against each structure's
     `ServerWorld`, with `IsShipBlock`-style protection for other players' ships + game-spawned stations
     (unmineable). (4) **Collision** — cleanest path is a **"dock/attach to structure" 1:1 mode** that drops the
     player into that structure's world with the EXISTING on-foot `CharacterController` voxel collision (sidesteps
     inventing free-space mesh physics for building). (5) **Persistence + registry** — a player station persists as
     block edits (its `LocationId`) + a system-registry row (position, owner, boardable) so it reappears + shows on
     the star map. (6) Reuses the chunk-stream + `BlockChanged` paths (add a structure id).
   - **De-risk order (prototype).** ① Co-render ONE voxel structure (the player's ship from its design) in the
     flight scene at 1:1 — prove meshing + positioning + scale. ② EVA/dock + place/mine vs that one structure —
     prove the interaction (via the dock-to-1:1 mode). ③ Decide scale fork A vs B against the real view. ④ Multi-
     structure + voxel asteroids + persistence + registry + protection. Each stage is shippable on its own.
   - **Open questions to put to the user before building:** (a) scale fork **A (backdrop) vs B (stylised)**?
     (b) do we voxelise the player's flight ship now, or keep the model first + only voxelise stations/asteroids
     (phased, lower risk)? (c) building interaction: **free-flight EVA placement** (needs free-space voxel
     collision) vs the simpler **"dock → 1:1 on-foot build" mode** (reuses existing collision)? (d) max station
     size + is it boardable/enterable like today's stations? (e) does a player station need **life support /
     gravity / power** rules, or is it purely structural for now?

   **[DECISIONS 2026-06-08 — building the ambitious version]** (a) **Stylised scale** — keep flying up to bodies +
   E-to-land as today; voxel structures may look large vs planets (accepted). (b) **Voxelise the flight ship now**
   — the player's ship renders as a voxel mesh of its editor design (replacing `BuildShip`). (c) **Free EVA-flight
   building** — place/mine while floating in the suit, which needs NEW free-space voxel collision + zero-g block
   aiming. (d) **Boardable, capped stations** (~48³–64³), persisted + on the star map; no life-support/power rules
   yet (purely structural for now).
   **STAGED PLAN (each stage shippable + committed on its own):**
   - ✅ **S1 — Voxel structures in the flight scene (foundation) (done 2026-06-08; needs in-engine test).**
     Server: new `SpaceStructure` (`GameServerSpaceStructure.cs`) = a sparse voxel grid (`Dictionary<Vector3i,
     BlockId>`, no generation, no wrap) + a flight-scene position + bounding-box dims, carried in
     `SpaceInstance.Structures` keyed by player. `BuildShipStructure` seeds it from the player's active ship DESIGN
     (`GetShipLayout`, mirroring `StampShipLayout`'s cell→block mapping; hatch/door cells become holes), with a
     hollow-hull-box fallback for ships without a designed layout. `EnterSpace` builds + stores the structure and
     sends it via a new `SpaceShipDesign` message (NetCodec tag 105; sparse parallel arrays X/Y/Z/Block + dims).
     Client: `NetworkClient`/`GameBootstrap` decode + store it on `Game.ShipDesign`; `SpaceView.BuildVoxelShip`
     meshes it with the same `ChunkMesher` + block atlas the planet world uses, centred on the ship pivot (front =
     +Z), 1:1 (stylised) — REPLACING the cube `BuildShip` for the player's own ship (cube model kept as the
     fallback). The design arrives just after entry, so the view rebuilds the ship mesh once it lands
     (`RebuildShipModel`, pose preserved). Tail hatch marker + exhaust FX kept; the atlas hull isn't runtime-tinted
     so the hull-colour pick (item 32) doesn't apply to the voxel ship (`_hullMat` stays null, its uses are
     null-guarded). No voxel collider yet (flight is float-positioned — collision is S2). Test
     `ShipStructure_IsSeededFromTheShipDesign` (393 green). *Known to eyeball in-engine: the stylised scale next to
     the sphere planets, the +Z facing, and the block atlas shader's look in the space scene.* *Proves meshing +
     position + scale.*
   - ✅ **S2 — Free EVA collision + build/mine vs a structure (done 2026-06-08; needs in-engine test).** On an EVA,
     the suit now collides with the ship's voxel hull and can mine/place blocks on it. Client
     ([SpaceView.cs](client/Assets/Spacecraft/Scripts/SpaceView.cs)): `BuildVoxelShip` keeps the ship's block grid
     (`_shipCells`) + adds a `MeshCollider` per voxel chunk (refactored into `RebuildShipVoxels`, reused after every
     edit). `UpdateEva` resolves movement in **design space** per-axis (`ResolveEvaVoxelMove`/`SuitBlocked`) so the
     suit slides along the hull instead of passing through (replaces the old `ShipKeepOut` sphere bounce; sphere
     bounce kept as the cube-fallback path). `AimShipVoxel` ray-marches the grid (a design-space DDA mirroring the
     on-foot `AimBlock`); a small marker shows the targeted cell; **LMB mines, RMB places** the held hotbar block.
     Net: new `StructureEditIntent` (client→server, tag 106) + `StructureBlockChanged` (server→client, tag 107).
     Server ([GameServerSpaceStructure.cs](src/Spacecraft.GameServer/GameServerSpaceStructure.cs)) `HandleStructureEdit`
     — the free-space analogue of `HandlePlace`/`HandleMine`: edits the structure's sparse grid, banks mined drops /
     consumes the placed item (`MaterialPool`), and broadcasts to the instance; gated to EVA + **your own ship**
     (other ships/stations protected in S5). Edits live in the instance's structure (survive re-entry while you stay
     in space). Coordinate transforms go root-local↔design via the ship transform, so it's correct at any heading.
     Tests `EvaStructureEdit_MinesAndPlacesOnTheShipVoxelGrid` + `StructureEdit_RejectedWhenNotOnEva` (395 green).
     **Deviations from the plan / known for in-engine eyeballing:** aim + collision are the analytic voxel grid (not
     Unity `Physics` raycast) — the MeshCollider is added but used for future on-foot/raycast, not the suit; **no
     server-side reach check** yet (trusts the client's capped ray-march — S5 hardening); edits are **session-only**
     (durable per-structure save is S4). *Proves the core interaction.*
   - ✅ **S3 — Voxel ore asteroids, hybrid shoot + mine (done 2026-06-09; needs in-engine test).** **Decision (user
     2026-06-09): hybrid** — voxel asteroids you can BOTH shoot from the ship AND EVA-mine block-by-block. Each
     asteroid is now a **paired CombatEntity + `SpaceStructure`** (same id): the entity keeps the existing
     targeting/range/respawn/loot machinery; the structure is a small voxel ore body (rough sphere of
     iron/copper/titanium ore + stone, `MakeAsteroidStructure`/`SpawnAsteroid` in
     [GameServerSpaceStructure.cs](src/Spacecraft.GameServer/GameServerSpaceStructure.cs)). **Shoot:** `FireWeapon`
     carves the rock to match its remaining hull (`CarveAsteroidToHull` removes outermost blocks + broadcasts
     `StructureBlockChanged`); on destroy the structure is dropped (`RemoveAsteroidStructure`) and the existing
     tier-0 loot path grants ore. **Mine:** `HandleStructureEdit` now allows mining any `Kind=="asteroid"` structure
     (grants the block's ore drops); a fully mined-out asteroid removes its entity + structure. `RespawnAsteroids`
     refills with voxel asteroids (sends the new design + state). **Networking:** `SpaceShipDesign` gained `Kind` +
     world `Pos`; `EnterSpace`/respawn send each asteroid structure. **Client**
     ([SpaceView.cs](client/Assets/Spacecraft/Scripts/SpaceView.cs)): generalized the S1/S2 voxel pipeline to
     **multiple structures** — a `_structs` dict of static voxel bodies at world positions (`ReconcileStructs`,
     built from `OnStructureDesign`, removed on `SpaceEntityDestroyed`), the mesher extracted to `BuildVoxChunks`,
     and EVA aim/collision generalized across the ship + all asteroids (`AimVoxel`/`SuitBlockedWorld`, world-axis
     sliding). `SyncEntities` no longer draws asteroid cubes (the entity stays only as a fire target). GameBootstrap
     routes `Kind=="ship"` → `Game.ShipDesign`, asteroids → SpaceView. Tests `Shoot_CarvesVoxelAsteroid_ThenDestroysIt`
     + `EvaMine_TakesOreFromAVoxelAsteroid` (396 green). **Deviations / in-engine eyeballing:** collision/aim are the
     analytic voxel grid (not Unity physics); shoot-loot is bulk-on-destroy while EVA-mine is per-block (two
     playstyles); shoot carving removes outermost blocks (not the exact impact point); edits are session-only (durable
     save = S4). The old split-into-tiers model is replaced by carve-to-deplete.
   - ✅ **S4 — Player-built stations (done 2026-06-09; needs in-engine test).** **Decisions (user 2026-06-09):**
     (a) start via a **deployable station-core block**, (b) boardable once it **has an airlock + min size**, (c)
     board by **stamping the build into a void-world** (reuse the station boarding infra). **Data:** new
     `station_core` block + item + workshop recipe + DE/EN names. **Deploy:** new `DeployStationCoreIntent` (tag 108)
     — on EVA, **B** spawns an owned `SpaceStructure{Kind="station"}` with a core block a few units ahead
     ([GameServerPlayerStations.cs](src/Spacecraft.GameServer/GameServerPlayerStations.cs)); build it out with the
     S2 EVA place flow (`HandleStructureEdit` now allows place/mine on your own station). **Commission:**
     `TryCommissionStation` fires when the build reaches ≥12 blocks **and** contains a door (airlock) — registers a
     `BoardableStation`, adds a `SpaceStation` dock contact (so EVA "press E to board" works), adds a runtime
     `CelestialBody` to the star map, and persists it. **Board:** `BoardStation` branches to `StampPlayerStation`
     which stamps the player's own cells into the `orbital_station` void world (spawn at the build's centre on a
     guaranteed floor pad); leaving reuses `LeaveStation` unchanged. **Persistence:** new `space_structure` SQLite
     table + `StoredSpaceStructure` (id, owner, name, location, pos, boardable, serialized cells); saved on commission
     + on later edits, loaded at `Start` (`LoadPlayerStations` → re-adds star-map bodies + registry) and re-created
     into the matching space instance on entry (`AddPersistedStations`). **Tests:**
     `PlayerStation_Deploys_BuildsOut_AndCommissions` + `PlayerStation_Persists_AcrossServerRestart` (398 green).
     **Deviations / in-engine eyeballing:** airlock detection = "contains a door block" (not a flood-fill sealed
     volume); the boarded interior is the raw build (no auto-generated rooms/markers/crew beyond a spawn pad +
     incidental civilians); station-core appearance uses the palette colour (no bespoke atlas texture yet); one
     station core per deploy, no explicit decommission/claim-protection yet (S5).
   - ✅ **S5 — Protection + MP polish (done 2026-06-09; needs in-engine test).** Most protection already fell out of
     the S2–S4 owner-gating (`HandleStructureEdit` only lets you edit your OWN ship/station; asteroids are public;
     game-spawned void-world stations aren't `SpaceStructure`s so can't be edited; ship weapons can't target
     stations). S5 adds the remaining hardening: **server-side reach** — a static structure (asteroid/station) can
     only be mined/built from within `StructureEditRange` (40 u) of the suit, closing the S2–S4 "trusts the client"
     gap (the own ship rides the pilot, so it's exempt). **Far-structure unload** (client) — `ReconcileStructs` now
     drops a voxel body's mesh beyond ~95 u (keeping its data) and rebuilds it when you return, so dozens of bodies
     don't all carry live meshes. **Multiple structures co-rendered** was already delivered in S3's `_structs`
     system. Tests `StructureEdit_RejectedWhenTooFarFromAsteroid` + `StructureEdit_CannotEditAnotherPlayersShip`
     (400 green). **Deviations / in-engine eyeballing:** reach is a coarse sphere (not per-cell); unload distance is
     a flat radius; no decommission/claim-transfer UI yet; persistence hardening kept to the S4 save/load round-trip.
     **Item 20 (S1–S5) is now feature-complete; remaining polish (textures, sealed-volume airlock test, station
     interiors) is optional follow-up.**
   - ✅ **S6 follow-up — bespoke voxel ship layouts for the unlockable ships (done 2026-06-09; needs in-engine
     test).** The starter already reads as a ship (its silhouette is added in the box fallback). The 3 unlockable
     types now have hand-shaped, distinct voxel layouts in `data/ship_layouts/` (generated by
     [tools/gen_ship_layouts.py](tools/gen_ship_layouts.py), referenced via `ships.json` `layout`): **scout** —
     small, pointed glass nose, swept wings, single engine; **corvette** — twin stacked engines, side portholes,
     forward weapon nubs, a raised glass bridge; **hauler** — big boxy freighter, four engines, roof cargo
     containers. Each is a complete design (hull + hollow interior + floor + a rear airlock + front windows + the
     7→3 station markers, with cockpit+medbay together at the FRONT so you spawn away from the airlock and can take
     the helm). The space mesh skips station tiles (hollow interior) and renders any block key (carbon cargo etc.);
     `StampShipLayout`/`BuildShipStructure` resolve real block ids. Starter stays the parametric box (keeps its
     well-tested energy-hatch interior). 401 green. *Distinct silhouettes per unlockable ship — the original ask.*
   - **Biggest risks (de-risk early):** the free-space voxel collision + zero-g aim (S2) and the stylised-scale
     framing of voxel structures next to spheres (S1) — prototype + eyeball both before going wide.
21. ✅ **Feature — loading screens always show + stay readable ≥3s (done 2026-06-07; needs in-engine test).**
   `WorldLoadingOverlay` (the shared planet-landing + station-board veil) used to **skip** when the world was
   already ready (fast/cached load) and only held `MinShow=0.7s`. Removed the skip-when-ready early return so the
   veil **always** raises after the descent, and set **`MinShow=3.0s`** so it stays up long enough to read on both
   transitions (`WorldReady` now only gates the fade-out, with the 25s safety cap unchanged). Client rebuilt.
   *(Done together with item 19.)*
22. **Analysis + plan — immersive tutorial / onboarding mode (requested 2026-06-07).** *(ANALYSIS + PLAN ONLY —
   do NOT implement yet.)* Design how a **tutorial mode** could **introduce a new player**: in-game **tips** that
   teach the **basic functions** (move/look, mine, place, craft at a station/workbench, inventory/hotbar, the suit
   vitals/oxygen, flying the ship, landing, scanning, missions, …). It should be **immersive**, not a wall of
   pop-ups — e.g. the player's **ship AI** speaks the tips in-character (voiced/text lines triggered by context:
   first time you open the inventory, first low-oxygen warning, first ore mined, first station approach, etc.).
   **Analyse:** what onboarding exists today (does anything explain controls? the HUD prompts?), what the natural
   first-session beat sequence is, how to gate/trigger tips by player state + first-time events (a persisted
   "seen" set), how to present them (a diegetic ship-AI voice line + subtitle vs. a HUD toast), localisation
   (DE+EN), and a skip/replay option. **Plan** a staged, low-risk first version. *(Pairs with item 23 — the ship
   AI is the natural narrator.)*
23. **Analysis + plan — the ship AI as a recurring in-game character / system (requested 2026-06-07).**
   *(ANALYSIS + PLAN ONLY — do NOT implement yet.)* Plan a **ship AI** companion as a persistent presence and
   what **role it could play across future expansions**, beyond the tutorial (item 22). **Explore ideas:** a named
   on-board AI that comments on events (arrivals, danger, discoveries, low resources), gives **mission/quest
   hints + lore**, reads out **scan/sensor** results, warns about **hazards** (toxic atmosphere, hull/oxygen),
   manages **ship systems** as an in-fiction interface (travel, modules, fleet), reacts to the player's choices
   (a light **personality / relationship** track), and could later branch into **story beats**. **Analyse** how it
   would hook into existing systems (events the server already raises — arrivals, combat, scans, missions, vitals;
   the audio/voice pipeline; the HUD), the **content/voice** cost (lines + DE/EN + generated VO), and how to keep
   it **non-annoying** (frequency caps, mute/skip). **Plan** an incremental rollout (tips → event barks → systems
   narrator → story) so each step is shippable on its own.

### Small "rest" TODOs surfaced from completed items (promoted here 2026-06-07 for visibility)
*(These were implicit/deferred notes buried inside already-✅ items — captured as explicit backlog entries.)*
24. ✅ **Large landable asteroids (done 2026-06-07; needs in-engine test).** **Implemented:** worldgen now
   places **2–3 large landable asteroid bodies per system** (`UniverseGenerator` — `CelestialKind.AsteroidField`
   with **`PlanetType = "asteroid"`**, scattered orbit positions, ids `{system}-a{0..2}`), so each is **Asteroid
   size-class** → a defined small walkable world (circumference 800–1600 per id) and the existing
   `EvaLandingAllowed` guard permits it. The flight view (`SpaceView.BuildSystemBodies`) gained a **third loop**
   that renders `AsteroidField` bodies + adds them to `_landables`, so you **fly up + press E to land** — with
   the **ship or on an EVA** (travel/land already route through `LoadWorld(body.PlanetType, …)`; planets/moons
   stay EVA-rejected). The **small mineable rocks** are unchanged (space entities you shoot/harvest). Test
   `Universe_GeneratesLargeLandableAsteroidBodies_PerSystem` (2–3 per system, all asteroid-class) — 365 green;
   client + bundled server rebuilt. *(Original analysis below.)* *(from item 5, Stage 6; re-scoped 2026-06-07 — MEDIUM,
   not small).* **Finding on closer look:** there are **no discrete walkable asteroid bodies** — a system has at
   most one **asteroid *belt*** (`CelestialKind.AsteroidField`, no `PlanetType`, rendered in space as mineable
   rock *entities*). `RestoreJoinBody` already treats an `AsteroidField` as a travelable body, but it has no
   `PlanetType`, so `SizeClassFor` doesn't classify it as `Asteroid` and the flight view's **`_landables`** only
   loops **planets + moons**. So landing on an asteroid needs: **(1) worldgen** — give the `AsteroidField` body
   the **`asteroid` `PlanetType`** so travel/land loads the walkable asteroid world (+ `SizeClass=Asteroid`);
   **(2) client** — add an `AsteroidField` loop to `SpaceView._landables` (+ approach/land prompt + descent);
   **(3) server** — allow ship-landing on it (the generic land-to-body flow). **Design nuance to resolve:** the
   belt is *both* a field of mineable rocks in space **and** (after this) a landable asteroid world — decide how
   they coexist (e.g. land = descend to a walkable asteroid; staying in space = mine the rocks). Touches worldgen
   + flight view + server + needs an in-engine playtest. **Decision (user 2026-06-07): landable with the ship too**
   (like planets/moons), not EVA-only. *(Was mislabeled "small"; it's a medium feature — pick it deliberately.)*
   **Clarified design (user 2026-06-07):** **two asteroid scales.** Keep the **small** asteroids as mineable rocks
   you **shoot + harvest from the ship** (already exist as space combat entities). **Add bigger asteroids** with a
   **defined size (in the world generator)** that you can **land on** — **with the ship or via EVA** — descending
   onto a walkable asteroid world. So small = mine-from-ship (existing), large = a sized **landable** body (new).
   ### Analysis + plan (2026-06-07)
   **As-is (mapped):** small asteroids = `CombatEntity Kind=Asteroid` tiers 0/1/2 spawned in the per-body
   `SpaceInstance` (`CreateSpaceInstance` spawns 3; `RespawnAsteroids` refills) — shoot to split + harvest loot
   (works from the ship). A system has one **`AsteroidField`** belt body (no `PlanetType`). Landing flow:
   approach a `_landables` body → **E** → `SendLeaveSpace(bodyId)` → server `HandleTravel`/`LeaveSpace` →
   `LoadWorld(body.PlanetType, body.Id)`; the walkable world's size = `CircumferenceFor(id, SizeClassFor(kind,
   planetType))` (Asteroid class = **800–1600** blocks, deterministic per id). **EVA guard** `EvaLandingAllowed`
   already permits only `WorldSizeClass.Asteroid`. **Gap:** the `AsteroidField` body has no `PlanetType`, so it's
   not Asteroid-class and never appears in `SpaceView._landables` (planets+moons only) → unreachable.
   **Plan (small, mostly wiring):** (1) **worldgen** — `AsteroidField` body gets `PlanetType = "asteroid"` (→
   Asteroid size-class → defined walkable size 800–1600; EVA guard + travel then work as-is); (2) **client** —
   add an `AsteroidField` loop to `SpaceView.BuildSystemBodies` so the belt renders + is in `_landables` (ship +
   EVA can fly up + **E** to land), rendered with the `asteroid` look; (3) **bigger/sized large asteroids** — see
   the open questions; (4) tests (lands from ship + EVA; planets/moons still EVA-rejected; world loads as
   asteroid-class). Small mineable rocks stay as the space-instance entities.
   **Open design questions (ask the user):** how many **large landable** asteroids per system (just the one belt
   body, or several scattered)? and the **size** model — the auto deterministic 800–1600 per id, or an explicit
   small/large tier (a new size field in worldgen)?
25. ⏭️ **SKIPPED (user 2026-06-07) — covered by item 6a + the fixed landing zone.** You already land at the same
   persisted landing zone (with your ship) on every visit, and a reload restores your current body + position, so
   a per-body position map adds little and would *separate you from your just-landed ship* on travel-back. Left
   out by decision. *(Original below for reference.)*
   **Full per-planet position memory (option b)** *(from item 6).* Today only the **last** planet's position is
   restored on load (`PlayerState.CurrentLocationId` + `Position`). Option b = keep a **per-body position map** so
   travelling **back** to a previously-visited body drops you where you left it (instead of the landing-zone
   spawn). **To do:** persist a `{ locationId → position }` map on the player snapshot; on arrival at a visited
   body, restore that position (still guarded by `EnsureSafeSpawn`); update it on leave/checkpoint. Server-only,
   testable.
26. ✅ **Intermediate ship tier — Corvette (done 2026-06-07).** Added a balanced mid-class **corvette** between
   scout (fast/light) and hauler (slow/cargo): baseHull 130, baseShield 45, flightSpeed 1.1, handling 1.1, cargo
   60; `requiredBlueprint: ship_corvette` (data_fragment 4 + titanium_plate 11 + cable 5); craftCost on the new
   materials (titanium/iron plate + cable + energy_cell + **steel** + **light_alloy** + **circuit_board**); DE/EN
   names. Data-only — 364 green (consistency + locale parity). *(Original note below.)*
   **Intermediate ship tier — ✅ ALREADY SHIPPED (stale entry; verified 2026-06-11).** The decided corvette
   exists in full since commit `387d95f` (+ voxel layout in `abe8eb3`): `ships.json` "corvette" (130 hull /
   45 shield / speed+handling 1.1 / 60 cargo slots, craftCost on the new alloys/electronics, startModules),
   `ship_corvette` blueprint, bilingual names/descs, `data/ship_layouts/ship_corvette.json`, covered by
   `CreativeModeTests`. Nothing left to build — only the live balance feel (a playtest item).
27. **Already-listed future work (see "Not started / larger future work" near the end):** ~~W5 — poles~~
   (**dropped 2026-06-11 by user decision — superseded by the round-worlds torus, see its DONE block**),
   **per-species/planet flora colour tint** (a `ChunkMesher` tint-UV pass, requested 2026-06-06), **texture
   audit**, **uGUI theme/icon polish**. Kept in that section; pointer here so they're not forgotten. *(PvP ship
   combat + big cruisers stay **deferred by design**.)*
28. ✅ **Bug — multi-second freeze after "start new game" before the loading screen (done 2026-06-07; needs
   in-engine test).** Clicking Singleplayer → entering a new name / starting a new game → **several seconds of
   nothing**, *then* the loading screen (percent bar) appears.
   **Fix (done):** the loading screen is now shown **first** and the local server is spawned **off the main
   thread**, so the blocking `Process.Start` can never freeze the menu. `LocalServerLauncher` was split into
   **`Prepare`** (main thread — builds the launch info from Unity paths) + **`LaunchPrepared`** (thread-safe —
   does `Process.Start`); `AppShell.StartSingleplayerWorld` now calls `Prepare` + sets `Phase = Loading`
   immediately, and `Update` kicks `LaunchPrepared` on a `Task` once the loading UI is on screen. The client's
   existing connect-retry (`GameBootstrap` — every 2s, up to 6×) covers the case where the server isn't listening
   yet, so no launch gate is needed; `StopLocalServer` waits for an in-flight spawn so backing out can't orphan a
   server. *(The percent bar is still the time-based `MinShow` timer — driving it from real load stages is a
   future polish, noted below.)* Client rebuilt.
   ### Analysis (2026-06-07) — root cause
   Two things combine; both are in `AppShell.StartSingleplayerWorld` + `LocalServerLauncher` + `LoadingScreen`:
   - **(1) The loading screen is only shown *after* the server is spawned, and the spawn can block the UI
     thread.** `StartSingleplayerWorld` calls **`_localServer.Start(...)` synchronously** and only **then** sets
     `Phase = ShellPhase.Loading` (`AppShell.cs:157,169`). `Start()` itself just does `Process.Start()` on the
     bundled **self-contained .NET server EXE** (`LocalServerLauncher.cs:133`) — normally fast, **but on Windows
     the first launch of a freshly-built EXE is commonly stalled for seconds by Defender/SmartScreen real-time
     scanning** (the EXE changes on every `build-client.ps1`, so the scan re-runs). While `Process.Start()`
     blocks, the **menu is frozen** and the loading screen hasn't been built yet → "nothing happens".
   - **(2) The percent bar is fake + a fixed warm-up wait.** `LoadingScreen.Progress` is **purely time-based**
     (`_elapsed / MinShow`, `MinShow = 2.5s` set explicitly to "give the server time to start listening",
     `AppShell.cs:161`); it is **not** real world-load progress. After the 2.5s timer `LaunchGame()` runs, the
     client connects, joins, and streams the world. The bundled server's **cold start** (runtime init + content
     load + open/create the new world's SQLite DB + generate the spawn region + bind the socket) takes **~3s**
     end-to-end — server log: process at `13:41:39`, listening `13:41:40`, client connect `13:41:42`, join
     `13:41:43`.
   **So the perceived gap = the blocking `Process.Start()` (Defender first-scan) + the fixed 2.5s warm-up, none
   of which shows real feedback.** **Fix direction (when tackled):** (a) set `Phase = Loading` and render the
   loading screen **before** spawning the server (one frame), then start the server + connect on a coroutine so
   `Process.Start()` never freezes the menu; (b) drive the bar from **real stages** ("starting server →
   connecting → generating world → streaming chunks") instead of a fixed timer, and drop `MinShow` once real
   progress exists; (c) optionally reduce the Defender hit (don't rebuild the server EXE when unchanged / sign it
   / document a Defender exclusion for the saves+server dir). *(Analysis only — not yet implemented.)*

---

## 🐞 Reported bugs — triaged 2026-06-07 (sorted into the backlog; **not yet fixed**)
A batch of user-reported issues. Each checked against the code for validity + a code pointer + fix direction.
Tags: **VALID** (confirmed in code), **PARTIAL** (partly true / nuance), **FEATURE** (a new system, not a bug),
**PLAYTEST** (needs in-engine confirmation).
**✅ Fixed 2026-06-07 (quick tunables, 365 green, built):** B1 (audio `maxDistance 45→20` + Linear rolloff),
B5 (hard-block hardness ×1.6 — stone 3.8→6.1, ores/metals up; soft blocks unchanged), B9 (asteroid respawn
40→120s), B10 (asteroids scattered by golden-angle within weapon range), B13 (machete `damage 12→7`), B18
(temperament weights → Aggressive 15→9 / PackHunter 5→3 + aggro range 10→8). *Each still wants a playtest.*
**✅ Fixed 2026-06-07 (second quick batch, 365 green, built):** B23 (slide-door `OpenRange 4.5→2.8` so station/
village doors close again; ship hatch stays 1.8), B3 (place guard now only rejects the **head** cell, so you can
pillar-jump by placing at your feet), B16 (right-click a held **consumable** → eat: wires up the existing
`SendConsume` which was defined-but-never-called + a new ElevenLabs **eat** sound), B19 (volcanic lava abundance
`0.55→0.7` so lava pools are higher/more visible). *Each still wants a playtest.*
**✅ Fixed 2026-06-07 (medium batch, 365 green, built):** B2 (dedicated `asteroid_rock` OpenAI texture on the
mineable rocks), B20 (avatar face made clearer — bigger eyes + a mouth; it already *had* eyes/visor, so the
report was likely a pre-fix build), B17 (rounded **sphere** creature eyes — bigger, dark pupil + a white glint),
B12 (organic **meandering** wander — hold a hashed heading per segment + weave, instead of milling in tight
circles), B14 (weapon-aware attack animation — blade **slash arc**, gun **recoil**, else jab), B4 **partial**
(ship editor: added **Hinged Door**, relabelled **Medbay → "Medbay (Heal-Tank)"** + "Sliding Door"). *Playtest
wanted.* **B4 remainder ✅ DONE 2026-06-19 + B22 ✅ FIXED 2026-06-07** — see those entries.
**✅ Fixed 2026-06-07 (B21 — damage feedback, built):** on-foot **red screen flash** + a **cause label** in
`HudUi` whenever health drops, cause inferred from local state (in **lava** → "Burning", O₂≤0 → "Suffocating",
hunger≤0 → "Starving", else "Taking damage"), DE+EN. No more dying "out of nowhere" — you see the flash + why.
Client-only. *Playtest wanted.*

- **B1 — Creature sounds carry too far; should fade with distance, be silent when far. [✅ FIXED 2026-06-07 — playtest]**
  `ClientAudio.At` plays 3D at `minDistance 4`, `maxDistance 45` with Unity's default (logarithmic) rolloff →
  audible from far. *Fix:* lower `maxDistance` (~18–22) + use a Linear/custom rolloff so distant creatures fade
  to silence. (`ClientAudio.cs:316-329`.) Relates to [[netcodec-register-messages]]-era audio work.
- **B2 — Small mineable asteroids need their own texture. [✅ FIXED 2026-06-07 — playtest]** They're **not** untextured — `AsteroidMat()`
  reuses the **stone** block texture tinted grey (`SpaceView.cs:2027`). *Fix:* generate a dedicated asteroid-rock
  texture (OpenAI — **pre-approved**) + use it. ([[asset-generation-approved]].)
- **B3 — Can't pillar-jump (place a block under yourself). [✅ FIXED 2026-06-07 — playtest]** `HandlePlace` rejects placing in the cell you
  occupy — feet `fy` or head `fy+1` — "You can't place a block where you're standing" (`GameServer.cs:1586`); a
  jump-place targets your feet cell → rejected, so it seems to "break instantly". *Fix:* allow placing at the feet
  cell while airborne (keep the head-cell guard) so building straight up works.
- **B4 — Ship editor missing usable items; audit ship + station + settlement editors. [✅ DONE 2026-06-19]** The ship-editor
  palette **does** include **Medbay** (the heal-tank) + a **Door** (door_slide) (`ShipEditor.cs:59-78`) — "no
  heal-tank" is a labelling/visibility issue. But newer placeables are missing (e.g. **crate**, **door_hinge**,
  workbench/forge, modules like refinery/detoxifier/oxygen_generator/cargo_hold_1). *Fix:* audit + complete the
  ship palette, and do the same for the **station** + **settlement** editors. **Partly done 2026-06-07:** added
  **Hinged Door** + relabelled Medbay/Door in the ship editor. **Remainder — ✅ DONE 2026-06-19:** airlock/lab/
  console/crate added to the ship editor (editor→stamp round-trip verified — designed-ship station cells register),
  and the **station + settlement (structure) editors** were audited/completed (full block palette + brushes per the
  editor-palette upgrade).
- **B5 — Stone (and other) mining still too fast. [✅ FIXED 2026-06-07 — playtest]** Stone `hardness 3.8` (`blocks.json`); the
  drill's mining power clears it quickly. *Fix:* raise stone + rock/metal hardness (and/or lower tool power) and
  rebalance the hardness table for a slower dig.
- **B6 — Plants are a solid textured cube; want transparent leaves (also tree crowns). [FIXED 2026-06-07 — real
  alpha-cutout leaves]** `ChunkMesher` rendered `tree_leaves` + `flora_*` as full opaque cube faces. **User chose
  real alpha leaf textures** (over procedural shader holes / translucent blend). Implemented with the actual leaf
  art (no normal-map artefacts, holes follow the real leaf shapes):
  - **Bake** (`tools/ai-assets/bake_leaf_alpha.py`): the block atlas was already RGBA32 + preserves tile alpha,
    but foliage tiles shipped fully opaque. The script derives an alpha mask from each tile's OWN art (the
    darkest ~34% — the shadow gaps between leaves/petals — become transparent), leaving RGB untouched so the
    Sobel normal atlas stays clean. Rewrote the 20 foliage `.bytes` in place (`tree_leaves` + leafy `flora_*`;
    excludes structural/glowing flora: cactus, crystal, succulent, mushroom, puffball, pitcher, glowcap,
    emberbloom, sporepod, glowvine — and never the ground `grass`).
  - **Mesher**: a per-vertex **foliage flag** (new uv2.x) marks leaf faces (`IsFoliageBlock`, set kept in sync
    with the bake script). Opaque blocks now also draw faces toward foliage neighbours (`nbSeeThrough`), so you
    see leaf layers + the block behind through the holes (not a hollow shell). Leaves still collide.
  - **Shader** (`BlockAtlas.shader`): leaf-flagged faces `clip(texel.a - _LeafCutoff)` (default 0.5) — every
    other tile is fully opaque and unaffected. No new shader/submesh/material (alpha-test lives fine in the
    opaque queue → no GraphicsSettings always-include needed). Shader compiles clean; client build verified.
  - *Playtest:* check crown/bush density + whether distant foliage thins too much (alpha-test + mips); tune
    `--hole` (bake) or `_LeafCutoff` (shader) if so.
  - **Follow-up 2026-06-07 (first build still looked solid):** three fixes — (1) **atlas overflow**: the block
    atlas was only 8×8 = 64 tiles but `data/blocks.json` has **80 blocks**, so every block id ≥ 64 (newer flora +
    doors) got an untextured grey, alpha-less tile (also no cutout). Bumped to **16×16** (256 slots). (2) Holes
    were baked from the darkest *single pixels* → scattered sub-pixel specks that mip-average back to opaque;
    re-baked on a **coarse 16×16 grid** so holes are chunky, visible gaps. (3) Reverted the "draw faces toward
    foliage" culling back to a **thin shell**, so the holes show the sky/world BEHIND the tree (a dense volume
    just showed more leaves through the holes → read as solid).
- **B7 — How often is there water; lakes/ponds or only seas? [FIXED 2026-06-07 — upland ponds]** Worlds only had
  seas (basins flooded to one global level); no isolated lakes/ponds, and swim-deep water was rare (B26).
  **Carve-and-fill upland ponds** in `WorldGenerator.Generate`: per column **above** the sea, a low-frequency
  pond-mask noise on **flat ground** (slope-gated so the water sits level) carves a shallow bowl
  (`seabedY = surfaceY − depth`, depth tapering 0→5 rim→centre) and fills it with water up to the original
  surface — a pond flush with the ground, 2–5 deep, so B26's `IsSubmerged` swimming actually triggers. **Frequency
  derives from the world's `WaterAbundance`** (the same property that sets the sea level — the user's ask): wet
  worlds get more/larger ponds, dry worlds almost none, lava/airless worlds none (their sea isn't water). Pond
  columns grow aquatic flora (kelp/lily) not land plants. Deterministic (pure noise). Tuned scattered/rare
  ("water rare except oceans"); regression test `WateryWorld_GeneratesUplandPonds_AboveSeaLevel`; the atmosphere
  tests were made pond-robust (find the open-air surface, since a pond can now sit at the origin). 368 green; build
  verified. *(Knobs: `pondThreshold`, `PondMaxDepth`, `PondBand`, `PondMaxSlope` in `WorldGenerator`.)*
- **B8 — Thunderstorm has thunder audio but no visible lightning. [FIXED 2026-06-07 — weather Stage 3]** The faint
  pre-existing screen flash (`_flash*0.35`, no bolt) was the reason "no lightning showed". `WeatherFx` now: a
  **brighter** sky flash (`_flash*0.6`) **plus a jagged white bolt** (`DrawBolt`, glow + hot core, ~0.18 s) drawn
  from the top of the screen down to mid-screen on each strike; strikes every 4–11 s. Gated to **rain
  thunderstorms only** (`Weather=="storm" && Precipitation=="rain"`) — no bolts in blizzards/sandstorms/ashfall;
  thunder audio gated the same way (`ClientAudio.cs`).
- **B9 — Mined asteroids respawn too fast. [✅ FIXED 2026-06-07 — playtest]** `AsteroidRespawnInterval = 40s`, target 3
  (`GameServerSpaceCombat.cs:765-766`). *Fix:* raise the interval.
- **B10 — Mineable asteroids too clustered; spread them around the planet. [✅ FIXED 2026-06-07 — playtest]** `CreateSpaceInstance` spawns
  the 3 at a fixed near-line `(12 + i*8, 0, 18)` (`GameServerSpaceCombat.cs:327`). *Fix:* scatter them over a
  wider volume around the body. *(Note: pairs with item 24's new large landable asteroids.)*
- **B11 — Per-planet temperature + climate precipitation. [✅ DONE 2026-06-07 — Stages 1–3; playtest]**
  **✅ Stage 1 done 2026-06-07 (weather pass):** added `PlanetType.BaseTemperature` (per type in `planets.json` —
  lava 115, ice -38, desert 44, jungle 28, rocky 12, crystal 4, swamp 22, varied 16, asteroid -25, void 20) +
  `GameServerWeather.CurrentTemperature`: base + a **per-world seeded variation** (±14 → "especially hot/cold"
  worlds) + a weather cooling (storm -8/rain -5/clouds -2/clear +2) + a **day↔night swing** (±6 with air, ±16
  airless). Networked via `WorldEnvironment.Temperature` (+ a `Precipitation` field) and shown in the HUD
  (time-of-day panel, °C). **User refinement:** ship/station cabins read **22 °C**; **vacuum** worlds
  (space-sky asteroid/crystal) + **above the atmosphere** read **"—"**. `PrecipitationFor` classifies
  none/rain/snow/hail/ash/**sandstorm** by temp + surface block (server-side; deserts now `weather:"dynamic",
  stormChance 0.18` → sandstorms). 365 green.
  **✅ Stage 2 done 2026-06-07:** `WeatherFx3D` now renders by `env.Precipitation` (gated on `Precipitation!="none"`)
  with a per-form `Style` — **rain** (blue streaks), **snow** (white, slow, sideways wobble), **hail** (grey, fast),
  **fire-ash** (glowing orange, slow, drifting), **sandstorm** (tan horizontal streaks). Fog tinted per form
  (sandstorm = dense tan blind, ash = smoky, snow/hail = white-out, rain storm = grey-blue). `WeatherFx` screen wash
  tinted per form; 2D streaks limited to actual rain. Audio (B34): two new ElevenLabs loops `sandstorm_loop` +
  `ash_loop`; `ClientAudio.OnEnvironment` picks the bed by precip (sandstorm/ash/snow+hail→`wind_strong`/rain/storm).
  **✅ Stage 3 done 2026-06-07:** lightning flash + bolt — see B8.
- **B12 — Creature movement too simple (a few steps back-and-forth). [✅ FIXED 2026-06-07 — playtest]** Wander runs through
  `CreatureBehaviour.Step` (`GameServerCreatures.cs:352`); the wander is basic. *Fix:* richer roaming — varied
  headings, pauses/grazing, longer wanders, maybe light flocking.
- **B13 — Machete one-shots most animals. [✅ FIXED 2026-06-07 — playtest]** Machete `damage: 12` (`items.json`) vs creature
  `MaxHealth = 10 + size*8 (+10 if hostile)` (`CreatureGenerator.cs:78`) → small fauna ~15 HP die in one hit.
  *Fix:* lower machete damage or raise creature HP.
- **B14 — Weapon attack animations are poor. [✅ FIXED 2026-06-07 — playtest]** One generic `Swing()` for everything
  (`PlayerController.TriggerSwing` → Avatar/viewmodel `Swing`). *Fix:* better per-weapon-class swing/thrust/fire
  animations.
- **B15 — A "red creature" of two stacked blocks with no texture — what is it? [PLAYTEST]** There is **no**
  lava/2-block entity in space combat; it's almost certainly a **fauna creature** rendered **red** (hostile
  creatures get a red tint in `CreatureBuilder`) with a **missing/failed hide texture** (untextured → flat red)
  and/or a minimal body. *Fix:* confirm in-engine (scan it), then fix the hide fallback / body so a creature never
  renders as bare red cubes.
- **B16 — Can't eat food. [✅ FIXED 2026-06-07 — playtest]** The server has `ConsumeItemIntent` + `HandleConsume`/`ConsumeItem` (food
  heals) (`GameServer.cs:1101`, `GameServerCreatures.cs:498`), but the **client never sends it** (no eat key/UI).
  *Fix:* add a client eat action (a key, or an inventory "eat") that sends `ConsumeItemIntent` for the selected
  food. *(Hunger has a separate auto-consume path, `GameServer.cs:747`, but manual eating is unreachable.)*
- **B17 — Creature eyes look bad (tiny). [✅ FIXED 2026-06-07 — playtest]** Eyes are small flat-coloured quads + pupil
  (`CreatureBuilder.cs:79-94`, eyeSize ≈ 0.24·headScale). *Fix:* bigger/rounder eyes, better shading/highlight.
- **B18 — Creatures still too aggressive. [✅ FIXED 2026-06-07 — playtest]** Despite item 7's give-up leash, the temperament roll
  still makes hostiles (Aggressive 15 / PackHunter 5 weights in `CreatureGenerator`) + the aggro range bites.
  *Fix:* lower the hostile weights / aggro range / damage, or make more species flee. Pairs with B13.

### Second batch (reported 2026-06-07)
- **B19 — No lava on lava planets? [✅ FIXED 2026-06-07 — lava abundance bumped; playtest]** The code **does** generate a lava sea: the lava planet's
  `surfaceBlock "basalt"` → `volcanic` → `lavaAb 0.55`, and `WorldGenerator.ResolveSeaFluid` pools **lava in
  basins** below ~`BaseHeight − 0.25·Amplitude` (≈ Y 50 on the lava world). So lava only fills **low ground**;
  on high terrain / the landing zone you may see none. *Investigate in-engine:* dig down to ~Y 50 / find a basin;
  if still none, it's a gen/render bug. *(Possible tweak: raise `lavaAb` so lava is more visible.)*
- **B20 — Player avatar has no face. [✅ FIXED 2026-06-07 — bigger eyes + mouth; playtest]** The procedural avatar has no facial features (eyes/visor). *Fix:*
  add a face — at least eyes / a helmet visor — to the avatar model (`AvatarBuilder`/`AvatarEditor`).
- **B21 — "Died suddenly with no idea why" — audit the damage indicators. [✅ FIXED 2026-06-07 — red flash + cause label; playtest]**
  **Damage sources (all server-side, `GameServer.TickEnvironment` + combat):** **oxygen suffocation** — toxic/
  airless atmosphere (or being submerged) drains O₂; at 0 → **Health −5/s** (`GameServer.cs:651-653`); **lava** —
  standing in lava → **Health −15/s** (kills from full in ~7s!) (`:658-660`); **starvation** — hunger 0 → **−3/s**
  (`:677-679`); **fall damage** (`HandleFallDamage`, scales with impact speed); **creature** attacks
  (`GameServerCreatures.cs:181`) + **NPC enemy** attacks (`GameServerEnemies.cs:73`); **toxic food** (negative
  `ConsumeHealth`). **Display gap:** the HUD has health/O₂/hunger **bars** (bigger since item 19), but there is
  **no on-foot damage feedback** — no red hurt-flash/vignette, no hit indicator, and **no cause warning**
  ("Suffocating" / "Burning — lava" / "Starving"). The space view has a hit flash; on foot there's none. So a
  fast drain (lava 15/s, or O₂ hitting 0) kills with only a quietly-emptying bar → "out of nowhere". *Fix:* add
  an on-foot damage cue — a red flash/vignette pulse on health loss + a short cause label, and verify each source
  decrements health correctly. *(Likely culprit for the sudden deaths: lava or a fast O₂ drain on a toxic world.)*
- **B22 — Vendor "E" opens the inventory/crafting menu, not a trade screen. [✅ FIXED 2026-06-07 — barter screen; playtest]** A vendor sets
  `NearbyStation = "market"` and **E** calls `GameMenu.OpenMarket()`, which just opens the unified **crafting/tech
  menu on the "market" category** (`GameMenu.cs:69` — its own comment flags the "crafting list instead of the
  vendor's trade view" issue), not a dedicated barter screen. *Fix:* give the vendor its own trade/barter screen
  (or a clearly trade-only view) instead of the crafting menu. *(Deferred from the medium batch — it's a
  medium-**large** new UI; do it as its own focused task.)*
  **Analysis 2026-06-07:** the economy is **pure barter, no currency** — vendor trades are the **market recipes**
  in `data/recipes.json` (`station:"market"`, e.g. 5 iron_ore → 1 titanium_ore), executed via the normal
  `CraftIntent` → `GameServer.HandleCraft` (validates `MarketAvailable`, consumes inputs, grants outputs, sends
  `InventoryUpdate`). E at a vendor sets `NearbyStation="market"` (`NearVendor`, vendor NPC within 3.6) → `GameMenu.
  OpenMarket()` opens the crafting menu on the market category. **Plan (chosen = barter screen, no server change):**
  a dedicated client `VendorTradeUI` opened on E at a vendor instead of the crafting menu — lists the market
  exchanges as "give X → get Y" cards, greys out the ones the player can't afford (lacks inputs), and on click
  sends the existing `CraftIntent(recipeKey)`; refreshes on `InventoryUpdate`/`CraftResult`. Shows the vendor's
  name. No new messages/NetCodec/economy. (Currency-based shop with credits considered but rejected — it'd be a
  whole new economy: a balance on `PlayerState`, prices on every item, buy/sell intents.)
  **[FIXED 2026-06-07 — user chose barter]** New client `VendorTradeUI` (own canvas, sortingOrder 60): a focused
  centred panel titled with the nearest vendor's name, listing each market exchange as a "give X → get Y" card —
  unaffordable ones show the short count in red + a disabled "Not enough" button; affordable ones have a "Trade"
  button that sends `SendCraft(key, 1)`. Refreshes on `InventoryUpdated`; closes on Esc/Tab/close-button/world-
  reset. `GameMenu.OpenMarket()` now opens this instead of the crafting menu (the two are mutually exclusive via
  `SetOpen`/`CloseForTransition`). E at the vendor opens it (`PlayerController` unchanged). Bilingual keys
  `ui.vendor.*` added (EN+DE). No server/protocol change. 367 tests green (locale parity); client build verified.
- **B23 — Station doors don't auto-open/close; open radius too large. [✅ FIXED 2026-06-07 — OpenRange 4.5→2.8; playtest]**
  Station slide doors are registered via `RegisterStationDoors → MakeDoor("slide", …)` with the **default
  `SlideDoorOpenRange = 4.5`**; the per-door tighter range from the ship-hatch fix (1.8) was applied **only** to
  ship-stamp doors, not stations. In a station's tight rooms 4.5 means you're always within range → doors stay
  open. *Fix:* give station (and tight interior) slide doors a smaller open range, like the hatch.
- **B24 — Red dots in the space HUD — enemies? Flew to one, saw no enemy ship. [PLAYTEST/analysis]** Most likely
  the **enemy drones at long range**: combat spawns drones **150+ units away** from the launch point
  (`GameServerSpaceCombat.cs:349` — deliberately far so launching is safe) and each drone has a **glowing red
  sensor "eye"** (`SpaceView.cs:1232`), so at distance it reads as a small red dot; singleplayer runs
  `--space-combat PvE --space-npcs Normal` so drones do spawn. (Less likely: reddish background **stars**, or the
  ship's own red **port nav-light**.) Flying "to a dot" and seeing nothing could mean: it was far/you stopped
  short, the drone moved, or it isn't an enemy at all. *Investigate in-engine:* fly fully out toward one + scan;
  if confirmed enemies, they may need a clearer HUD contact marker (a labelled hostile blip / off-screen arrow)
  so "red dot" reliably means "enemy ship there". *(Relates to B21's missing on-foot feedback — the space HUD's
  contact cues are thin.)*

### Third batch (reported 2026-06-07)
- **B25 — Avatar has no face in the avatar editor; the in-game colour menu shows no avatar at all. [FIXED
  2026-06-07]** **(a)** Already resolved by B20: `AvatarEditor` builds its preview with `PlayerAvatar.Build`, which
  now adds the eyes/pupils/brow/mouth (+ the visor moved below the eyes) — so the editor inherits the face; the
  report predated the B20 fix. **(b)** The colour tab (`CraftingTechShipUI` Mode.Character) showed only colour
  swatches, no figure. Added a **live faced-avatar preview**: new `AvatarPreviewRig` builds a real `PlayerAvatar`
  (same faced body) at an isolated far spot with its own short-range point light + camera rendering to a
  `RenderTexture`, shown via a `RawImage` in the colour tab's detail pane. It rotates while the tab is open and
  recolours live as you cycle a part (`BuildCharacterList` → `SetColors`); the camera only renders while the
  Character tab is visible (`ShowMode`/`Hide` toggle). One shared faced-build path (`PlayerAvatar.Build`) across
  the in-world avatar, the editor, and this preview. Bilingual key `ui.settings.preview`. 367 tests green; build
  verified.
- **B26 — Water can't be scanned, and you don't sink in / can't swim. [(a) FIXED 2026-06-07; (b) = B7]**
  **(a) [FIXED]** Scanning water returned nothing because the client scan used `Physics.Raycast`, and fluids have
  **no collider** (excluded from the chunk collider so you can swim) — so the ray passed straight through water.
  The server already scans any block (water yields a sensible "Yields: water×1" result via `ScanSubject`). Fix:
  `PlayerController.ScanTarget` now targets via the **voxel ray-march with `includeFluids: true`** (a new param on
  `AimBlock`), so a water/lava block under the crosshair is hit + scanned. Client build verified.
  **(b) NOT a bug — gated on water depth (B7).** The swim code is intact and correct: the collider excludes fluids
  (you fall/sink into water), and `IsSubmerged` (water at **chest height**, feet+1.1) switches to buoyant swimming
  (idle sink, hold Jump to rise, slower horizontal). It only triggers in water **≥ ~2 blocks deep**; the world's
  seas sit BELOW `BaseHeight` so only genuine low ground floods, and deep basins are uncommon → the player usually
  meets shallow, wadeable water and never submerges. Making swimmable water common (lakes/ponds + deeper basins)
  is **B7** — do that to make swimming actually reachable.

### Fourth batch (reported 2026-06-07)
- **B27 — On a planet, the landed ship's door "stands in the air" / "on the roof". [FIXED 2026-06-07]** A
  regression test (`ShipHatchDoor_SitsAtCabinFloor_NotOnTheRoof`) proved the box-ship hatch door is registered at
  cabin-floor level (y0+1, `dY=1.0`), so the placement was never on the roof — the real cause is the hull being a
  flat box anchored at the *centre's* surface height: on sloped/jungle ground the rear hatch hangs over (or buries
  into) terrain at a different height, and a rear engine nozzle sat directly in the left hatch column.
  *Fix (`GameServerShipStructure.StampShip`):* moved the engine nozzles outboard (`cx ± _shipHalfX`) so they never
  block the way out, and carve a **flush rear doorstep** — a solid threshold at floor level out the hatch, the
  doorway cleared above it, and a foundation under it — so the door always meets level, walkable ground regardless
  of the terrain it landed on. *(Playtest to confirm the visual.)*
- **B28 — German menu tab "Einstellungen" overflows its button graphic. [FIXED 2026-06-07]** The tab buttons are a
  fixed 150 px wide with 22 pt bold text (`CraftingTechShipUI.BuildHeader` + `UiKit.AddButton`, overflow wrap), so
  the 14-char "Einstellungen" spilled out. *Fix:* enable best-fit on each tab label (`resizeTextForBestFit`,
  max 22 / min 12) so a long localized tab auto-shrinks to fit its button while short labels keep full size — no
  row-width change (the 8-tab row is already ~1296 px).
- **B29 — A space station is stuck inside a moon / large asteroid. [FIXED 2026-06-07]** Stations + wrecks were
  placed by `DiscPoint` with no overlap check (and they skip the flight-view body-separation pass, which only
  de-overlaps planets/moons/asteroids). *Fix (`UniverseGenerator`):* a `SeparateFromBodies` pass pushes each
  station/wreck radially clear of every planet/moon/asteroid at generation (system-unit clearances sized for the
  0.16× flight scale — planet 300 / moon 185 / asteroid 150), so the authoritative position is clear for both the
  render and the server's board-range check. Regression test `StationsAndWrecks_NeverSpawnInsideABody`. 367 green.
- **B30 — NPCs walk out through the station's window/glass. [FIXED 2026-06-07]** Two causes: NPC world-collision
  only checked the wander **endpoint** cell (so an NPC's wander arc could teleport across a one-block glass pane
  when the far side was open air), and it keyed on "non-air" rather than the block's **Solid** flag. *Fix
  (`GameServerNpcs`):* the step is now **swept** (`PathBlockedByWorld` samples the segment every ¼ block, so no
  tunnelling through a 1-block wall/pane), and collision uses a new `IsSolidCell` keyed on `BlockDefinition.Solid`
  — keeping **solid** and **transparent** distinct exactly as noted: **glass** is solid-but-transparent (blocks
  NPCs, you see through it) while a non-solid transparent block like **water** stays passable.
- **B31 — Trees (flora) spawn on top of the landed ship. [✅ FIXED 2026-06-08]** On a jungle planet the
  player landed and **trees were standing on the ship**. World decoration (flora/trees, and likely also rocks/
  creatures) is placed without checking the landed ship's stamped footprint, so vegetation grows through/on the
  hull. *Fix:* when stamping the ship (or when scattering flora/decoration), **keep a keep-out around the ship
  footprint** so nothing spawns on or inside it — either skip decoration cells whose column intersects a ship
  stamp (`GameServerShipStructure` `CurStamp` bounds), or stamp the ship *after* clearing any flora in its
  footprint. Check whether the order is worldgen-decoration-then-ship-stamp (ship should clear what it overlaps)
  and whether ongoing flora respawn also needs the keep-out. Likely also applies to settlements/stations stamped
  onto terrain. Medium.
  **FIXED 2026-06-08:** `StampShip` now calls `ClearShipKeepOut(cx,cz,halfX,halfZ,y0,height)` in **both** stamp
  paths (the default box hull and a designed-ship voxel layout) **before** laying the hull. It clears any
  vegetation (`flora_*` / `wood_log` / `tree_leaves`, via the existing `IsFlammable` predicate) in the ship's
  footprint **plus a 3-block crown margin** and **up to 9 blocks above the roof** — so a tree world-gen grew there
  can neither stand on the hull nor poke through it, and an overhanging neighbour's crown is removed too. Only
  vegetation is cleared (terrain + hull stay); runs at world-load before chunks stream, so no `BlockChanged`
  broadcast is needed (same as the hull stamp). Test: `ShipKeepOut_ClearsTreesOnAndAboveTheHull` (plants a trunk
  in the cabin + leaves above the roof → both cleared). **Settlements/stations stamped onto terrain may want the
  same keep-out** — left as a follow-up (not reported yet). Playtest: land on a jungle world, no trees on the ship.
- **B32 — Sometimes a block can't be mined (e.g. mud/grass): one block mines, the adjacent one won't. [✅ FIXED 2026-06-08 — client ghost-clear; orig VALID —
  reported 2026-06-07]** Intermittent: the player mines one mud/grass block fine, but a neighbouring identical-
  looking block doesn't break. Mud/grass need no tool, so it's not a tool-tier gate. *Hypotheses to investigate:*
  (a) **mining protection** — the block is part of a ship/settlement/structure footprint (`IsProtectedShipBlock`
  / `_shipExtra` / settlement-protected cells, or the **foundation fill** under a stamp) that silently rejects the
  dig, so a stray protected block sits among normal terrain; (b) **client↔server target mismatch** — the raycast/
  aim resolves a slightly different cell than the server validates (off-by-one at block edges / longitude wrap),
  so the dig lands on air/another cell and "does nothing"; (c) a **block-state/hardness desync** (mined on the
  server but still shown client-side, or hardness/durability not reset); (d) the cell is actually a different
  block than it looks (a flora/decoration or a stamped block re-textured like mud). *Where:* `GameServer.MineBlock`
  + the mining-protection checks + the client `PlayerController` dig raycast. Needs a repro (which block, is it
  near a ship/settlement?). Medium.
  **Analysis 2026-06-07:** `GameServer.HandleMine` (GameServer.cs:1398) rejects a dig via `Reject("mine", reason)`
  on: air, **ship block** (`IsShipBlock` — hull AABB + `_shipExtra` incl. the 6-deep stone foundation), **settlement
  block** (`IsSettlementBlock` — a coarse min/max **AABB that also covers the natural terrain between buildings**),
  **station block**, not-mineable, **out of reach** (server MaxReach 8 vs client raycast Reach **6**), another
  player's landing zone, tool-tier. Mud/grass are hardness 0.6 / no tool → one-shot, so it's not a progress issue.
  Each `Reject` sends `ActionRejected` → client plays an error sound + sets `LastMessage`, which the HUD shows as
  the cyan **toast** (`HudUi.cs:270`). So **two distinct cases**: (1) a *reject* (toast + buzz) ⇒ a protection
  AABB is covering plain terrain — fix = exempt natural-terrain blocks from settlement/structure protection (or
  track exact built cells); (2) *nothing at all* (no buzz/toast) ⇒ the client never sent the dig (raycast missed /
  collider gap at a chunk seam, or the cell is just past the **6**-unit client reach while looking adjacent), or a
  client↔server state desync. Awaiting user repro detail (sound+message vs nothing; near a ship/settlement or open
  wilderness) to pick the fix.
  **User repro 2026-06-07: "nothing happens" (no buzz/toast), "everywhere".** ⇒ case (2): the dig intent was
  never sent — `HandleInteract` does `Physics.Raycast(...Reach...)` and **silently `return`s** when it misses.
  Two causes: the client `Reach` was **6** vs the server's **8** (a 2-unit dead-band where a click does nothing),
  and the chunk **MeshCollider is rebuilt right after every dig**, so a raycast against it can miss a block that's
  clearly there for a frame. **[FIXED 2026-06-07]** (a) bumped client `Reach` to 8 to match the server; (b)
  replaced the block mine/place targeting with a **voxel ray-march** (`PlayerController.AimBlock`, Amanatides &
  Woo over `ClientWorld` — the source of truth, always in sync, immune to collider rebuild/recook lag + seams;
  fluids passed through to match the collider). Mining/placing now never silently fails when a block is in front.
  Client build verified. *Playtest to confirm.*
  **Update 2026-06-07 (still happens after the fix):** the raymarch/reach fix did **not** resolve it, so it is
  **not** a targeting miss. The user's pattern: mining gets **stuck for a while** (several blocks in a row won't
  break), then **un-sticks** (some block suddenly mines) and **the previously-stuck blocks then work too**. That
  "all blocked, then all allowed" cadence points at a **per-player global gate**, not a per-block one — i.e. a
  **mining cooldown / rate-limit / swing-lock** (client or server) that drops dig attempts within a window, OR a
  server-tick/cadence (e.g. mines only processed on a timer; progressive `_miningProgress` gated by a cooldown).
  Re-investigate: a `GetMouseButtonDown` vs continuous-hold mismatch, any mine cooldown/swing-lock in
  `PlayerController`, and any per-player throttle in `GameServer.HandleMine`/the tick loop. Tool power (Basic
  Drill `MiningPower` vs mud/grass hardness 0.6) may mean a click only adds partial progress, so a block needs
  several clicks — feels like "won't mine" until enough accumulate.
  **Root cause found 2026-06-07 (the real one):** there are **two** mining paths and the first fix only touched
  one. Path A `HandleInteract` (single click, bare hands) → now voxel-raymarched. **Path B `HandleDrillAudio`**
  (drill **held**, sends a mine every 0.28 s) still used **`Physics.Raycast`** — and the user mines with the
  **Basic Drill**, so all their mining went through Path B. After a block breaks the chunk collider is rebuilt;
  the raycast misses for a frame, and the `if (Physics.Raycast(...))` wrapped the **whole** drill body, so the
  tick/mine/sparks all stalled until it settled — then everything resumed (exactly "stuck, then a block mines and
  the stuck ones work"). **[FIXED 2026-06-07]** `HandleDrillAudio` now targets via `AimBlock` (the voxel
  raymarch) too — sparks use the targeted cell centre. Both mining paths are now collider-independent. Client
  build verified. *Playtest to confirm.*
  **Real root cause 2026-06-07 (third pass — precise):** still happened; the user now sees HUD **"mine: block is
  already empty"** while **scanning the same cell shows grass**. So it's a genuine **client↔server desync**: the
  CLIENT renders+scans a block (grass) that the SERVER has as **air**. The client only ever learns of edits via
  `BlockChanged`; some server path set the cell to air **without a broadcast the client applied**, leaving a
  **ghost block** in the stale client chunk. Confirmed non-broadcasting `SetBlock(air)` paths: the structure
  **stamps** (ship/settlement/wreck/station clear cells to air — incl. the B27 doorstep carve) and any **re-stamp**
  (ship expand/return) that modifies chunks a client already has. **[FIXED 2026-06-07 — self-heal]** `HandleMine`'s
  "already empty" branch now calls **`ResyncStaleChunk`**: it sends the authoritative block for the cell
  immediately AND drops the chunk from the session's `SentChunks` so `StreamChunks` re-sends the current chunk next
  tick — every ghost in it vanishes. Robust against ANY desync source (not just stamps). 367 tests green; client
  build verified. *Follow-up if still seen: make the live re-stamps broadcast their clears so ghosts never form.*
  **✅ RESOLVED 2026-06-07 (user-confirmed: "problem no longer occurs").** The server-side re-stream alone wasn't
  enough (timing / a stale client chunk GameObject). The fix that worked: a **client-side ghost clear** — the
  server is authoritative, so when a dig is rejected as **"already empty"**, the client now sets that cell to air
  locally (+ remesh) using the last-mined cell (`GameBootstrap.ActionRejected` + `PlayerController.LastMineCell`).
  The phantom block vanishes on the first dig and the next dig hits what's actually behind it. Kept the server
  `ResyncStaleChunk` too (belt-and-suspenders). Root cause stands: structure **stamps** `SetBlock`-clear terrain
  without a `BlockChanged` broadcast → stale already-streamed clients get ghost blocks; *optional hardening: have
  live re-stamps broadcast their clears so ghosts never form in the first place.*
- **B33 — Station NPC name labels float in a planet's sky. [FIXED 2026-06-07]** User saw blue floating NPC name
  labels up in the air on a planet — space-station inhabitants leaking into the planet view. Cause: `NpcView`
  subscribed only to `NpcsReceived` and **never cleared its NPCs on a world change**. Each world keeps its own NPC
  list with IDs that restart at 1, so the station's NPCs (sitting at the station's void-world Y≈64) lingered in
  the client after returning to the planet (their high Y → floating in the sky; `ScenePos` passes Y through
  unwrapped). *Fix:* `NpcView` now also subscribes to `WorldResetReceived` and **destroys all NPCs on every world
  reset** (the new world's `NpcList` repopulates). Note: the other entity views (`CreatureView`, `RemotePlayers`,
  `DoorView`) rely on the same snapshot-pruning and have the same latent gap — apply the same `WorldReset` clear if
  creatures/remote players ever leak across worlds. Client build verified.
- **B34 — Loading flashes the empty/half-built world before the loading screen. [FIXED 2026-06-08 for dock +
  enter-ship; planet landing keeps its descent — re-check]**
  When a world / station / ship interior loads, the player briefly sees the **empty (still-streaming) world**,
  *then* the loading overlay appears, *then* the finished world. Wanted: the **loading screen from the very first
  frame** of the transition, hiding everything until the destination (planet / station / the player's ship,
  depending on where they load) is fully built — then reveal. *Investigate:* the loading-overlay timing in
  `GameBootstrap` (`WorldLoadStarted`/`HyperjumpStarted` events, `WorldReady`, the `OnWorldReset` order) + the
  `PlayerController` settle-freeze that dismisses the overlay. The overlay is currently raised *inside*
  `OnWorldReset` (after chunks are torn down) and dismissed once the player "settles onto solid ground", so there's
  a gap at the very start (old world torn down, overlay not yet up) and possibly an early dismiss before the world
  finishes meshing. *Fix idea:* show the overlay the instant a travel/board/land intent is sent (before any world
  teardown), and only dismiss after the destination's chunks around the spawn have meshed (not just collider-
  settle). Medium.
  **Re-reported 2026-06-08:** the same flash happens specifically when **docking a station, landing on a planet,
  and boarding your own ship** — you briefly see the current planet / station / ship before the loading overlay
  comes up. Same root + fix as above (raise the overlay the instant the dock/land/board intent is sent).
  **FIXED 2026-06-08:** added `GameBootstrap.BeginWorldTransition()` + a `WorldTransitionStarted` event; the
  overlay now **pre-raises the veil immediately** on a descent-less transition — **boarding a station**
  (`SendBoardStation`) and **stepping into the ship interior** (`SendEnterShip`, from the cockpit and from an EVA)
  — instead of waiting for the server's `WorldLoadStarted` (which only fires after the world swap, leaving the old
  view visible for the round-trip). A **2.5 s confirmation timeout** drops the pre-raised veil if no world load
  follows (a rejected action can't get stuck behind it), and `WorldLoadStarted` confirms + refreshes the title
  once the destination is known (both paths send `WorldReset{Hyperjump=false}`, verified). **Planet landing was
  left as-is on purpose** — it plays the in-space **descent** which already masks the build-up, and the overlay
  raises as the descent ends; veiling it immediately would hide that animation. **Playtest:** dock a station +
  enter your ship from space → no flash; **then check a planet landing** — if it still flashes at the end of the
  descent, tell me and I'll veil just that last moment without killing the descent.
- **B35 — Trees (flora) stand *in* the water. [FIXED 2026-06-08]** On a world the player saw **trees
  growing inside water** (submerged trunks). Almost certainly a **side-effect of B7 (upland ponds/lakes, just
  shipped)**: the flora scatter places trees from the **terrain surface height** without checking whether that
  column is **under water** (a pond/lake carved below the surface). *Investigate:* `WorldGenerator`'s flora/tree
  placement — it should skip any column whose surface cell is water or that sits **below the local water level**
  (use the same `PondDepthAt`/pond-mask that carves the water, or test the top solid cell for a fluid above it).
  *Fix idea:* before stamping a tree/flora, reject the site if the surface is a fluid or within the pond depth;
  only plant on **dry** land. Small, pairs with B36 (same root: placement ignores the new scattered water).
  **CONFIRMED 2026-06-08:** `WorldGenerator.StampTrees` excludes only the **global** sea (`sy + 1 <= fluidLevel`)
  and never consults the B7 pond mask, so a tree on a pond column gets a trunk standing on the pond water (the
  small `flora_*` loop already guards with `seabedY + 1 > waterTop`; trees were missed). *Plan:* add a centralized
  `WorldGenerator.SurfacePondDepth(planet,x,z)` (computes this world's pond-enable + threshold + seed internally,
  reusing `PondDepthAt`) and, in `StampTrees`, `continue` when `SurfacePondDepth > 0` (after the existing sea
  check). Same helper fixes B36. **FIXED:** added `WorldGenerator.SurfacePondDepth` + `IsSurfaceWater`;
  `StampTrees` now skips pond columns. Test: `Trees_DoNotStandInUplandPonds` (no `wood_log` has water directly
  beneath it on a forested watery world) + `IsSurfaceWater_FlagsPondsAndDryLand`.
- **B36 — The ship lands *in* a lake (stands in the water). [FIXED 2026-06-08]** The player's ship
  **landed inside a see/pond**, sitting in the water. Task 1 made landing **fluid-aware for oceans** (ships rest on
  the seabed on water worlds, dry cabin) — but for a **scattered upland pond/lake (B7)** on an otherwise dry land
  world, landing the ship submerged in a small pond looks like a bug, not intended seabed-landing. *Investigate:*
  the **landing-site selection** (where the server picks the ship's touchdown column on a planet) — it likely
  doesn't avoid pond/lake columns. *Fix idea:* on non-ocean worlds, the landing-site search should **prefer dry
  land** (reject columns that are water/pond at the surface, nudge to the nearest dry spot), reserving seabed-
  landing for genuine ocean worlds. Small-medium, pairs with B35 (both = pond placement not respected).
  **CONFIRMED 2026-06-08:** `EnsureLandingZone` (`GameServerSpace.cs`) marches the zone +X (spacing 24, radius 8)
  skipping only `OverlapsSettlement` — **no water check** — and `StampShip` anchors at `y0 = SurfaceHeight` (the
  terrain top, water-blind), so a pond/sea column at the zone centre puts the ship in the water. The zone is
  computed once per (player, world) and persisted, so the fix only affects *new* landings. *Plan:* extend the
  march to also skip columns where `SurfacePondDepth > 0` (and, per the chosen policy below, genuine sea), keeping
  zones ≥ spacing apart (wrap-aware) so players don't collide; if the whole budget is water (all-ocean world),
  **fall back** to the settlement-only spot so Task 1's seabed-landing (dry cabin) still applies — no far-marching.
  **FIXED 2026-06-08 (chosen policy: prefer dry land, seabed fallback on all-ocean):** `EnsureLandingZone` now
  marches to the first spot clear of the settlement AND of other zones AND on dry land (`LandingFootprintWet`
  samples the pad centre + radius edges via `WorldGenerator.IsSurfaceWater`); keeps the first town/zone-clear spot
  as the all-ocean fallback. Test: `Ship_LandsOnDryLand_NotInWater` (jungle pad comes out dry, via the
  `LandingPadIsDry` hook). Note: only affects *new* landing zones (existing ones are persisted).
- **B37 — Audit: is the (varying) sun colour used *consistently* everywhere? [AUDITED + FIXED 2026-06-08]** The
  star colour is meant to **vary per system**. It *is* generated server-side (`GameServerWeather.StarColor(system)`
  → `_sunColor`, networked as `Environment.SunColor`) and consumed in several places — `Sky.cs` (planet sky +
  directional sun light + `SetGrade` colour grade), `SpaceView.cs` (the sun disc/corona in space), `Clouds.cs`
  (cloud tint). **Audit goal:** confirm the variation is (a) actually *visible* (real spread between systems, not
  all near-white), (b) used **consistently** — the **system's sun in the space/system view** matches the colour the
  **planet** sees, and (c) the planet is **lit & tinted to match** (directional light colour, **ambient/skybox**,
  and the terrain/world colour-grade all follow the star — not a fixed white ambient that washes the tint out).
  *Check:* does `SpaceView` use *each planet's* star colour or one global sun? Is `RenderSettings.ambientLight`
  derived from `SunColor` or constant? Does the colour-grade meaningfully shift block/terrain colours? Report
  gaps; fix only after. Small audit, possibly small fixes.
  **FINDINGS 2026-06-08:** the source varies well — `StarColor` blends across a real hot→cool ramp
  (blue-white → white → yellow → orange → red-orange). Already **consistent**: the planet **directional sun light**
  + the block-shader **diffuse** global (`Sky.cs` `LightId = sunColor*brightness`), the **sun disc**, the
  `SetGrade` post **colour-grade**, the **cloud** tint at sunset (`Clouds.cs`), and the **space sun disc/corona +
  lens flare** (`SpaceView` `_sunColor` from `Environment.SunColor`) and the **station-window sun**
  (`StationBackdrop`). **GAP found + FIXED:** the **daytime sky was a hardcoded blue** (`Sky.cs.ApplyLighting`),
  ignoring the star — so a red-star world still had a blue sky. Now the day sky (and the fog + reflection global
  that follow it) is **tinted toward the star's hue** (normalise the sun colour to a pure hue, lerp the sky 0.5
  toward `daySky*hue`): warm/red stars → warmer skies, blue-white stars → cooler. **Remaining (lower-priority,
  not done):** Unity `RenderSettings.ambientLight` is unmanaged (world blocks don't need it — they use the
  sun-tinted `LightId` global — but standard-shader objects could pick up a star-tinted ambient); **space-view
  planets + their cloud shells** use fixed biome tints (not star-lit) and the starfield is generic (arguably
  correct — those are *other* suns). Playtest: land under a warm/orange star and a blue-white star, compare skies.
- **B38 — Audit: are creatures & flora generated with the intended *random* colours? [AUDITED + FIXED 2026-06-08]**
  Two parts. **(a) Flora:** a per-planet `FloraColor`/`Environment.FloraTint` re-tints flora via the
  block shader global `_Sc_FloraTint` — but `ChunkMesher` only flags the **small `flora_*` plants** for the tint
  (`IsFlora`, comment "small flora plants"). **Verify tree crowns (`tree_leaves`) are also flagged/tinted** — if
  not, trees stay one fixed green while ground flora recolours (likely gap). **(b) Creatures:** creatures carry
  `ColorRgb`/`BellyRgb` (client `CreatureBuilder` builds from them), but spawn defaults to `0xFFFFFF` when unset
  (`GameServerCreatures` `ColorRgb = sp?.ColorRgb ?? 0xFFFFFF`). **Verify spawning actually assigns the intended
  *randomized* colours** (per-individual variation, not all white/default) — a white/default fallback is also the
  suspected root of **B15** (the untextured red/blank creature). *Check:* where creature `ColorRgb` is set at spawn
  (species palette + per-spawn RNG), and whether `tree_leaves` gets the flora-tint vertex flag. Report gaps; fix
  after. Small audit; pairs with B15.
  **FINDINGS 2026-06-08:** **(a) Flora — GAP found + FIXED:** `ChunkMesher.IsFloraBlock` flagged only the small
  `flora_*` plants, so **tree crowns stayed a fixed green** while ground flora recoloured per planet — yet the
  server's `FloraColor` is documented as *"one hue for all of a planet's plant life."* Fix: `IsFloraBlock` now also
  flags **`tree_leaves`**, so a planet's leaves take its flora hue (alien purple/amber foliage on exotic worlds);
  the `wood_log` trunk keeps its natural bark colour. **(b) Creatures — VERIFIED WORKING (no change):**
  `CreatureGenerator.PickColor` already randomises every species' `ColorRgb`/`BellyRgb` — ~50 % vivid exotics (a
  fully random hue at high saturation) and ~50 % habitat-tinted naturals with ±60 per-channel jitter — assigned at
  species generation. The `?? 0xFFFFFF` white default only triggers if the **species is null** (a data/desync
  error, never normal spawn); that — or a `PickHide` texture-load failure — is the **B15** lead, a *separate* bug,
  not a colour-generation gap. Playtest: trees on a non-green-flora world should now match its foliage hue.
- **B39 — New-world spawn sometimes fails: player falls in space, then the loading screen hangs forever.
  [FIXED 2026-06-08]** Occasionally when **generating/entering a new world** the player **doesn't spawn
  on the surface** — instead they **fall through into the void/space** — and the **"Loading world" overlay then
  comes up and stays stuck**, with nothing further happening (a softlock; the only way out is to quit). This is
  worse than B34 (a *flash*): here the load **never completes**. *Likely root:* the spawn position isn't resolved
  to solid ground for that world (placed above terrain that hasn't streamed, or in a column with no surface — cf.
  the B7 pond / B36 landing + `EnsureSafeSpawn` / the `SurfacePlayer` open-air scan), so the player free-falls; and
  the loading overlay's **dismiss condition is "player settles onto solid ground"** (see B34) — which **never fires
  while they're falling in the void**, so the overlay hangs forever. *Investigate:* the new-world spawn-Y
  resolution (does it scan for the surface / clamp onto terrain before handing control over?), `EnsureSafeSpawn`,
  and the overlay-dismiss watchdog. *Fix ideas:* (1) make spawn **always** resolve to a solid surface cell for the
  destination (scan down/up from the spawn column once its chunks are meshed, like the landing-zone + SurfacePlayer
  logic), and (2) give the loading overlay a **timeout / fallback** so it can never hang indefinitely (e.g. after N
  seconds without a settle, force a safe re-spawn or surface the player) — a softlock should never be reachable.
  Medium; **higher priority than B34** (softlock vs cosmetic). Pairs with B34's overlay-dismiss logic.
  **ROOT FOUND + FIXED 2026-06-08:** the server already (a) streams the **spawn's own chunk nearest-first** so
  ground arrives fast (`StreamChunks`, tested) and (b) runs a **runtime void-rescue** every 1 s + a join-time guard
  (`GameServerSpawnSafety`, tested). The hole was on the **client**: `PlayerController` froze the player at spawn
  until ground streamed in, but after a **20 s timeout it released them UNCONDITIONALLY** — so when a fresh world's
  spawn chunk was slow, the freeze ended with **nothing under them → an endless fall**, and the overlay
  (dismissed by that same release) revealed an empty void. Fix (client): the settle-freeze now **never drops the
  player into the void** — it **reveals the world** (dismisses the overlay) after ≤8 s so the screen never
  overstays, but **holds the player at spawn until there is real ground below**; only as an absolute last resort
  (30 s) does it release, and then the server's runtime void-rescue immediately teleports them back to safe ground.
  So a slow/never-arriving chunk can no longer become either an endless fall or a frozen softlock. Server tests
  cover the safety net (`RuntimeRescue_PullsAPlummetingPlayer_BackToSafeGround`, `JoinGuard_…`,
  `StreamChunks_SendsThePlayersOwnChunkFirst_…`); the settle change is client-side. **Playtest:** start several new
  games / land on fresh worlds and confirm you always settle onto the surface (never fall into space / hang).
- **B40 — "Launch to space" from the ship while already in space replays the planet take-off animation; and the
  planet landing/take-off animation isn't oriented to the planet. [(a) FIXED 2026-06-08; (b) DONE 2026-06-19]**
  Two related
  flight-animation issues. **(a)** When you're **already flying in space**, step **into the ship interior**, and
  pick **"Launch to space"** in the menu, the **planetary take-off animation plays** (ship rising as if leaving a
  surface) — but you came from space, so it should **skip the take-off** and just return to the flight view. Item
  **5e** already added a **`SpaceState.SkipLaunch`** path ("launch from inside the ship no longer plays the
  take-off animation"; `EnterSpace(skipLaunch:true)`) — so this case is either **not taking that path** or it
  **regressed**; *investigate* the ship-interior → launch flow (`SendEnterSpace`/`GameMenu` launch vs.
  `EnterSpace`) and gate the take-off on "was the player actually on a planet surface?", not just "is the ship
  stamped." **(b)** The planet **landing + take-off animations move the ship straight up (take-off) / down
  (landing)**, which is fine — **but the planet may not be *below* the ship** (it can end up **in front of** the
  player), so the up/down motion doesn't read as leaving/approaching the surface. *Fix:* **orient the ship (and/or
  the camera + planet placement) before the animation** so the planet sits **beneath** the ship and the vertical
  motion makes sense (point the descent/ascent along the planet direction). Where: the space-view landing/launch
  sequence in `SpaceView` (the `Phase.Landing` descent + the launch/take-off path) + how the planet body is
  positioned relative to the ship in that view. Medium; (a) is the clearer bug, (b) is a polish/orientation pass.
  - **✅ RESOLVED (2026-06-19):** the **landing** half of (b) was fixed in `a6da709` (fly-toward-planet shrink via
    `BeginLandingFlyAway`); the **take-off** half + the (a) skip-launch flow are now also done — take-off orients
    the planet beneath the ship and the interior→launch path takes the skip-launch route. **B40 fully closed.**
  **(a) FIXED 2026-06-08:** the ship interior is **only ever entered from a space instance** (`EnterShipInterior`
  requires one), so launching to space from there must always skip the take-off. The client already shows the
  **helm** there (skips), but the server's `EnterSpaceIntent` handler (`HandleEnterSpace`) always used
  `skipLaunch=false`. Now `HandleEnterSpace` routes a player who is `_inShipInterior` through `ExitShipToFlight`
  (restores the parked ship position + `skipLaunch:true`) — so **every** path that fires the intent from the
  interior skips the take-off; only a launch from a real planet surface animates. Test:
  `LaunchToSpaceFromShipInterior_TakesTheSkipPath_NotAFreshPlanetLaunch` (380 green). **(b) DONE 2026-06-19
  (was: needs live playtest iteration):** the up/down motion is fine, and framing the **planet beneath the ship** during
  the sequence is a camera/orientation choreography problem I can't tune blind. The home planet *is* placed below
  (`SpaceView` `homePos = (0,-150,-20)`) and the ship moves straight in `_root` Y, but the third-person camera
  looks ~forward at the ship (so the planet sits low/off-frame), and when you **land on a body you flew *to*** (E),
  that body is at its orbit position — **not below** — so the vertical descent doesn't point at it (the reported
  "planet in front"). *Plan when tackled (with playtest):* at the start of `Phase.Landing`/`Launch`, reorient so
  the **actual target body** is directly beneath the ship and pitch the camera down to keep it framed; needs the
  landing-target body position threaded into `UpdateSequence`. Deferred so it can be tuned against the real view.

### Reported-bug batch (2026-06-08, after item 38) — B41–B59
**STATUS 2026-06-08:** ✅ FIXED + built + 392 tests green: **B41a/b, B42, B43, B44, B45, B46, B47, B48, B49/B56,
B50, B51, B52, B53, B54, B57, B59** (commits f2a0cc4, b5c8a82, 2e7e691, 27afca8). ✅ **B55** (per-vendor trade
themes) + **B58** (customisable quick-bar) DONE 2026-06-10 — see the B55+B58 block near the top. ✅ **Feature 40**
(terrain-scanner gadget) DONE 2026-06-10 — see its block near the top. **The B41–B59 batch is fully closed.**
- **B41 — Player-ship hatch sits *lengthwise* inside the ship (not parallel to / at the opening); + suffocated
  inside the ship on an airless planet. [TODO]** Two issues on the player's own landed ship: **(a)** the hatch
  door is oriented wrong — it should sit **at the doorway opening, parallel to it**, but renders **along the ship's
  length** inside the cabin. **(b)** On a planet with **no atmosphere/oxygen**, the player **suffocated inside the
  ship** — the sealed cabin should supply breathable air (oxygen refill / no drain) like a suit-safe space. Where:
  `GameServerShipStructure` (hatch door placement/axis) + the oxygen/suffocation logic vs. "inside ship interior".
- **B42 — Wood (tree trunk / `wood_log`) must not be hand-mineable. [TODO]** Currently `wood_log` can be mined by
  hand; it should require a tool (axe/drill tier). Fix: set its `requiredTool`/`minToolTier` in `blocks.json` so
  bare hands can't harvest it.
- **B43 — Underwater, the deep↔shallow water boundary draws a water *surface* texture at the block edge. [TODO]**
  Where deep (swimmable) water meets shallow (non-swimmable) water, the client mesher draws a **water-surface face
  at the vertical block edge** that shouldn't be there when the player is **underwater**. Fix: in the client
  `ChunkMesher` water-face logic, don't emit the surface face at a water↔water depth seam (only at the true
  air/water top). Client rendering.
- **B44 — Machete needs a 1.5 s cooldown. [TODO]** The melee machete can be swung with no rate limit. Add a
  **1.5 s** attack cooldown (server-side melee cooldown, like the gadget cooldowns).
- **B45 — Can't land on asteroids: "you can only land on a planet or moon." [TODO]** Landing on a (landable)
  asteroid is refused by `HandleTravel`'s body-kind check (only Planet/Moon accepted). Asteroids ARE landable
  (item 24/33) — allow `CelestialKind.Asteroid` (with a `PlanetType`) as a land destination.
- **B46 — Bottomless world: removed a few blocks and fell forever. [TODO]** Worlds need a **depth limit** with an
  **unmineable bottom layer**: **lava** at the very bottom on **planets**, **solid rock** on **asteroids/moons**.
  The last layer must **not be mineable**. Where: `WorldGenerator` (emit a floor/bedrock at min-Y) + mining
  (reject mining the bottom layer / bedrock). Relates to the void-fall self-heal (a real floor prevents the fall).
- **B47 — How does the player open settlement wooden (hinge) doors? [TODO]** Settlement hinge doors should be
  **player-openable**. Check the interact flow: `HandleDoorInteract` (E) toggles hinge doors within `HingeDoorReach`,
  and the client `DoorView.NearestHinge` + the E handler — confirm settlement hinge doors are reachable + the prompt
  shows, and fix if they can't be opened.
- **B48 — Launching from a moon takes off over the *start planet*, not the moon. [TODO]** After landing on a moon
  and launching again, the take-off / flight view shows the **home/start planet** below, not the moon you launched
  from. The launch location (`EnterSpace` `locationId` / `_ship.CurrentLocationId`) isn't the current body. Fix the
  per-player launch-from-current-body so you rise off the **moon** you're on.
- **B49 — Flying the ship into a small (mineable) asteroid: instant destruction, no explosion VFX/sound. [TODO]**
  Ramming a small mineable asteroid with the ship currently **destroys it instantly** and the player **respawns**
  with no effect. **B56 (clarification):** the collision should NOT instantly destroy the ship — it should take
  **shield damage**, or **hull damage** once the shield is gone/absent — only destroyed (with explosion VFX +
  sound) when the hull actually reaches 0. Where: the space collision/damage path (`GameServerSpaceCombat`
  `ApplyShipDamage` / `ShipCollisionMaxDamage`/`Factor` — too high, one-shots) + the ship-loss VFX/audio
  (`DeathFx` flash + a missing explosion sound).
- **B50 — Space stations can still stick inside planets. [TODO]** Despite B29's keep-out, orbital stations still
  sometimes **overlap a planet/large body**. Re-audit station placement vs. body radius + keep-out margin in the
  system layout (`GameServerSpaceStations` / the galaxy body positions).
- **B51 — Some of the player's own ship blocks are still mineable when landed on a planet. [TODO]** `IsShipBlock`
  should protect every stamped hull cell, but some ship blocks can still be mined on a planet. Audit the ship-stamp
  bounds vs. `IsShipBlock` (footprint/height, the energy-door cells, multi-block parts) so no hull cell is mineable.
- **B52 — Inconsistent mining: a material sometimes breaks instantly on one click, then the same material takes
  longer. [TODO]** Mining progress is erratic — the first hit on a block sometimes one-shots it, then an identical
  block needs several hits. Likely the `_miningProgress` accumulator keyed/seeded wrong (stale progress carried to a
  new cell, or the first-hit applies full hardness). Investigate `HandleMine` progress accounting + fix so a block
  of a given hardness always takes the same hits.
- **B53 — Appearance/ship colour tabs show the WRONG preview (character ↔ ship swapped). [TODO]** In the in-game
  menu: **Settings → character/colour** shows a character submenu but **no colour controls**, and the right detail
  view shows the **ship** instead of the character. Conversely **Ship → paint** shows the **ship** in the detail
  view but **also the character**. The avatar-preview rig and the ship-preview rig (`AvatarPreviewRig` /
  `ShipPreviewRig` in `CraftingTechShipUI`) are mixed up / both active on the wrong tab. Fix: the colour/appearance
  tab shows ONLY the avatar (+ colour controls); the ship paint tab shows ONLY the ship.
- **B54 — A lava planet showed no visible lava. [TODO]** A planet typed as volcanic/lava rendered with **no lava**
  visible on the surface. Relates to B19 (lava pools only in low basins below ~`BaseHeight − 0.25·Amplitude`), so a
  high-terrain landing/view can show none. Re-check the lava-world sea-fill (`WorldGenerator.ResolveSeaFluid` +
  the volcanic `lavaAb`/sea level) so a lava planet reliably shows lava (raise the lava sea level / abundance, or
  guarantee a visible lava basin near a landing pad).
- **B55 — Station vendors all sell the SAME goods / offer the same trade. [TODO]** Multiple traders on one station
  offer identical stock. Each vendor should have its **own** (deterministic but varied) inventory/offers. Where:
  vendor/market stock generation (`GameServerTrade` / vendor inventory seeding) — seed per-vendor, not per-station.
- **B57 — "Einstellungen" + "Schließen" don't fit their button frames in the in-game player menu. [TODO]** Two
  button labels overflow the button graphic in the in-game menu (German, longer words). Like B28 (tab overflow) but
  for these menu buttons. Fix: size the button / shrink-to-fit the label so the text fits (`GameMenu`/`UiKit`
  button sizing).
- **B58 — Can't customise the hotbar/quick-bar (add/remove items). [TODO]** The on-foot quick-bar shows fixed
  slots; the player can't choose what goes in it. Add a way to move items into/out of the quick-bar slots (e.g.
  drag from the inventory, or an assign action) so the player controls their loadout. Where: the inventory/hotbar
  UI (`HudUi` hotbar + the inventory panel) + the slot model. Medium (client UI + maybe a server slot-swap intent).
- **B59 — Deleting a singleplayer world from the save picker does nothing. [TODO]** In the main menu's
  singleplayer world list, clicking the ✕ on a world opens a Delete/Cancel dialog, but **Delete does nothing** —
  the world isn't removed. Where: the world-picker delete handler (`AppShell`/the save-list UI → the delete
  confirm button's action + the actual save-folder delete).
- **Feature 40 — Terrain scanner gadget (pulse reveals nearby valuable ores, incl. underground). [TODO — analysis
  first.]** A new equipment/gadget item: emits an energy pulse around the player and **highlights valuable
  materials**, including **underground** ones. For a few seconds the surrounding terrain at the trigger point goes
  briefly **"transparent"** and only the **valuable blocks in line of sight stay visible** for the effect's
  duration. Like the item-36 gadgets (tool kind `gadget`, cooldown + suit energy) + an OpenAI icon + ElevenLabs
  sound + a client VFX (an x-ray/see-through pass that fades terrain and keeps ore blocks opaque/glowing). Needs:
  the gadget item/recipe/blueprint, a server `UseGadget` branch (or a client-only reveal driven by the local chunk
  data), and the client x-ray VFX. Medium — the see-through terrain effect is the novel client piece.
  scanned**, **no texture**. **User's read (2026-06-07): it's a creature, not lava** — lava wouldn't spawn as a
  lone two-block thing. So most likely a **hostile fauna creature** rendered **red** (hostile tint) with a
  **failed/missing hide texture** (`CreatureBuilder.PickHide` returned null → untextured red material) and a
  **minimal body** (e.g. ~2 stacked cubes: body + head, legs=0). The "can't scan" is the real puzzle — creatures
  *should* be scannable (`GameServerScanning`), so either the scan missed it (range/aim) or this entity isn't a
  normal creature. **When tackled:** make `PickHide` never return null (always fall back to a real hide so a
  creature can't render as bare red cubes), give a sane minimal body, and check why the scan didn't catch it.
Features: B7/B11. Rendering: B6/B8/B17/B20. B15/B19 need an in-engine look; B21 is the damage-feedback audit.)*

## 📋 More feature requests — 2026-06-07 (backlog only, analysis-first, not started)
29. **In-game integration of the editors (load player-made ships/stations/settlements + content). [✅ DONE]**
   *(Resolved 2026-06-19: editor outputs now feed worldgen/content — usercontent auto-load + pack picker +
   `StructureTemplate` Weight/Pack so player-designed stations/settlements stamp into systems; player material/
   tech definitions load as content. See the "Editors→worldgen + dev-editor labelling" work, 2026-06-15.)*
   *(Original analysis kept below.)* *(Analysis + effort estimate first.)* The ship editor, **space-station / city / village (structure) editor**, **material
   editor**, **blueprint editor** and **ship-parts editor** exist as standalone tools — can their **outputs be
   loaded into an actual game / own server / singleplayer** so players *use* what they built? **Assess the effort
   per editor:** where each editor saves its design, what format, and what the server/worldgen would need to
   ingest it (stamp a player-designed station/settlement into a system; register a player-designed ship as
   craftable; load player material/blueprint/ship-part definitions as content). Likely needs a persistence/import
   path + a content-merge layer. *Estimate complexity per editor + a staged plan.*
30. **Harvestable water + lava, flowing placement, and a fire system. [✅ DONE 2026-06-08 — playtest]** *(Analysis first.)* **Partly exists:**
   `water`/`lava` blocks are `mineable` (drill **tier 3**) and placing a fluid flows (`RegisterFluidSource`). The
   request: make water **harvestable** (confirm it works / lower the tool gate?) and when **placed it flows**;
   make **lava** harvestable too, and when **placed it ignites the surroundings** — the **flora catches fire**
   (needs a **new fire system**: fire spreads across flammable blocks, a **visual flame effect + sound**, burns
   them away); and **water extinguishes fire**. New systems: a fire/burning simulation (server) + flame VFX/SFX
   (client) + flammability per block + water↔fire interaction. Medium-large.
   **Plan (fire system) 2026-06-07:** model fire as a transient **`fire` block** (server-simulated, broadcast via
   the existing `BlockChanged`, client renders it emissive + alpha + non-colliding — like a fluid). A new
   `GameServerFire` cellular tick mirrors `GameServerFluids` (per-world state in `WorldData`, ~6 Hz, per-tick
   budget): a burning flammable block becomes `fire` for a short duration then turns away; each tick it ignites
   flammable neighbours (spread) and is extinguished if a **water** neighbour touches it. **Lava ignites**: the
   fluid tick, when it processes an (active, i.e. placed/flowing) lava cell, ignites its flammable neighbours.
   Flammable = `flora_*` + `wood_log` + `tree_leaves`. Fire damages a player standing in it (like lava). Bounded
   by the budget + active-set dormancy (existing lava seas don't ignite unless flowing). Client: `ChunkMesher`
   treats `fire` like a see-through, non-collidable, glowing block; a fire-crackle ambience near fire. No new
   message/NetCodec. Tests mirror `FluidTests`.
   **[FIXED 2026-06-07 — user chose controlled→ash]** New blocks `fire` (non-solid, non-mineable, emissive,
   alpha-blended) + `ash` (charred remains) appended to blocks.json (+ bilingual names). New `GameServerFire`
   partial: per-world `FireTimer`/`ActiveFire` state, ~6 Hz tick, 300-cell budget; a flammable block
   (`flora_*`/`wood_log`/`tree_leaves`) set alight becomes `fire` for ~3.5 s, igniting flammable neighbours, then
   collapses to `ash`; a `water` neighbour douses it to air. The fluid tick ignites the flammable neighbours of
   any active/flowing **lava** cell (placed/harvested lava → place near plants → fire). Fire burns a player
   standing in it (10 hp/s, armour-reduced). Client: `ChunkMesher` renders `fire` see-through + non-collidable +
   emissive (atlas tile from a generated **transparent flame texture** — alpha baked from brightness so flames
   show on transparent gaps); `ash` is a solid charred tile; a **fire-crackle loop** (ElevenLabs) plays near
   fire via the fluid-proximity ambience. 5 `FireTests` (ignite→ash, spread, water-douse, lava-ignite); 373
   green; client build verified. *(Water/lava are already mineable (tier-3 drill) + flow on placement — the
   item-30 "harvestable + flowing" parts pre-existed.)*
31. **Player-created missions (a real player-to-player mission board). [✅ DONE 2026-06-08 — playtest]** *(Analysis first.)* A player can **post a
   mission** others accept from a mission board: the poster types a **name + description**, **stakes a reward**
   (an item/material they give up) and defines **what the completer receives**; on success the **poster is
   notified** and gets back a **multiple of their stake**. The poster sets the **objectives** — e.g. *mine N of a
   material*, *travel to a place* — with **multi-select** (several objectives per mission). Builds on the existing
   mission board + `GameServerMissions`/`StoredMission` persistence (admin/player-created missions already persist)
   + the NPC-memory/relationship hooks. Needs: a post-mission UI, objective tracking + verification, the
   stake/escrow + payout, and cross-player notification. Big-ish.
   **Analysis 2026-06-07 — most of the SERVER already exists:** `CreateMissionIntent` (NetCodec 13) →
   `GameServerMissions.HandleCreateMission` makes a `MissionDefinition{Source=Player, CreatorId, Objectives,
   Rewards}`, **escrows the reward** into a depot `StoredContainer`, persists via `_repo.SaveMission`; `Accept`/
   `TurnIn` verify objectives (`OnBlockMined` tracks **Mine**; Collect/Deliver checked at turn-in) and pay the
   completer from the depot (`PayoutDepot`). **Missing:** (1) a **client UI to POST a mission** (the form — title,
   description, objectives, reward stake); (2) **notify the poster** when completed (+ the "multiple of stake"
   payout); (3) **Travel** objective tracking (only Mine is hooked). Plan: build the post-mission form in the
   Missions tab (a "+ New" view), add a poster-notification (`ServerMessage`/a small notice) + the chosen payout
   on turn-in, and hook `HandleTravel`/landing for Travel objectives. Reuses the existing messages — no NetCodec
   change for posting.
   **[DONE 2026-06-08 — "Einsatz mit Multiplikator" payout.]** Shipped the three missing pieces:
   (1) **Post-mission form** — a new "**Post a mission**" entry in the Missions sidebar (`CraftingTechShipUI.
   BuildMissionForm`): title + optional description inputs, an **objectives builder** (cycle type Mine/Collect/
   Deliver × target material × count, **+ Add** to stage several, ✕ to remove) and a **stake** (reward item +
   count); **Post** sends `CreateMissionIntent` (`NetworkClient.SendCreateMission`). (2) **Poster payout +
   notice** — `GameServerMissions.RewardMissionPoster` pays the poster **1.5× their staked reward** on turn-in
   (rounded, online posters only) and sends a `ServerMessage` notice; **self-completion earns no bonus**
   (`CreatorId == completer` guard) so you can't mint items off your own mission. (3) **Travel objective
   tracking** — `OnPlayerTravelled` (hooked after `ActiveLocationNames()` on arrival) completes any matching
   **Travel** objective; `HandleCreateMission` now accepts Travel objectives. Tests: existing
   `PlayerCreatedMission_…` (escrow + self-turn-in, implicitly the no-self-bonus guard) +
   `PlayerCreatedTravelMission_ProgressesWhenPlayerArrives`. **Playtest:** post a mission from the Missions tab,
   have a second player complete it, confirm the poster gets 1.5× back + the notice.
32. **Choose a ship hull colour (tints the hull texture), changeable in the in-game menu. [DONE 2026-06-08]**
   *(Analysis first.)*
   Let the player pick a **colour** that **tints the ship's hull texture**, set from the **in-game menu**. The
   ship already renders its hull with a textured material (`SpaceView.BuildShip` + the remote avatars use `LitTex`
   with the `iron_wall` hull texture — item 5b); tinting = multiply a per-player **HullColor** into that material
   (and the on-planet/station stamped ship + remote ships, for consistency). Needs: a `HullColor` on the ship/
   player state (persisted + networked), a colour picker in the ship tab of the menu, and applying the tint
   everywhere the ship is built (flight view, interior, remote avatars). Small-medium, mostly client + one state
   field. *(Pairs with the existing avatar colour customization.)*
   **ANALYSIS + PLAN 2026-06-08 (user chose: Ship tab WITH a live ship preview).** Mirrors the avatar-colour
   pipeline. The flight ship (`SpaceView.BuildShip`) and remote ships (`BuildRemoteAvatar`) already build their
   hull with `LitTex("iron_wall", tint)` — so tinting = set that tint to the player's hull colour. Remote ships
   render in **space** from `NetSpacePlayer` (no colour field yet); the voxel planet/interior ship is mesher-based
   and **out of scope** for this small pass (noted as a follow-up). Pieces: (1) `ClientSettings.HullColor`
   (persisted client-side like the avatar colours) + `GameBootstrap.HullRgb` (set from settings in `WorldRig`);
   (2) network it — add `Hull` to `SetAppearanceIntent` + `NetSpacePlayer`, extend `SendAppearance`, store
   `PlayerSession.HullColor`, populate it in `OtherPlayersInSpace` (MessagePack is contractless, so new fields
   need no codec work); (3) `GameMenu.CycleHull` + `ApplyAppearance` sends the 5th colour; (4) a new
   `ShipPreviewRig` (mirrors `AvatarPreviewRig` — a textured rotating ship rendered to a RenderTexture, with
   `SetHullColor`); (5) Ship tab gets a **"paint"** sidebar category → a hull-colour swatch + cycle in the list and
   the live ship preview in the detail pane; (6) `SpaceView` tints the local ship (`_hullMat`) + remote ships from
   the hull colour, re-tinting live when it changes. Default hull `0xD1D6E0` = today's tint, so unchanged ships
   look identical. Bilingual `ui.ship.cat_paint` / `ui.ship.hull_color`.
   **SHIPPED 2026-06-08 exactly as planned.** New Ship-tab **"Paint"** category: a hull-colour swatch (cycles the
   shared palette via `GameMenu.CycleHull`) + a **live rotating ship preview** (`ShipPreviewRig`, mirrors
   `AvatarPreviewRig`). The colour persists in `ClientSettings.HullColor`, flows through `GameBootstrap.HullRgb` +
   `SetAppearanceIntent.Hull` → `PlayerSession.HullColor` → `NetSpacePlayer.Hull`, and tints the local flight ship
   (live re-tint mid-flight) and other players' ships in space. 377 tests green (locale parity); client built.
   **Out of scope (follow-up):** the **voxel** stamped ship you walk on (planet/interior) stays the default hull —
   per-player tinting of mesher blocks is a separate, bigger change. **Playtest:** Ship tab → Paint → cycle the
   colour, watch the preview, then fly and confirm the ship + (multiplayer) other players' ships show it.
33. **Cratered terrain for airless moons + landable asteroids. [✅ DONE 2026-06-08 — playtest]** *(Analysis first.)* On **moons without an
   atmosphere** and on the **landable asteroids** (item 24 — the big ones you land on; never the mini mineable
   rocks) — both always airless — add a **frequently-used crater landscape**: mostly **flat** ground pocked with
   **many round craters**. Adjust the **terrain generator** accordingly (a crater-field height function — e.g.
   sum of inverted radial bumps / worley-style pits — selected for airless moons + asteroid bodies, blended with
   the existing terrain). Where: `WorldGenerator` surface-height + the per-planet terrain params (the `asteroid`
   planet type + a moon-airless variant). Medium.
   **SHIPPED 2026-06-08.** `SurfaceHeight` branches for cratered worlds: a near-flat regolith base (0.30×
   amplitude undulation, no archetype hills/ridges) with `CraterCarve` on top — a seam-safe FBM mask (the B7
   pond-mask approach, so it wraps the X seam) carving smooth bowls (to −7) each ringed by a raised ejecta rim
   (+2). Selected two ways: a data flag `PlanetType.Cratered` (set on the **`asteroid`** type → landable
   asteroids, craters everywhere incl. standalone queries) **and** a per-world `SetCratered` the server flips for
   **airless moons** (`Kind==Moon && atmosphere=="none"`) at `LoadWorld` (beside `SetCircumference`), so airless
   *planets* stay normal — only moons + asteroids get craters. **User add-ons:** *some* (not all) craters carry
   a few small clumps of **rare metal** on their deeper floors — `CraterFloorMetal` gates whole craters with a
   coarse region mask, then scatters tiny clumps (titanium/gold/platinum/cobalt/uranium/tungsten/neodymium) on the
   top two cells. Test: `CrateredWorld_FlatRegolithWithPits_AndRareMetalInSomeCraters` (flat-with-pits + sparse
   metal); 385 green; client built. Playtest: land on an asteroid / airless moon — flat cratered ground, dig the
   odd crater floor for metal.
34. **Precipitation by climate — fire/ash rain on very hot worlds (and snow on cold). [DONE 2026-06-07 — folded
   into B11 Stage 2.]** Resolved as part of the weather pass: server `PrecipitationFor(weather, temp)` picks the
   form from the per-world temperature (B11) + surface block — **ash** ≥55 °C (lava worlds), **hail** ≤-15 °C,
   **snow** ≤2 °C, **sandstorm** on sand surfaces, else **rain**. Client renders all five in `WeatherFx3D` with
   tinted fog + screen wash, and `ClientAudio` plays a matching bed. See the B11 Stage 2 note for details.
35. **Energy door — an automatic sliding door with a passable blue force-field in the opening; use it as the
   ship's outer door. [✅ DONE 2026-06-08 — playtest]** *(Analysis first.)* A **new door type**
   that behaves like the existing **automatic slide door** (auto-open/close on proximity) but, while **open**,
   shows a **transparent blue energy field filling the doorway** that the **player can walk through** (passable —
   it's a visual/atmospheric membrane, not a barrier). Then **replace the starter ship's outer hatch with this
   energy door**. *Pointers:* doors are server entities over **air** cells (the client mesher renders+collides
   every non-air block, so passable cells stay air + a server door entity — see the door system; `DoorSnapshots`
   `Kind == "slide"`). The blue field can reuse the existing **`force_field`** look (already in `IsTransparent`)
   but must stay **non-solid/passable** (solidity ≠ transparency — glass is solid+transparent, this is
   non-solid+transparent); render it only in the **open** state (a thin emissive-blue alpha plane in the opening),
   not when closed. The ship hatch is stamped in `StampShip` (the 2-wide gap in the `-Z` wall). **Bundle the
   related fix:** the **hatch isn't centred** on the starter ship — the gap is at `x == cx || x == cx-1` (offset
   half a block), so **centre it**, widening the ship by one if needed for an even/odd-symmetric doorway, and keep
   `RegisterDoors`/the interior clear. Needs: a door-kind/flag for "energy" + the open-state field render
   (client), the hatch re-centre + hull width tweak (server `StampShip`), and bilingual names. Medium.
   **SHIPPED 2026-06-08:** new door kind **`"energy"`** — server treats it exactly like a slide door for
   auto-open/close (`GameServerDoors` open tick now matches `slide` **or** `energy`); the **ship's outer hatch** is
   now registered as `"energy"` (`RegisterDoors`). Client `DoorView`: an energy door builds the normal cyan slide
   panels **plus** a translucent blue **energy field** quad filling the opening (a thin cube on the door pivot,
   no collider → passable) whose alpha **fades in with the open amount** + a faint shimmer; reuses the
   always-included `Spacecraft/Cloud` alpha shader (no texture → a solid tinted quad, can't strip to pink). The
   field shows only while open; the door's own collider still blocks while closed. **Hatch centred (bundled fix):**
   the box ship's hatch is now a **3-wide** gap `cx-1..cx+1` (was 2-wide `cx-1..cx`, half a block off) — symmetric
   about the hull centre `cx+0.5`, no hull-width change needed; the threshold pad + door marker were moved to
   match. No new blocks/items, so no locale change (it's ship structure, not craftable). Tests:
   `ShipHatch_IsAnEnergyDoor_CentredOnTheHull` + updated the two hatch tests to expect `"energy"`; 379 green.
   **Note:** designed (editor) ships' doors were already all converted to slide on stamp, so they become energy
   too (a consistent sci-fi look). **Playtest:** walk up to the ship hatch — it slides open with a blue field you
   can walk through, and sits centred on the rear wall.
36. **New gadget items + ship systems: medpack, stasis projector, terrain blaster (with their own sounds,
   textures and visual effects). [✅ DONE 2026-06-08 — playtest]** *(Analysis first.)* Three new
   craftable gadgets: **(a) Medpack** — heal **yourself and other players** (a use-on-self + aim-at-ally heal, a
   chunk of HP, consumable or charged). **(b) Stasis projector** — a beam that **briefly "freezes" a creature**
   (suspends its movement/AI for a few seconds) so you can **scan it safely** at close range. **(c) Terrain
   blaster** — destroys a **large volume of terrain** in one shot (a radius/sphere of blocks → air). For **each**:
   an item def + **blueprint unlock** + craft recipe (`items.json`, the blueprint/unlock system), server handling
   (heal players; pause a creature's AI/aggro timer in `GameServerCreatures`; bulk-`SetBlock`→air that **broadcasts
   `BlockChanged`** for every changed cell — see the block-broadcast rule — and respects protections/landing
   zones), client use (aim + fire), **OpenAI textures/icons**, **ElevenLabs sounds**, and **visual effects** (heal
   sparkle, stasis shimmer/blue tint on the frozen creature, blaster shockwave + debris). Asset gen is pre-approved
   (keys in `tools/ai-assets/.env`, run via `uv`). Big — best split into three sub-items (one gadget at a time),
   each its own pass (item + assets + VFX + tests). *(Stasis pairs with the scan system; the blaster must honour
   the same protection checks as mining so it can't grief towns/other players' zones.)*
   **SHIPPED 2026-06-08 (3 commits, design via AskUserQuestion).** All three are **reusable, blueprint-gated
   gadgets** that right-click at the aim point, cost **suit energy** with a per-player **cooldown** (a shared
   `ToolKind.Gadget` + `UseGadgetIntent` + `HandleUseGadget` framework; the medpack decision: the existing basic
   medpack stays a self-heal consumable, this is the separate area-heal gadget). **(a) Field medkit** — heals you
   + every on-foot ally within 6 blocks (+45 HP), green pulse VFX + heal chime. **(b) Stasis projector** — freezes
   creatures within 7 blocks for 6 s (no movement, no biting → safe to scan); icy-blue stasis shell + `NetCreature.
   Frozen`; cyan burst VFX. **(c) Terrain blaster** — clears a radius-3 sphere of terrain to air **with no loot**
   (chosen for balance — not a super-miner), honouring ship/settlement/station/landing-zone protection + leaving
   indestructible blocks, broadcasting `BlockChanged` per cell + waking fluids; orange detonation + debris VFX.
   Each has an **OpenAI icon**, an **ElevenLabs sound**, a handheld emitter held-model, DE/EN locale, and a test
   (`FieldMedkit_…`, `StasisProjector_…`, `TerrainBlaster_…`); 384 green. *VFX are local-to-user for now;
   multiplayer-visible gadget flashes could be a follow-up.* Playtest: unlock + craft each at a workshop, then
   right-click — heal a teammate, freeze + scan a creature, blast a crater.
37. **Craftable radio beacon — a placeable block that appears as a labelled point on the map + minimap.**
   **[✅ DONE 2026-06-08 — playtest]** *(Analysis first. Backlog only — not started, requested 2026-06-08.)* A **craftable, placeable block** the
   player drops on a planet; once placed it shows up on the **world map** and the **minimap** as **its own point**
   with a **player-typed, freely-editable label** (a personal waypoint/landmark, e.g. "Eisensee", "Basis 1"). It is
   **unlocked as a blueprint first** (not available from the start) and has **OpenAI-generated textures**. Needs: a
   new block (`blocks.json`) + its blueprint/craft recipe + unlock; a **server-tracked beacon entity** (position +
   label + owner, **persisted per world**, networked to clients) created on place and removed on mine; a small
   **label-entry UI** when placing (type the name); **map + minimap rendering** of beacons (a distinct marker +
   the label, like the existing station/landing markers); and the **OpenAI texture**. Bilingual UI strings.
   Medium — touches blocks/blueprints, a new persisted entity + message, and the map/minimap client layers.

   **[ANALYSIS DONE 2026-06-08 — plan below, mirrors the door entity pattern]**
   - **Reuse:** the **door** entity is the exact template — a server-tracked, per-world-persisted, networked entity
     created in `HandlePlace` and removed in `HandleMine` (`GameServerDoors.cs`, `StoredDoor` via
     `IWorldRepository`/SQLite, `DoorList` over NetCodec). Beacons copy this wholesale.
   - **Data:** append `radio_beacon` to `blocks.json` (drops `radio_beacon`), `items.json`
     (`placesBlock: radio_beacon`), `recipes.json` (workshop, `requiredBlueprint: radio_beacon`),
     `blueprints.json` (category Tools). OpenAI **block** texture via `gen_textures.py` + **item** icon via
     `gen_item_icons.py`. 5 locale keys each in en/de (block/item name+desc, blueprint name+desc).
   - **Server:** new `GameServerBeacons.cs` partial — `ServerBeacon { Id, Pos, Label, OwnerId }`,
     `LoadedWorld.Beacons` + `NextBeaconId`, `StoredBeacon` DTO + `SaveBeacon/ListBeacons/DeleteBeacon` on the repo,
     `PlaceBeacon`/`RemovePlayerBeaconAt`/`LoadBeacons`/`BroadcastBeacons`/`SendBeacons`. Hook `HandlePlace`
     (`blockDef.Key == "radio_beacon"`) + `HandleMine`. Beacon block stays an **air cell + entity** (like doors) so
     the label travels with the entity, not the voxel.
   - **Networking:** `NetBeacon { Id, X, Y, Z, Label, OwnerId }` + `BeaconList`; register a free NetCodec tag;
     a `SetBeaconLabelIntent { Id, Label }` (rename) registered too. Sent on join + on change.
   - **Client:** label-entry overlay on place (uses `UiKit.AddInput`) → sends label with the place (new
     `PlaceBlockIntent.Label` field, free on MessagePack) ; rename via interact (E) on an owned beacon.
     `GameBootstrap` stores `Beacons`; `WorldMap.cs` draws a beacon glyph + label (extend `PoiLook`); `HudUi.cs`
     compass gets beacon blips.
   - **Tests:** `BeaconTests.cs` mirroring `PlaceableDoorTests` — place→persist→reload→mine lifecycle, label set,
     locale parity.
   - **Open design questions (asking before impl):** (a) is the label **re-editable** after placement, or only typed
     once at placement? (b) are beacon map markers **visible to everyone** in the world, or **owner-only** (personal)?
   **[SHIPPED 2026-06-08]** Chosen: **re-editable** (type at placement + rename later) and **visible to everyone**
   (owner stored; only the owner renames). Implemented exactly to the door-entity template: new `radio_beacon`
   block/item/recipe (workshop, `requiredBlueprint`)/blueprint (Tools) + OpenAI block texture + item icon + 5 DE/EN
   locale keys each (+ `ui.beacon.*` overlay strings). Server: `GameServerBeacons.cs` (`ServerBeacon`,
   place/remove/load/broadcast/rename, owner-gated + reach-checked), `StoredBeacon` table + `Save/List/DeleteBeacon`,
   `LoadedWorld.Beacons`/`NextBeaconId`; hooked into `HandlePlace`/`BreakBlockAt` (covers area-mine) + the terrain
   blaster cleanup + join/world-init sends. The beacon is a **real voxel block** (mesher draws it + the block edit
   persists) plus a metadata entity for the label/owner. Networking: `NetBeacon`/`BeaconList` (tag 100),
   `SetBeaconLabelIntent` (tag 101), `PlaceBlockIntent.Label`. Client: a modal `BeaconLabelUi` (name at placement /
   E-rename your own), markers + names on the world map (`WorldMap`), amber blips on the HUD compass (`HudUi`), and a
   floating world-space name above the block (`BeaconView`). 4 `BeaconTests` (place/persist/reload/mine/owner-rename);
   **389 tests green** (locale parity included); client rebuilt. Playtest: unlock + craft at a workshop, place →
   name it → see it on M + compass + above the block; walk up + E to rename; mine to take it back.
38. **Fixed, pre-planned landing zones (capacity-limited, no-build, ship-size aware).** **[✅ DONE 2026-06-08 —
   playtest]** *(ANALYSIS FIRST — ask clarifying questions before any implementation. Backlog only — not started, requested 2026-06-08.)* Planets,
   moons and **landable asteroids** should have a set of **fixed landing zones for ships**, **baked into the
   generated map** (deterministic positions, not the current dynamic march): a player always lands on one of these
   pre-planned pads. The **number of zones varies with map size** (a small asteroid has few, a big planet many).
   **No building** is allowed on a landing zone (reserve the pad). In **multiplayer**, when a body is **full** (all
   its zones occupied) that must be **shown in-game** (e.g. on the star map / when trying to land — "all landing
   zones taken"). The design **must account for variable ship sizes** — it should be possible to own **bigger ships
   than the starter**, so a zone needs enough clearance for the largest ship (or zones come in sizes / a big ship
   needs a big pad). *Builds on what exists:* there is already a per-player `LandingZone` (`GameServerSpace.
   EnsureLandingZone`) with **protection** (others can't mine/build in it — `IsLandingZoneBlockedForOther`) and the
   B36 **dry-land + settlement-clear + collision-free** search; this feature turns that **dynamic** allocation into
   a **fixed, map-planned set with a capacity limit + a "full" signal + ship-size clearance**, and extends the
   no-build rule to *everyone* on the pad. *Open questions to raise at planning:* how many zones per size class +
   how spaced; are zones visible markers on the surface/map; what happens when full (refuse landing / queue / send
   elsewhere); do zones have size tiers for big vs small ships, or one generous size; how this interacts with the
   existing settlement/station placement + the persisted per-player zones (migration); single-player behaviour
   (always a free pad?). Medium-large — worldgen planning + server allocation/capacity + UI for "full" + ship-size
   handling. *(Relates to the bigger-ships goal and to item 20 "build in space / player station".)*

   **[ANALYSIS DONE 2026-06-08 — plan below; asking 4 design questions before impl]**
   - **As-is:** `EnsureLandingZone(playerId)` (`GameServerSpace.cs`) is **dynamic** — marches +X at `LandingZoneSpacing=24`
     from `baseIndex = _landingZones.Count`, prefers dry land (B36 `LandingFootprintWet`), and persists **one zone per
     `(player, body)` forever** (`landing_zone` table, key `player_id+location_id`). `LandingZone { PlayerId, LocationId,
     CenterX, CenterZ, Radius=8, Protected }`. Protection (`IsLandingZoneBlockedForOther`) blocks only **other** players.
     Landing: client `SpaceView` E → `SendLeaveSpace(bodyId)` → `HandleLeaveSpace`/`HandleTravel` → `StampShip` at the
     zone centre + spawn. Star map `NetBody` has **no** occupancy. Settlement/wreck anchor at the **first** zone (+48,+48 /
     −56,+56). Largest ship = `hauler` 7×9 (halfX3/halfZ4); the current radius-8 pad (17×17) already clears it.
   - **Plan:** turn the dynamic march into a **deterministic, map-planned pad set per body**:
     1. `LandingPads(body)` helper: from `seed ^ StableHash("landingpad:"+key)` + circumference, place **N pads** at
        deterministic equator-band longitudes, each nudged deterministically to dry, settlement/wreck-clear ground.
        **N by size class** (asteroid 1–2, moon 2–4, planet 4–8; scaled by circumference). **One generous pad size**
        (radius 8 — already clears the hauler; no size tiers).
     2. **Live occupancy** (proposed): a pad is held by whoever is on the body, released when they leave for space/another
        world; `LoadedWorld` keeps `pad→playerId`. Replace `EnsureLandingZone` with `AssignPad(player)` (free-pad finder).
     3. **Full signal:** no free pad on a land attempt → `Reject("land", "all landing zones taken")`; `NetBody` gains
        `PadsTotal`/`PadsFree` so the star map + land prompt show occupancy / "FULL".
     4. **No-build for everyone:** a pad-area check that blocks **all** players (not just others) from mining/placing on a
        pad (reserve it); the assigned ship still stamps there.
     5. **Re-anchor** settlement/wreck offsets to deterministic **pad[0]** (removes the player-dependency).
     6. **Migration:** old per-player `landing_zone` rows are superseded by deterministic pads — ignore/clear them on load
        for bodies using fixed pads.
     7. **Client:** star map per-body occupancy + "FULL"; world-map/compass pad markers; optional visible pad platform.
     8. **Tests:** deterministic pad set (same seed→same pads), capacity (N players fill N pads, N+1 refused), release on
        leave, no-build-for-everyone, dry-land pads.
   - **DECISIONS 2026-06-08:** (a) **live occupancy** — a pad is held while the player is on the body, released on
     leave/disconnect (SP never full); (b) **player picks the pad** — landing opens a pad chooser (keyboard 1–N in the
     flight view, no cursor) showing free/occupied; (c) **refuse + message** when full ("all landing zones taken") +
     "FULL"/free-count on the star map; (d) **invisible pads** — no built platform, terrain stays natural, pad shown
     only as a map/compass marker. One generous pad size (radius 8, clears the hauler). **N = a seeded-random count
     per body within its size-class range** (so it varies body to body, deterministically): asteroid 1–2, moon 2–4,
     planet 4–8 — asteroids fewest, moons more, planets most. Messages: `RequestLandingPadsIntent{BodyId}` →
     `LandingPadList{BodyId, Pads[]{Index,X,Z,Occupied,Occupant}}`; `LeaveSpaceIntent`/`TravelIntent` gain `PadIndex`;
     `NetBody` gains `PadsTotal`/`PadsFree`. No-build on a pad applies to **everyone**.
   **[SHIPPED 2026-06-08]** Replaced the dynamic per-player landing zones with deterministic, seeded-random pad
   sets. Server: rewrote `GameServerSpace.cs` — `LandingPad {Index,CenterX,CenterY,CenterZ}`, `PadCountFor`
   (seeded-random per size class: asteroid 1–2, moon 2–4, planet 4–8), `BuildLandingPads` (pad 0 on the prime
   meridian, the rest spread round the body, each nudged to dry land), live occupancy derived from sessions
   (`AssignedPadIndex` + on-body + not-in-space), `TryClaimPad`/`ClaimPadOrReject` (refuse when taken/full),
   `PlayerPad`, `IsOnLandingPad` (reserves only the **landing volume** above the pad — placing blocked for everyone,
   mining + building high above are fine), star-map occupancy via `ToNetBody`. Hooked into `HandleTravel` /
   `HandleLeaveSpace` (same-body relocate) / join / `StampShip` / settlement+wreck anchoring (now pad 0). **Cleanup:**
   deleted the old `EnsureLandingZone` march, `LandingZone` struct, the `landing_zone` table + `Save/ListLandingZone`
   repo methods, and `IsLandingZoneBlockedForOther`. Messages: `RequestLandingPadsIntent`/`LandingPadList`/
   `NetLandingPad` (tags 102/103), `LeaveSpaceIntent`/`TravelIntent.PadIndex`, `NetBody.PadsTotal/Free`. Client: a
   keyboard **pad chooser** in the flight view (E/L → request pads → pick a free pad 1–N, "ALL FULL" when none),
   pad markers on the world map (green free / red taken), and the star-map cards show `⊕ free/total` or `VOLL`.
   **Plus (requested mid-build):** in MP, others on the body now see the landing/launch **animation** of the moving
   player's ship — `ShipTransitFx` (tag 104) broadcast on land (descend) + launch (ascend) to the others present,
   played by a new `ShipTransitView` (procedural hull tinted to the mover's hull colour, eased descent/ascent,
   engine glow + thruster roar). 3 new `LandingPadTests` replace the old zone tests; **392 tests green**; client
   rebuilt. Playtest: fly to a body, press E → pick a pad; fill a small body's pads in MP to see FULL + the refuse;
   watch another player's ship land/launch. *(Star-map "Travel" auto-picks the first free pad; the in-flight E/L is
   the manual chooser.)*
39. ✅ **Document where the ASP admin dashboard is reached (done 2026-06-12).** Investigated + **verified by
   running it**: `BlocksBeyondTheStars.Api` is a standalone ASP.NET Core host that binds
   `http://{adminBindAddress}:{adminPort}` (default **`http://127.0.0.1:31416/`**) from the `config/server.json`
   **next to its own executable** (same resolution as the game server → in a published package both share one
   config; under `dotnet run` each has its own `bin/.../config/`). `/` serves the embedded HTML dashboard
   (status / config editing / backups / log tail / admin missions / content packs); `/api/*` is the JSON API,
   gated by an `X-Admin-Password` header when `adminPassword` is set (no password → loopback-bind reliance +
   a status warning); plus a public `/portal` landing page and the `/play` browser-client placeholder.
   **Documented:** new "Admin dashboard" section in `README.md` + expanded `docs/developer/SELF_HOSTING.md` §5 (start,
   URL, auth, full API route table, curl/Invoke-RestMethod example, dev-mode config caveat). *(Original entry:)*
   *(Docs only — backlog, requested 2026-06-08.)* There is
   an ASP.NET server admin component (`src/Spacecraft.Api`); **find out how/where its admin dashboard is opened**
   (which executable/host serves it, the **URL/port**, any route/auth, and how to launch it) and **make sure that's
   written in the README** (and/or the project docs) so it's discoverable — currently it doesn't appear to be
   documented. *Steps when tackled:* check `Spacecraft.Api` (Program/Startup, launch settings, any dashboard
   page/Razor/Blazor), confirm the real access path by running it, then add a short "Admin dashboard" section to
   `README.md` (URL, how to start, what it's for). Small — investigation + a docs edit.
40. ✅ **Feature — singleplayer world mode at creation: Explorer vs Creative (done 2026-06-09; needs in-engine
   test).** **Decisions (user 2026-06-09):** Creative = a head-start sandbox — **survival mechanics stay ON**
   (oxygen/hunger/material cost; `GameMode` untouched), it just changes what's available; "all-on" toggles (not
   per-item); a curated kit (not a full picker). **UI** ([UiSaveSelect.cs](client/Assets/Spacecraft/Scripts/UiSaveSelect.cs)):
   the new-world panel got a **Mode** selector (Explorer/Kreativ) + 3 Creative checkboxes — *Alle Blaupausen*,
   *Alle Schiffe*, *Kreativ-Startset* (DE/EN locales). Passed via `AppShell.StartSingleplayerWorld` →
   `LocalServerLauncher.Prepare` as `--unlock-all-blueprints/--start-all-ships/--creative-kit` →
   `ServerConfig.ApplyCommandLine`. **Persistence:** the 3 options are baked into `WorldMetadata` on world
   creation (so they survive reloads, sidestepping the fleet-not-persisted limitation). **Server**
   ([GameServer.cs](src/Spacecraft.GameServer/GameServer.cs) `ApplyCreativeGrants`, called on every join): unlock
   every blueprint + own every ship type (idempotent), and grant a curated starter kit (better tools + generous
   stacks of key ores/materials/components/blocks) **once** (`CreativeKitGranted`). Explorer = all off (current
   behaviour). Tests `CreativeWorld_…`, `ExplorerWorld_GrantsNothingExtra`, `CreativeOptions_PersistAcrossRestart…`
   (404 green). *Follow-ups if wanted: a real Creative GameMode toggle (no-cost/no-oxygen), per-category or full
   pickers, a bespoke item picker.*

---

## ⏭ Requested 2026-06-07: six analysis-first tasks (do one at a time)
Workflow for these (per the user): for **each** task — (1) thorough analysis of the current code, (2) write
an **Analysis + Plan** block here **before** any implementation, (3) ask clarifying questions if needed,
(4) only then implement + commit. One task at a time. Asset generation (OpenAI textures / ElevenLabs sounds)
is **pre-approved** (keys in `tools/ai-assets/.env`, run via `uv`).

- ✅ **Task 1 — Swimming / diving + ship landing underwater** (done 2026-06-07). Part 1: the chunk collider
  now excludes fluids so the player swims/dives — buoyant sink, **Jump = rise/surface**, water breaks falls;
  submerging spends the **suit oxygen** even on a breathable world. Part 2: water renders **transparent**
  (alpha submesh, clear-blue tile alpha, no frost) so you see down into seas. Part 3: the fluid sim is
  **ship-aware** (`FluidCanEnter`) — ships land at the seabed (so underwater on water worlds) with a **dry,
  watertight cabin** that water can't flow into. Tests: `Submerged_DrainsSuitOxygen_…`,
  `Fluid_DoesNotFlowIntoAShipInterior`. ✅ **Polish:** a subtle blue full-screen wash while the eye is
  submerged (`WeatherFx.EyeUnderwater` + an IMGUI wash, smoothed, hidden in space/menu).
- ✅ **Task 2 — Walk all the way around a planet** (done 2026-06-07). **Verdict: yes** — the world is a
  cylinder (X = wrapping longitude, Circumference 6000, seam-free noise), a lap ≈ 16 min. Shipped: **Fix 1** —
  `WrapDistanceSquared`/`WrapDistSq` make every on-planet proximity check seam-aware (creatures, doors,
  enemies, NPCs, vendors, containers, trade, ship station, bump) so interactions work across X=0 (space combat
  left alone). **Fix 2** — `WorldConstants.LatitudeLimit` + an invisible **pole barrier** (server clamps Z,
  client wall) so N/S is bounded instead of an infinite strip. **Sizes** — each planet/moon gets a
  deterministic random **size in the orbit view** (`BodySizeScale`); walkable circumference stays 6000.
  Tests: `WrapDistanceSquared_MeasuresProximityAcrossTheSeam`, `WalkingTowardThePole_IsBoundedByTheLatitude-
  Barrier`. *(Future option: true per-world walkable circumference — see Task 2b below.)*

  ### Task 2b — Per-world walkable circumference (requested 2026-06-07, analysis-first)
  **Finding: there is NO per-world walkable size today** — every world is the global `WorldConstants.
  Circumference = 6000`. `PlanetType.WorldRadius` exists but is *informational only* (not wired into gen/wrap);
  the orbit `BodySizeScale` is cosmetic. **Blast radius:** ~23 sites + the 6 static wrap helpers (`WrapX`/
  `WrapDeltaX`/`CanonicalChunkX`/`CanonicalChunk`/`CanonicalBlock`/`WrapDistanceSquared`) + derived
  `ChunksAround`/`LatitudeLimit`, across server (`ServerWorld` block/chunk canonicalisation, `GameServer`
  streaming/move/reach), client (`GameBootstrap.SceneX`/`RepositionChunks`/`DayCircumference`, `ClientWorld`),
  and worldgen (`Noise.FbmCylX`/`ValueCylX` circular domain + flora `WrapX`). **Persistence:** circumference is
  baked into chunk keys (`ChunksAround`) — it must be **immutable per world**, or saved chunks break.

  **Plan (multi-commit, non-breaking for old saves):**
  1. **Shared `WorldGeometry`** — an instance object `{ int Circumference; ChunksAround; LatitudeLimit; WrapX;
     WrapDeltaX; CanonicalChunkX/Chunk/Block; WrapDistanceSquared }`. Keep the old static `WorldConstants`
     helpers (default 6000) as thin wrappers so nothing else breaks mid-refactor. Add `CircumferenceFor(key)`
     — deterministic per body (moons smaller than planets, e.g. moons ~3000–4500, planets ~5000–9000).
  2. **Persist** the chosen circumference in world metadata at creation (default 6000 when absent → old saves
     stay 6000 and keep working); load it immutably.
  3. **Server** threads the world's `WorldGeometry` into `ServerWorld` (block/chunk canonicalisation), the
     `WorldGenerator` (ctor param → noise domain + flora wrap), move/reach/stream, and `LatitudeLimit`.
  4. **Network** — send `Circumference` (+ `LatitudeLimit`) in `WorldEnvironment` (already broadcast on
     join/world-switch).
  5. **Client** caches it into a `WorldGeometry`, uses it for `SceneX`/`RepositionChunks`/`DayCircumference`/
     `ClientWorld` wrap; the **orbit `BodySizeScale` now reflects each body's real circumference** (derive via
     `CircumferenceFor(body.Id)`), so the space-view size ≈ the walkable size.
  6. **Tests** — wrap consistency across several circumferences; a small vs large world differ.

  **Decisions (2026-06-07):** ignore save compatibility (derive deterministically, no metadata persistence);
  round circumference to a multiple of ChunkSize (16). **Three size classes** by body: **asteroid**
  (landable, PlanetType `asteroid`) **800–1600**, **moon** (`CelestialKind.Moon`) **2500–4000**, **planet**
  **5000–12000** — `CircumferenceFor(bodyId, class)` deterministic; `SizeClassFor(kind, planetKey)` in shared
  so server (active world from `_galaxy.FindBody`) + client (orbit from `NetBody`) agree.

  ✅ **DONE 2026-06-07** (3 stages): **Stage 1** (`b21a299`) — `WorldConstants` circumference overloads +
  `WorldSizeClass`/`SizeClassFor`/`CircumferenceFor`. **Stage 2** (`d35a587`) — server sizes each world from
  its body (`LoadWorld` → `SetCircumference` on the generator + `ServerWorld.Circumference`); terrain/caves/
  ore/biomes/flora wrap at it; move-wrap, pole clamp, reach + proximity read the active size; `WorldEnvironment`
  carries Circumference + LatitudeLimit. **Stage 3** — client caches it (`GameBootstrap.Circumference` +
  `ClientWorld._circumference`) for `SceneX`/day span/chunk wrap; the orbit view sizes each body by its real
  circumference (`OrbitDiameterFor`). Tests read `server.World.Circumference`; 318 pass.

  ### Task 2 — Analysis + Plan (2026-06-07)
  **Verdict: circumnavigation already works (W0–W4).** The world is a **cylinder**: X is a wrapping longitude,
  **Circumference = 6000 blocks** (`WorldConstants.cs:22`). Terrain/biomes/caves/ore are seam-free via
  circular-domain noise (`Noise.FbmCylX`/`ValueCylX`), **proven by 10 `WorldWrapTests`** (height/biome/caves/
  ore identical at X=0 ≡ 6000). The server wraps the player's X (`GameServer.cs:1184`); the client renders the
  nearest wrapped copy via `SceneX`, so crossing X=0 has **no visible jump/seam**. Mining/placing wrap
  (`WithinReach` uses `WrapDeltaX`). A full lap ≈ **6000 ÷ 6 m/s ≈ 16–17 min** of straight walking. Latitude
  (Z) is **not** wrapped (you circle the equator, not over the poles).

  **Two gaps:**
  1. **~21 unwrapped `DistanceSquared` proximity checks** break interactions across the seam: `GameServer-
     Creatures.cs` (171/375/391/402), `GameServerDoors.cs:184`, `GameServerSettlements.cs:233`,
     `GameServerTrade.cs:49`, `GameServerEnemies.cs` (71/146), `GameServerContainers.cs:56`,
     `GameServerShipStructure.cs:410`, `GameServerSpaceCombat.cs` (365/562/671/694). An object just across X=0
     reads as ~6000 blocks away. Mining/placing are fine (already wrapped); these AI/interaction checks aren't.
  2. **Poles (W5) not done** — Z is unbounded: you can walk north/south forever into generated terrain with no
     barrier, so the planet doesn't feel bounded N/S.

  **Plan:**
  - **Fix 1 (seam interactions):** add a wrap-aware `WrapDistanceSquared(a, b)` helper (X the short way round,
    plain Y/Z) and replace the unwrapped proximity `DistanceSquared` calls in the listed server systems. Add a
    test that a creature/door/vendor just across the seam is reachable.
  - **Fix 2 (poles, optional):** bound latitude with a **pole barrier** — past a latitude limit the surface
    rises into an impassable ice wall (frozen biome), so walking N/S ends at a wall instead of infinite void.
  - **Planet size:** 6000 blocks ≈ 16 min/lap; can shrink for a faster "around the world" feel if desired.
- **Task 3 — Shadows & darkness on planets.** Analyse how shadow-casting + darkness work. A **cave entrance
  currently looks like a black wall** — change it so the entrance reads as **softly lit**. Shadows should be
  **softer**, not so **hard-edged**.

  ### Task 3 — Analysis + Plan (2026-06-07)
  **There is no real shadow-mapping.** "Shadow"/darkness comes from a per-face **skylight** flag the mesher
  bakes into `TEXCOORD1.x`: `sky = ny > Top(nx,nz) ? 1 : 0` (`ChunkMesher.cs:128`) — 1 if the air cell the face
  looks into is above its column's highest solid (open to the sky), else 0. `BlockAtlas.shader` turns it into
  `amb = lerp(0.24, 0.70, sky)` and `col = albedo*(light*(amb + 0.5*ndl*sky) + 0.05)*faceAo`
  (`BlockAtlas.shader:107-117`). So an open face is ~0.70 + sun; an occluded face (cave/overhang/indoor) is a
  flat **0.24** (+0.05 floor). **Root cause of both symptoms = the binary skylight:** one block into a cave
  jumps 0.70→0.24, so the mouth is an abrupt dark wall and overhang shadows have hard edges. The shader already
  consumes a *continuous* `sky`, so the fix is mesher-side only. (The `ndl` sun term is already smooth; there's
  no sun shadow-casting at all, just the dark side of blocks + the skylight occlusion.)

  **Implications elsewhere:** the same skylight gates ship/station interiors (compensated by `_Sc_Indoor`),
  overhangs, cliff undersides and tree shade — softening it improves all of them, and bleeds a little light
  into doorways/windows (a plus). It does **not** touch night-on-planet (that's the day/night `light` term).

  **Plan (mesher-only):** replace the binary `sky` with a **smooth sky-occlusion** 0..1 — average the
  open-to-sky test (`ny > Top`) over a small horizontal kernel (e.g. 3×3 or 5×5 columns) around the air cell,
  blended with the cell's own openness. A face at a cave mouth (some open neighbours) gets a partial value → a
  soft gradient instead of a wall; a **deep** cave (no open neighbours) stays ~0, so it still needs a lamp.
  `Top()` is column-cached, so this is a few extra dict lookups per drawn face at **mesh time** (not per
  frame). Optionally nudge the cave-ambient floor (0.24) up slightly. No shader change required.

  **Decisions:** only soften+light the **entrance** (deep caves stay dark, lamp required); **strong (5×5)**.
  ✅ **Implemented 2026-06-07:** `ChunkMesher.Skylight(wx,wy,wz)` returns 1 if the cell sees open sky, else the
  fraction of a 5×5 horizontal column-neighbourhood open at that height — a smooth gradient feeding the shader's
  existing `lerp(0.24,0.70,sky)`. `Top()` is column-cached so the extra lookups are mesh-time only. No shader
  change.
- **Task 4 — Appealing icons for everything pickup-able / hand-held. [✅ RESOLVED 2026-06-19 — superseded by the
  generated-icon + uGUI icon passes].** Current icons are crude and off-style.
  Plan: **materials** → a downscaled in-game **texture** (like the harvested-plant icon), generated from game
  content. **Meat** → a steak icon (green if toxic, else normal). **Items + tools** → same style as the in-game
  icons. **Audit which items + materials need an icon, make a list**, then use the **OpenAI** generator to
  create + wire them. Use the icons in the **player menu (crafting)** and on **blueprints**. Also make icons
  for the **space view** (laser, tractor beam) and for **ship upgrades/modules**, and use them in the menu.
- **Task 5 — Crafting + tech-tree + materials overhaul. [✅ RESOLVED 2026-06-19 — superseded by the "Material
  variety overhaul" (dead-ends fixed, new tiers + metal blocks, depth-banding)].** Analyse the crafting/tech tree + existing materials.
  Goal: a **working crafting base that builds up in stages** with real **prerequisites** — some materials are
  gathered, others are **crafted from base materials**. Find inconsistencies. Plan how to expand materials +
  crafting for **player items, ship parts, and ships**. Plan what **kinds of objects** are still needed to
  **build on worlds**. **Expand the metals/materials found on planets** — gold, silver, copper, etc.: take all
  plausible **metals, rare earths, raw resources**. Generate their **textures (OpenAI)** and fold the new
  materials into the crafting logic.
- **Task 6 — Drastically increase flora & fauna variety. [✅ RESOLVED 2026-06-19 — superseded by the "Flora
  variety" overhaul (per-biome themes, archetypes, new flora/leaf blocks) + the species/flora/colour overhaul].**
  Add new **base types** with their **sounds +
  textures** generated immediately (OpenAI textures, ElevenLabs sounds — via the Python tools). Remember some
  flora/fauna can (rarely) serve as a **material substitute**. Generate textures + sounds for the new fauna too.

### Task 1 — Analysis + Plan (2026-06-07) — swimming/diving, transparent water, underwater ship
**Analysis of today's behaviour (file:line):**
- **Player can't swim.** `PlayerController` is a `CharacterController` with simple gravity/jump
  (`Gravity 20`, `JumpSpeed 7`, vertical at `PlayerController.cs:759/763/698`). It has **no water detection**
  for movement. `water` block is `Solid` (no `solid:false` in `data/blocks.json:18`) and the chunk
  **`MeshCollider` uses the whole render mesh** (`GameBootstrap.cs:594/599`), so the player **walks on top of
  water like ground** — no sinking, diving or buoyancy. (Only `ClientAudio.HeadInFluid` samples water, for the
  muffle.)
- **Water is opaque.** `ChunkMesher.IsTransparent` returns true only for `glass`/`force_field`
  (`ChunkMesher.cs:223-231`); water renders in the opaque submesh 0. A `BlockAtlasTransparent` shader exists
  (used by glass). Transparent faces are only drawn toward air (`ChunkMesher.cs:101`).
- **Ship lands on the seabed.** `StampShip` anchors at `SurfaceHeight` (= terrain/seabed) at
  `GameServerShipStructure.cs:55`, so on a water world it is already **underwater**. The interior is stamped to
  air (clears any water there at stamp time). `FillShipFoundation` plugs only **air** cavities below the ship,
  not water. **The fluid sim has zero ship awareness** (`GameServerFluids.cs` TickFluids/Spread/FillFluid only
  test `IsAir`), so woken water can flow through the hatch/gaps into the interior; the hull is protected from
  mining but **not watertight against fluids**.

**Plan (3 parts):**
1. **Swim/dive (client).** Build the chunk **MeshCollider from solid blocks only** (exclude `water`/`lava`),
   so the player falls *into* water instead of standing on it (ChunkMesher emits a collider triangle set that
   skips fluids; `GameBootstrap` assigns it). Add water physics to `PlayerController`: detect submerged (sample
   the water block at the body), replace gravity with gentle buoyant **sink**, **Jump = swim up / surface**, and
   a real jump-out when the head breaches the surface; damp horizontal speed in water. (Dive-deeper control TBD
   — see questions.)
2. **Transparent water (client).** Add `water` to `IsTransparent` → renders in the alpha submesh; give the
   **water tile an alpha (~0.6)** and have `BlockAtlasTransparent` blend by texture alpha (glass stays ~0.85
   milky per the glass memory). Internal water faces already cull (only faces toward air draw), so a sea shows
   its surface + you can see down into it while diving.
3. **Underwater ship, watertight (server).** Make the fluid sim **ship-aware**: never fill/flow into cells
   inside `ShipInteriorContains` (water stops at the doorway plane). Explicitly **clear water/lava in the
   interior** (and a 1-block margin) at stamp; extend `FillShipFoundation` to also replace water/lava under the
   footprint so nothing seeps up. Leave landing at the seabed (so it *can* land underwater) unless the user
   prefers dry-land preference (see questions). Tests: collider excludes fluids; fluid won't enter a stamped
   ship interior; ship interior is water-free after landing in a sea.

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
- Follow-ups noted (für später): aquatic fauna + water flora. (Mineable water/lava — beam + source logic —
  shipped 2026-06-06; see the planned-list ✅ below.)

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
- **Follow-up DONE (2026-06-06):** auto-doors for **orbital stations** (each cut module doorway emits a
  `door_slide` marker → `RegisterStationDoors` on board) and **the box starter ship's hatch** (now a sliding
  door); designed ships already had editor door cells.

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
**[docs/developer/STATION_AS_LOCATION.md](docs/developer/STATION_AS_LOCATION.md)** (`PlanetType.Void` + all-air gen,
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
cross-world MP polish) in **[docs/developer/MULTIWORLD_AND_SYSTEM_FLIGHT.md](docs/developer/MULTIWORLD_AND_SYSTEM_FLIGHT.md)**.
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
**~~Remaining edge (→ P7)~~ — RESOLVED (verified 2026-06-11, note was stale):** ship stamps are per player
per world (`WorldManager.ShipStamps` is keyed by player id) and the per-player ship cursor resolves each
player's own heal-tank/aboard/stamp (`SetCurrent`) — two players on the same planet each keep their own
stamped ship.

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

### Orbital bodies in the planet sky — ✅ DONE 2026-06-11 (scope per user: NO stations; own sky cycles)
Shipped as `SkyBodiesView` (client ambience, wired in WorldRig): the system's other LANDABLE bodies —
moons, neighbour planets, landable asteroids (stations explicitly excluded) — hang in the surface sky as
lit tinted spheres. **Each body follows its own deterministic sky cycle like the sun** (orbit speed as a
fraction/multiple of the local day — asteroids cross fast, planets drift stately — plus a phase + its own
path bearing), hashed from current-planet+body, so every world has a unique, stable sky choreography.
Sized by world class, tinted by planet type, a touch brighter at night, horizon-faded, terrain-occluded;
star map auto-requested after spawn, rebuilt on travel (stale-map guard). Original plan kept below:
- A `SkyBodies` component that, only while on a planet surface (not in space / not boarded), places small
  **lit sphere billboards** for the system's neighbours and small **station icons** for stations orbiting
  the current body, in the sky dome — direction derived from the star map's relative system coords
  (`Game.StarMap`, already client-side), distance scaled to the far plane, following the camera like the
  starfield. Tint/size from each body's planet type; stations read as tiny metallic specks/cross shapes.
- Visible day + night (a touch brighter at night); a very slow drift so they feel like they orbit.
- Phasing: **O1** render neighbours + stations from the star map; **O2** slow orbital drift; **O3** per-type
  look + stations-of-this-body shown nearer/bigger; **O4** optional labels on look/scan.

### Doors — ✅ FULLY shipped (settlements + stations + ship + placeable; verified 2026-06-10)
**Settlement doors done** (D1–D5). **Stations + ship done too** (this section was stale): the station
generator emits `door_slide` markers, `RegisterStationDoors` registers them on boarding, ship hatches are
energy doors from `ShipStamps.Doors`, and the **placeable door** (craftable `door_slide`/`door_hinge` items,
place/remove + persistence, `PlaceableDoorTests`) exists. Original plan kept below for reference:
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

### ✅ Done (2026-06-07): species/flora/colour/naming overhaul
A multi-phase feature requested 2026-06-07 — random per-world flora & fauna species with generated looks +
names, wilder colours, uniform per-planet flora hue, and full OpenAI/ElevenLabs asset coverage. The user chose
**"work the plan in order"** and **gave blanket approval for paid asset generation** (OpenAI textures +
ElevenLabs sounds — no further per-batch confirmation needed; keys are in `tools/ai-assets/.env`, run via `uv`).

- ✅ **Phase 1 — per-system star colour** (done 2026-06-07) — replaced the 5-swatch sun palette with
  `StarColor(system)`: a weighted hot→cool stellar ramp (blue-white→white→yellow→orange→red) blended by a
  second hash, so every system gets a distinct sun tint. Already shared between the planet surface + space +
  station views via `WorldEnvironment.SunColor`; unified the env-null fallback too. (`0d988a9`)
- ✅ **Phase 2 — flora re-tint engine** (done 2026-06-07) — the server picks a **per-planet flora base hue**
  (`FloraColor`: green-dominant palette with rarer brown/pink/purple/amber, deterministic from seed+planet),
  broadcast via `WorldEnvironment.FloraTint`. The mesher tags flora vertices with a flag in `TEXCOORD1.y`
  (`IsFloraBlock` — `flora_*` only; trees keep their colours); `Sky` feeds the hue to the block shader as the
  global `_Sc_FloraTint`; `BlockAtlas.shader` **desaturates the flora tile to its luminance and re-tints it**
  by the hue. No tile regen + no re-mesh needed (it's a live shader global, off in space/stations). Grayscale
  flora tiles (Phase 5a) become optional polish now. Test: `FloraTint_IsAColourfulPlanetHue`.
- ✅ **Phase 3 — FloraGenerator + names** (done 2026-06-07) — a new `FloraGenerator.GenerateRoster(planet,
  seed)` derives a per-world flora roster: each archetype block (`FloraCatalog`) becomes a named species
  (`NameGenerator.Flora`) with an edible/**toxic** trait, deterministic from seed+planet. The server maps
  block→species (`FloraSpeciesForBlock`); **scanning a plant now shows its coined name + Edible/Toxic** (the
  block branch in `GameServerScanning`). Per-world identity = the Phase-2 hue + the coined names + toxicity
  over the shared archetype blocks (no dynamic blocks needed). Tests: `FloraRoster_NamesEveryArchetype_…`,
  `FloraRoster_IsEmpty_OnABarrenWorld`. ✅ **toxic flora now bites back** (2026-06-07): harvesting a toxic
  species yields **`toxic_berries`** (a harmful consumable, `consumeHealth -18`) instead of edible berries —
  `BreakBlockAt` remaps the drop by the world's flora species, so the scan's Edible/Toxic warning has teeth.
  Test: `ToxicFlora_YieldsToxicBerries_…`. ✅ **per-world archetype subsetting** (2026-06-07): each world now
  activates only ~60% of the flora forms (`FloraSpecies.Active`), so worlds differ in plant *shapes*, not just
  hue/names — with an `EnsureCoverage` pass that force-activates the minimum so every land host surface + the
  seas keep ≥1 active species (no bare biome). `WorldGenerator.ResolveFlora` builds the per-surface pools +
  kelp/lily gating from the active subset; placement + scan share `_meta.Seed` so they always agree. Test:
  `FloraRoster_ActivatesASubset_ButKeepsFullCoverage`.
- ◑ **Phase 4 — fauna polish + names** — ✅ **names + colours done** (`3c1500c`): a shared `NameGenerator`
  coins per-species names (shown on scan as the readout subject; rides `NetCreature.Name`); `PickColor` now
  makes ~half of species vivid exotics (HSV hues → pink/violet/yellow/teal). ⏳ **still to do:** per-**biome**
  species affinity (spawn species by the player's current biome, not just per-planet). ✅ **done 2026-06-07**:
  `CreatureSpecies.BiomeAffinity` (assigned per species from the planet's biome count; -1 on single-biome
  worlds); `TrySpawnCreatureNear` now does a biome-first two-pass spawn (native species preferred, any as
  fallback) so a multi-biome world shows different fauna per region. Test:
  `Roster_AssignsBiomeAffinity_SpreadAcrossAMultiBiomeWorld`.
- ◑ **Phase 5 — asset generation (paid, approved)** — ✅ **(a) flora textures complete**: generated +
  bundled `flora_kelp` + `flora_lily` (OpenAI), so **all 15 `flora_*` blocks now have tiles** (audit confirmed
  flora 15/15 + creature hides 12/12 covered; only special blocks — lights/force-field/ladder/stairs/data-cache
  — use BaseColor, by design). Grayscale regen is unnecessary — the Phase-2 shader re-tints by luminance, so
  the existing colour tiles already work. ✅ **(c) more creature calls**: generated 6 new ElevenLabs signature
  calls (trill / click / rumble / bellow / hiss / chitter) → `CreatureView.Calls[]` now has **12** (audio
  106 → 112), so a world's fauna sounds far more varied. ⏳ **still to do:** (b) more creature hides only if
  new body parts are added.

Also in the backlog: **multiplayer player-name reservation** — ✅ DONE (server-side name verification; see the
"Planned — requested 2026-06-06" entry below).

Everything builds + **302 tests pass** as of `0d988a9`.

### Planned — requested 2026-06-06 (für später)
- ✅ **Mineable water/lava (with the mining beam) + source logic** (done 2026-06-06) — water/lava are now
  `mineable` but `requiredTool: drill` + `minToolTier: 3`, so **only the mining beam** clears them (the
  basic/titanium drills can't); each drops its placeable item. Removing a fluid cell calls `OnFluidRemoved`,
  which **wakes the surrounding fluid** so a body refills the hole — worldgen sea cells act as full sources,
  so you can only drain a **finite pool** by taking its last feeding cells. A settle guard in `TickFluids`
  (`HasAirNeighbor`) lets calm full cells go dormant, so a big sea doesn't keep every cell active. Tests:
  `WaterAndLava_AreMineable_OnlyByTheMiningBeam`, `Water_BasicDrillCannot_MiningBeamCan`,
  `WaterBody_RefillsAMinedHole`.
- ✅ **Source/flowing fluids — no floating shelf + retraction** (done 2026-06-14) — fixed a reported bug where
  water poured from a mountain pond over a cliff **hung in the air over the plain** instead of falling. Root
  cause: a lip cell, once it had dropped one block, took the sideways-spread branch and crawled along at its
  own (high) elevation over the void, and flowing water never receded. Fix in `GameServerFluids`: (1) a
  **`FallingFluid` set** marks cells filled from above — a cell feeding a waterfall no longer spreads sideways,
  so water crests the rim by one overhang cell and pours straight down; (2) a **source vs flowing** split — a
  source is an *untracked* cell (bottomless, e.g. worldgen seas + placed water/lava), a flowing cell lives in
  `_fluidLevel` and each tick recomputes its `SupportedLevel` (full if fluid above, else best horizontal
  neighbour − 1); with no feed it **retracts** (dries up) and wakes its neighbours so the whole orphaned tail
  recedes. `Wake` no longer promotes untracked cells (they stay sources). Tests:
  `Water_PouringOverACliff_DoesNotHangInTheAir`, `FlowingWater_Recedes_WhenItsSourceIsRemoved`. *(Note: worldgen
  ponds are still infinite sources — finite springs are a separate future item.)*
- **Multiplayer player-name reservation — ✅ DONE.** Player names are now verified/reserved server-side so two
  clients can't collide on the same name/identity (`GameServer.cs` join path; covered by `NameVerificationTests`).
  Requested 2026-06-06.
- ✅ **Creature swim undulation + dive** (done 2026-06-06) — `CreatureBuilder` now hangs every part off a
  `BodyRig` pivot; for aquatic species (`Habitat == "Water"`) `CreatureAnimator` undulates that rig (a yaw
  weave that lags the tail beat + a counter-roll + a slow vertical glide), beats the tail faster/wider, and
  drops the legs to a fin-flutter instead of a stride. Server **dive**: `AdjustHabitatHeight` porpoises
  swimmers up and down the water column over time (`sin(_creatureClock·0.22 + pos)`), clamped to the column,
  instead of holding one depth. (Earlier: terrain/water-following Y — land/lava walk, fliers hover.)
- ✅ **Underwater sound filter** (done 2026-06-06) — `ClientAudio` adds an `AudioLowPassFilter` to its own
  GameObject (which also hosts `ClientMusic`), so when the player's **head sits inside a water/lava block**
  (`HeadInFluid`: sample the block ~1.5 above the player root) the cutoff sweeps to ~680 Hz and the whole
  bed + music muffle; it sweeps back to open above water. 3D one-shots (`At`) get a per-source low-pass while
  submerged too. A low-pass on the AudioListener does nothing in Unity — it must live on the sources, which
  is why this rides the ClientAudio object rather than the camera.
- ✅ **Aquatic fauna + water flora** (done 2026-06-07) — **Water flora:** two new flora blocks `flora_kelp`
  (stalk rooted on the seabed, grows a few cells up, top stays open water) + `flora_lily` (pad on the surface
  water cell); a `StampWaterFlora` worldgen pass seeds them in submerged columns of **water** seas (never
  lava). Both in `FloraCatalog` with seabed/water hosts + an `Aquatic` flag that keeps them out of the
  land surface-flora pool (their hosts overlap dry-land blocks). Mining underwater now refills the hole
  (`BreakBlockAt` wakes adjacent fluid via `HasFluidNeighbor`), so harvesting kelp doesn't leave air pockets.
  BaseColor fallbacks (sea-green) until OpenAI tiles are generated. Tests:
  `AtmosphereWorld_GrowsAquaticFlora`, `AirlessFloraWorld_GrowsNoAquaticFlora`. **Aquatic fauna** already
  worked (water-habitat species generate on water-life planets + spawn in water columns + swim/dive).
  *Follow-up: generate kelp/lily textures (OpenAI) for parity with the other flora tiles.*
- **Flora re-tint per species × planet — ✅ SHIPPED 2026-06-11 (463 tests green, client built).**
  Every flora species (flora_* + tree_leaves) now rolls ONE deterministic colour per world — uniform
  within the world, different on the next — instead of the single per-planet hue. No texture regeneration
  needed: the shader already desaturates flora to luminance before tinting, so the existing tiles tint
  cleanly. Implementation: shared `FloraTints.For(seed, location, blockKey)` (FNV + HSV band, pure
  function → all clients agree with zero traffic); `ChunkMesher` TEXCOORD2 grew from float2 to float4
  (x = leaf flag as before, **yzw = per-species tint**; black = "no per-vertex tint"); both `BlockAtlas`
  SubShaders (URP + Built-in) prefer the vertex tint and fall back to the global `_Sc_FloraTint` hue —
  so meshes built WITHOUT a resolver (ship preview etc.) keep the old behaviour, and the global alpha
  stays the master enable (off in space/menus). `GameBootstrap` rebuilds the tint map on join + world
  change. Tests: `FloraTintTests` (determinism, per-species variance, per-world variance, colour band).
  *(Per-BIOME variation within one world stays optional future polish.)*

### Not started / larger future work — ✅ CLOSED (resolved 2026-06-19)
*(Per user decision 2026-06-19: this whole section is considered resolved — the leftovers below are either
shipped, superseded by later work, or deliberate non-goals. Kept for the record.)*
- **World wrap (walk around the planet)** — ✅ **W0–W4 shipped**: X is a wrapping longitude (cylinder
  world), so you can walk east and arrive back at the start with a **seam-free** edge (terrain/biomes/caves/
  ore/structures continuous across X = 0 ≡ X = 6000). Seam-free generation via circular-domain noise; server
  + client + persistence + interaction all route X through one wrap helper. **Remaining: W5 (poles)** — bound
  latitude (Z) with an ice-wall/barrier biome. Full plan + progress in
  [docs/developer/WORLD_WRAP.md](docs/developer/WORLD_WRAP.md).
- **Advanced graphics roadmap** — Built-in RP vs URP decision, god rays, reflection probes, LUT grade.
  Full research in [docs/developer/ADVANCED_GRAPHICS.md](docs/developer/ADVANCED_GRAPHICS.md).
- **Texture audit** — review/expand item & icon art and creature/NPC texture variety.
- **uGUI theme polish — ✅ icon pass SHIPPED 2026-06-11:** 10 new generated cyan line icons
  (`map_player/ship/waypoint/beacon/pad/settlement/ruin/wreck/station` + `icon_vega`). The world map's
  unicode-glyph markers are now SPRITES (player arrow rotates, pads pin to the edge as icons, POI types
  get distinct art) with glyph fallback when an icon is missing; the glyph legend line became a real
  icon legend row; the VEGA companion panel shows her avatar chip beside the name. Remaining (kept):
  hover/selected states + spacing harmonisation across the newer screens.
- **Deferred by design** (see [docs/developer/SPACE_COMBAT_CONCEPT.md](docs/developer/SPACE_COMBAT_CONCEPT.md)): PvP ship
  combat, large cruisers/bosses. (Per-player ships shipped in P4.)

---

## Reference docs (committed, under docs/)
Concept/design detail for the larger systems: `STATION_AS_LOCATION_PLAN`, `WORLD_WRAP_PLAN`, `MULTIWORLD_AND_SYSTEM_FLIGHT_PLAN`, `SPACE_COMBAT_CONCEPT`,
`CLIENT_COMPLETION_PLAN`, `CRAFTING_TECH_SHIP_UI_PLAN`, `STATION_SETTLEMENT_EDITOR_PLAN`,
`SHIP_TYPE_EDITOR_PLAN`, `ADVANCED_GRAPHICS_PLAN`, `SOUND_DESIGN`, `SELF_HOSTING`, `AI_MISSION_BACKEND`,
`CLIENT_SHELL_AND_ASSETS`.
