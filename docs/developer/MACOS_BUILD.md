# macOS Build — How It Works (Experimental)

Status: **Experimental** (2026-06-27). The Unity client can be built as a `StandaloneOSX` `.app` bundle and
is published as a portable zip from the release CI. It is **unsigned and un-notarized** — a preview build.

This builds directly on the [Linux port](LINUX_PORT.md): all the cross-platform groundwork (non-`.exe`
binary names, the console launcher, the bundled-server flow) already covers macOS. Only the build target,
a CI job pair and the packaging differ.

## Why it was cheap to add

- **Mono scripting backend.** The client uses Unity's Mono backend (`scriptingBackend: {}` is empty in
  `client/ProjectSettings/ProjectSettings.asset` → the default, Mono). Mono **cross-compiles** the
  `StandaloneOSX` target, so GameCI builds the `.app` on the existing **Linux** runner — no Mac hardware in
  CI. (If anyone ever switches the backend to IL2CPP, this breaks: IL2CPP for macOS requires a `macos-latest`
  runner.)
- **No native plugins.** Everything in `client/Assets/Plugins/` is managed .NET (Concentus is pure C#), so
  there is nothing to port to `.dylib`/`.bundle`.
- **UWB/CEF is gone.** The embedded browser (Windows-only) was removed in the Stream-D refactor
  (see [adr/0009](adr/0009-embedded-browser-wiki-arcade.md)); the Wiki/Arcade are now native uGUI/Canvas2D,
  which is platform-agnostic.

## What was done

### 1. BuildScript.cs — BuildMacOS()

A new editor build method, parallel to `BuildWindows()`/`BuildLinux()`:

```csharp
[MenuItem("BlocksBeyondTheStars/Build macOS Player")]
public static void BuildMacOS()
    => BuildPlayer(BuildTarget.StandaloneOSX, "BlocksBeyondTheStars.app", "Build/macOS");
```

The shared `BuildPlayer()` handles version injection, shader prewarming, scene generation and icon embedding
for all three targets. `version.txt` is written next to the `.app` (the CI version guard reads it there).

**macOS gotcha — Microphone Usage Description.** Voice chat (`BBS_VOICE`) uses Unity's `Microphone` API.
On macOS/iOS, Apple requires a non-empty `NSMicrophoneUsageDescription`; if it is empty, the `StandaloneOSX`
post-processor **fails the build** with *"Microphone class is used but Microphone Usage Description is empty
in Player Settings."* (Windows/Linux don't gate on it, so this only surfaces on a real macOS build.)
`BuildPlayer()` therefore sets `PlayerSettings.macOS.microphoneUsageDescription` for the `StandaloneOSX`
target before building — idempotent, and a no-op on the other platforms.

### 2. Bundled local server (osx-x64)

`publish-local-server.sh` already takes a runtime argument (`${1:-linux-x64}`), so `publish-local-server.sh
osx-x64` publishes the self-contained single-file `BlocksBeyondTheStars.GameServer` into
`client/Assets/StreamingAssets/server/`. Unity bakes `StreamingAssets/` into the `.app` under
`Contents/Resources/Data/StreamingAssets/`. At runtime `LocalServerLauncher.cs` resolves the non-`.exe`
binary name on macOS and launches it for Singleplayer — the same path Linux uses.

### 3. CI/CD Pipeline

`release.yml` gained two jobs alongside the Windows/Linux ones:

- **build-player-mac:** builds the Unity `StandaloneOSX` player via GameCI's Docker image (Mono cross-build),
  publishes the `osx-x64` bundled server first, produces the `player-mac` artifact. A step-for-step clone of
  `build-player-linux` (only the target, build method, server runtime, cache key and out-dir differ). The
  Phase-1 build deliberately **omits the console-splash launcher**.
- **package-mac:** restores the Unix execute bits (artifacts strip them — see below), then `zip -ry`s the
  `.app` into `…-osx-…-Portable.zip` and attaches it to the GitHub Release on a tag. No Velopack.

### 4. The execute-bit fix (important)

GitHub's `upload-/download-artifact` **does not preserve the Unix execute bit**. After the player artifact
round-trips through `package-mac` (and `package-linux`), both the entry binary and the **bundled local
server** lose `+x`. Nothing in the launcher or `LocalServerLauncher.cs` restores it, so without a fix the
server would fail to start with `EACCES` even though the file is present. Both package jobs therefore
`chmod +x` the entry binary **and** the bundled server before packing:

```bash
# package-mac
APP="client/Build/macOS/BlocksBeyondTheStars.app"
chmod +x "$APP/Contents/MacOS/"*
chmod +x "$APP/Contents/Resources/Data/StreamingAssets/server/BlocksBeyondTheStars.GameServer"
```

The macOS zip uses `zip -y` to preserve the symlinks inside the `.app` bundle (a plain re-zip would corrupt
the bundle).

## How to build locally

```bash
# Prerequisites: Unity 6000.4.x with the Mac-Mono build module + .NET 8 SDK
./scripts/sync-client-libs.sh
./scripts/sync-velopack-libs.sh
./scripts/publish-local-server.sh osx-x64
# Then run the Unity batch build with the macOS build method, e.g.:
#   <Unity> -batchmode -quit -nographics -projectPath client \
#     -buildMethod BlocksBeyondTheStars.Client.EditorTools.BuildScript.BuildMacOS \
#     -buildOut <out-dir>
```

## Fast CI iteration — the slim `macos-build.yml`

`release.yml` builds all platforms in parallel, but GitHub only frees a job's logs once the **whole** run
finishes — so debugging a Mac build there means waiting on the Windows/Linux jobs too. For a tight loop,
use **`.github/workflows/macos-build.yml`**: a dispatch-only workflow with a **single** job that builds just
the `StandaloneOSX` player, packages the `.app` into a portable zip and uploads it as a run artifact
(`mac-portable-zip`). Nothing is published. Because it is the only job, the run completes — and the logs
unlock — the moment the Mac build does.

Trigger it from the Actions tab ("macOS build (experimental)" → Run workflow), or:

```bash
gh workflow run macos-build.yml --ref <branch>
```

(It is a trimmed clone of `release.yml`'s `build-player-mac` + `package-mac`. The cleaner long-term shape is
a reusable `workflow_call` shared by both; the clone is the pragmatic interim.)

## Running an unsigned build

The `.app` is not signed or notarized, so macOS Gatekeeper quarantines it. After unzipping, either
right-click the app → **Open** (confirm once), or clear the quarantine flag:

```bash
xattr -dr com.apple.quarantine "BlocksBeyondTheStars.app"
```

The bundle is **Intel x64**; it runs on Apple Silicon through Rosetta 2.

## Known limitations / next steps

- **Limited hands-on testing.** There is no Mac in CI, so the build is verified to *compile and package*, not
  to *run*. The bundled-server launch (the `+x` failure mode) and Singleplayer should be smoke-tested on a
  real Mac before this is promoted beyond "experimental". (MacOS-only build-time issues do still surface in
  CI — e.g. the Microphone Usage Description failure above was caught by the first dry-run, not on a Mac.)
- **No signing/notarization.** Required for friction-free distribution outside the App Store; needs an Apple
  Developer account and a CI signing step. Out of scope for the experimental phase.
- **No Velopack / no auto-update / no splash launcher** on macOS yet (Phase 1 ships a plain zip).
- **Intel-only.** A native `osx-arm64` or Universal binary is a later step; for now Apple Silicon uses
  Rosetta 2.
- **itch.io.** `publish-itch.ps1` only has Windows channels; a macOS channel can be added later.

## Architecture decisions

| Decision | Rationale |
|----------|-----------|
| Cross-build on Linux, no Mac runner | The Mono backend cross-compiles `StandaloneOSX`; keeps CI cost/complexity flat |
| Plain zip, not Velopack (Phase 1) | Velopack's macOS packaging leans on signing; a zip ships something runnable today |
| Explicit `chmod +x` in packaging | Deterministic; does not rely on artifact round-trip or `appimagetool`/`vpk` preserving perms |
| Reuse the console launcher path later | The cross-platform launcher already exists; the bundle-vs-exe handoff is deferred to Phase 2 |
