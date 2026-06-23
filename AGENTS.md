# AGENTS.md — Guide for AI Agents Working on Blocks Beyond the Stars

This file orients any AI agent (or developer) contributing to **Blocks Beyond the Stars**.
Read it before making changes.

## What Blocks Beyond the Stars is

A block-based 3D space crafting game for Windows and Linux. The player starts with a small
spaceship, explores procedurally generated planets, mines resources, crafts gear,
researches blueprints, and grows the ship. The current status (Done/Open) lives in
[TODO.md](TODO.md); player-facing operation in [docs/user/USER_MANUAL.md](docs/user/USER_MANUAL.md).
(The original German requirement specs under `plans/` were consolidated and removed.)

## Golden architecture rule

> **The Unity client is presentation and input. The .NET server is the truth of the
> game world.**

The client sends *intents*; the server validates them authoritatively and replies
with *state*. Never make the client authoritative over resources, inventory,
crafting, ship state, oxygen, damage, blueprints, or travel. This keeps multiplayer,
LAN/self-hosting and anti-cheat correct by construction.

## Tech stack (decided)

- **Client:** Unity 6 LTS (6000.4.x), C#. Lives in `client/` (open in the Unity Editor). Builds for Windows (WinForms launcher) and Linux (console launcher).
- **Server:** .NET 8, standalone console host. **No Unity runtime on the server.**
- **Admin/API:** ASP.NET Core 8 (Minimal API).
- **DB:** SQLite by default (portable); PostgreSQL later.
- **Realtime networking:** LiteNetLib (UDP). MessagePack for wire serialization.
- **Shared/WorldGeneration target `netstandard2.1`** so the *same* code runs in both
  Unity and the .NET server. Everything else targets `net8.0`.

## Repository layout

```
src/BlocksBeyondTheStars.Shared/          data models, data-driven definitions, localization, protocol DTOs
src/BlocksBeyondTheStars.WorldGeneration/ seed-based deterministic chunk generation
src/BlocksBeyondTheStars.Persistence/     SQLite repository, savegame layout, autosave
src/BlocksBeyondTheStars.Networking/      transport abstraction (LiteNetLib + loopback), messages
src/BlocksBeyondTheStars.GameServer/      authoritative tick loop + console host
src/BlocksBeyondTheStars.Api/             admin web UI + API
src/BlocksBeyondTheStars.Tools/           backup/export/debug CLI
src/BlocksBeyondTheStars.Client.Core/     Unity-free client logic (NetworkClient, ClientWorld), netstandard2.1
src/BlocksBeyondTheStars.Launcher/        Windows-only WinForms loading-splash launcher (net8.0-windows)
src/BlocksBeyondTheStars.Launcher.Console/Cross-platform console launcher for Linux (net8.0, SkiaSharp splash)
tests/BlocksBeyondTheStars.Tests/         xUnit tests (server/shared)
tests/BlocksBeyondTheStars.Client.Tests/  headless client<->server integration (real NetworkClient vs real GameServer)
client/                         Unity project (incl. Assets/Tests EditMode/PlayMode suites)
ai-backend/                     optional Python LLM service (missions, NPC/ship-AI text); offline-safe
tools/                          editor-export merge tools + AI asset generation (tools/ai-assets)
data/                           data-driven JSON definitions (blocks, items, recipes, ...)
data/locales/                   localization resource files (en.json, de.json)
docs/user/                      player-facing manual (USER_MANUAL.md)
docs/developer/                 ARCHITECTURE.md + design/how-it-works docs + ADRs (docs/developer/adr/); see docs/developer/README.md index
scripts/                        build-client.ps1 + publish scripts
```

Dependency direction (no cycles): `Shared` ← everything; `WorldGeneration`,
`Persistence`, `Networking` ← `GameServer`.

## Hard rules for contributors (human or AI)

1. **Language of text:**
   - *Documentation and code comments → English only.* Even though the spec docs
     and chat are German.
   - *In-game player-facing text → bilingual (German + English).* Never hardcode
     player-facing strings; use localization keys + `data/locales/*.json`. Default
     fallback locale is English.
2. **Server is authoritative** — see the golden rule above.
3. **Data-driven content** — blocks, items, recipes, ship modules, tech nodes,
   planets live in `data/*.json`, not hardcoded in logic. Adding content should not
   require touching game logic.
4. **World = seed + parameters + deltas.** Only persist player changes, never every
   natural block.
5. **Lightweight server** — no rendering/physics engine on the server; keep CPU,
   RAM and disk-write load low and configurable.
6. **Atomic saves** — write to a temp file then swap; autosave + rotating backups.
7. **Keep `Shared`/`WorldGeneration` netstandard2.1-clean** so Unity can consume them.
8. **Unity-free client logic goes in `Client.Core`** (netstandard2.1, no `UnityEngine`) so the *same*
   `NetworkClient`/`ClientWorld` can be tested headless against the real server. Unity-coupled code
   (meshers, MonoBehaviours, views) stays in the client asmdef. A new shared DLL Unity consumes must be
   listed in **both** the client asmdef's `precompiledReferences` **and** `scripts/sync-client-libs.ps1`
   (miss either → the player build fails `CS0246` while the Editor still compiles). See
   [docs/developer/CLIENT_TESTING.md](docs/developer/CLIENT_TESTING.md).

