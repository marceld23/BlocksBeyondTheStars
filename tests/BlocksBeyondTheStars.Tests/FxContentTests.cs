// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#2152: every action tool and every ship weapon carries its effect look as data (<c>"fx"</c> in
/// <c>data/items.json</c> / <c>data/ship_modules.json</c>) — a known style and readable <c>#rrggbb</c> colours — so the
/// client never has to guess a shipped item's look from its name.</summary>
public sealed class FxContentTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static readonly Regex Hex = new("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant, System.TimeSpan.FromSeconds(1));

    private static void AssertValid(string owner, FxDefinition? fx)
    {
        Assert.True(fx is not null, $"{owner}: no fx");
        Assert.True(FxStyles.IsKnown(fx!.Style), $"{owner}: unknown fx style '{fx.Style}'");
        Assert.True(fx.Color is not null && Hex.IsMatch(fx.Color), $"{owner}: fx color '{fx.Color}' is not #rrggbb");
        Assert.True(fx.Color2 is null || Hex.IsMatch(fx.Color2), $"{owner}: fx color2 '{fx.Color2}' is not #rrggbb");
        Assert.True(fx.Charge is >= 0f and <= 2f, $"{owner}: fx charge {fx.Charge} out of range");
        Assert.True(fx.Size is > 0f and <= 8f, $"{owner}: fx size {fx.Size} out of range");
        Assert.True(fx.Speed >= 0f, $"{owner}: fx speed {fx.Speed} is negative");
    }

    [Fact]
    public void EveryActionTool_HasAnFx_WithAKnownStyleAndReadableColours()
    {
        var tools = Content.Items.Values
            .Where(i => i.Tool is { Kind: ToolKind.Weapon or ToolKind.Drill or ToolKind.Scanner or ToolKind.Gadget })
            .ToList();
        Assert.True(tools.Count >= 30, "the shipped tools went missing");
        foreach (var item in tools)
        {
            AssertValid(item.Key, item.Tool!.Fx);
        }

        // Spot checks against the overhaul's look sheet.
        Assert.Equal(FxStyles.Rail, Content.GetItem("gauss_pistol")!.Tool!.Fx!.Style);
        Assert.Equal(0.12f, Content.GetItem("gauss_pistol")!.Tool!.Fx!.Charge, 3);
        Assert.Equal(45f, Content.GetItem("scrap_pistol")!.Tool!.Fx!.Speed);
        Assert.Equal(FxStyles.HealPulse, Content.GetItem("field_medkit")!.Tool!.Fx!.Style);
        Assert.Equal(FxStyles.DrillCrystal, Content.GetItem("diamond_drill")!.Tool!.Fx!.Style);
    }

    [Fact]
    public void EveryShipWeapon_HasAnFx()
    {
        var weapons = Content.ShipModules.Values.Where(m => m.Stats.ContainsKey("weapon_damage")).ToList();
        Assert.NotEmpty(weapons);
        foreach (var module in weapons)
        {
            AssertValid(module.Key, module.Fx);
        }
    }

    [Theory]
    [InlineData("ship_laser_basic", FxStyles.TwinPulse)]
    [InlineData("ship_cannon_1", FxStyles.PlasmaBolt)]
    [InlineData("laser_cannon_2", FxStyles.HeavyBeam)]
    [InlineData("asteroid_breaker", FxStyles.DrillBeam)]
    [InlineData("tractor_beam", FxStyles.Tractor)]
    [InlineData("planet_scanner", FxStyles.PlanetScan)]
    [InlineData("shield_generator", FxStyles.Shield)]
    [InlineData("jump_generator", FxStyles.Warp)]
    public void TheEffectModules_CarryTheirLook(string key, string style)
    {
        var module = Content.GetShipModule(key);
        Assert.NotNull(module);
        AssertValid(key, module!.Fx);
        Assert.Equal(style, module.Fx!.Style);
    }

    [Fact]
    public void NoContentNamesAnUnknownStyle()
    {
        foreach (var item in Content.Items.Values.Where(i => i.Tool?.Fx is not null))
        {
            AssertValid(item.Key, item.Tool!.Fx);
        }

        foreach (var module in Content.ShipModules.Values.Where(m => m.Fx is not null))
        {
            AssertValid(module.Key, module.Fx);
        }
    }

    [Fact]
    public void EveryStyleConstant_IsKnown_AndNothingElseIs()
    {
        var constants = typeof(FxStyles)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.Equal(35, constants.Count); // +ship_scan (#2237), +shock_push +energy_fist (#2278), +rope (#2317)
        Assert.Equal(constants.OrderBy(s => s, System.StringComparer.Ordinal), FxStyles.All.OrderBy(s => s, System.StringComparer.Ordinal));
        Assert.False(FxStyles.IsKnown(null));
        Assert.False(FxStyles.IsKnown(string.Empty));
        Assert.False(FxStyles.IsKnown("LASER")); // the data spells styles in lower case
    }

    [Fact]
    public void ActionKinds_AreOneToSix()
    {
        Assert.False(FxActionKinds.IsValid(0));
        for (byte k = FxActionKinds.Shot; k <= FxActionKinds.Place; k++)
        {
            Assert.True(FxActionKinds.IsValid(k));
        }

        Assert.False(FxActionKinds.IsValid(7));
        Assert.False(FxActionKinds.IsValid(255));
    }
}
