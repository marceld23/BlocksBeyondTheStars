# Planets from orbit and their weather

How the flight view, the bodies in the surface sky and the maps show a planet as the world you will land on, and how
the live weather reaches them (#2170–#2179). Read [WORLD_GENERATION.md](WORLD_GENERATION.md) for the generator itself and
[MULTIWORLD_AND_SYSTEM_FLIGHT.md](MULTIWORLD_AND_SYSTEM_FLIGHT.md) for resident worlds and system flight.

## The pipeline at a glance

```
server (authoritative)                                         client
──────────────────────                                         ──────
WeatherSim per body of every occupied system  ── SystemWeather ──►  SystemWeatherState (extrapolates fronts/clock)
  (loaded world ticks it, else 1 Hz ambient)      (10 s + events)        │
                                                                         ▼
deterministic world generator  ◄── same seed/body/type/gen ──  PlanetMapBakeJob (worker thread / time slices)
                                                                         │  colours + height/biome/temperature
                                                                         ▼
Shared.Weather.WeatherProjection  ◄──── one formula ────────►  WeatherMapProjector (per map pixel)
  (BiomeWeatherAt on the surface)                                         │
                                                                         ▼
pad weather in LandingPadList                                  cloud shells · landing-map layer · M-map layer · glyphs
```

## 1. The planet map (what a body looks like)

`PlanetMapBakeJob` (Client.Core) runs the real `WorldGenerator` with the inputs that make the server's terrain:
- seed, body id (salts the terrain, #478), planet type, circumference — the server's value off the star map
  (`NetBody.Circumference` through `BodySize.CircumferenceOf`, #2691), never recomputed on the client;
- the save's continents, terrain generation and lava-core volcanoes (all from `JoinAccepted`);
- the airless-moon crater flag.

The bake writes an equirect map: row 0 is the south edge of the latitude band, column 0 is longitude 0, the layout
the globe's UVs (§1a) and the landing map share. Per pixel it reads `WorldGenerator.SummarizeSurface`, a read-only
query that chunk generation never calls, so the goldens are unaffected. That query gives the column's height, its
biome, the ground block (the biome's own surface; snow or ice above the snow line) and its plant cover (the
biome's flora and tree multipliers, faded by the cold). Seas show depth, ice on cold seas and the world's own water
colour (`FluidTints`); vegetation shows in the world's own flora hue (`FloraTints`).

Upland ponds come from the summary's own `Pond` flag (#2758), never from `SurfacePondDepth`. From terrain
generation 24 a pond fills to one level, which `SurfacePondDepth` finds by searching the whole pond. Map pixels lie
dozens of blocks apart and never share a pond, so every water candidate paid a full search: a landing map took
2–14 s and the browser never finished one. The flag stops at the pond gates (mask, flat ground, neighbours, no
volcano, above the sea). It is a superset of the real ponds at map scale and the exact answer below generation 24.
`PlanetMapPondTests` holds a bake to zero pond-level searches.

The job keeps per-pixel height, biome index and temperature next to the colours; the weather projection needs
them.

**Cost and threading.** Measured 2026-10-18 on .NET 10 on a generation-26 system (#2758):
- calibration: 15–200 ms per body (once per session; the generator caches it statically);
- 96×48: 5–60 ms;
- 256×128: ~100 ms.

Before the pond flag, a large planet took 2.5–3.8 s for 96×48 and up to 13.7 s for 256×128. Unity's runtimes are
slower, so `WorldMinimap` never bakes synchronously:
- **Desktop:** two bakes run on worker threads (`Task.Run`), plus one slot reserved for interactive requests
  (`PlanetMapSchedule`, #2761). Every bake owns its `WorldGenerator`; the generator's shared caches are already
  lock-protected for the server's chunk pool.
- **WebGL:** managed threads do not run there, so bakes are time-sliced into rows under a 4 ms budget per frame.
- **The atlas:** ground colours are read from the block atlas on the main thread before a job starts. The job
  touches no Unity API.
- **Progressive display:** callers show the flat data-driven colour (`PlanetOrbitLook.GroundColor`) until their
  callback swaps the map in. The landing map never shows a blank panel (#2760). It shows the body's coarse 96×48
  map at once, stretched; that is the orbit sphere's map, usually baked already. A small "Mapping the surface…" note
  stays in the corner until the sharp 256×128 map replaces it, and the weather layer is rebuilt at the new size.
- **Priority:** the planet below you and the landing map go first. Priority orders which bake starts next; it
  never stops a running bake. A request at `PlanetMapSchedule.InteractivePriority` (20) or higher therefore gets a
  slot of its own. The landing map's coarse map (21) and sharp map (20) start even while two orbit bakes run.
  Entering space queues every body of the system, and before #2761 the landing map waited behind them. The browser
  serves the highest priority first every frame and needs no slot.
- **Lifetime:** textures are cached per request key and destroyed on world exit (#966).

## 1a. The globe: own mesh and polar caps (#3127/#3128)

The orbit planet, its haze and cloud shell, and the bodies in the surface sky all wear one shared mesh,
`FxKit.GlobeMesh`, built from `GlobeMeshData` (Client.Core). It replaces Unity's primitive sphere: the primitive has
24 segments and a triangle fan at each pole whose UVs shear the map differently in every triangle, which showed as
straight wedges at the poles.
- **Layout:** radius 0.5, 48 × 24 segments. v runs from 0 at the south pole to 1 at the north pole, linear in
  latitude like the map rows. u = 0 sits at +X and the azimuth `atan2(z, x)` grows with u, so the map reads
  unmirrored from outside. The seam column exists twice (u = 0 and 1).
- **Poles:** each pole triangle has its own pole vertex, with u at its segment's centre. That removes the shear.
- **Winding:** every triangle faces outward, so the back-face-culling shaders draw what they drew on the primitive.
- **Mapping:** `PlanetWeatherVisuals.MeasureMapping` measures the seam and direction from this mesh, so the spin and
  the lightning points follow it. `PlanetGlobeTests` pins the layout, the winding and the convention.

A planet's surface is a torus: it wraps north–south too (`WorldConstants.LatitudePeriodFor`). So the map's first and
last rows, ordinary strips of terrain, each squeeze into one point at a pole. The pole vertices cannot fix that.
`PlanetGlobeCaps` hides it on the CPU:
- Rows from 62° latitude blend toward the world's average ground colour (`PlanetMapData.GroundRgb`), fully from 72°.
- `WorldMinimap.RequestGlobe` gives the globes a capped copy of the cached map, once per cache entry. The raw texture
  stays untouched, because the landing map's coarse preview shares the entry. The weather still projects onto the raw
  `PlanetMapData`.
- `ComposeShell` multiplies the cloud alpha by `1 − PlanetGlobeCaps.Fade(latitude)`, so the clouds do not pinch over
  the caps either.
- There is no shader switch: the Cloud shader is shared by many other effects.

## 2. The atmosphere from data

`OrbitLook.For` applies the server's own rules:
- no clouds on airless and space-sky bodies;
- otherwise the type's `cloudDensity`;
- the per-world sky and cloud tints from `Shared.World.AtmosphereTints`, with the save's terrain generation from
  `JoinAccepted` (from generation 26 the families come from the mixed hash, #2692). These moved from `GameServerWeather` and
  are bit-identical; a server test pins it.

The flight view used a hard-coded table of 15 type keys before #2170, so 33 of 51 types flew past without clouds.
The haze rim takes the world's sky colour.

## 2a. The four seasons (#3041/#3042)

The system clock (`_systemTimeDays`, 600 s per system day) is saved since #3041 (`WorldMetadata.SystemTimeDays`), so a restart
no longer starts the wet/dry season and the year over. `Shared/World/PlanetSeasons` derives a body's season from that clock,
the body's day length and a phase seeded per world and body: 5 planet days per season, spring → summer → autumn → winter. A
type with air, no void and `seasonTemperature > 0` (`planets.json`, default 8) has them. The server adds the warmth
(± `seasonTemperature`, cosine over the year) in `TemperatureAt` — the active world, the landing-pad reading — so the suit,
the precipitation form (winter snow on a cold world) and the HUD feel it; `WeatherRegrowFactor` multiplies the season's
growth (spring 1.2, autumn 0.9, winter 0.6). `WorldEnvironment.Season/SeasonProgress/SeasonWarmth` (additive) carry it to
the client: `SeasonLook` (Client.Core) turns season + progress into three targets that `Sky.Seasons` eases and sets as the
global `_Sc_Season` every frame — `FloraAccent.hlsl` turns plant tints amber in autumn and pale in winter and strengthens
the blossom accents in spring, `ScatterLit` turns the grass tufts the same way; no chunk is re-meshed (the far terrain keeps
its baked colours). A cold winter adds a frost on the snow channel, the grade warms in summer/autumn and cools in winter,
and the clock panel names the season.

## 3. Ambient weather (server)

`GameServerAmbientWeather` keeps a `BodyWeather` for every Planet, Moon and AsteroidField body of a system with a
joined player in it. Each one holds a `WeatherSim` built exactly as a world load would build it: the same seed and
the same `NewWeatherSim` set-up of ladder band, season, volatility and event weights.

- **A loaded world ticks the sim itself** in `TickWeather`. Every other body is advanced at 1 Hz in
  `TickAmbientWeather`, with the arrival clock (0.35) as its day fraction.
- **`InitWeather` adopts the body's ambient sim** when a world loads. It is the same object, so you land in the
  weather you saw and nothing restarts. A world that loads with no ambient entry registers its fresh sim there,
  so the body's weather keeps running after the last player leaves.
- **The orbital clock** (`_systemTimeDays`) now advances once per server tick. Before, it advanced once per
  occupied world, so two loaded worlds ran it twice as fast. The per-biome offsets rotate on it.
- **Restarts:** ambient weather is runtime-only, like the orbital clock. A restart starts every body fresh.
- **Fog frequency (#3269):** each `BodyWeather` carries its world's `MistProfile.FogFrequency`, so the ambient
  sim rolls fog and ground fog exactly as often as the loaded world does (`WeatherContext.FogFrequency`
  multiplies both event weights).

**`SystemWeather` (tag 281).** One snapshot per system lists, per body:
- the episode: state, ladder severity, band, peak, intensity and the rolled precipitation form;
- the wind;
- the fronts: centre, true half-width, drift and boost;
- the body's time of day, and whether its clock runs.

It is ~30–40 bytes per body and is sent:
- on join;
- on entering space (before the space state, so the view builds with it);
- with every landing-pad list;
- as a 10 s heartbeat.

**Pad weather.** `FillPadWeather` evaluates the shared formula for each pad. It switches the shared generator to
the target body *with that body's levelled pads* (`PadFlats`), so a pad's biome and height match the loaded world
exactly, and restores the generator afterwards. `NetLandingPad.Weather`/`Precipitation` are appended contractless
fields.

## 4. One weather formula

`Shared.Weather` now holds `WeatherFamily`, `WeatherDef` and `WeatherCatalog`. `WeatherSim` stays on the server.

`WeatherProjection` is the per-position rule the server's `BiomeWeatherAt` calls:
- the world episode;
- plus the persistent per-biome offset, which rotates every 6 system-days;
- plus any front covering the longitude;
- plus one step above the summit line (`MistProfiles.SummitLineY`, #3269: the base of the body's type-relief cloud band,
  else the type's `WeatherProjection.CloudLineY`);
- clamped into the world's ladder band.

Events (fog, blizzard, ion storm, …) blanket the world and ignore every shift. `Precipitation` turns a state and a
temperature into what falls.

`WeatherMapProjector` (Client.Core) runs the same functions per map pixel on the snapshot. A headless test projects
the real server's snapshot onto a baked map and compares it pixel by pixel with the server's own `BiomeWeatherAt`.
The surface, `FillPadWeather` and the projector all take the summit line from the body's mist profile; the projector
needs the body id for it (its optional `bodyId` argument — without one it falls back to the type's cloud line).

**Fronts are drawn wider than they are.** A front's true band is 140–520 blocks, 1–4 pixels on a 96-pixel map. The
views feather it out to at least 4 % of the circumference (`VisualFrontHalfWidthShare`). The pixel's *true* state,
which is what the pad captions and the surface use, never changes; only the drawn cover and tint do.

`WeatherLook` (Client.Core) is the single table per state for every view:
- the surface sky's cover, darkening, wind, storm towers and cirrus (formerly a `switch` in `Clouds.cs`);
- the HUD/map glyph and the family warning colour (formerly in `HudUi`);
- the orbit tint: snow white, acid yellow-green, ash dark, sand like the ground, spores in the flora hue.

## 5. The views

- **Flight view (`SpaceView`)**
  - Each body is an `OrbitBody`. Its cloud shell is a 128×64 RGBA texture composed on the CPU from a cached fBm
    pattern × the projected cover, with the tint in the colour channel. The Cloud shader only adds the star light
    and the night shade, so there is no new shader and no extra draw call.
  - The shell drifts slowly with the wind relative to its planet. Composition counter-shifts the weather, so a storm
    stays over its land.
  - It is recomposed when a snapshot arrives, and in between every 1.5 s for a planet you are near and every 6 s for
    the rest. It is never recomposed per frame.
  - Lightning (`FxKit.Flash`, fewer and softer with "reduce flashes") flickers in storm pixels of the nearest planet
    only. A meteor shower streaks into it; an ion storm makes its rim shimmer.
- **Spin (#2177)** — `PlanetSpin` turns each sphere so the noon meridian (`u = 0.5 − timeOfDay`, the landing map's
  rule) faces the star. The globe mesh's UV seam and direction are measured once from the mesh (§1a). A loaded body's
  clock is extrapolated with its day length; an unloaded one waits at its arrival hour. `SkyBodiesView` turns the
  sky discs the same way.
- **Landing map (#2176)**
  - The terrain map plus a weather layer (`PlanetWeatherVisuals.ComposeMapLayer`): clear sky transparent, every
    weather in its tint with alpha by severity.
  - Drift arrows mark the fronts, and every pad gets a glyph + weather-name caption.
  - The layer sits under the day/night band and the pads. `MapWeatherLayer.On` (PlayerPrefs) is the remembered
    switch, shared with the M map.
- **M world map, system chart, travel orrery (#2178)**
  - The M map projects the body's 256×128 map onto the shown region with fronts at their true width, and lists the
    weather where you stand.
  - The system chart and the travel orrery (current system only) show a glyph per body.

## Budgets

| Item | Budget |
|---|---|
| Bake | off the main thread (desktop) / ≤ 4 ms per frame (WebGL) |
| Weather projection | per snapshot, < 1 ms for 96×48; 256×128 only while the landing map is open |
| Textures | shell 32 KB per body with clouds; capped globe copy ~24 KB per body (#3128); landing layer 128 KB while open; M layer 16 KB |
| Network | ~0.5 KB per player every 10 s |
| Server | one `WeatherSim.Advance` per ambient body per second |

## Tests

- `PlanetWeatherServerTests`:
  - ambient sims exist and advance;
  - landing adopts the sim;
  - the snapshot lists the system;
  - pad weather == the surface's weather after landing;
  - the landing-map clock of a held body;
  - the once-per-tick orbital clock;
  - the shared tints.
- `WeatherProjectionTests` — the shared formula and precipitation.
- `MistProfileTests` / `MistProfileServerTests` (#3269) — the mist profile's properties (determinism, per-world
  variety, none without air, bright colours with ~60 % tinted, the band on the cloud line, fog weights scale with
  the fog frequency) and its wiring into `WorldEnvironment` and the summit line.
- `PlanetGlobeTests` — the globe mesh (counts, seam, pole vertices, outward winding, the mapping `PlanetSpin` turns)
  and the polar caps.
- `PlanetWeatherClientTests`:
  - orbit look over all types;
  - the `WeatherLook` table;
  - spin maths;
  - snapshot extrapolation;
  - bake slices == one run;
  - biomes, snow and craters on the map;
  - projector == shared formula;
  - end to end: client projection == server weather.
