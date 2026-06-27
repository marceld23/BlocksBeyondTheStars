// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>A hand-designed structure template is faithfully turned into a station/settlement
/// structure (blocks become voxels, markers become interaction points).</summary>
public sealed class StructureTemplateTests
{
    private static GameContent Content() => ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void StationTemplate_BuildsVoxelsAndMarkers()
    {
        var c = Content();
        ushort hull = c.GetBlock("iron_wall")!.NumericId.Value;

        var t = new StructureTemplate
        {
            Key = "test_station",
            Name = "Test",
            Tier = "small",
            Kind = "station",
            Width = 6,
            Height = 4,
            Length = 6,
            Cells = new List<TemplateCell>
            {
                new() { X = 1, Y = 0, Z = 1, Kind = "block", Id = "iron_wall" },
                new() { X = 4, Y = 0, Z = 4, Kind = "block", Id = "glass" },
                new() { X = 3, Y = 1, Z = 3, Kind = "marker", Id = "heal_tank" },
            },
        };

        var s = StationGenerator.FromTemplate(t, c);

        Assert.Equal(6, s.Width);
        Assert.Equal(hull, s.Get(1, 0, 1));      // block placed
        Assert.Equal(0, s.Get(0, 0, 0));         // empty stays air
        Assert.Contains(s.Markers, m => m.Type == "heal_tank");
        Assert.Contains(s.Markers, m => m.Type == "vendor");        // guaranteed essentials
        Assert.Contains(s.Markers, m => m.Type == "mission_board");
    }

    [Fact]
    public void SettlementTemplate_BuildsVoxelsAndMarkers()
    {
        var c = Content();
        ushort stone = c.GetBlock("stone")!.NumericId.Value;

        var t = new StructureTemplate
        {
            Key = "test_town",
            Name = "Test",
            Tier = "village",
            Kind = "settlement",
            Width = 5,
            Height = 4,
            Length = 5,
            Cells = new List<TemplateCell>
            {
                new() { X = 0, Y = 0, Z = 0, Kind = "block", Id = "stone" },
                new() { X = 2, Y = 1, Z = 2, Kind = "marker", Id = "npc" },
                new() { X = 1, Y = 1, Z = 1, Kind = "marker", Id = "vendor" },
            },
        };

        var s = SettlementGenerator.FromTemplate(t, c);

        Assert.Equal("village", s.Tier);
        Assert.False(s.Ruined);
        Assert.Equal(stone, s.Get(0, 0, 0));
        Assert.Contains(s.Markers, m => m.Type == "npc");
        Assert.Contains(s.Markers, m => m.Type == "vendor");
    }

    [Fact]
    public void StationTemplate_CarriesTintGlowShape()
    {
        var c = Content();
        var t = new StructureTemplate
        {
            Key = "tinted",
            Tier = "small",
            Kind = "station",
            Width = 3,
            Height = 2,
            Length = 3,
            Cells = new List<TemplateCell>
            {
                new() { X = 1, Y = 0, Z = 1, Kind = "block", Id = "iron_wall", Tint = 0x3F6FB0, Glow = 0x00FFFF, Shape = 0x15 },
                new() { X = 0, Y = 0, Z = 0, Kind = "block", Id = "stone" }, // plain, no modifier
            },
        };

        var s = StationGenerator.FromTemplate(t, c);

        Assert.Equal((0x3F6FB0, 0x00FFFF), s.GetModifier(1, 0, 1));
        Assert.Equal(0x15, s.GetShape(1, 0, 1));
        Assert.Equal((0, 0), s.GetModifier(0, 0, 0)); // plain cell carries nothing
        Assert.Equal(0, s.GetShape(0, 0, 0));
    }

