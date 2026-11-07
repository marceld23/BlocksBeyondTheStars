// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>#3394: talking to the own robot companion; #3398/#3399: giving it gear and taking it back.</summary>
    public sealed partial class NetworkClient
    {
        /// <summary>E at the own robot companion (<paramref name="companionId"/> from the roster; empty = the sender's robot) — it
        /// answers in VEGA's panel under its own name.</summary>
        public void SendTalkToCompanion(string companionId) => Send(new TalkToCompanionIntent { CompanionId = companionId ?? string.Empty });

        /// <summary>#3398/#3399: E at the own robot with a weapon or a jetpack in hotbar <paramref name="slot"/> — one of it goes
        /// to the robot.</summary>
        public void SendGiveCompanionGear(string companionId, int slot)
            => Send(new GiveCompanionGearIntent { CompanionId = companionId ?? string.Empty, Slot = slot });

        /// <summary>#3398/#3399: "Take back" in the Companions tab — the robot's <paramref name="piece"/> ("weapon" / "jetpack").</summary>
        public void SendTakeCompanionGear(string companionId, string piece)
            => Send(new TakeCompanionGearIntent { CompanionId = companionId ?? string.Empty, Piece = piece ?? string.Empty });
    }
}
