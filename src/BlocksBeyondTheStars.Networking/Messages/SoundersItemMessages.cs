// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Server → client (#3352, the Sounders story pack): open a story minigame with a context — today the note-key minigame
/// for a Song Disc the player just put into a Song Spindle. The words are what the disc sings (one per round); the client
/// plays them in the note alphabet and the player picks the letters. Sent again with a higher <see cref="Failures"/> after
/// a failed try, so an open window can offer "skip" once the server will accept it. An older client drops the tag (the disc
/// still sings; the story waits until the player decodes it on a newer client).
/// </summary>
public sealed class StoryMinigameStart
{
    /// <summary>The minigame key (<c>note_key</c>).</summary>
    public string Game { get; set; } = string.Empty;

    /// <summary>What the run is about — the disc item key.</summary>
    public string Context { get; set; } = string.Empty;

    /// <summary>The words to decode, one per round (A–Z only).</summary>
    public string[] Words { get; set; } = System.Array.Empty<string>();

    /// <summary>The letters the player already knows (decoded discs, heard runes) — the alphabet table shows them filled in.</summary>
    public string KnownLetters { get; set; } = string.Empty;

    /// <summary>Failed tries the server counted for this context; from <c>SongDiscs.FailuresBeforeSkip</c> on it accepts a skip.</summary>
    public int Failures { get; set; }

    /// <summary>The procedural instrument the phrase is played with (4 = the Sounder voice).</summary>
    public int Instrument { get; set; } = 4;
}

/// <summary>
/// Client → server (#3352): the outcome of a story minigame run. <see cref="Answers"/> are the words the player spelled, one
/// per round; the server checks them against the disc's phrase (a run it did not open, or wrong words, count as a failed
/// try). <see cref="Skip"/> asks to skip — accepted only after <c>SongDiscs.FailuresBeforeSkip</c> failed tries. Allowed
/// while a menu holds a singleplayer world (it only changes the player's own ledger). An older server drops the tag.
/// </summary>
public sealed class StoryMinigameResultIntent
{
    public string Game { get; set; } = string.Empty;

    public string Context { get; set; } = string.Empty;

    /// <summary>True when the run was finished (every round spelled); false for a failed or abandoned run.</summary>
    public bool Completed { get; set; }

    public string[] Answers { get; set; } = System.Array.Empty<string>();

    /// <summary>The player chose to skip the minigame after failing it.</summary>
    public bool Skip { get; set; }
}
