// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using BlocksBeyondTheStars.Client.Minigames;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Headless tests for the minigame raster surface (Stream D Arcade). The drawing primitives are pure, so the
/// pixels written (and the bounds clipping) are asserted exactly without Unity.
/// </summary>
public sealed class Canvas2DTests
{
    private static (byte R, byte G, byte B, byte A) At(Canvas2D c, int x, int y)
    {
        int i = (y * c.Width + x) * 4;
        return (c.Rgba[i], c.Rgba[i + 1], c.Rgba[i + 2], c.Rgba[i + 3]);
    }

    [Fact]
    public void Clear_FillsEveryPixel()
    {
        var c = new Canvas2D(3, 2);
        c.Clear(Rgba.Rgb(10, 20, 30));
        Assert.Equal(((byte)10, (byte)20, (byte)30, (byte)255), At(c, 0, 0));
        Assert.Equal(((byte)10, (byte)20, (byte)30, (byte)255), At(c, 2, 1));
    }

    [Fact]
    public void SetPixel_WritesInBounds_AndIgnoresOutOfBounds()
    {
        var c = new Canvas2D(4, 4);
        c.SetPixel(1, 2, Rgba.White);
        Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), At(c, 1, 2));

        // Out-of-range writes are clipped, never throw.
        c.SetPixel(-1, 0, Rgba.White);
        c.SetPixel(0, 99, Rgba.White);
        c.SetPixel(99, 99, Rgba.White);
        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)0), At(c, 0, 0));
    }

    [Fact]
    public void FillRect_FillsRegion_ClipsOverflow_LeavesOutsideUntouched()
    {
        var c = new Canvas2D(4, 4);
        c.Clear(Rgba.Black);
        c.FillRect(2, 2, 10, 10, Rgba.White); // overflows the right/bottom edges
        Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), At(c, 2, 2));
        Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), At(c, 3, 3));
        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)255), At(c, 0, 0)); // outside the rect
    }

    [Fact]
    public void DrawLine_SetsBothEndpoints()
    {
        var c = new Canvas2D(4, 4);
        c.Clear(Rgba.Black);
        c.DrawLine(0, 0, 3, 3, Rgba.White);
        Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), At(c, 0, 0));
        Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), At(c, 3, 3));
    }

    [Fact]
    public void FillCircle_FillsCentre_NotFarCorner()
    {
        var c = new Canvas2D(5, 5);
        c.Clear(Rgba.Black);
        c.FillCircle(2, 2, 1, Rgba.White);
        Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), At(c, 2, 2));
        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)255), At(c, 0, 0));
    }
}
