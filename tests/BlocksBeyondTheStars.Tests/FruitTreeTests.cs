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
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The fruit trees (#2038, terrain generation 14): four fruit shapes hang under the crowns, the shape and colour rolled
/// per tree kind and world, toxic exactly when the world's tree species is; the stamp, the harvest with its toxic swap,
/// the regrow in colour (also across a restart), a sapling's tree, the scan of needles and fronds.
/// </summary>
public sealed class FruitTreeTests : IDisposable
{
    private const int Gen = WorldDescription.FruitTreesGeneration;
    private static readonly string[] FruitKeys = { "flora_fruit_round", "flora_fruit_long", "flora_fruit_grape", "flora_fruit_banana" };
    private static readonly string[] Foliage = { "tree_leaves", "pine_needles", "palm_frond" };

    private readonly string _root;
    private readonly GameContent _content;

    public FruitTreeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts-fruit-" + Guid.NewGuid().ToString("N"));
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
        }
    }

    private SvGameServer Started(out SqliteWorldRepository repo, string name, int seed = 7)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = seed,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            StartPlanet = "jungle", // a tree world; a new save is the current generation
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void TheWave_IsGenerationFourteen_WithFourHangingFruitSpecies_AndTheirItems()
    {
        Assert.Equal(14, Gen);
        Assert.True(WorldDescription.CurrentTerrainGeneration >= Gen);
        Assert.Equal(FruitKeys, FloraCatalog.FruitKeys());
        foreach (var key in FruitKeys)
        {
            var sp = FloraCatalog.Find(key);
            Assert.NotNull(sp);
            Assert.True(sp!.Fruit && sp.Hanging && !sp.Solid && !sp.Cultivated, key);
            Assert.Equal(Gen, sp.MinGeneration);
            Assert.Equal(Foliage, sp.Hosts);
            Assert.True(FloraCatalog.IsFruit(key) && FloraCatalog.IsHanging(key));

            var block = _content.GetBlock(key);
            Assert.NotNull(block);
            var drop = Assert.Single(block!.Drops);
            var item = _content.GetItem(drop.Item);
            Assert.NotNull(item);
            Assert.True(item!.ConsumeHunger > 0 && item.ConsumeHealth >= 0, $"{drop.Item} is food");
            Assert.True(HerdRules.IsFoodForCreatures(item), "a begging herd comes for fruit");
            var toxic = _content.GetItem("toxic_" + drop.Item);
            Assert.NotNull(toxic);
            Assert.True(toxic!.ConsumeHealth < 0, "the toxic twin hurts");
            Assert.False(HerdRules.IsFoodForCreatures(toxic));
        }

        // A fruit's hosts are foliage: a leaf is no ground to plant on, so the leaves stay off the FloraHost set.
        foreach (var f in Foliage)
        {
            Assert.False(_content.GetBlock(f)!.FloraHost, f);
        }
    }

    [Fact]
    public void Rules_RollShapeAndColourPerTreeKind_AndStayInTheirBands()
    {
        var keys = FloraCatalog.FruitKeys();
        var bearing = new[] { TreeKind.Broadleaf, TreeKind.Conifer, TreeKind.Palm, TreeKind.Jungle, TreeKind.Baobab, TreeKind.Mangrove, TreeKind.Willow };
        var barren = new[] { TreeKind.None, TreeKind.Dead, TreeKind.Bamboo, TreeKind.Saguaro, TreeKind.MushroomTree, TreeKind.CrystalTree };
        foreach (var k in bearing)
        {
            Assert.True(FruitRules.BearsFruit(k), k.ToString());
        }

        foreach (var k in barren)
        {
            Assert.False(FruitRules.BearsFruit(k), k.ToString());
            Assert.Null(FruitRules.ShapeFor(123, k, keys));
        }

        Assert.Null(FruitRules.ShapeFor(123, TreeKind.Broadleaf, Array.Empty<string>()));
        Assert.Equal("flora_fruit_grape", FruitRules.ShapeFor(5, TreeKind.Conifer, new[] { "flora_fruit_grape" })); // one shape: every kind bears it

        var tints = new HashSet<int>();
        int differs = 0;
        for (int seed = 1; seed <= 60; seed++)
        {
            var shape = FruitRules.ShapeFor(seed, TreeKind.Palm, keys);
            Assert.NotNull(shape);
            Assert.Contains(shape, keys);
            Assert.Equal(shape, FruitRules.ShapeFor(seed, TreeKind.Palm, keys));

            int tint = FruitRules.TintFor(seed, TreeKind.Broadleaf);
            Assert.Equal(tint, FruitRules.TintFor(seed, TreeKind.Broadleaf));
            tints.Add(tint);
            if (tint != FruitRules.TintFor(seed, TreeKind.Conifer))
            {
                differs++;
            }

            int r = (tint >> 16) & 0xFF, g = (tint >> 8) & 0xFF, b = tint & 0xFF;
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            Assert.InRange(max, 216, 255);                               // value 0.85..1.0 — ripe, never dark
            Assert.True(min <= max * 0.56 + 1, $"tint {tint:X6} is grey"); // saturation >= 0.45
        }

        Assert.True(tints.Count >= 50, $"colours vary across worlds ({tints.Count}/60)");
        Assert.True(differs >= 55, $"two kinds of one world differ ({differs}/60)");
    }

    [Fact]
    public void Rules_HangFruitUnderTheLowestLeaves_TwoToFive_Deterministically()
    {
        // A little crown: two 3×3 leaf layers over a two-log trunk. Fruit may only hang under the lower layer — never
        // under the trunk cell and never under the upper layer, where a leaf sits.
        var leaves = new List<(int X, int Y, int Z)>();
        var cells = new HashSet<(int X, int Y, int Z)> { (0, 9, 0), (0, 8, 0) };
        for (int y = 10; y <= 11; y++)
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    leaves.Add((dx, y, dz));
                    cells.Add((dx, y, dz));
                }

        var picks = FruitRules.PickFruitCells(leaves, cells, 0xABCDEF12345UL);
        Assert.InRange(picks.Count, FruitRules.MinFruitPerTree, FruitRules.MaxFruitPerTree);
        Assert.Equal(picks, FruitRules.PickFruitCells(leaves, cells, 0xABCDEF12345UL));
        Assert.Equal(picks.Count, picks.Distinct().Count());
        foreach (var (x, y, z) in picks)
        {
            Assert.Equal(9, y);
            Assert.False(x == 0 && z == 0, "never under the trunk");
            Assert.InRange(Math.Abs(x), 0, 1);
            Assert.InRange(Math.Abs(z), 0, 1);
        }

        var counts = new HashSet<int>();
        for (ulong h = 1; h <= 40; h++)
        {
            counts.Add(FruitRules.PickFruitCells(leaves, cells, unchecked(h * 0x9E3779B97F4A7C15UL)).Count);
        }

        Assert.True(counts.Count >= 3, "the count varies from tree to tree");
        Assert.Empty(FruitRules.PickFruitCells(new List<(int X, int Y, int Z)>(), cells, 1));

        int bearing = 0;
        for (ulong h = 1; h <= 2000; h++)
        {
            if (FruitRules.TreeBearsFruit(unchecked(h * 0x9E3779B97F4A7C15UL)))
            {
                bearing++;
            }
        }

        Assert.InRange(bearing, 550, 850); // about a third of the trees
    }

    [Fact]
    public void Roster_FruitSpecies_InheritTheTreeToxicFlag_AndStayInactiveBeforeTheWave()
    {
        int activeFruit = 0;
        foreach (var planetKey in new[] { "jungle", "boreal", "desert", "varied" })
        {
            var planet = _content.GetPlanet(planetKey)!;
            for (int seed = 1; seed <= 25; seed++)
            {
                long rosterSeed = WorldGenerator.RosterSeedFor(seed, planetKey + "-" + seed);
                var tree = TreeGenerator.Generate(planet, rosterSeed);
                Assert.NotNull(tree);
                var now = FloraGenerator.GenerateRoster(planet, rosterSeed, Gen);
                var before = FloraGenerator.GenerateRoster(planet, rosterSeed, Gen - 1);
                Assert.Equal(before.Count, now.Count);
                for (int i = 0; i < now.Count; i++)
                {
                    var a = now[i];
                    var b = before[i];
                    Assert.Equal(b.BlockKey, a.BlockKey);
                    Assert.Equal(b.Id, a.Id);
                    Assert.Equal(b.Name, a.Name);
                    if (FloraCatalog.IsFruit(a.BlockKey))
                    {
                        Assert.Equal(tree!.Toxic, a.Toxic); // one identity with the tree
                        Assert.False(b.Active, "a generation-13 world grows no fruit");
                        if (a.Active)
                        {
                            activeFruit++;
                        }
                    }
                    else
                    {
                        Assert.Equal(b.Toxic, a.Toxic);   // every older species keeps its roll
                        Assert.Equal(b.Active, a.Active);
                    }
                }
            }
        }

        Assert.True(activeFruit > 0, "some world activates some fruit");
    }

    private readonly record struct AreaScan(int Foliage, int Fruit, HashSet<int> Tints, int Under, int Checked, HashSet<ushort> Above);

    /// <summary>Generates a <paramref name="chunksXZ"/>² chunk area from chunk (<paramref name="cx0"/>, <paramref name="cz0"/>)
    /// and counts foliage and fruit cells, the fruit tints, and what stands above each fruit. The rows are found by
    /// sampling the surface: the relief of generation ≥ 1 is reshaped, so BaseHeight ± Amplitude does not bound it.</summary>
    private AreaScan ScanArea(WorldGenerator gen, PlanetType planet, int cx0, int cz0, int chunksXZ)
    {
        int cs = WorldConstants.ChunkSize;
        int min = int.MaxValue, max = int.MinValue;
        for (int x = cx0 * cs; x < (cx0 + chunksXZ) * cs; x += 4)
            for (int z = cz0 * cs; z < (cz0 + chunksXZ) * cs; z += 4)
            {
                int sy = gen.SurfaceHeight(planet, x, z);
                min = Math.Min(min, sy);
                max = Math.Max(max, sy);
            }

        int cyLo = Math.Max(0, (min - 4) / cs), cyHi = (max + 40) / cs + 1;
        var fruitIds = FruitKeys.Select(k => _content.GetBlock(k)!.NumericId.Value).ToHashSet();
        var foliageIds = Foliage.Select(k => _content.GetBlock(k)!.NumericId.Value).ToHashSet();
        int foliage = 0, fruit = 0, under = 0, checkedCells = 0;
        var tints = new HashSet<int>();
        var above = new HashSet<ushort>();
        for (int cx = cx0; cx < cx0 + chunksXZ; cx++)
            for (int cz = cz0; cz < cz0 + chunksXZ; cz++)
                for (int cy = cyLo; cy <= cyHi; cy++)
                {
                    var chunk = gen.Generate(planet, WorldConstants.WorldToChunk(new Vector3i(cx * cs, cy * cs, cz * cs)));
                    for (int lx = 0; lx < cs; lx++)
                        for (int ly = 0; ly < cs; ly++)
                            for (int lz = 0; lz < cs; lz++)
                            {
                                ushort id = chunk.Get(lx, ly, lz).Value;
                                if (foliageIds.Contains(id))
                                {
                                    foliage++;
                                }

                                if (!fruitIds.Contains(id))
                                {
                                    continue;
                                }

                                fruit++;
                                tints.Add(chunk.GetModifier(lx, ly, lz).Tint);
                                if (ly + 1 < cs)
                                {
                                    checkedCells++;
                                    ushort up = chunk.Get(lx, ly + 1, lz).Value;
                                    above.Add(up);
                                    if (foliageIds.Contains(up))
                                    {
                                        under++;
                                    }
                                }
                            }
                }

        return new AreaScan(foliage, fruit, tints, under, checkedCells, above);
    }

    /// <summary>A generation-14 generator for a seed whose world activates at least one fruit shape (each shape is a
    /// 40–85 % roll) AND whose origin area is wooded — a seed can roll a frozen or barren world, or put the origin in a
    /// polar zone — together with that area's scan.</summary>
    private (WorldGenerator Gen, int Seed, AreaScan Scan) WoodedWorld(PlanetType planet, int chunksXZ)
    {
        for (int seed = 2026; seed < 2066; seed++)
        {
            var gen = new WorldGenerator(seed, _content);
            gen.SetTerrainGeneration(Gen);
            if (!FloraGenerator.GenerateRoster(planet, gen.RosterSeed, Gen).Any(f => f.Active && FloraCatalog.IsFruit(f.BlockKey)))
            {
                continue;
            }

            var scan = ScanArea(gen, planet, 0, 0, chunksXZ);
            if (scan.Foliage >= 300)
            {
                return (gen, seed, scan);
            }
        }

        throw new Xunit.Sdk.XunitException($"no seed with a wooded origin found on {planet.Key}");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Worldgen_HangsFruitUnderTheCrowns_OnGenerationFourteen_AndNoneBefore()
    {
        var planet = _content.GetPlanet("jungle")!;
        var (_, seed, scan) = WoodedWorld(planet, 5);
        Assert.True(scan.Fruit > 0, $"a jungle wood of generation 14 hangs fruit under its crowns ({scan.Foliage} foliage cells, none with fruit)");
        Assert.DoesNotContain(0, scan.Tints);        // every fruit carries its kind's colour
        Assert.InRange(scan.Tints.Count, 1, 7);      // one colour per tree kind
        // Every fruit hangs under foliage — bar the rare one whose leaf a later tree's trunk overwrote.
        Assert.True(scan.Under >= scan.Checked * 0.95, $"{scan.Under} of {scan.Checked} fruit hang under foliage");

        var gen13 = new WorldGenerator(seed, _content);
        gen13.SetTerrainGeneration(Gen - 1);
        var before = ScanArea(gen13, planet, 0, 0, 5);
        Assert.Equal(0, before.Fruit);
        // The same wood without the fruit: a fruit cell can only take a leaf that a NEIGHBOURING crown would have
        // written into that air later, so generation 13 has at most as many extra leaves as there are fruit.
        Assert.InRange(before.Foliage - scan.Foliage, 0, scan.Fruit);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Worldgen_ConiferWorld_HangsOneColourUnderItsNeedles()
    {
        var planet = _content.GetPlanet("highland")!; // the alpine theme: conifers only
        var (_, _, scan) = WoodedWorld(planet, 6);
        Assert.True(scan.Fruit > 0, $"a conifer wood of generation 14 hangs fruit under its needles ({scan.Foliage} foliage cells, none with fruit)");
        Assert.Single(scan.Tints);                   // one kind → one colour
        Assert.True(scan.Under >= scan.Checked * 0.95, $"{scan.Under} of {scan.Checked} fruit hang under needles");
        Assert.Contains(_content.GetBlock("pine_needles")!.NumericId.Value, scan.Above);
    }

    [Fact]
    public void Harvest_SwapsToTheToxicTwin_WhenTheTreeIsToxic_AndTheFruitRegrowsInItsColour()
    {
        var server = Started(out var repo, "fruit-harvest");
        using (repo)
        {
            var leaves = _content.GetBlock("tree_leaves")!.NumericId;
            var fruit = _content.GetBlock("flora_fruit_round")!.NumericId;
            var tree = server.TreeSpeciesForBlock("wood_log");
            var sp = server.FloraSpeciesForBlock("flora_fruit_round");
            Assert.NotNull(tree);
            Assert.NotNull(sp);
            Assert.Equal(tree!.Toxic, sp!.Toxic);
            Assert.Equal(tree.Name, server.TreeSpeciesForBlock("pine_needles")?.Name); // needles and fronds scan as the tree
            Assert.Equal(tree.Name, server.TreeSpeciesForBlock("palm_frond")?.Name);

            var p = server.AddLocalPlayer("Picker");
            p.State.Position = new Vector3f(0.5f, 132f, 0.5f);
            var leafPos = new Vector3i(0, 131, 0);
            var pos = new Vector3i(0, 130, 0);
            server.World.SetBlock(leafPos, leaves);
            server.World.SetBlock(pos, fruit, tint: 0x3355AA);
            Assert.Equal(0x3355AA, server.World.GetModifier(pos).Tint);

            server.MineBlock("Picker", pos.X, pos.Y, pos.Z);
            Assert.True(server.World.GetBlock(pos).IsAir);
            string expected = sp.Toxic ? "toxic_fruit_round" : "fruit_round";
            string other = sp.Toxic ? "fruit_round" : "toxic_fruit_round";
            Assert.True(p.State.Inventory.CountOf(expected) > 0, $"the harvest yields {expected}");
            Assert.Equal(0, p.State.Inventory.CountOf(other));

            server.Tick(60.0);
            Assert.True(server.World.GetBlock(pos).IsAir, "a fruit takes longer to ripen than a tuft of grass");
            server.Tick(70.0);
            Assert.Equal(fruit.Value, server.World.GetBlock(pos).Value);
            Assert.Equal(0x3355AA, server.World.GetModifier(pos).Tint);
        }
    }

    [Fact]
    public void Regrow_KeepsTheColour_AcrossARestart()
    {
        var leaves = _content.GetBlock("tree_leaves")!.NumericId;
        var fruit = _content.GetBlock("flora_fruit_banana")!.NumericId;
        var leafPos = new Vector3i(40, 131, 40);
        var pos = new Vector3i(40, 130, 40);

        var server1 = Started(out var repo1, "fruit-restart");
        var s1 = server1.AddLocalPlayer("Picker");
        s1.State.Position = new Vector3f(40.5f, 132f, 40.5f);
        server1.World.SetBlock(leafPos, leaves);
        server1.World.SetBlock(pos, fruit, tint: 0x66CC22);
        server1.MineBlock(s1.State.PlayerId, pos.X, pos.Y, pos.Z);
        server1.Tick(1.0);
        Assert.True(server1.World.GetBlock(pos).IsAir);
        var row = Assert.Single(repo1.ListFloraRegrow(server1.World.LocationId), r => r.WorldPosition.Equals(pos));
        Assert.Equal(0x66CC22, row.Tint);
        repo1.Dispose(); // a restart on the same save

        var server2 = Started(out var repo2, "fruit-restart");
        using (repo2)
        {
            server2.AddLocalPlayer("Picker");
            server2.Tick(130.0);
            Assert.Equal(fruit.Value, server2.World.GetBlock(pos).Value);
            Assert.Equal(0x66CC22, server2.World.GetModifier(pos).Tint);
        }
    }

    [Fact]
    public void Sapling_BearsFruit_OnAGenerationFourteenWorld()
    {
        // A seed whose world rolled a shape for the broadleaf kind (each shape activates with 40–85 %).
        SvGameServer? server = null;
        SqliteWorldRepository? repo = null;
        for (int seed = 1; seed <= 12 && server == null; seed++)
        {
            var s = Started(out var r, "fruit-sapling-" + seed, seed);
            if (s.FruitShapeForTest(TreeKind.Broadleaf) != null)
            {
                server = s;
                repo = r;
            }
            else
            {
                r.Dispose();
            }
        }

        Assert.NotNull(server);
        using (repo)
        {
            var dirt = _content.GetBlock("dirt")!.NumericId;
            var host = new Vector3i(50, 49, 50);
            var cell = new Vector3i(50, 50, 50);
            server!.World.SetBlock(host, dirt);
            for (int y = 50; y <= 58; y++)
                for (int dx = -3; dx <= 3; dx++)
                    for (int dz = -3; dz <= 3; dz++)
                    {
                        server.World.SetBlock(new Vector3i(50 + dx, y, 50 + dz), BlockId.Air);
                    }

            var session = server.AddLocalPlayer("Farmer");
            session.State.Inventory.Add("sapling", 4, 99);
            session.State.Position = new Vector3f(51.5f, 50f, 51.5f);
            server.PlaceBlock("Farmer", cell.X, cell.Y, cell.Z, "sapling");
            server.Tick(60.0);
            server.Tick(100.0); // > SaplingGrowSeconds
            Assert.Equal(_content.GetBlock("wood_log")!.NumericId.Value, server.World.GetBlock(cell).Value);

            var fruitId = _content.GetBlock(server.FruitShapeForTest(TreeKind.Broadleaf)!)!.NumericId.Value;
            int tint = server.FruitTintForTest(TreeKind.Broadleaf);
            var leaf = _content.GetBlock("tree_leaves")!.NumericId.Value;
            int n = 0;
            for (int dx = -3; dx <= 3; dx++)
                for (int dy = 0; dy <= 8; dy++)
                    for (int dz = -3; dz <= 3; dz++)
                    {
                        var c = new Vector3i(cell.X + dx, cell.Y + dy, cell.Z + dz);
                        if (server.World.GetBlock(c).Value != fruitId)
                        {
                            continue;
                        }

                        n++;
                        Assert.Equal(tint, server.World.GetModifier(c).Tint);
                        Assert.Equal(leaf, server.World.GetBlock(new Vector3i(c.X, c.Y + 1, c.Z)).Value);
                    }

            Assert.InRange(n, FruitRules.MinFruitPerTree, FruitRules.MaxFruitPerTree);
        }
    }

    [Fact]
    public void Persistence_RegrowRow_RoundTripsTheTint()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "fruit-rows"));
        repo.Initialize();
        var pos = new Vector3i(3, 4, 5);
        repo.SaveFloraRegrow("p", pos, 77, 12.5, 0xABCDEF);
        var row = Assert.Single(repo.ListFloraRegrow("p"));
        Assert.Equal((ushort)77, row.Block);
        Assert.Equal(12.5, row.Timer);
        Assert.Equal(0xABCDEF, row.Tint);
        Assert.Equal(pos, row.WorldPosition);

        repo.SaveFloraRegrow("p", pos, 77, 3.0); // a plain plant: no colour
        Assert.Equal(0, Assert.Single(repo.ListFloraRegrow("p")).Tint);
    }
}
