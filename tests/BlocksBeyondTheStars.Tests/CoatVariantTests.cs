// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3972: fixed coats per species — the data model, the per-animal pick, the content check and the wire. No record has
/// coats yet, so these build their own.
/// </summary>
public sealed class CoatVariantTests
{
    private static readonly List<CoatVariant> CatCoats = new()
    {
        new CoatVariant { Weight = 3, BodyRgb = 0x111111, BellyRgb = 0x222222, Hide = "fur" },
        new CoatVariant { Weight = 1, BodyRgb = 0x888888, BellyRgb = 0xDDDDDD, Pattern = "Tabby", PatternRgb = 0x333333 },
    };

    [Fact]
    public void ThePick_IsStablePerAnimal_FollowsTheWeights_AndUsesEveryMaskLayout()
    {
        Assert.Null(CoatVariants.Pick(new List<CoatVariant>(), 5));
        Assert.Null(CoatVariants.Pick(null, 5));

        int first = 0;
        var masks = new HashSet<int>();
        const int N = 8000;
        for (int i = 0; i < N; i++)
        {
            int seed = CoatVariants.AnimalSeedOf("wild:e" + i);
            Assert.NotEqual(0, seed);
            Assert.Same(CoatVariants.Pick(CatCoats, seed), CoatVariants.Pick(CatCoats, seed));
            first += CoatVariants.PickIndex(CatCoats, seed) == 0 ? 1 : 0;
            int mask = CoatVariants.MaskVariant(seed);
            Assert.InRange(mask, 0, CoatVariants.MaskVariants - 1);
            masks.Add(mask);
        }

        Assert.InRange(first / (double)N, 0.70, 0.80); // weight 3 of 4
        Assert.Equal(CoatVariants.MaskVariants, masks.Count);
        Assert.Equal(CoatVariants.AnimalSeedOf("pen:a:1"), CoatVariants.AnimalSeedOf("pen:a:1"));
    }

    [Fact]
    public void TheContentCheck_NamesUnknownPatternsAndHides_AndBadWeights()
    {
        Assert.Empty(CoatVariants.Problems("cat", CatCoats));
        Assert.Empty(CoatVariants.Problems("cat", new[] { new CoatVariant { Pattern = CoatVariants.PlainPattern } }));

        var bad = CoatVariants.Problems("cat", new[]
        {
            new CoatVariant { Weight = 0, Pattern = "Stripy", Hide = "velour" },
        }).ToList();
        Assert.Equal(3, bad.Count);
        Assert.Contains(bad, p => p.Contains("'Stripy'"));
        Assert.Contains(bad, p => p.Contains("'velour'"));
        Assert.Contains(bad, p => p.Contains("weight 0"));
    }

    [Fact]
    public void TheWire_CarriesThePickedCoat_AndNothingForASpeciesWithoutCoats()
    {
        var plain = new CreatureSpecies { ColorRgb = 0x123456, BellyRgb = 0x654321, Hide = "shaggy" };
        var n = new NetCreature();
        CreatureWireMapping.WriteSpecies(n, plain, ("t", 1), animalSeed: 99);
        Assert.Equal(string.Empty, n.CoatPattern);
        Assert.Equal(0x123456, n.ColorRgb);
        Assert.Equal("shaggy", n.Hide);

        var cat = new CreatureSpecies { ColorRgb = 0x123456, BellyRgb = 0x654321, Hide = "shaggy", Coats = CatCoats };
        for (int i = 0; i < 40; i++)
        {
            int seed = CoatVariants.AnimalSeedOf("e" + i);
            var coat = CoatVariants.Pick(CatCoats, seed)!;
            var wire = new NetCreature();
            CreatureWireMapping.WriteSpecies(wire, cat, ("t", 1), seed);
            Assert.Equal(coat.BodyRgb, wire.ColorRgb);
            Assert.Equal(coat.BellyRgb, wire.BellyRgb);
            Assert.Equal(coat.Hide.Length > 0 ? coat.Hide : "shaggy", wire.Hide);
            Assert.Equal(coat.Pattern.Length > 0 ? coat.Pattern : CoatVariants.PlainPattern, wire.CoatPattern);
            Assert.Equal(coat.PatternRgb, wire.CoatPatternRgb);
            Assert.Equal(CoatVariants.MaskVariant(seed), wire.CoatMask);
        }
    }
}
