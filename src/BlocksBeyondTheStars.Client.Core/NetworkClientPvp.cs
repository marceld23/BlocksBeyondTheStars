// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>On-foot PvP (#2972): whom the player may hit, duels, and the admin's live switches.</summary>
    public sealed partial class NetworkClient
    {
        /// <summary>The players this player may hit right now (sent when the list changes).</summary>
        public event Action<PvpTargets>? PvpTargetsReceived;

        /// <summary>A duel changed: a challenge, its start, a refusal, its end.</summary>
        public event Action<DuelNotice>? DuelNoticeReceived;

        /// <summary>A duel action — "challenge", "accept", "decline" (the other player's id) or "end".</summary>
        public void SendDuel(string action, string targetPlayerId = "")
            => Send(new DuelIntent { Action = action ?? string.Empty, TargetPlayerId = targetPlayerId ?? string.Empty });

        /// <summary>World admin: the PvP mode ("Off"/"DuelsOnly"/"GroupBased"/"On") and friendly fire ("On"/"Off"); empty =
        /// unchanged.</summary>
        public void SendSetPvpRules(string pvp = "", string friendlyFire = "")
            => Send(new SetWorldRulesIntent { Pvp = pvp ?? string.Empty, FriendlyFire = friendlyFire ?? string.Empty, MachineCatchUpMinutes = -1 });
    }
}
