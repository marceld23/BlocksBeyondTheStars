# The hidden star map

New worlds (#3997) generate the **whole galaxy at creation** — 24, 36, 48 or 64 rolled systems, chosen as the world
option *Universe size* (default 36) — and **chart it per player**. A rolled system a player has not charted is not on
their star map, is no hyperjump target and no travel destination. Playing pushes the chart outward. This replaced the
*Growing* universe step (#1123), which started small and appended systems at the rim.

Why not a growing galaxy: every special feature is placed over the systems a galaxy **starts** with — wormholes, debris
fields, derelict stations, giant wrecks, civilisation home worlds, guaranteed planet types, KuBa-W, the unique sites,
the red and the blue sun. A galaxy that grows from a small core keeps all of them in the core; a whole galaxy behind a
hidden map spreads them over everything, and needs no change to world generation, no new world generation and no save
version.

## The switch

| Where | What |
|---|---|
| `WorldDescription.HiddenStarMap` | The creation-time flag, baked into the save's metadata. Defaults to `false`, so every save from before keeps its open map. |
| `--hidden-map on\|off` | The CLI switch (`ServerConfigOptions`). `--systems` accepts 1..64 (`WorldDescription.MaxStarSystemCount`). |
| `ServerConfig.ApplyNewWorldDefaults()` | Sets `HiddenStarMap = true` and 36 systems. Called where a config is born from code: `LoadForStartup` with `--no-config` (the bundled singleplayer host), `Load` writing a first `server.json` (dedicated and hosted servers), and `BrowserLocalServer` (browser singleplayer). **Not** in the `World` initializer — hundreds of tests build a plain `new ServerConfig()` and rely on its 12 open systems. |
| Client `WorldCreationOptions` | Always sends `--systems <24/36/48/64>` and `--hidden-map true`; planets and moons per system stay the server's normal ones for every size. |

An existing `server.json` keeps its values (an operator who wants the new map sets `World.HiddenStarMap` and
`World.StarSystemCount` there, or passes the switches). A loaded save always keeps the description in its metadata. A
hidden-map world never grows (`MaybeGrowGalaxy` returns early), whatever its growth flag says.

## The player's chart

`PlayerState.ChartedSystems` (persisted additively next to `KnownSystems`) holds the rolled systems a player charted
without entering them. A rolled system is **visible** to a player (`GameServerStarChart.ChartHiddenFor` is false) when:

- the world has no hidden map, or the system is not a rolled one (`sys…`) — story, pack and data systems keep their own
  rules in `SystemHiddenFor`;
- the player is an observer or in admin mode;
- the system carries a finished relay (`_relaySystems`) — visible to everyone;
- it is the player's current system, or the player — or an **ally** — entered (`KnownSystems`) or charted it.

`SystemHiddenFor` (`GameServerStoryPacks.Systems.cs`) adds this as its last condition, so the star map (`SendStarMap`),
the hyperjump (`HyperjumpToSystem`) and travel (`AllowNormalTravel`) all respect it. An admin's travel command
(`adminBypass`) checks only the story half (`StorySystemHiddenFor`).

## How systems get charted

| Trigger | Hook | Rule |
|---|---|---|
| First entry of a system (landing, respawn, hyperjump, wormhole) | `ChartOnArrival` from `MarkArrivedOnBody` / `MarkSystemKnown` | `StarChartRules.NeighbourCount`: 1–4 nearest systems the player has not charted themselves, fixed per world and system (FNV + 64-bit mix of `seed` and the system id; 1 = 10 %, 2 = 35 %, 3 = 35 %, 4 = 20 %). The start system charts at least 2. |
| Any jump into a system with a `radar_array` fitted | `ChartOnArrival` (`jumped`) | 2 more (`StarChartRules.RadarBonus`), also when the system is known. |
| Wormhole scan | `ScanWormhole` | The far end's system. Flying through enters it. |
| A named place | `ChartNamedPlace` | The unique-site leads and markers (`ResolveUniqueSite`, `SpeakUniqueSiteWay` — also the guide-tower marker), the NPC site hint (`TryEmitUniqueSiteHint`), a taken mission's travel target (`ChartMissionTargets`; the "unlinked system" survey charts the nearest unlinked system when none is visible). `CanReachBody` treats every system as reachable with a jump generator, so a place must be charted **before** it is named. |
| Never stuck | `RescueIfStuck` (join + every arrival) | When nothing visible is left to enter but systems are still hidden: the 2 nearest. |

"Nearest" is `StarChartRules.Nearest`: star-map distance, ties by ordinal id. Charting counts only the player's
**own** chart, so a player keeps a frontier of their own when an alliance ends.

The player hears `srv.chart.found` (`{count}`, worded count-neutral); the spawn system's first charting stays silent.
The star map goes out again to the player and their online allies.

## Allies

Allies are manual alliance partners and crew mates (`StarChartAllyIds`, the same set `AreAllied` answers for). While
allied, each sees the other's entered and charted systems; `SendStarMap` reports the allies' entered systems in
`KnownSystemIds` too, so their names and worlds show (`NameablePlaceName` uses the same set). Landing rights,
achievements and story progress are not shared. An offline ally's chart is read from their save once and cached
(`_offlineCharts`); the entry is dropped when that player joins. Every alliance or crew change ends in
`RefreshAllianceRoster`, which re-sends the star map. The star map's player list leaves out players who are in a system
the receiver cannot see.

## Sol

`data/systems/sol.json` carries `"appear": { "afterGrowth": 6, "afterSystems": 8 }` (`FixedSystemAppearance.AfterSystems`,
docs/developer/FIXED_SYSTEMS.md). On a hidden map Sol joins the galaxy the first time a player has entered 8 rolled
systems (`RevealFixedSystemsBySystems`) and is visible to every player who did, or whose ally did
(`FixedSystemHiddenBySystems`); each is told once (`srv.chart.far_system`). Old growing worlds keep the growth rule;
fixed worlds get neither.

## Tests

`StarChartRulesTests` (counts, weights, home minimum, nearest order) and `HiddenStarMapTests` (start view, first entry,
travel refusal and named places, the whole galaxy charted by playing for every size, radar, allies online/offline/after
the alliance, Sol, an open world unchanged, no growth, persistence, the startup defaults, the biggest galaxy's names and
body count).
