// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Melody puzzles at runtime (#3346): one running puzzle per player, its rules in <see cref="MelodyPuzzle"/>. Other code
/// starts one (<see cref="StartMelodyPuzzle"/>) — the story at the Singing Shrine, the Song of Return, the Bridge Song, the
/// Soundi's sing duel — and hears about a solution through <see cref="GameEventKeys.MelodySolved"/> (arg: the puzzle id)
/// and the per-puzzle handlers of <see cref="RegisterMelodySolvedHandler"/>. Notes come from the melody bar
/// (<see cref="MelodyNoteIntent"/>) and from the pillars of the puzzle's melody site (the use key, see
/// <see cref="TryStrikeMelodyPillar"/>); the server judges them and answers with <see cref="MelodyPuzzleState"/>, which
/// opens, updates and closes the client's bar. Wrong notes never punish — the round (or the input) simply starts again.
/// A puzzle ends when it is solved, when the player walks away (<see cref="MelodyPuzzleSpec.LeaveDistance"/>), changes the
/// world, flies off, logs out, leaves it on the bar (tone -1), its time limit runs out, or other code cancels it.
/// <para><b>Together:</b> players who run the SAME puzzle id within <see cref="MelodyCoopRange"/> of each other solve it
/// together — when one of them plays the last note, everyone of them nearby is solved with them (event and handlers for
/// each). Everyone near the site hears the prompts and every played tone.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How close (blocks) a player running the same puzzle must stand to the solver to be solved with them.</summary>
    internal const float MelodyCoopRange = 16f;

    /// <summary>Seconds between a finished round and the next round's prompt.</summary>
    internal const double MelodyRoundPause = 0.9;

    /// <summary>Seconds after a wrong note before the round's prompt plays again.</summary>
    internal const double MelodyRetryPause = 1.1;

    /// <summary>Seconds per note of the chorus that answers a solved puzzle.</summary>
    private const float MelodyChorusNoteSeconds = 0.3f;

    /// <summary>The melody bar's note budget per player: a burst of this many notes, refilled at
    /// <see cref="MelodyNotesPerSecond"/> (a kid mashing keys is fine; a client flooding the tag is not).</summary>
    private const double MelodyNoteBurst = 16;
    private const double MelodyNotesPerSecond = 10;

    /// <summary>The highest procedural instrument (5 = the Sounder choir).</summary>
    internal const int MaxNoteInstrument = SoundersIds.InstrumentChoir;

    /// <summary>The melody clock (seconds of simulated time), advanced by <see cref="TickMelodyPuzzles"/>; the rune cooldown
    /// reads it too.</summary>
    private double _melodyClock;

    private readonly Dictionary<string, MelodyRun> _melodyRuns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Action<PlayerSession>>> _melodySolvedHandlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (double Tokens, long Stamp)> _melodyNoteBudget = new(StringComparer.Ordinal);
    private readonly List<string> _melodyTickIds = new();

    /// <summary>One player's running puzzle.</summary>
    private sealed class MelodyRun
    {
        public MelodyRun(MelodyPuzzle puzzle, string locationId, int circumference, Vector3f anchor)
        {
            Puzzle = puzzle;
            LocationId = locationId;
            Circumference = circumference;
            Anchor = anchor;
        }

        public MelodyPuzzle Puzzle { get; }
        public string LocationId { get; }
        public int Circumference { get; }

        /// <summary>The site's centre, or where the puzzle started — the prompts sound here and the leave distance counts
        /// from here.</summary>
        public Vector3f Anchor { get; }

        public double Elapsed;

        /// <summary>Seconds since the last note or since the last prompt finished.</summary>
        public double Idle;

        /// <summary>Seconds the current prompt still plays.</summary>
        public double PromptLeft;

        /// <summary>Seconds until the round's prompt plays (again); negative: none pending.</summary>
        public double PromptIn = -1;

        /// <summary>The pending prompt is a retry after a wrong note — a right note before it plays means the player simply
        /// carried on (a new try), so the replay is dropped instead of wiping that progress.</summary>
        public bool RetryPending;

        public string Id => Puzzle.Spec.Id;
    }

    // ---------------------------------------------------------------------------------------------
    // The API other code calls.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Starts <paramref name="spec"/> for <paramref name="session"/> (replacing a puzzle already running for them): opens the
    /// melody bar and plays the first prompt. With a <see cref="MelodyPuzzleSpec.SiteId"/> of a registered melody site the
    /// site's pillars strike its tones and the prompt sounds from the site. False (nothing starts) for a player who is not on
    /// the ground of a world or a spec without a single tone to play.
    /// </summary>
    internal bool StartMelodyPuzzle(PlayerSession session, MelodyPuzzleSpec spec)
    {
        if (session is null || spec is null || string.IsNullOrEmpty(spec.Id) || !session.Joined || session.Spectating
            || InSpace(session.State.PlayerId))
        {
            return false;
        }

        var puzzle = new MelodyPuzzle(spec.Clone());
        if (!puzzle.IsValid)
        {
            _log.Warn($"Melody puzzle '{spec.Id}' has no tone to play — not started.");
            return false;
        }

        // The player's world, whatever the active cursor points at (a story tick may start a puzzle).
        if (_worlds.Find(session.CurrentLocationId) is not { } world)
        {
            return false;
        }

        var site = spec.SiteId.Length > 0 ? FindMelodySite(session.CurrentLocationId, spec.SiteId) : null;
        var anchor = site?.CentreF ?? session.State.Position;
        var run = new MelodyRun(puzzle, session.CurrentLocationId, world.World.Circumference, anchor);
        _melodyRuns[session.State.PlayerId] = run;
        PlayMelodyPrompt(session, run, MelodyPuzzleResult.Playing);
        return true;
    }

    /// <summary>Ends the player's running puzzle (only <paramref name="puzzleId"/>, when given) without a solution and closes
    /// the bar. False when nothing matching ran.</summary>
    internal bool CancelMelodyPuzzle(PlayerSession session, string? puzzleId = null)
    {
        if (session is null || !_melodyRuns.TryGetValue(session.State.PlayerId, out var run)
            || (!string.IsNullOrEmpty(puzzleId) && !string.Equals(run.Id, puzzleId, StringComparison.Ordinal)))
        {
            return false;
        }

        EndMelodyRun(session, run, MelodyPuzzleResult.Cancelled);
        return true;
    }

    /// <summary>The id of the puzzle running for the player, or null.</summary>
    internal string? RunningMelodyPuzzle(PlayerSession session)
        => session is not null && _melodyRuns.TryGetValue(session.State.PlayerId, out var run) ? run.Id : null;

    /// <summary>Calls <paramref name="handler"/> with the player each time puzzle <paramref name="puzzleId"/> is solved
    /// (after <see cref="GameEventKeys.MelodySolved"/> was raised). Several handlers per id run in registration order; one
    /// that throws is logged and skipped.</summary>
    internal void RegisterMelodySolvedHandler(string puzzleId, Action<PlayerSession> handler)
    {
        if (string.IsNullOrEmpty(puzzleId) || handler is null)
        {
            return;
        }

        if (!_melodySolvedHandlers.TryGetValue(puzzleId, out var list))
        {
            _melodySolvedHandlers[puzzleId] = list = new List<Action<PlayerSession>>();
        }

        list.Add(handler);
    }

    // ---------------------------------------------------------------------------------------------
    // Input.
    // ---------------------------------------------------------------------------------------------

    /// <summary>A tone of the melody bar: feeds the running puzzle, or — without one — is free play the players nearby
    /// hear. Tone -1 leaves the running puzzle.</summary>
    private void HandleMelodyNote(PlayerSession session, MelodyNoteIntent intent)
    {
        if (session.Spectating || InSpace(session.State.PlayerId) || !TakeMelodyNoteBudget(session.State.PlayerId))
        {
            return;
        }

        _melodyRuns.TryGetValue(session.State.PlayerId, out var run);
        if (run is not null && intent.PuzzleId.Length > 0 && !string.Equals(intent.PuzzleId, run.Id, StringComparison.Ordinal))
        {
            return; // a late note (or stop) for a puzzle that already ended
        }

        if (intent.Tone < 0)
        {
            if (run is not null)
            {
                EndMelodyRun(session, run, MelodyPuzzleResult.Cancelled);
            }

            return;
        }

        if (intent.Tone >= SounderAlphabet.ToneCount)
        {
            return;
        }

        if (run is not null)
        {
            FeedMelodyRun(session, run, intent.Tone, pillar: null);
            return;
        }

        int instrument = Math.Max(0, Math.Min(MaxNoteInstrument, intent.Instrument));
        SendNotesAround(session.CurrentLocationId, _world.Circumference, session.State.Position, new[] { intent.Tone }, instrument,
            0.4f, false, "melody_bar", except: session);
        HearSingingPlants(session, intent.Tone, instrument); // #3367: echo flowers and bell blooms listen to free play
    }

    /// <summary>One judged tone. The bar already sounded it for the player; the others hear it — at the struck pillar when
    /// there is one, where everyone (the striker too) hears it.</summary>
    private void FeedMelodyRun(PlayerSession session, MelodyRun run, int tone, Vector3i? pillar)
    {
        run.Idle = 0;
        var spec = run.Puzzle.Spec;
        var notes = new[] { tone };
        if (pillar is { } p)
        {
            SendNotesAround(run.LocationId, run.Circumference, new Vector3f(p.X + 0.5f, p.Y + 1.5f, p.Z + 0.5f), notes,
                spec.Instrument, spec.NoteSeconds, false, "melody_pillar", except: null);
        }
        else
        {
            SendNotesAround(run.LocationId, run.Circumference, session.State.Position, notes, spec.Instrument, spec.NoteSeconds,
                false, "melody_bar", except: session);
        }

        var step = run.Puzzle.Play(tone);
        if (step != MelodyStep.Wrong && run.RetryPending)
        {
            run.RetryPending = false; // they carried on right away: no replay over their new try
            run.PromptIn = -1;
        }

        switch (step)
        {
            case MelodyStep.Correct:
                SendMelodyState(session, run, MelodyPuzzleResult.Correct, open: true, Array.Empty<int>(), tone);
                break;
            case MelodyStep.RoundDone:
                run.PromptIn = MelodyRoundPause;
                SendMelodyState(session, run, MelodyPuzzleResult.RoundDone, open: true, Array.Empty<int>(), tone);
                break;
            case MelodyStep.Wrong:
                if (run.Puzzle.HasPrompt)
                {
                    run.PromptIn = MelodyRetryPause; // the round starts again with its prompt
                    run.RetryPending = true;
                }

                SendMelodyState(session, run, MelodyPuzzleResult.TryAgain, open: true, Array.Empty<int>(), tone);
                break;
            case MelodyStep.Solved:
                SolveMelody(session, run, tone);
                break;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Prompts, solutions, endings.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Starts the current round: its prompt sounds from the anchor and the bar lights it along.</summary>
    private void PlayMelodyPrompt(PlayerSession session, MelodyRun run, MelodyPuzzleResult result)
    {
        var spec = run.Puzzle.Spec;
        run.Puzzle.RestartRound();
        run.PromptIn = -1;
        run.RetryPending = false;
        run.Idle = 0;
        var prompt = run.Puzzle.HasPrompt ? ToArray(run.Puzzle.Prompt) : Array.Empty<int>();
        run.PromptLeft = prompt.Length * (double)spec.NoteSeconds;
        if (prompt.Length > 0)
        {
            SendNotesAround(run.LocationId, run.Circumference, PromptPosition(run), prompt, spec.Instrument, spec.NoteSeconds,
                spec.PromptWithFifth, spec.Id, except: null);
        }

        SendMelodyState(session, run, result, open: true, prompt, -1);
    }

    private static Vector3f PromptPosition(MelodyRun run) => new(run.Anchor.X, run.Anchor.Y + 1.5f, run.Anchor.Z);

    /// <summary>The last note of the last round: the solver — and everyone running the same puzzle near them — is done.</summary>
    private void SolveMelody(PlayerSession session, MelodyRun run, int lastTone)
    {
        var spec = run.Puzzle.Spec;
        if (spec.ChorusOnSolve)
        {
            SendNotesAround(run.LocationId, run.Circumference, PromptPosition(run), ChorusOf(spec), SoundersIds.InstrumentChoir,
                MelodyChorusNoteSeconds, true, spec.Id, except: null);
        }

        var together = new List<(PlayerSession Session, MelodyRun Run)> { (session, run) };
        foreach (var other in _sessions.Values)
        {
            if (!ReferenceEquals(other, session) && other.Joined && other.CurrentLocationId == run.LocationId
                && _melodyRuns.TryGetValue(other.State.PlayerId, out var theirs)
                && string.Equals(theirs.Id, run.Id, StringComparison.Ordinal)
                && DistSqOn(run.Circumference, other.State.Position, session.State.Position) <= MelodyCoopRange * MelodyCoopRange)
            {
                together.Add((other, theirs));
            }
        }

        foreach (var (who, theirRun) in together)
        {
            _melodyRuns.Remove(who.State.PlayerId);
            SendMelodyState(who, theirRun, MelodyPuzzleResult.Solved, open: false, Array.Empty<int>(), lastTone);
        }

        foreach (var (who, _) in together)
        {
            RaiseGameEvent(who, GameEventKeys.MelodySolved, spec.Id);
            NotifyMelodySolved(spec.Id, who);
        }
    }

    private void NotifyMelodySolved(string puzzleId, PlayerSession who)
    {
        if (!_melodySolvedHandlers.TryGetValue(puzzleId, out var handlers))
        {
            return;
        }

        foreach (var handler in handlers.ToArray())
        {
            try
            {
                handler(who);
            }
            catch (Exception ex)
            {
                _log.Error($"Melody puzzle '{puzzleId}' solved handler failed for '{who.State.Name}': {ex.Message}");
            }
        }
    }

    /// <summary>What the choir sings back when a puzzle is solved: the melody (Repeat, Compose, Harmony) or the last call and
    /// its answer (CallAndResponse).</summary>
    private static int[] ChorusOf(MelodyPuzzleSpec spec)
    {
        if (spec.Mode != MelodyPuzzleMode.CallAndResponse)
        {
            return spec.Target;
        }

        int last = spec.Calls.Length - 1;
        if (last < 0)
        {
            return Array.Empty<int>();
        }

        int[] answer = last < spec.Answers.Length && spec.Answers[last] is { Length: > 0 } a ? a : spec.Calls[last];
        var chorus = new List<int>(spec.Calls[last] ?? Array.Empty<int>()) { SounderAlphabet.Rest };
        chorus.AddRange(answer);
        return chorus.ToArray();
    }

    /// <summary>Ends a run without a solution and closes the player's bar with <paramref name="result"/>.</summary>
    private void EndMelodyRun(PlayerSession? session, MelodyRun run, MelodyPuzzleResult result)
    {
        if (session is null)
        {
            return;
        }

        _melodyRuns.Remove(session.State.PlayerId);
        SendMelodyState(session, run, result, open: false, Array.Empty<int>(), -1);
    }

    private void SendMelodyState(PlayerSession session, MelodyRun run, MelodyPuzzleResult result, bool open, int[] highlight, int lastTone)
    {
        var p = run.Puzzle;
        Send(session, new MelodyPuzzleState
        {
            PuzzleId = run.Id,
            Open = open,
            Mode = (int)p.Spec.Mode,
            Round = p.Round,
            RoundCount = p.RoundCount,
            Entered = p.Entered,
            Expected = p.Expected.Count,
            Highlight = highlight,
            NoteSeconds = p.Spec.NoteSeconds,
            Instrument = p.Spec.Instrument,
            Result = (int)result,
            LastTone = lastTone,
        });
    }

    /// <summary>
    /// Sends one <see cref="NoteSequence"/> to every joined player on the ground of <paramref name="locationId"/> within
    /// <see cref="NoteSequenceRange"/> of <paramref name="at"/> (but <paramref name="except"/>). Location-explicit, so the
    /// melody tick can call it whatever world the active cursor points at.
    /// </summary>
    private void SendNotesAround(string locationId, int circumference, Vector3f at, int[] notes, int instrument, float noteSeconds,
                                 bool withFifth, string source, PlayerSession? except)
    {
        if (notes.Length == 0)
        {
            return;
        }

        var message = new NoteSequence
        {
            Notes = notes,
            Instrument = instrument,
            NoteSeconds = noteSeconds,
            WithFifth = withFifth,
            X = at.X,
            Y = at.Y,
            Z = at.Z,
            Source = source,
        };
        foreach (var s in _sessions.Values)
        {
            if (s.Joined && !ReferenceEquals(s, except) && s.CurrentLocationId == locationId && !InSpace(s.State.PlayerId)
                && DistSqOn(circumference, s.State.Position, at) <= (double)NoteSequenceRange * NoteSequenceRange)
            {
                Send(s, message);
            }
        }
    }

    private static double DistSqOn(int circumference, Vector3f a, Vector3f b) => WorldConstants.WrapDistanceSquared(a, b, circumference);

    /// <summary>The melody bar's per-player note budget (wall clock, so it refills during a held world as well).</summary>
    private bool TakeMelodyNoteBudget(string playerId)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double tokens = MelodyNoteBurst;
        if (_melodyNoteBudget.TryGetValue(playerId, out var b))
        {
            double refill = (now - b.Stamp) / (double)System.Diagnostics.Stopwatch.Frequency * MelodyNotesPerSecond;
            tokens = Math.Min(MelodyNoteBurst, b.Tokens + refill);
        }

        bool ok = tokens >= 1;
        _melodyNoteBudget[playerId] = (ok ? tokens - 1 : tokens, now);
        return ok;
    }
}
