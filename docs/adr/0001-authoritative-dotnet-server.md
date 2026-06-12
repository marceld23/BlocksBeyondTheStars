# ADR 0001 — Authoritative .NET server, Unity client

- **Status:** Accepted
- **Date:** 2026-05-30
- **Context source:** `technische_anforderungen.md`

## Context

Blocks Beyond the Stars must be a 3D Windows game that becomes multiplayer-capable and self-hostable
(including on a Raspberry Pi 5) without a painful rewrite. We must decide where game truth
lives, what runs the server, and how data is stored and exchanged.

## Decision

1. **The server is authoritative; the client is presentation + input.** The Unity client
   sends *intents*; the .NET server validates them and broadcasts authoritative *state*.
2. **The server is a standalone .NET 8 process — no Unity runtime.** This keeps it
   lightweight enough for ARM64 / Raspberry Pi 5 and free of rendering/physics-engine
   dependencies.
3. **Singleplayer = the same server in-process** (loopback transport), so there is a
   single game-logic code path.
4. **SQLite is the default database**, with a `IWorldRepository` abstraction so PostgreSQL
   can be added later for larger servers.
5. **LiteNetLib (UDP) + MessagePack** for realtime networking; HTTP for the admin UI.
6. **Shared game logic targets `netstandard2.1`** so the identical code (definitions,
   world generation) runs in both Unity and the server.
7. **World = seed + parameters + player deltas**; only edits are persisted.
8. **Content is data-driven** (`data/*.json`); **player-facing text is localized**
   (en/de). Developer docs and comments are English.

## Consequences

- Cheating is structurally limited and multiplayer/LAN/self-hosting are correct by
  construction.
- Two runtimes (Unity, .NET) share `netstandard2.1` libraries, so we avoid duplicate
  logic but must keep those libraries free of net8-only / Unity-incompatible APIs.
- A small `IsExternalInit` polyfill is needed for `record`/`init` on netstandard2.1.
- Block numeric ids are currently assigned deterministically from sorted keys; a future
  change will persist the palette into the save to stay save-compatible when content
  changes.
