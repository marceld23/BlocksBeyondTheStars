# World Generation — Seed → Galaxy → Planet → Surface

> Status: **Implemented.** The whole universe — every system, body, planet surface, biome, fluid,
> plant and creature — is derived deterministically from a single 64-bit world seed. Almost nothing
> is stored: server and every client regenerate identical worlds from the seed alone; only player
> deltas (placed/broken blocks, bases) persist.

This is the *how it all fits together* reference for procedural generation. For the fluid routing
internals see [FLUID_ROUTING.md](FLUID_ROUTING.md); for torus topology see [WORLD_WRAP.md](WORLD_WRAP.md);
for the system-flight layer see [MULTIWORLD_AND_SYSTEM_FLIGHT.md](MULTIWORLD_AND_SYSTEM_FLIGHT.md).

---

## 1. The one rule: everything hangs off the seed

A world has a single `long` seed (`WorldMetadata.Seed`). Every decision below is a **hash of that
seed plus some coordinates/indices**, never a running random stream. Two consequences:

- **Determinism** — same seed ⇒ byte-identical universe on every machine, with no stored world state.
- **Independence** — each layer hashes with its own salt, so adding a moon to planet 3 changes
  *nothing* about wrecks, stations, or the terrain of planet 1. Existing universes stay stable when
  content is added.

The primitives live in [`DeterministicRandom.cs`](../../src/BlocksBeyondTheStars.WorldGeneration/DeterministicRandom.cs)
(xorshift64 PRNG, not platform `System.Random`) and [`Noise.cs`](../../src/BlocksBeyondTheStars.WorldGeneration/Noise.cs)
(`Hash`, value noise, and torus-periodic FBM).

```
SEED
 └─ Galaxy ──> Star systems ──> Celestial bodies ──> PlanetType
                                                       └─ (on entry) World surface:
                                                          Terrain → Biomes → Water/Rivers → Flora → Fauna
```

---

## 2. Galaxy → system → body

Built by [`UniverseGenerator.cs`](../../src/BlocksBeyondTheStars.WorldGeneration/UniverseGenerator.cs).
The data shapes are in [`Galaxy.cs`](../../src/BlocksBeyondTheStars.Shared/World/Galaxy.cs).

- **~8 star systems** per galaxy (`StarSystemCount`), each with a random 2D star-map position.
- Per system: **2–6 planets**, each with **0–3 moons**, plus **2–3 asteroid fields** and (rarely)
  **1–3 space stations**.
- Each body gets a **deterministic orbit position** (polar coordinates around the star: first planet
  ~420 units out, +520 per planet ± jitter; moons 90 + m·55 around their planet) and an **orbit
  period** (planets 6–40 in-game days, moons 0.4–2.5, ~20 % retrograde). The orbit period is a
  purely visual phase driver — it never disturbs landing, travel distance or pad reservations.

A `CelestialBody` stores only Id, Name, `Kind` (Planet/Moon/AsteroidField/SpaceStation/Wreck), a
**`PlanetType` key**, and orbit data. The body's *content* is generated only when a player enters it.

Each layer uses a distinct hash salt (e.g. `Noise.Hash(seed, systemIndex, 1, 1)` per system, separate
calls for angle/radius/period) precisely so unrelated bodies don't shift when one is added.

---

## 3. The PlanetType: the master control sheet

The `PlanetType` key on a body points at a definition in `data/planets.json`
(schema: [`PlanetType.cs`](../../src/BlocksBeyondTheStars.Shared/Definitions/PlanetType.cs)). It is the
single source of truth for nearly everything about a world:

| Group | Fields | Drives |
|---|---|---|
| **Atmosphere** | `Atmosphere` (breathable/toxic/none), `OxygenExtractability`, `AtmosphereDensity`, `AtmosphereHeight`, `SpaceSky` | O₂ drain, haze/fog, whether water is possible, "in space" line |
| **Terrain** | `BaseHeight`, `Amplitude`, `TerrainScale`, `TerrainStyle`, `FloatingIslands`, `Cratered` | Shape & ruggedness |
| **Blocks** | `SurfaceBlock`, `SubSurfaceBlock`, `DeepBlock`, `SurfaceDepth`, `Ores`, `CaveThreshold` | Material layers, ore veins, caves |
| **Weather** | `DayLengthSeconds`, `StormChance`, `BaseTemperature`, `Weather`, `CloudColor`, `CloudDensity` | Day length, storm bias, clouds |
| **Fluids** | `WaterAbundance`, `LavaAbundance` | Sea level & sea type |
| **Life** | `Biomes`, `FloraTheme`, `FloraDensity`, `TreeDensity`, `CreatureAbundance` | Which biomes/plants/creatures |
| **Selection** | `SpawnWeight`, `Selectable`, `Exotic`, `Void` | How often it appears as a random planet |

