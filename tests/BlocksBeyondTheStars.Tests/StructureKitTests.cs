// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#1873: the kit contract — loading, validation, filters, pin-only templates, the JSON fields.</summary>
public sealed class StructureKitTests
{
    private static GameContent Content() => ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static StructureTemplate Module(string key, string kind, string kit, string function, string tier = "small")
    {
        var t = new StructureTemplate { Key = key, Name = key, Kind = kind, Kit = kit, Function = function, Tier = tier, Width = 3, Height = 3, Length = 3 };
        t.Cells.Add(new TemplateCell { X = 0, Y = 0, Z = 0, Kind = "block", Id = "iron_wall" });
        return t;
    }

    private static GameContent WithStationModules(params StructureTemplate[] modules)
    {
        var c = Content();
        var stations = new List<StructureTemplate>(c.StationTemplates);
        stations.AddRange(modules);
        c.SetStructureTemplates(stations, c.SettlementTemplates);
        return c;
    }

    [Fact]
    public void TemplateJson_CarriesKitFunctionPortAndPinOnly()
    {
        const string json = "{ \"key\": \"m\", \"kit\": \"station_small_1\", \"function\": \"canteen\", \"pinOnly\": true, " +
                            "\"width\": 1, \"height\": 1, \"length\": 1, \"cells\": [ { \"x\": 0, \"y\": 0, \"z\": 0, \"kind\": \"block\", \"id\": \"iron_wall\", \"port\": \"door:energy\" } ] }";
        var t = JsonSerializer.Deserialize<StructureTemplate>(json, ContentLoader.JsonOptions)!;
        Assert.Equal("station_small_1", t.Kit);
        Assert.Equal("canteen", t.Function);
        Assert.Equal("canteen", t.FunctionOrRole);
        Assert.True(t.PinOnly);
        Assert.True(t.IsModule);
        Assert.Equal("door:energy", t.Cells[0].Port);

        // A legacy module (role only) keeps its role as the function; a plain template is neither.
        Assert.Equal("house", new StructureTemplate { Role = "house" }.FunctionOrRole);
        Assert.False(new StructureTemplate().IsModule);
    }

    [Fact]
    public void Kits_ValidateAgainstTheTemplatePools_AndKeepPoolOrder()
    {
        var c = WithStationModules(Module("hub_a", "station", "k1", "hub"), Module("cab_a", "station", "k1", "cabins"));
        var warnings = new List<string>();
        c.SetStructureKits(new[]
        {
            new StructureKit
            {
                Key = "k1", Kind = "station", Tier = "small", Start = "hub_a",
                Entries = { new KitEntry { Module = "hub_a", Required = true }, new KitEntry { Module = "cab_a", Min = 1, Max = 3 }, new KitEntry { Module = "nope" } },
            },
            new StructureKit { Key = "k2", Kind = "spaceship", Entries = { new KitEntry { Module = "hub_a" } } },
            new StructureKit { Key = "", Kind = "station", Entries = { new KitEntry { Module = "hub_a" } } },
            new StructureKit { Key = "k3", Kind = "station", Entries = { new KitEntry { Module = "nope" } } },
            new StructureKit { Key = "k4", Kind = "station", Start = "ghost", Entries = { new KitEntry { Module = "cab_a" } } },
            new StructureKit { Key = "k1", Kind = "station", Entries = { new KitEntry { Module = "cab_a" } } },
        }, warnings.Add);

        Assert.Equal(new[] { "k1", "k4" }, c.StructureKits.Select(k => k.Key));
        var k1 = c.KitByKey("k1")!;
        Assert.Equal(new[] { "hub_a", "cab_a" }, k1.Entries.Select(e => e.Module));
        Assert.Equal(1, k1.Entries[0].Min); // required places at least one
        Assert.Equal(string.Empty, c.KitByKey("k4")!.Start); // a ghost start is cleared
        Assert.Contains(warnings, w => w.Contains("'nope'"));
        Assert.Contains(warnings, w => w.Contains("spaceship"));
        Assert.Contains(warnings, w => w.Contains("without a key"));
        Assert.Contains(warnings, w => w.Contains("no usable entry"));
        Assert.Contains(warnings, w => w.Contains("'ghost'"));
        Assert.Contains(warnings, w => w.Contains("already loaded"));
        Assert.Equal((2, 4), k1.EffectiveBounds()); // required 1 + min 1 … capacity 1 + 3
        k1.ModulesMin = 3;
        k1.ModulesMax = 9;
        Assert.Equal((3, 4), k1.EffectiveBounds()); // the fields win inside what the entries allow
    }

