// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Lyxette's garden report of 2026-11-24: the flower seeds that sow a mixed, colourful bed (#4001), the bush cutting and the
/// bush sapling that grows a small leafy bush without a trunk (#4003), all three from the gatherers' market.
/// </summary>
public sealed class GardenPlantsTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public GardenPlantsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts-garden-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "garden"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "garden", Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>A 5×5 bed of <paramref name="ground"/> around (50, 49, 50) with free air above it and a gardener beside it
    /// carrying <paramref name="item"/>.</summary>
    private static void Bed(SvGameServer server, GameContent content, string ground, string item)
    {
        var soil = content.GetBlock(ground)!.NumericId;
        for (int dx = -4; dx <= 4; dx++)
            for (int dz = -4; dz <= 4; dz++)
            {
                server.World.SetBlock(new Vector3i(50 + dx, 49, 50 + dz), soil);
                for (int y = 50; y <= 60; y++)
                {
                    server.World.SetBlock(new Vector3i(50 + dx, y, 50 + dz), BlockId.Air);
                }
            }

        var session = server.AddLocalPlayer("Gardener");
        session.State.Inventory.Add(item, 99, 1024);
        session.State.Position = new Vector3f(50.5f, 50f, 54.5f);
    }

    private static IEnumerable<Vector3i> BedCells()
    {
        for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
            {
                yield return new Vector3i(50 + dx, 50, 50 + dz);
            }
    }

    private string KeyAt(SvGameServer server, Vector3i c) => _content.BlockById(server.World.GetBlock(c))?.Key ?? "?";

    private List<string> Pool() => _content.GetItem("flower_seed")!.PlacesAnyOf!;

    [Fact]
    public void FlowerSeeds_AreMeadowFlowers_AndTheirFaceIsOneOfThem()
    {
        var seed = _content.GetItem("flower_seed")!;
        Assert.Contains(seed.PlacesBlock!, Pool());
        Assert.True(Pool().Count >= 5, "enough flowers for a mixed bed");
        foreach (string key in Pool())
        {
            var species = FloraCatalog.All.Single(s => s.Key == key);
            Assert.True((species.Tags & FloraTag.Floral) != 0, $"{key} is a flower");
            Assert.False(species.Authored, $"{key} takes the world's colours");
            Assert.False(FloraCatalog.KeepsOwnColour(key), $"{key} takes the world's colours");
        }
    }

    [Fact]
    public void FlowerSeeds_SowAMixedBedOnGrass_TheSameFlowerPerCell()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Bed(server, _content, "grass", "flower_seed");
            var planted = new Dictionary<Vector3i, string>();
            foreach (var c in BedCells())
            {
                server.PlaceBlock("Gardener", c.X, c.Y, c.Z, "flower_seed");
                planted[c] = KeyAt(server, c);
                Assert.Contains(planted[c], Pool());
            }

            Assert.True(planted.Values.Distinct().Count() >= 3, "a bed of one bag holds several flowers");

            // the pick belongs to the cell: pick a flower and sow the cell again, the same flower comes back
            foreach (var c in BedCells().Take(6))
            {
                server.MineBlock("Gardener", c.X, c.Y, c.Z);
                Assert.True(server.World.GetBlock(c).IsAir);
                server.PlaceBlock("Gardener", c.X, c.Y, c.Z, "flower_seed");
                Assert.Equal(planted[c], KeyAt(server, c));
            }
        }
    }

    [Fact]
    public void FlowerSeeds_OnDirt_PlantOnlyTheFlowersThatRootThere()
    {
        var rootsOnDirt = Pool().Where(k =>
        {
            var s = FloraCatalog.All.Single(sp => sp.Key == k);
            return s.Hosts.Concat(s.LateHosts ?? Array.Empty<string>()).Concat(s.CaveHosts ?? Array.Empty<string>()).Contains("dirt");
        }).ToHashSet();
        Assert.NotEmpty(rootsOnDirt);

        var server = Started(out var repo);
        using (repo)
        {
            Bed(server, _content, "dirt", "flower_seed");
            foreach (var c in BedCells())
            {
                server.PlaceBlock("Gardener", c.X, c.Y, c.Z, "flower_seed");
                Assert.Contains(KeyAt(server, c), rootsOnDirt);
            }
        }
    }

    [Fact]
    public void FlowerSeeds_OnStone_PlantNothing()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Bed(server, _content, "stone", "flower_seed");
            var c = new Vector3i(50, 50, 50);
            server.PlaceBlock("Gardener", c.X, c.Y, c.Z, "flower_seed");
            Assert.True(server.World.GetBlock(c).IsAir);
        }
    }

    [Fact]
    public void BushCutting_PlantsTheWildBush()
    {
        Assert.Equal("flora_bush", _content.GetItem("bush_cutting")!.PlacesBlock);
        var server = Started(out var repo);
        using (repo)
        {
            Bed(server, _content, "grass", "bush_cutting");
            var c = new Vector3i(50, 50, 50);
            server.PlaceBlock("Gardener", c.X, c.Y, c.Z, "bush_cutting");
            Assert.Equal("flora_bush", KeyAt(server, c));
        }
    }

    [Theory]
    [InlineData(0, 6)]
    [InlineData(1, 14)]
    public void BushShape_IsLeavesOnlyOneOrTwoHighAndThreeWide(int variant, int cells)
    {
        var bush = SaplingTrees.For(SaplingShape.Bush, variant, 1UL);
        Assert.Equal(new[] { (0, 0, 0) }, bush.Trunk);
        Assert.Equal(cells, bush.Trunk.Count + bush.Crown.Count);
        Assert.All(bush.Crown, c => Assert.InRange(c.Y, 0, 1));
        Assert.All(bush.Crown, c => Assert.True(Math.Abs(c.X) <= 1 && Math.Abs(c.Z) <= 1));
        Assert.Empty(bush.Blossoms);
        Assert.Empty(bush.Berries);
    }

    [Fact]
    public void BushSapling_GrowsALeafyBushWithoutATrunk()
    {
        var kind = SaplingCatalog.ForBlock("flora_sapling_bush")!;
        Assert.Equal(SaplingShape.Bush, kind.Shape);
        var server = Started(out var repo);
        using (repo)
        {
            Bed(server, _content, "dirt", kind.ItemKey);
            var c = new Vector3i(50, 50, 50);
            server.PlaceBlock("Gardener", c.X, c.Y, c.Z, kind.ItemKey);
            Assert.Equal(kind.BlockKey, KeyAt(server, c));

            server.Tick(SaplingCatalog.DefaultGrowSeconds + 5.0);
            Assert.Equal("tree_leaves", KeyAt(server, c));
            Assert.Equal("tree_leaves", KeyAt(server, new Vector3i(51, 50, 50)));
            Assert.Equal("tree_leaves", KeyAt(server, new Vector3i(50, 51, 50)));
            ushort log = _content.GetBlock("wood_log")!.NumericId.Value;
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = 0; dy <= 4; dy++)
                    for (int dz = -2; dz <= 2; dz++)
                    {
                        var cell = new Vector3i(50 + dx, 50 + dy, 50 + dz);
                        Assert.NotEqual(log, server.World.GetBlock(cell).Value);
                        if (dy >= 2)
                        {
                            Assert.True(server.World.GetBlock(cell).IsAir, "a bush is at most two blocks high");
                        }
                    }
        }
    }

    [Theory]
    [InlineData("flower_seed")]
    [InlineData("bush_cutting")]
    [InlineData("sapling_bush")]
    public void TheGatherersMarket_SellsTheGardenPlants(string item)
    {
        Assert.Contains(_content.Recipes.Values, r => r.Station == CraftingStation.Market && r.MarketTheme == "gatherers"
            && r.Outputs.Any(o => o.Item == item));
    }
}
