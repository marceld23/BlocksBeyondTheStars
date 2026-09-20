// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Textures;
using BlocksBeyondTheStars.Shared.World;
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

    [Fact]
    public void AShareCode_CarriesATextureWithItsKeyFramesAndSpeed()
    {
        var frames = new[] { new byte[TextureTiles.BytesPerFrame], new byte[TextureTiles.BytesPerFrame] };
        frames[0][0] = 200;
        frames[1][TextureTiles.BytesPerFrame - 1] = 77;

        string code = ShareCode.EncodeTexture("campfire", frames, 8);

        Assert.StartsWith("BBTS1-T-", code);
        Assert.True(ShareCode.TryDecodeTexture("  " + code + "\n", out string key, out var back, out int fps));
        Assert.Equal("campfire", key);
        Assert.Equal(8, fps);
        Assert.Equal(2, back.Length);
        Assert.Equal(frames[0], back[0]);
        Assert.Equal(frames[1], back[1]);
    }

    [Fact]
    public void ATextureShareCode_ThatLiesAboutItself_DoesNotDecode()
    {
        var one = new[] { new byte[TextureTiles.BytesPerFrame] };
        string pixels = WorldTextureCodec.Encode(one);

        // an illegal key never encodes; then: a frame count the pixels do not back, a speed the game does not
        // offer, no head line at all, and a code of another kind
        Assert.Equal(string.Empty, ShareCode.EncodeTexture("../stone", one, 0));
        string twoFrames = ShareCode.Encode(ShareCode.KindTexture, "2 8\n" + pixels, "stone");
        string oddSpeed = ShareCode.Encode(ShareCode.KindTexture, "2 5\n" + pixels, "stone");
        string headless = ShareCode.Encode(ShareCode.KindTexture, pixels, "stone");
        string form = ShareCode.Encode(ShareCode.KindForm, "1 0\n" + pixels, "stone");

        foreach (string bad in new[] { twoFrames, oddSpeed, headless, form, "BBTS1-T-@@@", string.Empty })
        {
            Assert.False(ShareCode.TryDecodeTexture(bad, out string key, out var frames, out _));
            Assert.Equal(string.Empty, key);
            Assert.Empty(frames);
        }
    }
}
