// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#3055: which blocks get parallax depth on the High preset, and how deep.</summary>
public sealed class BlockReliefTests
{
    private static GameContent LoadContent() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    [Fact]
    public void DataValue_Wins_AndIsClamped()
    {
        Assert.Equal(0.3f, BlockRelief.For(new BlockDefinition { Key = "grass", Category = "terrain", Relief = 0.3f }));
        Assert.Equal(1f, BlockRelief.For(new BlockDefinition { Key = "x", Relief = 4f }));
        Assert.Equal(0f, BlockRelief.For(new BlockDefinition { Key = "stone", Category = "terrain", RequiredTool = ToolKind.Drill, Relief = -1f }));
        Assert.Equal(0f, BlockRelief.For(null));
    }

    [Fact]
    public void Defaults_FollowTheMaterial_FromTheRealContent()
    {
        var content = LoadContent();
        float Of(string key) => BlockRelief.For(content.Blocks[key]);

        Assert.Equal(BlockRelief.Masonry, Of("ancient_brick"));
        Assert.Equal(BlockRelief.Rock, Of("stone"));
        Assert.Equal(BlockRelief.Rock, Of("granite"));
        Assert.Equal(BlockRelief.Rock, Of("iron_ore")); // an ore is rock first, despite "iron" in its key
        Assert.Equal(BlockRelief.Metal, Of("iron_wall"));
        Assert.Equal(BlockRelief.Metal, Of("steel_floor"));
        Assert.Equal(BlockRelief.Metal, Of("metal_panel"));
        Assert.Equal(0f, Of("grass"));
        Assert.Equal(0f, Of("sand"));
        Assert.Equal(0f, Of("environment_sensor")); // a machine, although "iron" hides in its key
    }

    [Fact]
    public void EveryBlock_HasAReliefInRange()
    {
        foreach (var def in LoadContent().Blocks.Values)
        {
            float r = BlockRelief.For(def);
            Assert.InRange(r, 0f, 1f);
            if (def.Category is "flora" or "light" or "door" or "machine")
            {
                Assert.Equal(0f, r);
            }
        }
    }
}
