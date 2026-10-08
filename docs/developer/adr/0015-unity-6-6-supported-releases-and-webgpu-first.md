# ADR 0015 — Unity 6.6, the Supported-release cadence, and WebGPU first in the browser

- **Status:** Accepted
- **Date:** 2026-10-09
- **Context source:** [#2389](https://github.com/marceld23/BlocksBeyondTheStars/issues/2389) (the engine upgrade),
  [#2390](https://github.com/marceld23/BlocksBeyondTheStars/issues/2390) (WebGPU), both part of the atmosphere
  package epic [#2408](https://github.com/marceld23/BlocksBeyondTheStars/issues/2408)

## Context

The client was built with Unity 6000.4.9f1 and documented as "Unity 6 LTS". That was wrong: Unity's long-term
line is 6000.3; 6.4, 6.5 and 6.6 are *Supported* releases that receive fixes only until the next Supported
release ships. 6.4 stopped being patched on 2026-06-17, 6.5 on 2026-09-02; 6.6 has been current since
2026-08-31. Staying on 6.4 meant shipping an unpatched editor and player; moving back to the 6.3 LTS would be a
downgrade of an already-migrated project.

Unity 6.6 is also the release in which the WebGPU graphics API stops being experimental. The browser build
matters to this project — the school club plays it, `/play` and the glitch.fun store serve it — and WebGL 2 has
no compute, no GPU particles and no modern upscaling.

## Decision

1. **The client follows Unity's Supported cadence.** The project is on 6000.6.5f1 now, and the pin
   (`client/ProjectSettings/ProjectVersion.txt`, the `unityVersion:` lines of the GameCI workflows) moves to the
   next Supported release when it ships and its GameCI images exist — not to an LTS. The build scripts find the
   matching editor through `scripts/resolve-unity.ps1` (Hub default folder, a per-user
   `%USERPROFILE%\Unity\Editors\<version>` install, or `UNITY_EDITOR_PATH`), so a machine that cannot install
   into Program Files still builds.
2. **WebGPU is first in the web graphics-API list, WebGL 2 the fallback, for every player at once.** The list is
   set in `BuildScript.ConfigureWebGLPlayer` on every build; `BBS_WEBGL_API=webgl2` pins the old API for an A/B
   build. Unity falls back by itself on browsers without WebGPU, on non-secure contexts and on devices Unity
   excludes.
3. **Everything that WebGL 2 forbids stays forbidden.** Because WebGL 2 remains the fallback, the renderer keeps
   to hand-written dual-pipeline shaders, Shuriken particles and no compute (see ADR 0003 and
   [VFX.md](../VFX.md)); WebGPU-only features are not used until WebGL 2 is dropped by a later decision.
4. **GPU reads go through one door.** `GpuReadback` chooses `ReadPixels` where the API allows it and an
   `AsyncGPUReadback` request on WebGPU, so the photo item, the chat and F1/F2 screenshots and the texture
   editor work on both; new readers use it too.

## Consequences

- Every Unity upgrade is a package upgrade of URP/core/Shader Graph (17.4 → 17.6 this time) and a full
  re-import of `client/Library`; the maintainer's checkout rebuilds its Library once after the merge.
- The browser build's behaviour now differs per browser and device (WebGPU vs WebGL 2). A bug report carries
  `SystemInfo.graphicsDeviceVersion` (`DeviceInfo`), and the player logs the API at start.
- Captures on the Windows player test the WebGPU *code paths* (`-clipReadbackCheck`, the SSR texel loads), not
  the WebGPU backend; only a browser run does that.
- A later decision may drop WebGL 2 once the school club's hardware is known to run WebGPU — that is when
  VFX Graph / compute become available to the web build.
