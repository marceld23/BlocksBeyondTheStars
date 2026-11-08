// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Client → server (#3346): the player played a tone on the melody bar (keys 1–8, the pad, the touch buttons). While a
/// melody puzzle runs for the player it feeds the server's checker; without one the tone is free play and the server lets
/// the players nearby hear it. The client already played it for the player itself. <see cref="Tone"/> -1 is "stop": the
/// player closed the bar, the running puzzle ends. No protocol bump — an older server drops the tag.
/// </summary>
public sealed class MelodyNoteIntent
{
    /// <summary>The tone 0–7 (C4 … C5), or -1 to leave the puzzle.</summary>
    public int Tone { get; set; }

    /// <summary>The puzzle the bar was open for (empty for free play) — a late note for a puzzle that already ended is
    /// dropped instead of feeding the next one.</summary>
    public string PuzzleId { get; set; } = string.Empty;

    /// <summary>Free play only: the instrument the client sounded (0–5). A running puzzle uses its own.</summary>
    public int Instrument { get; set; }
}

/// <summary>
/// Server → client (#3346): the melody bar's state — sent when a puzzle starts (opens the bar), after every judged note,
/// with every prompt, and when the puzzle ends (closes it). The client lights <see cref="Highlight"/> one tone after the
/// other, <see cref="NoteSeconds"/> apart, in time with the prompt's <c>NoteSequence</c>. An older client drops the tag.
/// </summary>
public sealed class MelodyPuzzleState
{
    /// <summary>The puzzle id (<c>SoundersIds.Puzzle*</c>).</summary>
    public string PuzzleId { get; set; } = string.Empty;

    /// <summary>True: the bar is (or stays) open. False: it closes (after showing <see cref="Result"/>).</summary>
    public bool Open { get; set; }

    /// <summary><c>MelodyPuzzleMode</c>: 0 repeat, 1 compose, 2 harmony, 3 call and response.</summary>
    public int Mode { get; set; }

    /// <summary>The current round, 0-based, and how many there are.</summary>
    public int Round { get; set; }

    public int RoundCount { get; set; }

    /// <summary>Right tones played in this round so far, and how many the round asks for.</summary>
    public int Entered { get; set; }

    public int Expected { get; set; }

    /// <summary>The prompt that starts playing with this state (tones 0–7, rests -1) — the bar lights each tone in turn.
    /// Empty when no prompt plays now.</summary>
    public int[] Highlight { get; set; } = System.Array.Empty<int>();

    /// <summary>Seconds per note of the prompt (and per highlight step).</summary>
    public float NoteSeconds { get; set; }

    /// <summary>The instrument the bar sounds its tones with (the puzzle's: 4 Sounder voice, 5 choir, or 0–3).</summary>
    public int Instrument { get; set; }

    /// <summary><c>MelodyPuzzleResult</c>: 0 playing, 1 correct, 2 round done, 3 try again, 4 solved, 5 cancelled,
    /// 6 timed out, 7 reminder.</summary>
    public int Result { get; set; }

    /// <summary>The tone the server just judged (-1: none) — the bar flashes it.</summary>
    public int LastTone { get; set; } = -1;

    /// <summary>#3426: the word the melody spells in the note alphabet (the Song of Return's ORNEMIS), or empty — the bar
    /// shows it letter by letter with <see cref="WordTones"/> (<c>MelodySpelling</c>), the letter the player is at marked
    /// (<see cref="Entered"/> / 2). Sent while the bar is open. A new field of a map-keyed message: an older client skips
    /// it, an older server simply sends none (no spelling line).</summary>
    public string Word { get; set; } = string.Empty;

    /// <summary>#3426: the two tones the player plays for each letter of <see cref="Word"/>, in order — the alphabet's own for
    /// the Song of Return, the counter-voice for the Bridge Song. Empty without a word.</summary>
    public int[] WordTones { get; set; } = System.Array.Empty<int>();
}
