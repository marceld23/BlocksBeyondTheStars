// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// On-foot PvP (#2972): the other players the crosshair and the auto-aim may pick — only those the server named in its
    /// <see cref="GameBootstrap.PvpTargets"/> list (the world's PvP rules, alliances, duels and every guard applied), and
    /// only while their avatar is drawn here. The attack goes out as <c>player:&lt;id&gt;</c>; the server checks it again.
    /// </summary>
    public sealed partial class PlayerController
    {
        private readonly List<(string Id, Vector3 Pos)> _pvpScratch = new List<(string Id, Vector3 Pos)>();

        /// <summary>The hittable players' target ids and feet positions (scene space), refilled per call.</summary>
        private List<(string Id, Vector3 Pos)> PvpTargetsInScene()
        {
            _pvpScratch.Clear();
            if (Game == null || Remotes == null || Game.PvpTargets.Count == 0)
            {
                return _pvpScratch;
            }

            foreach (var playerId in Game.PvpTargets)
            {
                if (Remotes.TryGetPosition(playerId, out var feet))
                {
                    _pvpScratch.Add((PvpRules.TargetId(playerId), feet));
                }
            }

            return _pvpScratch;
        }

        /// <summary>The crosshair's ray against the hittable players (a sphere around the body, like the machines') — a nearer
        /// hit replaces <paramref name="id"/>.</summary>
        private void AimPlayers(Vector3 o, Vector3 dir, ref float best, ref string id, ref Vector3 pos)
        {
            foreach (var (pid, feet) in PvpTargetsInScene())
            {
                if (RayHitsSphere(o, dir, feet + Vector3.up * 0.9f, 0.8f, out float d) && d < best)
                {
                    best = d;
                    id = pid;
                    pos = feet;
                }
            }
        }
    }
}
