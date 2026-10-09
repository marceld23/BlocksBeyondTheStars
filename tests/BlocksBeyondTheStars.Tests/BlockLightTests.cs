// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Real light sources (#2036, #2407): which block types flood coloured light into their surroundings on top of their
/// own glow. The lantern and the campfire used to glow without lighting anything — they sat below the old implicit
/// "colour + emission ≥ 0.85" rule — so every shipped light now declares its <c>lightColor</c> in data. Since #2407
/// lava, crystals and the glowing flora light too, quietly (a small <c>lightRadius</c>, lava from its surface only);
/// ores and the machines keep only their self-glow.
/// </summary>
public sealed class BlockLightTests
{
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private int LightOf(string key)
    {
        var def = _content.GetBlock(key);
        Assert.NotNull(def);
        return BlockLight.NaturalColorOf(def);
    }

    [Theory]
    [InlineData("lantern")]
    [InlineData("campfire")]
    [InlineData("forge")]
    [InlineData("beam_block")]
    public void TheNewFixtures_LightTheirSurroundings(string key)
    {
        Assert.NotEqual(0, LightOf(key));
    }

    [Fact]
    public void TheExistingLights_KeepTheirColour()
    {
        // Migrated from the hard-coded lamp switch and the implicit threshold — no visible change.
        Assert.Equal(0xFFFFFF, LightOf("light_white"));
        Assert.Equal(0xFF3838, LightOf("light_red"));
        Assert.Equal(0x53FF61, LightOf("light_green"));
        foreach (var key in new[] { "torch", "fire", "strip_light_cyan", "strip_light_warm" })
        {
            Assert.Equal(_content.GetBlock(key)!.Color!.Value & 0xFFFFFF, LightOf(key));
        }
    }

    [Fact]
    public void EveryLightCategoryBlock_IsALightSource()
    {
        // The guard against the next lantern: a fixture in the build palette's "light" section must light.
        var dark = _content.Blocks.Values
            .Where(b => b.Category == "light" && BlockLight.NaturalColorOf(b) == 0)
            .Select(b => b.Key);
        Assert.Empty(dark);
    }

    [Fact]
    public void EveryShippedLight_DeclaresItsColour()
    {
        // The authored-fixture fallback is for Material Editor materials only; shipped data says what it means.
        var implicitLights = _content.Blocks.Values
            .Where(b => b.LightColor is null && BlockLight.NaturalColorOf(b) != 0)
            .Select(b => b.Key);
        Assert.Empty(implicitLights);
    }

    [Theory]
    [InlineData("iron_ore")]
    [InlineData("copper_ore")]
    [InlineData("flora_prismbloom")] // generation 11 plants light through the flora catalog, not their block type
    [InlineData("ship_core")]
    [InlineData("station_core")]
    [InlineData("matter_forge")]
    [InlineData("energy_fence")]
    [InlineData("stone")]
    public void OresAndMachines_OnlyGlow(string key)
    {
        Assert.Equal(0, LightOf(key));
    }

    [Theory]
    [InlineData("lava", true)]
    [InlineData("crystal", true)]
    [InlineData("flora_glowcap", false)]
    [InlineData("flora_crystal", false)]
    [InlineData("flora_shardbloom", false)]
    [InlineData("flora_emberbloom", false)]
    [InlineData("flora_glowvine", false)]
    [InlineData("flora_cinderbush", false)]
    public void NaturalEmitters_LightQuietly(string key, bool surfaceOnly)
    {
        // #2407: a light of their own, but short — never the fixtures' full reach — and lava/crystal from the surface only.
        var def = _content.GetBlock(key);
        Assert.NotNull(def);
        Assert.NotEqual(0, BlockLight.NaturalColorOf(def));
        Assert.InRange(BlockLight.RadiusOf(def), 2, 4);
        Assert.Equal(surfaceOnly, def!.LightSurfaceOnly);

        int packed = BlockLight.PackedLightOf(def);
        Assert.Equal(BlockLight.NaturalColorOf(def), BlockLight.ColorFrom(packed));
        Assert.Equal(BlockLight.RadiusOf(def), BlockLight.RadiusFrom(packed));
        Assert.Equal(surfaceOnly, BlockLight.SurfaceOnlyFrom(packed));
    }

    [Fact]
    public void Fixtures_KeepTheFullReach()
    {
        foreach (var key in new[] { "light_white", "torch", "lantern", "campfire" })
        {
            int packed = BlockLight.PackedLightOf(_content.GetBlock(key));
            Assert.Equal(BlockLight.DefaultRadius, BlockLight.RadiusFrom(packed));
            Assert.False(BlockLight.SurfaceOnlyFrom(packed));
        }
    }

    [Fact]
    public void PackedSource_RoundTripsAndKeepsReachWhenRecoloured()
    {
        int packed = BlockLight.Pack(0xFF6A1A, 4, surfaceOnly: true);
        Assert.Equal(0xFF6A1A, BlockLight.ColorFrom(packed));
        Assert.Equal(4, BlockLight.RadiusFrom(packed));
        Assert.True(BlockLight.SurfaceOnlyFrom(packed));

        int dyed = BlockLight.Recolor(packed, 0x2040FF);
        Assert.Equal(0x2040FF, BlockLight.ColorFrom(dyed));
        Assert.Equal(4, BlockLight.RadiusFrom(dyed));
        Assert.True(BlockLight.SurfaceOnlyFrom(dyed));

        // A plain colour (a placed glow block) is a full-reach source — the upper bits stay clear.
        Assert.Equal(BlockLight.DefaultRadius, BlockLight.RadiusFrom(0x00FFFFFF));
        Assert.Equal(0, BlockLight.Pack(0xABCDEF, BlockLight.DefaultRadius, false) >> 24);
    }

    [Fact]
    public void AnAuthoredFixture_WithoutALightColour_StillCountsAboveTheThreshold()
    {
        var bright = new BlockDefinition { Key = "custom_lamp", Color = 0x123456, Emission = BlockLight.AuthoredFixtureEmission };
        var dim = new BlockDefinition { Key = "custom_panel", Color = 0x123456, Emission = 0.8f };
        Assert.Equal(0x123456, BlockLight.NaturalColorOf(bright));
        Assert.Equal(0, BlockLight.NaturalColorOf(dim));
    }

    [Fact]
    public void AnExplicitLightColour_WinsOverTheFallback()
    {
        var optedOut = new BlockDefinition { Key = "bright_panel", Color = 0x123456, Emission = 1f, LightColor = 0 };
        var dimButLit = new BlockDefinition { Key = "hearth", Emission = 0.2f, LightColor = 0x7F3300 };
        Assert.Equal(0, BlockLight.NaturalColorOf(optedOut));
        Assert.Equal(0x7F3300, BlockLight.NaturalColorOf(dimButLit));
        Assert.Equal(0, BlockLight.NaturalColorOf(null));
    }
}
