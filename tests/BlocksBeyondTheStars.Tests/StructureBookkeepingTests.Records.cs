// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#3513, part two: data cubes, counts, the wreck's design, the procedural marker, ruins and the persistence of the
/// new fields.</summary>
public sealed partial class StructureBookkeepingTests
{
    // ---------------------------------------------------------------- data cubes

    /// <summary>On this desert city world a rolled cube spot lies inside the city, so the fresh world nudges the cube off it —
    /// and the second load, no longer fresh, used to drop it. The nudged spot is recorded and replayed.</summary>
    [Fact]
    public void ANudgedDataCube_IsStillThere_AfterAReload()
    {
        const string name = "cubes";
        static void Cubes(ServerConfig c)
        {
            c.PlaceRuins = false;
            c.PlaceRailRuins = false;
            c.PlaceChests = false;
            c.PlaceWrecks = false;
            c.PlaceVaults = false;
            c.PlaceMonuments = false;
            c.PlaceFactories = false;
            c.PlaceLodges = false;
            c.PlacePioneers = false;
            c.PlaceLandmarks = false;
            c.Rules.Bandits = AlienActivity.Extreme;
        }

        static (List<string> Seats, List<(float, float, float)> Cubes) Look(SvGameServer s)
            => (s.PlacementRecordsForTest.Where(r => r.Kind == SvGameServer.DataCubeRecordKind).OrderBy(r => r.Index)
                    .Select(r => r.Placed ? r.Seat : "skip").ToList(),
                s.DataCubeSnapshots.Select(c => (c.Pos.X, c.Pos.Y, c.Pos.Z)).OrderBy(p => p).ToList());

        var first = Boot(name, "gds_desert", 16, Gen37, Look, Cubes);
        Assert.Contains("nudged", first.Seats); // the premise: one cube was nudged off the city

        var again = Boot(name, "gds_desert", 16, Gen37, Look, Cubes);
        Assert.Equal(first.Seats, again.Seats);
        Assert.Equal(first.Cubes, again.Cubes);
    }

    // ---------------------------------------------------------------- counts

    /// <summary>The camp count reads the bandit rule (and the danger option): a body stamped with bandits at "Extreme" lost its
    /// camp when the host turned them down to "Rare". The count is pinned at the first run; a fresh save of the same seed at
    /// "Rare" shows the roll would have said otherwise.</summary>
    [Fact]
    public void ChangingTheBanditLevel_AfterTheFirstLoad_KeepsTheCampCount()
    {
        static Action<ServerConfig> Bandits(AlienActivity level) => c =>
        {
            c.PlaceSettlements = false;
            c.PlaceRuins = false;
            c.PlaceRailRuins = false;
            c.PlaceChests = false;
            c.PlaceWrecks = false;
            c.PlaceVaults = false;
            c.PlaceDataCubes = false;
            c.PlaceMonuments = false;
            c.PlaceFactories = false;
            c.PlaceLodges = false;
            c.PlacePioneers = false;
            c.PlaceLandmarks = false;
            c.Rules.Bandits = level;
        };

        static (int?, int) Camps(SvGameServer s) => (s.StructureCountForTest("banditcamp"), s.BanditCampsForTest().Count);

        var extreme = Boot("camps", "meadowlands", BanditSeed, Gen37, Camps, Bandits(AlienActivity.Extreme));
        Assert.True(extreme.Item2 > 0, "the premise: the body has a camp at Extreme");
        Assert.Equal(extreme.Item1, extreme.Item2);

        var rareFresh = Boot("camps_rare", "meadowlands", BanditSeed, Gen37, Camps, Bandits(AlienActivity.Rare));
        Assert.True(rareFresh.Item2 < extreme.Item2, "the premise: at Rare the roll gives fewer camps");

        // The rules a world was created with are its own (RulesOverride) — the host's later edit is what changes them.
        EditMetadata("camps", meta => meta.RulesOverride!.Bandits = AlienActivity.Rare);
        var reloaded = Boot("camps", "meadowlands", BanditSeed, Gen37, Camps, Bandits(AlienActivity.Rare));
        Assert.Equal(extreme, reloaded);
    }

    private const long BanditSeed = 4;

    // ---------------------------------------------------------------- the wreck's design

