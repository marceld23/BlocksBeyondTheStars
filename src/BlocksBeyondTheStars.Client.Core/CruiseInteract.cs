// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Core;

/// <summary>What E does at the helm this frame — and so what the flight view's board prompt offers (#2934).</summary>
public enum CruiseInteractTarget
{
    /// <summary>Nothing in reach: the board prompt is hidden, and E says how to read an anomaly nearby (#2588).</summary>
    None,

    /// <summary>A wormhole within reach (#2242): E flies through it. The rift wins over everything else.</summary>
    Wormhole,

    /// <summary>Docked with a friend whose ship floats parked within reach, nearer than any station or body (#2724): E goes
    /// over to it.</summary>
    GoOver,

    /// <summary>The docked friend sits at the helm within reach and nothing else wants the key (#2724): E asks them to let
    /// us over (the server tells them).</summary>
    AskPartner,

    /// <summary>The docked friend is flying and E cannot ask (#2724) — with no station and no body in range the prompt says
    /// so. Information only: E does what it does for <see cref="None"/>.</summary>
    PartnerFlying,

    /// <summary>A station within board range, at least as close as the nearest body: E docks it.</summary>
    Station,

    /// <summary>A planet or moon within approach range, nearer than any station: E opens its landing-pad map.</summary>
    Body,
}

/// <summary>
/// The flight view's "what does E do" rule (#2934) — the cruise twin of <c>SpaceView.EvaBoardChoice</c> (#2703). The E key
/// and the board prompt both ask it, so the prompt always names what the key would do. Client presentation only: the
/// server checks every range again and has the last word.
/// </summary>
public static class CruiseInteractRules
{
    /// <summary>
    /// Picks the target, first match wins: a wormhole within reach; going over to the docked friend's parked ship
    /// (<paramref name="goesOver"/>); asking the docked friend at the helm (<paramref name="asksPartner"/>); the station when
    /// it is closer than the body or exactly as close; the body; and — information only — that the friend is flying.
    /// <paramref name="goesOver"/> and <paramref name="asksPartner"/> are the flight view's own picks from the same frame:
    /// it sets <paramref name="goesOver"/> only when the friend's ship is at least as close as the station and the body, and
    /// <paramref name="asksPartner"/> only while the friend flies within reach with no station and no body in range.
    /// </summary>
    /// <param name="wormholeInReach">A wormhole is within reach.</param>
    /// <param name="goesOver">E goes over to the docked friend's parked ship this frame.</param>
    /// <param name="asksPartner">E asks the docked friend at the helm to let us over this frame.</param>
    /// <param name="stationInRange">A station is within board range.</param>
    /// <param name="stationSq">Squared distance to that station's hull (ignored without one).</param>
    /// <param name="bodyInRange">A planet or moon is within approach range.</param>
    /// <param name="bodySq">Squared distance to that body (ignored without one).</param>
    /// <param name="partnerFlying">The visit prompt says the docked friend is flying.</param>
    public static CruiseInteractTarget Choose(bool wormholeInReach, bool goesOver, bool asksPartner,
        bool stationInRange, float stationSq, bool bodyInRange, float bodySq, bool partnerFlying)
    {
        if (wormholeInReach)
        {
            return CruiseInteractTarget.Wormhole;
        }

        if (goesOver)
        {
            return CruiseInteractTarget.GoOver;
        }

        if (asksPartner)
        {
            return CruiseInteractTarget.AskPartner;
        }

        if (stationInRange && (!bodyInRange || stationSq <= bodySq))
        {
            return CruiseInteractTarget.Station; // whichever is closer — a tie docks the station
        }

        if (bodyInRange)
        {
            return CruiseInteractTarget.Body;
        }

        return partnerFlying ? CruiseInteractTarget.PartnerFlying : CruiseInteractTarget.None;
    }
}
