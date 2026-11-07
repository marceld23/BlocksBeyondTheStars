// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3398/#3399 — the hunter robot's gear in the Companions tab: under the robot's row, the weapon and the jetpack it carries,
    /// each with "Take back" (the server refuses while the backpack is full), or a hint how to give it some (hold the item and
    /// press E at the robot). Which companion has the row and which pieces it lists is <see cref="RobotGearView"/> (Client.Core).
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        /// <summary>The extra height of a companion row with a gear row (the robot).</summary>
        private const float CompanionGearRowExtra = 54f;

        /// <summary>The extra height the gear row adds under a companion (0 for every companion but the robot).</summary>
        private static float CompanionGearRowHeight(NetCompanion c) => RobotGearView.HasGearRow(c) ? CompanionGearRowExtra : 0f;

        /// <summary>The robot's gear row at <paramref name="y"/>: "Gear:" and per piece its name and "Take back".</summary>
        private void BuildCompanionGearRow(NetCompanion c, float y)
        {
            if (!RobotGearView.HasGearRow(c))
            {
                return;
            }

            UiKit.AddText(_listContent, 28, y, 110, 44, L("ui.companions.gear"), 16, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            var pieces = RobotGearView.Pieces(c);
            if (pieces.Count == 0)
            {
                UiKit.AddText(_listContent, 140, y, 600, 44, L("ui.companions.gear_none"), 13, UiKit.CyanDim, TextAnchor.MiddleLeft);
                return;
            }

            string id = c.Id;
            float x = 140f;
            foreach (var (piece, item) in pieces)
            {
                var def = Game.Content?.GetItem(item);
                string name = def != null ? L(def.NameKey) : item;
                UiKit.AddText(_listContent, x, y, 170, 44, name, 15, UiKit.Cyan, TextAnchor.MiddleLeft);
                string which = piece;
                UiKit.AddButton(_listContent, x + 172, y + 2, 120, 40, L("ui.companions.take_back"),
                    () => Game.Network?.SendTakeCompanionGear(id, which));
                x += 302f;
            }
        }
    }
}
