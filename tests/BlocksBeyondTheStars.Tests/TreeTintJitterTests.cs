// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2856: the gentle colour drift between groves. The mesher shifts the colour of the tinted crowns and the trunks by a
/// slow field over the columns — a few degrees of hue and about ±10 % brightness — so neighbouring groves differ while
/// one crown stays uniform. Every client must agree (a pure function), the field must never jump (not between two
/// blocks, not where a round world wraps), and it must stay gentle.
/// </summary>
public sealed class TreeTintJitterTests
{
    private const int Classic = WorldConstants.Circumference;

    [Fact]
    public void Drift_StaysWithinAFewDegreesAndTenPercent()
    {
        for (int x = -3000; x < 3000; x += 7)
        {
            for (int z = -1500; z < 1500; z += 53)
            {
                var (hue, value) = TreeTintJitter.At(x, z, Classic);
                Assert.InRange(hue, -TreeTintJitter.MaxHueShift, TreeTintJitter.MaxHueShift);
                Assert.InRange(value, 1f - TreeTintJitter.MaxValueShift, 1f + TreeTintJitter.MaxValueShift);
            }
        }

        // A few degrees of hue (never a new colour), about ±10 % brightness.
        Assert.InRange(TreeTintJitter.MaxHueShift * 360f, 2f, 8f);
        Assert.Equal(0.10f, TreeTintJitter.MaxValueShift, 3);
    }

    [Fact]
    public void Drift_IsAPureFunctionOfTheColumn()
    {
        for (int i = 0; i < 500; i++)
        {
            int x = (i * 7919) - 40000, z = (i * 104729) % 3001 - 1500;
            Assert.Equal(TreeTintJitter.At(x, z, Classic), TreeTintJitter.At(x, z, Classic));
            Assert.Equal(TreeTintJitter.At(x, z, 9600), TreeTintJitter.At(x, z, 9600));
        }

        // A size of 0 (not known yet) reads as the classic size.
        Assert.Equal(TreeTintJitter.At(123, -45, Classic), TreeTintJitter.At(123, -45, 0));
    }

    [Theory]
    [InlineData(Classic)]
    [InlineData(9600)]
    [InlineData(2048)]
    [InlineData(160)]
    public void Drift_ReadsTheCanonicalCell_ALapAwayAndAtNegativeCoordinates(int circumference)
    {
        int pz = WorldConstants.LatitudePeriodFor(circumference);
        for (int i = 0; i < 300; i++)
        {
            int x = (i * 37) % circumference, z = ((i * 61) % pz) - (pz / 2);
            var here = TreeTintJitter.At(x, z, circumference);
            Assert.Equal(here, TreeTintJitter.At(x + circumference, z, circumference));
            Assert.Equal(here, TreeTintJitter.At(x - circumference, z, circumference));
            Assert.Equal(here, TreeTintJitter.At(x - (5 * circumference), z, circumference));
            Assert.Equal(here, TreeTintJitter.At(x, z + pz, circumference));
            Assert.Equal(here, TreeTintJitter.At(x, z - pz, circumference));
        }

        // Extreme coordinates never overflow.
        var (hue, value) = TreeTintJitter.At(int.MinValue, int.MaxValue, circumference);
        Assert.InRange(hue, -TreeTintJitter.MaxHueShift, TreeTintJitter.MaxHueShift);
        Assert.InRange(value, 0.9f - 1e-5f, 1.1f + 1e-5f);
    }

    [Theory]
    [InlineData(Classic)]
    [InlineData(9600)]
    [InlineData(160)]
    public void Drift_IsSmooth_BetweenNeighbours_AndAcrossTheSeams(int circumference)
    {
        // Value noise over a ~26-block lattice with smoothstep weights: the steepest step between two neighbouring
        // columns is 2 (the full range) × 1.5 (the smoothstep's peak slope) / the cell width — about 0.12 of the range.
        const float maxStep = 0.14f;
        int pz = WorldConstants.LatitudePeriodFor(circumference);
        for (int x = -60; x < circumference + 60; x += circumference > 1000 ? 13 : 1)
        {
            for (int z = -pz / 2; z < pz / 2; z += pz > 500 ? 97 : 3)
            {
                AssertNeighbours(x, z, x + 1, z, circumference, maxStep);
                AssertNeighbours(x, z, x, z + 1, circumference, maxStep);
            }
        }

        // The seams themselves: the last column and the first one are neighbours on a round world.
        for (int z = -pz / 2; z < pz / 2; z += 5)
        {
            AssertNeighbours(circumference - 1, z, 0, z, circumference, maxStep);
        }

        for (int x = 0; x < circumference; x += 5)
        {
            AssertNeighbours(x, (pz / 2) - 1, x, -pz / 2, circumference, maxStep);
        }
    }

