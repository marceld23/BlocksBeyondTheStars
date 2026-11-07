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

/// <summary>#3333: the "underground" flag on structure kits and whole templates — the JSON field, the surface pools that
/// never offer an underground kit / template / module, the accessors the underground stamper reads, and the station rule
/// (a station hangs in space: the flag is warned about and dropped).</summary>
public sealed class UndergroundStructureFlagTests
{
    private static StructureTemplate Whole(string key, string tier, bool underground = false, params string[] planetTypes)
    {
        var t = new StructureTemplate { Key = key, Name = key, Tier = tier, Underground = underground, Width = 1, Height = 1, Length = 1 };
        t.PlanetTypes.AddRange(planetTypes);
        t.Cells.Add(new TemplateCell { Kind = "block", Id = "iron_wall" });
        return t;
    }

    private static StructureTemplate Module(string key, string role, string kit, bool underground = false, string tier = "village")
    {
        var t = new StructureTemplate { Key = key, Name = key, Tier = tier, Role = role, Kit = kit, Function = role, Underground = underground, Width = 6, Height = 7, Length = 6 };
        t.Cells.Add(new TemplateCell { Kind = "block", Id = "iron_wall" });
        return t;
    }

    private static StructureCatalog Catalog(IReadOnlyList<StructureTemplate> stations, IReadOnlyList<StructureTemplate> settlements,
        IReadOnlyList<StructureKit>? kits = null, List<string>? warnings = null)
    {
        var c = new StructureCatalog();
        c.SetStructureTemplates(stations, settlements, warnings is null ? null : warnings.Add);
        c.SetStructureKits(kits ?? Array.Empty<StructureKit>(), warnings is null ? null : warnings.Add);
        return c;
    }

    [Fact]
    public void Json_CarriesTheUndergroundFlag_OnKitsAndTemplates_DefaultFalse()
    {
        var kit = JsonSerializer.Deserialize<StructureKit>("{ \"key\": \"ruin\", \"kind\": \"city\", \"underground\": true }", ContentLoader.JsonOptions)!;
        Assert.True(kit.Underground);
        var kitAgain = JsonSerializer.Deserialize<StructureKit>(JsonSerializer.Serialize(kit, ContentLoader.JsonOptions), ContentLoader.JsonOptions)!;
        Assert.True(kitAgain.Underground);
        Assert.False(JsonSerializer.Deserialize<StructureKit>("{ \"key\": \"plain\" }", ContentLoader.JsonOptions)!.Underground);

        const string templateJson = "{ \"key\": \"crypt\", \"tier\": \"village\", \"underground\": true, \"width\": 1, \"height\": 1, \"length\": 1, " +
                                    "\"cells\": [ { \"x\": 0, \"y\": 0, \"z\": 0, \"kind\": \"block\", \"id\": \"iron_wall\" } ] }";
        var template = JsonSerializer.Deserialize<StructureTemplate>(templateJson, ContentLoader.JsonOptions)!;
        Assert.True(template.Underground);
        var templateAgain = JsonSerializer.Deserialize<StructureTemplate>(JsonSerializer.Serialize(template, ContentLoader.JsonOptions), ContentLoader.JsonOptions)!;
        Assert.True(templateAgain.Underground);
        Assert.False(JsonSerializer.Deserialize<StructureTemplate>("{ \"key\": \"hut\" }", ContentLoader.JsonOptions)!.Underground);
    }

    [Fact]
    public void SurfacePools_NeverOfferAnUndergroundWholeTemplate_TheUndergroundAccessorDoes()
    {
        var c = Catalog(Array.Empty<StructureTemplate>(), new[]
        {
            Whole("hut", "village"),
            Whole("crypt", "village", underground: true, "gds_desert"),
            Whole("vault", "town", underground: true),
        });

        Assert.Equal(new[] { "hut" }, c.CompleteTemplatesFor(StructureKit.KindSettlement, "village", null, null).Select(t => t.Key));
        Assert.Empty(c.CompleteTemplatesFor(StructureKit.KindSettlement, "town", null, null));
        for (int seed = 0; seed < 64; seed++)
        {
            Assert.Equal("hut", c.PickSettlementTemplate("village", null, new Random(seed), "gds_desert")!.Key);
        }

        Assert.Null(c.PickSettlementTemplate("town", null, new Random(1))); // the tier has only an underground template

        // The underground stamper's view: every tier, the planet filter applied, pool order.
        Assert.Equal(new[] { "crypt", "vault" }, c.UndergroundTemplatesFor(null, "gds_desert").Select(t => t.Key));
        Assert.Equal(new[] { "vault" }, c.UndergroundTemplatesFor(null, "tundra").Select(t => t.Key));
        Assert.Empty(c.UndergroundTemplatesFor(new[] { "other_pack" }, null));

        // A pinned world still finds it by key.
        Assert.Equal("crypt", c.SettlementTemplateByKey("crypt")!.Key);
    }

