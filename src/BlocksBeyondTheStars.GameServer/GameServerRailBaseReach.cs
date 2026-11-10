// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3457 — which places a rail stop belongs to. Lyxette's spaceport is about 80×80 blocks and its station stands "top
/// left", next to the village the base was built around: 48 blocks from the foundation stone did not reach it, and the
/// village took the stop first anyway. Now:
/// <list type="bullet">
/// <item><b>The town's place</b> (<see cref="RailTownAt"/>): an intercity station's stop belongs to its town; any other stop
/// to the settlement whose box holds it or lies within <see cref="RailStopSettlementMargin"/>.</item>
/// <item><b>The base's place</b> (<see cref="RailBaseAt"/>): the nearest player base on this body whose core lies within
/// <see cref="RailStopBaseReach"/> blocks of the stop, or one of whose power relays (<see cref="BaseRelayCells"/>, the
/// sentries' chain, #1714) lies within <see cref="RailStopRelayReach"/> — both horizontally, ±<see cref="RailStopBaseReachY"/>
/// up or down.</item>
/// <item><b>Both</b> (<see cref="RailPlacesAt"/>): a stop beside a village a base surrounds is the village's stop (its
/// villagers keep riding) <i>and</i> the base's (guests come by train, residents ride from it). A trip or a guest route
/// never pairs a stop with itself.</item>
/// </list>
/// The relay chain is read only while the base's owner is on its body (a train guest needs them home anyway) and never
/// loads a chunk: base builds are anchored (#3211), and a walk that met an unloaded chunk is tried again after
/// <see cref="RailRelayRetrySeconds"/>. It is dropped by the same block hook as the sentries' chain
/// (<see cref="OnSentryChainCellSet"/>).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A stop belongs to a player base whose core lies within this many blocks of it (horizontally) …</summary>
    internal const float RailStopBaseReach = 96f;

    /// <summary>… or whose chained power relay lies within this many blocks of it (horizontally) …</summary>
    internal const float RailStopRelayReach = 24f;

    /// <summary>… and this many up or down.</summary>
    private const float RailStopBaseReachY = 24f;

    /// <summary>A relay walk that met an unloaded chunk is walked again after this long.</summary>
    private const double RailRelayRetrySeconds = 30.0;

    /// <summary>One base's power relays as the rail sees them: the cells, their canonical cells (what the block hook
    /// watches), whether the walk saw every chunk it needed, and when it ran.</summary>
    private sealed class RailRelayChain
    {
        public List<Vector3i> Relays = new();
        public HashSet<Vector3i> Cells = new();
        public bool Complete;
        public double WalkedAt;
    }

    /// <summary>Per base id (unique across bodies): the relay chain the rail last walked.</summary>
    private readonly Dictionary<int, RailRelayChain> _railRelayChains = new();

    /// <summary>The places a stop belongs to: none, the town's, the base's, or both (town first).</summary>
    private List<RailStopPlace> RailPlacesAt(Vector3i stop)
    {
        var places = new List<RailStopPlace>(2);
        if (RailTownAt(stop) is { } town)
        {
            places.Add(new RailStopPlace { Stop = stop, Name = town.Name, Settlement = town });
        }

        if (RailBaseAt(stop) is { } b)
        {
            places.Add(new RailStopPlace { Stop = stop, Name = b.Name, Base = b });
        }

        return places;
    }

    /// <summary>The town a stop belongs to: an intercity station's town, else the nearest settlement whose box holds the
    /// stop or lies within <see cref="RailStopSettlementMargin"/> of it. Null otherwise.</summary>
    private SettlementInstance? RailTownAt(Vector3i stop)
    {
        var ic = Intercity;
        if (ic.Active && ic.Stations.FirstOrDefault(s => s.Stop == stop) is { } station
            && _settlements.FirstOrDefault(s => !s.Ruined && s.Name == station.SettlementName) is { } town)
        {
            return town;
        }

        var at = new Vector3f(stop.X + 0.5f, stop.Y, stop.Z + 0.5f);
        SettlementInstance? best = null;
        double bestSq = double.MaxValue;
        foreach (var s in _settlements)
        {
            if (!s.Ruined && s.Name.Length > 0 && StopNearBox(stop, s) && WrapDistSq(at, SettlementCenter(s)) is var d && d < bestSq)
            {
                bestSq = d;
                best = s;
            }
        }

        return best;
    }

    /// <summary>The player base on this body that reaches a stop — by its core or a chained relay, the nearest wins — or null.</summary>
    private ServerBase? RailBaseAt(Vector3i stop)
    {
        ServerBase? best = null;
        double bestSq = double.MaxValue;
        foreach (var b in _bases)
        {
            if (b.Planet == _world.LocationId && RailBaseReachSq(b, stop) is var d && d < bestSq)
            {
                bestSq = d;
                best = b;
            }
        }

        return best;
    }

    /// <summary>How near a base reaches a stop: the squared horizontal distance from its core (within
    /// <see cref="RailStopBaseReach"/>) or from its nearest chained relay (within <see cref="RailStopRelayReach"/>),
    /// whichever is smaller; <see cref="double.MaxValue"/> when neither reaches.</summary>
    private double RailBaseReachSq(ServerBase b, Vector3i stop)
    {
        double best = double.MaxValue;
        var at = new Vector3f(stop.X + 0.5f, stop.Y, stop.Z + 0.5f);
        if (Math.Abs(b.Cell.Y - stop.Y) <= RailStopBaseReachY)
        {
            var core = BaseCorePos(b);
            double d = WrapDistSq(new Vector3f(at.X, core.Y, at.Z), core);
            if (d <= (double)RailStopBaseReach * RailStopBaseReach)
            {
                best = d;
            }
        }

        foreach (var r in BaseRelayCells(b))
        {
            if (Math.Abs(r.Y - stop.Y) > RailStopBaseReachY)
            {
                continue;
            }

            double d = WrapDistSq(at, new Vector3f(r.X + 0.5f, at.Y, r.Z + 0.5f));
            if (d <= (double)RailStopRelayReach * RailStopRelayReach && d < best)
            {
                best = d;
            }
        }

        return best;
    }

    /// <summary>
    /// The power relays of a base's chain (the sentries' rule: from the core, every relay inside the zone cube of the core
    /// or of a chained relay, at most <see cref="MaxPowerRelaysPerBase"/>) — empty while the owner is not on its body. Cached
    /// until a relay comes or goes (<see cref="OnSentryChainCellSet"/>); a walk that met an unloaded chunk is walked again
    /// after <see cref="RailRelayRetrySeconds"/>. Never loads a chunk.
    /// </summary>
    private IReadOnlyList<Vector3i> BaseRelayCells(ServerBase b)
    {
        if (b.Planet != _world.LocationId || !OwnerIsHome(b))
        {
            _railRelayChains.Remove(b.Id);
            return Array.Empty<Vector3i>();
        }

        if (_railRelayChains.TryGetValue(b.Id, out var chain) && (chain.Complete || _uptime - chain.WalkedAt < RailRelayRetrySeconds))
        {
            return chain.Relays;
        }

        var walk = WalkPowerChain(b, loadedOnly: true);
        chain = new RailRelayChain { Relays = walk.Relays.ToList(), Complete = walk.Complete, WalkedAt = _uptime };
        foreach (var cell in chain.Relays)
        {
            chain.Cells.Add(WorldConstants.CanonicalBlock(cell, _world.Circumference));
        }

        _railRelayChains[b.Id] = chain;
        _railRelayWalksForTest++;
        return chain.Relays;
    }

    /// <summary>The block hook's part for the rail (<see cref="OnSentryChainCellSet"/>): a relay that went up anywhere drops
    /// every cached chain (it may join any of them); a cached relay's cell that changed drops that base's.</summary>
    private void DropRailRelayChains(bool relayPlaced, Vector3i cell)
    {
        if (_railRelayChains.Count == 0)
        {
            return;
        }

        if (relayPlaced)
        {
            _railRelayChains.Clear();
            return;
        }

        // Every block write passes here (fluids, fire, digging): no allocation unless a cached relay's cell changed.
        List<int>? stale = null;
        foreach (var (baseId, chain) in _railRelayChains)
        {
            if (chain.Cells.Contains(cell))
            {
                (stale ??= new List<int>()).Add(baseId);
            }
        }

        if (stale != null)
        {
            foreach (int baseId in stale)
            {
                _railRelayChains.Remove(baseId);
            }
        }
    }

    /// <summary>The stops on any line that belong to a base (a line may have more than one).</summary>
    private IEnumerable<Vector3i> BaseRailStops(ServerBase b)
        => Rails.Lines.Values.SelectMany(l => l.Stops).Select(s => s.Cell).Where(c => RailBaseAt(c)?.Id == b.Id);

    /// <summary>#3457: whether the owner counts as home for a train guest — within the base's stop reach of the core (plus
    /// the guests' owner reach), within that owner reach of a chained relay, or of one of the base's stops (they may wait on
    /// the platform).</summary>
    private bool OwnerNearBaseForTrainGuest(ServerBase b, Vector3f pos)
    {
        float coreReach = RailStopBaseReach + BaseGuestOwnerReach;
        if (WrapDistSq(pos, BaseCorePos(b)) <= coreReach * coreReach)
        {
            return true;
        }

        const float near = BaseGuestOwnerReach;
        return BaseRelayCells(b).Any(r => WrapDistSq(pos, new Vector3f(r.X + 0.5f, r.Y + 0.5f, r.Z + 0.5f)) <= near * near)
               || BaseRailStops(b).Any(s => WrapDistSq(pos, new Vector3f(s.X + 0.5f, s.Y, s.Z + 0.5f)) <= near * near);
    }

    /// <summary>Test seam (#3457): how often the rail walked a relay chain.</summary>
    private int _railRelayWalksForTest;

    /// <summary>Test seam (#3457): every place a stop belongs to — name and "settlement" / "base" — town first.</summary>
    public IReadOnlyList<(string Name, string Kind)> RailPlacesForTest(Vector3i stop)
        => RailPlacesAt(stop).Select(p => (p.Name, p.Base is null ? "settlement" : "base")).ToList();

    /// <summary>Test seam (#3457): every (home, away) pair the trip planner may pick on each line — names, the home's kind
    /// ("settlement" / "base") and whether both are the same stop.</summary>
    public IReadOnlyList<(string Home, string HomeKind, string Away, bool SameStop)> RailTripPairsForTest()
        => Rails.Lines.Values.SelectMany(l => RailTripPairs(RailStopPlaces(l)))
            .Select(p => (p.Home.Name, p.Home.Base is null ? "settlement" : "base", p.Away.Name, p.Home.Stop == p.Away.Stop)).ToList();

    /// <summary>Test seam (#3457): whether the base's owner, where they stand now, counts as home for a train guest (the
    /// reach part only — not the day, the foot or the hostiles).</summary>
    public bool OwnerNearBaseForTrainGuestForTest(int baseId)
        => _bases.FirstOrDefault(b => b.Id == baseId) is { } b && FindSessionByPlayerId(b.OwnerId) is { } owner
           && OwnerNearBaseForTrainGuest(b, owner.State.Position);

    /// <summary>Test seam (#3457): the relays the rail reads for a base, and how often it walked a chain so far.</summary>
    public (IReadOnlyList<Vector3i> Relays, int Walks) RailRelaysForTest(int baseId)
        => (_bases.FirstOrDefault(b => b.Id == baseId) is { } b ? BaseRelayCells(b).ToList() : new List<Vector3i>(), _railRelayWalksForTest);
}
