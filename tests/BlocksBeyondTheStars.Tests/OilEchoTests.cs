// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The terrain scanner's oil echo (#2372): every pulse also asks the pocket grid for the nearest oil pocket within 800
/// blocks, checks the live world for oil left in it, and reports its top cell — plus a ping on the compass and the map.
/// Silent on a world without oil; a pocket pumped dry at its heart no longer answers. And the bio-lubricant (#2373): the
/// route to lubricant that needs no oil.
/// </summary>
public sealed class OilEchoTests : IDisposable
{
    private const int EchoRange = 800;
    private readonly string _root;
    private readonly GameContent _content;

    public OilEchoTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_oilecho_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(string planet, long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "oilecho-" + planet));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "oilecho-" + planet,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static readonly (int X, int Z)[] Heart = { (0, 0), (3, 0), (-3, 0), (0, 3), (0, -3) };

    [Fact]
    public void TheScan_HearsTheNearestPocket_AndAPocketPumpedDryAtItsHeartFallsSilent()
    {
        var server = Started("jungle", 3, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Driller");
            p.State.Inventory.Add("terrain_scanner", 1, 1);
            int ground = server.SurfaceHeightForTest(10, 10);
            p.State.Position = new Vector3f(10.5f, ground + 1, 10.5f);

            var pockets = server.World.Generator.FindOilPocketsNear(server.World.Planet, 10, 10, EchoRange);
            Assert.NotEmpty(pockets); // the seed is chosen so a pocket lies in range of the spawn
            var nearest = pockets[0];

            var scan = server.OreScanForTest("Driller");
            Assert.True(scan.OilEcho);
            Assert.True(scan.OilFound);
            Assert.Equal(EchoRange, scan.OilEchoRange);
            var oil = _content.GetBlock("oil")!.NumericId;
            Assert.Equal(oil, server.World.GetBlock(new Vector3i(scan.OilX, scan.OilY, scan.OilZ)));
            int circ = server.World.Circumference;
            Assert.InRange(System.Math.Abs(WorldConstants.WrapDeltaX(scan.OilX - nearest.X, circ)), 0, 3);
            Assert.InRange(System.Math.Abs(scan.OilZ - nearest.Z), 0, 3);
            Assert.InRange(scan.OilY, nearest.OilLo, nearest.OilHi);

            // A pump empties the pocket's heart — the five columns the echo listens at — and the echo moves on.
            foreach (var (dx, dz) in Heart)
            {
                for (int y = nearest.OilLo; y <= nearest.OilHi; y++)
                {
                    var cell = new Vector3i(nearest.X + dx, y, nearest.Z + dz);
                    if (server.World.GetBlock(cell) == oil)
                    {
                        server.World.SetBlock(cell, BlockId.Air);
                    }
                }
            }

            var again = server.OreScanForTest("Driller");
            Assert.True(again.OilEcho);
            if (again.OilFound)
            {
                bool samePocket = System.Math.Abs(WorldConstants.WrapDeltaX(again.OilX - nearest.X, circ)) <= 3
                                  && System.Math.Abs(again.OilZ - nearest.Z) <= 3;
                Assert.False(samePocket, "the echo still points at the pocket pumped dry at its heart");
            }
        }
    }

    [Fact]
    public void TheRealScan_PingsThePocket_OnTheCompass()
    {
        var server = Started("jungle", 3, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pinger");
            p.State.Inventory.Add("terrain_scanner", 1, 1);
            int ground = server.SurfaceHeightForTest(10, 10);
            p.State.Position = new Vector3f(10.5f, ground + 1, 10.5f);
            Assert.DoesNotContain(server.VisibleMarkersForTest("Pinger"), m => m.Ping);

            server.UseGadgetForTest("Pinger", "terrain_scanner", p.State.Position);

            Assert.Contains(server.VisibleMarkersForTest("Pinger"), m => m.Ping);
        }
    }

    [Fact]
    public void OnAWorldWithoutLife_TheScanSaysNothingAboutOil()
    {
        var server = Started("crystal", 3, out var repo);
        using (repo)
        {
            Assert.False(server.World.Planet.HasLife);
            var p = server.AddLocalPlayer("Miner");
            p.State.Inventory.Add("terrain_scanner", 1, 1);
            int ground = server.SurfaceHeightForTest(10, 10);
            p.State.Position = new Vector3f(10.5f, ground + 1, 10.5f);

            var scan = server.OreScanForTest("Miner");
            Assert.False(scan.OilEcho);
            Assert.False(scan.OilFound);
        }
    }

    [Fact]
    public void BioLubricant_IsARefineryRecipeWithoutOil_AndDearerThanCrude()
    {
        var lubricantRecipes = _content.Recipes.Values.Where(r => r.Outputs.Any(o => o.Item == "lubricant")).ToList();
        var bio = Assert.Single(lubricantRecipes, r => r.Inputs.All(i => i.Item != "oil"));
        var crude = Assert.Single(lubricantRecipes, r => r.Inputs.Any(i => i.Item == "oil"));

        Assert.Equal(CraftingStation.Refinery, bio.Station);
        Assert.Equal(3, bio.Inputs.Single(i => i.Item == "biofuel").Count);
        Assert.Equal(1, bio.Inputs.Single(i => i.Item == "carbon").Count);
        Assert.Equal(1, bio.Outputs.Single(o => o.Item == "lubricant").Count);
        Assert.True(string.IsNullOrEmpty(bio.RequiredBlueprint), "the fallback must not hide behind another blueprint");

        // Oil stays the better route: lubricant per input unit.
        double crudeYield = crude.Outputs.Single(o => o.Item == "lubricant").Count / (double)crude.Inputs.Sum(i => i.Count);
        double bioYield = bio.Outputs.Single(o => o.Item == "lubricant").Count / (double)bio.Inputs.Sum(i => i.Count);
        Assert.True(bioYield < crudeYield);

        // Biofuel itself needs nothing a world without oil lacks: a hand recipe from plants.
        Assert.Contains(_content.Recipes.Values, r => r.Station == CraftingStation.Hand && r.Outputs.Any(o => o.Item == "biofuel")
                                                     && r.Inputs.All(i => i.Item is "berries" or "plant_fiber"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }
}
