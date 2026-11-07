// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Client → server (#3345, the Sounders story pack): the player pressed the use key while aiming at a block that has a use —
/// a rune stone (plays its phrase), a pillar of a melody puzzle site (strikes its tone), a Song Spindle (plays the disc in
/// it). The server decides what the block does; a block without a registered use, out of reach, or not the block the
/// client named is ignored. No protocol bump — an older server drops the tag.
/// </summary>
public sealed class UseBlockIntent
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

/// <summary>
/// Server → client (#3345): play a short melody at a place — a rune's phrase, a puzzle prompt, a Song Disc, a Soundi's
/// call. One message per melody instead of one <c>SoundFx</c> per note, so the timing is the client's (no network jitter
/// between notes). An older client drops the tag and simply hears nothing.
/// </summary>
public sealed class NoteSequence
{
    /// <summary>The tones 0–7 (C4 … C5) and -1 for a rest (<c>SounderAlphabet.Rest</c>).</summary>
    public int[] Notes { get; set; } = System.Array.Empty<int>();

    /// <summary>The procedural instrument: 0 crystal, 1 bell, 2 bass, 3 blip, 4 Sounder voice, 5 Sounder choir.</summary>
    public int Instrument { get; set; }

    /// <summary>Seconds per note (and per rest).</summary>
    public float NoteSeconds { get; set; } = 0.32f;

    /// <summary>Play every note together with its fifth above (the shrine's answer, the Bridge Song).</summary>
    public bool WithFifth { get; set; }

    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>Optional: what is singing (a block key, a species id, a puzzle id) — for the client's visuals only.</summary>
    public string Source { get; set; } = string.Empty;
}
