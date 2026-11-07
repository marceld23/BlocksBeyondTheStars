// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Text;

namespace BlocksBeyondTheStars.Shared.Sounders;

/// <summary>
/// The Song Discs of the Sounders story pack (#3352): which disc sings which phrase, the order the story hands them out,
/// which ones fit the obsidian chest's socket (#3354) and the seeded songs of the collectible disc. One place for the
/// server (the Song Spindle, the story loot, the decode check) and the client (the note-key minigame).
/// <para>Every phrase is three words of <see cref="SounderLexicon"/>, sung in the note alphabet (<see cref="SounderAlphabet"/>):
/// the note-key minigame plays one word per round, so a disc is decoded in three rounds. The words are canon once shipped —
/// a saved decode milestone names the disc, and the Song Archive shows what the disc said.</para>
/// </summary>
public static class SongDiscs
{
    /// <summary>The key of the note-key minigame (the registry, <c>data/minigames/catalog.json</c>, the start message).</summary>
    public const string NoteKeyGame = "note_key";

    /// <summary>How many rounds the note-key minigame plays — one word of the phrase each.</summary>
    public const int RoundsPerDisc = 3;

    /// <summary>Failed tries after which the client offers to skip and the server accepts it (no dead end, #3352).</summary>
    public const int FailuresBeforeSkip = 2;

    /// <summary>Prefix of the per-player "this story disc was handed to you" milestones (<see cref="GotMilestone"/>).</summary>
    public const string GotPrefix = SoundersIds.MilestonePrefix + "got:";

    /// <summary>The story discs a relic cache hands out, in order, once disc 1 is decoded (#3352).</summary>
    public static readonly string[] LootOrder = { SoundersIds.DiscBuilders, SoundersIds.DiscHearing, SoundersIds.DiscHunger };

    /// <summary>The story discs the sound chambers hold, in order: the first chamber a player opens, then the second (#3353).</summary>
    public static readonly string[] ChamberOrder = { SoundersIds.DiscGreatSong, SoundersIds.DiscVoices };

    /// <summary>The discs that fit the obsidian chest's socket (#3354): hearing, calm and light.</summary>
    public static readonly string[] SocketDiscs = { SoundersIds.DiscHearing, SoundersIds.DiscCalm, SoundersIds.DiscLight };

    /// <summary>Every disc item: the six story discs, the two socket discs and the collectible.</summary>
    public static readonly string[] All =
    {
        SoundersIds.DiscBeginning, SoundersIds.DiscBuilders, SoundersIds.DiscHearing, SoundersIds.DiscHunger,
        SoundersIds.DiscGreatSong, SoundersIds.DiscVoices, SoundersIds.DiscCalm, SoundersIds.DiscLight, SoundersIds.DiscSong,
    };

    private static readonly Dictionary<string, string[]> Phrases = new(StringComparer.Ordinal)
    {
        [SoundersIds.DiscBeginning] = new[] { "MIRA", "SOA", "ESA" },  // listen, song, together
        [SoundersIds.DiscBuilders] = new[] { "OLU", "TAR", "SOA" },    // build, stone, song
        [SoundersIds.DiscHearing] = new[] { "MIRA", "VEL", "RUSO" },   // listen, water, wave
        [SoundersIds.DiscHunger] = new[] { "KEI", "DUNA", "ORN" },     // hunger, far away, star
        [SoundersIds.DiscGreatSong] = new[] { "SOA", "ESA", "NIRI" },  // song, together, home
        [SoundersIds.DiscVoices] = new[] { "ILA", "SOA", "LUM" },      // child, song, light
        [SoundersIds.DiscCalm] = new[] { "EMIS", "ARU", "VEL" },       // quiet, sky, water
        [SoundersIds.DiscLight] = new[] { "LUM", "ORN", "ARU" },       // light, star, sky
    };

    /// <summary>Whether <paramref name="itemKey"/> is any Song Disc.</summary>
    public static bool IsDisc(string? itemKey) => itemKey != null && Array.IndexOf(All, itemKey) >= 0;

    /// <summary>Whether <paramref name="itemKey"/> is one of the six story discs (decoded with the note-key minigame).</summary>
    public static bool IsStoryDisc(string? itemKey) => itemKey != null && Array.IndexOf(SoundersIds.StoryDiscs, itemKey) >= 0;

