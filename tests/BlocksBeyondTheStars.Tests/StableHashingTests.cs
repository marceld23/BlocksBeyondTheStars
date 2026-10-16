// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Linq;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#2686: the well-mixed hash. The legacy FNV stays bit-identical (worlds are derived from it), and the mixed
/// form spreads keys that differ only in a trailing counter evenly — the case where the raw FNV gave 500/10/90.</summary>
public sealed class StableHashingTests
{
    [Theory]
    [InlineData("")]
    [InlineData("crop:12:34:7")]
    [InlineData("sys1-p3")]
    [InlineData("Karth Village:mission:4")]
    public void Fnv64_IsBitIdentical_ToWorldGeneratorStableHash(string key)
    {
        Assert.Equal(unchecked((ulong)WorldGenerator.StableHash(key)), StableHashing.Fnv64(key));
    }

    [Fact]
    public void TheRawFnv_IsSkewed_ForTrailingCounters_WhichIsWhyTheMixedFormExists()
    {
        // Pins the defect this class works around, so nobody "simplifies" back to the raw hash.
        int[] raw = new int[3];
        for (int i = 0; i < 600; i++)
        {
            raw[(int)((WorldGenerator.StableHash($"crop:12:34:{i}") & 0x7fffffff) % 3)]++;
        }

        Assert.True(raw.Max() > 400, $"raw FNV % 3 = {string.Join("/", raw)}");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(10)]
    public void Index_SpreadsTrailingCounterKeys_Evenly(int n)
    {
        const int Keys = 6000;
        int[] counts = new int[n];
        for (int i = 0; i < Keys; i++)
        {
            int k = StableHashing.Index($"crop:12:34:{i}", n);
            Assert.InRange(k, 0, n - 1);
            counts[k]++;
        }

        double expected = Keys / (double)n;
        foreach (int c in counts)
        {
            Assert.InRange(c, expected * 0.85, expected * 1.15); // ±15 % — the raw FNV misses this by a mile
        }
    }

    [Fact]
    public void Seed32_GivesUnrelatedFirstDraws_ForNeighbouringKeys()
    {
        // System.Random seeded with related seeds couples its first draws (the mission boards' 4-cycle); with Seed32
        // a neighbour repeats the same Next(7) about as often as chance (1/7).
        int repeats = 0;
        int? last = null;
        for (int i = 0; i < 2000; i++)
        {
            int draw = new Random(StableHashing.Seed32($"Karth Village:mission:{i}")).Next(7);
            if (last == draw)
            {
                repeats++;
            }

            last = draw;
        }

        Assert.InRange(repeats / 1999.0, 0.10, 0.19);
    }

    [Fact]
    public void Unit_IsInRange_AndSeedSaltedForms_DifferPerWorld()
    {
        for (int i = 0; i < 1000; i++)
        {
            double u = StableHashing.Unit($"fauna:{i}");
            Assert.InRange(u, 0.0, 0.9999999999);
        }

        Assert.NotEqual(StableHashing.Mixed("sys0-p0", 1L), StableHashing.Mixed("sys0-p0", 2L));
        Assert.Equal(StableHashing.Mixed("sys0-p0", 42L), StableHashing.Mixed("sys0-p0", 42L));
        Assert.Equal(0, StableHashing.Index("anything", 1));
        Assert.Equal(0, StableHashing.Index("anything", 0));
    }

    /// <summary>Known answers: the outputs are bit-frozen once shipped (generation-26 planet sizes, colour families and
    /// re-stamped settlement picks derive from them). If one of these moves, every such save changes under its players —
    /// a different mixing needs a new function and a new generation, never an edit here.</summary>
    [Fact]
    public void Outputs_AreBitFrozen()
    {
        Assert.Equal(0UL, StableHashing.Mix64(0UL));
        Assert.Equal(0x5692161D100B05E5UL, StableHashing.Mix64(1UL)); // the published SplitMix64 finaliser of 1
        Assert.Equal(0x4E062702EC929EEAUL, StableHashing.Mix64(0xDEADBEEFUL));
        Assert.Equal(0x552D3FB62B3C344FUL, StableHashing.Mixed(string.Empty));
        Assert.Equal(0x3B8D438D77067224UL, StableHashing.Mixed("sys0-p0"));
        Assert.Equal(0x8D3779EB09668427UL, StableHashing.Mixed("sys0-p0", 42L));
        Assert.Equal(0x6811050A326802CFUL, StableHashing.Mixed("sys0-p0", -7L));

        Assert.Equal(99, StableHashing.Index(0xFEDCBA9876543210UL, 100)); // the high 32 bits, multiply-shift
        Assert.Equal(5, StableHashing.Index(0x8000000000000000UL, 10));
        Assert.Equal(2, StableHashing.Index("crop:12:34:7", 7));
        Assert.Equal(431, StableHashing.Index("sys1-p3", 42L, 1000));

        Assert.Equal(0.0, StableHashing.Unit(0UL));
        Assert.Equal((double)((1UL << 53) - 1) / (1UL << 53), StableHashing.Unit(ulong.MaxValue)); // the top 53 bits
        Assert.Equal(0.12283818626435739, StableHashing.Unit("fauna:3"));
        Assert.Equal(0.643039836153508, StableHashing.Unit("fauna:3", 9L));

        Assert.Equal(215891466, StableHashing.Seed32("Karth Village:mission:4"));
        Assert.Equal(-1573868566, StableHashing.Seed32("Karth Village:mission:4", 9L));
    }

    [Fact]
    public void MixedCropPick_IsBitFrozen()
    {
        // #2689: a placement record with HashRevision 1 re-stamps its greenhouses with this pick on every load.
        Assert.Equal(1, SettlementGenerator.CropIndex("1", 42, 0, 5, legacy: false));
        Assert.Equal(0, SettlementGenerator.CropIndex("1", 42, 1, 5, legacy: false));
        Assert.Equal(6, SettlementGenerator.CropIndex("2", 4242, 3, 7, legacy: false));
        Assert.Equal(3, SettlementGenerator.CropIndex("3", -9, 2, 4, legacy: false));
    }
}
