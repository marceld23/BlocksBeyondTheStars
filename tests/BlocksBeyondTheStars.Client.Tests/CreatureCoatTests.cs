// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The coat decisions (#2829, #2830, #2831): the species seed and proportions, which pattern a species wears and in which
/// colour, how each part shows it, and the per-animal jitter.
/// </summary>
public sealed class CreatureCoatTests
{
    private static NetCreature Species(int voiceSeed, string plan = "Standard", string id = "sp1", int legs = 4,
        bool tail = true, bool crest = false, int eyes = 2, int rgb = 0x6A8F3C)
        => new NetCreature
        {
            Id = "e" + voiceSeed,
            SpeciesId = id,
            VoiceSeed = voiceSeed,
            BodyPlan = plan,
            Legs = legs,
            HasTail = tail,
            HasCrest = crest,
            Eyes = eyes,
            ColorRgb = rgb,
        };

    [Fact]
    public void TheSpeciesSeed_IsTheVoiceSeed_ForARolledSpecies_AndTheIdHashOtherwise()
    {
        Assert.Equal(12345u, CreatureCoat.SpeciesSeed("sp4", 12345));
        Assert.Equal((uint)CreatureCoat.StableIdHash("au_leni"), CreatureCoat.SpeciesSeed("au_leni", 12345));
        Assert.Equal((uint)CreatureCoat.StableIdHash("sp4"), CreatureCoat.SpeciesSeed("sp4", 0)); // a legacy server
    }

    [Fact]
    public void Proportions_OfAnAuthoredSpecies_AreUnchanged_ARolledOneFollowsItsWorldSeed()
    {
        // The designed look keeps the exact old hash; a rolled species' build differs per world and per slot no more.
        Assert.Equal(CreatureCoat.StableIdHash("au_sreekmakra"), CreatureCoat.ProportionHash("au_sreekmakra", 999));
        Assert.Equal(CreatureCoat.StableIdHash("sp2"), CreatureCoat.ProportionHash("sp2", 0));
        Assert.Equal(CreatureCoat.ProportionHash("sp2", 777), CreatureCoat.ProportionHash("sp8", 777));
        Assert.True(CreatureCoat.ProportionHash("sp2", 777) >= 0);

        // Consecutive seeds must not walk the proportion bits in lockstep (the sp0..sp8 bug).
        var heads = Enumerable.Range(500, 9).Select(s => (CreatureCoat.ProportionHash("sp0", s) >> 2) & 7).ToList();
        Assert.True(heads.Distinct().Count() >= 4, string.Join(",", heads));
    }

    [Fact]
    public void ThereAreThirtyThreePatterns_PlusNone_SixteenOfThemTiles()
    {
        var all = Enum.GetValues(typeof(CoatPattern)).Cast<CoatPattern>().ToList();
        Assert.Equal(34, all.Count);
        Assert.Contains(CoatPattern.None, all);
        Assert.Equal(8, CreatureCoat.SurfacePatterns.Count);
        Assert.Equal(16, CreatureCoat.TilePatterns.Count);
        Assert.All(CreatureCoat.TilePatterns, p => Assert.True(CreatureCoat.IsTilePattern(p)));
        Assert.All(CreatureCoat.SurfacePatterns, p => Assert.False(CreatureCoat.IsTilePattern(p)));
        Assert.Equal(16, CreatureCoat.TilePatterns.Select(CoatMasks.TileKey).Distinct().Count());
    }

    [Fact]
    public void AStandardBody_CanWearEveryPattern_AndEachShowsUp()
    {
        // Without tiles the code patterns and the placed ones (with the countershading, #2863); with tiles all of them.
        Assert.Equal(17, CreatureCoat.PatternsFor(Species(1, crest: true)).Count);
        var full = CreatureCoat.PatternsFor(Species(1, crest: true), _ => true);
        Assert.Equal(33, full.Count);

        var seen = new Dictionary<CoatPattern, int>();
        const int N = 12000;
        for (int s = 1; s <= N; s++)
        {
            var p = CreatureCoat.PatternFor(Species(s, crest: true), null, _ => true);
            seen[p] = seen.TryGetValue(p, out int n) ? n + 1 : 1;
        }

        Assert.Equal(34, seen.Count);
        Assert.InRange(seen[CoatPattern.None] / (double)N, 0.12, 0.22); // about one in six
    }

