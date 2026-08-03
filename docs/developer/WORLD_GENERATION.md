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
- Per system: **2–6 planets**, each with **0–3 moons**, plus **2–3 landable asteroids** and (rarely)
  **1–3 space stations**.
- Each asteroid rolls one of five **families** (#515) — `asteroid` (stony), `asteroid_metallic`,
  `asteroid_icy`, `asteroid_carbon`, `asteroid_crystal` — weighted by each family's `spawnWeight`, so
  adding one is a pure `planets.json` change. They are all `selectable: false` (never a system planet)
  and all recognised by `WorldConstants.IsAsteroidType`, which is what maps them to the Asteroid size
  class. The draw uses its own hash, so it never shifts a system's stations or wrecks.
- Each body gets a **deterministic orbit position** (polar coordinates around the star: first planet
  ~420 units out, +520 per planet ± jitter; moons 90 + m·55 around their planet) and an **orbit
  period** (planets 6–40 in-game days, moons 0.4–2.5, ~20 % retrograde). The orbit period is a
  purely visual phase driver — it never disturbs landing, travel distance or pad reservations.
- **System archetypes (#546, worlds created ≥ 0.9.2):** when `WorldDescription.SystemVariance` is on,
  each system rolls a character class in
  [`SystemArchetypes.cs`](../../src/BlocksBeyondTheStars.WorldGeneration/SystemArchetypes.cs)
  (own `Hash01` salt 500; 501/502 serve the size-bias and twin-orbit draws) that shapes the counts
  above: **Standard** (30, exactly the legacy rolls), **Lone Giant** (12: 1 size-biased planet,
  4–8 moons), **Swarm** (12: 6–9 small planets, ≤1 moon), **Belt** (10: 5–8 asteroids), **Hub**
  (10: stations guaranteed), **Desolate** (12: 1–2 planets, nothing else), **Pirate Haven**
  (8: no stations, wrecks doubled), **Twin Worlds** (6: two like-sized planets on close orbits).
  System 0 (home) never rolls Desolate/Pirate Haven. Runtime consumers (trader traffic, pirate flag,
  camp odds, drones) resolve the archetype from the seed via `SystemArchetypes.For` — nothing is
  persisted. `SystemVariance` **defaults to false** on `WorldDescription` (old saves regenerate
  unchanged) and to **true** on `ServerConfig`'s creation-time description (new worlds get it).
- **Per-body size bias (#549):** archetypes set `CelestialBody.SizeBias` ∈ [-1, 1], which
  `WorldConstants.CircumferenceFor(key, class, bias)` maps onto an extended band (planets
  4000–16000 instead of 5000–12000). Bias 0 is bit-identical to the classic hash — that invariant
  protects every existing save's terrain. The client receives the bias via `NetBody.SizeBias` and
  must pass it to every `CircumferenceFor` call (orbit spheres, sky bodies, pad-map bakes).
- **Asteroid belts (#683, worlds created with `WorldDescription.AsteroidBelts`):** the system's
  landable asteroids share 1–2 **orbit annuli** instead of scattering across the whole disc (which
  regularly parked them inside a planet's orbit lane). The outer belt always sits one orbit step
  beyond the outermost planet; big systems (5+ planets, 4+ asteroids) may roll a second, inner belt
  into a ≥620-unit gap between two planet orbits. Members occupy evenly spaced angular slots with a
  small wobble (radial jitter ±60), so the flight view's clear-gap guarantee holds by construction.
  Geometry uses the `Hash01` **8xx salt series** and never the body rng. Like `SystemVariance`, the
  flag **defaults to false** (old saves keep the legacy `DiscPoint` scatter byte-identically) and to
  **true** on `ServerConfig`'s creation-time description; `--belts off` is the escape hatch
  (mirrors `--variance`). At runtime every asteroid body also carries a **mineable rock cluster** at its flight-view
  position (server: `AddBeltRockClusters`, shared transform `SystemBodyLayout.FlightViewScale`),
  and launching *from* an asteroid spawns a dense 9-rock local field instead of the classic trio.
  The flight chart draws grouped members as one translucent belt band (`ui.map.belt`) instead of
  stacked per-body orbit rings.

A `CelestialBody` stores only Id, Name, `Kind` (Planet/Moon/AsteroidField/SpaceStation/Wreck), a
**`PlanetType` key**, and orbit data. The body's *content* is generated only when a player enters it.

Each layer uses a distinct hash salt (e.g. `Noise.Hash(seed, systemIndex, 1, 1)` per system, separate
calls for angle/radius/period) precisely so unrelated bodies don't shift when one is added.

### Naming (#678)

Names are **display-only** (everything keys on the body `Id`) and come from a naming-only rng stream
(salt 700 per system) in [`NameGenerator.cs`](../../src/BlocksBeyondTheStars.WorldGeneration/NameGenerator.cs):

- **Systems** roll one of several registries: coined proper names (~55 %, "Tharion"), catalog
  designations (~25 %, "HX-113"), two-part region names (~15 %, "Ember Veil", "Korveth's Reach") and —
  in archetype-varied space — rare archetype-flavored names (~5 %; pirate space sounds menacing, hubs
  busy). Names are deduped galaxy-wide.
- **Planets** carry designations — Roman numerals ("Tharion II"), exoplanet letters in catalog systems
  ("HX-113 b") — except **landmark worlds**, which get a coined proper name flavored by their planet
  type (ice sounds cold, lava harsh): ringed planets, the Lone Giant, Twin Worlds (one stem, two
  endings), the Hub capital, and the start planet (proper-named post-hoc by the server via
  `EnsureStartPlanetProperName`, mirroring the ring guarantee).
- **Moons** of designation planets are lettered ("Tharion II-a"); moons of landmark worlds get short
  coined names of their own.
- **Asteroid fields and wrecks** are single coined words ("Skarrak"); stations stay attributive
  ("<planet> Station") except a Hub's first station, which is a coined port ("Port Halvek"). No English
  kind words live inside generated names anymore — the client pairs names with localized kind labels.
- A blocklist guard rejects coined words containing EN/DE profanity substrings.

⚠ The legacy `MakeName` draws are still **burned in place** on the body rng: its three draws sit in
front of every planet-count/type draw, so removing them would regenerate every existing universe.
`GalaxyLayoutRegressionTests` pins the exact pre-rework layout for fixed seeds — it must never break.
The retroactive rename is safe because names were never persisted or used as keys.

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

**a) Eight terrain archetypes** (`ArchetypeOffset`, explicit landform shapes since #576):

| Archetype | Shape |
|---|---|
| Flats | `h·A·0.18` |
| Rolling plains | `h·A·0.55` |
| Hills | `h·A·1.00` |
| Mountains | lightly ridged, `·A·1.9` |
| Canyons | strongly ridged, `·A·1.3` |
| Plateau decks (#576) | height quantised into terrace decks (`step = max(5, A·0.5)`) |
| Extreme peaks (#576) | sharpened ridged crests up to `·A·3.4` — the far tail of relief |
| Rift gorges (#576) | gentle swell gashed by deep ridged canyons (to `−A·3.0`) |

**b) Regional blend** (`BlendedArchetypeOffset`) — the heart of *within-world* variety. Each world
seed-picks **2–8** of the archetypes (from a rotated start index); a **broad field** (`TerrainScale × 6`,
3 octaves) selects a point in that subset per position and **smoothstep-blends the two nearest
archetypes' computed offsets** (offset-space, since decks/gorges cannot blend as parameters). So one
region reads flat, the next hilly, the next as terraced mesa country or a ridged mountain range.

**c) Per-world drama** (`DramaFor`) — a seeded **0.9–1.5×** multiplier on the whole relief, so the
same planet type rolls gentle on one world and jagged on the next. A **~6 % tail rolls 1.9–2.6×**
(#576): the rare outlier world whose relief reads genuinely extreme.

**d) Landmark landforms** (#477/#577/#578) — sparse discrete features on a deterministic hotspot-cell
grid, at most **one per column** (precedence volcano > massif > table mountain > rift): volcano cones
with molten craters, rare **massifs** (+120–220, ridged flanks, snow/ice summits), flat-topped
**table mountains** (radius 40–120, near-vertical walls) on dry rocky-reading worlds, and **rift
chasms** (50–130 deep, fjord-flooded below sea level). `SurfaceHeight` clamps everything at **Y 288**,
safely under the ~Y 320 atmosphere line.

**e) Overriding shapes** — `TerrainStyle` (mesa, **tablelands**, dunes, **badlands**, spires,
**karst**, flats… — the bold three are #579), `Cratered` (flat regolith + impact craters for airless
bodies), and `FloatingIslands`. A cratered body skips (c) and (d) entirely and instead rolls a
**`CraterProfile`** from its own body seed (#518): crater density, basin width, depth (5–12 blocks),
rim height and how rolling the regolith between craters is — so one rock is a pounded ruin and the
next a near-smooth pebble. Landable asteroids and airless moons share this path.

Terrain noise is **torus-periodic FBM** (4 octaves); the cave and ore fields are single-octave torus
value noise whose thresholds are **quantile-calibrated per world** against the field's measured
distribution (#472) — so data thresholds keep their meaning and everything stays seamless across both
wrap seams. Carved cave cells below the per-world **lava table** (~64–128 deep) fill with molten rock
in **coherent molten regions only** (#580): a coarse pocket field leaves ~40 % of the deep caverns
open, so the deep kilometre stays explorable.

Below the surface: surface/sub-surface layers (`SurfaceDepth`, default 4) → deep block → per-world
mantle → an unmineable bedrock foundation at 256–2048 blocks down (with a 6-block lava/basalt band
above it so digging out the bottom is impossible). The server's vertical build band reaches
**Y −2100** (#580), so even the deepest foundation is reachable — "dig to the bedrock" works on every
world. Ore veins (3D noise × rarity × per-world richness) and caves (3D noise, if `CaveThreshold > 0`)
are carved into the crust; ore density ramps up to **+60 % over the first ~600 blocks down** (#580),
so the descent pays.

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

Derived per **body** (#478: the roster seed is the world seed salted with the location id, so two
worlds of the same planet type grow different species) by
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

Derived per **body** (#478, same location-id salt as flora) by
[`CreatureGenerator.cs`](../../src/BlocksBeyondTheStars.WorldGeneration/CreatureGenerator.cs); spawned
live by `GameServerCreatures.cs`.

- **Species per world** from `CreatureAbundance`: `none` → 0, `few` → **5**, `many` → **9** (airless
  bodies → 0). The first 3 / 6 indices keep their exact pre-#640 rolls (each species draws its own
  sub-seed), so worlds created before the bump keep their known species and simply gain new ones.
- **Diversity guarantee (#640):** after the rolls, only the *appended* slots may be re-drawn into
  missing niches — priority ground (Land) → flier (Air) → aquatic (Water/Amphibian, water worlds
  only) — so no living world is all-cave or all-sea.
- **No fixed catalogue** — each species is *composed* from traits: a **habitat** (Land/Water/Lava/
  Air/Cave/Amphibian), weighted by what the planet allows (water needs `WaterAbundance > 0.15` or a
  wet biome; lava only on lava/basalt worlds; cave only if `CaveThreshold > 0`); an **activity cycle**
  (diurnal 40 / nocturnal 30 / crepuscular 20 / cathemeral 10); a **temperament** biased
  non-hostile (passive 42 / skittish 30 / territorial 16 / aggressive 9 / pack-hunter 3 — only the
  last two are hostile). Stats (size 0.6–2.2, HP, speed, damage), morphology (legs, eyes, horns,
  tentacles, bioluminescence…), locomotion style and drops are all rolled from the species seed.
- **Body plans (#637/#638):** drawn *after* every legacy roll (the same discipline as the locomotion
  style, so pre-plan worlds keep their species identity), a species may swap its architecture:
  **Medusa** (25 % of Air/Water species) — translucent pulsing bell, 6–10 rim tentacles, drifting,
  usually glowing, never hostile, per-species hover altitude 3–12; **Titan** (18 % of Land species) —
  size 3.5–6, pillar legs, neck (≥2 segments reads giraffe) or trunk, horns worn as tusks, HP ×3.5,
  drops 3–6, dangerous when provoked but never a pack-hunter. Everything else stays **Standard**.
- **Social species (#639):** each species rolls a `SocialGroupSize` (1 = solitary): titan herds 2–4,
  schooling water species 3–5, some flocks of fliers 2–4, occasional grazer pairs/trios. The spawner
  places the whole group together (4–8 blocks apart, each member habitat-gated and cap-counted), and
  roaming members drift gently toward nearby kin so groups stay loosely together.
- **Per biome:** on multi-biome worlds each species gets a **biome affinity** (`rng.Next(biomeCount)`,
  or −1 = anywhere). Spawning prefers biome natives, then falls back to any species — roughly
  **one-plus native species per biome** plus the biome-agnostic ones.
  Natives are tinted ~45 % toward their biome's anchor hue, so region A's fauna reads green-ish and
  region B's violet-ish on the same world.

**Live spawning:** a dynamic world cap (scaled by circumference × abundance × √players; a lush big
world reaches ~25–45, backstopped by a safety ceiling of 64 — #470), ring placement 18–45 blocks out
(two rotors: the ring slot advances on every attempt, the species on success), habitat gates (water animals only in
water columns, cave animals only in caves; titans additionally need a 3×3 level-ground clearance),
despawn beyond 70 blocks (titans 110 — a landmark animal must not evaporate mid-approach). Only
**awake, hostile** creatures deal damage — sleepers never attack — and the day/night cycle gates
which species are awake. Bite + aggro ranges grow gently with species size past 2 (bite capped at
the player's own 6-block attack reach).

**Terrain-aware roaming (#648, extended by #650–#654):** creatures have no colliders — their Y is
kept by the server — so movement is gated per step instead (`CreatureBehaviour.TerrainStepBlocked`,
the same discard mechanic as the ship hull and energy fences): land walkers accept at most a
2-block ground step (titans 1, mirroring their spawn gate) and never enter water deeper than
1 cell; water species never step out of their water body; fliers, cave/lava dwellers and amphibians
are unaffected. Ground heights come from **real blocks** (#650 — a nearest-standable-cell probe via
`GetBlockIfLoaded`, generator fallback for unloaded columns), so fauna honours player walls, dug
pits and built floors, and land walkers **ease** toward the ground at a capped vertical rate (#652;
fliers ease toward hover — no contour-pen terrain tracing; hoppers keep the snap, their pop is the
motion, and their stride pulses with it, #654). A blocked step first **probes alternative headings**
(±35°/±70°/±110°, #651) and only re-rolls when boxed in — contour/wall following with the
never-stuck property intact. Social species run the full boids trio (#639/#651): cohesion,
size-scaled separation, and heading alignment for schoolers. Hurting a creature (or a skittish bolt)
**startles** same-species kin within 12 blocks for 4 s (#653): non-retaliators flee the nearest
player, retaliators charge, and fleeing prey jinks off the straight escape ray.

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
4. Per column: surface height → water carve (pond/river/sea precedence) → freeze pass (#494: below
   the snow line a water column's top 1–4 cells become solid ice — frozen through to the seabed in
   the deep cold, so cold worlds get walkable frozen seas you can mine into) → biome index →
   optional floating islands / crater floors → vertical fill (air/fluid, bedrock, floor band, caves,
   surface/deep blocks, ore veins, rare data caches) → surface or aquatic flora (kept below any ice
   sheet; nothing grows in a frozen-through column).
5. Feature stamps with cross-chunk margins: trees, giant mushrooms, geysers, and set-dressing
   (boulders, crystal shards, dead logs, monoliths, stone circles).
6. Landing-pad flattening where a pad is reserved.

Every step is a pure function of `(seed, planetKey, coordinates)` — no persistent world storage, so
server and clients build the same chunk independently.

## 11. Terrain wonders (#698–#709, 2026-08-03)

Three stacked waves on top of §4's mechanisms — same determinism rules (pure functions of the seed,
hotspot-cell landmarks, quantile-calibrated consumers):

**Wave 1 — signature scars (class-B reshape).**
- *Mega-rift* (#698): ~3 % of solid-air worlds carry one meandering canyon girdling the entire planet
  (`MegaRiftOffset` — a great-circle path periodic in X, not a hotspot cell).
- *Complex craters* (#699): per-body `CraterProfile` rolls central peaks + terraced walls; hotspot
  crater chains + trig-free ejecta-ray repaint (`CraterChainCarve`, `CraterRayAt`).
- *Terrain grain* (#700): per-world direction for dunes/mountain chains via `GrainFbm` — integer
  stretch + period-normalised shear. ⚠ The latitude period is NOT exactly circumference/2 (chunk
  rounding), so shear must couple in units of the target axis's period or the seam tears.
- *Exotic accents* (#701): hex basalt column fields, travertine terraces (salt repaint + 1-deep deck
  pools), penitente blade-ice fields, Voronoi salt polygons.
- *Ring calderas + whole-planet escarpments* (#702); *style×archetype hybrid* (#703): a rolled
  20–40 % of most styled worlds fades into the archetype blend (identity styles flats/spires exempt).

**Continents (#704, NEW WORLDS ONLY).** `WorldDescription.TerrainContinents` (default true for new
configs, false on load) gates a bimodal platform/basin offset under everything on large planets
(circ ≥ 8000, ~50 % per-body roll, ocean type excluded, lava/ashen → basalt continents in lava
oceans). The sea percentile targets the basin share, so oceans settle at the shelf. The flag reaches
client preview bakes via `JoinAccepted.TerrainContinents`.

**Wave 2 — overhangs (#705–#707).** `GetExtraBands` generalises the floating-island band: up to 6
extra bands per column (island tiers/ponds/waterfalls, arch bars, sea-stack + hoodoo caps, cenote
lips), filled by `Generate`'s band switch. Multi-tier skylands (1–3 layers + stalactites, endless
rim waterfalls), cenotes (sheer shafts; caves open into the walls for free), underground
mega-caverns (`TryGetCavernSpan`, water/lava lakes, crystal-studded floors).

**Wave 3 — the tunnel carver (#708/#709).** `TunnelSpans` rebuilds a seeded worm polyline per
hotspot cell (xorshift, capsule y-spans per column) — noodle tunnels, wider lava tubes on volcano
worlds, skylight shafts, and real cave MOUTHS (tunnels may break the surface). River waterfall
columns incise a plunge-pool slot; ≤ −8 °C worlds grow crevasse fields.
