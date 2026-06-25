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

    /// <summary>Milestones reached (systems mapped / settlements helped / first base or station built).</summary>
    public int Milestones { get; set; }

    /// <summary>How many beats of the arc have been revealed so far (revealed strictly in order).</summary>
    public int BeatsRevealed { get; set; }

    /// <summary>Set once the finale system has been placed on the star map.</summary>
    public bool GuardianSystemRevealed { get; set; }

    /// <summary>Set once the finale is won — pacifies the galaxy (gates enemy spawns off, per-save, one-way).</summary>
    public bool GuardianDefeated { get; set; }

    /// <summary>Keys of net fragments already found, so the same fragment is never counted twice.</summary>
    public HashSet<string> FoundFragmentKeys { get; set; } = new(StringComparer.Ordinal);
}
