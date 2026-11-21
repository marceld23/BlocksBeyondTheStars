// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Sounders;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Melody puzzles that start from the melody bar itself (#3909) — the twin of the site starters
/// (<see cref="RegisterMelodySiteStarter"/>), for places that have no pillar a player could strike: on the other side the
/// use key on a rune stone is the way home, so a crack at a gate is mended from the bar. A tone played with no puzzle
/// running asks the registered starters; the first spec starts (the tone that asked is not judged — it called the puzzle),
/// and when every starter passes the tone is free play as before.
/// </summary>
public sealed partial class GameServer
{
    private readonly List<Func<PlayerSession, int, MelodyPuzzleSpec?>> _melodyFreePlayStarters = new();

    /// <summary>
    /// Asks <paramref name="starter"/> whenever a player plays a tone on the melody bar while no puzzle runs for them (the
    /// player's world is under the cursor): a spec starts that puzzle, null passes to the next starter. Keep it cheap — it
    /// runs for every free tone.
    /// </summary>
    internal void RegisterMelodyFreePlayStarter(Func<PlayerSession, int, MelodyPuzzleSpec?> starter)
    {
        if (starter is not null)
        {
            _melodyFreePlayStarters.Add(starter);
        }
    }

    /// <summary>A free tone of the melody bar as a call: true when a starter answered with a puzzle and it started.</summary>
    private bool TryStartFreePlayPuzzle(PlayerSession session, int tone)
    {
        foreach (var starter in _melodyFreePlayStarters)
        {
            MelodyPuzzleSpec? spec;
            try
            {
                spec = starter(session, tone);
            }
            catch (Exception ex)
            {
                _log.Error($"Melody free-play starter failed for '{session.State.Name}': {ex.Message}");
                continue;
            }

            if (spec is not null && StartMelodyPuzzle(session, spec))
            {
                return true;
            }
        }

        return false;
    }
}
