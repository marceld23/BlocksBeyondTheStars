// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Server → client: the active story's shared, server-wide progress for this save — the meter shown in the
/// Story Log tab + Map tab, plus the raw counters. Sent on join and whenever the story advances. The
/// narrator beats themselves arrive separately on the existing VEGA <c>ShipAiLine</c> channel; this message
/// carries only the aggregate state. <see cref="Active"/> is false when the story is disabled ("none"
/// sandbox).
/// </summary>
public sealed class StoryStateMessage
{
    /// <summary>The active story pack id (e.g. "vega_protocol"), or "none" when disabled.</summary>
    public string StoryId { get; set; } = string.Empty;

    /// <summary>False when no story is active (the "none" sandbox) — the UI hides the meter/tab.</summary>
    public bool Active { get; set; }

    /// <summary>The current weighted progress score.</summary>
    public int Progress { get; set; }

    /// <summary>The score that opens the finale (the last beat's threshold) — for the "NN %" meter.</summary>
    public int ProgressTarget { get; set; }

    public int FragmentsFound { get; set; }
    public int MachineKills { get; set; }
    public int Milestones { get; set; }

    /// <summary>How many narrator beats of the arc have been revealed so far.</summary>
    public int BeatsRevealed { get; set; }

    public bool GuardianSystemRevealed { get; set; }
    public bool GuardianDefeated { get; set; }
}

/// <summary>
/// Client → server (admin only): choose the save's active story pack, or "none" to disable the story
/// (sandbox). Switching resets the per-save story progress to a fresh state for the chosen pack.
/// </summary>
public sealed class StorySelectIntent
{
    public string StoryId { get; set; } = string.Empty;
}

/// <summary>One net fragment scattered on a body's surface for the client to render + let the player pick up
/// (walk up, press E). Text-only story finds — distinct from the knowledge mini-game dataqubes. The archive
/// text is not sent until pickup (see <see cref="NetFragmentRevealed"/>); this carries only position + the
/// lore <see cref="Category"/> for the icon/tint. Server-authoritative placement (deterministic from seed).</summary>
public sealed class NetStoryFragment
{
    public int Id { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>Lore category (vega | sps | guardian | network | settler | netnode) for the client icon/tint.</summary>
    public string Category { get; set; } = string.Empty;
}

/// <summary>Full set of net fragments the client should render for its current world (server → client).</summary>
public sealed class NetFragmentList
{
    public NetStoryFragment[] Fragments { get; set; } = System.Array.Empty<NetStoryFragment>();
}

/// <summary>The player picks up a net fragment they're standing at — press E (client → server). The server
/// validates the fragment exists and the player is within reach, then reveals it and advances the story.</summary>
public sealed class NetFragmentFoundIntent
{
    public int FragmentId { get; set; }
}

/// <summary>Server → client: the picked-up fragment's archive text to show in the reader panel. The client
/// localizes <see cref="TextKey"/> (bilingual DE+EN); <see cref="Category"/> tints/labels the entry.</summary>
public sealed class NetFragmentRevealed
{
    public string Category { get; set; } = string.Empty;
    public string TextKey { get; set; } = string.Empty;
}

/// <summary>Server → client: a personal player memory just unlocked (by defeating a Guardian machine). The
/// client localizes <see cref="TextKey"/> (DE+EN) and shows it in the reader. Per-player — each player is a
/// different neural-imprint clone, so memories never contradict across a multiplayer crew.</summary>
public sealed class PlayerMemoryRevealed
{
    public string TextKey { get; set; } = string.Empty;
}

// ---------------- Finale (P6): Guardian system reveal → core hack → argument duel ----------------

/// <summary>Server → client: the Guardian (finale) system has just been placed on the star map — the story is
/// complete enough to confront the core. Fired once, when the gate flips. A jump generator is needed to reach
/// it; the narrator line arrives separately on the VEGA channel.</summary>
public sealed class GuardianSystemRevealed
{
    /// <summary>Optional locale key / id for the map marker label (the client may also just use a fixed label).</summary>
    public string LabelKey { get; set; } = string.Empty;
}

/// <summary>Client → server: the player channels the core hack for one tick at the inner core (stage 3,
/// "channel-and-defend"). The server owns the increment (anti-cheat) and replies with
/// <see cref="CoreHackProgress"/>; completing it opens the argument duel.</summary>
public sealed class CoreHackIntent
{
}

/// <summary>Server → client: the core-hack channel progress (0..100). When <see cref="Complete"/> the hack is
/// done and the duel begins (the first <see cref="CoreDialogueMessage"/> follows).</summary>
public sealed class CoreHackProgress
{
    public int Progress { get; set; }
    public bool Complete { get; set; }
}

/// <summary>Server → client: the current state of the finale argument duel (stage 4). Carries the core's
/// statement (<see cref="PromptKey"/>) and the player's rebuttal options (<see cref="ChoiceKeys"/>), plus an
/// optional <see cref="ResponseKey"/> — the core's reaction to the previous pick. All are locale keys the
/// client localizes (bilingual DE+EN). When <see cref="Won"/> the core has been argued into shutdown
/// (pacification) and there are no further choices.</summary>
public sealed class CoreDialogueMessage
{
    /// <summary>Index of the current duel node (for the client's progress display).</summary>
    public int Node { get; set; }

    /// <summary>Locale key of the core's statement/challenge ("" when <see cref="Won"/>).</summary>
    public string PromptKey { get; set; } = string.Empty;

    /// <summary>Locale keys of the player's rebuttal options (empty when <see cref="Won"/>).</summary>
    public string[] ChoiceKeys { get; set; } = System.Array.Empty<string>();

    /// <summary>Locale key of the core's reaction to the previous pick ("" at the opening of the duel).</summary>
    public string ResponseKey { get; set; } = string.Empty;

    /// <summary>True once the last node is cleared — the core powers down (the galaxy is pacified).</summary>
    public bool Won { get; set; }
}

/// <summary>Client → server: the player offers a rebuttal at the current duel node. The server validates the
/// index against the active node; a correct (contradiction) choice advances the duel, a wrong one is dismissed
/// and the node is re-presented (the duel cannot be lost, only stalled).</summary>
public sealed class CoreDialogueChoiceIntent
{
    public int ChoiceIndex { get; set; }
}
