// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using Xunit.Abstractions;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The grown settlements on a real server (#3584, #3588, #3589, #3592, #3597): a fresh world of generation 41 seats its
/// planned settlements, pins their plans and replays them unchanged; the corners of a planned settlement's box are free ground
/// while its lots are protected; its pens hold animals and lay products; an older world never draws a planned kit.
/// </summary>
public sealed class GrownSettlementServerTests : IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_grown_" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _out;

    public GrownSettlementServerTests(ITestOutputHelper output) => _out = output;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort — a still-open SQLite handle on Windows
        }
    }

    private SvGameServer Start(string name, string planet, long seed, int generation, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            LandmarkChance = 0.0,
        };
        config.World.TerrainGeneration = generation;
        config.World.Settlements = Frequency.Frequent;
        var server = new SvGameServer(config, Content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    [Fact]
    public void AFreshWorld_PinsItsPlans_AndReplaysThemUnchanged()
    {
        var server = Start("fresh", "meadowlands", 2602, WorldDescription.GrownSettlementsGeneration, out var repo);
        var plans = server.PlacementRecordsForTest.Where(r => r.Kind == "settlement" && r.Plan.Length > 0).Select(r => r.Plan).ToList();
        var boxes = server.SettlementsForTest.ToList();
        var planned = server.PlannedSettlementsForTest.ToList();
        server.Stop();
        repo.Dispose();
        _out.WriteLine($"{boxes.Count} settlements, {plans.Count} planned");

        Assert.NotEmpty(plans);
        Assert.All(plans, p => Assert.True(SettlementPlan.TryParse(p, out _)));
        Assert.NotEmpty(planned);

        var again = Start("fresh", "meadowlands", 2602, WorldDescription.GrownSettlementsGeneration, out var repo2);
        try
        {
            Assert.Equal(plans, again.PlacementRecordsForTest.Where(r => r.Kind == "settlement" && r.Plan.Length > 0).Select(r => r.Plan).ToList());
            Assert.Equal(boxes, again.SettlementsForTest.ToList());
        }
        finally
        {
            again.Stop();
            repo2.Dispose();
        }
    }

    [Fact]
    public void ThePlannedBox_IsProtectedOnlyWhereItIsClaimed()
    {
        var server = Start("mask", "meadowlands", 2602, WorldDescription.GrownSettlementsGeneration, out var repo);
        try
        {
            var (min, groundY, layout) = server.PlannedSettlementsForTest.First();
            (int X, int Z)? open = null, claimed = null;
            for (int x = 0; x < layout.Width && (open is null || claimed is null); x++)
                for (int z = 0; z < layout.Length; z++)
                {
                    if (layout.FloorAt(x, z) < 0) open ??= (x, z);
                    else claimed ??= (x, z);
                }

            Assert.NotNull(open);
            Assert.NotNull(claimed);
            var o = open!.Value;
            var c = claimed!.Value;
            Assert.False(server.IsSettlementProtectedForTest(new Vector3i(min.X + o.X, groundY - 1, min.Z + o.Z)));
            Assert.True(server.IsSettlementProtectedForTest(new Vector3i(min.X + c.X, groundY + layout.FloorAt(c.X, c.Z) - 1, min.Z + c.Z)));
        }
        finally
        {
            server.Stop();
            repo.Dispose();
        }
    }

    [Fact]
    public void TheTownZone_FollowsTheClaimedColumns()
    {
        var server = Start("zone", "meadowlands", 2602, WorldDescription.GrownSettlementsGeneration, out var repo);
        try
        {
            var (min, groundY, layout) = server.PlannedSettlementsForTest.First();
            int lots = 0;
            for (int x = 0; x < layout.Width; x++)
                for (int z = 0; z < layout.Length; z++)
                {
                    if (layout.FloorAt(x, z) >= 0 && lots++ == 0)
                    {
                        Assert.True(server.TownZoneDepthForTest(min.X + x, groundY + layout.FloorAt(x, z) + 1, min.Z + z) > 0);
                    }
                }

            Assert.True(lots > 0);
        }
        finally
        {
            server.Stop();
            repo.Dispose();
        }
    }

    [Fact]
    public void APen_KeepsItsAnimals_AndLaysIntoItsCrate()
    {
        for (long seed = 2602; seed < 2612; seed++)
        {
            var server = Start("pens" + seed, "meadowlands", seed, WorldDescription.GrownSettlementsGeneration, out var repo);
            try
            {
                if (server.PensForTest.FirstOrDefault(p => p.Boxes > 0) is not { Key.Length: > 0 } pen)
                {
                    continue;
                }

                if (server.FieldsForTest.FirstOrDefault() is { W: > 0 } field)
                {
                    var feet = server.PlannedSettlementsForTest.First().GroundY; // a field's feet row is near its settlement's ground
                    var cell = new Vector3i(field.X + 1, feet + 1, field.Z + 1);
                    double onField = Enumerable.Range(-6, 14).Select(dy => server.RegrowSecondsForTest(cell + new Vector3i(0, dy, 0), "flora_cropgrain")).Max();
                    Assert.Equal(SvGameServer.FieldRegrowSeconds, onField);
                    Assert.True(server.RegrowSecondsForTest(new Vector3i(field.X - 40, feet, field.Z - 40), "flora_cropgrain") < SvGameServer.FieldRegrowSeconds);
                }

                server.TickProfessionsForTest();
                Assert.InRange(server.PensForTest.First(p => p.Key == pen.Key).Animals, 2, 3);
                server.LayPenProductsForTest();
                server.LayPenProductsForTest();
                Assert.Contains(server.Containers, c => c.Id.StartsWith("penbox_", StringComparison.Ordinal)
                    && c.Items.Any(s => s.Item is "egg" or "milk" or "wool" && s.Count > 0));
                return;
            }
            finally
            {
                server.Stop();
                repo.Dispose();
            }
        }

        Assert.Fail("no world of ten had a pen");
    }

    [Fact]
    public void TheGdsCity_GrowsItsOutline_ItsWall_AndItsSuburbs()
    {
        for (long seed = 1; seed <= 6; seed++)
        {
            var server = Start("city" + seed, "gds_desert", seed, WorldDescription.GrownSettlementsGeneration, out var repo);
            try
            {
                var rec = server.PlacementRecordsForTest.First(r => r.Kind == "settlement" && r.Index == 0);
                if (!rec.KitLayout.Contains("grown:", StringComparison.Ordinal))
                {
                    continue;
                }

                var spec = CityLayoutSpec.TryParse(rec.KitLayout, out var s) ? s : CityLayoutSpec.Default;
                Assert.True(spec.Grown);
                var box = server.SettlementsForTest.First();
                Assert.Equal(spec.Footprint - 1, box.MaxX - box.MinX);
                Assert.Contains('~', rec.Plan); // the suburbs are pinned, one plan per gate
                Assert.Contains(rec.Plan.Split('~'), p => SettlementPlan.TryParse(p, out _));
                return;
            }
            finally
            {
                server.Stop();
                repo.Dispose();
            }
        }

        Assert.Fail("no G.D.S. world of six drew the grown city");
    }

    [Fact]
    public void AnOlderWorld_NeverDrawsAPlannedKit()
    {
        var server = Start("older", "meadowlands", 2602, WorldDescription.GeographyGeneration, out var repo);
        try
        {
            Assert.DoesNotContain(server.PlacementRecordsForTest, r => r.Plan.Length > 0);
            Assert.Empty(server.PlannedSettlementsForTest);
        }
        finally
        {
            server.Stop();
            repo.Dispose();
        }
    }
}