## Build & test

```powershell
dotnet build BlocksBeyondTheStars.sln      # build everything (Windows)
dotnet test                      # run all xUnit tests (server/shared + headless client<->server)
./scripts/run-tests.ps1          # selectable suites: Dotnet, ClientCore, UnityEdit, UnityPlay, All
dotnet run --project src/BlocksBeyondTheStars.GameServer   # start a local server
./scripts/build-client.ps1       # full Windows client (shared libs + bundled server + Unity batch build)
```

```bash
dotnet build BlocksBeyondTheStars.sln      # build everything (Linux)
./scripts/run-tests.sh           # .NET suites (Dotnet + ClientCore)
./scripts/build-client.sh        # full Linux client (shared libs + bundled server + Unity batch build)
```

`run-tests.ps1` defaults to the fast .NET suites (`Dotnet` + `ClientCore`); the Unity Editor suites
(`UnityEdit` EditMode, `UnityPlay` PlayMode-vs-real-server-exe) are opt-in via `-Suites`. How the client is
tested against the real server is documented in
[docs/developer/CLIENT_TESTING.md](docs/developer/CLIENT_TESTING.md).

To confirm a client rebuild actually happened, check the `BlocksBeyondTheStars.Client.dll` timestamp in the
build output (the `.exe` timestamp is not reliable). The full build guide — pipeline details,
freshness verification and the known "works in the Editor, broken in the build" pitfalls — is in
[docs/developer/DEVELOPER.md](docs/developer/DEVELOPER.md).

## Local verification after changes (mandatory)

After finishing a set of local changes — before reporting done and before committing — run this chain so
problems are caught locally instead of at release time:

1. **Tests** — `dotnet test` for the affected suite(s) (`BlocksBeyondTheStars.Tests` for server/shared,
   `BlocksBeyondTheStars.Client.Tests` for client-core). They must be green.
2. **Warning check** — a clean rebuild (`dotnet build --no-incremental`) and confirm **0 warnings / 0 errors**.
   The Roslyn analyzers are on and CI builds with `-warnaserror`, so a warning fails CI. Don't rely on the
   `dotnet test -v minimal` output — it hides warnings.
3. **Format + lint** — match the PR checks so they don't fail in CI:
   - `dotnet format BlocksBeyondTheStars.CI.slnf --verify-no-changes` (C# style; the `.slnf` excludes the
     Windows-only launcher so it runs on any OS). Drop `--verify-no-changes` to auto-fix.
   - If you touched `ai-backend/`: `uvx ruff check ai-backend`. If you touched `web/`: `node --check` the
     changed `.js`. If you touched `.github/workflows/`: `actionlint -shellcheck=`.
4. **Local Unity build when client (Unity) code changed** — **PR CI never builds Unity** (it is .NET-only);
   the Unity player is only built on a release tag / manual dispatch by
   [.github/workflows/release.yml](.github/workflows/release.yml). So whenever a `client/Assets/**` file
   changed, run `./scripts/build-client.ps1` locally. This catches Unity-only compile failures (e.g. the
   `CS0246` "works in the Editor, broken in the build" trap) **and** surfaces generated/synced files that
   must be committed (synced libs under `client/Assets/.../Plugins`, `.meta` files, etc.). Pure
   server/shared/docs changes don't need it.

## Releases & versioning

Releases are built in the cloud by [.github/workflows/release.yml](.github/workflows/release.yml) — never
build a release locally for distribution. Cut one by pushing a SemVer tag:

```bash
git tag v0.3.0 && git push origin v0.3.0
```

That triggers three jobs: a GameCI Linux Docker job cross-builds the `StandaloneWindows64` player (Mono
backend), then a `windows-latest` job builds the launcher and runs `scripts/publish-client-installer.ps1 -Msi`
to attach **Setup.exe** (per-user, no admin), the **WiX MSI** (machine-wide/IT) and **Portable.zip** to a
published GitHub Release. In parallel, a third job reuses [.github/workflows/docker.yml](.github/workflows/docker.yml)
to build the optional **dedicated-server Docker image** and push it multi-arch (amd64+arm64) to **GHCR**
(`ghcr.io/<owner>/blocks-beyond-the-stars-server:<version>` + `:latest`) — see
[docs/developer/SELF_HOSTING.md](docs/developer/SELF_HOSTING.md) §10. The image is NOT a release asset (it lives
in GHCR), so the published release notes are prepended with its `docker pull` command for discoverability. The workflow runs *only* on tags / manual
dispatch (never `pull_request`), so the Unity license secrets (`UNITY_LICENSE`/`UNITY_EMAIL`/`UNITY_PASSWORD`)
are safe in a public repo. A manual *Run workflow* dispatch builds + packages a `0.1.0-dev` test artifact and
build-validates the image, but publishes nothing (no Release, no itch.io push, no GHCR push). `docker.yml` can
also be run on its own from the Actions tab to build/push the image ad-hoc.

