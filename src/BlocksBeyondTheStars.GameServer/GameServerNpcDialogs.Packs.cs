// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Story-pack dialogs (#3347, the Sounders story pack): a reply may be offered only under a condition
/// (<see cref="DialogChoice.VisibleIf"/>), a reply may be a sung melody (<see cref="DialogChoice.Melody"/> — the client shows
/// note symbols and plays it), a pack's reply may set a milestone of its own namespace (<c>milestone:&lt;key&gt;</c>), and a
/// dialog that reaches its end raises <see cref="GameEventKeys.DialogDone"/> with the dialog's key, so a story step can wait
/// for a conversation.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The pack of the dialog whose consequence runs (set right before <c>ApplyDialogConsequence</c>).</summary>
    private StoryDefinition? _dialogConsequencePack;

    /// <summary>The replies of <paramref name="node"/> the player is offered, as indexes into its choices: every reply whose
    /// <see cref="DialogChoice.VisibleIf"/> holds for them.</summary>
    private int[] ShownChoices(PlayerSession session, DialogNode node)
    {
        var shown = new List<int>(node.Choices.Count);
        for (int i = 0; i < node.Choices.Count; i++)
        {
            var c = node.Choices[i];
            if (c.VisibleIf is not { Count: > 0 } || ConditionTextsHold(c.VisibleIf, session))
            {
                shown.Add(i);
            }
        }

        return shown.ToArray();
    }

    /// <summary>The choice index of the node behind the client's reply number (the client numbers the replies it was shown),
    /// or -1 when the number names none.</summary>
    private static int ShownChoiceIndex(NpcConversation c, int clientIndex)
    {
        if (clientIndex < 0)
        {
            return -1;
        }

        if (c.Shown is null)
        {
            return clientIndex;
        }

        return clientIndex < c.Shown.Length ? c.Shown[clientIndex] : -1;
    }

    /// <summary>One step of a dialog for the client: the line, the shown replies resolved, their melodies (only when one is
    /// sung) and whether it is the last line.</summary>
    private NpcDialogState DialogStepState(PlayerSession session, ServerNpc npc, string text, DialogNode node, int[] shown)
    {
        var replies = shown.Select(i => node.Choices[i]).ToList();
        return new NpcDialogState
        {
            NpcId = npc.Id,
            Name = npc.Name,
            Text = text,
            Choices = replies.Select(c => Localize(session.Locale, c.TextKey)).ToArray(),
            ChoiceMelodies = replies.Any(c => !string.IsNullOrWhiteSpace(c.Melody))
                ? replies.Select(c => (c.Melody ?? string.Empty).Trim().ToUpperInvariant()).ToArray()
                : Array.Empty<string>(),
            End = shown.Length == 0,
        };
    }

    /// <summary>#3347: the dialog reached an end node (a reply without a follow-up, or a line without replies) —
    /// <see cref="GameEventKeys.DialogDone"/> with the dialog's key, for a story step that waits for the conversation.</summary>
    private void DialogReachedEnd(PlayerSession session, DialogDefinition dialog)
        => RaiseGameEvent(session, GameEventKeys.DialogDone, dialog.Key ?? string.Empty);

    // ---------------- Test seams ----------------

    /// <summary>Test seam: the reply texts and melodies the player's open conversation shows now (null = none open).</summary>
    internal (string[] Choices, string[] Melodies)? ShownDialogChoicesForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is not { Npc.Conversation: { } c } s
            || c.Node < 0 || c.Node >= c.Dialog.Nodes.Count)
        {
            return null;
        }

        var node = c.Dialog.Nodes[c.Node];
        var state = DialogStepState(s, c.Npc, string.Empty, node, c.Shown ?? Enumerable.Range(0, node.Choices.Count).ToArray());
        return (state.Choices, state.ChoiceMelodies);
    }
}
