# Fixed star systems (#3969, #3970)

Most of the galaxy is rolled: `UniverseGenerator` builds system *i* (`sys{i}`) from the world seed, and a growing galaxy
appends `sys{N}`, `sys{N+1}` … the same way. A **fixed star system** is the opposite: a hand-made system whose star and
bodies come from data. Sol — our own solar system — is the first one; a story pack's system (#3357, the Sounders'
Ornemis) is the same mechanism with one body. Adding another fixed system is a data change: one file under
`data/systems/`, its planet types in `data/planets.json`, its names in the locale tables.

## Where a system comes from

`StarSystem.Origin` (`Shared/World/StarSystemOrigin.cs`) says where a system comes from — `Rolled`, `Fixed` (a data
system), `Pack` (a story pack's) or `Finale` (the Guardian's). It is set where the system is created and, like the
rest of the galaxy, never persisted. `StarSystem.IsRolled` is what code holding a system asks; code holding only an id
asks `StarSystemIds` (`IsRolled`, `RolledIndex`, `IsStartRolled`, and `HasRolledPrefix` for the reservation check:
every `sys…` id belongs to the generator). No code tests the `sys` prefix itself any more.

Only the rolled systems take part in the galaxy's seed passes: wormholes, debris fields, derelicts, giant wrecks,
archetypes, civilization homes and unique sites all read the rolled index, and only a rolled system at the rim grows the
galaxy. The Guardian's map spot and the spots of the hand-made systems count only the rolled stars, so none moves another.

## The data file

`data/systems/<key>.json` → `FixedSystemDefinition` (`Shared/World/FixedSystemDefinition.cs`), loaded by
`ContentLoader.LoadFixedSystems` into `GameContent.FixedSystems` and checked by `FixedSystemRules.Problems`. A file with a
problem is reported through the content warnings and **dropped** — never a failed load.

| Field | Meaning |
|---|---|
| `id` | The system id (default: the file name). Key-safe, never `sys…`, never `guardian_finale`, unique. Bodies are `<id>-<body id>`. |
| `nameKey` / `name` | The system's name: the locale key (each player's star map shows it in their language), else the fixed name. |
| `star` | `name`/`nameKey`, `color` (`"0xRRGGBB"` or `"#RRGGBB"`; empty = the rolled colour a hand-made system gets) and `visibleSize` (1 = an ordinary star; stored, not drawn yet). |
| `placement` | `edge` (past every rolled star, on a bearing of its own) or `inside` (the emptiest spot among the stars). |
| `appear` | Exactly one of `afterGrowth: N` (joins with the galaxy's N-th growth, `WorldMetadata.GalaxyGrownSystems` ≥ N, only in a world created with a growing galaxy) and `visibleIf` (story conditions; joins the first time a player meets them and is then per player on the star map, like a pack system). |
| `bodies[]` | Moons after their parent. See below. |

A body (`FixedBodyDefinition`):

| Field | Meaning |
|---|---|
| `id` | The id inside the system (key-safe). |
| `kind` | `planet`, `moon` or `asteroidField`. A moon — and only a moon — names its `parent` (a planet or asteroid listed before it). |
| `planetType` | A loaded type of `data/planets.json`. |
| `orbitRadius`, `orbitAngle` | Its spot around the star (or its parent) at t = 0: system units and degrees. Rolled planets sit 420 + n·520 out, moons 90 + m·55. |
| `orbitPeriodDays` | The visual orbit period (negative = retrograde): planets 6–40, moons 0.4–2.5 like the rolled ones. |
| `circumference` | Walkable blocks, a whole number of chunks in 256–16,000. 0 = rolled from its size class. |
| `gravity` | The gravity factor, within the tested 0.35–1.6. 0 = rolled. |
| `ring` / `ringSeed` | A ring: its own style seed, or `ring: true` for one derived from the body id. |
| `nameKey` / `name` | The body's name (locale key, else fixed name, else the system's name). |
| `visibleDiameter` | The drawn diameter in the flight view, apart from the walkable size. 0 = drawn from the circumference. |

## Joining the galaxy

`GameServerFixedSystems.cs` holds the one mechanism for data and pack systems:

- **Build** — `FixedSystemBuilder.Build` turns a definition into the `StarSystem` (`Fixed` = its definition, each body's
  `Fixed` = its body definition). A pack system is first turned into a definition with one body (`<id>-world`, its
  planet type, orbit radius 940 at a bearing from its id), so its ids, orbit and names are what they always were.
