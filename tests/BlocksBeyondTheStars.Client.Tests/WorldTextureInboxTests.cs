// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Textures;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The client's end of the world texture stream (#1959): pages become ONE batch, a single change is a batch of
/// one, and nothing a server sends can break the alpha rule or the loader.
/// </summary>
public sealed class WorldTextureInboxTests
{
    private static byte[] Frame(byte red, byte alpha = 255)
    {
        var raw = new byte[TextureTiles.BytesPerFrame];
        for (int i = 0; i < raw.Length; i += 4)
        {
            raw[i] = red;
            raw[i + 3] = alpha;
        }

        return raw;
    }

    private static NetWorldTexture Net(string key, byte red, int frames = 1, int fps = 0, byte alpha = 255) => new NetWorldTexture
    {
        Key = key,
        Frames = frames,
        Fps = fps,
        Data = WorldTextureCodec.Encode(Enumerable.Range(0, frames).Select(_ => Frame(red, alpha)).ToArray()),
        Owner = "Marcel",
    };

    private static WorldTextureList Page(bool reset, bool final, params NetWorldTexture[] textures)
        => new WorldTextureList { Reset = reset, Final = final, Textures = textures };

    [Fact]
    public void Pages_BecomeOneBatch_WhenTheLastOneIsIn()
    {
        var inbox = new WorldTextureInbox();

        Assert.Null(inbox.Accept(Page(reset: true, final: false, Net("stone", 1))));
        Assert.Null(inbox.Accept(Page(reset: false, final: false, Net("grass", 2, frames: 2, fps: 8))));
        var batch = inbox.Accept(Page(reset: false, final: true, Net("bed", 3)));

        Assert.NotNull(batch);
        Assert.True(batch!.Complete);
        Assert.Equal(new[] { "bed", "grass", "stone" }, batch.Changes.Keys.OrderBy(k => k).ToArray());
        Assert.Equal(2, batch.Changes["grass"]!.Frames.Length);
        Assert.Equal(8, batch.Changes["grass"]!.Fps);
        Assert.Equal("Marcel", batch.Changes["stone"]!.Owner);
        Assert.Equal(1, batch.Changes["stone"]!.Frames[0][0]);
    }

    [Fact]
    public void AnEmptyFinalPage_IsACompleteEmptyList_TheWorldHasNoTexturesOrTheRuleIsOff()
    {
        var batch = new WorldTextureInbox().Accept(Page(reset: true, final: true));

        Assert.NotNull(batch);
        Assert.True(batch!.Complete);
        Assert.Empty(batch.Changes);
    }

    [Fact]
    public void ASingleChange_IsABatchOfOne_AndAWipeRemovesTheKey()
    {
        var inbox = new WorldTextureInbox();

        var published = inbox.Accept(new WorldTextureData { Texture = Net("stone", 9) });
        var wiped = inbox.Accept(new WorldTextureData { Texture = new NetWorldTexture { Key = "stone", Data = string.Empty } });

        Assert.False(published!.Complete);
        Assert.Equal(9, published.Changes["stone"]!.Frames[0][0]);
        Assert.True(wiped!.Changes.ContainsKey("stone"));
        Assert.Null(wiped.Changes["stone"]);
    }

    [Fact]
    public void AChangeWhileTheListStreams_BeatsTheOlderStateInALaterPage()
    {
        var inbox = new WorldTextureInbox();
        inbox.Accept(Page(reset: true, final: false, Net("grass", 2)));

        inbox.Accept(new WorldTextureData { Texture = Net("stone", 50) });                       // published just now
        inbox.Accept(new WorldTextureData { Texture = new NetWorldTexture { Key = "bed" } });    // wiped just now
        var batch = inbox.Accept(Page(reset: false, final: true, Net("stone", 1), Net("bed", 3))); // cut before that

        Assert.Equal(50, batch!.Changes["stone"]!.Frames[0][0]);
        Assert.Null(batch.Changes["bed"]);
    }

    [Fact]
    public void WhatDoesNotDecode_IsSkipped_TheRestApplies()
    {
        var inbox = new WorldTextureInbox();
        var lying = Net("grass", 2);
        lying.Frames = 3; // the pixels back one frame
        var oddSpeed = Net("sand", 2, frames: 2, fps: 5);
        var path = Net("stone", 2);
        path.Key = "../stone";
        var garbage = new NetWorldTexture { Key = "dirt", Frames = 1, Data = "@@@" };

        var batch = inbox.Accept(Page(reset: true, final: true, lying, oddSpeed, path, garbage, Net("bed", 7)));

        Assert.Equal(new[] { "bed" }, batch!.Changes.Keys.ToArray());
        Assert.Equal(4, inbox.Rejected);
    }

    [Fact]
    public void AServer_CannotMakeTerrainSeeThrough_ACutoutKeepsItsHoles()
    {
        var batch = new WorldTextureInbox().Accept(Page(reset: true, final: true,
            Net("stone", 1, alpha: 0), Net("flora_fern", 1, alpha: 0)));

        Assert.Equal(0, TextureTiles.CountSeeThrough(batch!.Changes["stone"]!.Frames[0]));
        Assert.Equal(TextureTiles.Size * TextureTiles.Size, TextureTiles.CountSeeThrough(batch.Changes["flora_fern"]!.Frames[0]));
    }

    [Fact]
    public void AHalfReceivedList_IsForgottenWithTheConnection()
    {
        var inbox = new WorldTextureInbox();
        inbox.Accept(Page(reset: true, final: false, Net("stone", 1)));

        inbox.Reset();
        var batch = inbox.Accept(Page(reset: true, final: true, Net("grass", 2)));

        Assert.Equal(new[] { "grass" }, batch!.Changes.Keys.ToArray());
    }
}
