# Web Client Feasibility (Unity WebGL)

Status of the browser-client requirement (`anf_webclient.md`). The architecture is
prepared; a full WebGL build is a later step (the Unity Editor is required to produce it).

## Decision

**Feasible with constraints — ship as a "Lite" browser client later.** The server side is
ready now; the Unity WebGL build and reduced-quality client come after the native client
is solid.

## Why the architecture already supports it

- **Transport abstraction.** `IServerTransport`/`IClientTransport` decouple game logic from
  the wire. Native clients use `LiteNetLibTransport` (UDP); browsers use the new
  `WebSocketServerTransport`. Both carry identical `NetCodec` payloads, so the **same
  authoritative server** serves both.
- **Composite transport.** `CompositeServerTransport` runs UDP + WebSocket together on the
  gameplay port, giving a single connection space — mixed native/browser servers work.
- **Server is authoritative.** The browser client decides nothing (anf_webclient.md §9), so
  no client-type-specific trust rules are needed.

## Constraints (browser vs native)

| Area | Note |
|---|---|
| Networking | No raw UDP in browsers → **WebSocket** (implemented). WebRTC optional later. |
| Performance | Fewer chunks, lower view distance, simpler effects → a **Lite** profile. |
| Load time | WebGL download + warmup; use compressed asset bundles, a loading screen. |
| Memory | Tighter heap; cap loaded chunks. |
| Input | Mouse+keyboard on desktop browsers (Chrome/Edge first); pointer-lock needed. |
| Mobile | Out of scope initially. |

## Prototype acceptance criteria (from §17.2)

- Unity WebGL build starts in Chrome/Edge desktop.
- Connects to a local/LAN server via WebSocket.
- Small block world is playable; mine/place validated server-side.
- No critical memory/crash issues; constraints documented.

## What is implemented now

- `WebSocketServerTransport` (browser-compatible, same protocol) + tests.
- `CompositeServerTransport` (UDP + WS).
- Server config `EnableWebSocket` / `WebSocketBindAddress`.
- Server web portal (`/portal`, `/play`) in the API.
- **Native client distribution from the server** (the §7 "download the client from the host" goal):
  a Velopack installer + auto-update feed (`scripts/publish-client-installer.ps1`), served by the API at
  `/download` (Setup.exe) and `/updates` (feed), with an in-app updater (`ClientUpdater`) and a polished
  `/portal` carrying both logos. See [SELF_HOSTING.md](SELF_HOSTING.md) §9. *(The browser/WebGL `/play`
  path below is still the remaining piece.)*

## Remaining (Unity-side, needs the Editor)

- A `WebSocketClientTransport` (Unity, using browser WebSocket via jslib) implementing
  `IClientTransport`.
- A Unity WebGL build profile (Lite quality) and asset bundling.
- Serving the built WebGL files from `/play`.
- Version negotiation so the served client matches the server.
