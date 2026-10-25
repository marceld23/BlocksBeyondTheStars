// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Duels (#2972) at the top of the Alliances tab's roster: a waiting challenge (accept / decline), the running duel (give
    /// up), or — while the world allows any PvP — the online players to challenge. A duel is the one fight two players both
    /// agreed to: in a "duels only" world it is the only one, and it never kills (the loser gets back up).
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        /// <summary>Draws the duel section from <paramref name="y"/> and returns where the roster continues. Nothing at all
        /// while the world's PvP is off (or the server predates it).</summary>
        private float BuildDuelSection(float y)
        {
            var rules = Game?.Rules;
            if (rules == null || string.IsNullOrEmpty(rules.FriendlyFire) || rules.Pvp == "Off" || string.IsNullOrEmpty(rules.Pvp))
            {
                return y;
            }

            y = AllianceSection(L("ui.duel.title"), y);
            string from = Game.DuelChallengeFrom;
            string partner = Game.DuelPartner;
            if (!string.IsNullOrEmpty(partner))
            {
                UiKit.AddText(_listContent, 8, y + 8, 420, 40, L("ui.duel.running").Replace("{name}", partner), 20, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.AddButton(_listContent, 600, y + 6, 150, 44, L("ui.duel.give_up"), () => Game.Network?.SendDuel("end"));
                return y + 68f;
            }

            if (!string.IsNullOrEmpty(from))
            {
                UiKit.AddText(_listContent, 8, y + 8, 420, 40, L("ui.duel.request_from").Replace("{name}", from), 20, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                var acc = UiKit.AddButton(_listContent, 440, y + 6, 150, 44, L("ui.alliance.accept"), () =>
                {
                    Game.Network?.SendDuel("accept", from);
                    Game.ClearDuelChallenge();
                });
                acc.GetComponent<Image>().color = new Color(0.2f, 0.5f, 0.36f);
                UiKit.AddButton(_listContent, 600, y + 6, 150, 44, L("ui.alliance.decline"), () =>
                {
                    Game.Network?.SendDuel("decline", from);
                    Game.ClearDuelChallenge();
                });
                return y + 68f;
            }

            string me = Game.LocalPlayerId ?? string.Empty;
            var seen = new HashSet<string>();
            foreach (var p in Game.StarMap?.Players ?? System.Array.Empty<NetPlayerLocation>())
            {
                string id = p.Name; // player id == name
                if (string.IsNullOrEmpty(id) || id == me || !seen.Add(id))
                {
                    continue;
                }

                UiKit.AddText(_listContent, 8, y + 8, 420, 40, id, 20, UiKit.TextCol, TextAnchor.MiddleLeft);
                var btn = UiKit.AddButton(_listContent, 560, y + 6, 190, 44, L("ui.duel.challenge"), () => Game.Network?.SendDuel("challenge", id));
                btn.GetComponent<Image>().color = new Color(0.55f, 0.32f, 0.18f);
                y += 58f;
            }

            if (seen.Count == 0)
            {
                UiKit.AddText(_listContent, 8, y, 760, 30, L("ui.duel.nobody"), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                y += 40f;
            }

            return y + 10f;
        }

        /// <summary>What an open Alliances view is built from, so it rebuilds when that changes: the crew's roster on the Crew
        /// view (#1216), the duel state and the world's PvP mode on the roster (#2972).</summary>
        private string AlliancesLiveStamp()
        {
            if (_category == "crew")
            {
                var c = Game?.Crew;
                return c == null ? string.Empty
                    : c.CrewId + "|" + c.Name + "|" + c.OwnerId + "|" + (c.Members?.Length ?? 0) + "|" + (c.Invites?.Length ?? 0);
            }

            return Game == null ? string.Empty : "duel|" + Game.DuelVersion + "|" + Game.Rules?.Pvp;
        }
    }
}
