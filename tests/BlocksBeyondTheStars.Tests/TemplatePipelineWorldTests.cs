// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The template side of the structure pipeline in a running world (#3518, #3519): on a world of terrain generation 38 a
/// village template's chest fills its crate, a whole template stands as a ruin on a ruined-only world and turns with its
/// markers, a generator entry is drawn, recorded and replayed (and a removed one leaves its blocks), a station template's
/// sound reaches the client and its tokens become blocks. Generation 37 keeps the old behaviour.
/// </summary>
public sealed partial class TemplatePipelineWorldTests : IDisposable
{
    private const int Gen37 = WorldDescription.LandmarkGeneration;
    private const int Gen38 = WorldDescription.StructurePipelineGeneration;
    private const long Seed = 1;

    private static readonly string[] Tiers = { "hamlet", "village", "town", "city" };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_templateworld_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort — a still-open SQLite handle on Windows
        }
    }

    // ---------------------------------------------------------------- marker settings

    [Fact]
    public void AChestWithALootTable_InAVillageTemplate_FillsItsCrate_Once()
    {
        var content = SettlementContent(tier => Whole(tier, "table=test_table"));
        var crate = content.GetBlock("wood_crate")!.NumericId.Value;
        List<string> Chests(SvGameServer s)
        {
            Assert.Contains(s.PlacementRecordsForTest, r => r.Kind == "settlement" && r.Template.StartsWith("test_whole_", StringComparison.Ordinal));
            var chests = s.Containers.Where(c => c.Id.StartsWith("loot_" + SvGameServer.StructureChestLane + "_chest_", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(chests);
            Assert.All(chests, c =>
            {
                Assert.Contains(c.Items, i => i.Item == "gold_ingot"); // the author's table
                Assert.Equal(crate, s.World.GetBlock(c.Position).Value);
            });
            return chests.Select(c => c.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
        }

        var first = Boot("chest", "meadowlands", Gen38, Chests, content);
        Assert.Equal(first, Boot("chest", "meadowlands", Gen38, Chests, content)); // filled once, the same crates on a reload

        // Generation 37: the marker stays what it always was — nothing.
        Boot("chest37", "meadowlands", Gen37, s =>
        {
            Assert.Contains(s.PlacementRecordsForTest, r => r.Kind == "settlement" && r.Template.StartsWith("test_whole_", StringComparison.Ordinal));
            Assert.DoesNotContain(s.Containers, c => c.Id.StartsWith("loot_" + SvGameServer.StructureChestLane, StringComparison.Ordinal));
            return 0;
        }, content);
    }

    // ---------------------------------------------------------------- templates as ruins, turned templates

    [Fact]
    public void AWholeTemplate_StandsAsARuin_OnARuinedOnlyWorld_FromGeneration38()
    {
        var content = SettlementContent(tier => Whole(tier, string.Empty));
        content.GetPlanet("toxic_world")!.SettlementsBias = 40; // a few settlements, so the rule has something to rule

        var fallen = Boot("toxic38", "toxic_world", Gen38, s =>
        {
            var records = s.PlacementRecordsForTest.Where(r => r.Kind == "settlement" && r.Placed).ToList();
            Assert.NotEmpty(records);
            Assert.All(records, r => Assert.StartsWith("test_whole_", r.Template, StringComparison.Ordinal));
            Assert.All(records, r => Assert.True(r.Ruined));
            Assert.All(s.SettlementsForTest, x => Assert.True(x.Ruined));
            return records.Count;
        }, content);
        Assert.True(fallen > 0);

        Boot("toxic37", "toxic_world", Gen37, s =>
        {
            var records = s.PlacementRecordsForTest.Where(r => r.Kind == "settlement" && r.Placed).ToList();
            Assert.NotEmpty(records);
            Assert.DoesNotContain(records, r => r.Template.StartsWith("test_whole_", StringComparison.Ordinal)); // dropped, as before
            return 0;
        }, content);
    }

    [Fact]
    public void ATurnedTemplate_KeepsItsMarkersInPlace_AcrossReloads()
    {
        var content = SettlementContent(tier => Whole(tier, "table=test_table"));
        var first = Boot("turned", "meadowlands", Gen38, TurnLook, content);
        Assert.Contains(first.Pins, p => p.Contains("test_whole_", StringComparison.Ordinal) && p.Contains("turns=", StringComparison.Ordinal));
        var again = Boot("turned", "meadowlands", Gen38, TurnLook, content);
        Assert.Equal(first.Pins, again.Pins);
        Assert.Equal(first.Markers, again.Markers);
    }

    [Fact]
    public void ATurnedLegacyPick_OfSettlementsSwitchedOnAfterTheFirstVisit_RecordsItsTurns_AndReplaysIdentically()
    {
        // The legacy branch: settlements switched on after a first visit (no records, the world no longer virgin) re-derive
        // with the frozen first-fit search and the legacy pool — a whole template there turns like a fresh one (generation 38),
        // so its turns must be recorded too, or the replay stamps its markers unturned.
        var content = SettlementContent(tier =>
        {
            var t = Whole(tier, "table=test_table");
            t.LegacyPool = true;
            return t;
        });
        string location = Boot("legacyturn", "meadowlands", Gen38, s =>
        {
            Assert.DoesNotContain(s.PlacementRecordsForTest, r => r.Kind == "settlement");
            return s.ActiveLocationId;
        }, content, configure: c => c.PlaceSettlements = false);
        using (var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "legacyturn")))
        {
            repo.Initialize();
            repo.SetBlock(location, new Vector3i(0, 250, 0), 1); // a block the first visit left: the world is no longer virgin
            repo.Flush();
        }

        var first = Boot("legacyturn", "meadowlands", Gen38, TurnLook, content);
        Assert.Contains(first.Pins, p => p.Contains(":legacy:", StringComparison.Ordinal)
            && p.Contains("test_whole_", StringComparison.Ordinal) && p.Contains("turns=", StringComparison.Ordinal));
        var again = Boot("legacyturn", "meadowlands", Gen38, TurnLook, content);
        Assert.Equal(first.Pins, again.Pins);
        Assert.Equal(first.Markers, again.Markers);
    }

    /// <summary>The settlement records (index, seat, template, kit layout) and every settlement marker and crate of a world.</summary>
    private static (List<string> Markers, List<string> Pins) TurnLook(SvGameServer s)
    {
        var pins = s.PlacementRecordsForTest.Where(r => r.Kind == "settlement" && r.Placed)
            .OrderBy(r => r.Index).Select(r => $"{r.Index}:{r.Seat}:{r.Template}:{r.KitLayout}").ToList();
        var markers = s.SettlementMarkers.Select(m => $"{m.Type}@{m.Pos.X},{m.Pos.Y},{m.Pos.Z}")
            .Concat(s.Containers.Select(c => $"crate@{c.Position}")).OrderBy(m => m, StringComparer.Ordinal).ToList();
        return (markers, pins);
    }

    // ---------------------------------------------------------------- generator entries

    [Fact]
    public void AGeneratorEntry_IsDrawn_RecordedAndReplayed_AndARemovedOneLeavesItsBlocks()
    {
        var content = SettlementContent(tier => new StructureTemplate { Key = "test_camp_" + tier, Tier = tier, Source = "bandit_camp" });
        Vector3i solid = default;
        ushort solidBlock = 0;
        var first = Boot("entry", "meadowlands", Gen38, s =>
        {
            var records = s.PlacementRecordsForTest.Where(r => r.Kind == "settlement" && r.Placed).ToList();
            Assert.NotEmpty(records);
            Assert.All(records, r => Assert.StartsWith("source:test_camp_", r.Template, StringComparison.Ordinal));
            (solid, solidBlock) = FirstStandingBlock(s);
            return s.SettlementsForTest.OrderBy(x => x.MinX).ThenBy(x => x.MinZ).ToList();
        }, content);
        Assert.NotEmpty(first);

        Assert.Equal(first, Boot("entry", "meadowlands", Gen38, s => s.SettlementsForTest.OrderBy(x => x.MinX).ThenBy(x => x.MinZ).ToList(), content));

        var log = new CapturingLogger();
        Boot("entry", "meadowlands", Gen38, s =>
        {
            Assert.Empty(s.SettlementsForTest); // nothing is re-derived …
            Assert.Equal(solidBlock, s.World.GetBlock(solid).Value); // … and the blocks stay
            return 0;
        }, SettlementContent(_ => null), log);
        Assert.Contains(log.Warnings, w => w.Contains("test_camp_", StringComparison.Ordinal) && w.Contains("no longer in the content", StringComparison.Ordinal));
    }

    [Fact]
    public void ALandmarkGeneratorEntry_IsBuiltByItsSource_PinnedWithItsSource_AndReplayed()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.SetLandmarks(new[] { new StructureTemplate { Key = "test_monument_entry", Source = "monument", Weight = 1 } });
        Assert.Single(content.Landmarks);
        static void Landmarks(ServerConfig c)
        {
            c.PlaceSettlements = false;
            c.PlaceLandmarks = true;
            c.LandmarkChance = 1.0;
        }

        string Look(SvGameServer s)
        {
            var lm = Assert.Single(s.LandmarksForTest);
            Assert.Equal("test_monument_entry", lm.Key);
            var rec = Assert.Single(s.PlacementRecordsForTest, r => r.Kind == LandmarkRules.RecordKind);
            Assert.Contains("src=monument", rec.KitLayout, StringComparison.Ordinal);
            Assert.True(lm.Structure.Width > 1 && lm.Structure.Height > 1); // the monument the source built, not an empty body
            return $"{lm.Origin}:{lm.Structure.Width}x{lm.Structure.Height}x{lm.Structure.Length}:{lm.Structure.Markers.Count}";
        }

        var first = Boot("lmentry", "meadowlands", Gen38, Look, content, configure: Landmarks);
        Assert.Equal(first, Boot("lmentry", "meadowlands", Gen38, Look, content, configure: Landmarks));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The shipped content with only the given whole templates (or generator entries) per tier in the settlement
    /// pool (the modules stay), no settlement kits, and the loot table <c>test_table</c>.</summary>
    private static GameContent SettlementContent(Func<string, StructureTemplate?> make)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var pool = content.SettlementTemplates.Where(t => t.IsModule).ToList();
        pool.AddRange(Tiers.Select(make).OfType<StructureTemplate>());
        content.SetStructureTemplates(content.StationTemplates, pool);
        content.SetStructureKits(content.StructureKits.Where(k => k.KindOrDefault != StructureKit.KindSettlement).ToList());
        AddTestTable(content);
        return content;
    }

    private static void AddTestTable(GameContent content)
    {
        var tables = content.LootTables.Values.ToList();
        tables.Add(new LootTableDefinition
        {
            Key = "test_table",
            Rolls = { new LootRoll { Picks = 2, Entries = { new LootEntry { Item = "gold_ingot", Min = 2, Max = 3 } } } },
        });
        content.SetLootTables(tables);
    }

    /// <summary>A whole template of <paramref name="tier"/>: the shipped river hamlet home with a chest (settings
    /// <paramref name="chest"/>, none when empty).</summary>
    private static StructureTemplate Whole(string tier, string chest)
    {
        var t = TemplatePipelineTests.VillageWithChest(out _, chest);
        if (chest.Length == 0)
        {
            t.Cells.RemoveAll(c => c.Kind == "marker" && c.Id == LandmarkRules.ChestMarker);
        }

        t.Key = "test_whole_" + tier;
        t.Tier = tier;
        return t;
    }

    /// <summary>The highest block cell in the first settlement's box (part of what was stamped there).</summary>
    private static (Vector3i Cell, ushort Block) FirstStandingBlock(SvGameServer s)
    {
        var (min, max, groundY, _) = s.SettlementBoxesForTest[0];
        for (int y = max.Y; y >= groundY; y--)
        {
            for (int x = min.X; x <= max.X; x++)
            {
                for (int z = min.Z; z <= max.Z; z++)
                {
                    var cell = WorldConstants.CanonicalBlock(new Vector3i(x, y, z), s.World.Circumference);
                    ushort b = s.World.GetBlock(cell).Value;
                    if (b != 0)
                    {
                        return (cell, b);
                    }
                }
            }
        }

        throw new InvalidOperationException("the settlement's box holds no block");
    }

    /// <summary>Boots the save <paramref name="name"/> (fresh the first time, a reload afterwards) with settlements only, hands the
    /// running server to <paramref name="look"/>, stops it and returns what it saw.</summary>
    private T Boot<T>(string name, string planet, int generation, Func<SvGameServer, T> look, GameContent content, IGameLogger? logger = null,
        Action<ServerConfig>? configure = null)
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = Seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceRuins = false,
            PlaceWrecks = false,
            PlaceVaults = false,
            PlaceDataCubes = false,
            PlaceChests = false,
            PlaceMonuments = false,
            PlaceFactories = false,
            PlaceBanditCamps = false,
            PlaceLodges = false,
            PlacePioneers = false,
            PlaceLandmarks = false,
            PlaceRailRuins = false,
        };
        config.World.TerrainGeneration = generation;
        config.World.SettlementTemplateUse = Frequency.Frequent;
        configure?.Invoke(config);
        var server = new SvGameServer(config, content, new LoopbackServerTransport(new LoopbackLink()), repo, logger);
        server.Start();
        try
        {
            return look(server);
        }
        finally
        {
            server.Stop();
        }
    }
}