**Type selection** (`UniverseGenerator.PickPlanetType`) is a weighted random pick over all
`Selectable` types by `SpawnWeight`, with `Exotic` types scaled by the world's `ExoticWorlds`
frequency (Off → 0, Normal → ×1, Frequent → ×2.5).

### Size

Size is a `WorldSizeClass` derived from Kind + type
([`WorldConstants.cs`](../../src/BlocksBeyondTheStars.Shared/World/WorldConstants.cs)), which sets a
**circumference** band (stable-hashed from the body id, rounded to 16-block chunks):

| Class | Circumference (blocks) |
|---|---|
| Asteroid | 800–1600 |
| Moon | 2500–4000 |
| Planet | 5000–12000 |

Circumference controls the east-west wrap distance, chunk count, the day/night terminator, gravity
band, and the live-creature cap (bigger world → more fauna). Worlds are a **torus** (both X and Z
wrap) so circumnavigation is seamless in every direction — see [WORLD_WRAP.md](WORLD_WRAP.md).

Gravity, sky hue, cloud tint and flora hue are each seeded **per world** (from `LocationId ^ Seed`
with per-feature salts) so two same-type worlds still look distinct — see
[`GameServerWeather.cs`](../../src/BlocksBeyondTheStars.GameServer/GameServerWeather.cs).

---

## 4. Terrain — how variety happens

Surface height is computed per column in
[`WorldGenerator.SurfaceHeight`](../../src/BlocksBeyondTheStars.WorldGeneration/WorldGenerator.cs).
Variety stacks from five mechanisms:

**a) Five terrain archetypes** (amplitude multiplier, ridged amount):

| Archetype | Amp | Ridged |
|---|---|---|
| Flats | 0.18 | 0.0 |
| Rolling plains | 0.55 | 0.0 |
| Hills | 1.00 | 0.0 |
| Mountains | 1.90 | 0.12 |
| Canyons | 1.30 | 0.65 |

**b) Regional blend** (`TerrainProfile`) — the heart of *within-world* variety. Each world
seed-picks **2–5** of the archetypes (from a rotated start index); a **broad field** (`TerrainScale × 6`,
3 octaves) selects a point in that subset per position and **smoothstep-blends** the two nearest. So
one region reads flat, the next hilly, the next as a ridged mountain range.

**c) Per-world drama** (`DramaFor`) — a seeded **0.9–1.5×** multiplier on the whole relief, so the
same planet type rolls gentle on one world and jagged on the next.

**d) Ridge transform** — for mountain/canyon archetypes, smooth swells are folded into sharp
ridges/valleys (`h·(1-ridged) + ridge(h)·ridged`).

**e) Overriding shapes** — `TerrainStyle` (mesa, dunes, spires, flats…), `Cratered` (flat regolith +
impact craters for airless bodies), and `FloatingIslands`.

All noise is **torus-periodic FBM** (4 octaves), so terrain, caves and ores are seamless across both
wrap seams.

Below the surface: surface/sub-surface layers (`SurfaceDepth`, default 4) → deep block → per-world
mantle → an unmineable bedrock foundation at 256–2048 blocks down (with a 6-block lava/basalt band
above it so digging out the bottom is impossible). Ore veins (3D noise × rarity × per-world richness)
and caves (3D noise, if `CaveThreshold > 0`) are carved into the crust.

---

## 5. Biomes — distribution

Two steps, both in [`WorldGenerator.cs`](../../src/BlocksBeyondTheStars.WorldGeneration/WorldGenerator.cs)
(`ResolveBiomes`, `BiomeIndex`):

1. **How many** — a planet type lists a *pool* of biomes; each world activates a seed-derived
   **2..pool** of them. A single-biome type yields exactly one.
2. **Where** — a **separate low-frequency noise field** (`FbmTorus`, scale **360**, 3 octaves) maps
   each column to a biome index.

