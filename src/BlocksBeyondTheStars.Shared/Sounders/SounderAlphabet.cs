// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Text;

namespace BlocksBeyondTheStars.Shared.Sounders;

/// <summary>
/// The Sounders' note alphabet (#3352, maintainer decision 2026-11-07: "a real one"). Eight tones, C4 to C5 — the same
/// scale as the melody block, indices 0–7. A letter is <b>two tones</b>: the first picks the row, the second the column of
/// this table (so the second tone is always one of the four lowest):
/// <code>
///            col C   D   E   F
///   row C:       A   B   C   D
///   row D:       E   F   G   H
///   row E:       I   J   K   L
///   row F:       M   N   O   P
///   row G:       Q   R   S   T
///   row A:       U   V   W   X
///   row H/B:     Y   Z
/// </code>
/// A <see cref="Rest"/> separates letters (the lore's <c>𝄽</c>), two rests separate words (<c>𝄽𝄽</c>). The table is
/// canon and printable — the school club's worksheet uses it — so never reorder it: a saved Song Archive and every rune
/// in every world would read differently.
/// </summary>
public static class SounderAlphabet
{
    /// <summary>A rest in a note sequence (silence one note long).</summary>
    public const int Rest = -1;

    /// <summary>Number of tones in the scale (C4 … C5).</summary>
    public const int ToneCount = 8;

    /// <summary>Letters per row: the column tone is always 0–3.</summary>
    public const int Columns = 4;

    /// <summary>The tone names (German note names use H for B; the client shows the locale's own via <c>ui.note.*</c>).</summary>
    public static readonly string[] ToneNames = { "C", "D", "E", "F", "G", "A", "B", "C'" };

    /// <summary>The two tones of <paramref name="letter"/> (A–Z, any case), or null for anything else.</summary>
    public static (int Row, int Column)? TonesOf(char letter)
    {
        char up = char.ToUpperInvariant(letter);
        if (up < 'A' || up > 'Z')
        {
            return null;
        }

        int index = up - 'A';
        return (index / Columns, index % Columns);
    }

    /// <summary>The letter two tones spell, or null when the pair is not in the table.</summary>
    public static char? LetterOf(int row, int column)
    {
        if (row < 0 || column < 0 || column >= Columns)
        {
            return null;
        }

        int index = row * Columns + column;
        return index < 26 ? (char)('A' + index) : null;
    }

    /// <summary>
    /// Encodes <paramref name="text"/> as a note sequence: two tones per letter, one <see cref="Rest"/> between letters, two
    /// between words. Anything that is not A–Z or a space is skipped (umlauts: write them out — the Sounders never had them).
    /// </summary>
    public static int[] Encode(string text)
    {
        var notes = new List<int>();
        bool pendingWordGap = false;
        foreach (char c in text ?? string.Empty)
        {
            if (c == ' ')
            {
                pendingWordGap = notes.Count > 0;
                continue;
            }

            (int Row, int Column)? tones = TonesOf(c);
            if (tones is null)
            {
                continue;
            }

            if (notes.Count > 0)
            {
                notes.Add(Rest);
                if (pendingWordGap)
                {
                    notes.Add(Rest);
                }
            }

            pendingWordGap = false;
            notes.Add(tones.Value.Row);
            notes.Add(tones.Value.Column);
        }

        return notes.ToArray();
    }

    /// <summary>
    /// Decodes a note sequence back to text — the inverse of <see cref="Encode"/> (two rests become a space). Pairs that
    /// are not in the table read as <c>?</c>; a dangling single tone is ignored.
    /// </summary>
    public static string Decode(IReadOnlyList<int> notes)
    {
        var text = new StringBuilder();
        int rests = 0;
        int? first = null;
        foreach (int n in notes)
        {
            if (n == Rest)
            {
                rests++;
                first = null;
                continue;
            }

            if (first is null)
            {
                if (rests >= 2 && text.Length > 0)
                {
                    text.Append(' ');
                }

                rests = 0;
                first = n;
                continue;
            }

            text.Append(LetterOf(first.Value, n) ?? '?');
            first = null;
        }

        return text.ToString();
    }

    /// <summary>Only the tones of <paramref name="notes"/> — what a player has to play (rests are timing, not input).</summary>
    public static int[] TonesOnly(IReadOnlyList<int> notes)
    {
        var tones = new List<int>(notes.Count);
        foreach (int n in notes)
        {
            if (n != Rest)
            {
                tones.Add(n);
            }
        }

        return tones.ToArray();
    }

    /// <summary>Whether every note is a valid tone (0–7) or a rest.</summary>
    public static bool IsValidSequence(IReadOnlyList<int> notes)
    {
        foreach (int n in notes)
        {
            if (n != Rest && (n < 0 || n >= ToneCount))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// The little Sounder vocabulary the rune stones sing (#3345). A rune's word is fixed per world seed and position, so the
/// same stone always sings the same word; the Song Archive shows the decoded letters and the translation
/// (<c>story.sounders.word.&lt;word&gt;</c>, lower case). The words are invented Sounder words, never English or German, so a
/// rune means the same in every language and every player in a multiplayer world hears the same tune.
/// </summary>
public static class SounderLexicon
{
    /// <summary>The rune words (A–Z only; short, so a rune plays in two to three seconds). Append only — a world's runes
    /// are an index into this list.</summary>
    public static readonly string[] Words =
    {
        "SOA",  // song
        "NIRI", // home
        "ORN",  // star
        "EMIS", // still, quiet
        "LUM",  // light
        "VEL",  // water
        "TAR",  // stone
        "KEI",  // hunger
        "ARU",  // sky
        "MIRA", // listen
        "ESA",  // together
        "OLU",  // build
        "FENA", // field
        "RUSO", // wave
        "ILA",  // child
        "DUNA", // far away
    };

    /// <summary>The word a rune at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) in a world of
    /// <paramref name="seed"/> sings. Deterministic and stable across platforms (no <c>string.GetHashCode</c>).</summary>
    public static string WordAt(long seed, int x, int y, int z)
    {
        unchecked
        {
            ulong h = (ulong)seed * 0x9E3779B97F4A7C15UL;
            h ^= (ulong)(uint)x * 0xC2B2AE3D27D4EB4FUL;
            h ^= (ulong)(uint)y * 0x165667B19E3779F9UL;
            h ^= (ulong)(uint)z * 0x27D4EB2F165667C5UL;
            h ^= h >> 33;
            h *= 0xFF51AFD7ED558CCDUL;
            h ^= h >> 33;
            return Words[(int)(h % (ulong)Words.Length)];
        }
    }

    /// <summary>Whether <paramref name="word"/> is one of the rune words.</summary>
    public static bool IsWord(string word) => Array.IndexOf(Words, word) >= 0;
}