    [Fact]
    public void AMissingTile_IsSkipped_AndOnlyTheSpeciesThatPickedItChange()
    {
        bool OnlyGiraffe(string key) => key == CoatMasks.TileKey(CoatPattern.Giraffe);
        int changed = 0;
        for (int s = 1; s <= 3000; s++)
        {
            var all = CreatureCoat.PatternFor(Species(s), null, _ => true);
            var some = CreatureCoat.PatternFor(Species(s), null, OnlyGiraffe);
            Assert.True(!CreatureCoat.IsTilePattern(some) || some == CoatPattern.Giraffe, some.ToString());
            if (all != some)
            {
                changed++;
                Assert.True(CreatureCoat.IsTilePattern(all), $"{all} changed to {some} though its tile is there");
            }
        }

        Assert.True(changed > 0);
    }

    [Fact]
    public void TheWorldMotif_FavoursItsPatternGroup()
    {
        var spots = new FaunaThemes.Look("temperate", "fur", "spots");
        int inGroup = 0, patterned = 0;
        for (int s = 1; s <= 4000; s++)
        {
            var p = CreatureCoat.PatternFor(Species(s), spots, _ => true);
            if (p == CoatPattern.None)
            {
                continue;
            }

            patterned++;
            inGroup += FaunaThemes.InGroup(p, "spots") ? 1 : 0;
        }

        // Plain share: 8 of 33 patterns are spots (~24 %); a third of the species pick from the group (~50 % in all).
        Assert.InRange(inGroup / (double)patterned, 0.38, 0.62);
    }

    [Fact]
    public void PlacedPatterns_OnlyGoWhereTheirPartExists()
    {
        var worm = CreatureCoat.PatternsFor(Species(1, plan: "Worm", legs: 0, tail: false));
        Assert.DoesNotContain(CoatPattern.Socks, worm);
        Assert.DoesNotContain(CoatPattern.Saddle, worm);
        Assert.Contains(CoatPattern.TailRings, worm); // the worm's body is its tail chain: a banded worm

        var noTail = CreatureCoat.PatternsFor(Species(1, tail: false));
        Assert.DoesNotContain(CoatPattern.TailRings, noTail);

        var blind = CreatureCoat.PatternsFor(Species(1, eyes: 0));
        Assert.DoesNotContain(CoatPattern.EyeMask, blind);

        var medusa = CreatureCoat.PatternsFor(Species(1, plan: "Medusa", legs: 0));
        Assert.DoesNotContain(CoatPattern.BellyBlaze, medusa);
        Assert.DoesNotContain(CoatPattern.Socks, medusa);

        var sandworm = CreatureCoat.PatternsFor(Species(1, plan: "Sandworm", legs: 0));
        Assert.Equal(new[] { CoatPattern.CrestBand, CoatPattern.BellyBlaze }, sandworm);
    }

    [Fact]
    public void AnAuthoredSpecies_WearsNoPattern()
    {
        for (int s = 1; s < 100; s++)
        {
            Assert.Equal(CoatPattern.None, CreatureCoat.PatternFor(Species(s, id: "au_leni")));
        }
    }

    [Theory]
    [InlineData(CoatPattern.EyeMask, CoatPart.Head, CoatCoverage.Masked)]
    [InlineData(CoatPattern.EyeMask, CoatPart.Torso, CoatCoverage.None)]
    [InlineData(CoatPattern.EyeMask, CoatPart.Jaw, CoatCoverage.None)]
    [InlineData(CoatPattern.TailRings, CoatPart.Tail, CoatCoverage.Masked)]
    [InlineData(CoatPattern.TailRings, CoatPart.Torso, CoatCoverage.None)]
    [InlineData(CoatPattern.Socks, CoatPart.LowerLimb, CoatCoverage.Full)]
    [InlineData(CoatPattern.Socks, CoatPart.Limb, CoatCoverage.None)]
    [InlineData(CoatPattern.BellyBlaze, CoatPart.Belly, CoatCoverage.Full)]
    [InlineData(CoatPattern.BellyBlaze, CoatPart.Torso, CoatCoverage.None)]
    [InlineData(CoatPattern.CrestBand, CoatPart.Ornament, CoatCoverage.Full)]
    [InlineData(CoatPattern.Saddle, CoatPart.Overlay, CoatCoverage.Full)]
    [InlineData(CoatPattern.Saddle, CoatPart.Torso, CoatCoverage.None)]
    [InlineData(CoatPattern.Spots, CoatPart.Torso, CoatCoverage.Masked)]
    [InlineData(CoatPattern.Spots, CoatPart.LowerLimb, CoatCoverage.Masked)]
    [InlineData(CoatPattern.Spots, CoatPart.Belly, CoatCoverage.None)]
    [InlineData(CoatPattern.Spots, CoatPart.Jaw, CoatCoverage.None)]
    [InlineData(CoatPattern.Spots, CoatPart.Ornament, CoatCoverage.None)]
    [InlineData(CoatPattern.None, CoatPart.Torso, CoatCoverage.None)]
    public void Coverage_FollowsThePartRole(CoatPattern pattern, CoatPart part, CoatCoverage expected)
        => Assert.Equal(expected, CreatureCoat.Coverage(pattern, part));

