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
- seed, body id (salts the terrain, #478), planet type, circumference;
- the save's continents, terrain generation and lava-core volcanoes (all from `JoinAccepted`);
- the airless-moon crater flag.

The bake writes an equirect map: row 0 is the south edge of the latitude band, column 0 is longitude 0, the layout
Unity's sphere UVs and the landing map share. Per pixel it reads `WorldGenerator.SummarizeSurface`, a read-only
query that chunk generation never calls, so the goldens are unaffected. That query gives the column's height, its
biome, the ground block (the biome's own surface; snow or ice above the snow line) and its plant cover (the
biome's flora and tree multipliers, faded by the cold). Seas show depth, ice on cold seas and the world's own water
colour (`FluidTints`); vegetation shows in the world's own flora hue (`FloraTints`).

The job keeps per-pixel height, biome index and temperature next to the colours; the weather projection needs
them.

**Cost and threading.** One body costs ~90 ms of calibration (once per session; the generator caches it statically)
plus ~40 ms for 96×48 or ~100 ms for 256×128 on .NET 10. Unity's runtimes are slower, so `WorldMinimap` never bakes
synchronously:
- **Desktop:** up to two bakes run on worker threads (`Task.Run`). Every bake owns its `WorldGenerator`; the
  generator's shared caches are already lock-protected for the server's chunk pool.
- **WebGL:** managed threads do not run there, so bakes are time-sliced into rows under a 4 ms budget per frame.
- **The atlas:** ground colours are read from the block atlas on the main thread before a job starts. The job
  touches no Unity API.
- **Progressive display:** callers show the flat data-driven colour (`PlanetOrbitLook.GroundColor`) until their
  callback swaps the map in.
- **Priority:** the planet below you and the landing map go first.
- **Lifetime:** textures are cached per request key and destroyed on world exit (#966).

## 2. The atmosphere from data

`OrbitLook.For` applies the server's own rules:
- no clouds on airless and space-sky bodies;
- otherwise the type's `cloudDensity`;
- the per-world sky and cloud tints from `Shared.World.AtmosphereTints`. These moved from `GameServerWeather` and
  are bit-identical; a server test pins it.

The flight view used a hard-coded table of 15 type keys before #2170, so 33 of 51 types flew past without clouds.
The haze rim takes the world's sky colour.

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
- plus one step above the cloud line;
- clamped into the world's ladder band.

Events (fog, blizzard, ion storm, …) blanket the world and ignore every shift. `Precipitation` turns a state and a
temperature into what falls.

`WeatherMapProjector` (Client.Core) runs the same functions per map pixel on the snapshot. A headless test projects
the real server's snapshot onto a baked map and compares it pixel by pixel with the server's own `BiomeWeatherAt`.

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
  rule) faces the star. The sphere mesh's UV seam and direction are measured once from the mesh. A loaded body's
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
| Textures | shell 32 KB per body with clouds; landing layer 128 KB while open; M layer 16 KB |
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
- `PlanetWeatherClientTests`:
  - orbit look over all types;
  - the `WeatherLook` table;
  - spin maths;
  - snapshot extrapolation;
  - bake slices == one run;
  - biomes, snow and craters on the map;
  - projector == shared formula;
  - end to end: client projection == server weather.