    [Fact]
    public void OneCrown_StaysUniform_WhileNeighbouringGrovesDiffer()
    {
        // A crown is at most nine blocks across (the generators keep |dx|, |dz| ≤ 4): inside one, the brightness may
        // drift by a few percent at most. Across a few groves the full ±10 % band shows up.
        float worstInCrown = 0f, lo = 2f, hi = 0f;
        for (int cx = 0; cx < 600; cx += 11)
        {
            for (int cz = -300; cz < 300; cz += 11)
            {
                float centre = TreeTintJitter.At(cx, cz, Classic).Value;
                lo = System.Math.Min(lo, centre);
                hi = System.Math.Max(hi, centre);
                for (int dx = -4; dx <= 4; dx += 4)
                {
                    for (int dz = -4; dz <= 4; dz += 4)
                    {
                        worstInCrown = System.Math.Max(worstInCrown, System.Math.Abs(TreeTintJitter.At(cx + dx, cz + dz, Classic).Value - centre));
                    }
                }
            }
        }

        Assert.True(worstInCrown <= 0.07f, $"one crown should stay one colour (worst drift inside {worstInCrown:F3})");
        Assert.True(hi - lo >= 0.12f, $"groves should differ visibly (value range {lo:F3}..{hi:F3})");
    }

    [Fact]
    public void Apply_TurnsTheHueGently_AndScalesTheBrightness_InBothColourSpaces()
    {
        var leaf = (R: 0.30f, G: 0.62f, B: 0.24f);
        for (int x = 0; x < 2000; x += 17)
        {
            int z = (x * 7) % 1000 - 500;
            var (_, value) = TreeTintJitter.At(x, z, Classic);
            var gamma = TreeTintJitter.Apply(leaf, x, z, Classic, linear: false);
            var linear = TreeTintJitter.Apply(leaf, x, z, Classic, linear: true);
            foreach (float c in new[] { gamma.R, gamma.G, gamma.B, linear.R, linear.G, linear.B })
            {
                Assert.InRange(c, 0f, 1f);
            }

            // Brightness (the HSV value = the brightest channel) follows the factor: as is in gamma space, raised to
            // ~2.2 on linear values, so ±10 % reads the same on screen.
            Assert.Equal(0.62f * value, gamma.G, 3);
            Assert.Equal(0.62f * (float)System.Math.Pow(value, 2.2), linear.G, 2);

            // Still the same green: the hue moves by a few degrees only, so the channel order never flips.
            Assert.True(gamma.G > gamma.R && gamma.R > gamma.B, $"a leaf green stays green ({gamma})");
        }

        // A grey stays grey (nothing to turn).
        var grey = TreeTintJitter.Apply((0.5f, 0.5f, 0.5f), 77, 33, Classic, linear: false);
        Assert.Equal(grey.R, grey.G, 5);
        Assert.Equal(grey.G, grey.B, 5);
    }

    private static void AssertNeighbours(int x0, int z0, int x1, int z1, int circumference, float maxStep)
    {
        var a = TreeTintJitter.At(x0, z0, circumference);
        var b = TreeTintJitter.At(x1, z1, circumference);
        float hueStep = System.Math.Abs(a.HueShift - b.HueShift) / TreeTintJitter.MaxHueShift;
        float valueStep = System.Math.Abs(a.Value - b.Value) / TreeTintJitter.MaxValueShift;
        Assert.True(hueStep <= maxStep, $"hue jumps between ({x0},{z0}) and ({x1},{z1}) on {circumference}: {hueStep:F3}");
        Assert.True(valueStep <= maxStep, $"brightness jumps between ({x0},{z0}) and ({x1},{z1}) on {circumference}: {valueStep:F3}");
    }
}
