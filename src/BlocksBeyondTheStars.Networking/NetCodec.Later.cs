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

        // #3099 the storage bag screen. No protocol bump — an older server drops the intents (the bag stays shut), an older
        // client the contents.
        Register(315, typeof(OpenBagIntent));                // Client -> Server
        Register(316, typeof(MoveBagItemIntent));            // Client -> Server
        Register(317, typeof(BagContents));                  // Server -> Client

        // #3245 distant fauna: the species of the world just entered, once per world entry. No protocol bump — an older
        // client drops the unknown tag and simply hears no distant calls.
        Register(318, typeof(FaunaRoster));                  // Server -> Client

        // #3292 the flower set's trader discount, pushed when it changes. No protocol bump — an older client drops the tag
        // and shows the full price.
        Register(319, typeof(TraderDiscountInfo));           // Server -> Client

        // #3315 festival day calendars (the Advent calendar) and the cosmetics their doors unlock. No protocol bump — an
        // older server drops the intent, an older client the state (it simply shows no calendar).
        Register(320, typeof(OpenCalendarDoorIntent));       // Client -> Server
        Register(321, typeof(FestivalCalendarState));        // Server -> Client
        Register(322, typeof(CosmeticUnlocks));              // Server -> Client

        // The Sounders story pack (#3368). No protocol bump — an older server drops the intents, an older client the rest.
        Register(323, typeof(UseBlockIntent));               // Client -> Server (#3345 rune stones, puzzle pillars, the Song Spindle)
        Register(324, typeof(NoteSequence));                 // Server -> Client (#3345 a melody at a place)
    }
}
