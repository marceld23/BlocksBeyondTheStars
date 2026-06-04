# World Wrap (walk around the planet) — Plan

**Status:** W0–W4 shipped (you can walk around the world seamlessly); **W5 (poles) remaining**. **Goal:** a
player can walk continuously east (or west) and arrive back at their start, as if the planet were a sphere —
and **the seam must be invisible: the eastern edge of the map must line up exactly with the western edge**
(terrain height, biomes, caves, ore, structures all continuous across the wrap). Written in English per
project doc policy.

> **Progress (2026-06-04):**
> - ✅ **W0** — `WorldConstants.Circumference = 6000` (375 chunks around) + `WrapX`/`WrapDeltaX`/
>   `CanonicalChunk`/`CanonicalBlock` helpers.
> - ✅ **W1** — seam-free generation via circular-domain noise (`Noise.FbmCylX`/`ValueCylX`/`Value4D`);
>   surface height, biomes, caves, ore all periodic across X = 0 ≡ X = C; proven by `WorldWrapTests`.
> - ✅ **W2** — `ServerWorld`/`ClientWorld` canonicalize the block/chunk/persistence boundary.
> - ✅ **W3** — server wraps player longitude (`HandleMove`) and streams across the seam.
> - ✅ **W4** — client renders seamlessly (`GameBootstrap.SceneX` + per-block chunk reposition; entities/
>   remotes/NPCs via `ScenePos`; world map wrap-aware) and interaction is cross-seam (canonical mine/place/
>   repair, wrap-aware `WithinReach`/`LandingZone.Contains`/wreck mask).
> - ⏳ **W5** — poles (bound latitude Z with an ice-wall/barrier biome). Not started.
> - ⏳ **Deferred** — a rare floating-origin rebase after very many laps (float precision); the world map
>   waypoint stays in scene space (goes stale after a full lap). Neither affects normal play.

---

## 1. Target topology — a cylinder, not a true sphere

A flat `(x, z)` heightmap cannot be a real sphere without pole distortion. The practical, seam-free model
that reads as "round planet" is a **cylinder**:

- **X = longitude → wraps** at a fixed circumference `C`. Walk `C` blocks east → back to the start.
- **Z = latitude → bounded** between two impassable **poles** (e.g. a cold ice wall / sheer cliff at
  `z = ±C/4`), or a soft barrier that turns you back. North/South does **not** wrap.
- This matches the day/night model already shipped: `GameBootstrap.DayCircumference = 6000` treats world-X
  as longitude for the terminator. **Reuse the same constant as the wrap circumference** so one lap east =
  one day's worth of longitude. (Promote it from a client visual constant to an authoritative world
  constant — see §4.)

A future "true sphere" with tapering longitude near the poles is possible but is a much larger distortion/
projection problem; the cylinder gives the walk-around-the-world feel for a fraction of the cost.

---

## 2. The hard part: seam-free terrain (the user's explicit requirement)

Today terrain is a **pure, non-periodic** function of `(x, z)` — `WorldGenerator.SurfaceHeight`,
`BiomeIndexAt`, caves, and ore all feed raw `x` into `Fbm2D` / the `Noise` hash, which is a chaotic
XOR-multiply chain with **no periodicity** (`src/Spacecraft.WorldGeneration/Noise.cs`,
`WorldGenerator.cs:47-74`). Wrapping `x` naively (`x % C`) would put two unrelated noise values next to each
other at the seam → a hard cliff/biome wall.

**Fix — make the X axis periodic by sampling noise on a circle.** Instead of sampling along a straight
X line, map longitude onto a circle of circumference `C` and sample a higher-dimensional noise domain:

```
theta = 2*pi * x / C
nx = cos(theta) * R          // R chosen so the circle's arc-length frequency == the old linear frequency:
ny = sin(theta) * R          // R = C / (2*pi)   (then divide by TerrainScale as before)
height = Fbm( seed, nx/scale, ny/scale, z/scale )   // z stays linear (bounded latitude)
```

Because `cos/sin` are exactly periodic, `noise(x=0) == noise(x=C)` **including slope** — a continuous,
invisible seam. This is the standard "tileable noise via circular domain" technique.

**Scope of this change — every place raw X enters noise must move to the circular mapping:**
- `WorldGenerator.SurfaceHeight` (terrain height)
- `WorldGenerator.BiomeIndexAt` / `BiomeIndex` (biome field — must wrap too, or biomes seam)
- cave / 3D density noise (any `x`-dependent sample)
- ore / resource frequency fields
- the per-biome weather offset field if it samples `x`

Add a single helper, e.g. `Noise.SampleCyl(seed, x, z, C, scale)`, and route **all** X-dependent samples
through it so the mapping is defined once.

---