    /// <summary>The planet wreck drew its design from the ship list in dictionary order; the record now names the design and the
    /// replay takes it — a record that names another ship builds that one, a record from before #3513 gains the name.</summary>
    [Fact]
    public void TheWrecksDesign_IsReadFromItsRecord()
    {
        const string name = "wreck";
        static void WrecksOnly(ServerConfig c)
        {
            c.PlaceSettlements = false;
            c.PlaceRuins = false;
            c.PlaceRailRuins = false;
            c.PlaceChests = false;
            c.PlaceVaults = false;
            c.PlaceDataCubes = false;
            c.PlaceMonuments = false;
            c.PlaceFactories = false;
            c.PlaceBanditCamps = false;
            c.PlaceLodges = false;
            c.PlacePioneers = false;
            c.PlaceLandmarks = false;
        }

        static (string? Built, string? Recorded) Look(SvGameServer s)
            => (s.WreckDesignForTest, s.PlacementRecordsForTest.FirstOrDefault(r => r.Kind == "wreck")?.Design);

        var first = Boot(name, "meadowlands", WreckSeed, Gen37, Look, WrecksOnly);
        Assert.NotNull(first.Built); // the premise: this body has a wreck
        Assert.Equal(first.Built, first.Recorded);
        Assert.Equal(string.Empty, Boot(name, "meadowlands", WreckSeed, Gen37, s => s.PlacementRecordsForTest.First(r => r.Kind == "wreck").Template, WrecksOnly));

        string other = Content.Ships.Keys.First(k => k != first.Built);
        EditMetadata(name, meta => meta.Placements.First(r => r.Kind == "wreck").Design = other);
        Assert.Equal((other, other), Boot(name, "meadowlands", WreckSeed, Gen37, Look, WrecksOnly));

        EditMetadata(name, meta => meta.Placements.First(r => r.Kind == "wreck").Design = null); // a record from before #3513
        Assert.Equal(first, Boot(name, "meadowlands", WreckSeed, Gen37, Look, WrecksOnly));
    }

    private const long WreckSeed = 2;

    // ---------------------------------------------------------------- the procedural marker

    /// <summary>A settlement composed procedurally was recorded with an empty template, which also means "recorded before
    /// template pinning" and replays through the legacy pool — so with templates in play a procedural village could come back
    /// as a template house. On a generation-38 world it records <see cref="StructurePlacementRecord.ProceduralTemplate"/> and
    /// replays as procedural; the same records with the empty template do not.</summary>
    [Fact]
    public void AGeneration38ProceduralSettlement_ReplaysAsProcedural()
    {
        const string name = "procedural";
        int gen = WorldDescription.StructurePipelineGeneration;
        static void Off(ServerConfig c)
        {
            c.World.SettlementTemplateUse = Frequency.Off; // nothing but procedural settlements
            c.PlaceRuins = false;
            c.PlaceRailRuins = false;
            c.PlaceChests = false;
            c.PlaceWrecks = false;
            c.PlaceVaults = false;
            c.PlaceDataCubes = false;
            c.PlaceMonuments = false;
            c.PlaceFactories = false;
            c.PlaceBanditCamps = false;
            c.PlaceLodges = false;
            c.PlacePioneers = false;
            c.PlaceLandmarks = false;
        }

        var first = Boot(name, "meadowlands", ProceduralSeed, gen, s =>
        {
            var recs = s.PlacementRecordsForTest.Where(r => r.Kind == "settlement" && r.Placed).ToList();
            Assert.NotEmpty(recs);
            Assert.All(recs, r => Assert.Equal((StructurePlacementRecord.ProceduralTemplate, 1, ""), (r.Template, r.Modules, r.Kit)));
            return s.SettlementsForTest;
        }, Off);

        // Templates come into play for this world (the option is part of the save's description).
        EditMetadata(name, meta => meta.Description.SettlementTemplateUse = Frequency.Frequent);
        Assert.Equal(first, Boot(name, "meadowlands", ProceduralSeed, gen, s => s.SettlementsForTest, Off));

        EditMetadata(name, meta =>
        {
            foreach (var r in meta.Placements.Where(r => r.Kind == "settlement"))
            {
                r.Template = string.Empty; // the ambiguous pin from before the marker
            }
        });
        Assert.NotEqual(first, Boot(name, "meadowlands", ProceduralSeed, gen, s => s.SettlementsForTest, Off)); // the premise
    }

    /// <summary>A meadow world with eight settlements — enough that the legacy pool hands at least one of them a template.</summary>
    private const long ProceduralSeed = 1;

    // ---------------------------------------------------------------- standalone ruins

