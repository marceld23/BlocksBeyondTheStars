// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Textures;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3939: the texture editor's soft whole-tile operations — calm, smooth, make seamless and the
/// "brightness only" colour rule. Deterministic, alpha left alone, wrapping where tiles repeat.
/// </summary>
public sealed class TileFiltersTests
{
    private const int S = TextureTiles.Size;

    /// <summary>A deterministic busy tile: a hash pattern in every channel, alpha <paramref name="alpha"/> (or a hash too).</summary>
    private static byte[] Noise(int seed, int alpha = 255)
    {
        var raw = new byte[TextureTiles.BytesPerFrame];
        uint h = (uint)seed * 2654435761u;
        for (int i = 0; i < raw.Length; i++)
        {
            h ^= h << 13;
            h ^= h >> 17;
            h ^= h << 5;
            raw[i] = (byte)h;
        }

        if (alpha >= 0)
        {
            for (int i = 3; i < raw.Length; i += 4)
            {
                raw[i] = (byte)alpha;
            }
        }

        return raw;
    }

    private static byte[] Gradient()
    {
        // Left to right 0 → 252 in red, a fine noise on green: a hard seam where the right edge wraps to the left.
        var raw = new byte[TextureTiles.BytesPerFrame];
        var noise = Noise(5);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                int o = ((y * S) + x) * 4;
                raw[o] = (byte)(x * 4);
                raw[o + 1] = (byte)(100 + (noise[o] % 20));
                raw[o + 2] = 60;
                raw[o + 3] = 255;
            }
        }

        return raw;
    }

    private static double Hue(byte r, byte g, byte b)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        if (d <= 0)
        {
            return 0;
        }

        double h = max == r ? ((g - b) / d) % 6 : max == g ? ((b - r) / d) + 2 : ((r - g) / d) + 4;
        return (h * 60 + 360) % 360;
    }

    private static void AssertAlphaKept(byte[] before, byte[] after)
    {
        for (int i = 3; i < before.Length; i += 4)
        {
            Assert.Equal(before[i], after[i]);
        }
    }

    [Fact]
    public void Calm_MovesEveryTexelTowardTheMean_AndKeepsAlpha()
    {
        var tile = Noise(1, alpha: -1);
        var half = TileFilters.Calm(tile, 0.5);
        var full = TileFilters.Calm(tile, 1.0);

        AssertAlphaKept(tile, half);
        AssertAlphaKept(tile, full);
        Assert.Equal(tile, TileFilters.Calm(tile, 0.0)); // 0 % = as painted

        // At 100 % every texel is the alpha-weighted mean; at 50 % it is halfway there.
        double mr = 0, w = 0;
        for (int i = 0; i < tile.Length; i += 4)
        {
            mr += tile[i] * tile[i + 3];
            w += tile[i + 3];
        }

        byte mean = TileFilters.ToByte(mr / w);
        for (int i = 0; i < tile.Length; i += 4)
        {
            Assert.Equal(mean, full[i]);
            Assert.Equal(TileFilters.ToByte(tile[i] + (((mr / w) - tile[i]) * 0.5)), half[i]);
        }
    }

    [Fact]
    public void Calm_IgnoresTheColourOfHoles()
    {
        // A cutout tile: green leaves on black holes. The mean must be green, not darkened toward black.
        var tile = new byte[TextureTiles.BytesPerFrame];
        for (int i = 0; i < tile.Length; i += 4)
        {
            bool leaf = (i / 4) % 2 == 0;
            tile[i + 1] = leaf ? (byte)200 : (byte)0;
            tile[i + 3] = leaf ? (byte)255 : (byte)0;
        }

        var calm = TileFilters.Calm(tile, 1.0);
        Assert.Equal(200, calm[1]);
        Assert.Equal(200, calm[5]); // a hole takes the leaves' colour, its alpha stays 0
        Assert.Equal(0, calm[7]);
    }

    [Fact]
    public void Calm_IsDeterministic() => Assert.Equal(TileFilters.Calm(Noise(7), 0.37), TileFilters.Calm(Noise(7), 0.37));

    [Fact]
    public void Smooth_WrapsAroundTheEdges_AndKeepsAlpha()
    {
        // One bright texel in the top-left corner (raw 0,0): its light must spread to the opposite edges too.
        var tile = new byte[TextureTiles.BytesPerFrame];
        for (int i = 3; i < tile.Length; i += 4)
        {
            tile[i] = 255;
        }

        tile[0] = 255;
        var smooth = TileFilters.Smooth(tile);
        AssertAlphaKept(tile, smooth);
        int Red(int x, int y) => smooth[((y * S) + x) * 4];
        Assert.Equal(64, Red(0, 0));       // 4 / 16
        Assert.Equal(32, Red(S - 1, 0));   // the left neighbour across the wrap: 2 / 16
        Assert.Equal(32, Red(0, S - 1));   // the neighbour across the other wrap
        Assert.Equal(16, Red(S - 1, S - 1)); // the diagonal across both: 1 / 16
        Assert.Equal(0, Red(2, 2));
        Assert.Equal(smooth, TileFilters.Smooth(tile)); // deterministic
    }

    [Fact]
    public void Smooth_LendsNoBlackFromHoles()
    {
        var tile = new byte[TextureTiles.BytesPerFrame];
        for (int i = 0; i < tile.Length; i += 4)
        {
            bool leaf = (i / 4) % 3 != 0;
            tile[i + 1] = leaf ? (byte)180 : (byte)0;
            tile[i + 3] = leaf ? (byte)255 : (byte)0;
        }

        var smooth = TileFilters.Smooth(tile);
        for (int i = 0; i < tile.Length; i += 4)
        {
            if (tile[i + 3] > 0)
            {
                Assert.Equal(180, smooth[i + 1]);
            }
        }
    }

    [Fact]
    public void MakeSeamless_HealsAHardSeam()
    {
        var tile = Gradient();
        double before = TileFilters.SeamRatio(tile);
        var healed = TileFilters.MakeSeamless(tile);
        double after = TileFilters.SeamRatio(healed);

        Assert.True(before > 10.0, $"the test tile should have a hard seam, ratio {before:0.00}");
        Assert.True(after <= 1.3, $"healed ratio {after:0.00} (before {before:0.00})");
        AssertAlphaKept(tile, healed);
        Assert.Equal(healed, TileFilters.MakeSeamless(tile)); // deterministic
    }

    [Fact]
    public void MakeSeamless_LeavesAConstantChannelAndATileWithoutSeamsAlone()
    {
        var tile = Gradient();
        var healed = TileFilters.MakeSeamless(tile);
        for (int i = 2; i < tile.Length; i += 4)
        {
            Assert.Equal(60, healed[i]); // the constant blue channel stays exactly
        }

        // A tile without a seam (noise) stays seamless, keeps its mean and keeps its texels in the middle: the heal only
        // bends the brightness near the edges.
        var noise = Noise(9);
        var same = TileFilters.MakeSeamless(noise);
        Assert.InRange(TileFilters.SeamRatio(same), 0.0, 1.3);
        long sumBefore = 0, sumAfter = 0;
        int middleMoved = 0;
        for (int i = 0; i < noise.Length; i += 4)
        {
            sumBefore += noise[i];
            sumAfter += same[i];
            int x = (i / 4) % S, y = (i / 4) / S;
            bool middle = x >= 16 && x < 48 && y >= 16 && y < 48;
            middleMoved += middle && Math.Abs(noise[i] - same[i]) > 3 ? 1 : 0;
        }

        Assert.InRange((sumAfter - sumBefore) / (double)(S * S), -1.0, 1.0);
        Assert.True(middleMoved < 32, $"{middleMoved} middle texels moved by more than 3");
    }

    [Fact]
    public void SeamRatio_IsAboutOneForNoise() => Assert.InRange(TileFilters.SeamRatio(Noise(3)), 0.7, 1.3);

    [Fact]
    public void WithLuminance_SetsTheBrightness_AndKeepsTheHue()
    {
        var colours = new[] { new TexelColor(200, 80, 40), new TexelColor(30, 120, 60), new TexelColor(90, 90, 200), new TexelColor(128, 128, 128) };
        foreach (var c in colours)
        {
            foreach (double luma in new[] { 40.0, 110.0, 180.0 })
            {
                var lit = TileFilters.WithLuminance(c, luma, 255);
                Assert.InRange(TileFilters.Luma(lit.R, lit.G, lit.B), luma - 1.0, luma + 1.0);
                if (c.R != c.G || c.G != c.B)
                {
                    Assert.InRange(Hue(lit.R, lit.G, lit.B), Hue(c.R, c.G, c.B) - 3.0, Hue(c.R, c.G, c.B) + 3.0);
                }
            }
        }
    }

    [Fact]
    public void KeepHue_ErasesAsUsual() => Assert.Equal(TexelColor.Clear, TileFilters.KeepHue(new TexelColor(10, 200, 30), TexelColor.Clear));

    [Fact]
    public void Greyscale_KeepsAlphaAndBrightness()
    {
        var tile = Noise(11, alpha: -1);
        var grey = TileFilters.Greyscale(tile);
        AssertAlphaKept(tile, grey);
        Assert.Equal(grey[0], grey[1]);
        Assert.Equal(grey[1], grey[2]);
        Assert.Equal(TileFilters.ToByte(TileFilters.Luma(tile[0], tile[1], tile[2])), grey[0]);
    }

    [Fact]
    public void SameShown_IgnoresTheColourOfInvisibleTexels()
    {
        var a = Noise(2);
        var b = (byte[])a.Clone();
        a[3] = b[3] = 0;
        b[0] ^= 0x55; // a hidden texel's colour differs
        Assert.True(TileFilters.SameShown(a, b));
        b[4] ^= 0x55; // a visible one too
        Assert.False(TileFilters.SameShown(a, b));
    }
}
