# Developer Guide — Building & Verifying the Game

How to build **Blocks Beyond the Stars** from source, how to tell whether a build is
actually fresh, and the known build pitfalls that compile fine but silently break in the
built player. Contributor rules (language, architecture, conventions) live in
[AGENTS.md](../../AGENTS.md); current project status lives in [TODO.md](../../TODO.md).

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 8.x | builds the server, shared libs, tests and tools |
| Unity Editor | 6 LTS (6000.4.x) | required only for the Windows client build; default path used by the build script: `C:\Program Files\Unity\Hub\Editor\6000.4.9f1\Editor\Unity.exe` |
| Windows | 10/11 | the client is a Windows player; the dedicated server also publishes for Linux |
| PowerShell | 7+ | for the `scripts/*.ps1` build scripts |

## Server, tests, tools (.NET)

```powershell
dotnet build BlocksBeyondTheStars.sln                       # build everything
dotnet test                                                 # run all xUnit tests
dotnet run --project src/BlocksBeyondTheStars.GameServer    # local dedicated server (UDP 31415)
dotnet run --project src/BlocksBeyondTheStars.Api           # admin UI (http://127.0.0.1:31416)
```

## Running the tests

```powershell
dotnet test                                # all .NET xUnit suites (server/shared + headless client<->server)
./scripts/run-tests.ps1                     # selectable runner — default: fast .NET suites only
./scripts/run-tests.ps1 -Suites All         # + the Unity Editor suites (EditMode + PlayMode-vs-real-server)
./scripts/run-tests.ps1 -Suites ClientCore  # just the headless client<->server integration tests
./scripts/run-tests.ps1 -Coverage           # .NET suites with a coverage report (TestResults/)
```

`run-tests.ps1` selects suites via `-Suites` (`Dotnet`, `ClientCore`, `UnityEdit`, `UnityPlay`, `All`); the
Unity suites are opt-in so they don't slow the common loop, and need `Unity.exe` (pass `-UnityPath` if not at
the default `6000.4.9f1` path). The client is tested against the **real** server at three tiers — the design,
the `Client.Core` split, and the per-tier prerequisites are documented in
[CLIENT_TESTING.md](CLIENT_TESTING.md).

## Continuous integration (pull requests)

Every pull request (and every direct push to `main`) is gated by
[`.github/workflows/ci.yml`](../../.github/workflows/ci.yml). It runs on `ubuntu-latest` and is fast and
free — **no Unity, no license** — because it only exercises the two headless .NET suites:

```
tests/BlocksBeyondTheStars.Tests        # Dotnet  — server/shared xUnit
tests/BlocksBeyondTheStars.Client.Tests # ClientCore — real NetworkClient ↔ in-process GameServer
```

These are the same two suites `run-tests.ps1` runs by default. Two deliberate choices:

- **It targets the test projects directly, not `BlocksBeyondTheStars.sln`.** The solution also contains the
  WinForms launcher (`net8.0-windows`), which cannot build on the Linux runner; building the test projects
  pulls in exactly their dependencies (server/shared/`Client.Core`) and nothing Windows-only.
- **Warnings fail the PR.** The build step runs with **`-warnaserror`**, so any compiler or analyzer warning
  breaks the check — even though the local build keeps `TreatWarningsAsErrors=false`
  ([Directory.Build.props](../../Directory.Build.props)) for a friction-free dev loop. The tree currently
  builds clean (0 warnings), so this only guards against regressions. The `.trx` results are uploaded as a
  run artifact for inspection.
