// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Story;

/// <summary>
/// Server-wide, per-save runtime state for one active story pack (mirrors how the alliance graph is
/// server-wide rather than per-body). Counters feed the story-progress score in <see cref="StoryEngine"/>;
/// <see cref="BeatsRevealed"/> tracks how far the ordered arc has been spoken (monotonic, never rewinds).
/// Per-player "seen beats" are tracked separately on each player (PlayerState.Milestones), not here.
/// </summary>
public sealed class StoryState
{
    /// <summary>The active pack id this state belongs to (e.g. "vega_protocol").</summary>
    public string StoryId { get; set; } = string.Empty;

    /// <summary>Net fragments found across the save (the primary story driver).</summary>
    public int FragmentsFound { get; set; }

    /// <summary>Guardian-machine kills across the save (contribution capped in <see cref="StoryEngine"/>).</summary>
    public int MachineKills { get; set; }

    /// <summary>Milestones reached (systems mapped, settlements helped, and the once-per-save firsts in
    /// <see cref="MilestoneKeys"/>).</summary>
    public int Milestones { get; set; }

    /// <summary>Once-per-save milestone keys already counted (e.g. <c>base:first</c>, <c>ship:first</c>,
    /// <c>monument:&lt;body&gt;</c>), so a first can never be farmed by repeating it (#1105).</summary>
    public HashSet<string> MilestoneKeys { get; set; } = new(StringComparer.Ordinal);

    /// <summary>How many beats of the arc have been revealed so far (revealed strictly in order).</summary>
    public int BeatsRevealed { get; set; }

    /// <summary>Set once the finale system has been placed on the star map.</summary>
    public bool GuardianSystemRevealed { get; set; }

    /// <summary>Set once the finale is won — pacifies the galaxy (gates enemy spawns off, per-save, one-way).</summary>
    public bool GuardianDefeated { get; set; }

    /// <summary>Keys of net fragments already found, so the same fragment is never counted twice.</summary>
    public HashSet<string> FoundFragmentKeys { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Pity budget (#1109): bodies stamped in a row that rolled zero surface fragments. At two the
    /// next body guarantees one, so a player can never go many worlds without a single story find.</summary>
    public int BodiesWithoutFragment { get; set; }
}
