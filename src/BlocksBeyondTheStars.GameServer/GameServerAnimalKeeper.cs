// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The lively places, the animal keeper's round (docs/developer/LIVELY_PLACES.md §4): the tamer of a settlement with a pen
/// does not stand at its stall all day — it walks over to the pen, stands at the fence for a while ("watching the
/// animals"; its own tame animal comes along, as it follows its keeper anyway) and walks back to its post. Behaviour
/// only, in every world: nothing is fed, nothing is laid, the pen's products are the farm pass's.
/// <list type="bullet">
/// <item><b>Only with a pen in reach.</b> No pen within <see cref="ErrandReach"/> of the post: the post is the whole job,
/// as it always was.</item>
/// <item><b>A customer holds it.</b> The tamer trades; it does not walk off while a player stands at its stall, and it
/// keeps its goods with it (a vendor trades wherever it stands, <see cref="MarketStock.VendorReach"/>).</item>
/// <item><b>The weather keeps it in.</b> It does not set out for the pen in rain, snow or a storm.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The job of the animal keeper (<c>NpcProfessions</c>).</summary>
    private const string AnimalKeeperJob = "tamer";

    /// <summary>Seconds the keeper stands at the pen, and at its post between two visits.</summary>
    private const double KeeperPenStayMin = 40.0;
    private const double KeeperPenStayMax = 80.0;
    private const double KeeperPostStayMin = 70.0;
    private const double KeeperPostStayMax = 140.0;

    /// <summary>A customer this close to the keeper keeps it at its post.</summary>
    private const float KeeperCustomerRange = 6f;

    private const string WatchingAnimalsActivityKey = "npc.activity.watching_animals";

    /// <summary>The keeper's day (from <see cref="TickSettlementDay"/>): post → pen → post. <c>SiteCursor</c> 0 = at the
    /// post, 1 = at the pen; <c>SiteUntil</c> is the end of the stay (0 = just arrived).</summary>
    private void TickAnimalKeeper(ServerNpc npc)
    {
        if (npc.Phase != NpcPhase.Day || npc.Pose != 0 || npc.Goal is not null
            || (npc.TalkWith is not null && _uptime < npc.TalkUntil) || _uptime < npc.AttendUntil || _uptime < npc.StartledUntil)
        {
            return;
        }

        if (SettlementOfNpc(npc) is not { Ruined: false } settlement || KeeperPenSpot(npc, settlement) is not { } pen)
        {
            return; // no pen in reach: the post is the whole job
        }

        bool atPen = npc.SiteCursor % 2 == 1;
        if (npc.SiteUntil <= 0)
        {
            npc.SiteUntil = _uptime + (atPen
                ? KeeperPenStayMin + (_errandRng.NextDouble() * (KeeperPenStayMax - KeeperPenStayMin))
                : KeeperPostStayMin + (_errandRng.NextDouble() * (KeeperPostStayMax - KeeperPostStayMin)));
            npc.ActivityKey = atPen ? WatchingAnimalsActivityKey : JobActivityKey(npc.Job);
            BroadcastNpcs();
            return;
        }

        if (_uptime < npc.SiteUntil)
        {
            return;
        }

        if (_worlds.Active.NpcPathQueue.Count > ErrandQueueSlack || (!atPen && (ErrandWeatherIsBad(npc.Pos) || AnyPlayerOnFootWithin(npc.Pos, KeeperCustomerRange))))
        {
            npc.SiteUntil = _uptime + 5.0; // the routes are busy, it rains, or somebody is at the stall: stay
            return;
        }

        npc.SiteCursor = atPen ? 0 : 1;
        npc.SiteUntil = 0;
        SetNpcGoal(npc, atPen ? npc.Work : pen, NpcArrival.None, WorkLeash);
    }

    /// <summary>The spot at the fence of the pen nearest the keeper's post, within walking reach and on its level; null
    /// when the place has no pen there.</summary>
    private Vector3f? KeeperPenSpot(ServerNpc npc, SettlementInstance settlement)
    {
        Vector3f? best = null;
        double bestSq = (double)ErrandReach * ErrandReach;
        foreach (var a in PlaceAnchorsOf(settlement))
        {
            if (a.Kind != PlaceAnchorKind.Pen || Math.Abs(a.Spot.Y - npc.Work.Y) > ErrandRise)
            {
                continue;
            }

            double d = WrapDistSq(a.Spot, npc.Work);
            if (d < bestSq)
            {
                bestSq = d;
                best = a.Spot;
            }
        }

        return best;
    }
}
