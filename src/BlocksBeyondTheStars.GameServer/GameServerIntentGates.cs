// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The intent filters <c>Dispatch</c> applies before any handler runs, for the states in which a player
/// may still talk to the server but must not act in the world: the held pause (#995) and the body that waits for the
/// respawn choice (#3370). The observer filter lives with observer mode (GameServerObserver.cs).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Messages still served while the world is held paused (#995): the resume path itself, the pause
    /// menu's own "Back to my ship" (#2286), chat and voice (players coordinating the resume), diagnostics,
    /// explicit saves, harmless UI state, read-only requests and admin commands. Everything else — movement,
    /// mining, building, crafting, combat, trading — would mutate a world whose simulation (threats, hunger,
    /// clock) is frozen.</summary>
    private static bool PausedMayHandle(object message) => message switch
    {
        PauseIntent or ChatIntent or VoiceFrame or BumpReport or SaveGameIntent
            or SelectHotbarIntent or AdminCommandIntent => true,
        // #2286: sent FROM the pause menu — in singleplayer that menu is exactly what holds the world, so the
        // intent would never get through otherwise. Every gate (on foot, own ship here, no fight, not falling,
        // cooldown) is still checked in ReturnToShipRefusal; the snap itself moves nothing but the player.
        ReturnToShipIntent => true,
        RequestStarMap or RequestMissions or RequestCompanionsIntent
            or RequestAllianceListIntent or RequestLandingPadsIntent or FarTerrainTileRequest
            or RequestBaseResidentsIntent => true, // reads (#1821: the far view keeps filling in; #2717: the core's resident list)
        // The crew/marker envelopes (#1216/#1217) carry reads AND writes — only the read passes a pause.
        CrewActionIntent c => c.Kind == "list",
        MarkerActionIntent m => m.Kind == "list",
        YieldIntent y => y.Action == YieldIntent.ActionStatus, // #2999: the core screen reads its yield store (a collect waits)
        // #2462 (B12): preferences, the tutorial and the story — sent from the Settings (which the pause menu opens),
        // the Tab menu's Story tab and admin tools. None moves the frozen world: they change this player's own
        // ledger or re-send state. SetNpcCallsIntent used to be dropped here, so a call preference changed from the
        // pause menu only took effect at the next join.
        SetNpcCallsIntent or SkipOnboardingIntent or RequestStoryResolutionIntent or StorySelectIntent
            or SetPlaceNameIntent => true, // #2952: a private place name only changes the player's own ledger
        SetFestivalPrefsIntent or OpenCalendarDoorIntent => true, // #3021 "My festivals"; #3315 a calendar door (the in-game menu) only fills the opener's own bag
        SetBasePadIntent => true, // #3069: a pad's name and access — its dialog may hold a singleplayer world; it moves nothing
        // #2466 (synthesis §5.7): the pack controls change only the player's own ledger — "skipPack" is the first tutorial
        // card's "I know the game", shown during a singleplayer hold; #2467 watching a scene again changes nothing; #2470
        // "Start the story now" starts the player's own story track. #2471 (issue: "every action is pause-whitelisted"): the
        // Journal can be reached from the pause menu's settings, so starting, skipping, restarting or replaying a chapter or a
        // step, and the Journal's state request, are served too — their lines and cards wait for the client like any other;
        // the frozen world itself does not move. Each action is validated in HandleStoryPackIntent.
        StoryPackIntent => true,
        // #2467 (synthesis §5.7): the cutscene director confirms a scene while its own singleplayer hold is still on — the
        // receipt only stamps the player's own seen ledger.
        CutsceneSeenIntent => true,
        // #2469 (synthesis §5.7): the tutorial's UI teaching flags come from the pause menu too ("Codex" there, VEGA's
        // continue while the menu holds the world) — a flag only ticks off the sender's own lesson.
        UiTutorialEvent => true,
        // #2478: the duel dialogue holds a singleplayer world while the core waits for an answer (like any scene that takes
        // the camera) — the answer itself must get through. It moves the duel and, at the last node, the story (the win
        // pacifies the machines); nothing of the frozen simulation ticks with it.
        CoreDialogueChoiceIntent => true,
        // #2616: closing an NPC's dialogue panel (the leave) — Esc closes it while the pause menu opens and holds the world.
        // It only ends the sender's own conversation and lets the NPC go back to its day; a real reply still waits.
        NpcDialogChoiceIntent d => d.ChoiceIndex < 0,
        // #2672: the look — colours, face, body paint, tool looks. They only change the sender's own appearance (saved and
        // relayed, rate-limited by the appearance bucket). A player joining while the host alone holds the world sends them
        // in the same poll as the join, before the next tick lifts the hold — dropping them left the newcomer in the
        // default colours with no face or paint for everyone.
        SetAppearanceIntent or SetFaceIntent or SetBodyPaintIntent or SetToolLookIntent => true,
        _ => false,
    };

    /// <summary>#3370: what a body waiting for the respawn choice (#462) may still send. It lies dead at the death spot at
    /// 0 HP for up to 30 s; the server used to ignore only its moves and its shots, so it could mine, build, open doors,
    /// eat, loot and craft the whole time — and a stock client sent exactly that, because the death screen left the
    /// on-foot controls live (#3369). Served: the answer itself, the moves (<see cref="HandleMove"/> ignores the pose; they
    /// are the client's keep-alive, #964), the off-switches a dying client sends while it folds its gear (a jetpack flag
    /// left on would read as thrusting after the respawn), leaving a trade, and the control plane the held pause keeps
    /// (chat, the F1 report, saves, reads, settings, the player's own ledger and look). Everything else is dropped
    /// silently — the client hides the affordances under the death screen.</summary>
    private static bool ChoicePendingMayHandle(object message) => message switch
    {
        RespawnChoiceIntent or MoveIntent => true,
        SetJetpackIntent jetpack => !jetpack.Active,
        SetGlidingIntent glide => !glide.Active,
        SetSeatedIntent seat => !seat.Active,
        TradeCancelIntent => true,
        _ => PausedMayHandle(message),
    };

    /// <summary>Exposes the respawn-choice intent filter (#3370) so a test can pin what a dead body may still do.</summary>
    public static bool ChoicePendingMayHandleForTest(object message) => ChoicePendingMayHandle(message);
}
