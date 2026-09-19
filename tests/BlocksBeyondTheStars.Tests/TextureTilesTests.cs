// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Textures;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The texture tile format and the alpha rule (#1952) — one definition for the client's loaders, the texture
/// editor, the server's validation of world textures and the asset test.
/// </summary>
public sealed class TextureTilesTests
{
    [Theory]
    [InlineData("stone", TextureAlphaMode.Opaque)]
    [InlineData("glass", TextureAlphaMode.Opaque)]        // translucency comes from the material, never the tile
    [InlineData("glass_clear", TextureAlphaMode.Opaque)]
    [InlineData("water", TextureAlphaMode.Opaque)]        // the atlas fades it at runtime
    [InlineData("bed", TextureAlphaMode.Opaque)]
    [InlineData("campfire", TextureAlphaMode.Opaque)]
    [InlineData("flora_fern", TextureAlphaMode.Cutout)]
    [InlineData("flora_sapling", TextureAlphaMode.Cutout)]
    [InlineData("tree_leaves", TextureAlphaMode.Cutout)]
    [InlineData("giant_leaves", TextureAlphaMode.Cutout)]
    [InlineData("fire", TextureAlphaMode.Cutout)]
    [InlineData("torch", TextureAlphaMode.Cutout)]
    [InlineData("lantern", TextureAlphaMode.Cutout)]
    [InlineData("creature_fur", TextureAlphaMode.Free)]
    [InlineData("microfauna_moth", TextureAlphaMode.Free)]
    [InlineData("avatar_suit", TextureAlphaMode.Free)]
    public void AlphaMode_FollowsWhatTheShadersReadFromTheTile(string key, TextureAlphaMode expected)
        => Assert.Equal(expected, TextureTiles.AlphaModeOf(key));

    [Fact]
    public void AnOpaqueTile_IsForcedFullyOpaque_SoNobodyCanSeeThroughTerrain()
    {
        var raw = new byte[TextureTiles.BytesPerFrame];
        for (int i = 3; i < raw.Length; i += 4)
        {
            raw[i] = (byte)(i % 251); // every alpha value, zero included
        }

        int changed = TextureTiles.EnforceAlpha(raw, TextureAlphaMode.Opaque);

        Assert.True(changed > 0);
        Assert.Equal(0, TextureTiles.CountSeeThrough(raw));
    }

    [Theory]
    [InlineData(TextureAlphaMode.Cutout)]
    [InlineData(TextureAlphaMode.Free)]
    public void CutoutAndFreeTiles_KeepTheirAlpha(TextureAlphaMode mode)
    {
        var raw = new byte[TextureTiles.BytesPerFrame]; // alpha 0 everywhere

        Assert.Equal(0, TextureTiles.EnforceAlpha(raw, mode));
        Assert.Equal(TextureTiles.Size * TextureTiles.Size, TextureTiles.CountSeeThrough(raw));
    }

    [Theory]
    [InlineData(1, 0, true)]
    [InlineData(1, 99, true)]   // a still texture ignores the speed
    [InlineData(2, 2, true)]
    [InlineData(8, 12, true)]
    [InlineData(9, 8, false)]   // one frame too many
    [InlineData(0, 8, false)]
    [InlineData(4, 5, false)]   // not an offered speed
    [InlineData(4, 0, false)]
    public void Animation_IsOneFrame_OrUpToEightAtAnOfferedSpeed(int frames, int fps, bool expected)
        => Assert.Equal(expected, TextureTiles.IsValidAnimation(frames, fps));

    [Theory]
    [InlineData("stone", true)]
    [InlineData("prop_door_slide_panel", true)]
    [InlineData("flora_2", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("Stone", false)]
    [InlineData("../stone", false)]     // never a path
    [InlineData("stone.png", false)]
    [InlineData("a b", false)]
    public void Keys_AreLowercaseIdentifiers(string? key, bool expected)
        => Assert.Equal(expected, TextureTiles.IsValidKey(key));

    [Fact]
    public void AKey_HasALengthLimit()
    {
        Assert.True(TextureTiles.IsValidKey(new string('a', TextureTiles.MaxKeyLength)));
        Assert.False(TextureTiles.IsValidKey(new string('a', TextureTiles.MaxKeyLength + 1)));
    }
}