    [Fact]
    public void ThePatternColour_ReadsAgainstTheBody_AndIsNeverGarish()
    {
        var rng = new Random(7);
        var patterns = Enum.GetValues(typeof(CoatPattern)).Cast<CoatPattern>().Where(p => p != CoatPattern.None).ToList();
        for (int i = 0; i < 4000; i++)
        {
            int body = rng.Next(0, 0x1000000);
            var pattern = patterns[i % patterns.Count];
            int rgb = CreatureCoat.PatternRgb((uint)rng.Next(), body, pattern);
            CreatureCoat.Unpack(body, out float br, out float bg, out float bb);
            CreatureCoat.Unpack(rgb, out float pr, out float pg, out float pb);
            float step = Math.Abs(CreatureCoat.Luma(pr, pg, pb) - CreatureCoat.Luma(br, bg, bb));
            Assert.True(step >= CreatureCoat.MinLumaStep - 0.01f, $"body {body:X6} → {rgb:X6} ({pattern}) differs by {step:0.000}");
            CreatureCoat.RgbToHsv(pr, pg, pb, out _, out float sat, out float val);
            Assert.True(sat <= 0.92f || val < 0.2f, $"{rgb:X6} is too saturated"); // a near-black's saturation is noise
        }
    }

    [Fact]
    public void TheCapeAndTheMaskAreDarker_TheBlazeLighter()
    {
        for (uint s = 1; s < 300; s++)
        {
            const int Body = 0x8A9A5B;
            CreatureCoat.Unpack(Body, out float br, out float bg, out float bb);
            float body = CreatureCoat.Luma(br, bg, bb);
            float Of(CoatPattern p)
            {
                CreatureCoat.Unpack(CreatureCoat.PatternRgb(s, Body, p), out float r, out float g, out float b);
                return CreatureCoat.Luma(r, g, b);
            }

            Assert.True(Of(CoatPattern.TwoTone) < body);
            Assert.True(Of(CoatPattern.EyeMask) < body);
            Assert.True(Of(CoatPattern.BellyBlaze) > body);
        }
    }

    [Fact]
    public void Jitter_IsAFewBuckets_WithinFivePercent_AndStablePerAnimal()
    {
        var buckets = Enumerable.Range(0, 2000).Select(i => CreatureCoat.JitterBucket("c" + i)).ToList();
        Assert.Equal(CreatureCoat.JitterBuckets, buckets.Distinct().Count());
        Assert.Equal(CreatureCoat.JitterBucket("c42"), CreatureCoat.JitterBucket("c42"));
        for (int b = 0; b < CreatureCoat.JitterBuckets; b++)
        {
            CreatureCoat.JitterRgb(b, out float r, out float g, out float bl);
            Assert.All(new[] { r, g, bl }, v => Assert.InRange(v, 0.949f, 1.051f));
        }
    }

    [Fact]
    public void PinsV_ForTheEyeMaskedHead_AndTheCountershadedFlanks()
    {
        Assert.True(CreatureCoat.PinsV(CoatPattern.EyeMask, CoatPart.Head));
        Assert.False(CreatureCoat.PinsV(CoatPattern.EyeMask, CoatPart.Torso));
        Assert.False(CreatureCoat.PinsV(CoatPattern.Spots, CoatPart.Head));
        Assert.True(CreatureCoat.PinsV(CoatPattern.Countershade, CoatPart.Torso));
        Assert.True(CreatureCoat.PinsV(CoatPattern.Countershade, CoatPart.Tail));
        Assert.False(CreatureCoat.PinsV(CoatPattern.Countershade, CoatPart.Limb));
        Assert.False(CreatureCoat.PinsV(CoatPattern.Countershade, CoatPart.Neck)); // a titan's neck is a stack of segments
        Assert.Equal(CoatCoverage.None, CreatureCoat.Coverage(CoatPattern.Countershade, CoatPart.Neck));
    }

