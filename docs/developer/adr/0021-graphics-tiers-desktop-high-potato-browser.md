# ADR 0021 — Graphics tiers: desktop High may go beyond the browser, Potato runs on slow devices, the browser stays

- **Status:** Accepted
- **Date:** 2026-11-21 (decided by the maintainer; recorded 2026-11-22 with the game look that first follows it)
- **Context source:** the maintainer's direction of 2026-11-21 for the new game look ([LOOK.md](../LOOK.md));
  [mission.md](../../strategy/mission.md) (a WebGL tab on a school laptop as a reference target);
  ADR 0003 (custom unlit URP shaders), ADR 0015 §3 (what WebGL 2 forbids stays forbidden).

## Context

The game has four quality presets — Potato, Low, Medium, High — and a browser build (WebGPU first, WebGL 2 as the
fallback, ADR 0015). ADR 0015 §3 kept everything WebGL 2 cannot do out of the renderer. The *Details (High)* group
(parallax, the grass carpet, room reflections, decals, volumetric light — [ADVANCED_GRAPHICS.md](../ADVANCED_GRAPHICS.md))
already gave the desktop client on High extras the browser does not get, but there was no stated rule for how far the
tiers may drift apart, nor for what the slowest devices and the browser must keep.

The new look (micro-block tree crowns, plants as bodies, fur and feathers, depth of field, soft edges on forms) needed a
rule for who gets what. The maintainer's words, 2026-11-21: there shall still be a browser version, but in the desktop
client on High the graphics shall look as good as they can; on Potato the desktop version shall also run on slow devices.

## Decision

1. **Desktop High is the showpiece.** On the desktop client (Windows, Linux) the High preset may use techniques the
   browser cannot run or cannot afford — more geometry, longer ranges, extra passes — as long as the other tiers keep a
   path that works.
2. **Potato exists so the game runs on slow devices.** It is the floor for a slow desktop and must stay playable there;
   an expensive feature is off or has a cheap stand-in on Potato.
3. **The browser build stays.** It remains a working variant of the game (the school club, `/play`, the store build):
   every feature has a browser path that works, even if it is the plain one.
4. **Every graphics plan and every look feature names its tiers.** A plan, an issue, a PR and the doc of a visual feature
   say what High, Medium, Low/Potato and the browser get. "Every tier" is a valid answer; leaving it out is not.

## Consequences

- ADR 0015 §3 is narrowed, not dropped: the WebGL 2 limit binds what the browser build runs and every shader path the
  browser compiles; a desktop-only technique needs its own fallback path and must not break the browser's compile. WebGL
  2 stays the browser's fallback.
- The gate in code is "desktop class" (`HighDetails.DesktopClass`, false in the browser and on phones) together with the
  preset (`FxKit.Preset`); the look's ranges per preset are fixed values in `GameLook.cs`, and what a preset meshes and
  builds at all is `GameLook.Tiers.cs` — nothing is prepared for a drawer that does not run on the preset.
- **The browser offers only Potato and Low** (#3946, `Client.Core/PresetLimit.cs`): a browser tab shares a weak GPU with
  the page and has no worker threads for the chunk mesher. A stored higher preset is lowered on load, the settings row
  toggles between the two, and the automatic calibration never steps above Low. The browser then gets what its preset
  gets, minus what needs a desktop-class client.
- The first package built this way is the game look of 2026-11-22 ([LOOK.md](../LOOK.md), its table at the top). High
  and Medium get everything (crown clusters, plant bodies, grass clumps, fur, depth of field, soft form edges, the
  longer emitter reach). **Low** gets none of the near geometry — the mesher does not even record its cells — keeps the
  classic emitter reach, draws needle and palm crowns as far forms on the outer crown cells plus the spire, device forms
  with hard edges, rounded avatar and tool parts, and bakes the plant far pictures on the desktop. **Potato** is Low
  minus the rest: needle and palm crowns are the plain leaf-block shell, avatar and tool parts are plain boxes, and no
  plant far picture is baked. A preset change in a loaded world remeshes the loaded chunks when it crosses Low; the
  emitter reach is fixed per world and the figures' rounding per run.
- A feature that differs by tier has to be looked at on the tiers it names, the browser included. For the 2026-11-22
  look neither the frame cost on the four presets nor a browser build has been checked yet; that is the open issue
  #3947 in [TODO.md](../../TODO.md), to be done before the next release.
- The Art Bible's rule 7 (preset-gated effects) and the strategy's small-hardware constraint stay; this ADR says which
  way the gating points: up for desktop High, down to a working floor for Potato and the browser.