    [Fact]
    public void SettlementTemplate_CarriesTintGlowShape()
    {
        var c = Content();
        var t = new StructureTemplate
        {
            Key = "tinted_town",
            Tier = "village",
            Kind = "settlement",
            Width = 3,
            Height = 2,
            Length = 3,
            Cells = new List<TemplateCell>
            {
                new() { X = 2, Y = 1, Z = 2, Kind = "block", Id = "wood_log", Tint = 0xAA5500, Shape = 0x06 },
            },
        };

        var s = SettlementGenerator.FromTemplate(t, c);

        Assert.Equal((0xAA5500, 0), s.GetModifier(2, 1, 2));
        Assert.Equal(0x06, s.GetShape(2, 1, 2));
    }

    [Fact]
    public void LargeTemplate_HonorsFullDimensions_NoSizeCap()
    {
        // The in-game structure editor now authors builds up to 128³; the generators must carry those full
        // dimensions through untruncated (block + marker at the far corner round-trip), so a big hand-built
        // station/city isn't silently clamped to a smaller envelope.
        var c = Content();
        ushort hull = c.GetBlock("iron_wall")!.NumericId.Value;
        ushort stone = c.GetBlock("stone")!.NumericId.Value;

        const int n = 128;
        var stationT = new StructureTemplate
        {
            Key = "huge_station",
            Name = "Huge",
            Tier = "huge",
            Kind = "station",
            Width = n,
            Height = n,
            Length = n,
            Cells = new List<TemplateCell>
            {
                new() { X = 0, Y = 0, Z = 0, Kind = "block", Id = "iron_wall" },
                new() { X = n - 1, Y = n - 1, Z = n - 1, Kind = "block", Id = "iron_wall" },
                new() { X = n - 1, Y = 0, Z = n - 1, Kind = "marker", Id = "vendor" },
            },
        };

        var station = StationGenerator.FromTemplate(stationT, c);
        Assert.Equal(n, station.Width);
        Assert.Equal(n, station.Height);
        Assert.Equal(n, station.Length);
        Assert.Equal(hull, station.Get(n - 1, n - 1, n - 1)); // far corner survives — no truncation

        var settlementT = new StructureTemplate
        {
            Key = "huge_city",
            Name = "City",
            Tier = "city",
            Kind = "settlement",
            Width = n,
            Height = n,
            Length = n,
            Cells = new List<TemplateCell>
            {
                new() { X = n - 1, Y = n - 1, Z = n - 1, Kind = "block", Id = "stone" },
            },
        };

        var city = SettlementGenerator.FromTemplate(settlementT, c);
        Assert.Equal(n, city.Width);
        Assert.Equal(n, city.Height);
        Assert.Equal(n, city.Length);
        Assert.Equal(stone, city.Get(n - 1, n - 1, n - 1));
    }

    // --- Tier-matched / pack-filtered / weighted selection (P1) ---

    private static StructureTemplate Tpl(string key, string tier, string pack = "default", int weight = 1)
        => new() { Key = key, Name = key, Tier = tier, Pack = pack, Weight = weight, Width = 1, Height = 1, Length = 1 };

    [Fact]
    public void ShippedSampleTemplates_LoadIntoBothPools()
    {
        var content = Content();

        Assert.NotEmpty(content.StationTemplates);
        Assert.NotEmpty(content.SettlementTemplates);
        Assert.Contains(content.StationTemplates, t => t.Key == "hub_outpost" && t.Tier == "medium");
        Assert.Contains(content.SettlementTemplates, t => t.Key == "river_hamlet" && t.Tier == "village");
        Assert.Contains("default", content.StructurePacks);
    }

    [Fact]
    public void Pick_MatchesTier_AndReturnsNullForEmptyTier()
    {
        var content = Content();
        content.SetStructureTemplates(
            new[] { Tpl("a", "small"), Tpl("b", "huge") },
            Array.Empty<StructureTemplate>());

        var rng = new Random(1);
        Assert.Equal("a", content.PickStationTemplate("small", null, rng)!.Key);
        Assert.Equal("b", content.PickStationTemplate("huge", null, rng)!.Key);
        Assert.Null(content.PickStationTemplate("colossal", null, rng)); // no template at this tier
    }

