using System;
using System.Text;

namespace Spacecraft.WorldGeneration;

/// <summary>
/// Coins pronounceable alien names for generated species (fauna + flora), deterministically from a seeded
/// <see cref="Random"/> so the same world always names a species the same way. Names are built from
/// syllables (onset + vowel + optional coda); creatures get a two-part "genus epithet" name, flora a stem
/// plus a botanical-sounding suffix (…weed / …bloom / …frond). Purely cosmetic — shown to the player on scan.
/// </summary>
public static class NameGenerator
{
    private static readonly string[] Onsets =
    {
        "br", "dr", "gr", "kr", "tr", "vr", "zr", "sk", "sp", "st", "th", "vh", "gh", "sh", "ph", "kl", "pl",
        "x", "z", "k", "t", "v", "n", "m", "s", "r", "l", "q", "j", "y",
    };

    private static readonly string[] Vowels =
    {
        "a", "e", "i", "o", "u", "y", "ae", "ei", "io", "ou", "ai", "ee", "oo", "ua",
    };

    private static readonly string[] Codas =
    {
        "x", "k", "th", "sh", "rn", "ng", "ss", "l", "r", "n", "m", "sk", "st", "z", "ch", "ph", "ll", "rk",
        "", "", "", // weighting toward open syllables
    };

    private static readonly string[] FloraSuffixes =
    {
        "weed", "bloom", "frond", "cap", "vine", "moss", "reed", "thorn", "leaf", "pod", "bract", "fern",
        "spore", "shoot", "petal", "root",
    };

    /// <summary>A two-part coined creature name, e.g. "Vexilth Krool" — a genus stem + a shorter epithet.</summary>
    public static string Creature(Random rng)
        => Word(rng, 2, 3) + " " + Word(rng, 1, 2).ToLowerInvariant();

    /// <summary>A coined flora name, e.g. "Skarn weed" or "Threll" — a stem, usually with a botanical suffix.</summary>
    public static string Flora(Random rng)
    {
        string stem = Word(rng, 2, 3);
        return rng.NextDouble() < 0.75 ? stem + FloraSuffixes[rng.Next(FloraSuffixes.Length)] : stem;
    }

    /// <summary>A coined personal name for an NPC, e.g. "Kra Thraxon" — a short given name + a longer surname,
    /// both capitalised (so it reads as a person, not a lowercase-epithet creature). Thousands of combinations.</summary>
    public static string Person(Random rng) => Word(rng, 1, 2) + " " + Word(rng, 2, 3);

    /// <summary>A coined robot/android designation, e.g. "Vex-42" — a short stem plus a unit number.</summary>
    public static string Robot(Random rng) => Word(rng, 1, 2) + "-" + rng.Next(2, 99);

    private static string Word(Random rng, int minSyllables, int maxSyllables)
    {
        int syllables = rng.Next(minSyllables, maxSyllables + 1);
        var sb = new StringBuilder();
        for (int i = 0; i < syllables; i++)
        {
            sb.Append(Onsets[rng.Next(Onsets.Length)]);
            sb.Append(Vowels[rng.Next(Vowels.Length)]);
            if (i == syllables - 1 || rng.NextDouble() < 0.4)
            {
                sb.Append(Codas[rng.Next(Codas.Length)]);
            }
        }

        string s = sb.ToString();
        return s.Length == 0 ? "Xel" : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
