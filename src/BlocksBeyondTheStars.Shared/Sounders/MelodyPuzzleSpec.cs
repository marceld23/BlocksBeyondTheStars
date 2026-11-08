// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Shared.Sounders;

/// <summary>The four kinds of melody puzzle (#3346). The numeric values travel on the wire (<c>MelodyPuzzleState.Mode</c>).</summary>
public enum MelodyPuzzleMode
{
    /// <summary>Simon says: the server plays the start of the melody, the player plays it back; every round the prompt
    /// grows until the whole melody is played. A wrong note restarts the round (the prompt plays again).</summary>
    Repeat = 0,

    /// <summary>Play a fixed whole sequence in order from memory (the Song of Return, ORNEMIS in the note alphabet). No
    /// prompt. A wrong note resets the input — and a wrong note that is the sequence's first tone starts a new try.</summary>
    Compose = 1,

    /// <summary>The server plays a melody, the player plays its counter-voice: every tone moved by
    /// <see cref="MelodyPuzzleSpec.HarmonyOffset"/> scale steps (<see cref="MelodyPuzzle.Transpose"/>). A wrong note resets
    /// the input (the melody plays again).</summary>
    Harmony = 2,

    /// <summary>The server plays a phrase (a call), the player answers with that call's answer phrase; one round per call.
    /// A wrong note restarts the round (the call plays again).</summary>
    CallAndResponse = 3,
}

/// <summary>What the server's checker made of a note, or why a puzzle closed — <c>MelodyPuzzleState.Result</c> on the wire.</summary>
public enum MelodyPuzzleResult
{
    /// <summary>The bar opened or a prompt plays — nothing judged yet.</summary>
    Playing = 0,

    /// <summary>The note was right; the round goes on.</summary>
    Correct = 1,

    /// <summary>The note finished a round; the next prompt follows.</summary>
    RoundDone = 2,

    /// <summary>The note was not the one asked for — never a punishment: the round (or the input) simply starts again.</summary>
    TryAgain = 3,

    /// <summary>The whole puzzle is solved; the bar closes.</summary>
    Solved = 4,

    /// <summary>The puzzle ended without a solution: the player walked away, changed the world, left it, or other code
    /// cancelled it. The bar closes.</summary>
    Cancelled = 5,

    /// <summary>The puzzle's time limit ran out. The bar closes; the player can simply start it again.</summary>
    TimedOut = 6,

    /// <summary>Nothing was played for a while: the round starts again and its prompt plays once more (a reminder).</summary>
    Reminder = 7,
}

/// <summary>
/// One melody puzzle (#3346) as other code asks the server to run it (<c>GameServer.StartMelodyPuzzle</c>). Pure data; the
/// rules are <see cref="MelodyPuzzle"/>'s. Tones are 0–7 (C4 … C5, the melody block's scale and the melody bar's eight
/// keys); <see cref="SounderAlphabet.Rest"/> (-1) is a rest — timing for the prompt, never something to play.
/// </summary>
public sealed class MelodyPuzzleSpec
{
    /// <summary>The puzzle id (<c>SoundersIds.Puzzle*</c>): the <c>melody:solved</c> event's argument and the key of the
    /// success handlers.</summary>
    public string Id { get; set; } = string.Empty;

    public MelodyPuzzleMode Mode { get; set; }

    /// <summary>Repeat: the whole melody. Compose: the sequence to play. Harmony: the melody whose counter-voice the player
    /// plays. Unused by CallAndResponse (see <see cref="Calls"/>).</summary>
    public int[] Target { get; set; } = Array.Empty<int>();

    /// <summary>Repeat only: how many rounds the melody is split into (each round's prompt is longer, the last one is the
    /// whole melody). 0 = one round per tone — classic Simon says.</summary>
    public int Rounds { get; set; }

    /// <summary>CallAndResponse: the phrases the server plays, one round each.</summary>
    public int[][] Calls { get; set; } = Array.Empty<int[]>();

    /// <summary>CallAndResponse: the answer to each call (same index). A missing or empty answer means "sing the call
    /// back".</summary>
    public int[][] Answers { get; set; } = Array.Empty<int[]>();

    /// <summary>Harmony: the scale steps between the melody and the counter-voice. +4 is a fifth above (C → G);
    /// a tone pushed past the top C' wraps down an octave, so the counter-voice always stays on the bar's eight keys.</summary>
    public int HarmonyOffset { get; set; } = 4;

    /// <summary>The procedural instrument the prompt (and the bar's own tones) sound with: 0 crystal, 1 bell, 2 bass, 3 blip,
    /// 4 Sounder voice, 5 Sounder choir.</summary>
    public int Instrument { get; set; } = SoundersIds.InstrumentVoice;

    /// <summary>Seconds per note (and per rest) of the prompt.</summary>
    public float NoteSeconds { get; set; } = 0.45f;

    /// <summary>The prompt sounds with its fifth above (the shrine's answer).</summary>
    public bool PromptWithFifth { get; set; }

    /// <summary>The melody site the puzzle is played at (its pillars strike its tones), or empty: only the melody bar.</summary>
    public string SiteId { get; set; } = string.Empty;

    /// <summary>Seconds without a note (after the prompt) before the round starts again with its prompt — a reminder,
    /// never a fail. 0 = never. Compose has no prompt and never reminds.</summary>
    public float IdleSeconds { get; set; } = 20f;

    /// <summary>Seconds after which the puzzle closes on its own (<see cref="MelodyPuzzleResult.TimedOut"/>). 0 = no limit.</summary>
    public float TimeLimitSeconds { get; set; }

    /// <summary>How far (blocks) the player may walk from the site's centre — or, without a site, from where the puzzle
    /// started — before it is cancelled. 0 = never by distance.</summary>
    public float LeaveDistance { get; set; } = 24f;

    /// <summary>When solved, the whole melody comes back once in the Sounder choir with its fifth (a small reward chord).</summary>
    public bool ChorusOnSolve { get; set; } = true;

    /// <summary>#3426: the word <see cref="Target"/> spells in the note alphabet (the Song of Return and the Bridge Song:
    /// ORNEMIS), or empty. The melody bar then shows it letter by letter with each letter's two notes
    /// (<see cref="MelodySpelling"/>) — the target itself stays the truth of what is played.</summary>
    public string Word { get; set; } = string.Empty;

    /// <summary>A copy (the server keeps its own, so a caller that reuses its spec cannot change a running puzzle).</summary>
    public MelodyPuzzleSpec Clone() => new()
    {
        Id = Id,
        Mode = Mode,
        Target = (int[])Target.Clone(),
        Rounds = Rounds,
        Calls = CloneRows(Calls),
        Answers = CloneRows(Answers),
        HarmonyOffset = HarmonyOffset,
        Instrument = Instrument,
        NoteSeconds = NoteSeconds,
        PromptWithFifth = PromptWithFifth,
        SiteId = SiteId,
        IdleSeconds = IdleSeconds,
        TimeLimitSeconds = TimeLimitSeconds,
        LeaveDistance = LeaveDistance,
        ChorusOnSolve = ChorusOnSolve,
        Word = Word,
    };

    private static int[][] CloneRows(int[][]? rows)
    {
        if (rows is null)
        {
            return Array.Empty<int[]>();
        }

        var copy = new int[rows.Length][];
        for (int i = 0; i < rows.Length; i++)
        {
            copy[i] = rows[i] is null ? Array.Empty<int>() : (int[])rows[i].Clone();
        }

        return copy;
    }
}