    [Fact]
    public void Pick_FiltersByEnabledPacks()
    {
        var content = Content();
        content.SetStructureTemplates(
            new[] { Tpl("vanilla", "medium", pack: "default"), Tpl("mine", "medium", pack: "mybuilds") },
            Array.Empty<StructureTemplate>());

        var rng = new Random(7);
        for (int i = 0; i < 50; i++)
        {
            Assert.Equal("mine", content.PickStationTemplate("medium", new[] { "mybuilds" }, rng)!.Key);
        }

        Assert.Null(content.PickStationTemplate("medium", new[] { "nonexistent" }, rng)); // pack has none here
        Assert.NotNull(content.PickStationTemplate("medium", Array.Empty<string>(), rng)); // empty ⇒ all packs
        Assert.NotNull(content.PickStationTemplate("medium", null, rng));                  // null ⇒ all packs
    }

    [Fact]
    public void Pick_RespectsWeight()
    {
        var content = Content();
        content.SetStructureTemplates(
            new[] { Tpl("rare", "town", weight: 1), Tpl("common", "town", weight: 9) },
            Array.Empty<StructureTemplate>());

        var rng = new Random(123);
        int common = 0, rare = 0;
        for (int i = 0; i < 1000; i++)
        {
            var pick = content.PickStationTemplate("town", null, rng);
            if (pick!.Key == "common") common++; else rare++;
        }

        Assert.True(common > rare * 3, $"expected 'common' to dominate, got common={common} rare={rare}");
        Assert.True(rare > 0, "the low-weight template should still be reachable");
    }

    [Fact]
    public void ZeroWeight_IsClampedToReachable()
    {
        var content = Content();
        content.SetStructureTemplates(
            new[] { Tpl("zero", "medium", weight: 0) },
            Array.Empty<StructureTemplate>());

        Assert.Equal("zero", content.PickStationTemplate("medium", null, new Random(0))!.Key);
    }

    [Fact]
    public void UserContentFolder_MergesTemplatesIntoPools()
    {
        // The local server reads editor output from a writable user-content folder, merged on top of the
        // shipped pools — so an in-game build appears in new worlds without a Python merge or rebuild.
        var root = Path.Combine(Path.GetTempPath(), "bbts_usercontent_" + Guid.NewGuid().ToString("N"));
        var stationDir = Path.Combine(root, "station_templates");
        Directory.CreateDirectory(stationDir);
        File.WriteAllText(Path.Combine(stationDir, "user_base.json"),
            "{ \"name\": \"My Base\", \"tier\": \"medium\", \"pack\": \"mybuilds\", \"weight\": 2, " +
            "\"width\": 1, \"height\": 1, \"length\": 1, " +
            "\"cells\": [ { \"x\": 0, \"y\": 0, \"z\": 0, \"kind\": \"block\", \"id\": \"iron_wall\" } ] }");

        try
        {
            var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir(), root);

            // Key defaults to the file name; pack is discovered for the picker; it's reachable by tier+pack.
            var loaded = content.StationTemplates.Single(t => t.Key == "user_base");
            Assert.Equal("mybuilds", loaded.Pack);
            Assert.Equal("station", loaded.Kind);
            Assert.Contains("mybuilds", content.StructurePacks);
            Assert.Equal("user_base", content.PickStationTemplate("medium", new[] { "mybuilds" }, new Random(0))!.Key);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WorldDescription_DefaultsKeepTemplatesOccasional()
    {
        var desc = new WorldDescription();
        Assert.Equal(Frequency.Rare, desc.StationTemplateUse);
        Assert.Equal(Frequency.Rare, desc.SettlementTemplateUse);
        Assert.Empty(desc.EnabledStructurePacks);
        Assert.Equal(0.0, Frequency.Off.Probability());                       // Off ⇒ never rolls a template
        Assert.True(Frequency.Frequent.Probability() > Frequency.Rare.Probability());
    }
}
