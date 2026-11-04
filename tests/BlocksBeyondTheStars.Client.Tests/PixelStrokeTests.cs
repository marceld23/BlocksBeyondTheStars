// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3179: the pixel editors' pointer mapping and brush line. A fast stroke must leave no gaps (the school club's PCs drew a
/// checker of unpainted cells), and every cell — the outer row and column included — must get the same hit area.
/// </summary>
public sealed class PixelStrokeTests
{
    [Fact]
    public void CellOf_GivesEveryCellAnEqualShare()
    {
        const int cells = 32;
        var hits = new int[cells];
        const int samples = cells * 100;
        for (int i = 0; i < samples; i++)
        {
            hits[PixelStroke.CellOf((i + 0.5f) / samples, cells)]++;
        }

        Assert.All(hits, h => Assert.Equal(100, h));
    }

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(0.01f, 0)]
    [InlineData(1f / 32f, 1)]
    [InlineData(0.5f, 16)]
    [InlineData(0.99f, 31)]
    [InlineData(1f, 31)]
    public void CellOf_MapsByFloor(float fraction, int expected)
        => Assert.Equal(expected, PixelStroke.CellOf(fraction, 32));

    [Fact]
    public void CellOf_OffTheCanvas_LandsOnTheEdgeCell()
    {
        // During a stroke the pointer may leave the canvas: it paints the nearest edge cell instead of nothing.
        Assert.Equal(0, PixelStroke.CellOf(-0.3f, 32));
        Assert.Equal(31, PixelStroke.CellOf(1.7f, 32));
        Assert.Equal(31, PixelStroke.CellOf(float.PositiveInfinity, 32));
        Assert.Equal(0, PixelStroke.CellOf(float.NegativeInfinity, 32));
        Assert.Equal(0, PixelStroke.CellOf(float.NaN, 32));
        Assert.Equal(0, PixelStroke.CellOf(0.7f, 1));
    }

    [Fact]
    public void CellOf_TheOldRoundingGaveTheOuterCellsHalfAHitArea()
    {
        // The regression: RoundToInt(u * 31) — cell 31 only for u ≥ 30.5 / 31 (≈ 0.984), half a cell's width.
        Assert.Equal(31, PixelStroke.CellOf(0.975f, 32));
        Assert.Equal(0, PixelStroke.CellOf(0.025f, 32));
    }

    [Fact]
    public void Line_OfOneCell_IsThatCell()
        => Assert.Equal(new[] { (4, 7) }, PixelStroke.Line(4, 7, 4, 7));

    [Theory]
    [InlineData(0, 0, 31, 0)]
    [InlineData(31, 5, 0, 5)]
    [InlineData(3, 0, 3, 31)]
    [InlineData(0, 0, 31, 31)]
    [InlineData(31, 0, 0, 31)]
    [InlineData(2, 3, 29, 11)]
    [InlineData(20, 30, 1, 2)]
    public void Line_IsGapFree_FromStartToEnd(int x0, int y0, int x1, int y1)
    {
        var cells = PixelStroke.Line(x0, y0, x1, y1).ToList();

        Assert.Equal((x0, y0), cells[0]);
        Assert.Equal((x1, y1), cells[^1]);
        Assert.Equal(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) + 1, cells.Count); // one cell per step of the long axis
        for (int i = 1; i < cells.Count; i++)
        {
            int stepX = Math.Abs(cells[i].X - cells[i - 1].X), stepY = Math.Abs(cells[i].Y - cells[i - 1].Y);
            Assert.True(stepX <= 1 && stepY <= 1 && stepX + stepY > 0, $"step {i}: {cells[i - 1]} → {cells[i]} is not a neighbour");
        }

        Assert.Equal(cells.Count, cells.Distinct().Count());
    }

    [Fact]
    public void Line_StaysInsideTheBoxOfItsEnds()
    {
        foreach (var (x, y) in PixelStroke.Line(5, 25, 12, 3))
        {
            Assert.InRange(x, 5, 12);
            Assert.InRange(y, 3, 25);
        }
    }
}
