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
    /// streamer, "interview" = open the interview box for the reporter; "" = nothing. Additive contractless field.</summary>
    public string Action { get; set; } = string.Empty;
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
/// with the next <see cref="NpcDialogState"/>. No dialogue is active → silently ignored.</summary>
public sealed class NpcDialogChoiceIntent
{
    public int ChoiceIndex { get; set; }
}