- **Docs-only changes skip the build — without blocking the PR.** A small `changes` job
  ([`dorny/paths-filter`](https://github.com/dorny/paths-filter)) decides whether the diff touches anything
  other than docs (Markdown, `docs/**`, licences, issue/PR templates). If it is docs-only, the heavy
  `build-test` job is skipped via its `if:`. The workflow still **runs** (so the check always reports), and a
  *skipped* required job counts as a pass — so the gate stays green on a docs PR instead of stalling. Any
  code/content file makes it build for real; `data/**` and `web/**` count as code (they feed tests, e.g. the
  en/de locale-parity test), so they are **not** in the doc-only exclusions.

It is **safe as a required status check** — and is one: because the workflow always runs and the build-test
check always reports (green/red/skipped-as-pass), a docs-only PR is never left waiting on a missing status, the
usual failure mode of a plain `paths-ignore` skip. (Require the `Build + test (.NET, headless)` job, **not** the
`Detect code changes` helper.)

The **Unity** suites (`UnityEdit` / `UnityPlay`) are **not** in CI — they need the Editor and stay local/opt-in
(run them with `./scripts/run-tests.ps1 -Suites All` before a client-affecting change). The release build
([`release.yml`](../../.github/workflows/release.yml)) is a **separate** workflow that triggers only on tags;
it does not run tests, so the PR gate is where correctness is checked before merge.

> **Required status checks on `main`** (branch protection): `Build + test (.NET, headless)`,
> `Format (dotnet format)`, `ruff (Python ai-backend)`, `web JS syntax (node --check)`, `actionlint (workflows)`
> and `CodeQL` — all six must pass before merge. The `Detect code changes` helper is intentionally **not**
> required (it's an always-skippable gate; only require jobs that always report). `strict` is off (no forced
> rebase) and `enforce_admins` is off (an owner can still `gh pr merge --admin` in an emergency).

### Other PR checks: format, lint, CodeQL

Beyond build+test, three more gates run on PRs:

- **`Format (dotnet format)`** — a second job in [`ci.yml`](../../.github/workflows/ci.yml) runs
  `dotnet format --verify-no-changes` against [`BlocksBeyondTheStars.CI.slnf`](../../BlocksBeyondTheStars.CI.slnf),
  a solution filter listing every project **except** the Windows-only launcher (so the set loads on Linux). It
  enforces `.editorconfig` style: indentation, `using` ordering, object-initializer line breaks, etc. **Line
  endings must be LF** — this only works because `.gitattributes` (`eol=lf`) and `.editorconfig`
  (`end_of_line = lf`) agree; a CRLF setting would fail it on every fresh checkout. Gated by the same `changes`
  job, so docs-only PRs skip it. Run `dotnet format BlocksBeyondTheStars.CI.slnf` to auto-fix before pushing.
- **[`lint.yml`](../../.github/workflows/lint.yml)** — the non-.NET languages: **ruff** for the Python
  `ai-backend`, **`node --check`** (parse-only, zero-config) for the browser JS under `web/`, and
  **actionlint** (ShellCheck disabled → pure workflow-syntax) for the workflow YAML.
- **[`codeql.yml`](../../.github/workflows/codeql.yml)** — GitHub CodeQL security + quality scanning for
  C#, JavaScript/TypeScript, Python and Actions. C# uses `build-mode: none` (buildless), so it needs no Unity
  and also covers the Unity client C# under `client/Assets/` that the .NET build never sees. Findings appear in
  **Security → Code scanning**. This is the *advanced setup* (a committed workflow) rather than the API default
  setup, because the workflow's `GITHUB_TOKEN` already has `security-events: write` (the API PUT needs a PAT).

The C# *compile/static-analysis* gate is the `-warnaserror` build (Roslyn + Meziantou + VS.Threading analyzers
as errors); `Format` adds pure style on top.

Run the equivalents locally before pushing (mirrors [AGENTS.md](../../AGENTS.md) §Local verification):

```powershell
dotnet format BlocksBeyondTheStars.CI.slnf --verify-no-changes                            # C# style/format
uvx ruff check ai-backend                                                                # Python (uv is a project dep)
Get-ChildItem web -Recurse -Filter *.js | ForEach-Object { node --check $_.FullName }     # web JS syntax
```

**All of these are required status checks** on `main` (see the list above), so every PR must pass build+test,
format, the three lint jobs and CodeQL before it can merge.

> **Heads-up for Windows devs:** `dotnet format --verify-no-changes` reads the working-tree bytes, so if an
> old checkout still has CRLF in some files (from before `.gitattributes eol=lf` landed) it can report
> line-ending diffs locally that CI (a fresh LF checkout) never sees. Re-normalize once with
> `git add --renormalize . ; git checkout -- .` if that happens.

## Local verification after changes (mandatory)

Because the PR gate is **.NET-only** (see above), some classes of breakage are invisible to CI until a release
tag is cut. Run this chain locally after finishing a set of changes — before committing — so they are caught
early:

1. **Tests** — `dotnet test` for the affected suite(s) (`BlocksBeyondTheStars.Tests` for server/shared,
   `BlocksBeyondTheStars.Client.Tests` for client-core), all green.
2. **Warning check** — a clean rebuild and confirm **0 warnings / 0 errors**:

   ```powershell
   dotnet build src/BlocksBeyondTheStars.GameServer/BlocksBeyondTheStars.GameServer.csproj --no-incremental
   ```

   The analyzers are on and CI builds with `-warnaserror`, so a warning fails the PR even though the local
   build keeps `TreatWarningsAsErrors=false`. Don't trust `dotnet test -v minimal` — it hides warnings.
3. **Local Unity build when client code changed** — the Unity player is **never** built by PR CI; only
   [`release.yml`](../../.github/workflows/release.yml) builds it (on a tag / manual dispatch). So whenever a
   `client/Assets/**` file changed, run:

   ```powershell
   ./scripts/build-client.ps1
   ```

   This catches Unity-only compile failures (the `CS0246` "works in the Editor, broken in the build" trap —
   see [Troubleshooting](#troubleshooting-works-in-the-editor-silently-broken-in-the-build)) **and** surfaces
   generated/synced files that must be committed (synced libs, `.meta` files). Confirm freshness via the
   `BlocksBeyondTheStars.Client.dll` timestamp (see [Verifying a build is actually fresh](#verifying-a-build-is-actually-fresh)).
   Pure server/shared/docs changes don't need this step.

## Building the Windows client

One command produces a fully self-contained singleplayer/multiplayer client:

```powershell
./scripts/build-client.ps1
```

It runs these steps:

1. **Sync shared libs + content** (`scripts/sync-client-libs.ps1`) — `dotnet publish`es
   `BlocksBeyondTheStars.Shared`, `.WorldGeneration`, `.Networking` and `.Client.Core` (netstandard2.1) with
   their NuGet dependencies (MessagePack, LiteNetLib, …) and copies the DLLs into
   `client/Assets/Plugins/`. It also copies `data/*` (blocks, items, recipes, locales, …)
   into `client/Assets/StreamingAssets/data/`.
2. **Bundle the singleplayer server** (`scripts/publish-local-server.ps1`) — publishes a
   self-contained, single-file `BlocksBeyondTheStars.GameServer` into
   `client/Assets/StreamingAssets/server/`. "Singleplayer" in the game launches this
   executable as a child process on `127.0.0.1` (see `LocalServerLauncher.cs`); it reuses
   `StreamingAssets/data` and writes saves to the user's persistent data path.
3. **Unity batch build** — runs the Unity Editor headless
   (`-batchmode -quit -nographics -executeMethod BlocksBeyondTheStars.Client.EditorTools.BuildScript.BuildWindows`)
   and writes the player to `client/Build/Windows/`. `BuildScript` also auto-generates the
   launcher scene, embeds the app icon, and force-includes the runtime shaders
   (`EnsureShadersIncluded`) on every build.
4. **Build the loading-splash launcher** — `dotnet publish`es
   `src/BlocksBeyondTheStars.Launcher` (a self-contained, single-file WinForms exe) and copies
   `BlocksBeyondTheStars.Launcher.exe` next to the player. It shows an instant "Loading…"
   splash to cover the black pre-engine startup gap, starts the Unity player, and hands off
   when the player window appears. It is the executable the Velopack installer launches
   (`publish-client-installer.ps1 --mainExe`).

Useful parameters:

```powershell
./scripts/build-client.ps1 -SkipPrereqs    # rebuild only the Unity player (client-only changes)
./scripts/build-client.ps1 -UnityPath "C:\...\6000.4.x\Editor\Unity.exe"
./scripts/build-client.ps1 -Out Build/SomewhereElse
```

> **Important:** after changing anything under `src/` (Shared, WorldGeneration, Networking)
> or `data/`, run the **full** build *without* `-SkipPrereqs` — those changes reach the
> client only through the synced DLLs/content from step 1.

To package the built player locally as a Velopack installer/update feed, run
`scripts/publish-client-installer.ps1` after a successful `build-client.ps1` (it ships the launcher exe as
`--mainExe`). Add `-Msi` to also build the machine-wide WiX MSI. Pass `-Version <semver>` for a real release;
without it the version is read from `PlayerSettings.bundleVersion` in `ProjectSettings.asset` (the version
source of truth — see [Releases & versioning](#releases-github-actions--versioning) below). For an actual
shipped release you normally do **not** run this by hand — push a tag and let CI do it.

### Build log, exit codes and duration

- The Unity log is written to **`client/build.log`**.
- **Do not trust Unity's exit code.** Unity may relaunch a child process to recompile, or
  retry on a licensing hiccup, so the initial process can return early (and with a
  null/non-zero code) even on success. The script therefore waits until no Unity process
  remains *and* the log contains a terminal marker (up to 25 minutes).
- The authoritative success signal in the log is **`BlocksBeyondTheStars build: Succeeded`**;
  a failed compile logs **`Scripts have compiler errors`** (this can still exit 0!).
- A full build typically takes a few minutes; first-time imports take longer.

## Verifying a build is actually fresh

The game `.exe` is **only the Unity launcher wrapper** — Unity reuses it, so its timestamp
stays old even after a successful rebuild. The real game code is in the managed assemblies:

```
client/Build/Windows/
├── BlocksBeyondTheStars.exe                  ← wrapper, timestamp NOT meaningful
├── UnityPlayer.dll                           ← Unity engine, rarely changes
└── BlocksBeyondTheStars_Data/
    ├── Managed/
    │   ├── BlocksBeyondTheStars.Client.dll          ← ★ check THIS timestamp
    │   ├── BlocksBeyondTheStars.Shared.dll          ← synced shared logic
    │   ├── BlocksBeyondTheStars.Networking.dll      ← synced networking
    │   └── BlocksBeyondTheStars.WorldGeneration.dll ← synced worldgen
    └── StreamingAssets/
        ├── data/                             ← JSON content + locales
        └── server/                           ← bundled singleplayer server
```

```powershell
Get-ChildItem client/Build/Windows/BlocksBeyondTheStars_Data/Managed/BlocksBeyondTheStars.*.dll |
    Select-Object Name, LastWriteTime
```

- **`BlocksBeyondTheStars.Client.dll`** holds all client scripts (compiled from the asmdef —
  there is **no** `Assembly-CSharp.dll`). If its timestamp is current, your client-code
  changes are in the build.
- For shared/networking/worldgen changes, also check the corresponding synced DLL — if it
  is old, the prereq sync didn't run (you probably built with `-SkipPrereqs`).
- **Symptom of a stale build:** a brand-new feature "does nothing" in the game although the
  code is correct and all tests pass.

## Releases (GitHub Actions) & versioning

Shipped releases are built in the cloud by [`.github/workflows/release.yml`](../../.github/workflows/release.yml),
not locally. Cut one by pushing a SemVer tag:

```bash
git tag v0.3.0 && git push origin v0.3.0
```

The workflow has two jobs:

1. **Build Unity player (Linux)** — GameCI (`game-ci/unity-builder`) cross-builds the `StandaloneWindows64`
   player from a Docker image (Mono backend). It first runs the same prereqs as `build-client.ps1`
   (`sync-client-libs.ps1`, `sync-velopack-libs.ps1`, `publish-local-server.ps1 -Runtime win-x64`), caches
   `client/Library`, and frees runner disk space before the ~6 GB editor image pull.
2. **Package + release (Windows)** — builds the launcher, downloads the player, and runs
   `publish-client-installer.ps1 -Msi` (with `vpk` pinned to the vendored Velopack version) to produce and
   attach three assets to a published GitHub Release: **`…-win-Setup.exe`** (per-user, no admin),
   **`…-win.msi`** (machine-wide, for IT/MDM) and **`…-win-Portable.zip`**. The same three assets are then
   mirrored to **itch.io** (see below).

The workflow triggers **only** on tag pushes and manual dispatch (never `pull_request`), so the Unity license
secrets (`UNITY_LICENSE`/`UNITY_EMAIL`/`UNITY_PASSWORD`) are never exposed — safe even with a public repo. A
manual *Run workflow* dispatch builds and packages a `0.1.0-dev` test artifact but does not publish a Release.

> **Note:** `publish-client-installer.ps1` deletes Unity's developer-only `*_DoNotShip` (Burst AOT debug
> symbols) and `*_BackUpThisFolder_ButDontShipItWithYourGame` (IL2CPP backup) folders from the player before
> packing, so they never end up inside the shipped installers. They are regenerated on the next Unity build.

### Mirroring releases to itch.io

After the GitHub Release is published, the same job pushes the three installers to the itch.io page
[`jumavegames/blocks-beyond-the-stars`](https://jumavegames.itch.io/blocks-beyond-the-stars) via
[butler](https://itch.io/docs/butler/) (`scripts/publish-itch.ps1`). Each artifact goes to its own channel,
stamped with the release version (`--userversion`):

| Artifact          | itch.io channel    |
| ----------------- | ------------------ |
| `…-win-Setup.exe` | `windows-setup`    |
| `…-win.msi`       | `windows-msi`      |
| `…-win-Portable.zip` | `windows-portable` |

Notes:

- The upload runs **after** the GitHub Release step, so a butler hiccup can never block the GitHub publish.
- butler authenticates with an itch.io API key stored as the **`BUTLER_API_KEY`** repository secret (Settings →
  Secrets and variables → Actions). It is **never** committed — generate one at itch.io → *Settings → API keys*.
- The script auto-downloads butler (win-x64) if it is not already on `PATH`, so it also works locally:
  `$env:BUTLER_API_KEY = '…'; ./scripts/publish-itch.ps1 -Version 0.3.0` (run after `publish-client-installer.ps1`).
- The `windows` prefix in each channel name tags the upload as a Windows download on the itch.io page.

### The git tag is the single source of truth for the version

The tag (e.g. `v0.3.0`) is the only place the version is set. It flows everywhere automatically:

- CI passes it to GameCI **`versioning: Custom`**, which sets `PlayerSettings.bundleVersion`. So in-game,
  `AppShell.Version` — now a property **`=> Application.version`** (no longer a hardcoded const) — shows the
  release version in the menu, loading and splash screens.
- The launcher gets `-p:Version`, and Velopack gets `--packVersion`, with the same value.
- `BuildScript` writes `version.txt` next to the player; a CI guard step **fails the build** if the baked
  version ≠ the tag, so the in-game version and the release can never silently drift.
- The committed `bundleVersion` is **`0.1.0-dev`** so local/dev builds are clearly marked.
- `Networking/Protocol.Version` (an `int`, wire-format compatibility) is **separate** and unrelated to the
  game version — do not conflate them.

When working on this pipeline, three non-obvious gotchas already cost a debugging cycle each:

- **GameCI always overrides `bundleVersion`** with its own versioning, beating anything the build method sets
  via a custom arg (`versioning: None` even bakes the literal string `"none"`). Drive it through
  `versioning: Custom` + `version:`; do not fight it.
- **Velopack requires `packVersion >= 0.0.1`** — that is why the dev value is `0.1.0-dev`, not `0.0.0-*`.
- **`gh workflow run` right after `git push`** can dispatch the *previous* commit (tag/branch HEAD hasn't
  propagated yet) — wait ~20 s and verify the run's `headSha`.

Linux/macOS **client** installers are intentionally not built: the client can't yet build for those platforms
(the Windows-only UnityWebBrowser/CEF engine is the blocker — see
[WEBCLIENT_FEASIBILITY.md](WEBCLIENT_FEASIBILITY.md)). The .NET **server** already cross-builds
(`publish-server.ps1`: win-x64/linux-x64/linux-arm64) and could be attached separately.

## Troubleshooting: works in the Editor, silently broken in the build

These pitfalls share one theme — *the code compiles and tests pass, but the built player
silently misbehaves*. Check them in this order:

1. **Stale build / stale synced DLLs** — see the section above. Rebuild without
   `-SkipPrereqs`.
2. **Shader got stripped.** All materials are created in code via
   `Shader.Find("BlocksBeyondTheStars/...")`; there are no `.mat` assets referencing the custom
   shaders, so Unity strips any shader not listed in
   `client/ProjectSettings/GraphicsSettings.asset` → `m_AlwaysIncludedShaders`.
   `Shader.Find` then returns `null` *only in the build*. When adding a new shader, take its
   GUID from the `.shader.meta` file and add `- {fileID: 4800000, guid: <GUID>, type: 3}`
   to the list (`BuildScript.EnsureShadersIncluded()` maintains the known runtime shaders).
   Classic symptom: effect works in the Editor, is missing/black in the `.exe`.
3. **Network message not registered.** Every new top-level message class (an `*Intent` or a
   server→client message) must be registered in `src/BlocksBeyondTheStars.Networking/NetCodec.cs`
   via `Register(<next free id>, typeof(NewMessage))` — ids are append-only, never reuse one.
   Forgetting this makes `NetCodec.Encode` throw at send time: the action simply "does
   nothing" in-game while unit tests (which call methods directly) still pass. New *fields*
   on registered classes and nested types need no registration.
4. **Unity layer used by name.** `LayerMask.NameToLayer("X")` returns **-1 in the batch
   build** for freshly added layers even though `TagManager.asset` has the name. Use the
   fixed layer **index** (`gameObject.layer = idx`, cull mask `1 << idx`), optionally trying
   the name first and falling back to the index.
5. **Block id beyond the texture atlas.** `BlockTextureAtlas` (client) has `Cols × Rows`
   tile slots (currently 16×16 = 256). A block id beyond that renders as an untextured grey
   tile with no error. When adding many blocks, raise `Cols`/`Rows`.
6. **Duplicate `System.*` assemblies.** If Unity complains about a duplicate assembly after
   a lib sync, delete that DLL from `client/Assets/Plugins/` — Unity already ships its own
   copy.

**Where to look when debugging the built player:** the player log is at
`%USERPROFILE%\AppData\LocalLow\JuMaVe Games\Blocks Beyond the Stars\Player.log`.
Add a `Debug.Log` before the guard that might fail (e.g. log the layer index or whether
`Shader.Find` returned null) and rebuild.

## Optional AI backend (development)

The optional LLM text service is **not** part of the client build — it is a separate
Python process the *game server* talks to. To run it during development:

```bash
cd ai-backend
uv run uvicorn app.main:app --host 127.0.0.1 --port 8077
```

Then set `aiLevel` (`Suggest` or `Auto`) in the server's `config/server.json`
(`aiBackendUrl` defaults to `http://127.0.0.1:8077`). Without it the game uses scripted
fallback text everywhere. Setup details: [SELF_HOSTING.md](SELF_HOSTING.md) §8; endpoints:
[ai-backend/README.md](../../ai-backend/README.md).

## Self-hosting / dedicated server packages

```powershell
./scripts/publish-server.ps1     # win-x64, linux-x64, linux-arm64
```

Produces self-contained single-file packages under `artifacts/` (no .NET runtime needed on
the host). On Linux/macOS use `scripts/publish-server.sh`. Operating the server is covered
in [SELF_HOSTING.md](SELF_HOSTING.md).

## Related documents

- [AGENTS.md](../../AGENTS.md) — contributor rules (English-only docs, bilingual in-game text,
  server-authoritative architecture, data-driven content)
- [TODO.md](../../TODO.md) — single Done/Open status doc
- [USER_MANUAL.md](../user/USER_MANUAL.md) — player-facing controls, mechanics and commands
- [SELF_HOSTING.md](SELF_HOSTING.md) — running a dedicated server