- **Pin** — the first append records the map spot in `WorldMetadata.StampedFeatures`: `fixedsystem|<id>|<x>|<y>` for a
  data system, `packsystem|<id>|<x>|<y>` for a pack system. Every start rebuilds the galaxy from the seed and appends the
  recorded systems again at their spot (`EnsurePackSystemsInGalaxy`, then `EnsureFixedSystemsInGalaxy`, at the end of
  `BuildGalaxy` — after the start body is chosen, so a fixed system is never the spawn world).
- **Growth** — `MaybeGrowGalaxy` appends the new rolled system first and then reveals every data system whose
  `afterGrowth` the galaxy now meets (`RevealFixedSystemsByGrowth`). An older save that already grew that far gets the
  system at its next start. A world without `GalaxyGrowth` never gets a growth system.
- **Story condition** — the 1 Hz pack tick (`TickPackSystemsFor`) also reveals a data system whose `visibleIf` a player
  meets; `SystemHiddenFor` keeps it off the star map and out of jumps for players who do not (unless they are or were in
  it).
- **Admin** — `/sol` (verb `reveal_system`, default id `sol`) or `/system <id>` reveals a data system now, behind the
  cheat rule (`GameServerFixedSystems.Admin.cs`). The reveal is pinned like any other.

A pack and a data file declaring the same id: the pack's system wins (`DataFixedSystems` skips the file).

## The body profile

`BodyProfile.Resolve` (`Shared/World/BodyProfile.cs`) is the one place a body's walkable circumference, gravity factor
and drawn diameter are decided: each value the body's data fixes wins, every other one is today's roll
(`WorldConstants.CircumferenceFor`, `BodyGravity.FactorFor`, `SystemBodyLayout.OrbitDiameter`) — bit for bit for every
rolled body. On the server `GameServerBodySize.cs` reads it for every path (`WorldCircumferenceOf`, `WorldGravityOf`,
`VisibleDiameterOf`); a location id finds a fixed body's values through `_fixedBodies`, filled when its system is
appended. `StarColorOf` takes a fixed star's colour before the roll. The star map sends a body's fixed drawn diameter
as `NetBody.VisibleDiameter` (0 = from `Circumference`, an additive field) and its name in the receiver's language.

## The drawn size

A fixed system may draw a body bigger than it walks (Sol's Jupiter: 16,000 blocks like Earth, drawn three Earths
across). `SystemBodyLayout.VisibleDiameter(fixed, circumference)` is the shared rule; the client reads it through
`BodySize.VisibleDiameterOf` (flight view spheres, keep-out shells and moon ladders in `SpaceView.Scene.cs`),
`BodySize.ApparentCircumferenceOf` (the sky discs and their prominence order in `SkyBodiesView.cs`) and
`BodySize.LooksGiant` (the system chart's larger dot in `SystemMapWidget.cs`). The server's trader descent sizes the
same spheres (`GameServerTraderDescent.cs`). The launch body is drawn `HomeDiameterFactor` bigger but never beyond
`SystemBodyLayout.MaxHomeDiameter` (`HomeDiameter`), so its keep-out shell stays below the flight plane — no rolled body
reaches that cap. The world map bake keeps the walkable circumference.

## Sol (#3970)

`data/systems/sol.json`: the system "Sol" (the same name in every language), a yellow-white star (`0xFFF4E0`), placed
at the `edge` with the galaxy's sixth growth. 21 bodies with ids `sol-<name>` and one planet type each (`sol_<name>`,
`selectable: false`, `spawnWeight: 0` — no galaxy ever rolls them). Sizes follow the plan (Earth 16,000 blocks around,
1 block ≈ 2.5 km); gravity is pressed into 0.35–1.6; the planets keep their real order with compressed spacing (420 to
7,600 system units) and bearings chosen so no two moon families overlap in the flight view; Jupiter, Saturn, Uranus and
Neptune are drawn at about 3, 2.5, 1.6 and 1.6 Earths across. The names are `system.sol.name`, `body.sol_<name>.name`
and `planet.sol_<name>.name`.

The terrain of the Sol types is rolled from their fields for now; map terrain (Earth, Moon, Mars, Venus, Mercury), the
Earth places and its structure list come in later packages.
