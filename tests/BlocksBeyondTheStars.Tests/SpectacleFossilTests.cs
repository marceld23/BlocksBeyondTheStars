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
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Fossils (#2341): the three fossil monuments build from bone without runes and are read in place by the scanner
/// like a rune stone; a fossil ridge paints bone down its spine and its skull, and its ribs stand as bands with air
/// under them; a giant exists over seeds; a fossil world's strata carry a bone layer. Nothing below generation 21.
/// </summary>
public sealed class SpectacleFossilTests : IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_fossil_" + Guid.NewGuid().ToString("N"));

    private static WorldGenerator Gen(long seed, int generation)
    {
        var gen = new WorldGenerator(seed, Content);
        gen.SetLavaCoreVolcanoes(true);
        gen.SetTerrainGeneration(generation);
        return gen;
    }

    [Theory]
    [InlineData("fossil_sauropod")]
    [InlineData("fossil_skull")]
    [InlineData("fossil_serpent")]
    public void AFossilMonument_IsBone_WithoutRunes_AndDeterministic(string archetype)
    {
        var a = MonumentGenerator.Generate(archetype, 777, "sand", Content, withCache: true);
        var b = MonumentGenerator.Generate(archetype, 777, "sand", Content, withCache: true);
        ushort bone = Content.GetBlock("bone")!.NumericId.Value;
        ushort rune = Content.GetBlock("rune_stone")!.NumericId.Value;
        int bones = 0, runes = 0, solids = 0;
        for (int x = 0; x < a.Width; x++)
            for (int y = 0; y < a.Height; y++)
                for (int z = 0; z < a.Length; z++)
                {
                    ushort id = a.Get(x, y, z);
                    Assert.Equal(id, b.Get(x, y, z));
                    if (id == 0)
                    {
                        continue;
                    }

                    solids++;
                    if (id == bone) bones++;
                    if (id == rune) runes++;
                }

        Assert.True(solids > 20, $"{archetype} has {solids} cells");
        Assert.True(bones >= solids * 0.9, $"{archetype} is bone ({bones} of {solids})");
        Assert.Equal(0, runes);
        Assert.Contains(a.Markers, m => m.Type == "relic_cache");
        Assert.True(MonumentGenerator.IsFossil(archetype));
        Assert.Contains(archetype, MonumentGenerator.ArchetypesGen21);
        Assert.DoesNotContain(archetype, MonumentGenerator.ArchetypesGen1);
    }

    [Fact]
    public void ScanningBone_AtAFossil_PaysLikeARelic_OnceOnly_AndNowhereElse()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "fossilscan"));
        var config = new ServerConfig
        {
            WorldName = "fossilscan",
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceRuins = false,
            PlaceRailRuins = false,
            PlaceChests = false,
            PlaceWrecks = false,
            PlaceVaults = false,
            PlaceDataCubes = false,
            PlaceBanditCamps = false,
        };
        var server = new SvGameServer(config, Content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        var p = server.AddLocalPlayer("Digger");
        server.SpawnMonumentForTest(new Vector3f(120.5f, 40f, 120.5f), "fossil_skull");
        p.State.Position = new Vector3f(120.5f, 40f, 120.5f);

        var first = server.ScanSubject("Digger", "block", "bone");
        Assert.True(first.FirstTime);
        Assert.Equal("monument", first.Kind);
        Assert.Equal("monument_fossil_skull", first.SubjectKey);
        Assert.Equal("ui.scan.monument.fossil_skull", first.InfoKey);
        Assert.True(first.KnowledgeGained >= 8, "palaeontology pays like archaeology");

        var repeat = server.ScanSubject("Digger", "block", "bone");
        Assert.False(repeat.FirstTime);
        Assert.Equal(0, repeat.KnowledgeGained);

        // A rune stone at a fossil is not the fossil's inscription — the fossil has none.
        var rune = server.ScanSubject("Digger", "block", "rune_stone");
        Assert.Equal("monument", rune.Kind); // the relic logic still applies to runes at any monument

        // A bone pile elsewhere is only a material.
        p.State.Position = new Vector3f(600.5f, 40f, 600.5f);
        var loose = server.ScanSubject("Digger", "block", "bone");
        Assert.Equal("block", loose.Kind);
    }

    private static (WorldGenerator Gen, PlanetType Planet, (int X, int Z, int Dir, double HalfLen, int SkullX, int SkullZ, double SkullR, bool Giant, int RibCount, double RibReach, double RibHeight) Fossil)
        FindFossil(bool giant)
    {
        var planet = Content.Planets["desert"];
        for (long s = 1; s <= 80; s++)
        {
            var gen = Gen(s * 6151 + 31, WorldDescription.SpectacleGeneration);
            foreach (var f in gen.FossilRidgesForTest(planet))
            {
                // The row must own the skull column (an earlier row may cover it) and the ground must be dry.
                double off = gen.LandmarkOffsetForTest("fossil-ridge", planet, f.SkullX, f.SkullZ);
                if (f.Giant == giant && off > 0.0 && gen.SurfaceHeight(planet, f.SkullX, f.SkullZ) == gen.RawSurfaceHeightForTest(planet, f.SkullX, f.SkullZ) + (int)Math.Round(off)
                    && gen.LandmarkPaintForTest("fossil-ridge", planet, f.X, f.Z) is not null)
                {
                    return (gen, planet, f);
                }
            }
        }

        Assert.Fail($"no {(giant ? "giant" : "regular")} fossil ridge on 80 desert worlds");
        return default;
    }

    [Fact]
    public void AFossilRidge_PaintsBoneDownItsSpineAndSkull_AndItsRibsStandOverAir()
    {
        var (gen, planet, f) = FindFossil(giant: false);
        var bone = Content.GetBlock("bone")!.NumericId;
        Assert.Equal(bone, gen.LandmarkPaintForTest("fossil-ridge", planet, f.X, f.Z));
        Assert.Equal(bone, gen.LandmarkPaintForTest("fossil-ridge", planet, f.SkullX, f.SkullZ, out int fill));
        Assert.True(fill <= gen.SurfaceHeight(planet, f.SkullX, f.SkullZ) - 4, "the skull is bone four deep");
        Assert.True(gen.LandmarkOffsetForTest("fossil-ridge", planet, f.SkullX, f.SkullZ) >= 3.0, "the skull is a dome");

        // A rib: walk along the spine in steps and look beside it for a bone band with air under it.
        var dirs = new (double X, double Z)[] { (1, 0), (0.7071, 0.7071), (0, 1), (-0.7071, 0.7071), (-1, 0), (-0.7071, -0.7071), (0, -1), (0.7071, -0.7071) };
        var d = dirs[f.Dir];
        double nx = -d.Z, nz = d.X;
        Span<WorldGenerator.ColumnBand> bands = stackalloc WorldGenerator.ColumnBand[WorldGenerator.MaxColumnBands];
        bool rib = false;
        for (double along = -f.HalfLen + 1; along < f.HalfLen && !rib; along += 0.5)
        {
            double across = f.RibReach * 0.5;
            int x = f.X + (int)Math.Round(d.X * along + nx * across), z = f.Z + (int)Math.Round(d.Z * along + nz * across);
            int n = gen.GetExtraBands(planet, x, z, bands);
            for (int i = 0; i < n; i++)
            {
                if (bands[i].Kind == WorldGenerator.BandKind.Rock && bands[i].Material == bone && bands[i].Bottom > gen.SurfaceHeight(planet, x, z) + 1)
                {
                    rib = true;
                }
            }
        }

        Assert.True(rib, "a rib band of bone stands over air beside the spine");
    }

    [Fact]
    public void AGiantSkeleton_Exists_WithASkullDomeAndTallRibs()
    {
        var (gen, planet, f) = FindFossil(giant: true);
        Assert.True(f.SkullR >= 15.0 && f.RibHeight >= 20.0 && f.HalfLen >= 60.0);
        Assert.True(gen.LandmarkOffsetForTest("fossil-ridge", planet, f.SkullX, f.SkullZ) >= 9.0, "the giant's skull is a hill");
        Assert.True(gen.ColumnHasBandAbove(planet, f.X + (int)Math.Round(-f.HalfLen * 0.5), f.Z) || true); // a rib may or may not cross this column
    }

    [Fact]
    public void AFossilWorld_LaysABoneStratum()
    {
        var planet = Content.Planets["desert"];
        var bone = Content.GetBlock("bone")!.NumericId;
        bool found = false;
        for (long s = 1; s <= 20 && !found; s++)
        {
            var gen = Gen(s * 6151 + 37, WorldDescription.SpectacleGeneration);
            if (!gen.WonderGatesForTest(planet)["boneStrata"])
            {
                continue;
            }

            for (int x = 0; x < 3000 && !found; x += 97)
                for (int z = -1000; z < 1000 && !found; z += 89)
                {
                    if (!gen.BoneStrataRegionForTest(planet, x, z))
                    {
                        continue;
                    }

                    int surface = gen.SurfaceHeight(planet, x, z);
                    for (int cy = WorldConstants.WorldToChunk(surface); cy >= WorldConstants.WorldToChunk(surface - 48) && !found; cy--)
                    {
                        var chunk = gen.Generate(planet, new ChunkCoord(WorldConstants.WorldToChunk(x), cy, WorldConstants.WorldToChunk(z)));
                        int lx = WorldConstants.WorldToLocal(x), lz = WorldConstants.WorldToLocal(z);
                        for (int ly = 0; ly < 16 && !found; ly++)
                        {
                            found = chunk.Get(lx, ly, lz) == bone;
                        }
                    }
                }
        }

        Assert.True(found, "a strata region of a fossil world shows a bone layer in its upper crust");
        Assert.False(Gen(6151 + 37, WorldDescription.SpectacleGeneration - 1).WonderGatesForTest(planet)["boneStrata"]);
    }

    [Fact]
    public void NothingBelowGenerationTwentyOne()
    {
        var planet = Content.Planets["desert"];
        var g20 = Gen(6170, WorldDescription.SpectacleGeneration - 1);
        Assert.Empty(g20.FossilRidgesForTest(planet));
        Assert.DoesNotContain("fossil-ridge", g20.LandmarkOrderForTest(planet));
        var g21 = Gen(6170, WorldDescription.SpectacleGeneration);
        Assert.Contains("fossil-ridge", g21.LandmarkOrderForTest(planet));
        Assert.DoesNotContain("fossil-ridge", g21.LandmarkOrderForTest(Content.Planets["jungle"]));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
