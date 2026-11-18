// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The pause of the body's own healing after a hit (#3794, rule and reasons in <see cref="NaturalHealing"/>).
/// <c>DamagePlayer</c> notes every hit that took health — "hit" is its <c>mitigated</c> damage, the kind armour works
/// against — and the healing line of <c>TickEnvironment</c> asks <see cref="HealingPausedByHit"/> before it adds
/// health. Kept per player id in server uptime, like the pacing of the hurt emotes: not saved, so a rejoin starts
/// without a wait, and an entry that has run out is dropped when it is next read.
/// </summary>
public sealed partial class GameServer
{
    private readonly Dictionary<string, double> _healingPausedUntil = new();

    /// <summary>A hit took <paramref name="lost"/> health from the player: the body's own healing waits.</summary>
    private void NoteHitForHealing(PlayerState p, float lost)
    {
        if (lost <= 0f)
        {
            return; // armour or a shield took it all — nothing to recover from
        }

        _healingPausedUntil.TryGetValue(p.PlayerId, out double until);
        _healingPausedUntil[p.PlayerId] = NaturalHealing.PauseUntil(_uptime, until);
    }

    /// <summary>True while the player's own healing waits after a hit.</summary>
    private bool HealingPausedByHit(PlayerState p)
    {
        if (!_healingPausedUntil.TryGetValue(p.PlayerId, out double until))
        {
            return false;
        }

        if (NaturalHealing.Paused(_uptime, until))
        {
            return true;
        }

        _healingPausedUntil.Remove(p.PlayerId);
        return false;
    }

    /// <summary>Test seam: a hit of <paramref name="amount"/> on the player, as a bite or a blow deals it (through armour).
    /// Returns the health lost.</summary>
    public float HitForTest(string playerId, float amount)
        => FindSessionByPlayerId(playerId) is { } session ? DamagePlayer(session.State, amount, mitigated: true) : 0f;

    /// <summary>Test seam: is the player's own healing waiting after a hit right now?</summary>
    public bool HealingPausedForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } session && HealingPausedByHit(session.State);
}
