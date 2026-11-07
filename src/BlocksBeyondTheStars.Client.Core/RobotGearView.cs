// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3398/#3399 — the robot's gear on the client: whether the held item is something the robot takes (the E prompt then says
    /// "Give … to Robo" instead of "Talk to Robo"), what the Companions tab lists with "Take back", and what the robot's body
    /// shows (the weapon in its hand, the jetpack on its back, the flame while it flies). The rules are
    /// <see cref="RobotGear"/> (Shared), the same the server applies. Unity-free so the headless suite covers it.
    /// </summary>
    public static class RobotGearView
    {
        /// <summary>The piece of gear the held item is (<see cref="RobotGear.WeaponPiece"/> / <see cref="RobotGear.JetpackPiece"/>),
        /// or empty — then E at the robot talks.</summary>
        public static string GiftPiece(GameContent? content, string? heldItemKey)
            => content == null || string.IsNullOrEmpty(heldItemKey) ? string.Empty : RobotGear.PieceOf(content.GetItem(heldItemKey!));

        /// <summary>Whether the Companions tab shows a gear row for this companion (the robot — no other companion takes gear).</summary>
        public static bool HasGearRow(NetCompanion? c) => c != null && CompanionSelection.IsRobot(c);

        /// <summary>The robot's pieces of gear, weapon first: (piece, item key) for each it carries.</summary>
        public static List<(string Piece, string Item)> Pieces(NetCompanion? c)
        {
            var pieces = new List<(string, string)>();
            if (c == null)
            {
                return pieces;
            }

            if (!string.IsNullOrEmpty(c.GearWeapon))
            {
                pieces.Add((RobotGear.WeaponPiece, c.GearWeapon));
            }

            if (!string.IsNullOrEmpty(c.GearJetpack))
            {
                pieces.Add((RobotGear.JetpackPiece, c.GearJetpack));
            }

            return pieces;
        }

        /// <summary>Whether a creature on the wire wears a jetpack.</summary>
        public static bool WearsJetpack(NetCreature? c) => c != null && (c.GearFlags & RobotGear.FlagJetpack) != 0;

        /// <summary>Whether a creature on the wire flies on its jetpack right now (the flame burns).</summary>
        public static bool Flying(NetCreature? c) => c != null && (c.GearFlags & RobotGear.FlagFlying) != 0;
    }
}
