// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Text;

namespace BlocksBeyondTheStars.Shared.Sounders;

/// <summary>
/// #3426: how a melody puzzle that spells a word in the note alphabet (the Song of Return's ORNEMIS, the Bridge Song) reads
/// letter by letter on the melody bar — "O = F E · R = G D · N = F D · …", the letter the player is at marked. A kid read
/// "play ORNEMIS" as the note names O-R-N-E-M-I-S (only the E is a key); the line shows that every letter is TWO notes and
/// which ones. One place for the server (the tones it sends with <c>MelodyPuzzleState.WordTones</c>) and the client (the
/// line it draws), so the line can never drift from the puzzle's expected tones.
/// </summary>
public static class MelodySpelling
{
    /// <summary>Between two letter groups of the line.</summary>
    public const string Separator = " · ";

    /// <summary>The letters of <paramref name="word"/> the note alphabet has (A–Z, upper case) — spaces and anything else
    /// dropped, exactly as <see cref="SounderAlphabet.Encode"/> skips them.</summary>
    public static string Letters(string? word)
    {
        var letters = new StringBuilder();
        foreach (char c in word ?? string.Empty)
        {
            if (SounderAlphabet.TonesOf(c) is not null)
            {
                letters.Append(char.ToUpperInvariant(c));
            }
        }

        return letters.ToString();
    }

    /// <summary>
    /// The tones the player plays for <paramref name="word"/>, two per letter: the alphabet's own (Compose, Repeat — the
    /// Song of Return) or, for <see cref="MelodyPuzzleMode.Harmony"/>, the counter-voice — every tone moved by
    /// <paramref name="harmonyOffset"/> scale steps (<see cref="MelodyPuzzle.Transpose"/>), as the puzzle expects it (the
    /// Bridge Song). Empty for no word and for call and response (its calls are no word).
    /// </summary>
    public static int[] PlayedTones(string? word, MelodyPuzzleMode mode, int harmonyOffset)
    {
        if (string.IsNullOrEmpty(word) || mode == MelodyPuzzleMode.CallAndResponse)
        {
            return Array.Empty<int>();
        }

        int[] tones = SounderAlphabet.TonesOnly(SounderAlphabet.Encode(word!));
        if (mode == MelodyPuzzleMode.Harmony)
        {
            for (int i = 0; i < tones.Length; i++)
            {
                tones[i] = MelodyPuzzle.Transpose(tones[i], harmonyOffset);
            }
        }

        return tones;
    }

    /// <summary><see cref="PlayedTones(string, MelodyPuzzleMode, int)"/> of a puzzle spec's <see cref="MelodyPuzzleSpec.Word"/>.</summary>
    public static int[] PlayedTones(MelodyPuzzleSpec spec)
        => spec is null ? Array.Empty<int>() : PlayedTones(spec.Word, spec.Mode, spec.HarmonyOffset);

    /// <summary>The letter (0-based) the player is at after <paramref name="entered"/> right tones — two tones per letter,
    /// never past the last one; -1 when there are no letters.</summary>
    public static int CurrentLetter(int entered, int letterCount)
        => letterCount <= 0 ? -1 : Math.Min(letterCount - 1, Math.Max(0, entered) / 2);

    /// <summary>
    /// The spelling line: every letter of <paramref name="word"/> with its two tones' names from <paramref name="noteNames"/>
    /// (the locale's <c>ui.note.names</c>, eight names — German says H where English says B; anything else falls back to
    /// <see cref="SounderAlphabet.ToneNames"/>), joined by <see cref="Separator"/>: <c>O = F E · R = G D · …</c>. The letter
    /// <paramref name="current"/> is wrapped in <paramref name="markOpen"/> / <paramref name="markClose"/> (the client's rich
    /// text). Letters without two tones in <paramref name="tones"/> are left out; empty when there is nothing to spell.
    /// </summary>
    public static string Line(string? word, IReadOnlyList<int>? tones, IReadOnlyList<string>? noteNames, int current = -1,
                              string markOpen = "", string markClose = "")
    {
        string letters = Letters(word);
        if (letters.Length == 0 || tones is null)
        {
            return string.Empty;
        }

        IReadOnlyList<string> names = noteNames is { Count: SounderAlphabet.ToneCount } ? noteNames : SounderAlphabet.ToneNames;
        var line = new StringBuilder();
        for (int i = 0; i < letters.Length && (2 * i) + 1 < tones.Count; i++)
        {
            if (i > 0)
            {
                line.Append(Separator);
            }

            bool marked = i == current;
            line.Append(marked ? markOpen : string.Empty)
                .Append(letters[i]).Append(" = ")
                .Append(NameOf(names, tones[2 * i])).Append(' ').Append(NameOf(names, tones[(2 * i) + 1]))
                .Append(marked ? markClose : string.Empty);
        }

        return line.ToString();
    }

    private static string NameOf(IReadOnlyList<string> names, int tone)
        => tone >= 0 && tone < names.Count ? names[tone] : "?";
}
