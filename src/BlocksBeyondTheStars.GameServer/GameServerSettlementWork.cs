// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The lively places, a villager's day (docs/developer/LIVELY_PLACES.md): what the people of a settlement do between
/// morning and evening. Somebody without a post goes on errand rounds (GameServerNpcErrands.cs); the gardener works where
/// the village grows things — the greenhouse, then a field's edge with the hoe, then a basket carried to the market stall,
/// and back. All of it is to be seen and nothing more: no crop is picked, no stock changes (a field is a player's food,
/// and the vendor's goods are the vendor's). The craftsman, the keeper and the vendor stay at their posts, as they did.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Seconds the village gardener works at one place.</summary>
    private const double VillageGardenStayMin = 25.0;
    private const double VillageGardenStayMax = 45.0;

    /// <summary>Seconds the gardener stands at the market stall with the basket.</summary>
    private const double VillageDeliveryStayMin = 8.0;
    private const double VillageDeliveryStayMax = 14.0;

    private const string CarryingActivityKey = "npc.activity.carrying";

    private enum GardenStop : byte { Greenhouse, Field, Market }

    private readonly List<(Vector3f Spot, GardenStop What)> _gardenStops = new(); // reused per call

    /// <summary>One villager's day (from <see cref="TickNpcJob"/>, every tick for the people in a player's reach).</summary>
    private void TickSettlementDay(ServerNpc npc)
    {
        if (npc.IsChild)
        {
            TickChildPlay(npc);
        }
        else if (GoesOnErrands(npc))
        {
            TickNpcErrand(npc);
        }
        else if (npc.Job == "gardener")
        {
            TickVillageGardener(npc);
        }
        else if (npc.Job == NightWatchJob)
        {
            TickNightWatch(npc);
        }
        else if (npc.Job == AnimalKeeperJob)
        {
            TickAnimalKeeper(npc);
        }
    }

    // =====================================================================================================
    // The night watch
    // =====================================================================================================

    /// <summary>The job of a town's or a city's night watch: on its round from the evening through the night with a
    /// lantern, asleep in the morning, off duty in the afternoon.</summary>
    internal const string NightWatchJob = "watch";

    /// <summary>What the night watch carries on its round (a client render key; the client lights it).</summary>
    internal const string NightWatchLantern = "npc_lantern";

    private const string NightWatchActivityKey = "npc.activity.night_watch";

    /// <summary>How many hello lines of its own the watch has on its round (<c>npc.hello.watch.&lt;n&gt;</c>, no name in them:
    /// a stranger hears them too).</summary>
    internal const int NightWatchHelloLines = 3;

    /// <summary>The local day fraction until which the night watch sleeps after its shift.</summary>
    private const double NightWatchSleepsUntil = 0.50;

    /// <summary>Seconds the night watch stands at a waypoint before it walks on.</summary>
    private const double NightWatchStayMin = 6.0;
    private const double NightWatchStayMax = 14.0;

    /// <summary>The most waypoints of a round.</summary>
    private const int NightWatchWaypoints = 10;

    /// <summary>How many of a settlement's residents keep the night watch.</summary>
    private static int NightWatchCount(string tier) => tier switch
    {
        "town" => 1,
        "city" => 2,
        _ => 0,
    };

    /// <summary>Makes the last residents without a post the night watch (a town's one, a city's two) — after every draw of
    /// the spawn, so nobody's name, look or bed changes.</summary>
    private static void AssignNightWatch(SettlementInstance settlement, List<ServerNpc> plainResidents)
    {
        int wanted = Math.Min(NightWatchCount(settlement.Tier), Math.Max(0, plainResidents.Count - 2)); // a place keeps people for the day
        for (int i = 0; i < wanted; i++)
        {
            plainResidents[plainResidents.Count - 1 - i].Job = NightWatchJob;
        }
    }

    /// <summary>The watch's round: the plaza, where the roads leave the place and its lanterns, in the order of their
    /// bearing from the plaza (a loop), at most <see cref="NightWatchWaypoints"/>.</summary>
    private List<Vector3f> NightWatchRound(SettlementInstance settlement)
    {
        var plaza = SettlementSquareCentre(settlement);
        var points = new List<Vector3f>();
        bool plazaTaken = false;
        foreach (var a in PlaceAnchorsOf(settlement))
        {
            if (a.Kind is PlaceAnchorKind.Lamp or PlaceAnchorKind.Exit || (a.Kind == PlaceAnchorKind.Plaza && !plazaTaken))
            {
                plazaTaken |= a.Kind == PlaceAnchorKind.Plaza;
                points.Add(a.Spot);
            }
        }

        points.Sort((p, q) => Math.Atan2(p.Z - plaza.Z, p.X - plaza.X).CompareTo(Math.Atan2(q.Z - plaza.Z, q.X - plaza.X)));
        while (points.Count > NightWatchWaypoints)
        {
            points.RemoveAt(points.Count - 1 - (points.Count % 2)); // thin it evenly enough, keep the loop's order
        }

        return points;
    }

    /// <summary>On duty (the routine's "Day" for the watch is the evening and the night): from waypoint to waypoint.</summary>
    private void TickNightWatch(ServerNpc npc)
    {
        if (npc.Phase != NpcPhase.Day || npc.Pose != 0 || npc.Goal is not null
            || (npc.TalkWith is not null && _uptime < npc.TalkUntil) || _uptime < npc.AttendUntil || _uptime < npc.StartledUntil)
        {
            return;
        }

        if (npc.Held != NightWatchLantern)
        {
            npc.Held = NightWatchLantern;
            npc.ActivityKey = NightWatchActivityKey;
            BroadcastNpcs();
        }

        if (npc.SiteUntil <= 0)
        {
            // Just arrived at a waypoint (or the shift just began): stand a moment and look about.
            npc.SiteUntil = _uptime + NightWatchStayMin + (_errandRng.NextDouble() * (NightWatchStayMax - NightWatchStayMin));
            return;
        }

        if (_uptime < npc.SiteUntil)
        {
            return;
        }

        if (npc.Patrol is null && SettlementOfNpc(npc) is { Ruined: false } settlement)
        {
            npc.Patrol = NightWatchRound(settlement);
            npc.PatrolIndex = 0;
        }

        if (npc.Patrol is not { Count: > 1 } round)
        {
            npc.Patrol = null; // nowhere to walk yet (the place is not loaded): look again in a moment
            npc.SiteUntil = _uptime + 5.0;
            return;
        }

        if (_worlds.Active.NpcPathQueue.Count > ErrandQueueSlack)
        {
            npc.SiteUntil = _uptime + 3.0; // the routes are busy
            return;
        }

        npc.PatrolIndex = (npc.PatrolIndex + 1) % round.Count;
        npc.SiteUntil = 0;
        SetNpcGoal(npc, round[npc.PatrolIndex], NpcArrival.None, ErrandLeash);
    }

    /// <summary>The lantern goes out when the shift ends.</summary>
    private static void EndNightWatchShift(ServerNpc npc)
    {
        if (npc.Job == NightWatchJob && npc.Held == NightWatchLantern)
        {
            npc.Held = string.Empty;
        }

        if (npc.Job == NightWatchJob)
        {
            npc.Patrol = null; // read again at the next shift: a lantern may have come or gone
        }
    }

    /// <summary>The gardener's round: the greenhouse, then for every field in reach its edge and the market stall.</summary>
    private List<(Vector3f Spot, GardenStop What)> VillageGardenStops(ServerNpc npc, SettlementInstance settlement)
    {
        _gardenStops.Clear();
        _gardenStops.Add((npc.Work, GardenStop.Greenhouse));
        Vector3f? market = null;
        var anchors = PlaceAnchorsOf(settlement);
        foreach (var a in anchors)
        {
            if (a.Kind == PlaceAnchorKind.Market && WrapDistSq(a.Spot, npc.Work) <= ErrandReach * ErrandReach)
            {
                market = a.Spot;
                break;
            }
        }

        foreach (var a in anchors)
        {
            if (a.Kind != PlaceAnchorKind.Field || WrapDistSq(a.Spot, npc.Work) > ErrandReach * ErrandReach || Math.Abs(a.Spot.Y - npc.Work.Y) > ErrandRise)
            {
                continue;
            }

            _gardenStops.Add((a.Spot, GardenStop.Field));
            if (market is { } stall)
            {
                _gardenStops.Add((stall, GardenStop.Market));
            }

            _gardenStops.Add((npc.Work, GardenStop.Greenhouse));
        }

        return _gardenStops;
    }

    /// <summary>The gardener just arrived at a stop (or its shift just began at the greenhouse): it works there for a
    /// while — at the stall it only sets the basket down, at the field and the greenhouse the hoe is back in its hand.</summary>
    private void BeginGardenStay(ServerNpc npc, bool delivery)
    {
        npc.SiteUntil = _uptime + (delivery
            ? VillageDeliveryStayMin + (_errandRng.NextDouble() * (VillageDeliveryStayMax - VillageDeliveryStayMin))
            : VillageGardenStayMin + (_errandRng.NextDouble() * (VillageGardenStayMax - VillageGardenStayMin)));
        if (!delivery)
        {
            PutDownBasket(npc);
            npc.ActivityKey = JobActivityKey(npc.Job);
        }

        BroadcastNpcs();
    }

    private void TickVillageGardener(ServerNpc npc)
    {
        if (npc.Phase != NpcPhase.Day || npc.Pose != 0 || npc.Goal is not null
            || (npc.TalkWith is not null && _uptime < npc.TalkUntil) || _uptime < npc.AttendUntil || _uptime < npc.StartledUntil)
        {
            return;
        }

        if (SettlementOfNpc(npc) is not { Ruined: false } settlement)
        {
            return;
        }

        var stops = VillageGardenStops(npc, settlement);
        if (stops.Count <= 1)
        {
            return; // no field in reach: the greenhouse is the whole job, as it always was
        }

        var here = stops[npc.SiteCursor % stops.Count];
        if (npc.SiteUntil <= 0)
        {
            BeginGardenStay(npc, delivery: here.What == GardenStop.Market);
            return;
        }

        if (_uptime < npc.SiteUntil)
        {
            return;
        }

        var next = stops[(npc.SiteCursor + 1) % stops.Count];
        if (_worlds.Active.NpcPathQueue.Count > ErrandQueueSlack || (next.What != GardenStop.Greenhouse && ErrandWeatherIsBad(npc.Pos)))
        {
            npc.SiteUntil = _uptime + 5.0; // the routes are busy, or it rains: stay where the roof is
            return;
        }

        npc.SiteCursor = (npc.SiteCursor + 1) % stops.Count;
        npc.SiteUntil = 0;
        if (next.What == GardenStop.Market && npc.ErrandHeld is null)
        {
            npc.ErrandHeld = npc.Held; // from the field to the stall with a basket; the hoe is back in its hand afterwards
            npc.Held = "npc_basket";
            npc.ActivityKey = CarryingActivityKey;
            BroadcastNpcs();
        }

        SetNpcGoal(npc, next.Spot, NpcArrival.None, WorkLeash);
    }

    /// <summary>What a village post holder carries at work (a client render key): the tools a base resident of the same
    /// job carries, so a gardener is a gardener wherever it lives.</summary>
    private static string ResidentTool(string job) => job switch
    {
        "gardener" => "npc_hoe",
        "craftsman" => "npc_hammer",
        _ => string.Empty,
    };

    /// <summary>The basket goes, the tool is back in the hand.</summary>
    private static void PutDownBasket(ServerNpc npc)
    {
        if (npc.ErrandHeld is { } before)
        {
            npc.Held = before;
            npc.ErrandHeld = null;
        }
    }
}
