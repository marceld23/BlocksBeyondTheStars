// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The flora catalog's absolute order and its key lookups. A species' catalog index is persisted identity: it is a wild
/// species' roster id (<c>"fl" + i</c>), the index of its per-species RNG stream, and the plant-form bits a bred plant
/// stores in a save (<c>FloraForm</c>). Reordering, sorting or inserting above an existing entry therefore renames every
/// world's plants and corrupts saved bred plants. A new species wave APPENDS before the cultivated crops (which must stay
/// at the tail) and updates <see cref="ExpectedOrder"/> on purpose, as a conscious one-line change — never "fix" this test
/// by sorting either list. The catalog is static, so no content is loaded.
/// </summary>
public sealed class FloraCatalogOrderTests
{
    private static readonly string[] ExpectedOrder =
    {
        "flora_plant", "flora_fern", "flora_flower", "flora_bush", "flora_vine", "flora_mushroom",
        "flora_cactus", "flora_dryshrub",
        "flora_reed", "flora_glowcap",
        "flora_kelp", "flora_lily",
        "flora_frostflower", "flora_emberbloom",
        "flora_crystal",
        "flora_palm", "flora_orchid", "flora_bellflower", "flora_glowvine",
        "flora_moss", "flora_sporepod",
        "flora_succulent", "flora_thornbush",
        "flora_pitcher", "flora_puffball",
        "flora_lichen", "flora_ashweed",
        "flora_coral", "flora_seagrass",
        "flora_tendril", "flora_bulb", "flora_gasbloom", "flora_alienfern", "flora_shardbloom",
        "flora_grasstuft", "flora_rockflower", "flora_snowbush", "flora_icereed", "flora_saltgrass", "flora_cinderbush",
        "flora_sunblossom", "flora_tulip", "flora_hangkelp",
        "flora_cavecap", "flora_glowmoss", "flora_glowthread", "flora_prismbloom",
        "flora_fruit_round", "flora_fruit_long", "flora_fruit_grape", "flora_fruit_banana",
        FifiPlant.BerriesKey,
        "flora_berrybush", "flora_blossombush", "flora_featherbush", "flora_droopbush", "flora_needlebush",
        "flora_frostberry", "flora_clover", "flora_daisy", "flora_thistle", "flora_herb", "flora_foxtail", "flora_lupine",
        FloraCatalog.LanternFruitKey,
        "flora_pumpkin", // #3005 (generation 32)
        "flora_cropberry", "flora_cropgrain", "flora_cropshroom",
    };

    // The two host arrays several species share (FloraCatalog.CaveRock and FloraCatalog.FruitHosts).
    private static readonly string[] CaveRock =
    {
        "stone", "basalt", "granite", "sandstone", "sulfur_stone", "dirt", "mud", "mycelium", "crystal", "salt", "ice",
    };

    private static readonly string[] FruitHosts = { "tree_leaves", "pine_needles", "palm_frond", "tree_leaves_b", "tree_leaves_c" };

    [Fact]
    public void All_KeysAreInTheExactCatalogOrder()
    {
        Assert.Equal(ExpectedOrder, FloraCatalog.All.Select(s => s.Key).ToArray());
        Assert.Equal(69, FloraCatalog.All.Count); // #3005: the pumpkin
        Assert.Equal(FloraCatalog.All.Count, FloraCatalog.All.Select(s => s.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void SharedHostArrays_AreStable()
    {
        Assert.Equal(CaveRock, FloraCatalog.Find("flora_cavecap")!.Hosts);
        Assert.Equal(FruitHosts, FloraCatalog.Find("flora_fruit_round")!.Hosts);

        var mushroom = FloraCatalog.Find("flora_mushroom")!;
        Assert.Equal(new[] { "grass", "mud", "mycelium" }, mushroom.Hosts);
        Assert.Equal(CaveRock, mushroom.CaveHosts);

        // No CaveHosts of its own: the cave hosts are the very Hosts array (the null → Hosts default).
        var plant = FloraCatalog.Find("flora_plant")!;
        Assert.Same(plant.Hosts, plant.CaveHosts);
        Assert.Empty(plant.LateHosts);

        Assert.Equal(new[] { "peat" }, FloraCatalog.Find("flora_reed")!.LateHosts);
    }

    [Fact]
    public void Lookups_AgreeWithTheRecord()
    {
        foreach (var sp in FloraCatalog.All)
        {
            Assert.Same(sp, FloraCatalog.Find(sp.Key));
            Assert.Equal(sp.Cultivated, FloraCatalog.IsCultivated(sp.Key));
            Assert.Equal(sp.Height == FloraHeight.Tall, FloraCatalog.IsTall(sp.Key));
            Assert.Equal(sp.Hanging, FloraCatalog.IsHanging(sp.Key));
            Assert.Equal(sp.Solid, FloraCatalog.IsSolid(sp.Key));
            Assert.Equal(sp.Rainbow, FloraCatalog.IsRainbow(sp.Key));
            Assert.Equal(sp.Fruit, FloraCatalog.IsFruit(sp.Key));
            Assert.Equal(sp.Authored, FloraCatalog.IsAuthored(sp.Key));
            Assert.Equal(sp.Light, FloraCatalog.LightOf(sp.Key));
            Assert.Equal(sp.Cultivated || sp.Authored, FloraCatalog.KeepsOwnColour(sp.Key));
        }
    }

    [Fact]
    public void UnknownKey_YieldsDefaults()
    {
        // The two flora_* blocks that are no species, a plain block, the empty key, and null (never a species).
        string[] unknown = { "flora_hybrid", "flora_sapling", "grass", "", null! };
        foreach (var key in unknown)
        {
            Assert.Null(FloraCatalog.Find(key));
            Assert.False(FloraCatalog.IsCultivated(key));
            Assert.False(FloraCatalog.IsTall(key));
            Assert.False(FloraCatalog.IsHanging(key));
            Assert.False(FloraCatalog.IsSolid(key));
            Assert.False(FloraCatalog.IsRainbow(key));
            Assert.False(FloraCatalog.IsFruit(key));
            Assert.False(FloraCatalog.IsAuthored(key));
            Assert.False(FloraCatalog.KeepsOwnColour(key));
            Assert.Equal(0f, FloraCatalog.LightOf(key));
        }
    }
}