    [Fact]
    public void LegacyReplay_StillSeesAFlaggedLegacyTemplate_ItReproducesWhatAlreadyStands()
    {
        var legacy = Whole("old_hamlet", "hamlet", underground: true);
        legacy.LegacyPool = true;
        var c = Catalog(Array.Empty<StructureTemplate>(), new[] { legacy });

        Assert.Null(c.PickSettlementTemplate("hamlet", null, new Random(3)));
        Assert.Equal("old_hamlet", c.PickSettlementTemplate("hamlet", null, new Random(3), legacyOnly: true)!.Key);
    }

    [Fact]
    public void KitsFor_SkipsUndergroundKits_UndergroundKitsForReturnsThemFiltered()
    {
        var settlements = new[] { Module("house_a", "house", "v1"), Module("hall_a", "city_hall", "c1", tier: StructureRoles.MetropolisTier) };
        var c = Catalog(Array.Empty<StructureTemplate>(), settlements, new[]
        {
            new StructureKit { Key = "c1", Kind = "city", Tier = "metropolis", Entries = { new KitEntry { Module = "hall_a" } } },
            new StructureKit { Key = "ruin_desert", Kind = "city", Tier = "metropolis", Underground = true, PlanetTypes = { "gds_desert" }, Entries = { new KitEntry { Module = "hall_a" } } },
            new StructureKit { Key = "v1", Kind = "settlement", Tier = "village", Entries = { new KitEntry { Module = "house_a" } } },
            new StructureKit { Key = "crypt_village", Kind = "settlement", Tier = "village", Underground = true, Entries = { new KitEntry { Module = "house_a" } } },
            new StructureKit { Key = "ruin_pinned", Kind = "city", Underground = true, PinOnly = true, Entries = { new KitEntry { Module = "hall_a" } } },
            new StructureKit { Key = "ruin_packed", Kind = "city", Underground = true, Pack = "ruins", Entries = { new KitEntry { Module = "hall_a" } } },
        });

        Assert.Equal(new[] { "c1" }, c.KitsFor(StructureKit.KindCity, null, null, "gds_desert").Select(k => k.Key));
        Assert.Equal(new[] { "v1" }, c.KitsFor(StructureKit.KindSettlement, "village", null, null).Select(k => k.Key));

        Assert.Equal(new[] { "ruin_desert", "crypt_village", "ruin_packed" }, c.UndergroundKitsFor(null, "gds_desert").Select(k => k.Key));
        Assert.Equal(new[] { "crypt_village", "ruin_packed" }, c.UndergroundKitsFor(null, "tundra").Select(k => k.Key));
        Assert.Equal(new[] { "ruin_desert", "crypt_village" }, c.UndergroundKitsFor(new[] { "default" }, "gds_desert").Select(k => k.Key));
        Assert.True(StructureCatalog.IsUndergroundKit(c.KitByKey("ruin_desert")!));
        Assert.False(StructureCatalog.IsUndergroundKit(c.KitByKey("c1")!));
    }

    [Fact]
    public void AStationKitWithTheFlag_IsWarnedAbout_AndStaysASurfaceKit()
    {
        var hub = new StructureTemplate { Key = "hub_a", Kind = "station", Kit = "st_crypt", Function = "hub", Tier = "small", Width = 3, Height = 3, Length = 3 };
        hub.Cells.Add(new TemplateCell { Kind = "block", Id = "iron_wall" });
        var warnings = new List<string>();
        var c = Catalog(new[] { hub }, Array.Empty<StructureTemplate>(), new[]
        {
            new StructureKit { Key = "st_crypt", Kind = "station", Tier = "small", Underground = true, Entries = { new KitEntry { Module = "hub_a", Required = true } } },
        }, warnings);

        Assert.Contains(warnings, w => w.Contains("'st_crypt'") && w.Contains("underground"));
        Assert.False(c.KitByKey("st_crypt")!.Underground);
        Assert.Equal(new[] { "st_crypt" }, c.KitsFor(StructureKit.KindStation, "small", null, null).Select(k => k.Key));
        Assert.Empty(c.UndergroundKitsFor(null, null));
    }

