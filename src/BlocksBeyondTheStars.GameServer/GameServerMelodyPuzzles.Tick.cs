// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Sounders;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The melody puzzles' clock (#3346): once per tick, after the worlds (a puzzle belongs to its player, wherever they are).
/// It ends the runs whose player logged out, changed the world, flew off or walked away, runs the time limits, plays the
/// prompts that wait after a finished round or a wrong note, and replays a round's prompt as a reminder after a long
/// pause. A held (paused) world stops this clock with everything else.
/// </summary>
public sealed partial class GameServer
{
    private void TickMelodyPuzzles(double dt)
    {
        _melodyClock += dt;
        if (_melodyRuns.Count == 0)
        {
            return;
        }

        _melodyTickIds.Clear();
        _melodyTickIds.AddRange(_melodyRuns.Keys);
        foreach (string playerId in _melodyTickIds)
        {
            if (!_melodyRuns.TryGetValue(playerId, out var run))
            {
                continue; // solved together with someone earlier in this pass
            }

            var session = FindSessionByPlayerId(playerId);
            if (session is null)
            {
                _melodyRuns.Remove(playerId); // logged out: nobody to tell
                continue;
            }

            TickMelodyRun(session, run, dt);
        }
    }

    private void TickMelodyRun(PlayerSession session, MelodyRun run, double dt)
    {
        var spec = run.Puzzle.Spec;
        if (session.CurrentLocationId != run.LocationId || InSpace(session.State.PlayerId)
            || (spec.LeaveDistance > 0
                && DistSqOn(run.Circumference, session.State.Position, run.Anchor) > (double)spec.LeaveDistance * spec.LeaveDistance))
        {
            EndMelodyRun(session, run, MelodyPuzzleResult.Cancelled); // another world, flight, or walked away
            return;
        }

        run.Elapsed += dt;
        if (spec.TimeLimitSeconds > 0 && run.Elapsed >= spec.TimeLimitSeconds)
        {
            EndMelodyRun(session, run, MelodyPuzzleResult.TimedOut);
            return;
        }

        run.PromptLeft = System.Math.Max(0, run.PromptLeft - dt);
        if (run.PromptIn >= 0)
        {
            run.PromptIn -= dt;
            if (run.PromptIn <= 0)
            {
                PlayMelodyPrompt(session, run, MelodyPuzzleResult.Playing);
            }

            return;
        }

        if (!run.Puzzle.HasPrompt || spec.IdleSeconds <= 0 || run.PromptLeft > 0)
        {
            return;
        }

        run.Idle += dt;
        if (run.Idle >= spec.IdleSeconds)
        {
            PlayMelodyPrompt(session, run, MelodyPuzzleResult.Reminder); // forgot it? hear it again — never a fail
        }
    }

    /// <summary>Test seam: runs the melody clock alone (no full server tick).</summary>
    internal void TickMelodyPuzzlesForTest(double dt) => TickMelodyPuzzles(dt);
}
