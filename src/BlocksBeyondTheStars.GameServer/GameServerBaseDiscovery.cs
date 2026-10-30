// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Base discovery (#2997, Marcel 2026-10-29): a player sees their own bases and their allies' and crew mates' bases on the
/// planet map always, anyone else's only once they have found it. The server filters the base list per recipient (a
/// modified client must not see more), so the map, the zone hint and the dome all follow from what the player knows.
/// <list type="bullet">
/// <item><b>Walking near</b> a base core — within <see cref="BaseRules.DiscoveryRadius"/> blocks horizontally — finds it,
/// checked every <see cref="BaseDiscoveryBeatSeconds"/> on every world with players (cheap: the bases of that body only).</item>
/// <item>A <b>Quantum scan</b> of a body finds every base there (<see cref="DiscoverAllBasesOn"/>, the settler scan #2998).</item>
/// <item>Saved per player (<c>PlayerState.DiscoveredBases</c>, "body|x|y|z" of the core), so a reconnect keeps it; a key whose
/// base is gone is never matched again.</item>
/// </list>
/// Nothing else changes: anyone may still walk into a base and breathe there, and admins keep <c>/builds</c>.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Seconds between two discovery checks of one world.</summary>
    private const double BaseDiscoveryBeatSeconds = 2.0;

    /// <summary>Per world (location id): seconds until its next discovery check.</summary>
    private readonly Dictionary<string, double> _baseDiscoveryClock = new();

    /// <summary>The discovery key of a base: its body and core cell.</summary>
    private static string BaseDiscoveryKey(ServerBase b) => b.Planet + "|" + b.Cell.X + "|" + b.Cell.Y + "|" + b.Cell.Z;

    /// <summary>Whose base this is for <paramref name="viewerId"/>: own, an ally's or crew mate's, or someone else's.</summary>
    private byte BaseRelation(ServerBase b, string viewerId)
        => b.OwnerId == viewerId ? NetBase.RelationOwn : AreAllied(b.OwnerId, viewerId) ? NetBase.RelationAlly : NetBase.RelationOther;

    /// <summary>Whether the base is in the player's base list: own or allied, or discovered.</summary>
    private bool SeesBase(PlayerSession viewer, ServerBase b)
        => BaseRelation(b, viewer.State.PlayerId) != NetBase.RelationOther || viewer.State.DiscoveredBases.Contains(BaseDiscoveryKey(b));

    /// <summary>Re-sends the base list to each of these players that is online (an alliance or a crew changed), and lets the
    /// partner groups' base indexes recount (#2996: what lies between two bases follows the groups).</summary>
    private void RefreshBasesFor(IEnumerable<string> playerIds)
    {
        foreach (var id in playerIds)
        {
            foreach (var b in _bases.Where(x => x.OwnerId == id))
            {
                if (_baseIndex.TryGetValue(b.Id, out var idx))
                {
                    idx.Dirty = true;
                }
            }

            if (FindSessionByPlayerId(id) is { Joined: true } s)
            {
                SendBases(s);
            }
        }
    }

    /// <summary>The bases' visitors, one schedule step: the friendly guests walking up to a base (#2602) and the players
    /// passing one (#2997).</summary>
    private void TickBaseVisits(double dt)
    {
        TickBaseGuests(dt);
        TickBaseDiscovery(dt);
    }

    /// <summary>The discovery beat of the active world: everyone on its surface finds the bases near them.</summary>
    private void TickBaseDiscovery(double dt)
    {
        string loc = _world.LocationId;
        double left = _baseDiscoveryClock.TryGetValue(loc, out var c) ? c - dt : 0;
        if (left > 0)
        {
            _baseDiscoveryClock[loc] = left;
            return;
        }

        _baseDiscoveryClock[loc] = BaseDiscoveryBeatSeconds;
        if (_world.Planet.Void || !_bases.Any(b => b.Planet == loc))
        {
            return;
        }

        foreach (var s in _sessions.Values)
        {
            if (s.Joined && s.CurrentLocationId == loc && !InSpace(s.State.PlayerId))
            {
                DiscoverBasesNear(s);
            }
        }
    }

    /// <summary>Finds every unknown base whose core is within reach of the player's feet; announces and re-sends the list.</summary>
    private void DiscoverBasesNear(PlayerSession s)
    {
        var p = s.State;
        long r2 = (long)BaseRules.DiscoveryRadius * BaseRules.DiscoveryRadius;
        List<ServerBase>? found = null;
        foreach (var b in _bases)
        {
            if (b.Planet != s.CurrentLocationId || SeesBase(s, b))
            {
                continue;
            }

            double dx = WorldConstants.WrapDeltaX((double)(p.Position.X - (b.Cell.X + 0.5f)), _world.Circumference);
            double dz = WorldConstants.WrapDeltaZ((double)(p.Position.Z - (b.Cell.Z + 0.5f)), _world.Circumference);
            if ((dx * dx) + (dz * dz) <= r2)
            {
                (found ??= new List<ServerBase>()).Add(b);
            }
        }

        if (found is null)
        {
            return;
        }

        foreach (var b in found)
        {
            p.DiscoveredBases.Add(BaseDiscoveryKey(b));
            Send(s, new ServerMessage { Text = "@srv.base.discovered:" + b.Name + " · " + b.OwnerId });
        }

        _repo.SavePlayer(p);
        SendBases(s);
    }

    /// <summary>A Quantum scan of <paramref name="body"/> finds every base there the player did not know (#2998). Returns how
    /// many were new; the list is re-sent when the player stands on that body.</summary>
    private int DiscoverAllBasesOn(PlayerSession s, string body)
    {
        int added = 0;
        foreach (var b in _bases)
        {
            if (b.Planet == body && !SeesBase(s, b) && s.State.DiscoveredBases.Add(BaseDiscoveryKey(b)))
            {
                added++;
            }
        }

        if (added > 0)
        {
            _repo.SavePlayer(s.State);
            if (s.CurrentLocationId == body)
            {
                SendBases(s);
            }
        }

        return added;
    }

    /// <summary>Test seam: one discovery check for the active world right now.</summary>
    public void RunBaseDiscoveryForTest()
    {
        _baseDiscoveryClock.Clear();
        TickBaseDiscovery(0);
    }

    /// <summary>Test seam: the base list this player would be sent (#2997).</summary>
    public IReadOnlyList<NetBase> BaseListForTest(PlayerSession session)
    {
        Serve(session);
        string me = session.State.PlayerId;
        return _bases.Where(b => b.Planet == session.CurrentLocationId && SeesBase(session, b)).Select(b => ToNetBaseFor(b, me)).ToList();
    }
}
