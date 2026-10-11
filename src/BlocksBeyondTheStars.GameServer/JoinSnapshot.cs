// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// What a player looked like when they arrived, taken ONCE at the very top of the join (#2460, B01) and never
/// re-evaluated for the rest of the session. By the time VEGA boots, the join itself has already changed the
/// player state: <c>SetupPlayerShip</c> maps the spawn system, the Creative grants unlock blueprints and the
/// story catch-up pays a knowledge point per revealed beat. Deciding "newcomer or veteran" from the live state
/// after all that read every fresh player on a progressed multiplayer world as a veteran — no prologue, no
/// tutorial. Everything that needs the arrival state (VEGA's onboarding boot, the resolution catch-up, and the
/// tutorial pack that replaces the onboarding chain later) reads this snapshot instead.
/// </summary>
public sealed class JoinSnapshot
{
    /// <summary>The snapshot of a session that never went through a join (a fresh, unclassified player).</summary>
    public static readonly JoinSnapshot Empty = new(veteran: false, hadOnboardingIntro: false, Array.Empty<string>());

    private readonly HashSet<string> _storyKeys;

    public JoinSnapshot(bool veteran, bool hadOnboardingIntro, IEnumerable<string> storyKeys)
    {
        Veteran = veteran;
        HadOnboardingIntro = hadOnboardingIntro;
        _storyKeys = new HashSet<string>(storyKeys, StringComparer.Ordinal);
    }

    /// <summary>The player arrived with veteran evidence — knowledge, blueprints or missions — on a world that
    /// does not hand blueprints out (a Creative world never counts, #1461).</summary>
    public bool Veteran { get; }

    /// <summary>VEGA's onboarding had already booted for this player (<c>vega:intro</c>) before this join.</summary>
    public bool HadOnboardingIntro { get; }

    /// <summary>A first-timer: never booted into the onboarding and no veteran evidence.</summary>
    public bool Newcomer => !HadOnboardingIntro && !Veteran;

    /// <summary>The player's per-player story keys (<c>story:…</c> milestones) as they were before the join —
    /// for example which beats this player had personally heard before the join's catch-up added the rest.</summary>
    public IReadOnlyCollection<string> StoryKeys => _storyKeys;

    /// <summary>Whether the player carried <paramref name="key"/> before this join.</summary>
    public bool HadStoryKey(string key) => _storyKeys.Contains(key);
}
