// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// What a player may do on the other side (#3904) — the intent filter <c>Dispatch</c> applies while
/// <see cref="PlayerSession.OtherSide"/> is set, beside the observer's, the fallen's, the pause's and the dead body's.
/// It is an <b>allow-list</b>: an intent that is not named here — every future one included — is refused. Over there a
/// player moves, talks, touches the rune stones, talks to the Echoes, uses the Resonance Blaster and the melody bar;
/// the control plane (pause, reports, settings, the Journal, reads) stays live. A refused action is answered with one
/// system line, at most once every <see cref="OtherSideRefusalLineSeconds"/>; what a client sends by itself (a hard
/// landing, a crash report) is dropped without a word. Three parts:
/// <list type="bullet">
/// <item><see cref="OtherSideMayHandle"/> — the intent types (and, where the message itself decides, its kind).</item>
/// <item><see cref="OtherSideConditionHolds"/> — the four allowed intents that depend on the world: the use key only on a
/// rune stone, a talk only with somebody who lives over there, a gadget only when it is the Resonance Blaster in hand.</item>
/// <item><see cref="OtherSideAllowsMore"/> — a hook for the features built on the other side (the sound layer, the
/// story chapters), so they open what they need without touching this list.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Seconds between two refusal lines for one player.</summary>
    internal const double OtherSideRefusalLineSeconds = 4.0;

    /// <summary>The line a refused action is answered with ("Over here you can only touch the rune stones.").</summary>
    internal const string OtherSideRefusedLine = "@srv.otherside.refused";

    private readonly Dictionary<string, double> _otherSideRefusalAt = new(StringComparer.Ordinal);

    /// <summary>A feature built on the other side may allow one more intent for <paramref name="s"/> (set
    /// <paramref name="allowed"/>); it is asked only for what the list below refused. Without an implementation nothing
    /// more is allowed.</summary>
    partial void OtherSideAllowsMore(PlayerSession s, object message, ref bool allowed);

    /// <summary>The intents a player over there may send, by type. Anything unlisted is refused.</summary>
    private static bool OtherSideMayHandle(object message) => message switch
    {
        // Moving: the pose, the far view, the hand, the suit's own switches. Sitting down is refused, standing up is not.
        MoveIntent or FarTerrainTileRequest or SelectHotbarIntent => true,
        SetJetpackIntent or SetGlidingIntent or ReleaseRopeIntent or SetLampIntent => true,
        SetSeatedIntent seat => !seat.Active,

        // Talking: chat of every kind, voice, a gesture (seen only by who is over there too), the effect of an own action.
        ChatIntent or DirectChatIntent or VoiceFrame or PlayerEmoteIntent or FxIntent => true,

        // The control plane: pause, saves, diagnostics, reports, the admin's own tools, leaving a trade, a death's answer.
        PauseIntent or SaveGameIntent or BumpReport or CallAdminIntent or PlayerReportIntent => true,
        AdminCommandIntent or AdminModeIntent or AdminPanelIntent or AdminJumpIntent or AdminReportAction => true,
        TradeCancelIntent or RespawnChoiceIntent => true,

        // The look: colours, face, body paint, tool looks (relayed only to who may see the player).
        SetAppearanceIntent or SetFaceIntent or SetBodyPaintIntent or SetToolLookIntent => true,

        // Reads: the star map and the lists the menus ask for. They change nothing.
        RequestStarMap or RequestMissions or RequestCompanionsIntent or RequestAllianceListIntent => true,
        RequestLandingPadsIntent or RequestKnownNpcsIntent or RequestBaseResidentsIntent or LocateStationIntent => true,
        CrewActionIntent crew => crew.Kind == "list",
        MarkerActionIntent marker => marker.Kind == "list",
        YieldIntent yield => yield.Action == YieldIntent.ActionStatus,

        // The Journal and the stories: pack controls, scenes seen, lesson flags, the settings that are the player's own.
        StoryPackIntent or StorySelectIntent or CutsceneSeenIntent or RequestStoryResolutionIntent => true,
        UiTutorialEvent or PlaytestWatchIntent or SetNpcCallsIntent or SetFestivalPrefsIntent => true,

        // The own backpack and equipment — the disc may be taken out, coming back does not need it.
        MoveItemIntent or EquipItemIntent or UnequipItemIntent => true,

        // The Sounders' things; the first four are checked against the world (OtherSideConditionHolds).
        UseBlockIntent or TalkToNpcIntent or NpcDialogChoiceIntent or UseGadgetIntent or MelodyNoteIntent => true,
        _ => false,
    };

    /// <summary>What a client sends by itself, not because the player did something: refused over there like everything
    /// unlisted, but without the line.</summary>
    private static bool OtherSideDropsQuietly(object message)
        => message is FallDamageIntent or SpeederImpactIntent or TransitLaunchDoneIntent or NetFragmentFoundIntent;

    /// <summary>The allowed intents whose target decides: the use key only on a rune stone, a talk only with somebody who
    /// lives over there (leaving a talk always), a gadget only when it is the Resonance Blaster in the hand.</summary>
    private bool OtherSideConditionHolds(PlayerSession s, object message) => message switch
    {
        UseBlockIntent use => IsRuneStoneCell(new Vector3i(use.X, use.Y, use.Z)),
        TalkToNpcIntent talk => OtherSideNpc(talk.NpcId) is not null,
        NpcDialogChoiceIntent reply => reply.ChoiceIndex < 0 || s.Npc.Conversation is { Npc.OtherSideOnly: true },
        UseGadgetIntent gadget => gadget.GadgetKey == SoundersIds.ResonanceBlaster && HoldsResonanceBlaster(s.State),
        _ => true,
    };

    private bool IsRuneStoneCell(Vector3i cell)
        => _content.BlockById(_world.GetBlockIfLoaded(cell)) is { Key: "rune_stone" };

    private static bool HoldsResonanceBlaster(PlayerState p)
        => ItemKey.Base(HeldItemKey(p)) == SoundersIds.ResonanceBlaster;

    /// <summary>The gate itself (from <c>Dispatch</c>, with the player's world under the cursor): true lets the intent
    /// through to its handler. A refused one is answered here.</summary>
    private bool OtherSideAllows(PlayerSession s, object message)
    {
        bool allowed = OtherSideMayHandle(message) && OtherSideConditionHolds(s, message);
        if (!allowed)
        {
            OtherSideAllowsMore(s, message, ref allowed);
        }

        if (!allowed && !OtherSideDropsQuietly(message))
        {
            TellOtherSideRefusal(s);
        }

        return allowed;
    }

    /// <summary>The one line a refused action gets — paced, so a held mouse button does not fill the chat.</summary>
    private void TellOtherSideRefusal(PlayerSession s)
    {
        string id = s.State.PlayerId;
        if (_otherSideRefusalAt.TryGetValue(id, out double at) && _uptime - at < OtherSideRefusalLineSeconds)
        {
            return;
        }

        _otherSideRefusalAt[id] = _uptime;
        Send(s, new ServerMessage { Text = OtherSideRefusedLine });
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: the intent types a player over there may send (<see cref="OtherSideMayHandle"/>).</summary>
    public static bool OtherSideMayHandleForTest(object message) => OtherSideMayHandle(message);

    /// <summary>Test seam: what is refused over there without the line.</summary>
    public static bool OtherSideDropsQuietlyForTest(object message) => OtherSideDropsQuietly(message);

    /// <summary>Test seam: the whole gate for one intent of this player, with their world under the cursor.</summary>
    public bool OtherSideAllowsForTest(PlayerSession s, object message)
    {
        Serve(s);
        return OtherSideAllows(s, message);
    }

    /// <summary>Test seam: the refusal line may be said again at once.</summary>
    public void ResetOtherSideRefusalPaceForTest(PlayerSession s) => _otherSideRefusalAt.Remove(s.State.PlayerId);
}
