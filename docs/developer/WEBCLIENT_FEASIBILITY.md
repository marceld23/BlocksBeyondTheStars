# Web client (Unity WebGL) — feasibility decision

Status: feasibility decided — not built · 2026-06-19 (re-verified 2026-06-26: the Wiki/Arcade blocker
is gone after the "Stream D" native-UI refactor — see Constraints/Blockers below)

## Decision

A browser client is **feasible with constraints** and would ship later as a reduced-quality "Lite"
profile. The **server side is ready now**; the actual WebGL build and the Lite client are deferred
because they require the Unity Editor and a meaningful client-side effort. This document records the
decision, the architecture that already supports it, and the remaining blockers — it is not a to-do
list.

## Why the architecture already supports it

- **Transport abstraction.** `IServerTransport` / `IClientTransport` decouple game logic from the
  wire. Native clients use `LiteNetLibTransport` (UDP); browsers would use `WebSocketServerTransport`.
  Both carry identical `NetCodec` payloads, so the **same authoritative server** serves both.
- **Composite transport.** `CompositeServerTransport` runs UDP + WebSocket together on the gameplay
  port, giving one connection space for mixed native/browser play.
- **Server is authoritative.** The browser client decides nothing, so no client-type-specific trust
  rules are needed.

## What is implemented now (server side)

- `WebSocketServerTransport` (browser-compatible, same protocol) + tests.
- `CompositeServerTransport` (UDP + WS).
- Server config `EnableWebSocket` / `WebSocketBindAddress`.
- Server web portal in the API: `/portal` landing page, `/play` browser-client placeholder.
- **Native client distribution from the server** (the "download the client from the host" goal): a
  Velopack installer + auto-update feed (`scripts/publish-client-installer.ps1`), served at `/download`
  (Setup.exe) and `/updates` (feed), with an in-app `ClientUpdater`. See `SELF_HOSTING.md` §9.

## Key files

- `src/BlocksBeyondTheStars.Networking/Transport/WebSocketServerTransport.cs`
- `src/BlocksBeyondTheStars.Networking/Transport/CompositeServerTransport.cs`
- Server API portal (`/portal`, `/play`, `/download`, `/updates`).

## Constraints (browser vs native)

| Area | Note |
|---|---|
| Networking | No raw UDP in browsers → WebSocket (done). WebRTC optional later. |
| Performance | Fewer chunks, lower view distance, simpler effects → a **Lite** profile. |
| Load time | WebGL download + warmup; compressed asset bundles + a loading screen. |
| Memory | Tighter heap; cap loaded chunks. |
| Input | Mouse+keyboard on desktop browsers (Chrome/Edge first); pointer-lock needed. |
| Mobile | Out of scope initially. |

## Open blockers (Unity-side, need the Editor)

- A `WebSocketClientTransport` for Unity (browser WebSocket via jslib) implementing `IClientTransport`.
- A Unity WebGL build profile (Lite quality) + asset bundling.
- MessagePack `ContractlessResolver` does not survive IL2CPP AOT — the wire serialization would need an
  AOT-safe path for the WebGL build.
- ~~The in-game UWB wiki/arcade browser content is lost under WebGL.~~ **Resolved (2026-06-26):** the
  "Stream D" refactor replaced the embedded browser. The Wiki is now native uGUI (`WikiUI.cs`) and the
  Arcade runs an engine-free C# `MinigameHost` (`Client.Core/Minigames`) rendering Canvas2D→Texture2D→
  RawImage (`MinigameHostUI.cs`). No UWB/CEF plugin remains and `BBS_UWB` is undefined, so both screens
  are already WebGL-compatible — this is no longer a blocker (and it also unblocked the Linux build).
- ~177 MB of `Resources` would have to be downloaded/streamed — shrink + bundle first.
- Serving the built WebGL files from `/play` + version negotiation so the served client matches the
  server.

## Bottom line

Treat the browser client as a **~3–5 week** Lite-only sub-project taken on after the native client is
solid (down from 4–6 now that the Wiki/Arcade no longer need re-integration). Nothing on the server
needs to change to start it; the work is entirely the Unity WebGL build and the constraints/blockers
above. The two remaining hard risks are MessagePack-Contractless under IL2CPP/AOT and the absence of
in-browser singleplayer (Lite is multiplayer-only) — validate both as spikes before investing.
