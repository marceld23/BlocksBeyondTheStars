// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Textures;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2832 flora looks (every world wears one of an archetype's tiles) and #2834 accent flora (blossoms and berries in
/// colours of their own): the pick is deterministic and spread over all looks, and the data stays consistent — every
/// look is a bundled tile with its block's alpha rule, and only wild flora carries looks or accents.
/// </summary>
public sealed class FloraLooksTests
{
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void IndexFor_IsDeterministic_AndInRange()
    {
        for (long seed = 0; seed < 200; seed++)
        {
            int a = FloraLooks.IndexFor(seed, "Sol · Terra", "tree_leaves", 11);
            Assert.Equal(a, FloraLooks.IndexFor(seed, "Sol · Terra", "tree_leaves", 11));
            Assert.InRange(a, 0, 11);
        }

        Assert.Equal(0, FloraLooks.IndexFor(42, "Sol · Terra", "flora_moss", 0));
    }

    [Fact]
    public void IndexFor_SpreadsOverEveryLook()
    {
        const int looks = 7; // flora_bush: its own tile + 7 looks
        var counts = new int[looks + 1];
        const int worlds = 4000;
        for (long seed = 0; seed < worlds; seed++)
        {
            counts[FloraLooks.IndexFor(seed * 7919, "Kepler · b", "flora_bush", looks)]++;
        }

        int expected = worlds / (looks + 1);
        foreach (int n in counts)
        {
            Assert.InRange(n, expected * 3 / 4, expected * 5 / 4); // every look turns up, none dominates
        }
    }

    [Fact]
    public void TheArchetypesOfOneWorld_PickIndependently()
    {
        // On a world the bush and the plant must not move in lockstep: across worlds their picks differ often.
        int same = 0;
        for (long seed = 0; seed < 400; seed++)
        {
            if (FloraLooks.IndexFor(seed, "Vega · c", "flora_bush", 7) == FloraLooks.IndexFor(seed, "Vega · c", "flora_plant", 7))
            {
                same++;
            }
        }

        Assert.InRange(same, 20, 90); // ≈ 1 in 8 by chance
    }

    [Theory]
    [InlineData("tree_leaves_look3", "tree_leaves")]
    [InlineData("flora_bush_look12", "flora_bush")]
    [InlineData("flora_bush", "flora_bush")]
    [InlineData("flora_look", "flora_look")]
    [InlineData("flora_bush_lookx", "flora_bush_lookx")]
    [InlineData("_look2", "_look2")]
    public void BaseKeyOf_StripsOnlyANumberedLookSuffix(string key, string expected)
        => Assert.Equal(expected, FloraLooks.BaseKeyOf(key));

    [Fact]
    public void TextureKeyFor_IsTheOwnKeyOrOneOfTheLooks()
    {
        var bush = _content.GetBlock("flora_bush")!;
        Assert.NotNull(bush.Looks);
        var seen = new HashSet<string>();
        for (long seed = 0; seed < 300; seed++)
        {
            string key = FloraLooks.TextureKeyFor(seed, "Sol · Terra", bush);
            Assert.True(key == bush.Key || bush.Looks!.Contains(key), key);
            seen.Add(key);
        }

        Assert.Equal(bush.Looks!.Count + 1, seen.Count);
    }

    [Fact]
    public void Looks_AreWellFormed_OnFloraBlocksOnly()
    {
        var all = new HashSet<string>();
        foreach (var def in _content.Blocks.Values.Where(b => b.Looks != null))
        {
            // #2851: the trunks (TreeWood) carry bark looks too — "building" blocks, but grown, wild wood.
            Assert.True(def.Category == "flora" || TreeWood.IsKey(def.Key), $"{def.Key} ({def.Category}) carries looks");
            Assert.False(FloraCatalog.KeepsOwnColour(def.Key), $"{def.Key} keeps its own colour — a look would be a second fixed look");
            bool leafSlot = TreeFoliage.LeafSlotKeys.Contains(def.Key);
            var own = new HashSet<string>();
            foreach (string look in def.Looks!)
            {
                Assert.True(own.Add(look), $"look '{look}' is listed twice on {def.Key}");
                Assert.NotEqual(def.Key, look);
                Assert.Equal(TextureTiles.AlphaModeOf(def.Key), TextureTiles.AlphaModeOf(look));
                if (leafSlot)
                {
                    // #2851: the three leaf slots share ONE pool (FloraLooks.TextureKeysForGroup keeps them apart on a
                    // world): the looks of tree_leaves and the slots' own tiles.
                    Assert.True(TreeFoliage.LeafSlotKeys.Contains(look) || FloraLooks.BaseKeyOf(look) == TreeFoliage.LeavesKey,
                        $"{def.Key} lists '{look}', which is not in the leaf slots' pool");
                    all.Add(look);
                    continue;
                }

                Assert.Equal(def.Key, FloraLooks.BaseKeyOf(look));
                Assert.True(all.Add(look), $"look '{look}' is listed twice");
                Assert.Null(_content.GetBlock(look)); // a look is a texture, never a block of its own
            }
        }

        Assert.True(all.Count >= 59, $"expected the 59 looks of #2832, found {all.Count}");
    }

    [Fact]
    public void EveryLook_HasABundledTile()
    {
        string dir = Path.Combine(TestPaths.RepoRoot(), "client", "Assets", "Resources", "textures");
        var missing = _content.Blocks.Values
            .Where(b => b.Looks != null)
            .SelectMany(b => b.Looks!)
            .Where(look => !File.Exists(Path.Combine(dir, look + ".bytes")))
            .ToList();
        Assert.True(missing.Count == 0, "looks without a tile (tools/ai-assets: gen_textures.py → bundle_textures.py → "
            + "bake_leaf_alpha.py): " + string.Join(", ", missing));
    }

    [Fact]
    public void AccentFlora_IsWildFlora()
    {
        var accents = _content.Blocks.Values.Where(b => b.Accent).ToList();
        Assert.NotEmpty(accents);
        foreach (var def in accents)
        {
            Assert.Equal("flora", def.Category);
            Assert.False(FloraCatalog.KeepsOwnColour(def.Key), $"{def.Key} keeps its own colour, an accent would never show");
            Assert.False(FloraCatalog.IsRainbow(def.Key), $"{def.Key} is a rainbow plant, its colour is already its own");
        }
    }
}