    /// <summary>Whether <paramref name="itemKey"/> fits the obsidian chest's disc socket.</summary>
    public static bool IsSocketDisc(string? itemKey) => itemKey != null && Array.IndexOf(SocketDiscs, itemKey) >= 0;

    /// <summary>The three words a disc sings, or an empty list for the collectible (its song comes from
    /// <see cref="CollectibleWords"/>) and anything that is not a disc.</summary>
    public static IReadOnlyList<string> WordsOf(string? disc)
        => disc != null && Phrases.TryGetValue(disc, out var words) ? words : Array.Empty<string>();

    /// <summary>The disc's phrase as one line of words ("OLU TAR SOA").</summary>
    public static string PhraseOf(string? disc) => string.Join(" ", WordsOf(disc));

    /// <summary>The disc's phrase in the note alphabet (two tones per letter, rests between letters and words).</summary>
    public static int[] NotesOf(string? disc) => SounderAlphabet.Encode(PhraseOf(disc));

    /// <summary>The per-player milestone that records a story disc was handed out (loot, a chamber, the flight recorder, the
    /// shrine through <c>GiveSoundersItem</c>) — the story loot never hands the same disc twice.</summary>
    public static string GotMilestone(string disc) => GotPrefix + disc;

    /// <summary>
    /// The little Sounder song a collectible <c>song_disc</c> sings on the spindle at (<paramref name="x"/>,
    /// <paramref name="y"/>, <paramref name="z"/>) of a world of <paramref name="seed"/>: two or three rune words, chosen
    /// stable across platforms (no <c>string.GetHashCode</c>) and never the same word twice in a row. Real words in the
    /// note alphabet, so a player who knows the key can read it.
    /// </summary>
    public static string[] CollectibleWords(long seed, int x, int y, int z)
    {
        unchecked
        {
            ulong h = (ulong)seed * 0xD6E8FEB86659FD93UL;
            h ^= (ulong)(uint)x * 0x9E3779B97F4A7C15UL;
            h ^= (ulong)(uint)y * 0xBF58476D1CE4E5B9UL;
            h ^= (ulong)(uint)z * 0x94D049BB133111EBUL;
            int count = 2 + (int)(Mix(ref h) % 2UL);
            var words = new string[count];
            int last = -1;
            for (int i = 0; i < count; i++)
            {
                int pick = (int)(Mix(ref h) % (ulong)SounderLexicon.Words.Length);
                if (pick == last)
                {
                    pick = (pick + 1) % SounderLexicon.Words.Length;
                }

                words[i] = SounderLexicon.Words[pick];
                last = pick;
            }

            return words;
        }
    }

    /// <summary>The distinct letters of <paramref name="words"/>, A–Z, in alphabet order ("AOS" for "SOA").</summary>
    public static string LettersOf(IEnumerable<string> words)
    {
        var seen = new bool[26];
        foreach (string word in words)
        {
            foreach (char c in word ?? string.Empty)
            {
                char up = char.ToUpperInvariant(c);
                if (up >= 'A' && up <= 'Z')
                {
                    seen[up - 'A'] = true;
                }
            }
        }

        var letters = new StringBuilder(26);
        for (int i = 0; i < seen.Length; i++)
        {
            if (seen[i])
            {
                letters.Append((char)('A' + i));
            }
        }

        return letters.ToString();
    }

    /// <summary>Whether the answers of a note-key run spell the disc's phrase, word by word (case and outer spaces ignored).</summary>
    public static bool AnswersMatch(string? disc, IReadOnlyList<string>? answers)
    {
        var words = WordsOf(disc);
        if (words.Count == 0 || answers is null || answers.Count != words.Count)
        {
            return false;
        }

        for (int i = 0; i < words.Count; i++)
        {
            if (!string.Equals((answers[i] ?? string.Empty).Trim(), words[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static ulong Mix(ref ulong h)
    {
        unchecked
        {
            h ^= h >> 33;
            h *= 0xFF51AFD7ED558CCDUL;
            h ^= h >> 33;
            h *= 0xC4CEB9FE1A85EC53UL;
            h ^= h >> 33;
            return h;
        }
    }
}
