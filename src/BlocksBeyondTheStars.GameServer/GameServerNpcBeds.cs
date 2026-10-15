// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Beds and seats that belong to someone (#2655, epic #2649) — one assignment for every NPC with a daily routine.
/// <list type="bullet">
/// <item><b>Keep what you have.</b> A bed and an evening seat stay with their NPC as long as the block stands. A bed
/// built somewhere else never moves anybody.</item>
/// <item><b>Only free furniture, only to those without.</b> A bed or seat is free when no NPC of the world holds it
/// (<c>ServerNpc.Bed</c> / <c>Seat</c> are the claims). The bedless pick in a fair queue: whoever has been without a bed
/// longest (<c>ServerNpc.BedlessSince</c>) goes first. The best bed (<see cref="BetterBed"/>): one in the same room as
/// the work spot (<see cref="AirRoomIdAt"/>), otherwise the nearest to the work spot — height counting more
/// (dx² + dz² + 4·dy²) — with the coordinates as the tie-break.</item>
/// <item><b>Event-driven.</b> A <c>ServerWorld.BlockSet</c> subscriber (<see cref="OnFurnitureCellSet"/>) releases a claim
/// the moment its bed or seat is gone — the NPC is bedless from then on and looks again — and bumps the world's
/// furniture generation whenever a bed or a seat appears. An NPC without a bed or a seat whose last look is older than
/// the generation looks again in the next routine pass, at most <see cref="FurnitureScansPerTick"/> per tick
/// (<see cref="RunFurnitureSearches"/>). This replaced the one-shot "looked once" flag: a bed built later is taken within
/// seconds, the sleeper of a broken bed finds another.</item>
/// <item><b>Who looks where.</b> Base residents get theirs in the base scan (<see cref="UpdateBaseResidents"/> — the
/// same rules, plus slots and persistence). Villagers and lodge residents keep what they spawned with and look within
/// <see cref="FurnitureSearchReach"/> of their resting spot, never inside a base's reach (that furniture is the base's).
/// A kit-station resident looks within <see cref="CabinFurnitureReach"/> of its cabin spot. The crew of a generated or
/// player-built station takes any bed of the station that stands in a room with air (#2657,
/// <see cref="StationBedCandidates"/>) — post keepers first, as they spawn first.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A world's furniture bookkeeping (#2655), by location id — NPCs live per world, their ids restart per world.</summary>
    private sealed class FurnitureLedger
    {
        /// <summary>Bumped whenever a bed or a seat appears on the world. Starts at 1, so an NPC that never looked (0)
        /// looks once.</summary>
        public long Generation = 1;

        /// <summary>Bumped only when a bed appears — the station's bed list is re-read on this one.</summary>
        public long BedGeneration = 1;

        /// <summary>Canonical bed-head or seat cell → the NPC holding it: the BlockSet subscriber's lookup. The truth is the
        /// NPCs' own <c>Bed</c>/<c>Seat</c>; the map is rebuilt from them every routine pass (NPCs leave through many paths)
        /// and kept current by <see cref="SetNpcBed"/> / <see cref="SetNpcSeat"/> in between. A stale entry only ever
        /// releases an NPC that is gone already.</summary>
        public readonly Dictionary<Vector3i, ServerNpc> Claims = new();

        /// <summary>The station's bed heads (#2657), coordinates-sorted, read at <see cref="StationBedsGeneration"/>.</summary>
        public readonly List<Vector3i> StationBeds = new();
        public long StationBedsGeneration;
        public string StationBedsFor = string.Empty;
    }

    private readonly Dictionary<string, FurnitureLedger> _furniture = new(System.StringComparer.Ordinal);

    /// <summary>The NPCs that look around this tick, longest-bedless first (reused per tick).</summary>
    private readonly List<ServerNpc> _furnitureQueue = new();

    /// <summary>Test seam: a stand-in for <see cref="AirRoomIdAt"/> (null = ask the air engine).</summary>
    private System.Func<Vector3i, int?>? _airRoomForTest;

    private FurnitureLedger FurnitureOf(string locationId)
    {
        if (!_furniture.TryGetValue(locationId, out var ledger))
        {
            _furniture[locationId] = ledger = new FurnitureLedger();
        }

        return ledger;
    }

    /// <summary>The sealed room holding an air cell, or null — the air engine's answer (or the test's stand-in).</summary>
    private int? RoomIdAt(Vector3i cell) => _airRoomForTest is { } rooms ? rooms(cell) : AirRoomIdAt(cell);

    /// <summary>Marks an NPC as having looked around at the world's current furniture generation: it keeps what it has and
    /// looks again only when a bed or seat appears (or its own goes). Every spawner that hands out furniture calls this.</summary>
    private void MarkFurnitureLooked(ServerNpc npc) => npc.FurnitureGen = FurnitureOf(_world.LocationId).Generation;

    /// <summary>Gives an NPC a bed — or none — and keeps the claims and the fair queue in step (losing a bed starts the
    /// NPC's wait, <c>BedlessSince</c>).</summary>
    private void SetNpcBed(ServerNpc npc, Vector3i? bed)
    {
        if (System.Nullable.Equals(npc.Bed, bed))
        {
            return;
        }

        var claims = FurnitureOf(_world.LocationId).Claims;
        int circ = _world.Circumference;
        if (npc.Bed is { } old && claims.TryGetValue(WorldConstants.CanonicalBlock(old, circ), out var holder) && holder == npc)
        {
            claims.Remove(WorldConstants.CanonicalBlock(old, circ));
        }

        npc.Bed = bed;
        if (bed is { } now)
        {
            claims[WorldConstants.CanonicalBlock(now, circ)] = npc;
        }
        else
        {
            npc.BedlessSince = _uptime;
        }
    }

    /// <summary>Gives an NPC an evening seat — or none — and keeps the claims in step.</summary>
    private void SetNpcSeat(ServerNpc npc, Vector3i? seat)
    {
        if (System.Nullable.Equals(npc.Seat, seat))
        {
            return;
        }

        var claims = FurnitureOf(_world.LocationId).Claims;
        int circ = _world.Circumference;
        if (npc.Seat is { } old && claims.TryGetValue(WorldConstants.CanonicalBlock(old, circ), out var holder) && holder == npc)
        {
            claims.Remove(WorldConstants.CanonicalBlock(old, circ));
        }

        npc.Seat = seat;
        if (seat is { } now)
        {
            claims[WorldConstants.CanonicalBlock(now, circ)] = npc;
        }
    }

    /// <summary>Rebuilds the active world's claims from its NPCs (once per routine pass) — drops what left with an NPC.</summary>
    private void RefreshFurnitureClaims()
    {
        var claims = FurnitureOf(_world.LocationId).Claims;
        claims.Clear();
        int circ = _world.Circumference;
        foreach (var n in _npcs)
        {
            if (n.Bed is { } bed)
            {
                claims[WorldConstants.CanonicalBlock(bed, circ)] = n;
            }

            if (n.Seat is { } seat)
            {
                claims[WorldConstants.CanonicalBlock(seat, circ)] = n;
            }
        }
    }

    /// <summary>
    /// The BlockSet subscriber (#2655, registered next to the other per-world caches in <c>LoadWorld</c>): a claimed bed
    /// or seat that is no longer there is released — its NPC is bedless (seatless) from now and looks again — and a bed or
    /// a seat that appears bumps the world's furniture generation. Cheap: one map lookup and one block read per set.
    /// </summary>
    private void OnFurnitureCellSet(LoadedWorld world, Vector3i cell)
    {
        var sw = world.World;
        EnsureBaseIndexIds();
        var id = sw.GetBlockIfLoaded(cell);
        bool bed = !id.IsAir && _ixBed != 0 && id.Value == _ixBed;
        int form = id.IsAir ? 0 : ShapeCode.ShapeOf(sw.GetShapeIfLoaded(cell));
        bool bedHead = bed && form != (int)BlockShape.BedFoot;
        bool seat = !id.IsAir && !bed && FurnitureShapes.IsSeat(form);
        if (!_furniture.TryGetValue(world.LocationId, out var ledger))
        {
            if (!bedHead && !seat)
            {
                return;
            }

            ledger = FurnitureOf(world.LocationId);
        }

        if (ledger.Claims.TryGetValue(cell, out var holder))
        {
            int circ = sw.Circumference;
            bool holdsBed = holder.Bed is { } b && WorldConstants.CanonicalBlock(b, circ) == cell;
            bool holdsSeat = holder.Seat is { } s && WorldConstants.CanonicalBlock(s, circ) == cell;
            if (holdsBed && !bedHead)
            {
                ledger.Claims.Remove(cell);
                ReleaseNpcBed(holder);
            }
            else if (holdsSeat && !seat)
            {
                ledger.Claims.Remove(cell);
                ReleaseNpcSeat(holder);
            }
            else if (!holdsBed && !holdsSeat)
            {
                ledger.Claims.Remove(cell); // a stale entry — its NPC holds something else by now
            }
        }

        if (bedHead || seat)
        {
            ledger.Generation++;
            if (bedHead)
            {
                ledger.BedGeneration++;
            }
        }
    }

    /// <summary>The bed is gone: bedless from now (the queue), look again at the next pass, and out of the broken bed.</summary>
    private void ReleaseNpcBed(ServerNpc npc)
    {
        npc.Bed = null;
        npc.BedlessSince = _uptime;
        npc.FurnitureGen = 0;
        if (npc.Phase == NpcPhase.Night || npc.Pose == 2)
        {
            npc.PhaseCheckedAt = double.NegativeInfinity; // re-think the night: another bed, or rest standing
        }
    }

    /// <summary>The seat is gone: look again at the next pass, and up from the broken seat.</summary>
    private void ReleaseNpcSeat(ServerNpc npc)
    {
        npc.Seat = null;
        npc.SeatWait = false;
        npc.FurnitureGen = 0;
        if (npc.Phase == NpcPhase.Evening || npc.Pose == 1)
        {
            npc.PhaseCheckedAt = double.NegativeInfinity;
        }
    }

    /// <summary>
    /// The routine's look-around pass (#2655): every NPC in reach that lacks a bed or a seat and whose last look predates
    /// the world's furniture generation, longest-bedless first (then by id — spawn order, so a station's post keepers come
    /// before its civilians), at most <see cref="FurnitureScansPerTick"/> per tick; the rest wait for the next tick. An NPC
    /// that found what its current phase needs re-thinks the phase at once.
    /// </summary>
    private void RunFurnitureSearches(double aoiSq)
    {
        var ledger = FurnitureOf(_world.LocationId);
        _furnitureQueue.Clear();
        foreach (var npc in _npcs)
        {
            if (!npc.RoutineEnabled || npc.BaseId > 0 || npc.FurnitureGen >= ledger.Generation
                || (npc.Bed is not null && npc.Seat is not null) || !InRoutineReach(npc, aoiSq))
            {
                continue;
            }

            _furnitureQueue.Add(npc);
        }

        if (_furnitureQueue.Count == 0)
        {
            return;
        }

        _furnitureQueue.Sort((a, b) =>
        {
            double qa = a.Bed is null ? a.BedlessSince : double.MaxValue;
            double qb = b.Bed is null ? b.BedlessSince : double.MaxValue;
            int c = qa.CompareTo(qb);
            return c != 0 ? c : a.Id.CompareTo(b.Id);
        });

        int looks = System.Math.Min(FurnitureScansPerTick, _furnitureQueue.Count);
        for (int i = 0; i < looks; i++)
        {
            var npc = _furnitureQueue[i];
            var bedBefore = npc.Bed;
            var seatBefore = npc.Seat;
            SearchNpcFurniture(npc);
            bool newBed = !System.Nullable.Equals(bedBefore, npc.Bed);
            bool newSeat = !System.Nullable.Equals(seatBefore, npc.Seat);
            if ((newBed && npc.Phase == NpcPhase.Night) || (newSeat && npc.Phase == NpcPhase.Evening))
            {
                npc.PhaseCheckedAt = double.NegativeInfinity; // a bed or seat for right now: go there
            }
        }
    }

    /// <summary>Whether an NPC is within a routine target's area of interest (the routine only runs where someone is).</summary>
    private bool InRoutineReach(ServerNpc npc, double aoiSq)
    {
        foreach (var t in _routineTargets)
        {
            if (WrapDistSq(t.State.Position, npc.Pos) <= aoiSq)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// One NPC looks around (#2655): without a bed it takes the best free bed it may use, without a seat the nearest free
    /// seat around its resting spot; what it has it keeps. Base residents are the base scan's. Returns whether anything
    /// changed.
    /// </summary>
    private bool SearchNpcFurniture(ServerNpc npc)
    {
        npc.FurnitureGen = FurnitureOf(_world.LocationId).Generation;
        if (npc.BaseId > 0 || (npc.Bed is not null && npc.Seat is not null))
        {
            return false;
        }

        int circ = _world.Circumference;
        var takenBeds = new HashSet<Vector3i>();
        var takenSeats = new HashSet<Vector3i>();
        foreach (var n in _npcs)
        {
            if (n == npc)
            {
                continue;
            }

            if (n.Bed is { } b)
            {
                takenBeds.Add(WorldConstants.CanonicalBlock(b, circ));
            }

            if (n.Seat is { } s)
            {
                takenSeats.Add(WorldConstants.CanonicalBlock(s, circ));
            }
        }

        var station = ActiveStationInterior();
        bool stationCrewBeds = station is { Kit: null } && !npc.CabinBed; // #2657: the whole station, not the reach
        var anchor = (npc.CabinBed || !npc.HasWork ? npc.Rest : npc.Work).ToBlock();
        int? anchorRoom = RoomIdAt(anchor);
        Vector3i? bestBed = null, bestSeat = null;
        long bestSeatD = long.MaxValue;

        if (npc.Bed is null && stationCrewBeds)
        {
            foreach (var c in StationBedCandidates(station!))
            {
                if (!takenBeds.Contains(WorldConstants.CanonicalBlock(c, circ)) && IsBedHeadAt(c) && StationCellHasAir(station!, Above(c))
                    && (bestBed is not { } cur || BetterBed(c, cur, anchor, anchorRoom)))
                {
                    bestBed = c;
                }
            }
        }

        bool scanBeds = npc.Bed is null && !stationCrewBeds;
        bool scanSeats = npc.Seat is null;
        if (scanBeds || scanSeats)
        {
            int reach = npc.CabinBed ? CabinFurnitureReach : FurnitureSearchReach;
            var home = npc.Rest.ToBlock();
            var bases = station is null ? _bases.Where(b => b.Planet == _world.LocationId).ToList() : null;
            ushort bedId = _content.GetBlock(BedBlock)?.NumericId.Value ?? 0;
            for (int dx = -reach; dx <= reach; dx++)
                for (int dz = -reach; dz <= reach; dz++)
                    for (int dy = -2; dy <= 3; dy++)
                    {
                        var c = new Vector3i(home.X + dx, home.Y + dy, home.Z + dz);
                        var id = _world.GetBlockIfLoaded(c);
                        if (id.IsAir)
                        {
                            continue;
                        }

                        int form = ShapeCode.ShapeOf(_world.GetShapeIfLoaded(c));
                        bool isBed = scanBeds && bedId != 0 && id.Value == bedId && form != (int)BlockShape.BedFoot;
                        bool isSeat = scanSeats && !(bedId != 0 && id.Value == bedId) && FurnitureShapes.IsSeat(form);
                        if (!isBed && !isSeat)
                        {
                            continue;
                        }

                        var canonical = WorldConstants.CanonicalBlock(c, circ);
                        if ((isBed ? takenBeds : takenSeats).Contains(canonical))
                        {
                            continue;
                        }

                        // On a station only what stands in its air (#1775); on a planet never a base's furniture — that is
                        // the base scan's to hand out to its residents.
                        if ((station != null && !StationCellHasAir(station, Above(c)))
                            || (bases is { Count: > 0 } && bases.Any(b => WithinBaseWallReach(b, canonical))))
                        {
                            continue;
                        }

                        if (isBed)
                        {
                            if (bestBed is not { } cur || BetterBed(c, cur, anchor, anchorRoom))
                            {
                                bestBed = c;
                            }
                        }
                        else
                        {
                            long d = (long)dx * dx + (long)dz * dz + 4L * dy * dy;
                            if (d < bestSeatD || (d == bestSeatD && bestSeat is { } s && CoordinateOrder(c, s) < 0))
                            {
                                bestSeatD = d;
                                bestSeat = c;
                            }
                        }
                    }
        }

        bool changed = false;
        if (npc.Bed is null && bestBed is { } bed)
        {
            SetNpcBed(npc, bed);
            changed = true;
        }

        if (npc.Seat is null && bestSeat is { } seat)
        {
            SetNpcSeat(npc, seat);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// The shared bed ranking (#2655): whether <paramref name="candidate"/> beats <paramref name="current"/> for someone
    /// working at <paramref name="anchor"/> — first a bed in the same room as the work spot (when the air engine names
    /// rooms, <paramref name="anchorRoom"/>; a bed's room is the air above it), then the nearest (dx² + dz² + 4·dy², across
    /// the world's seam), then the coordinates (Y, Z, X).
    /// </summary>
    private bool BetterBed(Vector3i candidate, Vector3i current, Vector3i anchor, int? anchorRoom)
    {
        if (anchorRoom is { } room)
        {
            bool candidateIn = RoomIdAt(Above(candidate)) == room;
            bool currentIn = RoomIdAt(Above(current)) == room;
            if (candidateIn != currentIn)
            {
                return candidateIn;
            }
        }

        long dc = BedDistance(candidate, anchor), du = BedDistance(current, anchor);
        return dc != du ? dc < du : CoordinateOrder(candidate, current) < 0;
    }

    /// <summary>The best bed of <paramref name="candidates"/> for a work spot at <paramref name="anchor"/>
    /// (<see cref="BetterBed"/>); null when there is none. <paramref name="anchorAir"/> is the air cell whose room counts.</summary>
    private Vector3i? BestBedFor(Vector3i anchor, Vector3i anchorAir, IEnumerable<Vector3i> candidates)
    {
        int? room = RoomIdAt(anchorAir);
        Vector3i? best = null;
        foreach (var c in candidates)
        {
            if (best is not { } cur || BetterBed(c, cur, anchor, room))
            {
                best = c;
            }
        }

        return best;
    }

    private long BedDistance(Vector3i a, Vector3i b)
    {
        int circ = _world.Circumference;
        long dx = WorldConstants.WrapDeltaX(a.X - b.X, circ);
        long dz = WorldConstants.WrapDeltaZ(a.Z - b.Z, circ);
        long dy = a.Y - b.Y;
        return dx * dx + dz * dz + 4 * dy * dy;
    }

    /// <summary>The deterministic tie-break: Y, then Z, then X (the base index's order).</summary>
    private static int CoordinateOrder(Vector3i a, Vector3i b)
        => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X);

    private static Vector3i Above(Vector3i c) => new(c.X, c.Y + 1, c.Z);

    private bool IsBedHeadAt(Vector3i c)
    {
        var id = _world.GetBlockIfLoaded(c);
        EnsureBaseIndexIds();
        return !id.IsAir && id.Value == _ixBed && ShapeCode.ShapeOf(_world.GetShapeIfLoaded(c)) != (int)BlockShape.BedFoot;
    }

    /// <summary>The boarded station whose interior world is active (any kind), once stamped; null on a planet.</summary>
    private BoardableStation? ActiveStationInterior()
        => _world.LocationId.StartsWith(StationLocationIdPrefix, System.StringComparison.Ordinal)
            && _stationsById.TryGetValue(_world.LocationId.Substring(StationLocationIdPrefix.Length), out var station)
            && station.Stamped
            ? station
            : null;

    /// <summary>Whether an air cell of a station holds air: always on an NPC station (blanket air), only in a sealed pocket on
    /// a player-built one or a derelict.</summary>
    private bool StationCellHasAir(BoardableStation station, Vector3i cell)
        => !UsesSealedPocketAir(station.Id) || InSealedStationPocket(station, cell);

    /// <summary>
    /// Every bed head of a station (#2657, Marcel 2026-10: "a crew quarters wing at the other end of the station"): the
    /// stamped box is scanned once per bed generation of the world and the cells kept sorted by coordinates; the air and the
    /// claims are judged when someone picks. A player station's box follows its build (<c>BoundsMin</c>/<c>BoundsMax</c>),
    /// a generated one's is its structure at its origin.
    /// </summary>
    private List<Vector3i> StationBedCandidates(BoardableStation station)
    {
        var ledger = FurnitureOf(_world.LocationId);
        if (ledger.StationBedsGeneration == ledger.BedGeneration && ledger.StationBedsFor == station.Id)
        {
            return ledger.StationBeds;
        }

        ledger.StationBedsGeneration = ledger.BedGeneration;
        ledger.StationBedsFor = station.Id;
        ledger.StationBeds.Clear();
        EnsureBaseIndexIds();
        if (_ixBed == 0)
        {
            return ledger.StationBeds;
        }

        var (min, max) = StationBedBox(station);
        for (int x = min.X; x <= max.X; x++)
            for (int z = min.Z; z <= max.Z; z++)
                for (int y = min.Y; y <= max.Y; y++)
                {
                    var c = new Vector3i(x, y, z);
                    var id = _world.GetBlockIfLoaded(c);
                    if (!id.IsAir && id.Value == _ixBed && ShapeCode.ShapeOf(_world.GetShapeIfLoaded(c)) != (int)BlockShape.BedFoot)
                    {
                        ledger.StationBeds.Add(c);
                    }
                }

        ledger.StationBeds.Sort(CoordinateOrder);
        return ledger.StationBeds;
    }

    private static (Vector3i Min, Vector3i Max) StationBedBox(BoardableStation station)
    {
        if (!IsPlayerStationId(station.Id) && station.Structure is { } s)
        {
            var o = station.Origin;
            return (o, new Vector3i(o.X + s.Width - 1, o.Y + s.Height - 1, o.Z + s.Length - 1));
        }

        return (station.BoundsMin, station.BoundsMax);
    }

    /// <summary>Test seam (#2657): stands in for the air engine's room ids (null = ask <see cref="AirRoomIdAt"/>).</summary>
    public void SetAirRoomIdForTest(System.Func<Vector3i, int?>? rooms) => _airRoomForTest = rooms;

    /// <summary>Test seam (#2655): every NPC of the active world with its bed and seat.</summary>
    public IReadOnlyList<(int Id, string Role, Vector3i? Bed, Vector3i? Seat)> NpcFurnitureForTest
        => _npcs.Select(n => (n.Id, n.Role, n.Bed, n.Seat)).ToList();
}