> **Biomes are independent of terrain.** There is no temperature/elevation biome model. The biome
> index comes from its own noise field (`seed ^ 0x0B10E`), *not* from height or the terrain-profile
> field (`seed ^ 0x7E44A1`). Mountains and biomes are rolled separately and overlap freely. The large
> scale (360, ~7.5× the default `TerrainScale` of 48) just makes each biome a big contiguous region
> so per-biome systems (weather) cover a meaningful area.

Each resolved biome carries its own surface/sub-surface blocks, a **flora theme**, and flora/tree
density multipliers (a multiplier of 0 ⇒ a treeless biome). There are **11 flora themes** (temperate,
tropical, savanna, desert, swamp, tundra, alpine, fungal, alien, crystal, ashen), each defining
preferred climate tags, density multipliers and which tree archetypes it allows.

---

## 6. Flora & trees

Derived per world by
[`FloraGenerator.cs`](../../src/BlocksBeyondTheStars.WorldGeneration/FloraGenerator.cs) and
[`TreeGenerator.cs`](../../src/BlocksBeyondTheStars.WorldGeneration/TreeGenerator.cs):

- **Catalogue:** 33 fixed flora archetypes (`FloraCatalog.All`).
- **World roster:** each archetype is activated with `ActivationChance(theme, tags)` — **85 %** when
  its climate tags match the world's theme, **40 %** otherwise. Each active species gets a
  procedurally coined **name** and is **toxic with 30 % probability**. `EnsureCoverage` then
  force-activates a minimum so no used surface and no sea ever goes bare.
- **Per biome:** `FloraForSurface` only draws from active species whose host surface matches the
  biome's surface block, weighted by theme (preferred species count 4:1). In practice **a handful of
  species per biome** (typically ~3–7 land plants on that biome's ground block) plus aquatic species
  in water. Which species stands where is chosen by a low-frequency patch field (scale 18), so plants
  cluster in coherent patches rather than per-cell salt.
- **Density:** `baseDensity × biome.FloraMul × theme.DensityMul × VegetationRichness` (0.45–2.2,
  coupled to the same forest mask the trees use), capped at 0.95 so some bare ground always remains.

**Trees:** exactly **one tree species per world** (its own name + toxicity), growing in up to **5 form
archetypes** (Broadleaf / Conifer / Palm / Jungle / Dead) — whichever the biome theme allows. Trees
cluster via a forest mask (`TerrainScale × 2`): dense grove >0.62 → 9× local density, fringe 2×, open
land 0.15×. Fungal/crystal themes grow no trees (giant mushrooms instead).

All plant life on a world is re-tinted to one seeded **flora base hue** (green-dominant, with rarer
brown/pink/violet/amber exotics).

---

## 7. Fauna

Derived per world by
[`CreatureGenerator.cs`](../../src/BlocksBeyondTheStars.WorldGeneration/CreatureGenerator.cs); spawned
live by `GameServerCreatures.cs`.

- **Species per world** from `CreatureAbundance`: `none` → 0, `few` → **3**, `many` → **6** (airless
  bodies → 0).
- **No fixed catalogue** — each species is *composed* from traits: a **habitat** (Land/Water/Lava/
  Air/Cave/Amphibian), weighted by what the planet allows (water needs `WaterAbundance > 0.15` or a
  wet biome; lava only on lava/basalt worlds; cave only if `CaveThreshold > 0`); an **activity cycle**
  (diurnal 40 / nocturnal 30 / crepuscular 20 / cathemeral 10); a **temperament** biased
  non-hostile (passive 42 / skittish 30 / territorial 16 / aggressive 9 / pack-hunter 3 — only the
  last two are hostile). Stats (size 0.6–2.2, HP, speed, damage), morphology (legs, eyes, horns,
  tentacles, bioluminescence…), locomotion style and drops are all rolled from the species seed.
- **Per biome:** on multi-biome worlds each species gets a **biome affinity** (`rng.Next(biomeCount)`,
  or −1 = anywhere). Spawning prefers biome natives, then falls back to any species. So with 3 species
  across 3 biomes you get roughly **one native species per biome** plus the biome-agnostic ones.
  Natives are tinted ~45 % toward their biome's anchor hue, so region A's fauna reads green-ish and
  region B's violet-ish on the same world.

**Live spawning:** a dynamic world cap (scaled by circumference × abundance × √players), a hard cap of
12 wild creatures near a player, ring placement 18–45 blocks out, habitat gates (water animals only in
water columns, cave animals only in caves), despawn beyond 70 blocks. Only **awake, hostile** creatures
deal damage — sleepers never attack — and the day/night cycle gates which species are awake.

---

## 8. Water, rivers & seas

Three cooperating systems (full detail in [FLUID_ROUTING.md](FLUID_ROUTING.md)):

**a) Sea level & fluid type** (`ResolveSeaFluid`): a world with an atmosphere (and `WaterAbundance ≠ 0`)
gets a **water sea**; a dry **volcanic** (basalt) or **airless** world gets a **lava sea**; otherwise
it is dry. The level sits *below* `BaseHeight` so only genuine low ground floods —
`level = BaseHeight + (abundance − 0.95)·Amplitude` for water — and higher abundance raises it.

