// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #1530: the end-of-world-tick flush. Entity lists (enemies + bandits, creatures, NPCs) and player states are
/// MARKED by the simulation and sent here at most once per tick and per world — where they used to go out on
/// every event (a creature list per spawn, prune, reconcile AND the 2 Hz beat in the same tick; a player state
/// per biting attacker per tick, followed by a duplicate from the 2 Hz vitals gate). Wire messages and their
/// contents are unchanged; only the count and the moment within the tick differ.
/// </summary>
public sealed partial class GameServer
{
    private void FlushEntityLists()
    {
        var w = _worlds.Active;
        if (w == null)
        {
            return;
        }

        if (w.EnemyListDirty)
        {
            w.EnemyListDirty = false;
            SendPlanetEnemyList();
        }

        if (w.CreatureListDirty)
        {
            w.CreatureListDirty = false;
            SendCreatureList();
        }

        if (w.NpcListDirty)
        {
            w.NpcListDirty = false;
            SendNpcList();
        }

        foreach (var session in JoinedInActiveWorld())
        {
            if (session.PlayerStateDirty)
            {
                session.PlayerStateDirty = false;
                SendPlayerState(session);
            }
        }
    }

    /// <summary>Marks a session's state for the end-of-tick send and records the vitals as sent, so the 2 Hz
    /// vitals gate does not follow up with a duplicate. A lethal hit still sends at once — the death path
    /// (RespawnPlayer) must see the zeroed health on the wire first, exactly as before.</summary>
    private void MarkPlayerStateDirty(PlayerSession session)
    {
        var p = session.State;
        session.LastSentHealth = p.Health;
        session.LastSentOxygen = p.Oxygen;
        session.LastSentEnergy = p.SuitEnergy;
        session.LastSentHunger = p.Hunger;
        if (p.Health <= 0f)
        {
            session.PlayerStateDirty = false;
            SendPlayerState(session);
            return;
        }

        session.PlayerStateDirty = true;
    }
}
