// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Real light sources (#2036): which block types flood coloured light into their surroundings on top of their own
/// glow. The lantern and the campfire used to glow without lighting anything — they sat below the old implicit
/// "colour + emission ≥ 0.85" rule — so every shipped light now declares its <c>lightColor</c> in data, and the
/// natural emitters (lava, crystals, ores, glowing flora) and the machines keep only their self-glow.
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
    [InlineData("lava")]
    [InlineData("crystal")]
    [InlineData("iron_ore")]
    [InlineData("copper_ore")]
    [InlineData("flora_glowcap")]
    [InlineData("flora_crystal")]
    [InlineData("flora_prismbloom")] // generation 11 plants light through the flora catalog, not their block type
    [InlineData("ship_core")]
    [InlineData("station_core")]
    [InlineData("matter_forge")]
    [InlineData("energy_fence")]
    [InlineData("stone")]
    public void NaturalEmittersAndMachines_OnlyGlow(string key)
    {
        Assert.Equal(0, LightOf(key));
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
