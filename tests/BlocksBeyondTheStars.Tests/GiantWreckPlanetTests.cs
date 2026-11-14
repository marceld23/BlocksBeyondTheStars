// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Tests.Helpers;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3610: the giant ship wrecks on planets — stamped with the landmark machinery from terrain generation 42 as ONE structure
/// overlay (no block edit per cell), pinned by a placement record (fingerprint, ruin revision 3, seed, turn, air), chests filled
/// once, replayed from the record on every load, never on a gas giant, the finale body or an older world. The pool here is the
/// shipped sample hull made rollable (the shipped file is pin-only), so no assertion depends on the real ships or on the
/// exact look of the ruin pass — only on what the stamp must do with whatever the generator kept.
/// </summary>
public sealed class GiantWreckPlanetTests : IDisposable
{
    /// <summary>The sample's overlay must stay well under this (a landmark's budget is a megabyte; the hull is tiny).</summary>
    private const int OverlayBudgetBytes = 100 * 1024;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_giantwreck_" + Guid.NewGuid().ToString("N"));

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

    private SvGameServer Start(GameContent content, string name, out SqliteWorldRepository repo, string planet = "meadowlands",
        int generation = WorldDescription.CurrentTerrainGeneration, double chance = 1.0, Action<ServerConfig>? tweak = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = GiantWreckTestWorlds.Config(name, planet, generation, chance);
        tweak?.Invoke(config);
        var server = new SvGameServer(config, content, new RecordingTransport(), repo);
        server.Start();
        return server;
    }

    [Fact]
    public void AGeneration42Body_CarriesItsWreck_AsOneOverlay_ChestsOnce_ReplayedFromItsRecord()
    {
        var content = GiantWreckTestWorlds.ContentWithHulk(out var hulk);
        const string Name = "gw_once";
        string location;
        Vector3i origin;
        byte[] blob;
        ushort[] cells;
        List<(string Id, Vector3i Cell, string Items)> chests;
        var server = Start(content, Name, out var repo);
        using (repo)
        {
            location = server.World.LocationId;
            var wreck = Assert.Single(server.GiantWrecksForTest);
            Assert.Equal("sample_hulk", wreck.Key);
            Assert.True(wreck.GiantWreck);
            Assert.False(wreck.Protected); // open: what players mine stays mined
            Assert.Empty(server.LandmarksForTest); // its own list — the landmark machinery is fed, the landmark list untouched
            origin = wreck.Origin;

            var rec = Assert.Single(server.PlacementRecordsForTest, r => r.Kind == GiantShipRules.RecordKind);
            Assert.True(rec.Placed);
            Assert.Equal((0, "sample_hulk"), (rec.Index, rec.Template));
            Assert.Equal((wreck.Origin.X, wreck.GroundY, wreck.Origin.Z), (rec.X, rec.GroundY, rec.Z));
            var pin = MarkerSettings.Parse(rec.KitLayout);
            Assert.Equal(GiantShipGenerator.RuinRevision, pin.GetInt("rev", 0));
            Assert.Equal(GiantShipGenerator.Fingerprint(hulk), pin.Get(SvGameServer.GiantWreckFingerprintPin));
            Assert.Equal(SvGameServer.LandmarkOverlayStorage, pin.Get(SvGameServer.LandmarkStoragePin));
            Assert.Equal(1, pin.GetInt(SvGameServer.GiantWreckAirPin, -1));
            Assert.Contains(server.StampReportForTest, r => r.Kind == GiantShipRules.RecordKind && r.Requested == 1 && r.Placed == 1);

            // ONE blob well under the budget, and no block edit on any of its cells.
            blob = Assert.Single(repo.ListStructureOverlays(location), r => r.Key == GiantShipRules.FeatureKey(0)).Data;
            Assert.True(blob.Length < OverlayBudgetBytes, $"the sample's overlay is {blob.Length} bytes");
            var overlay = GiantWreckTestWorlds.Decode(blob);
            Assert.True(overlay.CellCount > 0);
            Assert.Empty(GiantWreckTestWorlds.EditsOn(repo, location, overlay));

            GiantWreckTestWorlds.AssertStandsAsBuilt(server, wreck);
            cells = GiantWreckTestWorlds.LayoutCells(wreck);
            chests = GiantWreckTestWorlds.ChestsIn(server, wreck);
            Assert.NotEmpty(chests);
            Assert.Equal(wreck.Structure.Markers.Count(m => m.Type == LandmarkRules.ChestMarker), chests.Count);
            Assert.All(chests, c => Assert.NotEmpty(c.Items));
            server.Stop();
        }

        // A player emptied the first chest and broke it: it must never come back.
        using (var looted = new SqliteWorldRepository(new SaveGamePaths(_root, Name)))
        {
            looted.Initialize();
            looted.DeleteContainer(chests[0].Id);
        }

        var again = Start(content, Name, out var repo2);
        using (repo2)
        {
            var wreck = Assert.Single(again.GiantWrecksForTest);
            Assert.Equal(origin, wreck.Origin);
            Assert.Equal(cells, GiantWreckTestWorlds.LayoutCells(wreck)); // the same wreck, re-derived from its record
            Assert.Equal(blob, Assert.Single(repo2.ListStructureOverlays(location), r => r.Key == GiantShipRules.FeatureKey(0)).Data);
            Assert.DoesNotContain(again.StampReportForTest, r => r.Kind == GiantShipRules.RecordKind); // replayed, not stamped
            Assert.Equal(chests.Skip(1), GiantWreckTestWorlds.ChestsIn(again, wreck)); // the rest untouched, none refilled
            GiantWreckTestWorlds.AssertStandsAsBuilt(again, wreck);
        }
    }

