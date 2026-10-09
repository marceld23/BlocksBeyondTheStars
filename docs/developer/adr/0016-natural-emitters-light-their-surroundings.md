# ADR 0016 — Lava, crystals and the glowing flora light their surroundings (quietly)

- **Status:** Accepted (supersedes the glow-only rule of [#2036](https://github.com/marceld23/BlocksBeyondTheStars/issues/2036)
  for these three groups)
- **Date:** 2026-10-09
- **Context source:** [#2407](https://github.com/marceld23/BlocksBeyondTheStars/issues/2407), part of the atmosphere
  package epic [#2408](https://github.com/marceld23/BlocksBeyondTheStars/issues/2408)

## Context

The game has two kinds of light. *Emission* makes a block's own surface shine (HDR, bloom) and lights nothing else.
*Block light* is a coloured flood fill with a reach of nine blocks, baked per vertex by the client's mesher, that
lights the faces around a source. #2036 settled that only the fixtures — lamps, torches, campfires, strip lights —
flood light, and that the natural emitters (lava, crystals, ores, glowing flora) and the machines glow only. The
reasons were cost (a lava lake is hundreds of cells; the flood runs on the browser's single thread with a 2–4 ms
chunk budget) and readability (a whole lava world turning orange).

The result looked wrong in exactly the places the game wants to be atmospheric: a lava lake in a pitch-black cave
with a glowing surface, a crystal cavern dark between the crystals, a glowcap forest black at night with bright caps.

## Decision

1. **Lava, crystal blocks and the glowing flora light their surroundings; ores and machines still glow only.**
   The rule stays data-driven: these blocks declare a `lightColor` in `data/blocks.json` like every fixture.
2. **Quietly.** They declare a small `lightRadius` (3–4 blocks; the fixtures keep the full 9). The mesher floods
   each reach in its own pass: a short-reach source starts at 80 % of a fixture's level and fades out over its own
   few blocks (a fixture loses one level per block over nine) — the rock beside a crystal is lit, the cave four
   blocks away is not; a lava world glows at its shores, it does not turn orange.
3. **From the surface only, where they mass.** Lava and crystal blocks carry `lightSurfaceOnly`: only a cell with an
   air neighbour is indexed as a source, so a lake of a thousand cells seeds the flood with its shoreline and top,
   not its depth. That keeps the flood-fill cost bounded by the lit volume, as before.
4. **One packed source value.** The client's light index, the chunk light lists and the mesher's source list carry
   a source as one int — colour in the low 24 bits, reach in bits 24–27 (0 = full), the surface-only flag in bit 28
   (`BlockLight.Pack`) — so nothing on the wire or in the save changed. A dye keeps the reach (`BlockLight.Recolor`).
5. **A switch.** "Lava and crystals light caves" in the atmosphere settings group; off indexes only the full-reach
   fixtures, taking effect as chunks stream in. Medium and better by default.

## Consequences

- `BlockLightTests` now asserts the quiet light (reach 2–4, lava/crystal surface-only) instead of asserting
  darkness; the fixtures' full reach is pinned.
- Existing worlds change: lava caves and crystal caverns are lit on the next visit (the light is client-side).
- The chunk-build budget on the browser is to be measured with a lava world on Low (see the package's TODO entry);
  the reach can be lowered in data without a code change.
- The gen-11 cave plants (prismbloom, glowthread, …) keep lighting through the flora catalogue path they already
  had; this ADR adds the block-type emitters.
