// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3178: the avatar faces the body-paint editor cannot reach (shoulder tops, the strip under the chest, the wrist ring, the
/// elbow wedge, the first-person cuff) take the colour of the painting instead of the blue base tint.
/// </summary>
public sealed class BodyPaintEdgesTests
{
    private static int[] Grid(int size, int fill) => Enumerable.Repeat(fill, size).ToArray();

    [Fact]
    public void MostCommon_OfAFullyPaintedPart_IsItsPaint()
    {
        var grid = Grid(4 * 32 * 32, 5);
        for (int i = 0; i < 100; i++)
        {
            grid[i] = 9; // a small motif in another colour
        }

        Assert.Equal(5, BodyPaintEdges.MostCommon(grid));
    }

    [Fact]
    public void MostCommon_CountsTransparentAsTheTint()
    {
        // A small logo on an otherwise unpainted part: the part still reads as its tint, so the cuff stays tint-coloured.
        var grid = Grid(1024, 0);
        for (int i = 0; i < 200; i++)
        {
            grid[i] = 7;
        }

        Assert.Equal(0, BodyPaintEdges.MostCommon(grid));
    }

    [Fact]
    public void MostCommon_ATieGoesToThePaint()
    {
        var grid = Grid(64, 0);
        for (int i = 0; i < 32; i++)
        {
            grid[i] = 12;
        }

        Assert.Equal(12, BodyPaintEdges.MostCommon(grid));
        grid[0] = 3; // 31 × 12, 1 × 3, 32 × transparent: the tint wins only when it is strictly the most common
        Assert.Equal(0, BodyPaintEdges.MostCommon(grid));
    }

    [Fact]
    public void MostCommon_TieBetweenPaints_TakesTheLowerIndex()
    {
        var grid = new[] { 20, 20, 4, 4, 0 };
        Assert.Equal(4, BodyPaintEdges.MostCommon(grid));
    }

    [Fact]
    public void MostCommon_IgnoresOutOfRangeValues_AndEmptyInput()
    {
        Assert.Equal(0, BodyPaintEdges.MostCommon(null));
        Assert.Equal(0, BodyPaintEdges.MostCommon(Array.Empty<int>()));
        Assert.Equal(2, BodyPaintEdges.MostCommon(new[] { 99, 99, 99, -1, 2 }));
    }

    [Theory]
    [InlineData(1f, true, 31)]          // the top of the box: its top row
    [InlineData(0.987f, true, 31)]      // the shoulder top, just under the box top
    [InlineData(0f, false, 0)]          // the bottom of the box (a sole): its bottom row
    [InlineData(0.494f, false, 15)]     // the chest's lower edge, half way up the torso: the row just above it
    [InlineData(0.494f, true, 15)]      // the abdomen's top at the same height shows the row just below it
    [InlineData(0.5f, true, 15)]        // exactly on a texel boundary: the top edge shows the row below it…
    [InlineData(0.5f, false, 16)]       // …the bottom edge the row above it
    [InlineData(0.16f, true, 5)]        // the wrist: the hand's top
    [InlineData(-0.2f, false, 0)]       // out of range clamps
    [InlineData(1.3f, true, 31)]
    public void EdgeTexel_IsTheRowTheSideFaceShowsAtThatEdge(float v, bool top, int expected)
        => Assert.Equal(expected, BodyPaintEdges.EdgeTexel(v, top, 32));

    [Fact]
    public void EdgeTexel_SurvivesFloatNoiseOnABoundary()
    {
        Assert.Equal(15, BodyPaintEdges.EdgeTexel(0.5f + 1e-6f, topEdge: true, texels: 32));
        Assert.Equal(16, BodyPaintEdges.EdgeTexel(0.5f - 1e-6f, topEdge: false, texels: 32));
        Assert.Equal(0, BodyPaintEdges.EdgeTexel(float.NaN, topEdge: true, texels: 32));
    }

    [Fact]
    public void TexelCentre_SamplesInsideTheRow()
    {
        for (int row = 0; row < 32; row++)
        {
            float v = BodyPaintEdges.TexelCentre(row, 32);
            Assert.Equal(row, (int)(v * 32));
        }
    }
}
