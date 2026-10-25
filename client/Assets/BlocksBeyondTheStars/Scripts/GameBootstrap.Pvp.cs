// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// On-foot PvP (#2972) on the client: whom the server lets this player hit (<see cref="PvpTargets"/> — the only avatars
    /// the crosshair and auto-aim may pick) and the duel state the Alliances tab shows. Everything is decided by the
    /// server; this only mirrors it.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>Player ids this player may hit right now.</summary>
        public readonly HashSet<string> PvpTargets = new HashSet<string>();

        /// <summary>Who challenged this player to a duel and is waiting for the answer (empty = nobody).</summary>
        public string DuelChallengeFrom { get; private set; } = string.Empty;

        /// <summary>The player this one is fighting a duel with (empty = no duel).</summary>
        public string DuelPartner { get; private set; } = string.Empty;

        /// <summary>Bumped on every duel change, so an open Alliances tab rebuilds.</summary>
        public int DuelVersion { get; private set; }

        /// <summary>The PvP subscriptions; called from <see cref="WireKnowledge"/>.</summary>
        private void WirePvp()
        {
            Network.PvpTargetsReceived += m =>
            {
                PvpTargets.Clear();
                foreach (var id in m.PlayerIds ?? System.Array.Empty<string>())
                {
                    PvpTargets.Add(id);
                }
            };
            Network.DuelNoticeReceived += m =>
            {
                DuelVersion++;
                string other = m.Other ?? string.Empty;
                switch (m.Kind)
                {
                    case "request":
                        DuelChallengeFrom = other;
                        ShowMessage(DuelText("ui.duel.request_from", other));
                        break;
                    case "started":
                        DuelChallengeFrom = string.Empty;
                        DuelPartner = other;
                        ShowMessage(DuelText("ui.duel.started", other));
                        break;
                    case "declined":
                        ShowMessage(DuelText("ui.duel.declined", other));
                        break;
                    case "ended":
                        DuelPartner = string.Empty;
                        ShowMessage(string.IsNullOrEmpty(m.Winner) ? DuelText("ui.duel.ended", other) : DuelText("ui.duel.won_by", m.Winner));
                        break;
                }
            };
        }

        private string DuelText(string key, string name) => (Localizer?.Get(key) ?? key).Replace("{name}", name);

        /// <summary>The answer to a waiting challenge was sent — the row goes away at once.</summary>
        public void ClearDuelChallenge()
        {
            DuelChallengeFrom = string.Empty;
            DuelVersion++;
        }
    }
}