    [Fact]
    public void KitsFor_FiltersKindTierPackAndPlanet_AndCompleteTemplatesSkipPinOnly()
    {
        var c = WithStationModules(Module("hub_a", "station", "k1", "hub"));
        var settlements = new List<StructureTemplate>(c.SettlementTemplates) { Module("house_a", "settlement", "v1", "house", "village") };
        c.SetStructureTemplates(c.StationTemplates, settlements);
        c.SetStructureKits(new[]
        {
            new StructureKit { Key = "st_small", Kind = "station", Tier = "small", Entries = { new KitEntry { Module = "hub_a" } } },
            new StructureKit { Key = "st_large", Kind = "station", Tier = "large", Pack = "extra", Entries = { new KitEntry { Module = "hub_a" } } },
            new StructureKit { Key = "v_ice", Kind = "settlement", Tier = "village", PlanetTypes = { "tundra" }, Entries = { new KitEntry { Module = "house_a" } } },
            new StructureKit { Key = "city_1", Kind = "city", Tier = "metropolis", Entries = { new KitEntry { Module = "house_a" } } },
        });

        Assert.Equal(new[] { "st_small" }, c.KitsFor("station", "small", null, null).Select(k => k.Key));
        Assert.Empty(c.KitsFor("station", "large", new[] { "default" }, null));
        Assert.Equal(new[] { "st_large" }, c.KitsFor("station", "large", new[] { "extra" }, null).Select(k => k.Key));
        Assert.Equal(new[] { "v_ice" }, c.KitsFor("settlement", "village", null, "tundra").Select(k => k.Key));
        Assert.Empty(c.KitsFor("settlement", "village", null, "desert"));
        Assert.Equal(new[] { "city_1" }, c.KitsFor("city", null, null, "gds_desert").Select(k => k.Key));
        Assert.Contains("extra", c.StructurePacks);

        // Pin-only: never in the joint table, never rolled for a new station — but a legacy replay still sees it.
        var pinned = new StructureTemplate { Key = "old_box", Kind = "station", Tier = "huge", PinOnly = true, LegacyPool = true, Width = 1, Height = 1, Length = 1 };
        pinned.Cells.Add(new TemplateCell { Kind = "block", Id = "iron_wall" });
        var stations = new List<StructureTemplate>(c.StationTemplates) { pinned };
        c.SetStructureTemplates(stations, c.SettlementTemplates);
        Assert.Empty(c.CompleteTemplatesFor("station", "huge", null, null));
        Assert.Null(c.PickStationTemplate("huge", null, new Random(1)));
        Assert.Equal("old_box", c.PickStationTemplate("huge", null, new Random(1), legacyOnly: true)!.Key);
        Assert.Same(pinned, c.TemplateByKey("station", "old_box"));
    }

    [Fact]
    public void UserContentFolder_LoadsKits_KeyFromTheFileName_UnreadableFileWarned()
    {
        var root = Path.Combine(Path.GetTempPath(), "bbts_kits_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "station_templates"));
        Directory.CreateDirectory(Path.Combine(root, "structure_kits"));
        File.WriteAllText(Path.Combine(root, "station_templates", "my_hub.json"),
            "{ \"kit\": \"my_kit\", \"function\": \"hub\", \"tier\": \"small\", \"width\": 1, \"height\": 1, \"length\": 1, " +
            "\"cells\": [ { \"x\": 0, \"y\": 0, \"z\": 0, \"kind\": \"block\", \"id\": \"iron_wall\" } ] }");
        File.WriteAllText(Path.Combine(root, "structure_kits", "my_kit.json"),
            "{ \"name\": \"My Kit\", \"kind\": \"station\", \"tier\": \"small\", \"pack\": \"mine\", \"modulesMin\": 1, \"modulesMax\": 2, " +
            "\"entries\": [ { \"module\": \"my_hub\", \"required\": true }, { \"module\": \"missing\" } ] }");
        File.WriteAllText(Path.Combine(root, "structure_kits", "broken.json"), "{ \"entries\": [ {");

        try
        {
            var warnings = new List<string>();
            var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir(), root, warnings.Add);
            var kit = Assert.Single(content.StructureKits, k => k.Key == "my_kit");
            Assert.Equal("My Kit", kit.Name);
            Assert.Equal(new[] { "my_hub" }, kit.Entries.Select(e => e.Module));
            Assert.True(kit.Entries[0].Required);
            Assert.Contains("mine", content.StructurePacks);
            Assert.True(content.TemplateByKey("station", "my_hub")!.IsModule);
            Assert.DoesNotContain(content.StationTemplates.Where(t => t.Key == "my_hub"), t => !t.IsModule);
            Assert.Contains(warnings, w => w.Contains("broken.json"));
            Assert.Contains(warnings, w => w.Contains("'missing'"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
