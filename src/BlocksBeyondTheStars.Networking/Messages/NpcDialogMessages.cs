// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>Client → server (#1127): the player talks to an NPC (press E near them, outside any station
/// block's reach). The server answers with <see cref="NpcDialogState"/> when a dialogue applies, or with
/// the ordinary greeting bubble when none does.</summary>
public sealed class TalkToNpcIntent
{
    public int NpcId { get; set; }
}

/// <summary>Server → client (#1127): one step of an NPC dialogue — the NPC's line plus the player's reply
/// options. All text arrives RESOLVED in the player's locale (dialogue is per-player content: stage gates,
/// once-flags and authored characters make it non-shareable, unlike the cached greeting lines). Empty
/// <see cref="Choices"/> + <see cref="End"/> = a closing line; the client shows it and offers only "Leave".</summary>
public sealed class NpcDialogState
{
    public int NpcId { get; set; }

    /// <summary>The speaker's display name (authored characters carry their fixed name).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The NPC's current line (prompt or response), resolved.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The player's reply options, resolved. Empty when the dialogue is over.</summary>
    public string[] Choices { get; set; } = System.Array.Empty<string>();

    /// <summary>True when this is the dialogue's last line — the panel closes after showing it.</summary>
    public bool End { get; set; }

    /// <summary>What the client does after showing this line (2026-09 professions): "photo" = take a photo with the
    /// streamer, "interview" = open the interview box for the reporter; "" = nothing. Additive contractless field.
    /// #2616: <see cref="ActionClose"/> = the conversation is over without a last line (the player walked away, changed
    /// world, the NPC is gone, or a new talk replaced it) — the client closes the panel and sends no leave. Sent only to a
    /// client announcing <see cref="ClientFeatures.DialogClose1"/>: an older panel would pop open again to show it.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>#2616: the <see cref="Action"/> that closes the panel (see there).</summary>
    public const string ActionClose = "close";
}

/// <summary>Client → server (2026-09): the player's answer to a reporter's interview. Only accepted right after that
/// reporter asked (the server remembers the pending interview); screened like chat, stored as the place's news.</summary>
public sealed class InterviewAnswerIntent
{
    public int NpcId { get; set; }

    public string Text { get; set; } = string.Empty;
}

/// <summary>Client → server (#1127): the player picks a reply in the active dialogue. The server owns the
/// walk — it validates the index against ITS current node, applies the choice's consequence and answers
/// with the next <see cref="NpcDialogState"/>. No dialogue is active → silently ignored.
/// <para>#2616: <see cref="Leave"/> (any negative index) = the player closed the panel — the server ends the
/// conversation. Every released server already dropped the dialogue on a negative index, so the leave needs no intent
/// and no NetCodec tag of its own; it is served while a singleplayer world is held paused.</para></summary>
public sealed class NpcDialogChoiceIntent
{
    /// <summary>#2616: the <see cref="ChoiceIndex"/> of "the player closed the panel".</summary>
    public const int Leave = -1;

    public int ChoiceIndex { get; set; }

    /// <summary>#2616: the NPC whose panel the choice (or the leave) belongs to — a late leave for an earlier NPC must not
    /// end the conversation with the next one. 0 = not said (an older client): the current conversation. Additive
    /// contractless field.</summary>
    public int NpcId { get; set; }
}