    [Fact]
    public void AStationTemplateWithTheFlag_IsWarnedAbout_AndStaysASurfaceTemplate()
    {
        var warnings = new List<string>();
        var c = Catalog(new[] { Whole("station_crypt", "small", underground: true) }, Array.Empty<StructureTemplate>(), warnings: warnings);

        Assert.Contains(warnings, w => w.Contains("'station_crypt'") && w.Contains("underground"));
        Assert.False(c.StationTemplates[0].Underground);
        Assert.Equal(new[] { "station_crypt" }, c.CompleteTemplatesFor(StructureKit.KindStation, "small", null, null).Select(t => t.Key));
        Assert.Equal("station_crypt", c.PickStationTemplate("small", null, new Random(1))!.Key);
    }

    [Fact]
    public void ModulePool_SkipsModulesOfAnUndergroundKit_AndFlaggedModules_ButKeepsABorrowedSurfaceModule()
    {
        var settlements = new[]
        {
            Module("house_a", "house", "v1"),
            Module("ruin_hall", "city_hall", "ruin_k", tier: StructureRoles.MetropolisTier),
            Module("crypt_house", "house", string.Empty, underground: true),
        };
        var c = Catalog(Array.Empty<StructureTemplate>(), settlements, new[]
        {
            new StructureKit { Key = "v1", Kind = "settlement", Tier = "village", Entries = { new KitEntry { Module = "house_a" } } },
            // The underground kit borrows the surface house: the house stays a surface module.
            new StructureKit { Key = "ruin_k", Kind = "city", Underground = true, Entries = { new KitEntry { Module = "ruin_hall" }, new KitEntry { Module = "house_a" } } },
        });

        Assert.Equal(new[] { "house_a" }, c.SettlementModulesFor(null, null).Select(t => t.Key));
        Assert.True(c.IsUndergroundModule(settlements[1]));
        Assert.True(c.IsUndergroundModule(settlements[2]));
        Assert.False(c.IsUndergroundModule(settlements[0]));

        // The underground kit still resolves its own module by key.
        Assert.Equal(new[] { "ruin_hall", "house_a" }, c.KitByKey("ruin_k")!.Entries.Select(e => e.Module));
        Assert.Same(settlements[1], c.TemplateByKey(StructureKit.KindCity, "ruin_hall"));
    }

    [Fact]
    public void UserContentFiles_CarryTheFlag_AndAStationTemplateFileIsWarned()
    {
        var root = Path.Combine(Path.GetTempPath(), "bbts_underground_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "settlement_templates"));
        Directory.CreateDirectory(Path.Combine(root, "station_templates"));
        Directory.CreateDirectory(Path.Combine(root, "structure_kits"));
        const string cell = "\"width\": 1, \"height\": 1, \"length\": 1, \"cells\": [ { \"x\": 0, \"y\": 0, \"z\": 0, \"kind\": \"block\", \"id\": \"iron_wall\" } ]";
        File.WriteAllText(Path.Combine(root, "settlement_templates", "my_crypt.json"),
            "{ \"name\": \"My Crypt\", \"tier\": \"village\", \"pack\": \"mine\", \"underground\": true, " + cell + " }");
        File.WriteAllText(Path.Combine(root, "station_templates", "my_station.json"),
            "{ \"name\": \"My Station\", \"tier\": \"small\", \"pack\": \"mine\", \"underground\": true, " + cell + " }");
        File.WriteAllText(Path.Combine(root, "structure_kits", "my_ruin.json"),
            "{ \"name\": \"My Ruin\", \"kind\": \"city\", \"tier\": \"metropolis\", \"pack\": \"mine\", \"underground\": true, " +
            "\"entries\": [ { \"module\": \"gds_hall_1\" } ] }");

        try
        {
            var warnings = new List<string>();
            var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir(), root, warnings.Add);
            var mine = new[] { "mine" };

            Assert.True(content.SettlementTemplateByKey("my_crypt")!.Underground);
            Assert.Equal(new[] { "my_crypt" }, content.UndergroundTemplatesFor(mine, null).Select(t => t.Key));
            Assert.Empty(content.CompleteTemplatesFor(StructureKit.KindSettlement, "village", mine, null));

            Assert.Contains(warnings, w => w.Contains("'my_station'") && w.Contains("underground"));
            Assert.False(content.StationTemplateByKey("my_station")!.Underground);

            Assert.True(content.KitByKey("my_ruin")!.Underground);
            Assert.Equal(new[] { "my_ruin" }, content.UndergroundKitsFor(mine, "gds_desert").Select(k => k.Key));
            Assert.Empty(content.KitsFor(StructureKit.KindCity, null, mine, "gds_desert"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
