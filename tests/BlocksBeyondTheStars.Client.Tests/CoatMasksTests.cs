// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The coat masks (#2830): drawn in code at 64², deterministic, 0/1 per texel, a believable coverage, tileable where the
/// texture repeats, and the placed bands where they belong.
/// </summary>
public sealed class CoatMasksTests
{
    private const int S = CoatMasks.Size;

    public static TheoryData<CoatPattern> Surface()
    {
        var data = new TheoryData<CoatPattern>();
        foreach (var p in CreatureCoat.SurfacePatterns)
        {
            data.Add(p);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Surface))]
    public void ASurfaceMask_IsDeterministic_BinaryAndPlausiblyCovered(CoatPattern pattern)
    {
        foreach (uint seed in new uint[] { 1, 77, 0xDEADBEEF, 123456789 })
        {
            var a = CoatMasks.Draw(pattern, CoatPart.Torso, seed);
            var b = CoatMasks.Draw(pattern, CoatPart.Torso, seed);
            Assert.Equal(S * S, a.Length);
            Assert.Equal(a, b);
            Assert.All(a, v => Assert.True(v == 0f || v == 1f));
            double coverage = a.Average();
            Assert.InRange(coverage, 0.03, 0.7);
        }
    }

    [Theory]
    [MemberData(nameof(Surface))]
    public void ASurfaceMask_Tiles_TheSeamIsNoWorseThanTheInterior(CoatPattern pattern)
    {
        foreach (uint seed in new uint[] { 3, 991, 0xC0FFEE })
        {
            var m = CoatMasks.Draw(pattern, CoatPart.Torso, seed);
            AssertSeamless(m, horizontal: true);
            AssertSeamless(m, horizontal: false);
        }
    }

    [Fact]
    public void DifferentSpecies_GetDifferentLayouts()
    {
        var a = CoatMasks.Draw(CoatPattern.Spots, CoatPart.Torso, 1);
        var b = CoatMasks.Draw(CoatPattern.Spots, CoatPart.Torso, 2);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void StripesRingALimb_TheMaskIsTransposed()
    {
        var torso = CoatMasks.Draw(CoatPattern.CrossStripes, CoatPart.Torso, 9);
        var leg = CoatMasks.Draw(CoatPattern.CrossStripes, CoatPart.Limb, 9);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Assert.Equal(torso[y * S + x], leg[x * S + y]);
            }
        }

        Assert.Equal(CoatMasks.Variant.AlongY, CoatMasks.VariantOf(CoatPattern.CrossStripes, CoatPart.LowerLimb));
        Assert.Equal(CoatMasks.Variant.AlongZ, CoatMasks.VariantOf(CoatPattern.CrossStripes, CoatPart.Tail));
    }

    [Fact]
    public void TheEyeBand_SitsAtEyeHeight_AndTilesAlongU()
    {
        var m = CoatMasks.Draw(CoatPattern.EyeMask, CoatPart.Head, 5);
        for (int x = 0; x < S; x++)
        {
            Assert.Equal(0f, m[0 * S + x]);                 // the bottom rows (the pinned bottom face) stay clear
            Assert.Equal(0f, m[(S - 1) * S + x]);           // and so do the top rows (the pinned top face)
            Assert.Equal(1f, m[(CoatMasks.EyeBandLow + CoatMasks.EyeBandHigh) / 2 * S + x]);
        }

        // Nothing above the pinned top rows' start or below the bottom rows' end.
        int top = (int)Math.Floor((1f - TiledCube.PinnedCapRows) * S), bottom = (int)Math.Ceiling(TiledCube.PinnedCapRows * S);
        for (int y = 0; y < S; y++)
        {
            if (y >= top || y < bottom)
            {
                Assert.All(Enumerable.Range(0, S), x => Assert.Equal(0f, m[y * S + x]));
            }
        }

        AssertSeamless(m, horizontal: true);
    }

    [Fact]
    public void TailRings_AreBandsAcrossU()
    {
        var m = CoatMasks.Draw(CoatPattern.TailRings, CoatPart.Tail, 5);
        for (int x = 0; x < S; x++)
        {
            float first = m[x];
            Assert.All(Enumerable.Range(0, S), y => Assert.Equal(first, m[y * S + x])); // constant down a column
        }

        Assert.InRange(m.Average(), 0.3, 0.55);
    }

    [Fact]
    public void NoneIsEmpty_AndFullIsFull()
    {
        Assert.All(CoatMasks.Draw(CoatPattern.Spots, CoatPart.Belly, 1), v => Assert.Equal(0f, v));
        Assert.All(CoatMasks.Draw(CoatPattern.Socks, CoatPart.LowerLimb, 1), v => Assert.Equal(1f, v));
        Assert.All(CoatMasks.Draw(CoatPattern.None, CoatPart.Torso, 1), v => Assert.Equal(0f, v));
    }

    /// <summary>The number of value changes across the wrap seam (column 63 → 0, or row 63 → 0) must not exceed what the
    /// interior shows across a typical column pair, with a little slack — a non-tiling mask breaks along the whole seam.
    /// </summary>
    private static void AssertSeamless(float[] m, bool horizontal)
    {
        int Changes(int a, int b)
        {
            int n = 0;
            for (int i = 0; i < S; i++)
            {
                float va = horizontal ? m[i * S + a] : m[a * S + i];
                float vb = horizontal ? m[i * S + b] : m[b * S + i];
                n += va != vb ? 1 : 0;
            }

            return n;
        }

        int seam = Changes(S - 1, 0);
        int worstInterior = Enumerable.Range(0, S - 1).Max(i => Changes(i, i + 1));
        Assert.True(seam <= worstInterior + 2, $"seam {seam} vs interior max {worstInterior}");
    }
}
