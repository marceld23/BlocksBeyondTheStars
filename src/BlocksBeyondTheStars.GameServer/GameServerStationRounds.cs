// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The lively places, a round through the station (docs/developer/LIVELY_PLACES.md §8): one of a kit station's crew
/// members without a post — a service android when there is one among them — walks from post to post by day instead of
/// standing in the hall: the trade post, the mission board, the hangar, the greenhouse, the med bay, the lounges and the
/// profession posts, a short stop at each, round and round. Behaviour only: nobody is added, nothing is pinned, so it
/// works aboard every kit station of every world.
/// <list type="bullet">
/// <item><b>Who.</b> Picked when the crew is spawned (<see cref="AssignServiceRounds"/>): the first android among the
/// crew without a post, else the first of them; a station whose every cabin has a post has nobody on rounds.</item>
/// <item><b>Where.</b> A free standable spot beside every post marker, a step off the post itself (its holder stands
/// there), chained nearest-first into a loop from the hall. Found once per station and kept for two minutes.</item>
/// <item><b>Cheap.</b> One route search per stop, and only while the route queue is nearly empty; the evening and the
/// night are the routine's (the walker has a cabin like everybody else).</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Seconds the walker stays at a stop.</summary>
    private const double RoundStayMin = 10.0;
    private const double RoundStayMax = 22.0;

    /// <summary>Seconds a station's stops are kept before they are looked up again.</summary>
    private const double RoundStopsKeep = 120.0;

    /// <summary>How far from a post marker the walker's spot may lie, and how far it keeps off the post itself.</summary>
    private const int RoundStopReach = 2;
    private const float RoundStopKeepOff = 1.2f;

    internal const string RoundsActivityKey = "npc.activity.rounds";

    /// <summary>The markers a round leads past (the profession posts come on top).</summary>
    private static readonly string[] RoundStopMarkers = { "vendor", "mission_board", "hangar", "greenhouse", "heal_tank", "lounge" };

    private readonly Dictionary<string, (double At, List<Vector3f> Stops)> _stationRoundStops = new(StringComparer.Ordinal);

    /// <summary>Picks the crew member that walks the round: among the ones without a post (in spawn order) the first
    /// android, else the first of them.</summary>
    private static void AssignServiceRounds(List<ServerNpc> crewWithoutPost)
    {
        var walker = crewWithoutPost.FirstOrDefault(n => n.IsRobot) ?? crewWithoutPost.FirstOrDefault();
        if (walker is not null)
        {
            walker.ServiceRounds = true;
        }
    }

    /// <summary>The stops of a station's round: a spot beside each post, nearest-first from the hall.</summary>
    private List<Vector3f> StationRoundStops(BoardableStation station)
    {
        if (_stationRoundStops.TryGetValue(station.Id, out var kept) && _uptime < kept.At + RoundStopsKeep)
        {
            return kept.Stops;
        }

        var found = new List<Vector3f>();
        foreach (var (type, pos) in station.Markers)
        {
            if (Array.IndexOf(RoundStopMarkers, type) < 0 && NpcProfessions.ByMarker(type) is null)
            {
                continue;
            }

            if (AnchorSpotNear(pos, RoundStopReach, RoundStopKeepOff, station.Spawn) is { } spot
                && !found.Any(f => WrapDistSq(f, spot) < 1.5 * 1.5))
            {
                found.Add(spot);
            }
        }

        // A loop: from the hall always on to the nearest stop not yet in it.
        var stops = new List<Vector3f>(found.Count);
        var from = station.Spawn;
        while (found.Count > 0)
        {
            int best = 0;
            for (int i = 1; i < found.Count; i++)
            {
                if (WrapDistSq(found[i], from) < WrapDistSq(found[best], from))
                {
                    best = i;
                }
            }

            from = found[best];
            stops.Add(from);
            found.RemoveAt(best);
        }

        _stationRoundStops[station.Id] = (_uptime, stops);
        return stops;
    }

    /// <summary>The round of the crew member on service (from <see cref="TickNpcJob"/>, every tick while a player is near).</summary>
    private void TickStationRounds(ServerNpc npc)
    {
        if (npc.Phase != NpcPhase.Day || npc.Pose != 0 || npc.Goal is not null)
        {
            return; // on the way — or the evening and the night, which are the routine's
        }

        if ((npc.TalkWith is not null && _uptime < npc.TalkUntil) || _uptime < npc.AttendUntil || _uptime < npc.StartledUntil || NpcInPlayerDialog(npc))
        {
            return; // somebody talks to it: the stop simply lasts that much longer
        }

        if (npc.OnErrand)
        {
            // Arrived at a stop (or the way there gave out): stay a little, greet whoever stands there.
            npc.OnErrand = false;
            npc.ErrandNextAt = _uptime + RoundStayMin + (_errandRng.NextDouble() * (RoundStayMax - RoundStayMin));
            NodToANeighbour(npc);
            return;
        }

        if (npc.ErrandNextAt <= 0)
        {
            npc.ErrandNextAt = _uptime + 4.0 + (StableUnit("rounds:first", npc.Id) * 20.0); // a boarder first sees it in the hall
            return;
        }

        if (_uptime < npc.ErrandNextAt)
        {
            return;
        }

        SetOutForTheNextStop(npc);
    }

    /// <summary>The walker's stay is over: on to the next stop of the round — or a little later when there is nothing to
    /// walk, the routes are busy or somebody stands at the stop.</summary>
    private void SetOutForTheNextStop(ServerNpc npc)
    {
        if (ActiveStationInterior() is not { } station || StationRoundStops(station) is not { Count: >= 2 } stops)
        {
            npc.ErrandNextAt = _uptime + 60.0; // no station, or one with a single post: nothing to walk
            return;
        }

        if (_worlds.Active.NpcPathQueue.Count > ErrandQueueSlack)
        {
            npc.ErrandNextAt = _uptime + 3.0; // the routes are busy — a little later
            return;
        }

        npc.RoundStop = (npc.RoundStop + 1) % stops.Count;
        var stop = stops[npc.RoundStop];
        if (ErrandSpotTaken(npc, stop))
        {
            npc.ErrandNextAt = _uptime + 2.0; // somebody stands there: on to the next one
            return;
        }

        npc.OnErrand = true;
        if (npc.ActivityKey != RoundsActivityKey)
        {
            npc.ActivityKey = RoundsActivityKey;
            BroadcastNpcs();
        }

        SetNpcGoal(npc, stop, NpcArrival.None, ErrandLeash);
    }

    /// <summary>Test seam: the ids of the crew members on service rounds in the active world.</summary>
    internal IReadOnlyList<int> ServiceRoundWalkersForTest => _npcs.Where(n => n.ServiceRounds).Select(n => n.Id).ToList();

    /// <summary>Test seam: the stops of the active station's round ("no station" = empty).</summary>
    internal IReadOnlyList<Vector3f> StationRoundStopsForTest()
        => ActiveStationInterior() is { } station ? StationRoundStops(station) : new List<Vector3f>();

    /// <summary>Test seam: the stop a walker last set out for, and whether it is on the way or standing there.</summary>
    internal (int Stop, bool Walking, string Activity) ServiceRoundForTest(int npcId)
        => _npcs.FirstOrDefault(n => n.Id == npcId) is { } n ? (n.RoundStop, n.OnErrand && n.Goal is not null, n.ActivityKey) : (-1, false, string.Empty);
}
