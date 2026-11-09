# Linux Port — How It Works

Status: **MVP complete** (2026-06-23). The .NET server, build scripts, console launcher and CI pipeline
support Linux. The Unity client can be built as a `StandaloneLinux64` player.

## What was done

The Linux port touches every layer of the project. The goal was to make the **full game** build and run on
Linux — not just the server (which already worked). The strategy was: keep the existing Windows path
untouched, and add parallel Linux paths alongside it.

### 1. Cross-platform binary names

**Problem:** Unity scripts hardcoded `.exe` checks (`RuntimePlatform.WindowsPlayer`).

**Fix:** `LocalServerLauncher.cs` and `ServerRoundtripPlayModeTests.cs` now use a ternary: `.exe` is appended
only on `RuntimePlatform.WindowsPlayer` or `WindowsEditor`. On any other platform (Linux, macOS) the binary
name has no extension — Unity's `System.Diagnostics.Process` handles this natively.

### 2. BuildScript.cs — BuildLinux()

The Unity editor build script previously only had `BuildWindows()` (hardcoded `BuildTarget.StandaloneWindows64`,
`.exe` output path). A new `BuildLinux()` method was added:

```csharp
[MenuItem("BlocksBeyondTheStars/Build Linux Player")]
public static void BuildLinux()
{
    BuildPlayer(BuildTarget.StandaloneLinux64, "BlocksBeyondTheStars.x86_64", "Build/Linux");
}
```

The shared `BuildPlayer()` method handles version injection, shader prewarming, scene generation, and icon
embedding for both targets.

### 3. Linux Console Launcher

**Problem:** Windows has a WinForms loading-splash launcher (`src/BlocksBeyondTheStars.Launcher/`) — it
shows a graphical splash, runs Velopack hooks, then launches the Unity player. WinForms doesn't run on Linux.

**Solution:** A new `src/BlocksBeyondTheStars.Launcher.Console/` project (net10.0, cross-platform) replaces it
on Linux:

- **Program.cs:** runs Velopack lifecycle hooks, prints "Loading..." to the terminal, starts the Unity
  player as a child process (`BlocksBeyondTheStars.x86_64`), waits for exit, forwards the exit code.
- **SplashRenderer.cs:** uses SkiaSharp (3.x) to render the splash art to a PNG for the Velopack installer.
  The procedural art (dark gradient, stars, title, progress bar) mirrors the Windows launcher's appearance.

### 4. Bash Build Scripts

The critical-path Windows PowerShell scripts were ported to bash:

| Script | Purpose |
|--------|---------|
| `sync-client-libs.sh` | Publishes netstandard2.1 shared libs into `client/Assets/Plugins/`, copies `data/` into `StreamingAssets/data/` and the music library `client/Music/*.mp3` into `StreamingAssets/music/` |
| `sync-velopack-libs.sh` | Vendors Velopack runtime DLLs into Plugins |
| `publish-local-server.sh` | Publishes GameServer for `linux-x64` into `client/Assets/StreamingAssets/server/` |
| `build-client.sh` | Full Linux client build (prerequisites + Unity batch build + console launcher) |
| `run-tests.sh` | Selectable .NET test runner (Dotnet + ClientCore suites) |

### 5. CI/CD Pipeline

The `release.yml` workflow gained two new jobs alongside the existing Windows jobs:

- **build-player-linux:** builds the Unity `StandaloneLinux64` player via GameCI's Docker image, produces
  `player-linux` artifact
- **package-linux:** packages the Linux player with Velopack (`vpk`) into an AppImage (plus the update feed
  `releases.linux.json` + full `.nupkg`), published to the GitHub Release. There is no Linux portable zip — the
  AppImage is its own portable form.

The `BlocksBeyondTheStars.CI.slnf` solution filter now includes `Launcher.Console` (it targets plain net10.0
and builds on Linux, unlike the WinForms launcher which is net10.0-windows).

**Execute-bit fix:** GitHub's `upload-/download-artifact` strips the Unix execute bit, so after the player
artifact round-trips into `package-linux` every program in it has lost `+x`. `package-linux` therefore
`chmod +x`s all three before `vpk pack`:

- `BlocksBeyondTheStars.Launcher.Console` — vpk's `--mainExe`, the file the AppImage's `AppRun` execs. Without
  `+x` the AppImage dies at once with `…/usr/bin/BlocksBeyondTheStars.Launcher.Console: Permission denied`. This
  line was missing from the first Linux release until #3451, so **every AppImage up to v2026.11.14 could not
  start** (workaround for those: `--appimage-extract`, `chmod +x` the launcher in `squashfs-root/usr/bin/`, run
  `squashfs-root/AppRun`);
- `BlocksBeyondTheStars.x86_64` — the Unity player the launcher starts;
- `…_Data/StreamingAssets/server/BlocksBeyondTheStars.GameServer` — otherwise Singleplayer's `Process.Start` on
  the server fails with `EACCES` inside the AppImage.

**Guard (#3451):** right after `vpk pack`, the step *Verify AppImage executables* unpacks the finished AppImage
(`--appimage-extract`, no FUSE needed — the runtime is static) and fails the release if any of those three is
not executable, or if any other ELF program inside lost its execute bit. A program added to the package later
therefore cannot ship without `+x` silently; add its `chmod +x` to the step above when the guard names it.
The same execute-bit care applies to the macOS package job — see [MACOS_BUILD.md](MACOS_BUILD.md).

The AppImage uses the static type-2 runtime, so it does **not** need `libfuse2` (confirmed on Arch with only
fuse3 installed); where FUSE is unavailable altogether, `--appimage-extract-and-run` still works.

## How to build

See [DEVELOPER.md](DEVELOPER.md) (§ Building the Linux client) for the full guide.

Quick start:

```bash
# Prerequisites: the project's Unity version (6000.6.x) as Linux Editor + .NET 10 SDK

# One-command full build:
./scripts/build-client.sh --unity-path /path/to/Unity

# Or step-by-step:
./scripts/sync-client-libs.sh
./scripts/sync-velopack-libs.sh
./scripts/publish-local-server.sh
./scripts/build-client.sh --skip-prereqs
```

## Known limitations

- **No graphical splash** on Linux — the console launcher is terminal-only. The pre-engine startup gap
  is just a "Loading..." text line. A future enhancement could use SDL2 or Avalonia for a graphical splash.
- **No MSI installer** on Linux — Velopack produces only the AppImage. Deb/rpm are possible but
  not yet configured.
- **The Linux player was tested only on Ubuntu/Debian** (the CI runner OS). Other distros may need
  additional system libraries (e.g., `libc++`, `libdecor`).

## Architecture decisions

| Decision | Rationale |
|----------|-----------|
| Console launcher, not GUI | Simplest cross-platform approach; Unity handles its own window after launch |
| SkiaSharp for splash rendering | Cross-platform, MIT-licensed, works on all .NET platforms; replaces `System.Drawing` |
| Separate project, not ifdefs | The WinForms launcher stays untouched; the console launcher lives in its own project |
| Bash scripts, not pwsh | Pure bash runs everywhere without PowerShell Core; critical path only |
