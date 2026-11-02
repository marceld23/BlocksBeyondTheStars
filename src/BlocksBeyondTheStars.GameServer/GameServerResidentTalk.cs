// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3089 — talking to the people at the player's base (Justus 2026-11-01: "in my base I can't interact with the people who
/// work there!"). Every base job has lines of its own (<c>data/dialogs.json</c>: the posts' roles and professions, the guard,
/// the gardener, the craftsman, the lumberjack), and a resident without a post in a base with a focus talks about that
/// focus (<see cref="DialogJobOf"/>: the Miner's shaft, the Market's merchants, the Radio centre's calls). The
/// quartermaster's "Show me the jobs" answer opens the Missions tab (<see cref="MissionsConsequence"/>).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The dialogue consequence (and the <see cref="NpcDialogState.Action"/> it sends) that opens the client's
    /// Missions tab after the closing line — the quartermaster's "Show me the jobs".</summary>
    internal const string MissionsConsequence = NpcDialogState.ActionMissions;

    /// <summary>The prefix of the dialogue job a resident without a post talks under in a base with a focus.</summary>
    internal const string FocusDialogJobPrefix = "focus_";

    /// <summary>The job a dialogue is matched against (<see cref="DialogDefinition.Job"/>): the NPC's own job — except a base
    /// resident without a post in a base whose focus brings no job of its own (Miner, Market, Radio centre), who talks
    /// about the focus (<c>focus_miner</c>, <c>focus_market</c>, <c>focus_radio</c>). A Lumberjack base's woodcutter has a
    /// job ("lumberjack") and its own lines.</summary>
    private string DialogJobOf(ServerNpc npc)
    {
        if (npc.Job.Length > 0 || npc.BaseId <= 0 || _bases.FirstOrDefault(b => b.Id == npc.BaseId) is not { } home)
        {
            return npc.Job;
        }

        string focus = BaseFocusOf(home);
        return focus is BaseFocus.Miner or BaseFocus.Market or BaseFocus.Radio ? FocusDialogJobPrefix + focus : npc.Job;
    }

    /// <summary>Test seam (#3089): the key of the dialogue the NPC would open for the player now, or null (the greeting).</summary>
    public string? PickedDialogKeyForTest(string playerId, int npcId)
        => FindSessionByPlayerId(playerId) is { } session && _npcs.FirstOrDefault(n => n.Id == npcId) is { } npc
            ? PickDialog(session, npc, DialogNpcKey(session, npc))?.Key
            : null;
}
