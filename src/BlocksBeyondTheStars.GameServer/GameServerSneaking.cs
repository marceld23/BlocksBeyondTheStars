// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Sneaking past sleeping animals (#3751 — Justus: "when you sneak, sleeping animals should not notice you"). An animal in the
/// off-phase of its day/night rhythm wakes when a player comes within <see cref="CreatureWakeDistance"/> — unless that player
/// sneaks: crouching on foot is quiet here as it is on the sand (#2001, the sandworms do not hear a crouching player's steps).
/// <list type="bullet">
/// <item>Only the wake <b>by coming close</b> changes. A hit, a shot beside it, mining next to an animal that minds it, a
/// ship's jets, a train's horn, fire and a wall built through it wake a sleeper as before.</item>
/// <item>One standing player in the wake distance is enough, whoever else sneaks there.</item>
/// <item>The giants are not part of this: they move by their own rules (<c>TickGiants</c>) and a guardian giant wakes when
/// a block of its body is mined.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Whether a player sneaks: crouching on foot. The crouch is the client's pose flag (#2435); a speeder's engine
    /// and a running jetpack are loud whatever the pose.</summary>
    internal static bool Sneaks(PlayerState p) => p.Crouching && !p.Jetpacking && string.IsNullOrEmpty(p.InSpeeder);

    /// <summary>Whether a player wakes the sleeper at <paramref name="sleeper"/> by standing close: one within the wake
    /// distance who does not sneak. <paramref name="nearestPlayer"/> is the nearest player's position — when even that one
    /// is out of the wake distance (the common case) nobody is scanned.</summary>
    private bool SleeperWokenByAPlayer(List<PlayerSession> players, Vector3f sleeper, Vector3f nearestPlayer)
    {
        double wakeSq = CreatureWakeDistance * CreatureWakeDistance;
        if (WrapDistSq(sleeper, nearestPlayer) > wakeSq)
        {
            return false;
        }

        foreach (var s in players)
        {
            if (!Sneaks(s.State) && WrapDistSq(sleeper, s.State.Position) <= wakeSq)
            {
                return true;
            }
        }

        return false;
    }
}
