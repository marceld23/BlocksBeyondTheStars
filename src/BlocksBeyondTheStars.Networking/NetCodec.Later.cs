// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Networking;

/// <summary>The message registry, continued: the ids from 307 on, split off the static constructor in
/// <c>NetCodec.cs</c> when the file reached 1000 lines (#3069). Ids stay append-only — a new message takes the next free id
/// here.</summary>
public static partial class NetCodec
{
    private static void RegisterLaterMessages()
    {
        // #2972 on-foot PvP: who may be hit, and duels. No protocol bump — an older peer drops them (PvP stays off for it).
        Register(307, typeof(PvpTargets));                   // Server -> Client
        Register(308, typeof(DuelIntent));                   // Client -> Server
        Register(309, typeof(DuelNotice));                   // Server -> Client

        // #2994/#2995 teleporter destinations (bases, pads, beacons, markers, coordinates) and #2999 the base/station yield.
        // No protocol bump — an older peer drops them.
        Register(310, typeof(TeleportToPointIntent));        // Client -> Server
        Register(311, typeof(YieldIntent));                  // Client -> Server
        Register(312, typeof(YieldStatus));                  // Server -> Client
        Register(313, typeof(SetFestivalPrefsIntent));       // Client -> Server (#3021 "My festivals"; an older server drops it)
        Register(314, typeof(SetBasePadIntent));             // Client -> Server (#3069 base landing pads: name + access)
    }
}
