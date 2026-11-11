// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3416 — rune shrines (terrain generation 36): the rune stones stand in small shrine ruins instead of alone. A complete shrine
/// is eight stones around an ancient-brick altar on a worn brick floor — exactly the ring the Sounders' shrine steps accept —
/// a partial one keeps three to six; they stand on the ground (levelled, never in water or lava), are the same for a seed and
/// from either side of a chunk edge, keep clear of the landing pads and of the trees, and come at the designed rate. Older
/// generations keep the lone stones and every chunk (the goldens pin that).
/// </summary>
public sealed class RuneShrineGenerationTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private static readonly string[] NormalWorlds =
    {
        "meadowlands", "savanna", "desert", "jungle", "varied", "tundra", "rocky", "swamp", "boreal", "badlands",
    };

    private readonly ITestOutputHelper _out;

    public RuneShrineGenerationTests(ITestOutputHelper output) => _out = output;

    private static WorldGenerator Gen(long seed, int generation = WorldDescription.RuneShrineGeneration)
    {
        var gen = new WorldGenerator(seed, Content);
        gen.SetLavaCoreVolcanoes(true);
        gen.SetTerrainGeneration(generation);
        return gen;
    }

    private static BlockId Id(string key) => Content.GetBlock(key)!.NumericId;

    /// <summary>Reads generated blocks by world cell, generating (and keeping) the chunks it touches.</summary>
    private sealed class Blocks
    {
        private readonly WorldGenerator _gen;
        private readonly PlanetType _planet;
        private readonly Dictionary<ChunkCoord, ChunkData> _chunks = new();

        public Blocks(WorldGenerator gen, PlanetType planet)
        {
            _gen = gen;
            _planet = planet;
        }

        public BlockId At(int x, int y, int z)
        {
            var c = new ChunkCoord(WorldConstants.WorldToChunk(x), WorldConstants.WorldToChunk(y), WorldConstants.WorldToChunk(z));
            if (!_chunks.TryGetValue(c, out var chunk))
            {
                _chunks[c] = chunk = _gen.Generate(_planet, c);
            }

            var o = WorldConstants.ChunkOrigin(c);
            return chunk.Get(x - o.X, y - o.Y, z - o.Z);
        }
    }

    /// <summary>Shrines near the spawn column, away from the X seam (so plain world coordinates around them are canonical).</summary>
    private static List<RuneShrine> ShrinesNear(WorldGenerator gen, PlanetType planet, int radius = 700)
        => gen.RuneShrinesNear(planet, 0, 0, radius)
            .Where(s => s.X > 16 && s.X < WorldConstants.Circumference - 16)
            .ToList();

    private static IEnumerable<(int Dx, int Dz)> Disk()
    {
        for (int dx = -6; dx <= 6; dx++)
            for (int dz = -6; dz <= 6; dz++)
            {
                if ((dx * dx) + (dz * dz) <= WorldGenerator.RuneShrineDiskR2)
                {
                    yield return (dx, dz);
                }
            }
    }

    [Fact]
    public void GenerationThirtySix_TurnsTheLoneRuneStoneOff_AndAnOlderWorldKeepsIt()
    {
        var savanna = Content.Planets["savanna"];
        Assert.Contains("rune-stone", Gen(31, WorldDescription.RuneShrineGeneration - 1).PropActiveForTest(savanna, true, true));
        Assert.DoesNotContain("rune-stone", Gen(31).PropActiveForTest(savanna, true, true));
        Assert.True(WorldDescription.RuneShrineGeneration <= WorldDescription.CurrentTerrainGeneration);

        // An older world has no shrine at all: no site, no claim on its trees and props.
        var meadow = Content.Planets["meadowlands"];
        var shrine = ShrinesNear(Gen(7), meadow).First();
        var older = Gen(7, WorldDescription.RuneShrineGeneration - 1);
        Assert.Empty(older.RuneShrinesNear(meadow, 0, 0, 700));
        Assert.False(older.RuneShrineClaimsForTest(meadow, shrine.X, shrine.Z, 4));
        Assert.NotEqual(Id("ancient_brick"), new Blocks(older, meadow).At(shrine.X, shrine.FloorY + 1, shrine.Z));
    }

    [Theory]
    [InlineData("titas")]
    [InlineData("valuma")]
    public void APlanetThatCanNeverRaiseAShrine_KeepsItsLoneRuneStones(string key)
    {
        // A structure whitelist without "monuments" never raises a shrine, so the lone stone stays as it was.
        var planet = Content.Planets[key];
        var gen = Gen(31);
        Assert.Contains("rune-stone", gen.PropActiveForTest(planet, true, true));
        Assert.Empty(gen.RuneShrinesNear(planet, 0, 0, 700));

        // A chunk sample: a rolled column holds its two-tall lone stone (an earlier row or water may take a column — the next
        // candidate is tried then).
        var blocks = new Blocks(gen, planet);
        var rune = Id("rune_stone");
        int tried = 0;
        bool found = false;
        for (int x = 64; x < 5800 && !found && tried < 60; x++)
            for (int z = -400; z <= 400 && !found && tried < 60; z++)
            {
                if (!gen.PropRollForTest("rune-stone", planet, x, z))
                {
                    continue;
                }

                tried++;
                int sy = gen.SurfaceHeight(planet, x, z);
                found = blocks.At(x, sy + 1, z) == rune && blocks.At(x, sy + 2, z) == rune;
            }

        Assert.True(found, $"no lone rune stone among {tried} rolled columns on a generation-36 {key} world");
    }

    [Fact]
    public void ANormalPlanet_HasNoRuneStoneOutsideItsShrines()
    {
        var planet = Content.Planets["meadowlands"];
        var gen = Gen(7);
        Assert.DoesNotContain("rune-stone", gen.PropActiveForTest(planet, true, true));
        var centre = ShrinesNear(gen, planet).First();
        var shrines = gen.RuneShrinesNear(planet, centre.X, centre.Z, 400);
        var rune = Id("rune_stone");
        int circ = WorldConstants.Circumference, inShrines = 0;
        int ccx = WorldConstants.WorldToChunk(centre.X), ccz = WorldConstants.WorldToChunk(centre.Z);
        for (int cx = ccx - 6; cx <= ccx + 6; cx++)
            for (int cz = ccz - 6; cz <= ccz + 6; cz++)
            {
                int surfaceCy = WorldConstants.WorldToChunk(gen.SurfaceHeight(planet, (cx * 16) + 8, (cz * 16) + 8));
                for (int cy = surfaceCy - 1; cy <= surfaceCy + 1; cy++)
                {
                    var coord = new ChunkCoord(cx, cy, cz);
                    var chunk = gen.Generate(planet, coord);
                    var o = WorldConstants.ChunkOrigin(coord);
                    for (int lx = 0; lx < 16; lx++)
                        for (int ly = 0; ly < 16; ly++)
                            for (int lz = 0; lz < 16; lz++)
                            {
                                if (chunk.Get(lx, ly, lz) != rune)
                                {
                                    continue;
                                }

                                int x = o.X + lx, z = o.Z + lz;
                                Assert.True(shrines.Any(s => Math.Pow(WorldConstants.WrapDeltaX(x - s.X, circ), 2)
                                    + Math.Pow(WorldConstants.WrapDeltaZ(z - s.Z, circ), 2) <= WorldGenerator.RuneShrineDiskR2),
                                    $"a rune stone outside every shrine at {x},{o.Y + ly},{z}");
                                inShrines++;
                            }
                }
            }

        Assert.True(inShrines >= 16, $"the sampled area holds shrine stones ({inShrines})");
    }

    [Fact]
    public void ACompleteShrine_HasItsAltarFloorAndRing_AndTheRingIsTheOneTheShrineStepsAccept()
    {
        var planet = Content.Planets["meadowlands"];
        var gen = Gen(7);
        var blocks = new Blocks(gen, planet);
        var rune = Id("rune_stone");
        var brick = Id("ancient_brick");
        var complete = ShrinesNear(gen, planet).Where(s => s.Complete).Take(3).ToList();
        Assert.Equal(3, complete.Count);
        foreach (var s in complete)
        {
            Assert.Equal(8, s.Stones);
            Assert.Equal(brick, blocks.At(s.X, s.FloorY + 1, s.Z)); // the altar
            Assert.Equal(brick, blocks.At(s.X, s.FloorY, s.Z));     // on the floor
            var bases = gen.RuneShrineStoneBasesForTest(planet, s.X, s.Z);
            Assert.Equal(8, bases.Count);
            Assert.All(bases, b => Assert.Equal(rune, blocks.At(b.X, b.Y, b.Z)));

            // The Sounders' ring rule (GameServerMelodyPuzzles.Sites): eight stone columns around an ancient brick, one per
            // 45° sector, 1.9–5.8 out, about the same distance — found from the altar, the struck stones among them.
            var ring = MelodySiteGeometry.FindRing(s.Altar, (ox, oy, oz) => blocks.At(s.X + ox, s.FloorY + 1 + oy, s.Z + oz) == rune);
            Assert.NotNull(ring);
            Assert.Equal(bases.OrderBy(b => b.X).ThenBy(b => b.Z), ring!.OrderBy(b => b.X).ThenBy(b => b.Z));

            int floor = Disk().Count(d => blocks.At(s.X + d.Dx, s.FloorY, s.Z + d.Dz) == brick);
            Assert.True(floor >= Disk().Count() * 3 / 4, $"a worn floor, not a missing one: {floor} of {Disk().Count()} bricks");
        }
    }

    [Fact]
    public void APartialShrine_KeepsThreeToSixStones_AndIsNoRingOfEight()
    {
        var planet = Content.Planets["meadowlands"];
        var gen = Gen(7);
        var blocks = new Blocks(gen, planet);
        var rune = Id("rune_stone");
        var partial = ShrinesNear(gen, planet).Where(s => !s.Complete).Take(3).ToList();
        Assert.Equal(3, partial.Count);
        foreach (var s in partial)
        {
            Assert.InRange(s.Stones, 3, 6);
            var bases = gen.RuneShrineStoneBasesForTest(planet, s.X, s.Z);
            Assert.Equal(s.Stones, bases.Count);
            Assert.All(bases, b => Assert.Equal(rune, blocks.At(b.X, b.Y, b.Z)));
            Assert.Null(MelodySiteGeometry.FindRing(s.Altar, (ox, oy, oz) => blocks.At(s.X + ox, s.FloorY + 1 + oy, s.Z + oz) == rune));
        }
    }

    [Theory]
    [InlineData("meadowlands", 7)]
    [InlineData("jungle", 20260903)]
    [InlineData("desert", 1)]
    [InlineData("tundra", 424242)]
    public void EveryShrine_StandsOnTheGround_WithNothingButItselfInside(string key, long seed)
    {
        var planet = Content.Planets[key];
        var gen = Gen(seed);
        var blocks = new Blocks(gen, planet);
        var own = new HashSet<BlockId> { BlockId.Air, Id("ancient_brick"), Id("rune_stone"), Id("data_cache") };
        var fluids = new HashSet<BlockId> { Id("water"), Id("lava") };
        var shrines = ShrinesNear(gen, planet, 400).Take(4).ToList();
        Assert.NotEmpty(shrines);
        foreach (var s in shrines)
        {
            foreach (var (dx, dz) in Disk())
            {
                int x = s.X + dx, z = s.Z + dz;
                var below = blocks.At(x, s.FloorY - 1, z);
                Assert.False(below.IsAir || fluids.Contains(below), $"{key} shrine at {s.X},{s.Z}: nothing under the floor at {dx},{dz}");
                Assert.False(blocks.At(x, s.FloorY, z).IsAir, $"{key} shrine at {s.X},{s.Z}: a hole in the floor at {dx},{dz}");
                for (int y = s.FloorY + 1; y <= s.FloorY + 20; y++)
                {
                    var b = blocks.At(x, y, z);
                    Assert.True(own.Contains(b), $"{key} shrine at {s.X},{s.Z}: '{Content.BlockById(b)?.Key}' inside at {dx},{y - s.FloorY},{dz}");
                }
            }
        }
    }

    [Fact]
    public void Shrines_AreTheSameForASeed_AndFromEitherSideOfAChunkEdge()
    {
        var planet = Content.Planets["savanna"];
        var a = Gen(20261108);
        var b = Gen(20261108);
        var first = ShrinesNear(a, planet);
        Assert.NotEmpty(first);
        Assert.Equal(first.Select(s => (s.X, s.FloorY, s.Z, s.Complete, s.Stones)),
            ShrinesNear(b, planet).Select(s => (s.X, s.FloorY, s.Z, s.Complete, s.Stones)));

        // The chunks a shrine spans, generated in opposite orders by two generators, hash the same.
        var s0 = first[0];
        var coords = new List<ChunkCoord>();
        foreach (int dx in new[] { -6, 6 })
            foreach (int dz in new[] { -6, 6 })
                foreach (int y in new[] { s0.FloorY - 4, s0.FloorY + 6 })
                {
                    var c = new ChunkCoord(WorldConstants.WorldToChunk(s0.X + dx), WorldConstants.WorldToChunk(y), WorldConstants.WorldToChunk(s0.Z + dz));
                    if (!coords.Contains(c))
                    {
                        coords.Add(c);
                    }
                }

        var forward = coords.ToDictionary(c => c, c => WorldGenerationGoldenTests.HashChunk(a.Generate(planet, c)));
        coords.Reverse();
        foreach (var c in coords)
        {
            Assert.Equal(forward[c], WorldGenerationGoldenTests.HashChunk(Gen(20261108).Generate(planet, c)));
        }
    }

    [Fact]
    public void AShrine_KeepsClearOfTheLandingPads()
    {
        var planet = Content.Planets["meadowlands"];
        var shrine = ShrinesNear(Gen(7), planet).First();
        var padded = Gen(7);
        int padX = shrine.X + 9, padZ = shrine.Z;
        padded.SetWorldMode(WorldConstants.Circumference, false, new[] { new LandingPadFlatten(padX, padZ, shrine.FloorY, 8) });
        var near = padded.RuneShrinesNear(planet, padX, padZ, 120);
        Assert.DoesNotContain(near, s => s.X == shrine.X && s.Z == shrine.Z);
        Assert.All(near, s => Assert.True(Math.Sqrt(Math.Pow(s.X - padX, 2) + Math.Pow(s.Z - padZ, 2)) >= 8 + 3 + 6 + 4,
            $"a shrine at {s.X},{s.Z} reaches the pad's levelled ground"));
    }

    [Fact]
    public void TheVegetationAndThePropPasses_KeepTheirAnchorsOutOfAShrine()
    {
        var planet = Content.Planets["jungle"];
        var gen = Gen(20260903);
        var s = ShrinesNear(gen, planet).First();
        Assert.True(gen.RuneShrineClaimsForTest(planet, s.X, s.Z, 0));
        Assert.True(gen.RuneShrineClaimsForTest(planet, s.X + 9, s.Z, 4));   // a crown of radius 4 would reach the floor
        Assert.False(gen.RuneShrineClaimsForTest(planet, s.X + 30, s.Z, 4)); // far enough out
    }

    [Fact]
    public void Shrines_ComeAtTheDesignedRate_AndACompleteOneStandsNearThePad()
    {
        var rates = new List<double>();
        var nearest = new List<double>();
        int shrines = 0, complete = 0;
        const int Radius = 600;
        foreach (string key in NormalWorlds)
        {
            var planet = Content.Planets[key];
            foreach (long seed in new long[] { 3, 20261108 })
            {
                var all = Gen(seed).RuneShrinesNear(planet, 0, 0, Radius);
                shrines += all.Count;
                complete += all.Count(s => s.Complete);
                rates.Add(all.Count / (Math.PI * Radius * Radius));
                var first = all.FirstOrDefault(s => s.Complete);
                nearest.Add(first.Stones == 0 ? double.PositiveInfinity
                    : Math.Sqrt(Math.Pow(WorldConstants.WrapDeltaX(first.X, WorldConstants.Circumference), 2) + Math.Pow(first.Z, 2)));
            }
        }

        double perColumns = 1.0 / rates.Average();
        _out.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"one shrine per {perColumns:F0} columns, complete share {complete / (double)shrines:P0}, nearest complete from the pad: {string.Join(", ", nearest.Select(d => d.ToString("F0", CultureInfo.InvariantCulture)))}"));

        // Designed: a cell of 112 rolls one with 0.8 and tries four spots — on a normal world one shrine per ~17–35 thousand
        // columns (every ~130–190 blocks), half of them complete; the lone stone was one per ~8 300 columns but a single stone.
        Assert.InRange(perColumns, 15_000, 40_000);
        Assert.InRange(complete / (double)shrines, 0.4, 0.6);
        Assert.True(nearest.Count(d => d <= 400) >= nearest.Count * 9 / 10,
            "a complete shrine stands within 400 blocks of the pad on nine worlds in ten");
    }
}
