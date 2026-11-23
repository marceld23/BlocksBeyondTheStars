// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3972: the look of a creature — body, belly, pattern, pattern colour, hide per part, mask seed, glow — and the body-plan
/// decisions behind it (which patterns a body can carry, its voice pool, its footprint) are pinned over many rolled and
/// authored species: every body plan (and an unknown one), every habitat and temper, every world theme, motif and pattern
/// group, with and without a fauna palette, with every tile available and with some missing.
/// </summary>
public sealed class CreatureLookResolverTests
{
    /// <summary>The digest of the looks the decision functions gave BEFORE the look resolver and the body-plan table existed
    /// (computed with <c>CreatureCoat.PatternFor</c>/<c>PatternRgb</c>/<c>MaskSeed</c>, <c>CreatureHides.Pick</c>,
    /// <c>GlowMasks</c>, the string-compared <c>PatternsFor</c>, <c>CreatureVoices.PoolFor</c> and <c>CreatureFootprint</c>).
    /// A change here is a change of every existing world's fauna.</summary>
    private const string LegacyDigest = "B096C8441FE48ABF";

    private static readonly string[] Plans = Enum.GetNames(typeof(CreatureBodyPlan)).Concat(new[] { "", "Unknown" }).ToArray();
    private static readonly string[] Habitats = Enum.GetNames(typeof(CreatureHabitat));
    private static readonly string[] Tempers = Enum.GetNames(typeof(CreatureTemperament));
    private static readonly string[] NamedHides = { "", "", "", "shaggy", "plated", "fur", "scales", "petal", "unknownhide" };
    private static readonly int[] Palette = { 0x8FD8F0, 0xB5F2D8, 0xD8C8F8 };

    [Fact]
    public void EveryLook_MatchesTheDecisionsOfBeforeTheResolver()
    {
        var sb = new StringBuilder();
        foreach (var (c, look, tiles) in Cases())
        {
            sb.Append(Describe(c, look, tiles)).Append('\n');
        }

        string digest = Fnv(sb.ToString()).ToString("X16", CultureInfo.InvariantCulture);
        Assert.Equal(LegacyDigest, digest);
    }

    /// <summary>Many creatures, each with a world look (or none) and a tile availability.</summary>
    private static IEnumerable<(NetCreature C, FaunaThemes.Look? Look, bool AllTiles)> Cases()
    {
        var looks = new List<FaunaThemes.Look?> { null };
        foreach (string theme in FaunaThemes.Themes.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            for (int g = 0; g < FaunaThemes.PatternGroups.Count; g++)
            {
                string family = FaunaThemes.MotifFamilies[(g * 5 + theme.Length) % FaunaThemes.MotifFamilies.Count];
                looks.Add(new FaunaThemes.Look(theme, family, FaunaThemes.PatternGroups[g]));
                looks.Add(new FaunaThemes.Look(theme, family, FaunaThemes.PatternGroups[g], Palette));
            }
        }

        var rng = new Random(3972);
        for (int i = 0; i < 6000; i++)
        {
            var c = RandomCreature(rng, i);
            yield return (c, looks[rng.Next(looks.Count)], rng.Next(3) != 0);
        }
    }

    private static NetCreature RandomCreature(Random rng, int i)
    {
        int kind = rng.Next(10);
        string id = kind == 0 ? "au_probe" + rng.Next(5) : kind == 1 ? "gi_colossus" : "sp" + rng.Next(9);
        return new NetCreature
        {
            Id = "e" + i,
            SpeciesId = id,
            VoiceSeed = rng.Next(12) == 0 ? 0 : rng.Next(int.MinValue, int.MaxValue),
            BodyPlan = Plans[rng.Next(Plans.Length)],
            Habitat = Habitats[rng.Next(Habitats.Length)],
            Temperament = Tempers[rng.Next(Tempers.Length)],
            Legs = rng.Next(4) == 0 ? 0 : rng.Next(1, 9),
            HasTail = rng.Next(2) == 0,
            HasCrest = rng.Next(3) == 0,
            HasFins = rng.Next(4) == 0,
            HasWings = rng.Next(4) == 0,
            Glows = rng.Next(4) == 0,
            Tentacles = rng.Next(5) == 0 ? rng.Next(1, 8) : 0,
            Eyes = rng.Next(5),
            Size = (float)(0.3 + rng.NextDouble() * 4.0),
            ColorRgb = rng.Next(0x1000000),
            BellyRgb = rng.Next(0x1000000),
            Hide = NamedHides[rng.Next(NamedHides.Length)],
        };
    }

    private static bool PartialTiles(string key) => (CreatureCoat.StableIdHash(key) % 5) != 0;

    private static string Describe(NetCreature c, FaunaThemes.Look? look, bool allTiles)
    {
        Func<string, bool> tiles = allTiles ? _ => true : PartialTiles;
        uint seed = CreatureCoat.SpeciesSeed(c.SpeciesId, c.VoiceSeed);
        var pattern = CreatureCoat.PatternFor(c, look, tiles);
        int mark = CreatureCoat.PatternRgb(seed, c.ColorRgb, pattern, look);
        var hides = CreatureHides.Pick(c, tiles, look);
        var glow = GlowMasks.PatternFor(c);
        int glowRgb = GlowMasks.ColourFor(seed, c.ColorRgb);
        string options = string.Join(",", CreatureCoat.PatternsFor(c, allTiles ? tiles : null));
        return string.Join("|",
            pattern, mark.ToString("X6", CultureInfo.InvariantCulture), c.ColorRgb, c.BellyRgb,
            hides.Torso, hides.Head, hides.Limbs, hides.Ornament, CreatureCoat.MaskSeed(c), glow, glowRgb, options,
            Voice(c), Footprint(c));
    }

    private static string Voice(NetCreature c)
    {
        var pool = CreatureVoices.PoolFor(c.Habitat, c.BodyPlan);
        return pool.Count + ":" + (pool.Count > 0 ? pool[0] : "") + ":" + (CreatureVoices.SungPool(c.BodyPlan)?.Length ?? -1);
    }

    private static string Footprint(NetCreature c)
    {
        if (!Enum.TryParse(c.BodyPlan, out CreatureBodyPlan plan) || !Enum.IsDefined(typeof(CreatureBodyPlan), plan))
        {
            return "-";
        }

        var sp = new CreatureSpecies
        {
            BodyPlan = plan,
            Size = c.Size,
            BodySegments = 1 + (c.Legs % 4),
            NeckLength = c.Eyes % 4,
            HeadRatio = 1f + c.Eyes * 0.2f,
            SnoutLength = c.Tentacles * 0.1f,
            GiantHeight = c.Size * 12f,
        };
        return CreatureFootprint.Radius(sp, 1.1f).ToString("R", CultureInfo.InvariantCulture) + ":"
            + CreatureFootprint.HeadReach(sp, 0.9f).ToString("R", CultureInfo.InvariantCulture);
    }

    private static ulong Fnv(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char ch in s)
        {
            h = unchecked((h ^ ch) * 1099511628211UL);
        }

        return h;
    }
}