## 3. Wrapping everything else (or it corrupts)

Once `x` is canonical in `[0, C)`, every subsystem that consumes X must agree, or persistence collides and
rendering tears at the seam. Checklist (each verified against today's code):

| Area | Today | Needed |
|---|---|---|
| **Player position** | unbounded float, stored as-is (`GameServer.HandleMove`, `PlayerController`) | wrap X into `[0,C)` on the **server** (authority) and mirror on the client transform so the camera doesn't jump |
| **Movement / camera** | continuous transform | when crossing the seam, shift the local world origin by `C` so the `CharacterController` never sees a teleport (a "floating-origin"-style rebase at the seam) |
| **Chunk streaming** | `ChunkCoord(cx,cz)` raw, `HashSet<ChunkCoord>` (`GameServer.StreamChunks:781`) | chunk-X computed `mod (C/16)`; near the seam, stream the wrapped neighbours from the far side |
| **Persistence** | `block_edit PRIMARY KEY (planet,x,y,z)` raw (`SqliteWorldRepository:53`) | store **canonical wrapped X** so `x=100` and `x=100+C` are the same cell (else duplicate/overwrite) |
| **Distance math** | `dx = a.x - b.x` everywhere | wrapped delta `dx = wrapDelta(a.x-b.x, C)` (= shortest way round) for render culling, AI aggro, interaction reach, radar, remote-player placement |
| **Landing zones** | march `index * spacing` in +X forever (`GameServerSpace.EnsureLandingZone`) | space players around the circumference (`(index*spacing) mod C`) with a min-gap guard; cap players-per-world |
| **Settlements / wrecks / ship stamp** | absolute X (`GameServerSettlements`, `GameServerShipStructure.StampShip`) | place at wrapped X; ship/structure stamps near the seam must wrap their block writes |
| **Remote players / entities** | absolute positions rendered | render at the wrapped-nearest copy relative to the local camera (so a player just across the seam appears adjacent, not `C` away) |

---

## 4. Suggested phasing (each phase shippable + testable, server stays green)

- **W0 — Decide constant & topology.** Promote `DayCircumference` to an authoritative
  `WorldConstants.Circumference` (server + shared); pick `C` (start with 6000 to match day length) and the
  pole half-width. Pure refactor, no behaviour change.
- **W1 — Seam-free generation (offline-provable).** Add `Noise.SampleCyl` and route all X-dependent
  worldgen through it. **Test:** assert `SurfaceHeight(x=0)==SurfaceHeight(x=C)`, `BiomeIndexAt` equal, and
  bounded slope across the seam, for many seeds. This is the user's core requirement and is fully unit-
  testable without the client.
- **W2 — Canonical persistence + distance helpers.** Introduce `WrapX`/`WrapDelta`; route block-edit keys,
  landing-zone/settlement/wreck placement, and all distance checks through them. **Test:** an edit at `x`
  and at `x+C` resolve to the same DB row; landing zones never collide.
- **W3 — Server position authority.** Wrap player X in `HandleMove`; wrap chunk streaming + far-side
  neighbour streaming. **Test:** walking past `C` reports `x≈0`; chunks stream continuously across the seam.
- **W4 — Client seam crossing.** Floating-origin rebase so the camera/`CharacterController` cross the seam
  without a visible jump; render remote players/entities at wrapped-nearest. Build-only verify + manual.
- **W5 — Poles.** Bound Z with an ice-wall/barrier biome and turn-back; tune so it reads as a planet.

---

## 5. Risks / open questions

- **Cross-cutting blast radius.** Distance math is sprinkled across AI, interaction, radar, rendering — a
  missed `dx` is a subtle seam bug (things ignore you / pop) rather than a crash. Centralising in
  `WrapDelta` and grepping every `\.x -` site is mandatory.
- **Circumference vs. exploration.** `C=6000` is ~6 km around. Big enough to feel open, small enough to lap.
  Tunable per planet later (`planet.Circumference`), but start global.
- **Existing saves.** Worlds generated pre-wrap have edits at `x>C`; a migration must `WrapX` existing rows
  (or gate wrap behind new-world creation only). Decide before shipping W2.
- **Structures straddling the seam.** Ship stamp / settlement footprints that cross `x=0` must write wrapped
  blocks — easiest to **forbid placement within `footprint/2` of the seam** initially.
- **True sphere** (pole-convergent longitude) is explicitly **out of scope** here; cylinder first.

---

## 6. Bottom line

Feasible and self-contained if phased. The seam-free requirement is solved by **circular-domain noise**
(W1) and is independently unit-testable before any client work. The larger cost is the discipline of
routing **every** X coordinate through one wrap helper (W2–W4). Recommend starting with W0+W1 since they
prove the hardest part (no visible seam) with zero gameplay risk.
