// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using BlocksBeyondTheStars.Client.Minigames;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>Headless tests for the Canvas2D text + colour + shape additions that back the ported games
/// (bitmap font, HSL, circle outline, triangle fill).</summary>
public sealed class MinigameDrawTests
{
    private static (byte R, byte G, byte B, byte A) At(Canvas2D c, int x, int y)
    {
        int i = (y * c.Width + x) * 4;
        return (c.Rgba[i], c.Rgba[i + 1], c.Rgba[i + 2], c.Rgba[i + 3]);
    }

    [Fact]
    public void DrawText_RendersGlyphPixels_PerFontRows()
    {
        var c = new Canvas2D(16, 8);
        c.Clear(Rgba.Black);
        c.DrawText(0, 0, "A", Rgba.White); // top row of 'A' = 0b01110 → x=1,2,3 set; x=0,4 clear
        Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), At(c, 1, 0));
        Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), At(c, 3, 0));
        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)255), At(c, 0, 0));
        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)255), At(c, 4, 0));
    }

    [Fact]
    public void DrawText_LowercaseFoldsToUppercase_AndUnknownIsBlankButAdvances()
    {
        var lower = new Canvas2D(16, 8);
        var upper = new Canvas2D(16, 8);
        lower.DrawText(0, 0, "a", Rgba.White);
        upper.DrawText(0, 0, "A", Rgba.White);
        Assert.Equal(upper.Rgba, lower.Rgba);

        Assert.Equal(0, Canvas2D.TextWidth(""));
        Assert.Equal(2 * BitmapFont.Advance, Canvas2D.TextWidth("A~")); // unknown '~' still advances
    }

    [Fact]
    public void DrawTextCentered_IsSymmetric()
    {
        var c = new Canvas2D(40, 8);
        c.Clear(Rgba.Black);
        c.DrawText(0, 0, "II", Rgba.White);
        int leftWidth = Canvas2D.TextWidth("II");
        Assert.Equal(2 * BitmapFont.Advance, leftWidth);
    }

    [Theory]
    [InlineData(0, 255, 0, 0)]     // red
    [InlineData(120, 0, 255, 0)]   // green
    [InlineData(240, 0, 0, 255)]   // blue
    public void Hsl_PrimaryHues(double h, int r, int g, int b)
    {
        var col = Rgba.Hsl(h, 1.0, 0.5);
        Assert.Equal(r, col.R);
        Assert.Equal(g, col.G);
        Assert.Equal(b, col.B);
    }

    [Fact]
    public void DrawCircle_SetsCardinalPoints_NotCentre()
    {
        var c = new Canvas2D(11, 11);
        c.Clear(Rgba.Black);
        c.DrawCircle(5, 5, 4, Rgba.White);
        Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), At(c, 9, 5)); // east
        Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), At(c, 5, 1)); // north
        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)255), At(c, 5, 5));       // centre untouched
    }

    [Fact]
    public void FillTriangle_FillsInterior()
    {
        var c = new Canvas2D(12, 12);
        c.Clear(Rgba.Black);
        c.FillTriangle(1, 1, 10, 1, 1, 10, Rgba.White);
        Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), At(c, 2, 2)); // inside
        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)255), At(c, 9, 9));       // outside the hypotenuse
    }
}