**b) Upland ponds:** an FBM mask plus a slope gate (only on ground with slope ≤ 4) carves shallow
0–5-block pools above sea level, filled flush to the surface so they sit level, not as exposed bowls.

**c) Rivers — routed downhill, not noise bands:**
- [`RiverNetwork.cs`](../../src/BlocksBeyondTheStars.WorldGeneration/RiverNetwork.cs) solves drainage on
  a coarse 8-block grid: a **Priority-Flood** depression fill from the ocean builds a drainage tree
  plus fill-and-spill lakes; deterministic upland **sources** (count ∝ abundance × area) each walk
  downhill accumulating flow; a cell becomes a **channel** at flow ≥ 2 (water) / ≥ 1 (lava); a drop
  of > 4 blocks flags a **waterfall**.
- [`RiverField.cs`](../../src/BlocksBeyondTheStars.WorldGeneration/RiverField.cs) rasterises those
  channels to block resolution: width grows with flow (headwater brooks 1 wide, trunks up to 7 water /
  9 lava), estuaries flare toward the sea, waterfalls get explicit fall columns.

Per column the precedence is **pond > river > sea**. On lava worlds the *same* machinery routes lava
(sparser, wider, shallower), tagged for shader animation. Submerged columns also grow aquatic flora
(kelp/seagrass/coral on the bed, lily pads on the surface).

---

## 9. Weather — coupled to biome, not terrain

Day/night + weather are server-authoritative in
[`GameServerWeather.cs`](../../src/BlocksBeyondTheStars.GameServer/GameServerWeather.cs).

- A **world-global state machine** cycles `clear → clouds → rain → storm` (plus `fog`), stepping every
  25 s — forward with the planet's `StormChance`, otherwise back. Planets with a fixed `Weather`
  (`clear`/`overcast`) never change; airless worlds have no weather at all.
- A **per-biome offset** (`BiomeWeatherAt`) shifts the world level by a persistent **−1 (drier) to +2
  (wetter)** per biome, so a swamp biome can storm while a neighbouring dry biome stays sunny. Weather
  is sent **per player by their current biome**, not as one world broadcast.

What is *not* tied to terrain or biome:

- **Temperature** = planet base + per-world variation (±14 °C) + weather delta + a **day/night swing**
  (±6 °C with air, ±16 °C airless). Elevation and biome don't enter it.
- **Precipitation form** follows temperature + surface block: sand → sandstorm, ≥55 °C → ash,
  ≤−15 °C → hail, ≤2 °C → snow, else rain.

So the only coupling is **weather ⇄ biome** (via the discrete biome index); terrain height never
influences weather, and the biome index itself is independent of terrain.

---

## 10. Per-chunk generation order

When a chunk is generated (`WorldGenerator.Generate(planet, coord)`):

1. Void worlds (ships/stations) → empty chunk and return.
2. Resolve per-world constants: planet seed, biomes, deep/bedrock blocks, cave threshold, ore
   richness, mantle depth, flora multiplier (all seeded).
3. Resolve fluids: sea level + type, pond mask, river field (memoised per world).
4. Per column: surface height → water carve (pond/river/sea precedence) → biome index → optional
   floating islands / crater floors → vertical fill (air/fluid, bedrock, floor band, caves,
   surface/deep blocks, ore veins, rare data caches) → surface or aquatic flora.
5. Feature stamps with cross-chunk margins: trees, giant mushrooms, geysers, and set-dressing
   (boulders, crystal shards, dead logs, monoliths, stone circles).
6. Landing-pad flattening where a pad is reserved.

Every step is a pure function of `(seed, planetKey, coordinates)` — no persistent world storage, so
server and clients build the same chunk independently.
