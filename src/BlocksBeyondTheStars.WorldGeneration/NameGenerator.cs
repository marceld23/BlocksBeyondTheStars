// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Text;

namespace BlocksBeyondTheStars.WorldGeneration;

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

    private static readonly string[] TreeSuffixes =
    {
        "wood", "bark", "oak", "pine", "timber", "trunk", "grove", "ash", "elm", "fir",
    };

    /// <summary>A coined flora name, e.g. "Skarn weed" or "Threll" — a stem, usually with a botanical suffix.</summary>
    public static string Flora(Random rng)
    {
        string stem = Word(rng, 2, 3);
        return rng.NextDouble() < 0.75 ? stem + FloraSuffixes[rng.Next(FloraSuffixes.Length)] : stem;
    }

    /// <summary>A coined tree name, e.g. "Skarnwood" or "Threlloak" — a stem with an arboreal suffix.</summary>
    public static string Tree(Random rng)
        => Word(rng, 2, 3) + TreeSuffixes[rng.Next(TreeSuffixes.Length)];

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

    // ---- Celestial naming (#678) ----------------------------------------------------------------
    // Star systems, planets, moons, asteroids, stations and wrecks. All methods take the galaxy's
    // DeterministicRandom (not System.Random): universe names must be a pure function of the world
    // seed, platform-independent, and drawn from a naming-only stream so they can never disturb the
    // body-layout rng (see UniverseGenerator).

    /// <summary>Catalog letter pool — no I/O (read as 1/0) and no vowels (avoids accidental words).</summary>
    private const string CatalogLetters = "BCDFGHKLMNPRSTVXZ";

    /// <summary>First words for two-part system names ("Ember Veil"). English-ish by design — the
    /// same registry look in every locale, like real proper nouns on a star chart.</summary>
    private static readonly string[] RegionFirsts =
    {
        "Ember", "Iron", "Frost", "Ashen", "Silver", "Amber", "Hollow", "Crimson", "Pale", "Shadow",
        "Aurora", "Onyx", "Cinder", "Sable", "Argent", "Halcyon",
    };

    private static readonly string[] RegionEpithets =
    {
        "Reach", "Veil", "Drift", "Expanse", "Gate", "Crown", "Verge", "Deep", "Shroud", "Passage",
        "Spur", "Haven",
    };

    // Archetype-flavored pools (#546 system character classes): the rare system whose name already
    // tells you what kind of space you are flying into.
    private static readonly string[] PirateFirsts = { "Redmaw", "Blacktide", "Cutlass", "Smuggler's", "Vulture's", "Ravager's" };
    private static readonly string[] PirateEpithets = { "Hollow", "Den", "Anchorage", "Refuge", "Cove", "Snare" };
    private static readonly string[] HubFirsts = { "Meridian", "Concord", "Beacon", "Crossway", "Lodestar", "Caravan" };
    private static readonly string[] HubEpithets = { "Cross", "Junction", "Reach", "Landing", "Exchange", "Terminus" };
    private static readonly string[] DesolateFirsts = { "Silent", "Barren", "Forsaken", "Hollow", "Lonely", "Forgotten" };
    private static readonly string[] DesolateEpithets = { "Silence", "Waste", "Stillness", "Remnant", "Expanse", "Vigil" };
    private static readonly string[] BeltFirsts = { "Shattered", "Broken", "Gravel", "Cinder", "Splinter", "Scattered" };
    private static readonly string[] BeltEpithets = { "Ring", "Field", "Scatter", "Girdle", "Belt", "Reef" };

    /// <summary>Per-planet-type syllable flavor, so a proper-named world SOUNDS like its biome (an ice
    /// world reads cold, a lava world harsh). Types not listed fall back to the generic inventory.</summary>
    private static readonly System.Collections.Generic.Dictionary<string, (string[] Onsets, string[] Suffixes)> PlanetFlavors = new()
    {
        ["ice"] = (new[] { "fr", "kr", "th", "v", "sk", "h", "gl" }, new[] { "heim", "fell", "gard", "yr", "os", "ost" }),
        ["tundra"] = (new[] { "fr", "kr", "th", "v", "sk", "h", "gl" }, new[] { "heim", "fell", "gard", "yr", "os", "ost" }),
        ["lava"] = (new[] { "p", "k", "dr", "z", "r", "kr", "v" }, new[] { "ax", "arr", "eth", "ur", "ash", "gar" }),
        ["ashen"] = (new[] { "p", "k", "dr", "z", "r", "kr", "v" }, new[] { "ax", "arr", "eth", "ur", "ash", "gar" }),
        ["desert"] = (new[] { "s", "z", "k", "r", "dr", "sh" }, new[] { "ara", "un", "akh", "ir", "um", "at" }),
        ["badlands"] = (new[] { "s", "z", "k", "r", "dr", "sh" }, new[] { "ara", "un", "akh", "ir", "um", "at" }),
        ["salt_flats"] = (new[] { "s", "z", "k", "r", "dr", "sh" }, new[] { "ara", "un", "akh", "ir", "um", "at" }),
        ["jungle"] = (new[] { "l", "m", "n", "v", "s", "y" }, new[] { "ia", "ora", "une", "elle", "ys", "ana" }),
        ["swamp"] = (new[] { "l", "m", "n", "v", "s", "y" }, new[] { "ia", "ora", "une", "elle", "ys", "ana" }),
        ["fungal"] = (new[] { "l", "m", "n", "v", "s", "y" }, new[] { "ia", "ora", "une", "elle", "ys", "ana" }),
        ["savanna"] = (new[] { "l", "m", "n", "v", "s", "y" }, new[] { "ia", "ora", "une", "elle", "ys", "ana" }),
        ["ocean"] = (new[] { "m", "n", "th", "s", "l", "ner" }, new[] { "mar", "une", "ea", "ys", "aris", "ion" }),
        ["crystal"] = (new[] { "k", "z", "x", "ch", "s", "kr" }, new[] { "iel", "ith", "yne", "ir", "iss", "ax" }),
        ["crystal_living"] = (new[] { "k", "z", "x", "ch", "s", "kr" }, new[] { "iel", "ith", "yne", "ir", "iss", "ax" }),
    };

    /// <summary>Substrings no coined celestial name may contain (EN + DE) — the syllable mill can and
    /// does produce them by accident ("Rapeearr" came up in testing), and this is a kids' game. Only the
    /// NEW deterministic-rng paths retry on a hit; the legacy System.Random creature/flora paths keep
    /// their draw behavior untouched so existing worlds' species names stay stable.</summary>
    private static readonly string[] BlockedSubstrings =
    {
        "rape", "nazi", "anal", "penis", "vagin", "fuck", "shit", "cunt", "porn", "sperm",
        "arsch", "fotze", "hure", "titt",
    };

    private static bool IsClean(string s)
    {
        foreach (var blocked in BlockedSubstrings)
        {
            if (s.Contains(blocked, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A coined star name, e.g. "Tharion" — the bread-and-butter system registry.</summary>
    public static string Star(DeterministicRandom rng) => Word(rng, 2, 3);

    /// <summary>A catalog designation, e.g. "HX-113" — evokes real star catalogs (HD/Gliese/Kepler);
    /// keeping these a minority makes the coined proper names feel earned.</summary>
    public static string Catalog(DeterministicRandom rng)
    {
        char a = CatalogLetters[rng.Range(0, CatalogLetters.Length - 1)];
        char b = CatalogLetters[rng.Range(0, CatalogLetters.Length - 1)];
        int number = rng.NextDouble() < 0.2 ? rng.Range(1000, 9999) : rng.Range(100, 999);
        return $"{a}{b}-{number}";
    }

    /// <summary>A two-part region name, e.g. "Ember Veil" or "Korveth's Reach".</summary>
    public static string Region(DeterministicRandom rng)
    {
        string first = rng.NextDouble() < 0.5 ? Word(rng, 1, 2) + "'s" : RegionFirsts[rng.Range(0, RegionFirsts.Length - 1)];
        return first + " " + RegionEpithets[rng.Range(0, RegionEpithets.Length - 1)];
    }

    /// <summary>An archetype-flavored system name (pirate space sounds menacing, hub space busy) —
    /// or null when the archetype has no registry of its own (the caller falls back to a coined star).</summary>
    public static string? ArchetypeRegion(DeterministicRandom rng, SystemArchetype archetype) => archetype switch
    {
        SystemArchetype.PirateHaven => Pick(rng, PirateFirsts) + " " + Pick(rng, PirateEpithets),
        SystemArchetype.Hub => Pick(rng, HubFirsts) + " " + Pick(rng, HubEpithets),
        SystemArchetype.Desolate => Pick(rng, DesolateFirsts) + " " + Pick(rng, DesolateEpithets),
        SystemArchetype.Belt => Pick(rng, BeltFirsts) + " " + Pick(rng, BeltEpithets),
        _ => null,
    };

    /// <summary>A coined proper name for a landmark planet, flavored by its planet type so the name
    /// hints at the biome from the star map (e.g. ice → "Frosheim", lava → "Pyrrax").</summary>
    public static string PlanetProper(DeterministicRandom rng, string? planetType)
    {
        if (planetType is null || !PlanetFlavors.TryGetValue(planetType, out var flavor))
        {
            return Word(rng, 2, 3);
        }

        for (int attempt = 0; ; attempt++)
        {
            var sb = new StringBuilder();
            int syllables = rng.Range(1, 2);
            for (int i = 0; i < syllables; i++)
            {
                sb.Append(flavor.Onsets[rng.Range(0, flavor.Onsets.Length - 1)]);
                sb.Append(Vowels[rng.Range(0, Vowels.Length - 1)]);
            }

            sb.Append(flavor.Suffixes[rng.Range(0, flavor.Suffixes.Length - 1)]);
            string s = sb.ToString();
            s = char.ToUpperInvariant(s[0]) + s.Substring(1);
            if (IsClean(s) || attempt >= 8)
            {
                return s;
            }
        }
    }

    /// <summary>Twin planet names coined from one stem (e.g. "Kaldra" / "Kaldros") — visually a pair
    /// on the chart (#549), audibly a pair on the map.</summary>
    public static (string A, string B) TwinPair(DeterministicRandom rng)
    {
        for (int attempt = 0; ; attempt++)
        {
            string stem = Word(rng, 1, 2);
            var endings = new[] { ("a", "os"), ("is", "ys"), ("ar", "or"), ("el", "il"), ("ia", "ea") };
            var (ea, eb) = endings[rng.Range(0, endings.Length - 1)];
            if ((IsClean(stem + ea) && IsClean(stem + eb)) || attempt >= 8)
            {
                return (stem + ea, stem + eb);
            }
        }
    }

    /// <summary>A coined moon name, short like the real ones (e.g. "Skell", "Vore").</summary>
    public static string Moon(DeterministicRandom rng) => Word(rng, 1, 2);

    /// <summary>A coined name for a landable asteroid body, e.g. "Skarrak".</summary>
    public static string Asteroid(DeterministicRandom rng) => Word(rng, 2, 2);

    /// <summary>A coined port name for a Hub-archetype trade station, e.g. "Port Halvek".</summary>
    public static string Port(DeterministicRandom rng) => "Port " + Word(rng, 1, 2);

    /// <summary>A coined name for a wrecked ship — wrecks are dead ships, so they carry one.</summary>
    public static string Ship(DeterministicRandom rng) => Word(rng, 2, 3);

    /// <summary>Roman numeral for planet designations ("Tharion II"); supports any realistic count.</summary>
    public static string Roman(int n)
    {
        if (n <= 0)
        {
            return n.ToString();
        }

        var sb = new StringBuilder();
        (int Value, string Symbol)[] table = { (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") };
        foreach (var (value, symbol) in table)
        {
            while (n >= value)
            {
                sb.Append(symbol);
                n -= value;
            }
        }

        return sb.ToString();
    }

    private static string Pick(DeterministicRandom rng, string[] pool) => pool[rng.Range(0, pool.Length - 1)];

    /// <summary>Celestial word shape: open syllables with at most a FINAL coda, capped length, no
    /// triple letters. The creature generator's mid-word codas are great for alien fauna ("Vexilth
    /// krool") but on a star chart they pile into unreadable crunches ("Vrydrernplooss") — map names
    /// must be sayable out loud ("fly to Tharion").</summary>
    private static string Word(DeterministicRandom rng, int minSyllables, int maxSyllables)
    {
        for (int attempt = 0; ; attempt++)
        {
            int syllables = rng.Range(minSyllables, maxSyllables);
            var sb = new StringBuilder();
            for (int i = 0; i < syllables; i++)
            {
                sb.Append(Onsets[rng.Range(0, Onsets.Length - 1)]);
                sb.Append(Vowels[rng.Range(0, Vowels.Length - 1)]);
            }

            if (rng.NextDouble() < 0.6)
            {
                sb.Append(Codas[rng.Range(0, Codas.Length - 1)]);
            }

            string s = sb.ToString();
            s = s.Length == 0 ? "Xel" : char.ToUpperInvariant(s[0]) + s.Substring(1);
            if ((IsClean(s) && s.Length <= 9 && !HasTripleLetter(s)) || attempt >= 8)
            {
                return s;
            }
        }
    }

    private static bool HasTripleLetter(string s)
    {
        for (int i = 2; i < s.Length; i++)
        {
            if (s[i] == s[i - 1] && s[i] == s[i - 2])
            {
                return true;
            }
        }

        return false;
    }
}
