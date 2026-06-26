# Fluid Routing — Rivers, Waterfalls & Lava

> Status: **Implemented.** Water rivers, waterfalls and the lava parity pass (animated surface,
> lava rivers, lavafalls) all ship from one fluid-agnostic worldgen path. Old saves are out of
> scope — the procedural baseline regenerates; only player deltas persist.

How surface fluids are placed and animated. Both water and lava use the **same** routing machinery:
a deterministic, store-nothing network built once per world from the seed, queried per column in
O(1), and rendered with procedural shader animation plus client VFX/sound. The only difference is
which fluid fills the channels and a handful of tuned constants.

---

## 1. The problem this replaced

The original rivers were a noise band: `RiverDepthAt` carved a channel wherever an FBM value sat
near 0.5, with **no relationship** to where the sea or any pond was. Each column filled flush to its
*own* local surface, so water appeared to climb hills; channels dead-ended anywhere a slope gate or
height gate fired; ponds were *excluded*, not connected; and worldgen water was never registered as
a fluid source, so it never flowed or fell. A `RiverMaxSlope` gate (added to suppress a "floating
water wall" artefact) actively chopped channels exactly at the steep steps where a waterfall belongs.

Lava had the same static-placement story and, on top of it, none of water's animation: a flat
glowing block on the very worlds (`lava`, `ashen`) where it is the dominant "ocean".

---

## 2. Architecture — one network, two fluids

The whole pipeline is **fluid-agnostic** and parameterised by a fill fluid:

- **`RiverNetwork`** (`src/BlocksBeyondTheStars.WorldGeneration/RiverNetwork.cs`) — the coarse
  drainage solver. Pure, deterministic, **integer math** (terrain heights are `int`, so there is no
  float to drift across .NET ↔ Unity/IL2CPP). Algorithm:
  - Sample `SurfaceHeight` on a **coarse grid** (cell size **8** blocks) over the whole torus.
  - **Priority-Flood** depression fill (Barnes) from the sea cells → a drainage tree plus
    **fill-and-spill** lakes, so every cell has a downhill path to a sink. Lake depth is capped
    (see §5) so the naive flood doesn't drown 20–28 % of the world.
  - Scatter deterministic **sources** at high-percentile (≈45th) upland cells, count scaling with
    fluid abundance × world area.
  - **Flow accumulation:** each source walks its drainage path tallying cells; a cell becomes a
    **channel** once its flow exceeds `channelFlowThreshold`; downstream trunks that gather
    tributaries widen toward a cap.
  - A channel step whose terrain drops more than `waterfallMinDrop` (**4** blocks) is flagged a
    **waterfall**.

- **`RiverField`** (`RiverField.cs`) — rasterizes the coarse network to **block resolution**:
  terrain-following water surface (a thin sheet, never a floating wall), capped pools, per-block
  waterfall detection, and a flared **estuary** where the trunk meets the sea. Carries a
  **`FillFluid`** so the exact same field code stamps water *or* lava.

- **`WorldGenerator` integration** (`WorldGenerator.cs`):
  - The old noise-band `RiverDepthAt` and the `RiverMaxSlope` / `riverMaxY` gates are **gone**.
  - `RiverFieldFor(planet)` memoizes the network+field per world, keyed by
    `(planet.Key, circumference, cratered)`, built lazily from the seed and soft-capped at 8 worlds.
    Because it rebuilds from the seed, **server and client get byte-identical fields with no wire
    traffic** — consistent with the store-nothing engine.
  - `BuildRiverField(planet)` picks the fill: **water** on wet worlds (sea fluid = water,
    `WaterAbundance ≥ 0.4`), **lava** on `lava`/`ashen` (sea fluid = lava), or `RiverField.Empty()`
    on dry worlds with no sea.
  - `Generate` fills each column from `riverField.FillFluid`; `SurfaceRiverDepth` delegates to the
    field so flora placement, ship-landing checks and aquatic life all read the **single source of
    truth**. Pond-first precedence and "the sea owns columns ≤ sea level" are preserved.

### Why this is true by construction
- **Rivers always reach a body of water** — fill-and-spill guarantees every channel terminates in a
  sink (sea, lake or pond). A river that hits a basin forms its own (capped) lake, which then spills
  onward.
- **No floating water** — the surface follows the terrain as a thin sheet; steep steps become
  explicit vertical **waterfall columns** instead of being deleted by a slope gate.

---

## 3. Waterfalls (free on the client)

Waterfall *rendering* needs **zero** server simulation. A vertical run of water blocks written by
worldgen has water above and air on ≥2 sides, which is exactly what `WaterfallDetect.IsFalling`
(geometric) keys on. So a static column lights up automatically as:

- the cascade shader streak (`BlockAtlasTransparent.shader` fall mode), and
- **mist** at the impact — `WaterfallMistView.cs` (mirrors `GeyserView`, wired in `WorldRig`)
  bursts pale spray where the drop exceeds 3 blocks, capped/gravity-arced/shrink-fade.

A catch basin is carved at the foot of each fall so the column has a plunge pool to land in. The new
asset is the **sound**: `client/Assets/Resources/audio/water_fall.mp3` (ElevenLabs loop), selected
by `ClientAudio.WaterBedFor` for a nearby falling/impacting column (drop ≥ 4), ranked above the calm
beds (`water_fall` > `water_brook` > `water_surf` > `water_shore`).

---

## 4. Lava — the same machinery, viscous-tuned

Lava reuses the routing wholesale; only the fill fluid and a few constants change, plus a lava-tuned
shader look. A lava world has no water field, so it is *one* memoized field either way — no extra
cost.

### Animated surface (benefits every lava sea, not just rivers)
- `ChunkMesher` tags a lava **surface** cell (lava with air above) as tint **mode 5**; a falling-lava
  vertical flank as **mode 6** (X/Z faces only).
- `BlockAtlas.shader` (the **opaque** pass — tightly gated so no other block is touched):
  - **Mode 5** drives an animated emissive **crust** — slow `_Time`-scrolled dark slabs drifting over
    bright cracks on world position, ≈ **1/3 water speed** (viscous), with the *average* glow held
    near the prior constant so bloom stays balanced (it only redistributes brightness).
  - **Mode 6** streaks a hot glow straight **down** the flank, faster than the surface crust
    (molten free-fall).

### Lava rivers
`BuildRiverField` builds a **lava** field on `lava`/`ashen` worlds (sink = lava sea, fill = lava),
viscous-tuned for sparse chunky magma flows rather than dense brooks. Lava channel cells render with
the mode-5 surface automatically. Trees/flora/ship-landing avoid lava channels via the shared field
query.

### Lavafalls + embers + heat-haze + sound
Where a lava channel drops over a step (or a player mines under lava), the falling flanks render
mode 6 and:
- `LavaFallView.cs` (mirrors `WaterfallMistView`, wired in `WorldRig`) emits **rising embers** —
  bright orange cinders cooling to dark on a gravity arc — the case the white water mist deliberately
  skips. Distinct colour ramp + motion so they don't read as fire.
- **Heat-haze:** `HeatShimmer.AddProximityHeat` OR-s a localized boil into the existing full-screen
  shimmer, fed each frame from the nearest impact distance (≤7 blocks).
- **Sound:** `client/Assets/Resources/audio/lava_fall.mp3` (ElevenLabs loop); `ClientAudio.LavaBedFor`
  picks `lava_fall` for a nearby falling/impacting lava column, else `lava_bubble`. Uses the same
  fluid-agnostic `WaterfallDetect` with the lava id.

---

## 5. Tuned constants (as shipped)

| Parameter | Water | Lava | Note |
|---|---|---|---|
| Coarse cell size | 8 | 8 | shared grid |
| `channelFlowThreshold` | 2 | 1 | lava = sparser paths |
| `maxWidth` | 7 | 9 | lava = wider thick flows |
| `widthPerFlow` | 8 | 6 | lava = slower width growth |
| `maxLakeDepth` | 6 | 4 | lava = shallower basins |
| `estuaryWiden` | 3 | 4 | flare at the sea mouth |
| `waterfallMinDrop` | 4 | 4 | shared; matches client mist `MinDrop` |
| sources | abundance × area | base 26 × area scale | lava sparser |
| impact-sound / VFX drop | > 3 | > 3 | `WaterfallDetect.ImpactDrop` |

Ratios are deliberate starting values — tune in code after playtest.

---

## 6. Invariants & tests

Guarded by `RiverFieldTests` (and the lava analogue in `WorldGenerationTests`):

- **Determinism** — `RiverNetwork.Build` is byte-identical across runs and across server/client
  (integer pipeline).
- **Connectivity (headline)** — every channel cell drains to a sink; no path ends on dry land above
  sea level.
- **Monotonic surface** — water-surface Y is non-increasing along any path.
- **No floating water** — no water column has air directly beneath its bottom cell except inside a
  marked waterfall step landing in a basin.
- **Lava parity** — `LavaWorld_CarvesLavaRivers`: the field fills lava and channels hold lava near
  the surface on `lava`/`ashen`.
- **Density lever** — `MoreSources_WidenAndMultiplyRivers`: source count scales rivers monotonically.

Per the project verification routine, `client/Assets` changes (shaders, VFX, audio) want a **local
Unity build** to eyeball cascades, embers, heat-haze and the new sounds in-engine and confirm
client/server agree on river/lava placement.

---

## 7. Key files

- `src/BlocksBeyondTheStars.WorldGeneration/RiverNetwork.cs` — coarse drainage solver (priority-flood,
  sources, flow accumulation, waterfall flag).
- `src/BlocksBeyondTheStars.WorldGeneration/RiverField.cs` — block-resolution rasterizer; carries
  `FillFluid`.
- `src/BlocksBeyondTheStars.WorldGeneration/WorldGenerator.cs` — `RiverFieldFor` / `BuildRiverField`
  (water vs lava selection), `Generate` fill, `SurfaceRiverDepth` and the water/landing/flora queries.
- Client shader: `client/Assets/BlocksBeyondTheStars/Shaders/BlockAtlas.shader` (lava modes 5/6) +
  `BlockAtlasTransparent.shader` (water cascade).
- Client scripts: `ChunkMesher.cs` (surface/falling tags), `WaterfallDetect.cs` (fluid-agnostic),
  `WaterfallMistView.cs`, `LavaFallView.cs`, `HeatShimmer.cs`, `ClientAudio.cs`, `WorldRig.cs`.
- Assets: `client/Assets/Resources/audio/water_fall.mp3`, `lava_fall.mp3`.
- Tests: `RiverFieldTests`, `WorldGenerationTests` (lava), `FluidTests`.
