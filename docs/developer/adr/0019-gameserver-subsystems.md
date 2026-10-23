# ADR 0019 — GameServer subsystems: top-level classes behind compiler-derived host interfaces

- **Status:** Accepted
- **Date:** 2026-10-22
- **Context source:** the 2026-10 code-health pass ([#2883](https://github.com/marceld23/BlocksBeyondTheStars/issues/2883),
  maintainer decision 2026-10-21); the first extraction, the crews
  ([#2897](https://github.com/marceld23/BlocksBeyondTheStars/issues/2897)); ADR 0001 (one authoritative server) and
  ADR 0005 (the `netstandard2.1` flavour of the server that runs in the browser).

## Context

`GameServer` is one `sealed partial class`: `GameServer.cs` (about 7 600 lines) plus 214 `GameServer*.cs` feature
partials, about 126 000 lines and 765 public `*ForTest` seams. A partial shares every field and every private method with
every other partial, so a feature file is a view onto one object, not a boundary: nothing stops `GameServerX.cs` from
reading `GameServerY.cs`'s state, and nothing tells a reader which server members a feature really needs.

The code-health pass moves the feature slices that own state of their own (crews, bio lab, equipment, markers, journal,
temperature, tutorial, vehicles, …) out of the partial class. The moves must be pure — same behaviour, strings, wire and
save format, tests unchanged — and several of them run as parallel branches, so they must not collide.

Measured before deciding, over the partials planned for extraction: about 90 % of the server members a partial uses
are private; about 50 private static helpers and 4 private nested types are used across files; the members a partial
needs from the rest of the server range from 3 (hull repair) to 31 (speeders); and nine members recur in most of them:
`Send`, `Reject`, `RaiseGameEvent`, `FindSessionByPlayerId`, `_world`, `_uptime`, `_worlds`, `SendPlayerState`,
`SendInventory`. The server already has classes beside the partials that show the target: `WorldManager`, `ServerWorld`,
`PlayerSession`, `MaterialPool` and the air fill, `AirFill` over the `IAirSpace` interface that the tests fake.

## Shapes considered

1. **Nested class with a server reference.** The partial becomes `GameServer.XSystem`, nested in the class, and reaches
   everything outside through `_server.`. No decision per member — the compiler flags every missing `_server.` — and the
   system's own state is encapsulated. But nesting re-indents about 82 % of every moved file, which is below git's
   rename threshold (blame and every later rebase suffer), and the outward surface stays unbounded: `_server.` reaches
   every member, so no test can run the system without a whole `GameServer`.
2. **Top-level class with a host interface listed by hand.** A narrow, explicit surface the compiler enforces, and the
   indentation is kept. But listing the host up front is where the errors were: the first specification had five
   compile blockers, four of them from the hand-written list (a static method calling an instance member, an
   unqualified nested type, a missing `using`, a wrong member count).
3. **Hybrid: top-level layout, host derived by the compiler (chosen).** A top-level class that keeps the original
   indentation, and a host interface that grows from the compiler's own errors, one member per missing name, on top of
   a shared base interface for the members nearly every system needs.

## Decision

1. **One subsystem is one `internal sealed class XSystem`** in `src/BlocksBeyondTheStars.GameServer/XSystem.cs` (flat
   in the project, namespace `BlocksBeyondTheStars.GameServer`), made from the old `GameServerX.cs`: same licence header
   and `using`s, same member order and the same 4-space member indentation, so `git blame -C` still follows the lines.
   The system owns the partial's constants, fields, nested types and method bodies, verbatim.
2. **The compiler derives the host.** Build; for every CS0103/CS0120 on a `GameServer` member, rewrite the use as
   `_host.X` and add one line to `internal interface IXHost : IServerHost` (declared at the top of `XSystem.cs`) plus one
   explicit one-line implementation on `GameServer`. Fields become host properties (`_world` → `_host.World`,
   `_uptime` → `_host.Uptime`). A static method that now needs `_host` loses `static`; a private static helper of
   `GameServer` becomes `internal static` and is called as `GameServer.X`; a private nested type or constant becomes
   `internal` and is written `GameServer.X`. The host is never listed by hand.
3. **`IServerHost`** (`IServerHost.cs`) holds the members several systems need — `Send`, `Reject`, `RaiseGameEvent`,
   `FindSessionByPlayerId`, `World`, `Uptime`, `Worlds`, `SendPlayerState`, `SendInventory`, and since
   [#2931](https://github.com/marceld23/BlocksBeyondTheStars/issues/2931) `Sessions`, `Rules`, `InSpace`,
   `BroadcastToWorld`, `WrapDistSq`, `ShipInteriorContains` — and `GameServer` implements it once, explicitly, in
   `GameServer.Systems.cs`. A member joins it only when at least three systems need it; otherwise it belongs to the
   system's own host interface.
4. **The constructor stores references only:** `(IXHost host[, GameContent content][, IWorldRepository repo][,
   IGameLogger log][, ServerConfig config])` — the host plus objects `GameServer` already holds in its own constructor.
   The worlds, the active world, the world metadata, the galaxy and the ship cursor are not there yet when the
   constructor runs, or change per call, so a system reads them through its host at use time and never caches them.
5. **`GameServer.Systems.cs` is the holder:** one field (`= null!`, not `readonly`, because it is assigned in a method) and
   one construction line per system, in pre-seeded alphabetical slots, built by `ConstructSystems()`, which the
   constructor calls at its end — the only constructor edit of the extractions. (Since the tick schedule, #2904, the
   constructor builds the four schedule tables right after `ConstructSystems()`; those stay its last statements, because a
   schedule entry may capture a system field.) A system that is used once and needs no state may be constructed at its
   use site instead (`HullRepairSystem`, built where `Start` runs the repair), and a slice that is pure computation
   without state may become a static class (`ShipExtensionGeometry`, whose server-side glue stays in the facade
   partial); neither has a field.
6. **The old file name becomes the facade.** `GameServerX.cs` shrinks to `public sealed partial class GameServer : IXHost`
   with a one-line facade (same name, signature and visibility) for every member that sibling partials, the dispatch
   table (`GameServerDispatch.cs`), the tick schedule or the tests call; every `*ForTest` seam unchanged, delegating; and
   the explicit `IXHost` implementations. No caller file is rewritten. An explicit implementation calls the server's own
   member by its simple name — never `((IXHost)this).X`, which would recurse.
7. **Pure moves.** Statement order, conditions, reject and log strings, defaults, RNG seeds and tick `Guard` names stay
   identical. A field initializer that moves into a system now runs when `ConstructSystems` builds it, near the end of the
   constructor, instead of before the constructor body — same values; an RNG keeps its seed, and the change says so.
   Test files are not edited.

## Consequences

- Each subsystem's coupling to the server is a countable list the compiler checks. Narrow hosts (ten members or fewer)
  can later be faked the way `IAirSpace` is and tested without booting a server.
- **Wide hosts, shrink later.** Some systems need about 15 to 25 host members of their own on top of `IServerHost` (as
  extracted: journal 24, NPC work 21, bio lab 20, observer 19, vehicles 19, temperature 16), because they reach into
  the core orchestration or into other partials' state. They are extracted anyway: the wide interface documents the
  coupling, shrinking it (moving state, splitting the system) is follow-up work, and there is no promise of isolated
  unit tests for them until then. A narrow host does not always mean a small coupling: the tutorial's host had 8
  members as extracted only because its extraction is partial — the hint hold and the onboarding migration it works
  with stay in `GameServerTutorial.cs`.
- Extraction makes the dependency cycles visible, not gone: crews → markers → alliances → crews, bio lab ↔ equipment,
  temperature ↔ equipment all still run through the host.
- Each host member costs one interface line and one explicit implementation; the shared `IServerHost` removes the most
  frequent duplicates.
- The merge points of parallel extractions are `GameServer.Systems.cs` (separate slot lines per system) and each
  system's own facade file; `GameServer.cs` and the caller partials stay untouched.
- The facades and the `*ForTest` seams stay on `GameServer` until the tests move to the systems; the number of public
  seams does not change.
- The systems live in the GameServer project, which also compiles as `netstandard2.1` for the in-browser singleplayer,
  so a system may use nothing that target lacks; no new assembly is created.
- The first extraction is `CrewSystem` (#2897): `ICrewHost` adds four members to `IServerHost` (`ScreenPlayerName`,
  `NameOf`, `RefreshAllianceRoster`, `RefreshMarkersFor`), and `GameServerCrews.cs` is a 45-line facade. The recipe for
  the next one is in [DEVELOPER.md § Extracting a GameServer subsystem](../DEVELOPER.md#extracting-a-gameserver-subsystem).
- The 2026-10 pass then extracted, each behind its old file as the facade: `BioLabSystem` and `EquipmentSystem`
  ([#2897](https://github.com/marceld23/BlocksBeyondTheStars/issues/2897)); `AiLinesSystem` (its pure helpers split
  into `AiLineText.cs`, `AiLineBudget.cs` and `AiVariantCache.cs`), `ObserverSystem` and `NpcWorkSystem`
  ([#2898](https://github.com/marceld23/BlocksBeyondTheStars/issues/2898)); `HullRepairSystem`, `FarTerrainSystem` and
  the static `ShipExtensionGeometry` ([#2899](https://github.com/marceld23/BlocksBeyondTheStars/issues/2899));
  `MarkerSystem`, `JournalSystem` and `TemperatureSystem`
  ([#2900](https://github.com/marceld23/BlocksBeyondTheStars/issues/2900)); `ContainerSystem`, `VehicleSystem` (from
  `GameServerSpeeders.cs` and `GameServerBoats.cs`, both now facades) and `TutorialSystem`
  ([#2901](https://github.com/marceld23/BlocksBeyondTheStars/issues/2901)).
- `IServerHost` kept its nine members through the pass. By the three-systems rule of point 3, `Sessions`, `Rules`,
  `InSpace`, `BroadcastToWorld`, `WrapDistSq` and `ShipInteriorContains` then qualified — each was declared by three or
  four host interfaces — and were promoted to `IServerHost`
  ([#2931](https://github.com/marceld23/BlocksBeyondTheStars/issues/2931)): fifteen members, and the 22 duplicate
  declarations and their one-line implementations are gone.
