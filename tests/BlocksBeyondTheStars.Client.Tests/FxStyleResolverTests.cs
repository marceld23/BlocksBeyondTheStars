// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#2152: a tool's or ship module's effect comes from its <c>fx</c> data; content without it falls back to the
/// pre-overhaul heuristics — and whatever the data says, the result is always a style the client can draw.</summary>
public sealed class FxStyleResolverTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static ToolProperties Tool(ToolKind kind, float range = 0f, FxDefinition? fx = null)
        => new() { Kind = kind, Range = range, Fx = fx };

    // ---------------- Data-driven path ----------------

    [Fact]
    public void ToolFx_FromData_UsesStyleColoursAndNumbers()
    {
        var fx = new FxDefinition { Style = FxStyles.Rail, Color = "#8ce6ff", Color2 = "#ffffff", Charge = 0.12f, Size = 1.5f, Speed = 0f };

        var r = FxStyleResolver.ForTool("gauss_pistol", Tool(ToolKind.Weapon, 26f, fx));

        Assert.True(r.FromData);
        Assert.Equal(FxStyles.Rail, r.Style);
        Assert.Equal(0x8c / 255f, r.Color.R, 4);
        Assert.Equal(0xe6 / 255f, r.Color.G, 4);
        Assert.Equal(1f, r.Color.B, 4);
        Assert.Equal(new FxColor(1f, 1f, 1f), r.Color2);
        Assert.Equal(0.12f, r.Charge, 4);
        Assert.Equal(1.5f, r.Size, 4);
        Assert.Equal(0f, r.Speed);
    }

    [Fact]
    public void ToolFx_WithoutColor2_UsesALightenedPrimary()
    {
        var r = FxStyleResolver.ForTool("fluid_pump", Tool(ToolKind.Gadget, fx: new FxDefinition { Style = FxStyles.Pump, Color = "#3fa9ff" }));

        Assert.Equal(FxStyles.Pump, r.Style);
        Assert.True(r.Color2.R > r.Color.R && r.Color2.G > r.Color.G, "the default core is the primary pushed toward white");
        Assert.True(r.Color2.B >= r.Color.B);
        Assert.Equal(1f, r.Size); // the data default
    }

    [Fact]
    public void ToolFx_MalformedHex_FallsBackToTheStyleColour_NotToTheHeuristics()
    {
        var fx = new FxDefinition { Style = FxStyles.Laser, Color = "ff4a3d", Color2 = "#zzzzzz" };

        var r = FxStyleResolver.ForTool("laser_pistol", Tool(ToolKind.Weapon, 30f, fx));

        Assert.True(r.FromData); // the style is still the data's
        Assert.Equal(FxStyles.Laser, r.Style);
        Assert.Equal(FxStyleResolver.DefaultColor(FxStyles.Laser), r.Color);
        Assert.Equal(r.Color.Lighten(0.6f), r.Color2);
    }

    [Fact]
    public void ToolFx_OutOfRangeNumbers_AreClampedToSaneValues()
    {
        var fx = new FxDefinition { Style = FxStyles.Plasma, Color = "#c86bff", Charge = 999f, Size = -3f, Speed = float.NaN };

        var r = FxStyleResolver.ForTool("plasma_blaster", Tool(ToolKind.Weapon, 32f, fx));

        Assert.InRange(r.Charge, 0f, 2f);
        Assert.Equal(1f, r.Size);
        Assert.Equal(0f, r.Speed);
    }

    [Fact]
    public void UnknownStyle_FallsBackToTheHeuristics()
    {
        var r = FxStyleResolver.ForTool("laser_pistol", Tool(ToolKind.Weapon, 30f, new FxDefinition { Style = "disco_ball", Color = "#00ff00" }));

        Assert.False(r.FromData);
        Assert.Equal(FxStyles.Laser, r.Style);
        Assert.Equal(new FxColor(1f, 0.42f, 0.36f), r.Color); // the heuristic colour, not the data's
    }

    [Fact]
    public void EveryShippedTool_ResolvesFromItsData()
    {
        var tools = Content.Items.Values
            .Where(i => i.Tool is { Kind: ToolKind.Weapon or ToolKind.Drill or ToolKind.Scanner or ToolKind.Gadget })
            .ToList();
        Assert.NotEmpty(tools);
        foreach (var item in tools)
        {
            var r = FxStyleResolver.ForTool(item.Key, item.Tool);
            Assert.True(r.FromData, $"{item.Key} has no usable fx data");
            Assert.True(FxStyleResolver.TryParseHex(item.Tool!.Fx!.Color, out var c) && c == r.Color, $"{item.Key}: colour");
        }

        // The overhaul's personalities, straight from data/items.json.
        Assert.Equal(FxStyles.Slug, FxStyleResolver.ForTool("scrap_pistol", Content.GetItem("scrap_pistol")!.Tool).Style);
        Assert.Equal(45f, FxStyleResolver.ForTool("scrap_pistol", Content.GetItem("scrap_pistol")!.Tool).Speed);
        Assert.Equal(FxStyles.PlasmaBlade, FxStyleResolver.ForTool("plasma_sword", Content.GetItem("plasma_sword")!.Tool).Style);
        Assert.Equal(FxStyles.DrillCrystal, FxStyleResolver.ForTool("diamond_drill", Content.GetItem("diamond_drill")!.Tool).Style);
        Assert.Equal(FxStyles.HealPulse, FxStyleResolver.ForTool("field_medkit", Content.GetItem("field_medkit")!.Tool).Style);
    }

    [Fact]
    public void ShippedShipModules_ResolveFromTheirData()
    {
        var expected = new Dictionary<string, string>
        {
            ["ship_laser_basic"] = FxStyles.TwinPulse,
            ["ship_cannon_1"] = FxStyles.PlasmaBolt,
            ["laser_cannon_2"] = FxStyles.HeavyBeam,
            ["asteroid_breaker"] = FxStyles.DrillBeam,
            ["tractor_beam"] = FxStyles.Tractor,
            ["planet_scanner"] = FxStyles.PlanetScan,
            ["shield_generator"] = FxStyles.Shield,
            ["jump_generator"] = FxStyles.Warp,
        };
        foreach (var (key, style) in expected)
        {
            var r = FxStyleResolver.ForShipModule(key, Content.GetShipModule(key));
            Assert.True(r.FromData, key);
            Assert.Equal(style, r.Style);
        }

        Assert.Equal(0.25f, FxStyleResolver.ForShipModule("laser_cannon_2", Content.GetShipModule("laser_cannon_2")).Charge, 4);
        Assert.Equal(140f, FxStyleResolver.ForShipModule("ship_cannon_1", Content.GetShipModule("ship_cannon_1")).Speed);
    }

    // ---------------- Fallback (heuristic) path ----------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoItem_IsBareHands(string? key)
    {
        Assert.Equal(FxStyles.Fist, FxStyleResolver.ForTool(key, Tool(ToolKind.Weapon, 30f)).Style);
        Assert.Equal(FxStyles.Fist, FxStyleResolver.ForTool("anything", null).Style);
    }

    [Theory]
    [InlineData("gauss_rifle", 26f, FxStyles.Rail)]
    [InlineData("rail_gun", 26f, FxStyles.Rail)]
    [InlineData("scrap_shotgun", 12f, FxStyles.Slug)]
    [InlineData("slug_thrower", 12f, FxStyles.Slug)]
    [InlineData("plasma_sword_mk2", 4f, FxStyles.PlasmaBlade)]
    [InlineData("plasma_blade", 4f, FxStyles.PlasmaBlade)]
    [InlineData("plasma_cannon", 30f, FxStyles.Plasma)]
    [InlineData("laser_rifle", 40f, FxStyles.Laser)]
    [InlineData("sling", 20f, FxStyles.Laser)] // an unnamed ranged weapon draws a beam
    [InlineData("club", 3f, FxStyles.Slash)]   // an unnamed short one swings
    public void WeaponsWithoutData_AreGuessedByKeyThenReach(string key, float range, string style)
    {
        var r = FxStyleResolver.ForTool(key, Tool(ToolKind.Weapon, range));
        Assert.False(r.FromData);
        Assert.Equal(style, r.Style);
    }

    [Fact]
    public void HeuristicColours_KeepThePreOverhaulTints()
    {
        Assert.Equal(new FxColor(0.5f, 0.9f, 1f), FxStyleResolver.ForTool("gauss_rifle", Tool(ToolKind.Weapon, 26f)).Color);
        Assert.Equal(new FxColor(0.95f, 0.82f, 0.5f), FxStyleResolver.ForTool("scrap_gun", Tool(ToolKind.Weapon, 16f)).Color);
        Assert.Equal(new FxColor(0.92f, 0.45f, 1f), FxStyleResolver.ForTool("plasma_gun", Tool(ToolKind.Weapon, 30f)).Color);
        Assert.Equal(new FxColor(1f, 0.95f, 0.8f), FxStyleResolver.ForTool("club", Tool(ToolKind.Weapon, 3f)).Color);
    }

    [Theory]
    [InlineData("rock_drill", ToolKind.Drill, FxStyles.Drill)]
    [InlineData("heavy_mining_beam", ToolKind.Drill, FxStyles.MiningBeam)]
    [InlineData("pocket_scanner", ToolKind.Scanner, FxStyles.Scan)]
    [InlineData("rail_linker", ToolKind.Gadget, FxStyles.Generic)] // a gadget is not a rail gun
    [InlineData("laser_level", ToolKind.Gadget, FxStyles.Generic)]
    [InlineData("wrench", ToolKind.Repair, FxStyles.Generic)]
    public void OtherToolsWithoutData_AreGuessedByKind(string key, ToolKind kind, string style)
        => Assert.Equal(style, FxStyleResolver.ForTool(key, Tool(kind)).Style);

    [Fact]
    public void ShipModulesWithoutData_FallBackByWeaponClassThenKey()
    {
        var breaker = new ShipModuleDefinition { Key = "rock_cutter", Stats = new Dictionary<string, double> { ["weapon_class"] = 0 } };
        Assert.Equal(FxStyles.DrillBeam, FxStyleResolver.ForShipModule("rock_cutter", breaker).Style);

        var pulse = new ShipModuleDefinition { Key = "laser_cannon_3", Stats = new Dictionary<string, double> { ["weapon_class"] = 1 } };
        Assert.Equal(FxStyles.HeavyBeam, FxStyleResolver.ForShipModule("laser_cannon_3", pulse).Style);
        Assert.Equal(FxStyles.PlasmaBolt, FxStyleResolver.ForShipModule("ship_cannon_9", null).Style);
        Assert.Equal(FxStyles.TwinPulse, FxStyleResolver.ForShipModule("mystery_gun", null).Style);
        Assert.Equal(FxStyles.TwinPulse, FxStyleResolver.ForShipModule(null, null).Style);

        var bogus = new ShipModuleDefinition { Key = "ship_cannon_x", Fx = new FxDefinition { Style = "nope", Color = "#ffffff" } };
        var r = FxStyleResolver.ForShipModule("ship_cannon_x", bogus);
        Assert.False(r.FromData);
        Assert.Equal(FxStyles.PlasmaBolt, r.Style);
    }

    [Fact]
    public void EveryResolvedStyle_IsAKnownOne_AndHasADefaultColour()
    {
        foreach (var style in FxStyles.All)
        {
            var c = FxStyleResolver.DefaultColor(style);
            Assert.InRange(c.R, 0f, 1f);
            Assert.InRange(c.G, 0f, 1f);
            Assert.InRange(c.B, 0f, 1f);
        }

        Assert.Contains(FxStyleResolver.ForTool("x", Tool(ToolKind.None)).Style, FxStyles.All);
        Assert.Contains(FxStyleResolver.ForShipModule("x", null).Style, FxStyles.All);
    }

    // ---------------- Hex parsing ----------------

    [Theory]
    [InlineData("#000000", 0f, 0f, 0f)]
    [InlineData("#ffffff", 1f, 1f, 1f)]
    [InlineData("#FF8000", 1f, 0x80 / 255f, 0f)]
    [InlineData("#4dff88", 0x4d / 255f, 1f, 0x88 / 255f)]
    public void TryParseHex_ReadsRrGgBb(string hex, float r, float g, float b)
    {
        Assert.True(FxStyleResolver.TryParseHex(hex, out var c));
        Assert.Equal(r, c.R, 4);
        Assert.Equal(g, c.G, 4);
        Assert.Equal(b, c.B, 4);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#fff")]
    [InlineData("ffffff")]
    [InlineData("#fffffff")]
    [InlineData("#gg0000")]
    [InlineData("#-12345")]
    [InlineData("# 12345")]
    public void TryParseHex_RejectsAnythingElse(string? hex)
        => Assert.False(FxStyleResolver.TryParseHex(hex, out _));

    // ---------------- Style classes ----------------

    [Fact]
    public void StyleClasses_SplitMeleeTravellingAndBeams()
    {
        Assert.All(new[] { FxStyles.Slash, FxStyles.Vibro, FxStyles.PlasmaBlade, FxStyles.Fist }, s => Assert.True(FxStyleResolver.IsMelee(s)));
        Assert.All(new[] { FxStyles.Slug, FxStyles.Plasma, FxStyles.PlasmaBolt, FxStyles.TwinPulse }, s => Assert.True(FxStyleResolver.IsTravelling(s)));
        Assert.All(new[] { FxStyles.Laser, FxStyles.Rail, FxStyles.MiningBeam, FxStyles.HeavyBeam, FxStyles.DrillBeam }, s => Assert.True(FxStyleResolver.IsBeam(s)));

        // No style is two of these at once (twin_pulse is a travelling pulse, not a beam).
        foreach (var style in FxStyles.All)
        {
            int classes = (FxStyleResolver.IsMelee(style) ? 1 : 0) + (FxStyleResolver.IsTravelling(style) ? 1 : 0) + (FxStyleResolver.IsBeam(style) ? 1 : 0);
            Assert.True(classes <= 1, style);
        }

        Assert.False(FxStyleResolver.IsBeam(FxStyles.TwinPulse));
        Assert.False(FxStyleResolver.IsMelee(FxStyles.Drill));
    }

    // ---------------- #2278 the glove styles ----------------

    [Theory]
    [InlineData("shock_gloves", FxStyles.ShockPush)]
    [InlineData("energy_gloves", FxStyles.EnergyFist)]
    public void TheGloves_AreMelee_TwoHanded_AndCarryTheirLookFromData(string key, string style)
    {
        var r = FxStyleResolver.ForTool(key, Content.GetItem(key)!.Tool);
        Assert.True(r.FromData);
        Assert.Equal(style, r.Style);
        Assert.True(FxStyleResolver.IsMelee(style));
        Assert.True(FxStyleResolver.IsGlove(style));
        Assert.False(FxStyleResolver.IsTravelling(style));
        Assert.False(FxStyleResolver.IsBeam(style));
    }

    [Fact]
    public void TheGloveStyles_DefaultToCyanAndGold_AndOnlyTheyAreGloves()
    {
        var cyan = FxStyleResolver.DefaultColor(FxStyles.ShockPush);
        Assert.True(cyan.B > cyan.R && cyan.G > cyan.R, "the shock push is cyan");
        var gold = FxStyleResolver.DefaultColor(FxStyles.EnergyFist);
        Assert.True(gold.R > gold.G && gold.G > gold.B, "the energy fist is gold");
        Assert.NotEqual(FxStyleResolver.DefaultColor(FxStyles.Stasis), cyan); // not the stasis gadget's blue

        Assert.All(FxStyles.All.Where(s => s is not FxStyles.ShockPush and not FxStyles.EnergyFist),
            s => Assert.False(FxStyleResolver.IsGlove(s)));

        // A glove item without fx data stays drawable — it falls back to a swing, never to nothing.
        Assert.Equal(FxStyles.Slash, FxStyleResolver.ForTool("boxing_gloves", Tool(ToolKind.Weapon, 3f)).Style);
    }

    [Fact]
    public void MeleeCues_FollowTheStyle_SoEveryoneHearsTheSameSwing()
    {
        Assert.Equal("glove_shock_blast", FxStyleResolver.MeleeSwingCue(FxStyles.ShockPush));
        Assert.Equal("glove_whoosh", FxStyleResolver.MeleeSwingCue(FxStyles.EnergyFist));
        Assert.Equal("glove_energy_hit", FxStyleResolver.MeleeHitCue(FxStyles.EnergyFist));
        Assert.Equal("glove_shock_blast", FxStyleResolver.MeleeHitCue(FxStyles.ShockPush));
        foreach (var style in new[] { FxStyles.Slash, FxStyles.Vibro, FxStyles.PlasmaBlade, FxStyles.Fist, FxStyles.Generic })
        {
            Assert.Equal("melee_swing", FxStyleResolver.MeleeSwingCue(style));
            Assert.Equal("melee_hit", FxStyleResolver.MeleeHitCue(style));
        }
    }
}
