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
        Register(325, typeof(MelodyNoteIntent));             // Client -> Server (#3346 a tone of the melody bar, or -1: leave)
        Register(326, typeof(MelodyPuzzleState));            // Server -> Client (#3346 the melody bar: open, progress, prompt, result)
        Register(327, typeof(SetCompanionFollowIntent));     // Client -> Server (#3344 who follows, who stays home or on board)
        Register(328, typeof(BlueprintVisibility));          // Server -> Client (#3350 the hidden blueprints this player may see)
        Register(330, typeof(StoryMinigameStart));           // Server -> Client (#3352 the note-key minigame for a Song Disc)
        Register(331, typeof(StoryMinigameResultIntent));    // Client -> Server (#3352 its answers, or a skip)

        // #3394 the talking robot: E at the own robot companion. No protocol bump — an older server drops it (the robot stays
        // silent); the answer is an ordinary ShipAiLine with the additive SpeakerName.
        Register(332, typeof(TalkToCompanionIntent));        // Client -> Server

        // #3398/#3399 the robot's gear: a weapon and a jetpack given with E, taken back in the Companions tab. No protocol bump —
        // an older server drops the intents (the item stays with the player); the state rides on additive fields.
        Register(333, typeof(GiveCompanionGearIntent));      // Client -> Server
        Register(334, typeof(TakeCompanionGearIntent));      // Client -> Server

        // #3494/#3497 the landmarks: the far silhouettes and the sound emitters of the current world. No protocol bump — an
        // older client drops it (no silhouette, no emitter sound); the blocks stream as chunks either way.
        Register(335, typeof(LandmarkList));                 // Server -> Client

        RegisterAdminViewMessages();
    }

    /// <summary>#3502 the in-game admin view (ids 336–347): admin mode, the roster, the event log, the direct chat and the world's
    /// reports. No protocol bump — an older server drops the intents (no admin view), an older client the state. The next
    /// message after these takes 348.</summary>
    private static void RegisterAdminViewMessages()
    {
        Register(336, typeof(AdminModeIntent));              // Client -> Server (#3503 enter / leave admin mode)
        Register(337, typeof(AdminPanelIntent));             // Client -> Server (#3505/#3506 the panel opened / closed)
        Register(338, typeof(AdminRoster));                  // Server -> Client (#3505 every online player, 1 Hz)
        Register(339, typeof(AdminJumpIntent));              // Client -> Server (#3505 jump to a player or a place)
        Register(340, typeof(AdminEventBacklog));            // Server -> Client (#3506 the log when the panel opens)
        Register(341, typeof(AdminEvent));                   // Server -> Client (#3506 one live event)
        Register(342, typeof(DirectChatIntent));             // Client -> Server (#3507 a direct line)
        Register(343, typeof(DirectChatMessage));            // Server -> Client (#3507)
        Register(344, typeof(AdminReportList));              // Server -> Client (#3508 the world's reports)
        Register(345, typeof(AdminReportAction));            // Client -> Server (#3508 done / open again)
        Register(346, typeof(CallAdminIntent));              // Client -> Server (#3508 "Call an admin")
        Register(347, typeof(PlayerReportIntent));           // Client -> Server (#3508 a player report, also the portal mirror)
        Register(348, typeof(GuardianGiantStatus));          // Server -> Client (#3676 a guardian giant fight: move, aim, boss bar)

        // The feel pass: a player's emotes and the body states other players see (a flinch, the fall, the rise). No
        // protocol bump — an older server drops the intent (the emote shows on the sender only), an older client the
        // broadcast. The next message after these takes 351.
        Register(349, typeof(PlayerEmoteIntent));            // Client -> Server
        Register(350, typeof(PlayerEmote));                  // Server -> Client

        // #3753/#3754/#3756 space combat that moves: the barrel roll and the boost, and a hostile's heavy shot. No
        // protocol bump — an older server drops the intent (the roll is a picture only, the boost is not checked), an
        // older client drops the heavy shot (it still takes the damage). The next message after these takes 353.
        Register(351, typeof(ShipManeuverIntent));           // Client -> Server
        Register(352, typeof(SpaceHeavyShot));               // Server -> Client

        // #3766 a block carried over the head and thrown. No protocol bump — an older server drops both intents (the
        // block is never lifted), and the flight reaches an older client as a WorldFx kind it ignores. The next
        // message after these takes 355.
        Register(353, typeof(CarryBlockIntent));             // Client -> Server
        Register(354, typeof(ThrowBlockIntent));             // Client -> Server

        // The Test Pilot page: a playtest task's steps the game can recognise. The client names the game events its task
        // waits for, the server reports the ones the sender causes. No protocol bump — an older server drops the intent
        // (no ack comes, the player ticks those steps off by hand), an older client never asks. The next message after
        // these takes 358.
        Register(355, typeof(PlaytestWatchIntent));          // Client -> Server
        Register(356, typeof(PlaytestWatchAck));             // Server -> Client
        Register(357, typeof(PlaytestEvent));                // Server -> Client

        // The lively places: the inhabited places of the current world (bounds, colour, door signs, effect points, market
        // day) and an NPC's gesture without a line. No protocol bump — an older client drops both (no place greeting, no
        // signs, no place sound; the gesture is simply not played).
        Register(358, typeof(PlaceList));                    // Server -> Client
        Register(359, typeof(NpcEmote));                     // Server -> Client

        // #3870: a sample handed to a research station's archive. An older server drops it (the client offers the button
        // only aboard a research station, which an older server never makes). The next message after this takes 361.
        Register(360, typeof(GiveSampleToStationIntent));    // Client -> Server
    }
}
