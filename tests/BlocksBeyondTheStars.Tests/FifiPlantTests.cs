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
using BlocksBeyondTheStars.Shared.Textures;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The Fifi plant (#2085, terrain generation 16) — Sophie's tree-sized plant from the school club: a green trunk, yellow
/// leaves, glowing pink blossoms that light their surroundings, and berries that always grow back, standing in groves on every
/// world with plant life. The authored berry species, the grove and shape rules, the stamp (and nothing before generation 16),
/// the harvest with its 120 s regrow, the scan.
/// </summary>
public sealed class FifiPlantTests : IDisposable
{
    private const int Gen = WorldDescription.FifiPlantGeneration;

    private readonly string _root;
    private readonly GameContent _content;

    public FifiPlantTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts-fifi-" + Guid.NewGuid().ToString("N"));
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

    private SvGameServer Started(out SqliteWorldRepository repo, string name)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 7,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            StartPlanet = "meadowlands", // a new save is the current generation
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    // --- Content ---

    [Fact]
    public void TheWave_IsGenerationSixteen_AndTheBerriesAreAnAuthoredHangingFruit()
    {
        Assert.Equal(16, Gen);
        Assert.True(WorldDescription.CurrentTerrainGeneration >= Gen);

        var sp = FloraCatalog.Find(FifiPlant.BerriesKey);
        Assert.NotNull(sp);
        Assert.True(sp!.Authored && sp.Hanging && sp.Fruit && !sp.Cultivated && !sp.Solid);
        Assert.Equal(Gen, sp.MinGeneration);
        Assert.Equal(new[] { FifiPlant.LeafKey }, sp.Hosts);
        Assert.True(FloraCatalog.IsAuthored(FifiPlant.BerriesKey));
        Assert.True(FloraCatalog.KeepsOwnColour(FifiPlant.BerriesKey)); // no world tint: Sophie's colours
        Assert.True(FloraCatalog.IsFruit(FifiPlant.BerriesKey));          // ripens again slowly, like the tree fruit
        Assert.DoesNotContain(FifiPlant.BerriesKey, FloraCatalog.FruitKeys()); // but it is no TREE fruit shape
        Assert.DoesNotContain(FifiPlant.BerriesKey, FloraCatalog.CultivatedKeys()); // and no greenhouse grows it

        // Appended after the fruit trees and before the crops: every older species keeps its roster id.
        var keys = FloraCatalog.All.Select(s => s.Key).ToList();
        Assert.Equal(keys.IndexOf("flora_fruit_banana") + 1, keys.IndexOf(FifiPlant.BerriesKey));
        Assert.All(FloraCatalog.All.Skip(keys.IndexOf(FifiPlant.BerriesKey) + 1), s => Assert.True(s.Cultivated, s.Key));
    }

    [Fact]
    public void TheBlocks_AreSophiesPlant_TheBlossomLightsUp_AndTheBerriesAreNormalBerries()
    {
        foreach (var key in FifiPlant.PartKeys)
        {
            Assert.NotNull(_content.GetBlock(key));
        }

        foreach (var key in new[] { FifiPlant.StemKey, FifiPlant.LeafKey, FifiPlant.BlossomKey })
        {
            Assert.Equal(key, _content.GetItem(key)?.PlacesBlock); // the parts are placeable (like the Paul flower's)
            Assert.Contains(_content.GetBlock(key)!.Drops, d => d.Item == key);
        }

        var berries = _content.GetBlock(FifiPlant.BerriesKey)!;
        Assert.Equal("berries", Assert.Single(berries.Drops).Item);

        var blossom = _content.GetBlock(FifiPlant.BlossomKey)!;
        Assert.NotEqual(0, BlockLight.NaturalColorOf(blossom)); // it lights up its surroundings
        Assert.True((blossom.Emission ?? 0f) > 0.5f);            // and glows itself
        foreach (var key in new[] { FifiPlant.StemKey, FifiPlant.LeafKey, FifiPlant.BerriesKey })
        {
            Assert.Equal(0, BlockLight.NaturalColorOf(_content.GetBlock(key)));
        }

        Assert.Equal(TextureAlphaMode.Cutout, TextureTiles.AlphaModeOf(FifiPlant.LeafKey)); // a crown with leaf gaps
        Assert.Equal(TextureAlphaMode.Cutout, TextureTiles.AlphaModeOf(FifiPlant.BerriesKey));
        Assert.Equal(TextureAlphaMode.Opaque, TextureTiles.AlphaModeOf(FifiPlant.StemKey));
        Assert.Equal(TextureAlphaMode.Opaque, TextureTiles.AlphaModeOf(FifiPlant.BlossomKey));
    }

    [Fact]
    public void NoRoster_EverHoldsTheBerries_SoTheyAreNeverToxic()
    {
        foreach (var planet in _content.Planets.Values)
        {
            foreach (long seed in new long[] { 1, 7, 2026 })
            {
                var roster = FloraGenerator.GenerateRoster(planet, seed, Gen);
                Assert.DoesNotContain(roster, f => f.BlockKey == FifiPlant.BerriesKey);
                foreach (var f in roster)
                {
                    // The id is the catalog index: the berries (and the crops) behind every wild species move nothing.
                    Assert.Equal("fl" + FloraCatalog.All.ToList().FindIndex(s => s.Key == f.BlockKey), f.Id);
                }
            }
        }
    }

    // --- Rules ---

    [Fact]
    public void Groves_AreGroupsOfPlants_ThatStandApart_InsideTheirCell()
    {
        int groves = 0, plants = 0;
        for (int cx = 0; cx < 40; cx++)
            for (int cz = 0; cz < 40; cz++)
            {
                var members = FifiPlantRules.GroveMembers(20260927, cx, cz, FifiPlantRules.GroveCell, FifiPlantRules.GroveCell);
                if (members.Count == 0)
                {
                    continue;
                }

                groves++;
                plants += members.Count;
                Assert.InRange(members.Count, 2, FifiPlantRules.MaxGroup + 1);
                foreach (var (x, z) in members)
                {
                    Assert.InRange(x, 0, FifiPlantRules.GroveCell - 1);
                    Assert.InRange(z, 0, FifiPlantRules.GroveCell - 1);
                }

                for (int i = 0; i < members.Count; i++)
                    for (int j = i + 1; j < members.Count; j++)
                    {
                        int dx = members[i].X - members[j].X, dz = members[i].Z - members[j].Z;
                        Assert.True(dx * dx + dz * dz >= FifiPlantRules.MinSpacing * FifiPlantRules.MinSpacing, "two trunks crowd each other");
                    }
            }

        // About a third of the cells hold a grove, and a grove is a group, not a lone plant.
        Assert.InRange(groves, 1600 * 0.25, 1600 * 0.45);
        Assert.True(plants >= groves * 3, $"{plants} plants in {groves} groves");
    }

    [Fact]
    public void GroveMembership_IsTheSame_AcrossTheWrapSeams()
    {
        const int circ = 2048;
        int period = WorldConstants.LatitudePeriodFor(circ);
        int members = 0;
        for (int x = -60; x < 60; x++)
            for (int z = -60; z < 60; z++)
            {
                bool here = FifiPlantRules.IsGroveMember(99, x, z, circ);
                Assert.Equal(here, FifiPlantRules.IsGroveMember(99, x + circ, z, circ));
                Assert.Equal(here, FifiPlantRules.IsGroveMember(99, x, z + period, circ));
                members += here ? 1 : 0;
            }

        Assert.True(members > 0, "no grove near the origin to compare across the seams");

        // A cell cut short by the seam holds a grove only where the whole ring fits — never one that reaches over it.
        var last = FifiPlantRules.CellOf(489, 0, 490); // 490 = ten whole cells and a 10-wide rest
        Assert.Equal(10, last.Width);
        for (long seed = 0; seed < 200; seed++)
        {
            Assert.Empty(FifiPlantRules.GroveMembers(seed, last.CellX, last.CellZ, last.Width, last.Depth));
        }
    }

    [Fact]
    public void TheShape_IsTreeSized_WithATrunk_ACrown_BlossomsOnTop_AndBerriesUnderTheLeaves()
    {
        for (ulong h = 1; h < 400; h += 7)
        {
            foreach (double size in new[] { 0.76, 0.95, 1.0, 1.24 })
            {
                var s = FifiPlantRules.ShapeFor(h * 0x9E3779B97F4A7C15UL, size);
                Assert.InRange(s.Top + 1, 7, FifiPlantRules.MaxRise - 1); // as tall as a tree, inside the stamp's rise
                Assert.Equal(Enumerable.Range(0, s.Trunk.Count).Select(y => (0, y, 0)), s.Trunk); // one straight trunk from the root

                var leaves = s.Leaves.ToHashSet();
                var blossoms = s.Blossoms.ToHashSet();
                var trunk = s.Trunk.ToHashSet();
                Assert.Empty(leaves.Intersect(blossoms));
                Assert.Empty(leaves.Intersect(trunk));
                Assert.All(leaves.Concat(blossoms), c =>
                {
                    Assert.InRange(c.X, -FifiPlantRules.CrownRadiusMax, FifiPlantRules.CrownRadiusMax);
                    Assert.InRange(c.Z, -FifiPlantRules.CrownRadiusMax, FifiPlantRules.CrownRadiusMax);
                });

                Assert.Contains((0, s.Top, 0), blossoms); // the crown flower on the very top
                Assert.InRange(s.Blossoms.Count, FifiPlantRules.MinBlossoms, FifiPlantRules.MaxBlossoms);
                foreach (var (x, y, z) in s.Blossoms)
                {
                    // never on the underside — that is where the berries hang
                    Assert.True(leaves.Contains((x, y - 1, z)) || blossoms.Contains((x, y - 1, z)) || trunk.Contains((x, y - 1, z)));
                }

                Assert.InRange(s.Berries.Count, FifiPlantRules.MinBerries, FifiPlantRules.MaxBerries);
                foreach (var (x, y, z) in s.Berries)
                {
                    Assert.Contains((x, y + 1, z), leaves); // every berry hangs from a leaf
                    Assert.DoesNotContain((x, y, z), trunk);
                    Assert.DoesNotContain((x, y, z), leaves);
                }
            }
        }
    }

    // --- World generation ---

    [Fact]
    public void Worldgen_GrowsGroves_OnGenerationSixteen_AndNoneBefore()
    {
        var planet = _content.GetPlanet("meadowlands")!;
        var gen16 = new WorldGenerator(7, _content);
        gen16.SetTerrainGeneration(Gen);
        var plants = gen16.FifiPlantsForTest(planet, -192, -192, 384);
        Assert.True(plants.Count >= 10, $"only {plants.Count} Fifi plants on a generation-16 meadow world");

        // Groups: most plants have another within a grove's reach.
        int grouped = plants.Count(p => plants.Any(q => q != p && Math.Abs(q.X - p.X) <= 16 && Math.Abs(q.Z - p.Z) <= 16));
        Assert.True(grouped >= plants.Count * 0.6, $"{grouped} of {plants.Count} plants stand in a group");

        var gen15 = new WorldGenerator(7, _content);
        gen15.SetTerrainGeneration(Gen - 1);
        Assert.Empty(gen15.FifiPlantsForTest(planet, -192, -192, 384));
        var p0 = plants[0];
        var chunk15 = gen15.Generate(planet, new ChunkCoord(WorldConstants.WorldToChunk(p0.X), WorldConstants.WorldToChunk(p0.SurfaceY + 3), WorldConstants.WorldToChunk(p0.Z)));
        var fifiIds = FifiPlant.PartKeys.Select(k => _content.GetBlock(k)!.NumericId.Value).ToHashSet();
        Assert.DoesNotContain(Cells(chunk15), id => fifiIds.Contains(id));
    }

    [Fact]
    public void Worldgen_BuildsEveryPlant_AcrossChunkEdges()
    {
        var planet = _content.GetPlanet("meadowlands")!;
        var gen = new WorldGenerator(7, _content);
        gen.SetTerrainGeneration(Gen);
        var plants = gen.FifiPlantsForTest(planet, -192, -192, 384);
        var stem = _content.GetBlock(FifiPlant.StemKey)!.NumericId.Value;
        var leaf = _content.GetBlock(FifiPlant.LeafKey)!.NumericId.Value;
        var blossom = _content.GetBlock(FifiPlant.BlossomKey)!.NumericId.Value;
        var berries = _content.GetBlock(FifiPlant.BerriesKey)!.NumericId.Value;
        var chunks = new Dictionary<ChunkCoord, ChunkData>();
        int cs = WorldConstants.ChunkSize;

        ushort At(int x, int y, int z)
        {
            var c = new ChunkCoord(WorldConstants.WorldToChunk(x), WorldConstants.WorldToChunk(y), WorldConstants.WorldToChunk(z));
            if (!chunks.TryGetValue(c, out var chunk))
            {
                chunk = gen.Generate(planet, c);
                chunks[c] = chunk;
            }

            return chunk.Get(x - c.X * cs, y - c.Y * cs, z - c.Z * cs).Value;
        }

        int straddling = 0, expected = 0, matched = 0, berriesFound = 0;
        foreach (var p in plants.Take(12))
        {
            int rootY = p.SurfaceY + 1;
            var cells = p.Shape.Trunk.Select(c => (c, stem)).Concat(p.Shape.Leaves.Select(c => (c, leaf)))
                .Concat(p.Shape.Blossoms.Select(c => (c, blossom))).ToList();
            var touched = new HashSet<ChunkCoord>();
            foreach (var ((x, y, z), id) in cells)
            {
                touched.Add(new ChunkCoord(WorldConstants.WorldToChunk(p.X + x), WorldConstants.WorldToChunk(rootY + y), WorldConstants.WorldToChunk(p.Z + z)));
                expected++;
                matched += At(p.X + x, rootY + y, p.Z + z) == id ? 1 : 0;
            }

            straddling += touched.Count > 1 ? 1 : 0;
            Assert.Equal(stem, At(p.X, rootY, p.Z)); // the trunk always stands
            foreach (var (x, y, z) in p.Shape.Berries)
            {
                if (At(p.X + x, rootY + y, p.Z + z) == berries)
                {
                    berriesFound++;
                    Assert.Equal(leaf, At(p.X + x, rootY + y + 1, p.Z + z)); // hanging from a Fifi leaf
                }
            }
        }

        Assert.True(straddling > 0, "no checked plant crosses a chunk edge — the seam case went untested");
        Assert.True(matched >= expected * 0.9, $"{matched} of {expected} plant cells as built"); // a neighbouring tree may own a cell
        Assert.True(berriesFound > 0, "the plants bear berries");
    }

    private static IEnumerable<ushort> Cells(ChunkData chunk)
    {
        int cs = WorldConstants.ChunkSize;
        for (int x = 0; x < cs; x++)
            for (int y = 0; y < cs; y++)
                for (int z = 0; z < cs; z++)
                {
                    yield return chunk.Get(x, y, z).Value;
                }
    }

    // --- Server ---

    [Fact]
    public void Harvest_GivesNormalBerries_AndTheyGrowBackAfterTwoMinutes_UnderTheirLeaf()
    {
        var server = Started(out var repo, "fifi-harvest");
        using (repo)
        {
            var leaf = _content.GetBlock(FifiPlant.LeafKey)!.NumericId;
            var berries = _content.GetBlock(FifiPlant.BerriesKey)!.NumericId;
            Assert.Null(server.FloraSpeciesForBlock(FifiPlant.BerriesKey)); // authored: in no roster, never toxic

            var p = server.AddLocalPlayer("Picker");
            p.State.Position = new Vector3f(0.5f, 132f, 0.5f);
            var leafPos = new Vector3i(0, 131, 0);
            var pos = new Vector3i(0, 130, 0);
            server.World.SetBlock(leafPos, leaf);
            server.World.SetBlock(pos, berries);

            server.MineBlock("Picker", pos.X, pos.Y, pos.Z);
            Assert.True(server.World.GetBlock(pos).IsAir);
            Assert.True(p.State.Inventory.CountOf("berries") >= 2, "a Fifi harvest yields normal berries");
            Assert.Equal(0, p.State.Inventory.CountOf("toxic_berries"));

            server.Tick(60.0);
            Assert.True(server.World.GetBlock(pos).IsAir, "berries take two minutes to grow back");
            server.Tick(70.0);
            Assert.Equal(berries.Value, server.World.GetBlock(pos).Value);
        }
    }

    [Fact]
    public void Berries_DoNotGrowBack_WhenTheirLeafIsGone()
    {
        var server = Started(out var repo, "fifi-noleaf");
        using (repo)
        {
            var leaf = _content.GetBlock(FifiPlant.LeafKey)!.NumericId;
            var berries = _content.GetBlock(FifiPlant.BerriesKey)!.NumericId;
            var p = server.AddLocalPlayer("Picker");
            p.State.Position = new Vector3f(20.5f, 132f, 20.5f);
            var leafPos = new Vector3i(20, 131, 20);
            var pos = new Vector3i(20, 130, 20);
            server.World.SetBlock(leafPos, leaf);
            server.World.SetBlock(pos, berries);

            server.MineBlock("Picker", pos.X, pos.Y, pos.Z);
            server.World.SetBlock(leafPos, BlockId.Air);
            server.Tick(130.0);
            Assert.True(server.World.GetBlock(pos).IsAir, "no leaf, no berries");
        }
    }

    [Fact]
    public void Scan_ReadsEveryPart_AsTheEdibleFifiPlant_AndCountsItOnce()
    {
        var server = Started(out var repo, "fifi-scan");
        using (repo)
        {
            server.AddLocalPlayer("Scout");
            var first = server.ScanSubject("Scout", "block", FifiPlant.StemKey);
            Assert.True(first.FirstTime);
            Assert.Equal("flora", first.Kind);
            Assert.Equal(FifiPlant.ScanSubject, first.Subject);
            Assert.Equal("ui.scan.threat.edible", first.ThreatKey);

            var blossom = server.ScanSubject("Scout", "block", FifiPlant.BlossomKey);
            Assert.False(blossom.FirstTime); // the same plant
            Assert.Equal(FifiPlant.ScanSubject, blossom.Subject);
            Assert.False(server.ScanSubject("Scout", "block", FifiPlant.BerriesKey).FirstTime);
        }
    }

    [Fact]
    public void TheScanSubject_HasAName_InEnglishAndGerman()
    {
        foreach (var lang in new[] { "en", "de" })
        {
            string json = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", lang + ".json"));
            Assert.Contains("\"ui.scan.subject." + FifiPlant.ScanSubject + "\"", json);
        }
    }
}
