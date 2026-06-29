# Web client (Unity WebGL) — feasibility decision

Status: feasible and locally proven for hosted WebGL · 2026-06-29
(2026-06-29 update: the WebGL client now joins a hosted authoritative server through browser WebSockets, uses a
JSON `NetCodec` envelope for WebGL/AOT, loads `StreamingAssets/data` through the generated manifest cache, and
renders authoritative chunks locally against a Docker PostgreSQL-backed server.)

## Decision

A browser client is **feasible with constraints** and should ship as a reduced-quality hosted "Lite" profile.
The basic hosted gameplay lane now works locally: the WebGL player uses a browser WebSocket client transport to
talk to the same authoritative .NET server that desktop clients use. The remaining work is production hardening:
asset size, memory, longer browser soak tests, and official release-channel deployment.

## Why the architecture already supports it

- **Transport abstraction.** `IServerTransport` / `IClientTransport` decouple game logic from the
  wire. Native clients use `LiteNetLibTransport` (UDP); browsers use `BrowserWebSocketClientTransport` against
  `WebSocketServerTransport`.
- **Protocol edge adaptation.** Native clients keep the MessagePack `NetCodec` payloads. Browser clients use a
  tagged JSON `NetCodec` envelope at the WebSocket edge so Unity WebGL/IL2CPP does not depend on MessagePack
  contractless runtime formatter generation.
- **Composite transport.** `CompositeServerTransport` runs UDP + WebSocket together on the gameplay
  port, giving one connection space for mixed native/browser play.
- **Server is authoritative.** The browser client decides nothing, so no client-type-specific trust
  rules are needed.

## What is implemented now

- `WebSocketServerTransport` (browser-compatible, same protocol) + tests.
- `CompositeServerTransport` (UDP + WS).
- Server config `EnableWebSocket` / `WebSocketBindAddress`.
- `BrowserWebSocketClientTransport` for Unity WebGL, backed by `client/Assets/Plugins/BbsWebSocket.jslib`.
- WebSocket bridge support for `ArrayBuffer`, typed-array views, strings, and `Blob` frames, fixing the
  `FileReader.readAsArrayBuffer` crash when the browser receives a non-Blob frame.
- JSON `NetCodec` envelope for browser WebSocket clients, with server-side conversion from the native
  MessagePack send path.
- Server web portal in the API: `/portal` landing page, `/play` browser-client placeholder.
- **Native client distribution from the server** (the "download the client from the host" goal): a
  Velopack installer + auto-update feed (`scripts/publish-client-installer.ps1`), served at `/download`
  (Setup.exe) and `/updates` (feed), with an in-app `ClientUpdater`. See `SELF_HOSTING.md` §9.
- **WebGL build lane:** `BuildScript.BuildWebGL()` can produce a browser player folder. Startup handles
  WebGL's HTTP-backed StreamingAssets by caching the JSON content locally before the shared content loader runs,
  and preserves the shared/networking/client metadata that reflection-based JSON loading needs under IL2CPP. The
  WebGL shell logs the cached `StreamingAssets/data` file count and loaded content counts.
- **Hosted server/database lane:** SQLite remains the default for local/native worlds; PostgreSQL is opt-in for
  hosted realms and has a real Docker-backed smoke test path.

## Browser smoke diagnosis (2026-06-29)

The first hosted-browser smoke loaded the Unity payload and all `StreamingAssets/data` JSON successfully: the
captured HAR had no failed requests, and the manifest plus every listed content file returned 200/304. The
"empty world" screenshot was not an asset-load failure. It came from the old native gameplay path running inside
a browser: Singleplayer/Host tried to find a bundled native server under the HTTP-backed
`Application.streamingAssetsPath`, and Join/boot still constructed the default LiteNetLib UDP client.

The local fix is now verified: WebGL joins through `BrowserWebSocketClientTransport`, the server speaks
WebSockets with a browser JSON envelope, and authoritative chunks render in the browser against a real .NET
server using PostgreSQL. Provider-specific deployment wiring is intentionally left for a separate follow-up.

## Key files

- `src/BlocksBeyondTheStars.Networking/Transport/WebSocketServerTransport.cs`
- `src/BlocksBeyondTheStars.Networking/Transport/CompositeServerTransport.cs`
- `client/Assets/BlocksBeyondTheStars/Scripts/BrowserWebSocketClientTransport.cs`
- `client/Assets/Plugins/BbsWebSocket.jslib`
- `src/BlocksBeyondTheStars.Networking/NetCodec.cs`
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

## Remaining hardening

- A validated Unity WebGL Lite profile + asset bundling/shrinking. The build method exists and local play works,
  but the full asset/runtime profile is still too large for a polished public browser launch.
- ~~The in-game UWB wiki/arcade browser content is lost under WebGL.~~ **Resolved (2026-06-26):** the
  "Stream D" refactor replaced the embedded browser. The Wiki is now native uGUI (`WikiUI.cs`) and the
  Arcade runs an engine-free C# `MinigameHost` (`Client.Core/Minigames`) rendering Canvas2D→Texture2D→
  RawImage (`MinigameHostUI.cs`). No UWB/CEF plugin remains and `BBS_UWB` is undefined, so both screens
  are already WebGL-compatible — this is no longer a blocker (and it also unblocked the Linux build).
- ~~A `WebSocketClientTransport` for Unity WebGL.~~ **Resolved (2026-06-29):** browser builds now use
  `BrowserWebSocketClientTransport` + `BbsWebSocket.jslib`.
- ~~MessagePack `ContractlessResolver` does not survive IL2CPP AOT.~~ **Resolved for WebGL transport
  (2026-06-29):** browser WebSocket clients use the JSON `NetCodec` envelope; native UDP remains MessagePack.
- ~177 MB of `Resources` would have to be downloaded/streamed — shrink + bundle first.
- Serving the built WebGL files from `/play` + version negotiation so the served client matches the
  server.
- Longer production browser soak on the official hosting target.

## Bottom line

Treat the browser client as a **hosted Lite** path, not a replacement for the native desktop build. The largest
unknowns are no longer basic networking or IL2CPP serialization; they are production browser polish: download
size, memory limits, longer play sessions, deployment versioning, and accepting that WebGL is multiplayer/server
hosted rather than in-browser singleplayer.