    [Fact]
    public void AnAirlessWorld_CarriesAWreckWithIceForWater_AGasGiantAndAGeneration41WorldNone()
    {
        var content = GiantWreckTestWorlds.ContentWithHulk(out _);
        ushort water = content.GetBlock("water")!.NumericId.Value;
        var airless = Start(content, "gw_airless", out var repo, planet: "crystal");
        using (repo)
        {
            Assert.True(airless.World.Planet.IsAirless);
            var wreck = Assert.Single(airless.GiantWrecksForTest);
            var rec = Assert.Single(airless.PlacementRecordsForTest, r => r.Kind == GiantShipRules.RecordKind);
            Assert.Equal(0, MarkerSettings.Parse(rec.KitLayout).GetInt(SvGameServer.GiantWreckAirPin, -1));
            Assert.DoesNotContain(water, GiantWreckTestWorlds.LayoutCells(wreck)); // the pool froze
            GiantWreckTestWorlds.AssertStandsAsBuilt(airless, wreck);
        }

        var gas = Start(content, "gw_gas", out var repo2, planet: "gas_giant");
        using (repo2)
        {
            Assert.Empty(gas.GiantWrecksForTest);
            Assert.DoesNotContain(gas.PlacementRecordsForTest, r => r.Kind == GiantShipRules.RecordKind && r.Placed);
        }

        var older = Start(content, "gw_gen41", out var repo3, generation: WorldDescription.GrownSettlementsGeneration);
        using (repo3)
        {
            Assert.Empty(older.GiantWrecksForTest);
            Assert.DoesNotContain(older.PlacementRecordsForTest, r => r.Kind == GiantShipRules.RecordKind); // nothing decided at all
            Assert.DoesNotContain(older.StampReportForTest, r => r.Kind == GiantShipRules.RecordKind);
        }
    }

    [Theory]
    [InlineData("option_off")]
    [InlineData("host_off")]
    [InlineData("finale")]
    public void TheSwitches_AndTheFinaleBody_KeepAWorldFree(string which)
    {
        var content = GiantWreckTestWorlds.ContentWithHulk(out _);
        var server = Start(content, "gw_" + which, out var repo, tweak: c =>
        {
            c.World.GiantWrecks = which == "option_off" ? Frequency.Off : Frequency.Normal;
            c.PlaceGiantWrecks = which != "host_off";
        });
        using (repo)
        {
            if (which == "finale")
            {
                Assert.Single(server.GiantWrecksForTest); // the start body rolls one …
                server.LoadGuardianCoreForTest();
                Assert.True(server.HasCoreChamberForTest);
                Assert.Empty(server.GiantWrecksForTest); // … the finale body never
                Assert.DoesNotContain(server.PlacementRecordsForTest, r => r.Kind == GiantShipRules.RecordKind);
                return;
            }

            Assert.Empty(server.GiantWrecksForTest);
            Assert.DoesNotContain(server.PlacementRecordsForTest, r => r.Kind == GiantShipRules.RecordKind && r.Placed);
        }
    }

    /// <summary>The wreck keeps clear through the footprint registry: of the pads, the crash site, the settlements and the
    /// landmark stamped before it, and everything stamped after it keeps clear of the wreck.</summary>
    [Fact]
    public void TheWreck_KeepsClearOfEverythingElse_ThroughTheFootprintRegistry()
    {
        var content = GiantWreckTestWorlds.ContentWithHulk(out _);
        content.SetLandmarks(new[] { LandmarkShapes.Tower() });
        var server = Start(content, "gw_clear", out var repo, tweak: c =>
        {
            c.PlaceSettlements = true;
            c.PlaceLandmarks = true;
            c.LandmarkChance = 1.0;
        });
        using (repo)
        {
            Assert.Single(server.LandmarksForTest);
            var wreck = Assert.Single(server.GiantWrecksForTest);
            Assert.Contains(server.FootprintsForTest, f => f.Kind == GiantShipRules.RecordKind);

            // Since #3629/#3631 this world also has an intercity line: its stations and end spans touch the towns they join on
            // purpose (StructureFootprintTests.ByDesign). Every other pair — the wreck against the line included — stays clear.
            var collisions = server.FootprintCollisionsForTest()
                .Where(c => !((c.A.Kind == StructureTypeRules.Intercity && c.B.Kind == StructureTypeRules.Settlement)
                              || (c.A.Kind == StructureTypeRules.Settlement && c.B.Kind == StructureTypeRules.Intercity)))
                .ToList();
            Assert.True(collisions.Count == 0, "footprints overlap: " + string.Join("; ", collisions.Select(c => $"{c.A.Kind}@{c.A.Cx},{c.A.Cz} × {c.B.Kind}@{c.B.Cx},{c.B.Cz}")));
            Assert.True(server.OverlapsAnySettlement(wreck.Rect.Cx, wreck.Rect.Cz)); // the point checks see it too
        }
    }

