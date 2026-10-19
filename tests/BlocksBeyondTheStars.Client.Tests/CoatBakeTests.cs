// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The coat bake (#2830): body colour where the mask is 0, pattern colour where it is 1, the lifted hide luminance over
/// both — and only the hide is lifted, never the pattern colour.
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

    [Fact]
    public void Lift_RaisesTheShadows_AndKeepsWhite()
    {
        var lifted = CoatBake.Lift(GreyTile(0));
        Assert.Equal((byte)(255f - 255 * CoatBake.TileDetail), lifted[0]);
        Assert.Equal(255, CoatBake.Lift(GreyTile(255))[0]);
    }

    [Fact]
    public void Bake_MixesBodyAndPattern_ByTheMask_UnderTheLiftedHide()
    {
        var hide = GreyTile(0); // the darkest hide: lifted luma = 1 − TileDetail
        var mask = new float[CoatMasks.Size * CoatMasks.Size];
        mask[1] = 1f;
        var tex = CoatBake.Bake(hide, 0.8f, 0.6f, 0.4f, 0.2f, 0.1f, 0.05f, mask);
        float l = 1f - CoatBake.TileDetail;

        Assert.Equal(Byte(0.8f * l), tex[0]);    // body
        Assert.Equal(Byte(0.6f * l), tex[1]);
        Assert.Equal(Byte(0.2f * l), tex[4]);    // pattern, at full strength under the same hide
        Assert.Equal(Byte(0.05f * l), tex[6]);
        Assert.Equal(255, tex[3]);
    }

    [Fact]
    public void ThePattern_IsNotWashedOutTowardWhite()
    {
        // On a white hide the pattern texel IS the pattern colour — the lift never pulls it toward white.
        var mask = new float[CoatMasks.Size * CoatMasks.Size];
        Array.Fill(mask, 1f);
        var tex = CoatBake.Bake(GreyTile(255), 0.9f, 0.9f, 0.9f, 0.1f, 0.2f, 0.3f, mask);
        Assert.Equal(Byte(0.1f), tex[0]);
        Assert.Equal(Byte(0.2f), tex[1]);
        Assert.Equal(Byte(0.3f), tex[2]);
    }

    [Fact]
    public void NoHide_GivesAFlatCoat_AndABadMaskThrows()
    {
        var mask = new float[CoatMasks.Size * CoatMasks.Size];
        var tex = CoatBake.Bake(null, 0.5f, 0.25f, 1f, 0f, 0f, 0f, mask);
        Assert.Equal(Byte(0.5f), tex[0]);
        Assert.Equal(Byte(1f), tex[2]);
        Assert.Throws<ArgumentException>(() => CoatBake.Bake(null, 0, 0, 0, 0, 0, 0, new float[3]));
    }

    private static byte Byte(float v) => (byte)Math.Round(Math.Clamp(v, 0f, 1f) * 255f);
}
