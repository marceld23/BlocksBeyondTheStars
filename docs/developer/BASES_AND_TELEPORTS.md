# Bases and teleports — several bases, discovery, settlers, yield, teleporter places

**Status:** implemented 2026-10-29 (epic #2992: #2993–#2999). How the second generation of player bases and the teleporter's
destinations work, and why they are built this way. The base *air* (core zone, sealed rooms, the shared air engine) is
[BASE_LIFE_SUPPORT.md](BASE_LIFE_SUPPORT.md); the player-facing rules are in the user manual (*Bases*, *Suit teleporter*,
*Map markers*, *Scanning*).

## 1. Coordinates (#2993)

The client's scene position runs on unbounded as the player laps a round world (`GameBootstrap.PlayerPosition`), so every
readout wraps it back first: `Client.Core/CoordinateText` turns a scene position into the world's own block cell — X into
`[0, circumference)`, Z into the latitude band (`WorldConstants.WrapX/WrapZ`), Y floored — and prints
`X 812 · Y 71 · Z -1904` with invariant digits. Three places use it: the planet map's info box, the opt-in HUD line under the
clock (`ClientSettings.ShowCoordinates`, default off; `HudUi.Coordinates.cs`) and the local chat command `/pos`
(`ChatUi.PositionLine`). All three stay silent aboard, in a station, in space and on a spacewalk — those scenes share their
interior coordinates.

Map markers used to be stored with the waypoint's scene X/Z as sent. `GameServer.HandleMarkerAction` now wraps a set or a
ping into the canonical range and normalises the player's markers on the current world whenever they list or set one.

## 2. Teleporter places (#2994) and the Quantum Teleporter (#2995)

Three teleport systems exist side by side; the numbers live in `Shared/Definitions/TeleportRules` so the server's gates and
the client's cost labels can never drift apart.

| System | Reach | Cost |
|---|---|---|
| Beam pads (`beam_block`) | pad → pad, own and allied, same body | 6 energy, 6 s |
| Suit teleporter | ship, an ally on this body, and **the places of this body** | 10 energy, 30 s |
| Quantum Teleporter | everything above, plus **a free point** (typed X/Z or the map waypoint) | fixed places 10 / 30 s, free point 20 energy / 60 s |

The cooldown is shared by every teleporter jump (`_teleportCooldown`); the pads keep their own.

**`TeleportToPointIntent { Kind, X, Y, Z, TargetId }`** (NetCodec 310) carries one jump. The client only *names* the place;
`GameServerTeleportPoints.TryResolveTeleportPoint` finds it and checks it:

| Kind | Found by | Allowed when | Arrival |
|---|---|---|---|
| `base` | the core's canonical cell on this body | own, an ally's or crew mate's (admins: any) | beside the core (`ArrivalBeside`: 8 neighbour columns, a floor and two clear cells), else on top |
| `beam` | the pad's cell | `CanUseBeam` (owner / ally / admin) | `BeamArrivalSpot` (on the pad, climbing out of a low ceiling) |
| `beacon` | the beacon's cell | own, an ally's or crew mate's (admins: any) | beside it |
| `marker` | the marker id — `MarkerSystem.FindJumpMarker`: own markers on this body or markers an ally shared | on ground the player explored (the planet map's cells, `ExploredByPlayer`); elsewhere only with the Quantum Teleporter, at its free-jump price | `SurfaceSpot` of the wrapped column; refused over lava |
| `coords` / `waypoint` | X/Z, wrapped | Quantum Teleporter in the inventory | `SurfaceSpot`; refused over lava or above the build height (`@srv.tp.unsafe`) |

Pings, VEGA's story flags and wish marks are not saved markers, so `FindJumpMarker` can never return them. A marker is a
point the player picked on the map, so without the explored-ground rule the suit teleporter would be a free jump in disguise.
Of several bases, "the base a thing belongs to" is one rule everywhere (`BaseOwnsCell`/`BaseCountingCell`): another base of
the partner group takes a thing only where its own index would count it. The snap rides the
`RespawnNotice` channel like every server teleport (a plain position update is discarded by the client), and the ground is
streamed ahead (`StreamFootingNow`). A ship guest is refused like for every teleport (`RefusedForGuest`). The intent is not
pause-whitelisted: the picker does not hold the world.

**Why a second tier:** typed coordinates for everyone would make the pad network and the beacons pointless and skip the
exploration. The Quantum Teleporter is late (blueprint after `suit_teleporter` and `quantum_scanner`; the recipe consumes the
suit teleporter), dearer and slower to recharge; the map waypoint is its kid-friendly path (click on the map, jump).

Client: `TeleporterUi` (sections, nearest first, at most 8 rows per kind; `TeleporterUi.Places.cs`) opens for either device
(`TeleportRules.IsTeleporter`).

## 3. Several bases per world (#2996)

`GameRules.BasesPerWorld` (1/2/3/5/8/12, default 5; `--bases-per-world` takes only these steps) replaces the one-base rule in the placement rule
`BaseCoreRule`. The `base_claim` table was always keyed by the core cell, so no schema change was needed. What several bases
needed (`GameServerBasesMulti.cs`):

- **One base per thing.** A bed, post, crate or machine inside the reach of two bases of one *partner group* (the owner and
  their allies) counts for the base whose core is nearest; ties go to the lower core cell, so the answer never depends on load
  order (`BaseOwnsCell`, applied in the base index and the founding-machine count; `NearestBaseCovering` for the bed and post
  messages). A stranger's base never takes anything — otherwise a core planted beside a yard would pull its beds away.
- **Residents per body.** The server runs every resident, so `BaseRules.ResidentCapPerBody` (32) is shared by one player's
  bases on one body (`BaseResidentRoom`: the per-base cap minus the other bases' saved plans, never below the founder).
- **Addressed by the cell.** `SetBaseNameIntent`, `RequestBaseResidentsIntent` and `BaseResidentList` gained the core cell,
  `NetMapBase` too (additive; a request without a cell takes the player's first base on the body, as an older client expects).

Touching bases need nothing else: protection, walls and the air engine already iterate over every base (the air engine fills
per partner group, the union of their boxes).

## 4. Discovery (#2997)

The base list is filtered per recipient on the server (`SendBases` → `SeesBase`): own and allied (or crew) bases always,
anyone else's only once discovered. Discoveries are saved per player (`PlayerState.DiscoveredBases`, `"body|x|y|z"`). Two ways
in: walking within `BaseRules.DiscoveryRadius` (48 blocks, horizontal) of a core — checked every 2 s per occupied world in the
`TickBaseVisits` step, which also runs the friendly guests — and a Quantum (tier 3) planet scan of the body
(`DiscoverAllBasesOn`). `NetBase.Relation` tells the client whose base it is (the map's teal / green / amber). An alliance or
crew change re-sends the lists of both players (piggybacked on `RefreshMarkersFor`). Unchanged on purpose: anyone may still
walk into a base and breathe there, and admins keep `/builds` and `/tp base`.

## 5. The planet scan's settlers (#2998)

`PlanetScanResult.Settlers` (`NetPlanetSettler { Name, Bases, Stations, Relation }`, additive with `SettlersKnown` so an older
server shows no block) is built from the server-wide base registry and the player stations' host bodies
(`GameServerPlanetScanSettlers.cs`), sorted most bases → most stations → the viewer → allies → name. Every scanner tier sends
it — it is a count, not a location. `Client.Core/PlanetSettlerText` words it once for the overview card and the Map tab.

## 6. Base and station yield (#2999)

Every base core and every commissioned player station has a **yield store** (`WorldMetadata.BaseYields`, keys
`base:<body>|x|y|z` and `station:<id>`; `Shared/State/BaseYieldStore`). It is **lazy**: whenever the store is read — the core
screen, a collect, the owner's base list — `BaseRules.Accrue` turns the real time since the last read into items, so nothing
ticks while nobody plays and a singleplayer world earns while the game is closed. Rules:

- Rate `BaseRules.YieldPerHour`: 6 per hour plus 2 per resident (base) or crew member aboard (station), at most 8 helpers,
  scaled by `GameRules.BaseYieldPercent` (Off / Half / Normal / Double; `--base-yield` takes only these steps). Every store
  is brought up to now before the rule changes, so a change never pays backwards.
- Capacity 64; a full store does not accrue (it stops — nothing is lost, nobody has to log in daily). A gap is capped at 30
  days and a clock that went back credits nothing. The first read only starts the clock; founding and commissioning start it.
- What: a base gathers its body's **non-rare** ore veins, weighted by density, from the same `SurveyResources` the planet
  scanner reads (fallback stone); a station gathers orbital salvage (`BaseRules.StationYield`). The picks are deterministic per
  store (`PickWeighted` over the store key and a running index), so a crash never changes what lay ready.
- Collect (`YieldIntent` 311 → `YieldStatus` 312): the owner at the core (within reach) or aboard their station; items go into
  the backpack as far as it holds, the rest stays; the metadata is saved right after a collect. The status read is
  pause-whitelisted (the core screen can be opened while a singleplayer world is held). Mining a core hands its rest to the
  owner's backpack when they are on that world; whatever does not fit (or everything, with the owner away) falls to the ground
  at the core as a drop packet.

**Why lazy and not a catch-up like #2269:** the machine catch-up has to *simulate* work (drills dig real blocks), so it runs on
return under a per-tick budget. A yield store only counts, so it can be computed in O(1) at read time — no tick cost at all,
which matters on a Raspberry Pi and in a browser tab. The economy rule "never mint rare metals from nothing" holds: only
common, planet-native ores and salvage.

## Tests

`TeleportToPointTests` (places, the Quantum gate, lava, cooldown sharing, canonical markers, content), `SeveralBasesTests`
(the rule, touching bases, rename by cell, the nearest-core rule, the per-body cap, discovery, settlers, the yield store and
collect), `NetCodecTests` (tags 310–312).