    /// <summary>Standalone ruins had no record, so the footprints the stampers after them keep clear of existed only on the
    /// load that stamped them. Every ruin now has a record, and every later load restores the footprints from it.</summary>
    [Fact]
    public void RuinFootprints_ArePresentAfterAReload()
    {
        const string name = "ruins";
        static void RuinsOnly(ServerConfig c)
        {
            c.PlaceSettlements = false;
            c.PlaceRailRuins = false;
            c.PlaceChests = false;
            c.PlaceWrecks = false;
            c.PlaceVaults = false;
            c.PlaceDataCubes = false;
            c.PlaceMonuments = false;
            c.PlaceFactories = false;
            c.PlaceBanditCamps = false;
            c.PlaceLodges = false;
            c.PlacePioneers = false;
            c.PlaceLandmarks = false;
        }

        var first = Boot(name, RuinPlanet, RuinSeed, Gen37, s =>
        {
            var recs = s.PlacementRecordsForTest.Where(r => r.Kind == SvGameServer.RuinRecordKind && r.Placed).ToList();
            Assert.NotEmpty(recs); // the premise: the body has a ruin
            Assert.All(recs, r => Assert.True(r.Width > 0 && r.Length > 0 && r.Ruined == true && r.Tier is "town" or "city"));
            Assert.Equal(recs.Count, s.RuinFootprintsForTest.Count);
            return s.RuinFootprintsForTest;
        }, RuinsOnly);

        Assert.Equal(first, Boot(name, RuinPlanet, RuinSeed, Gen37, s => s.RuinFootprintsForTest, RuinsOnly));
    }

    private const string RuinPlanet = "scrapyard";

    private const long RuinSeed = 1;

    // ---------------------------------------------------------------- persistence

    /// <summary>The new fields ride the metadata JSON: they round-trip through the SQLite and the in-memory repository, and a
    /// field that was never written reads back as "not recorded".</summary>
    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public void TheNewRecordFields_RoundTripThroughTheRepository(string kind)
    {
        var meta = new WorldMetadata { WorldName = "roundtrip", Seed = 3 };
        meta.StructureCounts["sys0-p0|settlement"] = 4;
        meta.StructureCounts["sys0-p0|chest"] = 0;
        meta.Placements.Add(new StructurePlacementRecord
        {
            LocationId = "sys0-p0",
            Kind = "settlement",
            Index = 2,
            Placed = true,
            Template = StructurePlacementRecord.ProceduralTemplate,
            Tier = "city",
            Ruined = true,
        });
        meta.Placements.Add(new StructurePlacementRecord { LocationId = "sys0-p0", Kind = "wreck", Placed = true, Design = "hammerhead" });
        meta.Placements.Add(new StructurePlacementRecord { LocationId = "sys0-p0", Kind = "ruin", Placed = true, Width = 31, Length = 27 });
        meta.Placements.Add(new StructurePlacementRecord { LocationId = "sys0-p0", Kind = "vault", Placed = true }); // nothing new written

        var paths = new SaveGamePaths(_root, "roundtrip_" + kind);
        IWorldRepository repo = kind == "sqlite" ? new SqliteWorldRepository(paths) : new MemoryWorldRepository(paths);
        WorldMetadata loaded;
        using (repo)
        {
            repo.Initialize();
            repo.SaveMetadata(meta);
            loaded = repo.LoadMetadata()!;
        }

        Assert.Equal(meta.StructureCounts, loaded.StructureCounts);
        var settlement = loaded.Placements[0];
        Assert.Equal((StructurePlacementRecord.ProceduralTemplate, "city", (bool?)true), (settlement.Template, settlement.Tier, settlement.Ruined));
        Assert.Equal("hammerhead", loaded.Placements[1].Design);
        Assert.Equal((31, 27), (loaded.Placements[2].Width, loaded.Placements[2].Length));
        var vault = loaded.Placements[3];
        Assert.Equal((null, null, null, 0, 0), (vault.Tier, vault.Ruined, vault.Design, vault.Width, vault.Length));
    }

    /// <summary>A save written before #3513 has neither the counts nor the new fields: it loads with them empty.</summary>
    [Fact]
    public void AMetadataBlobFromBeforeTheFields_LoadsWithThemEmpty()
    {
        const string json = "{\"WorldName\":\"old\",\"Seed\":5,\"Placements\":[{\"LocationId\":\"a\",\"Kind\":\"settlement\",\"Index\":0,"
                            + "\"Placed\":true,\"Template\":\"\"}]}";
        var meta = System.Text.Json.JsonSerializer.Deserialize<WorldMetadata>(json)!;
        Assert.Empty(meta.StructureCounts);
        var rec = Assert.Single(meta.Placements);
        Assert.Equal((null, null, null, 0, 0), (rec.Tier, rec.Ruined, rec.Design, rec.Width, rec.Length));
    }
}
