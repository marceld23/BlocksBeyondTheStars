// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #2564: who hurt the player. Every blow a creature, a machine, a bandit or a giant lands on a player records the
/// attacker's entity id and position on the session; the next <c>PlayerStateUpdate</c> (the hit already marks the state
/// dirty, so it goes out at the end of the same tick) carries them as <c>LastHitFrom*</c>, <c>LastHitEntityId</c> and
/// a <c>LastHitSeq</c> that changes with every blow. Before, the server reported only the health drop and the HUD's
/// damage marker guessed the nearest hostile within 24 blocks, in the horizontal plane only — a flier biting from
/// overhead read as "ahead", and the real attacker was not always the nearest one.
/// <para>Several attackers in one tick: the last recorded one wins — every one of them is a real attacker, and the
/// next tick's blows update it again.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Records <paramref name="attacker"/> as the source of the blow that just hurt <paramref name="session"/>'s
    /// player, the blow coming from <paramref name="from"/> (the attacker's position, or a giant's stomp/strike spot).</summary>
    private void NoteHitFrom(PlayerSession session, CombatEntity attacker, Vector3f from)
    {
        session.Sync.LastHitEntityId = attacker.Id;
        session.Sync.LastHitFrom = from;
        session.Sync.LastHitSeq = session.Sync.LastHitSeq == int.MaxValue ? 1 : session.Sync.LastHitSeq + 1; // never back to 0 ("none")

        // #3825: the first blow that lands while no radar scanner is worn — VEGA says, once, that there is one. The
        // scanner (#2292) shows hostiles on the compass and pings, but nothing in the game pointed to it.
        if (!Wears(session.State, RadarScannerItem))
        {
            ShipAiHintOnce(session, RadarScannerHint);
        }
    }

    /// <summary>The suit module that shows hostiles on the compass (#2292).</summary>
    internal const string RadarScannerItem = "radar_scanner";

    /// <summary>The once-hint that points to it (<c>vega.hint.radar_scanner</c>, #3825).</summary>
    internal const string RadarScannerHint = "radar_scanner";
}