**The git tag is the single source of truth for the version.** It flows into GameCI `versioning: Custom` →
`PlayerSettings.bundleVersion`, so the in-game UI (`AppShell.Version` is a property `=> Application.version`,
**not** a hardcoded const), the launcher (`-p:Version`) and Velopack (`--packVersion`) all show the same
value. `BuildScript` writes `version.txt`; a CI guard fails the build if the baked version ≠ the tag. The
committed `bundleVersion` is `0.1.0-dev` for local/dev builds. Keep `Networking/Protocol.Version` (wire
compatibility) separate — it is not the game version. Gotchas if you touch this: GameCI *always* overrides
`bundleVersion` (drive it via `versioning: Custom`, don't fight it with `-buildVersion`); Velopack needs
`packVersion >= 0.0.1` (so dev is `0.1.0-dev`, not `0.0.0-*`); after `git push` wait ~20 s before
`gh workflow run` or it dispatches the previous commit. Linux/macOS *client* installers are intentionally not
built (blocked by the Windows-only UnityWebBrowser/CEF engine on macOS; Linux now ships with the linux.x64 CEF engine).

## Project conventions

- C#: `LangVersion=latest`, nullable enabled, 4-space indent, Allman braces
  (see `.editorconfig`). Records/`init` work on netstandard2.1 via the
  `IsExternalInit` polyfill in `BlocksBeyondTheStars.Shared/Compatibility`.
- **Line endings are LF** everywhere — even on Windows. `.gitattributes` (`* text=auto eol=lf`) checks out
  LF on all platforms and `.editorconfig` (`end_of_line = lf`) must match it, or `dotnet format` fails on a
  fresh checkout. Don't reintroduce CRLF; modern Windows tooling handles LF fine.
- The author is JAM Software; follow sensible, consistent C# conventions.
- **Automated checks on every PR** (see [docs/developer/DEVELOPER.md](docs/developer/DEVELOPER.md) §CI):
  `ci.yml` builds + tests with `-warnaserror` (Roslyn/Meziantou/VS.Threading analyzers as errors = the C#
  syntax/static-analysis gate) **and** runs `dotnet format --verify-no-changes` (C# style); `lint.yml` runs
  ruff (Python), `node --check` (web JS) and actionlint (workflows); `codeql.yml` does security/quality
  scanning. **All six are *required* status checks on `main`** — a PR can't merge until they pass — so run
  the equivalents locally before pushing (next section).

## Git workflow (mandatory)

The repository is **public** and `main` is **branch-protected** — direct pushes to `main` are rejected.
Every change ships through a **pull request**:

1. **Branch off `main`** for each change: `git switch -c <type>/<short-topic>` (`feat/…`, `fix/…`, `docs/…`, `ci/…`).
2. **Commit on the branch** with a conventional-commit message, ending each message with the
   `Co-Authored-By: Claude …` trailer.
3. **Push the branch and open a PR** (`gh pr create` with a clear title + body). Never push to `main` directly.
4. **Merge via the PR** once the required checks pass (`gh pr merge`). `main` requires 1 approval **and** the
   six status checks (build+test, format, ruff, web-js, actionlint, CodeQL); an owner can `--admin`-override in
   an emergency. Do not force-push or rewrite shared history on `main`.

Other agents may share this clone, so **stage only your own files** (`git add <paths>` — never `git add -A`),
and never sweep another worker's in-progress changes into your commit.

## Before every commit and push (mandatory)

Documentation must never drift behind the code. Before *every* commit, and again before *every* push,
run through this checklist:

1. **Update [TODO.md](TODO.md) — always.** It is the single source of truth for Done/Open. Before
   committing, reflect the current state there: mark finished work ✅ (with the date and, once pushed,
   the commit hash), add anything newly discovered to the backlog, and correct any status that the change
   makes stale. A commit that changes behaviour but leaves TODO.md untouched is incomplete.
2. **Decide whether the docs in [docs/](docs/) need to change — decide this yourself.** Ask: does this
   change alter *how something works* in a way an existing doc describes (e.g. [docs/user/USER_MANUAL.md](docs/user/USER_MANUAL.md),
   [docs/developer/DEVELOPER.md](docs/developer/DEVELOPER.md), [docs/developer/SELF_HOSTING.md](docs/developer/SELF_HOSTING.md), a concept/design
   doc, or an ADR)? If yes, update that doc in the same commit. Player-facing controls/mechanics/commands
   changes **must** update `USER_MANUAL.md`.
3. **Decide whether a new doc is warranted.** If the change introduces a whole subsystem or a non-obvious
   "how it is done" that no existing doc covers, add a new doc under `docs/`. Keep `docs/` to *documentation
   and design/how-it-works notes* — not throwaway pre-implementation checklists (their status lives in
   TODO.md). When you add a new doc, **also update the root [README.md](README.md)** if it lists or links
   the docs, so the new doc is discoverable.
4. **When in doubt, ask the user.** If it is unclear whether a doc should be updated, rewritten, or newly
   created — or whether a stale plan should be archived — surface it and ask before committing, rather than
   guessing.

## Roadmap

See [TODO.md](TODO.md) for the current Done/Open status (the single status doc).
