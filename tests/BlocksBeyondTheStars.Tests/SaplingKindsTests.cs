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
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The sapling kinds of Lyxette's garden report (#2977 the fruit trees, #2978 the conifer, #2979 the Fifi plant, #2980 the
/// giant mahogany): where each comes from (data), the tree it grows (<see cref="SaplingTrees"/>), and the real server growing
/// it — fruit included, the young giant's 2×2 trunk into whichever quarter has room.
/// </summary>
public sealed class SaplingKindsTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public SaplingKindsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts-sapling-kinds-" + Guid.NewGuid().ToString("N"));
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

    private SvGameServer Started(out SqliteWorldRepository repo, string name = "sapling-kinds")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = name, Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Dirt under a 9×9 patch around (50,50,50) with sixteen free layers above it, a gardener beside it, and the
    /// sapling of <paramref name="kind"/> planted at the centre.</summary>
    private Vector3i Plant(SvGameServer server, SaplingKind kind, string ground = "dirt") => Plant(server, kind, out _, ground);

    private Vector3i Plant(SvGameServer server, SaplingKind kind, out PlayerSession session, string ground = "dirt")
    {
        var soil = _content.GetBlock(ground)!.NumericId;
        for (int dx = -4; dx <= 4; dx++)
            for (int dz = -4; dz <= 4; dz++)
            {
                server.World.SetBlock(new Vector3i(50 + dx, 49, 50 + dz), soil);
                for (int y = 50; y <= 66; y++)
                {
                    server.World.SetBlock(new Vector3i(50 + dx, y, 50 + dz), BlockId.Air);
                }
            }

        server.World.SetBlock(new Vector3i(50, 49, 50), _content.GetBlock("dirt")!.NumericId);
        var cell = new Vector3i(50, 50, 50);
        session = server.AddLocalPlayer("Gardener");
        session.State.Inventory.Add(kind.ItemKey, 4, 99);
        session.State.Position = new Vector3f(55.5f, 50f, 55.5f);
        server.PlaceBlock("Gardener", cell.X, cell.Y, cell.Z, kind.ItemKey);
        Assert.Equal(_content.GetBlock(kind.BlockKey)!.NumericId.Value, server.World.GetBlock(cell).Value);
        return cell;
    }

    private int Count(SvGameServer server, string blockKey, Vector3i around, int r = 6, int up = 16)
    {
        ushort id = _content.GetBlock(blockKey)!.NumericId.Value;
        int n = 0;
        for (int dx = -r; dx <= r; dx++)
            for (int dy = -1; dy <= up; dy++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (server.World.GetBlock(new Vector3i(around.X + dx, around.Y + dy, around.Z + dz)).Value == id)
                    {
                        n++;
                    }
                }

        return n;
    }

    private static SaplingKind Kind(string blockKey) => SaplingCatalog.ForBlock(blockKey)!;

    [Fact]
    public void EveryKind_HasItsBlockAndItem_AndIsPickedUpAsItself()
    {
        foreach (var kind in SaplingCatalog.All)
        {
            var block = _content.GetBlock(kind.BlockKey);
            var item = _content.GetItem(kind.ItemKey);
            Assert.NotNull(block);
            Assert.NotNull(item);
            Assert.Equal(kind.BlockKey, item!.PlacesBlock);
            Assert.Contains(block!.Drops, d => d.Item == kind.ItemKey && d.Count == 1);
            foreach (var part in new[] { kind.Log, kind.Crown, kind.Fruit, kind.Blossom, kind.Berries })
            {
                Assert.True(part == null || _content.GetBlock(part) != null, $"{kind.BlockKey}: {part} is a block");
            }

            Assert.DoesNotContain(FloraCatalog.All, s => s.Key == kind.BlockKey); // no roster, no greenhouse
        }
    }

    [Theory]
    [InlineData("round")]
    [InlineData("long")]
    [InlineData("grape")]
    [InlineData("banana")]
    public void FruitSaplingRecipe_TakesItsOwnCleanFruit(string fruit)
    {
        var recipe = _content.GetRecipe("sapling_fruit_" + fruit)!;
        Assert.Equal(CraftingStation.Hand, recipe.Station);
        Assert.Contains(recipe.Inputs, i => i.Item == "fruit_" + fruit && i.Count == 1);
        Assert.DoesNotContain(recipe.Inputs, i => i.Item.StartsWith("toxic_", StringComparison.Ordinal));
        Assert.Contains(recipe.Inputs, i => i.Group == "logs");
        Assert.Equal("sapling_fruit_" + fruit, Assert.Single(recipe.Outputs).Item);
    }

    [Fact]
    public void Leaves_DropTheirOwnSapling_AndOnlyTheirLeavesGiveTheFifiAndTheMahogany()
    {
        Assert.Contains(_content.GetBlock("pine_needles")!.RandomDrops!, d => d.Item == "sapling_conifer");
        Assert.DoesNotContain(_content.GetBlock("pine_needles")!.RandomDrops!, d => d.Item == "sapling");
        Assert.Contains(_content.GetBlock("tree_leaves")!.RandomDrops!, d => d.Item == "sapling");
        Assert.Contains(_content.GetBlock("fifi_leaf")!.RandomDrops!, d => d.Item == "sapling_fifi");
        Assert.Contains(_content.GetBlock("giant_leaves")!.RandomDrops!, d => d.Item == "sapling_mahogany");
        Assert.NotNull(_content.GetRecipe("sapling_conifer"));

        // #2979: no recipe and no trader — a Fifi sapling only comes from where the Fifi plant grows
        foreach (var r in _content.Recipes.Values)
        {
            Assert.DoesNotContain(r.Outputs, o => o.Item is "sapling_fifi" or "sapling_mahogany");
        }
    }

    [Fact]
    public void Shapes_AreTheTreesTheIssuesDescribe()
    {
        for (int variant = 0; variant < 6; variant++)
        {
            var fir = SaplingTrees.For(SaplingShape.Conifer, variant, 1UL);
            Assert.InRange(fir.Trunk.Count, 5, 6);
            int top = fir.Trunk.Max(c => c.Y);
            Assert.Contains((0, top + 2, 0), fir.Crown); // the tip
            int lowRing = fir.Crown.Count(c => c.Y == 2), highRing = fir.Crown.Count(c => c.Y == top);
            Assert.True(lowRing > highRing, "a cone: wide below, narrow on top");

            var palm = SaplingTrees.For(SaplingShape.Palm, variant, 1UL);
            Assert.InRange(palm.Trunk.Count, 5, 6);
            Assert.All(palm.Crown, c => Assert.True(c.Y >= palm.Trunk.Count - 1, "a bare trunk under the fronds"));

            var giant = SaplingTrees.For(SaplingShape.YoungGiant, variant, 1UL);
            Assert.InRange(giant.Trunk.Count / 4, 9, 11);
            Assert.Equal(4, giant.Trunk.Count(c => c.Y == 0)); // a 2×2 foot
            Assert.True(giant.Crown.Count > 60, "a wide crown");
            var mirrored = SaplingTrees.Mirrored(giant, -1, -1);
            Assert.Contains((-1, 0, -1), mirrored.Trunk);
        }

        var fifi = SaplingTrees.For(SaplingShape.Fifi, 0, 0xC0FFEEUL);
        Assert.NotEmpty(fifi.Blossoms);
        Assert.NotEmpty(fifi.Berries);
    }

    [Theory]
    [InlineData("flora_sapling_fruit_round", "flora_fruit_round", "tree_leaves")]
    [InlineData("flora_sapling_fruit_long", "flora_fruit_long", "tree_leaves")]
    [InlineData("flora_sapling_fruit_grape", "flora_fruit_grape", "tree_leaves")]
    [InlineData("flora_sapling_fruit_banana", "flora_fruit_banana", "palm_frond")]
    public void FruitSapling_GrowsATree_ThatAlwaysBearsItsFruit_InTheWorldsColourForIt(string saplingKey, string fruitKey, string crownKey)
    {
        var server = Started(out var repo);
        using (repo)
        {
            var cell = Plant(server, Kind(saplingKey));
            server.Tick(SaplingCatalog.DefaultGrowSeconds + 5.0);
            Assert.Equal(_content.GetBlock("wood_log")!.NumericId.Value, server.World.GetBlock(cell).Value);
            Assert.True(Count(server, crownKey, cell) >= 8, $"{saplingKey} grows a crown of {crownKey}");

            int fruit = Count(server, fruitKey, cell);
            Assert.InRange(fruit, 3, 5);
            int tint = FruitRules.TintForShape(WorldGenerator.RosterSeedFor(7, server.World.LocationId), fruitKey);
            ushort fruitId = _content.GetBlock(fruitKey)!.NumericId.Value;
            for (int dx = -4; dx <= 4; dx++)
                for (int dy = 0; dy <= 9; dy++)
                    for (int dz = -4; dz <= 4; dz++)
                    {
                        var c = new Vector3i(cell.X + dx, cell.Y + dy, cell.Z + dz);
                        if (server.World.GetBlock(c).Value == fruitId)
                        {
                            Assert.Equal(tint, server.World.GetModifier(c).Tint);
                        }
                    }
        }
    }

    [Fact]
    public void ConiferSapling_GrowsASmallFir_WithoutFruit()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var cell = Plant(server, Kind("flora_sapling_conifer"));
            server.Tick(SaplingCatalog.DefaultGrowSeconds + 5.0);
            Assert.Equal(_content.GetBlock("wood_log")!.NumericId.Value, server.World.GetBlock(new Vector3i(cell.X, cell.Y + 4, cell.Z)).Value);
            Assert.True(Count(server, "pine_needles", cell) >= 15, "a crown of needle rings");
            Assert.Equal(0, Count(server, "tree_leaves", cell));
            foreach (var fruit in FloraCatalog.FruitKeys())
            {
                Assert.Equal(0, Count(server, fruit, cell));
            }
        }
    }

    [Fact]
    public void FifiSapling_GrowsAFifiPlant_WithBlossomsAndBerries()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var cell = Plant(server, Kind("flora_sapling_fifi"));
            server.Tick(SaplingCatalog.DefaultGrowSeconds + 5.0);
            Assert.Equal(_content.GetBlock(FifiPlant.StemKey)!.NumericId.Value, server.World.GetBlock(cell).Value);
            Assert.True(Count(server, FifiPlant.LeafKey, cell) >= 10, "a yellow crown");
            Assert.True(Count(server, FifiPlant.BlossomKey, cell) >= 1, "a glowing blossom");
            Assert.True(Count(server, FifiPlant.BerriesKey, cell) >= 1, "berries under the leaves");
        }
    }

    [Fact]
    public void MahoganySapling_WaitsWithoutA2x2Patch_ThenGrowsIntoTheQuarterThatHasOne()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var kind = Kind("flora_sapling_mahogany");
            var cell = Plant(server, kind, ground: "stone"); // only the sapling's own cell has soil under it
            server.Tick(SaplingCatalog.GiantGrowSeconds + 5.0);
            Assert.Equal(_content.GetBlock(kind.BlockKey)!.NumericId.Value, server.World.GetBlock(cell).Value);

            var dirt = _content.GetBlock("dirt")!.NumericId;
            foreach (var (x, z) in new[] { (49, 49), (49, 50), (50, 49) }) // the south-west… quarter: -X / -Z
            {
                server.World.SetBlock(new Vector3i(x, 49, z), dirt);
            }

            server.Tick(35.0); // the retry interval
            ushort giant = _content.GetBlock("giant_log")!.NumericId.Value;
            foreach (var (x, z) in new[] { (50, 50), (49, 49), (49, 50), (50, 49) })
            {
                Assert.Equal(giant, server.World.GetBlock(new Vector3i(x, 50, z)).Value);
                Assert.Equal(giant, server.World.GetBlock(new Vector3i(x, 58, z)).Value); // at least nine high
            }

            Assert.True(Count(server, "giant_leaves", cell) > 60, "a wide crown of giant leaves");
        }
    }

    [Fact]
    public void PickedKindSapling_ComesBackAsItself()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var kind = Kind("flora_sapling_fruit_grape");
            var cell = Plant(server, kind, out var session);
            int before = session.State.Inventory.CountOf(kind.ItemKey);
            server.MineBlock("Gardener", cell.X, cell.Y, cell.Z);
            Assert.Equal(before + 1, session.State.Inventory.CountOf(kind.ItemKey));
            server.Tick(SaplingCatalog.DefaultGrowSeconds + 5.0);
            Assert.True(server.World.GetBlock(cell).IsAir, "a picked sapling grows nothing");
        }
    }

    // ---- #2976: several fruits per world, picked per tree (terrain generation 30) ----

    private IEnumerable<PlanetType> TreePlanets()
        => _content.Planets.Values.Where(p => !p.IsAirless && p.FloraDensity > 0 && p.TreeDensity is not <= 0.0 && !p.Void);

    private static List<string> ActiveFruit(PlanetType planet, long seed, int generation)
        => FloraGenerator.GenerateRoster(planet, seed, generation)
            .Where(f => f.Active && FloraCatalog.IsFruit(f.BlockKey)).Select(f => f.BlockKey).ToList();

    [Fact]
    public void Generation30_WorldsWithTrees_GrowAtLeastThreeFruits_AndOlderRostersStayAsTheyWere()
    {
        Assert.NotEmpty(TreePlanets());
        foreach (var planet in TreePlanets())
        {
            for (long seed = 1; seed <= 12; seed++)
            {
                var gen30 = ActiveFruit(planet, seed, 30);
                Assert.True(gen30.Count >= FloraGenerator.MinActiveFruitShapes, $"{planet.Key} seed {seed}: {gen30.Count} fruits");

                // everything that was active stays active; nothing else in the roster moves
                var before = FloraGenerator.GenerateRoster(planet, seed, 29);
                var after = FloraGenerator.GenerateRoster(planet, seed, 30);
                Assert.Equal(before.Count, after.Count);
                for (int i = 0; i < before.Count; i++)
                {
                    Assert.Equal(before[i].BlockKey, after[i].BlockKey);
                    Assert.Equal(before[i].Name, after[i].Name);
                    Assert.Equal(before[i].Toxic, after[i].Toxic);
                    if (!FloraCatalog.IsFruit(before[i].BlockKey))
                    {
                        Assert.Equal(before[i].Active, after[i].Active);
                    }
                    else if (before[i].Active)
                    {
                        Assert.True(after[i].Active);
                    }
                }
            }
        }
    }

    [Fact]
    public void FruitForTree_KeepsTheKindsRollUpToGeneration29_AndPicksPerTreeFrom30()
    {
        var active = FloraCatalog.FruitKeys().ToList();
        const long roster = 4242;
        for (ulong h = 1; h < 50; h++)
        {
            var old = FruitRules.FruitForTree(roster, TreeKind.Broadleaf, h * 7919UL, active, null, 29);
            Assert.Equal<(string Shape, int Tint)?>((FruitRules.ShapeFor(roster, TreeKind.Broadleaf, active)!, FruitRules.TintFor(roster, TreeKind.Broadleaf)), old);
        }

        var shapes = new HashSet<string>();
        for (ulong h = 1; h < 200; h++)
        {
            var borne = FruitRules.FruitForTree(roster, TreeKind.Broadleaf, h * 0x9E3779B97F4A7C15UL, active, null, 30)!.Value;
            shapes.Add(borne.Shape);
            Assert.Equal(FruitRules.TintForShape(roster, borne.Shape), borne.Tint); // one colour per fruit
        }

        Assert.Equal(active.Count, shapes.Count); // the broadleaves of one world bear every fruit
        Assert.Null(FruitRules.FruitForTree(roster, TreeKind.Dead, 1UL, active, null, 30));
        Assert.Equal("flora_fruit_banana", FruitRules.FruitForTree(roster, TreeKind.Palm, 5UL, active, "flora_fruit_banana", 30)!.Value.Shape);
    }

    [Fact]
    public void TintForShape_DiffersPerFruitAndPerWorld()
    {
        var keys = FloraCatalog.FruitKeys();
        Assert.Equal(keys.Count, keys.Select(k => FruitRules.TintForShape(11, k)).Distinct().Count());
        Assert.NotEqual(FruitRules.TintForShape(11, keys[0]), FruitRules.TintForShape(12, keys[0]));
    }

    [Fact]
    public void Generation30_CleanFruitItems_ListEveryActiveCleanFruit()
    {
        foreach (var planet in TreePlanets())
        {
            for (long seed = 1; seed <= 6; seed++)
            {
                if (TreeGenerator.Generate(planet, seed) is not { Toxic: false })
                {
                    continue;
                }

                var items = FruitRules.CleanFruitItems(planet, seed, 30);
                var expected = ActiveFruit(planet, seed, 30).Select(FruitRules.ItemFor).ToList();
                if (items.Count > 0)
                {
                    Assert.Equal(expected, items);
                }
            }
        }
    }
}
