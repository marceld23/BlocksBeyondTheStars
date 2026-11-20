// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The lively places, the trader pilot's walk (docs/developer/LIVELY_PLACES.md): a trader that set down beside a
/// settlement does not stand at its ship for a quarter of an hour. Once per stay — a while after touchdown, when no
/// customer stands at the ship and there is time left — its pilot walks to the settlement's market (the stall of the
/// vendor, a market stall of a lively place), stays there a couple of minutes "at the counter" and walks back. With no
/// market within walking reach it stays at its ship, as it always did.
/// <para>Its goods go where it goes: the barter reach, the "somebody is near, do not lift off yet" rule and the map
/// marker all follow the pilot's position.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Where a landed trader's pilot is on its walk.</summary>
    internal enum PilotWalk : byte
    {
        /// <summary>At the ship, the walk still ahead (or never: no market near).</summary>
        AtShip,
        ToMarket,
        AtMarket,
        Back,

        /// <summary>No market within walking reach: a few steps off, for a look at its ship.</summary>
        ToLook,
        AtLook,

        /// <summary>Back from the look, and then on a crate in front of its ship until a customer comes.</summary>
        BackToSit,
        Sitting,

        /// <summary>At the ship again — one walk per stay.</summary>
        Done,
    }

    /// <summary>Seconds after touchdown before the pilot sets out.</summary>
    private const double PilotWalkAfter = 40.0;

    /// <summary>Seconds of its stay the pilot must have left to set out, and at which it heads back from the market.</summary>
    private const double PilotWalkNeeds = 300.0;
    private const double PilotWalkBackAt = 120.0;

    /// <summary>Seconds the pilot stays at the market.</summary>
    private const double PilotMarketStayMin = 90.0;
    private const double PilotMarketStayMax = 160.0;

    /// <summary>How far the market may be from the ship (the route search covers 64 blocks) and how much higher or lower.</summary>
    private const float PilotWalkReach = 56f;
    private const float PilotWalkRise = 6f;

    /// <summary>A customer this close to the pilot keeps it where it is.</summary>
    private const float PilotCustomerRange = 12f;

    /// <summary>How far the pilot steps off for a look at its ship, and how long it stands there.</summary>
    private const float PilotLookStep = 6f;
    private const double PilotLookStayMin = 45.0;
    private const double PilotLookStayMax = 80.0;

    internal const string CheckingShipActivityKey = "npc.activity.checking_ship";

    /// <summary>The crate the pilot sits on in front of its ship (<c>NetNpc.Held</c> of a seated NPC, a client render key:
    /// the client draws the crate under it — no block is placed, nothing can be mined or taken).</summary>
    internal const string PilotCrateSeat = "npc_crate_seat";

    /// <summary>A customer this close gets the seated pilot on its feet; so does the last stretch before lift-off.</summary>
    private const float PilotStandsUpRange = 5f;
    private const double PilotStandsUpBefore = 45.0;

    private void StepTraderPilotWalk(NpcLandedTrader lt)
    {
        if (lt.PilotNpcId == 0 || lt.Walk == PilotWalk.Done || _npcs.FirstOrDefault(n => n.Id == lt.PilotNpcId) is not { } pilot)
        {
            return;
        }

        if (lt.Walk != PilotWalk.AtShip)
        {
            lt.PilotPos = pilot.Pos; // the goods, the "somebody is near" rule and the map marker go where the pilot goes
        }

        switch (lt.Walk)
        {
            case PilotWalk.AtShip:
                StepPilotAtShip(lt, pilot);
                break;

            case PilotWalk.ToLook or PilotWalk.AtLook or PilotWalk.BackToSit or PilotWalk.Sitting:
                StepPilotLookAndSit(lt, pilot);
                break;

            case PilotWalk.ToMarket when pilot.Goal is null:
                lt.Walk = PilotWalk.AtMarket;
                lt.WalkUntil = _uptime + PilotMarketStayMin + (_errandRng.NextDouble() * (PilotMarketStayMax - PilotMarketStayMin));
                pilot.ActivityKey = "npc.activity.trading";
                BroadcastNpcs();
                BroadcastPlanetPois(); // the map marker moved with the pilot
                break;

            case PilotWalk.AtMarket when _uptime >= lt.WalkUntil || lt.ExpiresAt - _uptime < PilotWalkBackAt:
                if (AnyPlayerOnFootWithin(pilot.Pos, MarketStockReachForPilot))
                {
                    break; // a customer at the stall: the walk home waits (the trader does not lift off either)
                }

                lt.Walk = PilotWalk.Back;
                pilot.ActivityKey = string.Empty;
                SetNpcGoal(pilot, lt.PilotHome, NpcArrival.None, NpcWanderLeash);
                BroadcastNpcs();
                break;

            case PilotWalk.Back when pilot.Goal is null:
                lt.Walk = PilotWalk.Done;
                lt.PilotPos = pilot.Pos;
                BroadcastPlanetPois();
                break;
        }
    }

    /// <summary>The walk of a pilot with no market near: a few steps off, a look at its ship, back, and onto its crate
    /// until a customer comes or lift-off is near.</summary>
    private void StepPilotLookAndSit(NpcLandedTrader lt, ServerNpc pilot)
    {
        switch (lt.Walk)
        {
            case PilotWalk.ToLook when pilot.Goal is null:
                lt.Walk = PilotWalk.AtLook;
                lt.WalkUntil = _uptime + PilotLookStayMin + (_errandRng.NextDouble() * (PilotLookStayMax - PilotLookStayMin));
                FaceTowards(pilot, lt.PilotHome); // back toward where it stood: the ship is right there
                BroadcastNpcs();
                break;

            case PilotWalk.AtLook when _uptime >= lt.WalkUntil || AnyPlayerOnFootWithin(lt.PilotHome, PilotCustomerRange):
                lt.Walk = PilotWalk.BackToSit; // looked enough — or a customer came to the ship
                pilot.ActivityKey = string.Empty;
                SetNpcGoal(pilot, lt.PilotHome, NpcArrival.None, NpcWanderLeash);
                BroadcastNpcs();
                break;

            case PilotWalk.BackToSit when pilot.Goal is null:
                lt.Walk = SitDownAtShip(lt, pilot) ? PilotWalk.Sitting : PilotWalk.Done;
                BroadcastNpcs();
                BroadcastPlanetPois(); // the map marker is back at the ship
                break;

            case PilotWalk.Sitting when AnyPlayerOnFootWithin(pilot.Pos, PilotStandsUpRange) || lt.ExpiresAt - _uptime < PilotStandsUpBefore:
                GetUpAtShip(pilot);
                lt.Walk = PilotWalk.Done;
                BroadcastNpcs();
                break;
        }
    }

    /// <summary>At the ship, the walk still ahead: once the stay has settled and nobody stands with the pilot, it sets out —
    /// to the market when one is within walking reach, else a few steps off to have a look at its ship.</summary>
    private void StepPilotAtShip(NpcLandedTrader lt, ServerNpc pilot)
    {
        if (lt.ExpiresAt - _uptime < PilotWalkNeeds)
        {
            lt.Walk = PilotWalk.Done; // too late for a walk
            return;
        }

        if (_uptime < lt.TouchdownAt + PilotWalkAfter || _worlds.Active.NpcPathQueue.Count > ErrandQueueSlack
            || AnyPlayerOnFootWithin(pilot.Pos, PilotCustomerRange))
        {
            return;
        }

        if (NearestMarketFor(pilot.Pos) is { } market)
        {
            lt.Walk = PilotWalk.ToMarket;
            pilot.ActivityKey = "npc.activity.visiting";
            SetNpcGoal(pilot, market, NpcArrival.None, 0.8f);
        }
        else if (ShipLookSpot(lt) is { } look)
        {
            lt.Walk = PilotWalk.ToLook;
            pilot.ActivityKey = CheckingShipActivityKey;
            SetNpcGoal(pilot, look, NpcArrival.None, 0.6f);
        }
        else
        {
            lt.Walk = PilotWalk.Done; // hemmed in where it stands: nothing to walk to
            return;
        }

        BroadcastNpcs();
    }

    /// <summary>Where the pilot steps to for a look at its ship: a standable spot a few blocks from where it stands, in a
    /// direction that is the trader's own (a hash of its id), or null when the ground offers none.</summary>
    private Vector3f? ShipLookSpot(NpcLandedTrader lt)
    {
        double angle = StableUnit("pilot:look:" + lt.Id, 0) * Math.PI * 2.0;
        var toward = new Vector3f(lt.PilotHome.X + ((float)Math.Cos(angle) * PilotLookStep * 2f), lt.PilotHome.Y,
            lt.PilotHome.Z + ((float)Math.Sin(angle) * PilotLookStep * 2f));
        return AnchorSpotNear(lt.PilotHome, (int)PilotLookStep + 1, PilotLookStep - 1.5f, toward);
    }

    /// <summary>Back from its look with nobody waiting and time left: the pilot sits down on its crate, its back to the
    /// ship. False when a customer stands there or lift-off is near — then it stays on its feet.</summary>
    private bool SitDownAtShip(NpcLandedTrader lt, ServerNpc pilot)
    {
        if (AnyPlayerOnFootWithin(pilot.Pos, PilotCustomerRange) || lt.ExpiresAt - _uptime < PilotStandsUpBefore * 2
            || WrapDistSq(pilot.Pos, lt.PilotHome) > 1.5 * 1.5)
        {
            return false; // somebody is here, it is nearly time to go, or it did not get back to its spot
        }

        pilot.ErrandHeld = pilot.Held;
        pilot.Held = PilotCrateSeat;
        pilot.Pose = 1;
        pilot.Facing = 0f; // the ship stands at -Z of the pilot's spot: it sits with its back to it
        pilot.FaceGoal = null;
        pilot.Path = null;
        pilot.ActivityKey = "npc.activity.resting";
        return true;
    }

    /// <summary>Up from the crate, where it sat (a seated villager's <see cref="StandUp"/> walks back to its chair's
    /// approach spot; the pilot has none).</summary>
    private static void GetUpAtShip(ServerNpc pilot)
    {
        pilot.Pose = 0;
        pilot.Held = pilot.ErrandHeld ?? string.Empty;
        pilot.ErrandHeld = null;
        pilot.ActivityKey = string.Empty;
    }

    /// <summary>Test seam: the id of a body's landed trader's pilot NPC (0 = none).</summary>
    internal int LandedTraderPilotIdForTest(string bodyId)
        => _landedTraders.TryGetValue(bodyId, out var lt) ? lt.PilotNpcId : 0;

    /// <summary>Test seam: where a body's landed trader's pilot is on its walk ("" = no trader there).</summary>
    internal string LandedTraderPilotWalkForTest(string bodyId)
        => _landedTraders.TryGetValue(bodyId, out var lt) ? lt.Walk.ToString() : string.Empty;

    /// <summary>A customer this close to the pilot at the market counts as trading with it.</summary>
    private const float MarketStockReachForPilot = 5f;

    /// <summary>The visitor's spot at the nearest market of an inhabited settlement within walking reach of
    /// <paramref name="from"/> (a lively place's stall first, else in front of the vendor), or null.</summary>
    private Vector3f? NearestMarketFor(Vector3f from)
    {
        Vector3f? best = null;
        double bestSq = (double)PilotWalkReach * PilotWalkReach;
        foreach (var s in _settlements)
        {
            if (s.Ruined || WrapDistSq(SettlementBoxCentre(s), from) > Math.Pow(PilotWalkReach + (0.75 * Math.Max(s.Max.X - s.Min.X, s.Max.Z - s.Min.Z)), 2))
            {
                continue; // a cheap look first: the place is nowhere near
            }

            foreach (var a in PlaceAnchorsOf(s))
            {
                if (a.Kind is not (PlaceAnchorKind.Stall or PlaceAnchorKind.Market) || Math.Abs(a.Spot.Y - from.Y) > PilotWalkRise
                    || ErrandSpotTaken(null, a.Spot))
                {
                    continue;
                }

                double d = WrapDistSq(a.Spot, from);
                if (d < bestSq)
                {
                    bestSq = d;
                    best = a.Spot;
                }
            }
        }

        return best;
    }

    /// <summary>Test seam: where the pilot of the trader landed on the active world is on its walk ("" = no trader).</summary>
    internal string TraderPilotWalkForTest()
        => _world is not null && _landedTraders.TryGetValue(_world.LocationId, out var lt) ? lt.Walk.ToString() : string.Empty;
}
