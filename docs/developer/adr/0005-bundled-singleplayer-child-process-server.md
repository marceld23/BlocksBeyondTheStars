# ADR 0005 — Bundled single-player runs the server as a loopback child process

- **Status:** Accepted
- **Date:** 2026-06-19
- **Context source:** `CLIENT_COMPLETION.md`, `ARCHITECTURE.md`
- **Amendment (2026-07-12):** server projects retargeted `net8.0` → `net10.0` (see ADR 0001
  amendment). The Mono-boundary reasoning below is unchanged — it applies to any modern .NET.
- **Amendment (2026-07-15):** Option B shipped — for the BROWSER. `GameServer` and `Persistence`
  are now multi-target (`net10.0` exe/SQLite + `netstandard2.1` library); WebGL runs the REAL
  server in-process (`BrowserLocalServer` pumps `Tick()` over the `LoopbackTransport`) on the fully
  managed `MemoryWorldRepository` (gzip'd JSON snapshot blob → IndexedDB + optional Glitch Cloud
  Save). **Desktop single-player deliberately KEEPS the child process below** — native SQLite saves,
  process isolation and the LAN-host path all favor it; in-process is the answer where a child
  process is impossible (browsers), not a replacement.

## Context

ADR 0001 §3 envisioned single-player as the same server hosted *in-process* over a loopback
transport. In practice the server projects (`GameServer`, `Persistence`) are `net8.0` with native
SQLite and cannot run inside Unity's Mono runtime, while the shared libraries are `netstandard2.1`.
We must decide how bundled single-player actually hosts the authoritative server.

## Decision

1. **Single-player launches the published dedicated server as a separate child process bound to
   loopback (Option A), not hosted inside the Unity process.** This refines ADR 0001 §3: same
   authoritative server, but a child process on `127.0.0.1` instead of in-process.
2. **The server exe is bundled in StreamingAssets.** `scripts/publish-local-server.ps1` publishes
   `BlocksBeyondTheStars.GameServer.exe` into `client/Assets/StreamingAssets/server/`.
3. **`LocalServerLauncher` starts it** with `System.Diagnostics.Process` on `127.0.0.1` (default
   port 31550 / first free port), pipes its stdout/stderr to the Unity log, and stops it on quit.
   The normal `NetworkClient` then connects — identical to multiplayer.
4. **"Host Game" (open-to-LAN) and "Join Server" use the same client and the same server binary.**

## Consequences

- Single-player runs the real, unchanged authoritative server, so there is one game-logic code
  path and anti-cheat stays correct by construction.
- The native-SQLite / net8.0 vs. Mono boundary is sidestepped without retargeting the server.
- The trade-off vs. ADR 0001's in-process ideal: an extra process to spawn, manage and shut down
  cleanly, plus the bundled exe inflates the client install. (A true in-process server retargeted
  to `netstandard2.1` — Option B — was left as a later optimization.)
