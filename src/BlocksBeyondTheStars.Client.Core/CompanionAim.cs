// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3394: the player's own robot companion under the crosshair — E talks to it ("Talk to Robo"). The same aim as a person
    /// (<see cref="NpcAim"/>): the ray against an upright body cylinder over its feet, within
    /// <see cref="InteractionReach.CompanionTalk"/> of the player, measured the short way round both seams. Only the local
    /// player's own robot counts; another player's robot shows its nameplate and nothing else.
    /// </summary>
    public static class CompanionAim
    {
        /// <summary>The robot's species id (<c>GameServer.RobotSpeciesId</c>).</summary>
        public const string RobotSpeciesId = "robot_hunter";

        /// <summary>The radius of the body the crosshair hits (a little wider than the drawn robot).</summary>
        public const float BodyRadius = 0.55f;

        /// <summary>The height of that body over the feet.</summary>
        public const float BodyHeight = 1.9f;

        /// <summary>The local player's robot whose body the ray from the eye (<paramref name="ox"/>, <paramref name="oy"/>,
        /// <paramref name="oz"/>) along the unit direction (<paramref name="dx"/>, <paramref name="dy"/>, <paramref name="dz"/>)
        /// enters within <paramref name="maxDistance"/>, while it stands within talk reach of the player at
        /// (<paramref name="px"/>, <paramref name="py"/>, <paramref name="pz"/>); null otherwise. <paramref name="distance"/> is
        /// where the ray enters the body (the caller compares it with the first solid block — a wall in front hides it).</summary>
        public static NetCreature? AimedOwnRobot(IReadOnlyList<NetCreature>? creatures, string? localPlayerId,
            float ox, float oy, float oz, float dx, float dy, float dz, float maxDistance,
            float px, float py, float pz, int circumference, out float distance)
        {
            distance = float.PositiveInfinity;
            if (creatures == null || string.IsNullOrEmpty(localPlayerId))
            {
                return null;
            }

            NetCreature? best = null;
            double reachSq = InteractionReach.CompanionTalk * (double)InteractionReach.CompanionTalk;
            for (int i = 0; i < creatures.Count; i++)
            {
                var c = creatures[i];
                if (!IsOwnRobot(c, localPlayerId)
                    || InteractReach.SeamDistanceSquared(px, py, pz, c.X, c.Y, c.Z, circumference) >= reachSq)
                {
                    continue;
                }

                double rx = circumference > 0 ? WorldConstants.WrapDeltaX(c.X - ox, circumference) : c.X - ox;
                double rz = circumference > 0 ? WorldConstants.WrapDeltaZ(c.Z - oz, circumference) : c.Z - oz;
                float t = NpcAim.RayCylinder(rx, c.Y - oy, rz, dx, dy, dz, BodyRadius, BodyHeight);
                if (t <= maxDistance && t < distance)
                {
                    distance = t;
                    best = c;
                }
            }

            return best;
        }

        /// <summary>Whether a creature is the local player's own robot companion.</summary>
        public static bool IsOwnRobot(NetCreature? c, string? localPlayerId)
            => c != null && !string.IsNullOrEmpty(localPlayerId)
               && string.Equals(c.OwnerId, localPlayerId, StringComparison.Ordinal)
               && string.Equals(c.SpeciesId, RobotSpeciesId, StringComparison.Ordinal);

        /// <summary>The name a companion answers to in a prompt ("Talk to Robo"): the name its owner gave it, else its
        /// species' name — safe for rich text.</summary>
        public static string DisplayName(NetCreature? c)
            => c == null ? string.Empty : ChatMarkup.RichSafe(string.IsNullOrWhiteSpace(c.CustomName) ? c.Name : c.CustomName);

        /// <summary>The robot's companion id from the player's roster (the one robot a player has), or empty — the server then
        /// takes the sender's robot itself.</summary>
        public static string RobotCompanionId(CompanionList? roster)
        {
            var all = roster?.Companions;
            if (all == null)
            {
                return string.Empty;
            }

            foreach (var c in all)
            {
                if (c != null && string.Equals(c.Kind, TamedCreature.RobotKind, StringComparison.Ordinal))
                {
                    return c.Id ?? string.Empty;
                }
            }

            return string.Empty;
        }
    }
}
