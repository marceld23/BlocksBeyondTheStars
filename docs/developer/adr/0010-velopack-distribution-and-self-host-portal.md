# ADR 0010 — Velopack distribution and self-host portal

- **Status:** Accepted
- **Date:** 2026-06-19
- **Context source:** `SELF_HOSTING.md`

## Context

Players self-host their own servers, and friends need an easy way to get and update the matching
client. We must decide how the client is distributed and updated, and how a LAN host hands the
client to its players.

## Decision

1. **Client distribution and auto-update use Velopack.** `ClientUpdater` performs in-app updates
   via Velopack's `UpdateManager`, pulling from the host's update feed.
2. **A WinForms launcher is the Velopack `--mainExe`.** `BlocksBeyondTheStars.Launcher` runs the
   Velopack lifecycle hooks (`VelopackApp.Build().Run()`) first, then shows an instant splash and
   starts the Unity game exe.
3. **The host serves the client from its own web portal.** `BlocksBeyondTheStars.Api`
   (ASP.NET Core minimal API) exposes `/portal` (a landing page with the one-click download and
   the update-feed URL), `/download` (the latest Velopack `Setup.exe`) and `/updates` (the static
   update feed under `<install>/clients`).
4. **Self-contained, no .NET install required** on the host (Windows / Linux / Raspberry Pi 5).

## Consequences

- A host can point friends at `http://<host>/portal` to download, install and later auto-update
  the client, with no app store or separate distribution infrastructure.
- The launcher must stay the `--mainExe` so Velopack's install/update hooks run before the game;
  it also provides the instant pre-Unity splash.
- The Api project owns the distribution surface (`/portal`, `/download`, `/updates`), so the
  `clients` directory must hold the published `Setup.exe` + feed for the portal to work.

## Addendum (2026-06-20) — GitHub Release packaging + version source of truth

Public, downloadable builds are now also produced in CI alongside the self-host portal flow:

- **The release CI ([`.github/workflows/release.yml`](../../../.github/workflows/release.yml)) runs
  `publish-client-installer.ps1 -Msi` on a tag push** and attaches the Velopack **trio** to a GitHub Release:
  `…-win-Setup.exe` (per-user), `…-win.msi` (machine-wide WiX, for IT/MDM) and `…-win-Portable.zip`. This does
  not change the self-host portal; it is an additional distribution channel. (Linux/macOS *client* installers
  are out of scope — blocked by the Windows-only UWB/CEF engine, see ADR 0009.)
- **The git tag is the single source of truth for the version.** It drives GameCI `versioning: Custom` →
  `PlayerSettings.bundleVersion`, so in-game `AppShell.Version` (now `=> Application.version`), the launcher
  (`-p:Version`) and Velopack (`--packVersion`) all match, enforced by a CI guard. Velopack requires
  `packVersion >= 0.0.1`, so the committed dev value is `0.1.0-dev`. See
  [DEVELOPER.md → Releases & versioning](../DEVELOPER.md#releases-github-actions--versioning).
