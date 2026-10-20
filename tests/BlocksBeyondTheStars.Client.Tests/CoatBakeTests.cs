// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The coat bake (#2830, #2860, #2862): every hide tile normalised to one bright mean and one contrast, the body colour
/// where the mask is 0 and the pattern colour where it is 1 under the same normalised hide, and the glow colour with alpha
/// 0 where the glow mask is set.
/// </summary>
public sealed class CoatBakeTests
{
    private static byte[] GreyTile(byte v)
    {
        var raw = new byte[CoatBake.TileBytes];
        for (int p = 0; p < raw.Length; p += 4)
        {
            raw[p] = raw[p + 1] = raw[p + 2] = v;
            raw[p + 3] = 255;
        }

        return raw;
    }

    private static byte[] StripedTile(byte dark, byte light)
    {
        var raw = new byte[CoatBake.TileBytes];
        for (int i = 0; i < raw.Length / 4; i++)
        {
            byte v = (i % 2) == 0 ? dark : light;
            raw[i * 4] = raw[i * 4 + 1] = raw[i * 4 + 2] = v;
            raw[i * 4 + 3] = 255;
        }

        return raw;
    }

    [Fact]
    public void Normalize_GivesEveryTileTheSameBrightMean_AndTheSameContrast()
    {
        // A faint tile and a harsh one end up alike: the faint one gains contrast, the harsh one loses some.
        foreach (var tile in new[] { StripedTile(70, 82), StripedTile(10, 200), StripedTile(40, 120) })
        {
            var n = CoatBake.Normalize(tile);
            Assert.InRange(CoatBake.Mean(n), CoatBake.TargetMean - 0.02f, CoatBake.TargetMean + 0.02f);
            Assert.InRange(CoatBake.Spread(n), CoatBake.TargetSpread - 0.015f, CoatBake.TargetSpread + 0.015f);
            Assert.All(Enumerable.Range(0, n.Length / 4), i => Assert.Equal(255, n[i * 4 + 3]));
        }

        // A flat tile stays flat (nothing to stretch) at the target mean.
        var flat = CoatBake.Normalize(GreyTile(60));
        Assert.Equal(Byte(CoatBake.TargetMean), flat[0]);
        Assert.Equal(0f, CoatBake.Spread(flat), 3);
    }

    [Fact]
    public void Normalize_KeepsATilesColour_AsAnOffset()
    {
        var tile = GreyTile(80);
        for (int p = 0; p < tile.Length; p += 8)
        {
            tile[p + 2] = 160; // every other texel bluish
        }

        var n = CoatBake.Normalize(tile);
        Assert.True(n[2] > n[0] + 20, "the bluish texels stay bluish");
        Assert.Equal(n[4], n[6]); // the grey texels stay grey
        Assert.Equal(n[4], n[5]);
    }

    [Fact]
    public void EveryBundledHide_ReadsAfterNormalising()
    {
        // The finding behind #2860: after the old fixed lift 21 of 49 hides kept less than ±5 % brightness. Now every
        // bundled hide (at least every one with any structure) lands in the target band, never darker than the old lift.
        string dir = Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "Resources", "textures");
        foreach (string hide in CreatureHides.All)
        {
            var file = Path.Combine(dir, CreatureHides.TileKey(hide) + ".bytes");
            Assert.True(File.Exists(file), file);
            var raw = File.ReadAllBytes(file);
            var n = CoatBake.Normalize(raw);
            float oldLiftMean = 1f - (1f - CoatBake.Mean(raw)) * 0.45f;
            Assert.True(CoatBake.Mean(n) >= oldLiftMean - 0.03f, $"{hide} got darker: {CoatBake.Mean(n):0.00} < {oldLiftMean:0.00}");
            if (CoatBake.Spread(raw) * CoatBake.MaxGain >= CoatBake.TargetSpread)
            {
                Assert.InRange(CoatBake.Spread(n), 0.06f, 0.115f); // clamping the extremes trims a two-tone hide a little
            }
        }
    }

    [Fact]
    public void Bake_MixesBodyAndPattern_ByTheMask_UnderTheHide()
    {
        var hide = GreyTile(128); // a mid-grey normalised hide: every texel × 128/255
        float l = 128f / 255f;
        var mask = new float[CoatMasks.Size * CoatMasks.Size];
        mask[1] = 1f;
        var tex = CoatBake.Bake(hide, 0.8f, 0.6f, 0.4f, 0.2f, 0.1f, 0.05f, mask);

        Assert.Equal(Byte(0.8f * l), tex[0]);    // body
        Assert.Equal(Byte(0.6f * l), tex[1]);
        Assert.Equal(Byte(0.2f * l), tex[4]);    // pattern, at full strength under the same hide
        Assert.Equal(Byte(0.05f * l), tex[6]);
        Assert.Equal(255, tex[3]);
    }

    [Fact]
    public void ThePattern_IsNotWashedOutTowardWhite()
    {
        // On a white hide the pattern texel IS the pattern colour.
        var mask = new float[CoatMasks.Size * CoatMasks.Size];
        Array.Fill(mask, 1f);
        var tex = CoatBake.Bake(GreyTile(255), 0.9f, 0.9f, 0.9f, 0.1f, 0.2f, 0.3f, mask);
        Assert.Equal(Byte(0.1f), tex[0]);
        Assert.Equal(Byte(0.2f), tex[1]);
        Assert.Equal(Byte(0.3f), tex[2]);
    }

    [Fact]
    public void TheGlow_TakesTheGlowColour_AndAlphaZero_ElsewhereOpaque()
    {
        var glow = new float[CoatMasks.Size * CoatMasks.Size];
        glow[2] = 1f;
        var tex = CoatBake.Bake(GreyTile(255), 0.5f, 0.5f, 0.5f, 0f, 0f, 0f, null, glow, 0.2f, 1f, 0.6f);
        Assert.Equal(0, tex[2 * 4 + 3]);
        Assert.Equal(Byte(1f), tex[2 * 4 + 1]);
        Assert.Equal(Byte(0.2f), tex[2 * 4]);
        Assert.Equal(255, tex[3]);
        Assert.Equal(Byte(0.5f), tex[0]);
    }

    [Fact]
    public void NoHide_GivesAFlatCoat_AndABadMaskThrows()
    {
        var mask = new float[CoatMasks.Size * CoatMasks.Size];
        var tex = CoatBake.Bake(null, 0.5f, 0.25f, 1f, 0f, 0f, 0f, mask);
        Assert.Equal(Byte(0.5f), tex[0]);
        Assert.Equal(Byte(1f), tex[2]);
        Assert.Throws<ArgumentException>(() => CoatBake.Bake(null, 0, 0, 0, 0, 0, 0, new float[3]));
        Assert.Throws<ArgumentException>(() => CoatBake.Bake(null, 0, 0, 0, 0, 0, 0, null, new float[3]));
    }

    private static byte Byte(float v) => (byte)Math.Round(Math.Clamp(v, 0f, 1f) * 255f);
}
