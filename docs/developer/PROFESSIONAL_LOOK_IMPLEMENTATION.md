# Professional look — how it works

Status: implemented (see TODO.md for live Done/Open status). Date: 2026-06-19.

The "professional look" pass (work packages WP-1…WP-16, derived from the G1–G32 gap analysis) shipped
in one continuous run. It is a mix of a rendering-foundation change (linear colour), client-only
gameplay juice, and a ship-presentation pass. This doc records the final shape of each piece and where
it lives; the normative style rules it produced now live in `docs/ART_BIBLE.md`.

## Overview

The whole run reused the codebase's established patterns rather than inventing parallel ones: code-built
UI (`UiKit`), code-built particles (the `FxParticle` pattern), entity-style visuals (the `DoorView`
pattern), and dual-pipeline shaders. Every player-facing string was localized DE+EN; every new shader is
in GraphicsSettings Always-Included; every new block was added to the atlas and broadcast on stamp.

## How it works

**Rendering foundation**
- **Linear colour space** (WP-1, the widest-blast-radius change): `m_ActiveColorSpace: 1`. All C# colour
  *composition* still happens in sRGB exactly as before; a single boundary helper, `ShaderColor.Srgb()`,
  converts once at every script→shader colour upload (~25 files), so per-planet flora hues and
  red-star→red-world propagation stay 1:1. The normal atlas is created `linear: true`. In-shader
  strength constants (flora blend, grade strength, emission ×2, bloom threshold, vertex AO) were retuned
  against the pre-change look.
- **Sci-fi UI font** (WP-2): Rajdhani-Medium (OFL, DE glyph coverage verified) is bundled and loaded
  first in `UiKit.Font`, keeping the OS-font fallback chain. No TMP migration (UiKit centralizes the
  font, so TMP stays a future option).
- **Event post-FX** (WP-4): `UrpScenePost` exposes runtime-driven params — low-O₂ pulsing blue vignette
  with an escalating two-beep alarm, a red damage-vignette kick, and a `Burst()` chromatic-aberration /
  film-grain API for future events. All honour the ReducedEffects setting.

**Gameplay juice (client-only)**
- **Camera-motion toggle** (WP-3): `ClientSettings.CameraMotion` gates head-bob, FOV-kick and shake
  (one accessibility row, default on).
- **Mining loop** (WP-5): final-hit flash at the block + the mined tile icon flies into the hotbar
  (icon id sampled during `MiningProgress`, keyed off the authoritative block-change).
- **Craft/unlock celebration** (WP-6): card pulse + floating "+1" label on craft; tech-node ring pulse +
  unlock label + `tech_unlock` fanfare on blueprint unlock (inventory-diff detection).
- **UI transitions** (WP-7): `UiKit.TransitionIn` (fade+rise ~0.14 s, unscaled time) on menu/tab and
  scan/wreck panels, plus a hotbar selection tick; instant under `UiKit.ReducedMotion`.
- **Music contexts** (WP-8): menu / planet / space / combat cross-fade between two AudioSources, backed
  by four ElevenLabs ambient loops with mood-matched procedural fallbacks; combat is inferred from
  hull+shield drops. Later extended to an AppShell-level director + a selectable Suno track library.

**World & ship surface**
- **Block texture variants** (WP-9): 2 procedural variant tiles per natural block + deterministic 90°
  top/bottom UV rotation, both chosen from a world-position hash (so remesh is deterministic);
  ship/tech panels are excluded so seams stay aligned.
- **Ship room identity** (WP-10): 7 new blocks (light strips, panels, hazard floor, engine nozzle);
  `PaintStationAccents` lays per-room 3×3 floor pads in both stamp paths; box ships get cyan side-wall
  strips; restamps re-stream chunks so there are no ghost blocks in multiplayer.
- **Cockpit + station decor** (WP-11/12): `StationDecorView` (a generalization of the `DoorView`
  pattern) draws the cockpit console + animated screen + a proximity-gated holographic system map
  (sourced from the same star-map data, no duplicated tables), medbay tank pulse, lab/console terminal
  flicker, and workshop sparks. The space view gained a flight readout (SPD/THR/HDG + HULL/SHD).
- **Thruster/transit FX** (WP-13): throttle-scaled exhaust particle stream, pad-dust bursts on other
  players' touchdown/lift-off, and an `engine_nozzle` idle glow so landed ships never look dead.
- **Ship damage** (WP-14): combat hull-hit sparks; aboard, interior spark bursts below 50% hull and a
  pulsing red emergency light + `hull_alarm` below 25% (hysteresis at 30%). Since #2807 the light glows steadily
  under *Reduce flashes* and *Mute alarms* silences the beep.
- **Build preview + materialize** (WP-15): a validity-tinted (green/red, pulsing) ship-editor placement
  ghost, plus a client-side restamp materialize sweep (rising holo ring + shimmer, keyed off
  `ShipStations` changes) instead of the hull popping in. An empty list ("no ship in this world", e.g.
  aboard a space station, #2606) clears the decor but is no restamp — undocking back to the same ship
  plays no sweep.

## Key files

- `client/Assets/BlocksBeyondTheStars/Scripts/ShaderColor.cs` — the sRGB→shader boundary helper.
- `client/Assets/BlocksBeyondTheStars/Scripts/UrpScenePost.cs` — event vignette / alarm / Burst API.
- `client/Assets/BlocksBeyondTheStars/Scripts/UiKit.cs` — bundled font, transitions, reduced-motion.
- `client/Assets/BlocksBeyondTheStars/Scripts/StationDecorView.cs` — cockpit + station idle decor.
- `client/Assets/BlocksBeyondTheStars/Scripts/ShipBuildFx.cs` — restamp materialize sweep.
- `client/Assets/BlocksBeyondTheStars/Scripts/BlockTextureAtlas.cs` — variant tiles + new room blocks.
- `src/BlocksBeyondTheStars.GameServer/GameServerShipStructure.cs` — `PaintStationAccents`, room stamping.
- `docs/ART_BIBLE.md` — the normative style + palette + quality checklist this run produced (WP-16).

## Known gaps / deferred

- The two human **playtest checkpoints** are the remaining verification: **PT-1** (linear-colour-space
  parity across the 8 reference scenarios + retune candidates) and **PT-2** (one structured pass over
  WP-2…15). Builds are green and the .NET test suite passes (count grows over time — see TODO.md for the
  live number; it was 475 when this run shipped and is well above 600 now).
- WP-13 **own-launch pad dust** is deferred (the camera sits inside the ship during the sequence).
- Items deliberately **out of scope** for this run and tracked in `ADVANCED_GRAPHICS.md`: decals
  (G6), detail scatter/shells (G7), biome blending (G9), flora light emission (G12), LOD/greedy meshing
  (G3), reflection probes (G2), cinematic camera (G24), 3D item icons (G30), sky meteors (G31),
  screenshot mode (G5).