    /// <summary>A record whose ship changed since (another fingerprint) keeps its blocks — the overlay still loads — and
    /// re-derives nothing: no instance, no chest, no silhouette (warned).</summary>
    [Fact]
    public void AChangedShip_KeepsTheStampedBlocks_AndReDerivesNothing()
    {
        const string Name = "gw_changed";
        var content = GiantWreckTestWorlds.ContentWithHulk(out _);
        Vector3i cell;
        ushort block;
        var server = Start(content, Name, out var repo);
        using (repo)
        {
            var wreck = Assert.Single(server.GiantWrecksForTest);
            var local = GiantWreckTestWorlds.SolidCells(wreck).First();
            cell = GiantWreckTestWorlds.At(server, wreck, local);
            block = wreck.Structure.Get(local.X, local.Y, local.Z);
            server.Stop();
        }

        var changed = GiantWreckTestWorlds.ContentWithHulk(out var hulk);
        hulk.RuinStrength = hulk.RuinStrength > 0.5 ? 0.25 : 0.75; // another design → another fingerprint
        changed.SetGiantShips(new[] { hulk });
        var again = Start(changed, Name, out var repo2);
        using (repo2)
        {
            Assert.Empty(again.GiantWrecksForTest);
            Assert.Equal(block, again.World.GetBlock(cell).Value); // its blocks stay in the world
            Assert.DoesNotContain(again.StampReportForTest, r => r.Kind == GiantShipRules.RecordKind); // and nothing is stamped anew
        }
    }

    /// <summary>The roll is a pure hash of seed and body: about one body in twenty at the shipped chance, none at 0, all at 1.</summary>
    [Fact]
    public void TheRoll_IsAHashOfSeedAndBody_AboutOneInTwenty()
    {
        int hits = Enumerable.Range(0, 4000).Count(i => SvGameServer.GiantWreckRolls(20261114, "body-" + i, GiantShipRules.Chance));
        Assert.InRange(hits, 140, 260);
        Assert.Equal(SvGameServer.GiantWreckRolls(7, "sol-3", 0.5), SvGameServer.GiantWreckRolls(7, "sol-3", 0.5));
        Assert.DoesNotContain(Enumerable.Range(0, 200), i => SvGameServer.GiantWreckRolls(i, "b", 0.0));
        Assert.All(Enumerable.Range(0, 200), i => Assert.True(SvGameServer.GiantWreckRolls(i, "b", 1.0)));
    }

    /// <summary>The sample's tree and pool are what the stamp must carry over: across seeds the planet wreck keeps them (the
    /// ruin pass never scorches plants or water), and without air the pool is ice, never water.</summary>
    [Fact]
    public void TheSamplesTreeAndPool_SurviveTheWreck_AsIceWithoutAir()
    {
        var content = GiantWreckTestWorlds.ContentWithHulk(out var hulk);
        ushort Id(string key) => content.GetBlock(key)!.NumericId.Value;
        int trees = 0, pools = 0;
        for (long seed = 1; seed <= 8; seed++)
        {
            var wet = GiantShipGenerator.BuildPlanet(hulk, seed, (int)(seed % 4), content, "grass", atmosphere: true).Structure;
            var dry = GiantShipGenerator.BuildPlanet(hulk, seed, (int)(seed % 4), content, "grass", atmosphere: false).Structure;
            var wetCells = GiantWreckTestWorlds.AllCells(wet).Select(c => wet.Get(c.X, c.Y, c.Z)).ToList();
            var dryCells = GiantWreckTestWorlds.AllCells(dry).Select(c => dry.Get(c.X, c.Y, c.Z)).ToList();
            trees += wetCells.Contains(Id("wood_log")) && wetCells.Contains(Id("tree_leaves")) ? 1 : 0;
            pools += wetCells.Contains(Id("water")) ? 1 : 0;
            Assert.DoesNotContain(Id("water"), dryCells);
        }

        Assert.True(trees >= 4, $"the sample's tree survived in only {trees} of 8 wrecks");
        Assert.True(pools >= 4, $"the sample's pool survived in only {pools} of 8 wrecks");
    }
}