    [Fact]
    public void Countershading_OnlyOnSmallBoxBodies_AndTheBackIsAlwaysDarker()
    {
        Assert.Contains(CoatPattern.Countershade, CreatureCoat.PatternsFor(Species(1)));
        Assert.DoesNotContain(CoatPattern.Countershade, CreatureCoat.PatternsFor(Species(1, plan: "Titan")));
        var big = Species(1);
        big.Size = 2.6f; // its flank would need three or more repeats on a pinned mesh
        Assert.DoesNotContain(CoatPattern.Countershade, CreatureCoat.PatternsFor(big));

        var rng = new Random(5);
        for (int i = 0; i < 3000; i++)
        {
            int body = rng.Next(0, 0x1000000);
            CreatureCoat.Unpack(body, out float br, out float bg, out float bb);
            if (CreatureCoat.Luma(br, bg, bb) < 0.2f)
            {
                continue; // too dark to go darker readably: there the shade goes lighter, like the cape's
            }

            int mark = CreatureCoat.PatternRgb((uint)rng.Next(), body, CoatPattern.Countershade);
            CreatureCoat.Unpack(mark, out float mr, out float mg, out float mb);
            Assert.True(CreatureCoat.Luma(mr, mg, mb) < CreatureCoat.Luma(br, bg, bb), $"{body:X6} → {mark:X6}");
        }
    }

    [Fact]
    public void BoldColours_AboutOneInFive_ReadAndAreBlackWhiteCreamOrWarning()
    {
        var rng = new Random(11);
        int bold = 0;
        const int N = 6000;
        var allowed = new HashSet<int> { 0x1E1B1A, 0x34302E, 0xF2F0E8, 0xE8D9B0, 0xF2C230, 0xD8342B };
        for (int i = 0; i < N; i++)
        {
            uint seed = (uint)rng.Next();
            int body = rng.Next(0, 0x1000000);
            if (CreatureCoat.BoldRgb(seed, body, CoatPattern.Spots) is not int mark)
            {
                continue;
            }

            bold++;
            Assert.Contains(mark, allowed);
            CreatureCoat.Unpack(body, out float br, out float bg, out float bb);
            CreatureCoat.Unpack(mark, out float mr, out float mg, out float mb);
            Assert.True(Math.Abs(CreatureCoat.Luma(mr, mg, mb) - CreatureCoat.Luma(br, bg, bb)) >= CreatureCoat.MinLumaStep);
        }

        Assert.InRange(bold / (double)N, 0.13, 0.22);
        Assert.Null(CreatureCoat.BoldRgb(1, 0x808080, CoatPattern.None));
    }

    [Fact]
    public void AWarmVividBody_GetsBlackWarningMarks()
    {
        int checkedSeeds = 0;
        for (uint s = 1; s < 3000 && checkedSeeds < 50; s++)
        {
            if (!CreatureCoat.IsBold(s))
            {
                continue;
            }

            checkedSeeds++;
            int? mark = CreatureCoat.BoldRgb(s, 0xF0B020, CoatPattern.Spots); // a yellow body: a wasp
            Assert.True(mark is 0x1E1B1A or 0x34302E, $"{mark:X6}");
        }

        Assert.True(checkedSeeds > 0);
    }

    [Fact]
    public void Placement_IsStablePerAnimal_AndSpreadsOverTheVariants()
    {
        var variants = Enumerable.Range(0, 400).Select(i => CreatureCoat.PlacementVariant("e" + i)).ToList();
        Assert.Equal(CreatureCoat.PlacementVariants, variants.Distinct().Count());
        Assert.Equal(CreatureCoat.PlacementVariant("e7"), CreatureCoat.PlacementVariant("e7"));
        int differs = Enumerable.Range(0, 200).Count(i => CreatureCoat.PlacementVariant("e" + i) != CreatureCoat.PlacementVariant("e" + (i + 1)));
        Assert.True(differs > 150, "neighbouring animals should mostly wear different placements");
    }
}
