// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

public class WorldGenerationTests
{
    private static GameContent Content() => ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void Generation_IsDeterministic_ForSameSeedAndCoord()
    {
        var content = Content();
        var planet = content.GetPlanet("rocky")!;
        var genA = new WorldGenerator(12345, content);
        var genB = new WorldGenerator(12345, content);

        var coord = new ChunkCoord(2, 3, -1);
        var a = genA.Generate(planet, coord);
        var b = genB.Generate(planet, coord);

        Assert.True(a.RawBlocks.SequenceEqual(b.RawBlocks));
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void CrateredWorld_FlatRegolithWithPits_AndRareMetalInSomeCraters()
    {
        // Item 33: landable asteroids (and airless moons) are mostly flat regolith pocked with round impact
        // craters; some crater floors expose a few clumps of rare metal.
        var content = Content();
        var asteroid = content.GetPlanet("asteroid")!;
        Assert.True(asteroid.Cratered, "the asteroid type is cratered");
        // Seed picked so the 256x256 scan window contains metal-bearing deep craters under the torus
        // noise field (the gates are a triple-conjunction, so not every seed hits inside one window).
        var gen = new WorldGenerator(14, content);

        var metals = new System.Collections.Generic.HashSet<BlockId>();
        foreach (var k in new[] { "titanium_ore", "gold_ore", "platinum_ore", "cobalt_ore", "uranium_ore", "tungsten_ore", "neodymium_ore" })
        {
            metals.Add(content.GetBlock(k)!.NumericId);
        }

        int baseH = asteroid.BaseHeight, cs = WorldConstants.ChunkSize;
        int minH = int.MaxValue, flat = 0, total = 0, metalCols = 0, surfaceCols = 0;

        for (int cx = 0; cx < 16; cx++)
            for (int cz = 0; cz < 16; cz++)
            {
                var col = new ChunkData[5];
                for (int cy = 0; cy < col.Length; cy++)
                {
                    col[cy] = gen.Generate(asteroid, new ChunkCoord(cx, cy, cz));
                }

                var origin = WorldConstants.ChunkOrigin(new ChunkCoord(cx, 0, cz));
                for (int lx = 0; lx < cs; lx++)
                    for (int lz = 0; lz < cs; lz++)
                    {
                        int h = gen.SurfaceHeight(asteroid, origin.X + lx, origin.Z + lz);
                        minH = System.Math.Min(minH, h);
                        if (System.Math.Abs(h - baseH) <= 3) flat++;
                        total++;

                        for (int gy = col.Length * cs - 1; gy >= 0; gy--) // top solid cell = the exposed surface
                        {
                            var b = col[gy / cs].Get(lx, gy % cs, lz);
                            if (!b.IsAir)
                            {
                                surfaceCols++;
                                if (metals.Contains(b)) metalCols++;
                                break;
                            }
                        }
                    }
            }

        Assert.True(baseH - minH >= 4, "craters dig pits below the flat regolith");
        Assert.True(flat > total / 4, "much of the surface is flat regolith between craters");
        Assert.True(metalCols > 0, "some crater floors expose rare metal");
        Assert.True(metalCols < surfaceCols / 12, "metal is sparse — only some craters, a few clumps each");
    }

    [Fact]
    public void CraterRelief_DiffersPerBody_ButIsStablePerBody()
    {
        // #518: every airless body used to share one crater character — same density, same depth, same rim,
        // only the craters' positions moved. Now each body rolls its own from its identity salt.
        var content = Content();
        var asteroid = content.GetPlanet("asteroid")!;
        int circ = WorldConstants.CircumferenceFor("sys0-a0", WorldConstants.WorldSizeClass.Asteroid);

        (double Pitted, double Rimmed, double Mean, int Deepest) Profile(string locationId)
        {
            var gen = new WorldGenerator(9001, content);
            gen.SetWorldMode(circ, cratered: true, landingPads: null, locationId: locationId);

            int pitted = 0, rimmed = 0, n = 0, deepest = 0;
            double sum = 0;
            for (int x = 0; x < 192; x += 2)
                for (int z = 0; z < 192; z += 2)
                {
                    int d = gen.SurfaceHeight(asteroid, x, z) - asteroid.BaseHeight;
                    if (d <= -4) pitted++;     // inside a crater bowl
                    if (d >= 2) rimmed++;      // up on an ejecta rim
                    deepest = System.Math.Min(deepest, d);
                    sum += System.Math.Abs(d);
                    n++;
                }

            return (pitted / (double)n, rimmed / (double)n, sum / n, deepest);
        }

        var bodies = new[] { "sys0-a0", "sys0-a1", "sys1-a0", "sys2-a1", "sys3-a2", "sys4-a0" };
        var profiles = bodies.Select(Profile).ToList();

        // Same body → identical relief (the profile is cached per seed and must not drift between instances).
        Assert.Equal(profiles[0], Profile(bodies[0]));

        // Across bodies the landscape genuinely differs — not just the craters' positions. Crater depth is
        // rolled from 5 to 12 blocks, so a handful of rocks must span a good part of that.
        int deepSpread = profiles.Max(p => -p.Deepest) - profiles.Min(p => -p.Deepest);
        Assert.True(deepSpread >= 3, $"crater depth should vary per body, spread was {deepSpread}");

        double pitMax = profiles.Max(p => p.Pitted), pitMin = profiles.Min(p => p.Pitted);
        Assert.True(pitMax > pitMin * 1.5, $"crater density should vary per body: {pitMin}..{pitMax}");

        // …while every one of them stays recognisably "flat regolith pocked with craters", never a mountain range.
        foreach (var p in profiles)
        {
            Assert.True(p.Pitted > 0.0, $"a cratered body needs pits: {p}");
            Assert.True(p.Rimmed > 0.0, $"a cratered body needs raised rims: {p}");
            Assert.True(p.Mean < 6.0, $"regolith must stay broadly flat between craters: {p}");
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void WateryWorld_GeneratesUplandPonds_AboveSeaLevel()
    {
        // B7: a watery (atmospheric) world should scatter swimmable upland ponds — water ABOVE the global sea
        // level — not just sea-level basins. (Determinism is covered by the test above, since ponds are part of
        // Generate.)
        var content = Content();
        var planet = content.GetPlanet("jungle")!;
        var gen = new WorldGenerator(7, content);
        int sea = gen.SeaLevel(planet);
        Assert.True(sea > int.MinValue, "expected a watery world for this test");
        var waterId = content.GetBlock("water")!.NumericId;
        int cs = WorldConstants.ChunkSize;

        int pondCells = 0;
        for (int cx = 0; cx < 16 && pondCells == 0; cx++)
            for (int cz = 0; cz < 16 && pondCells == 0; cz++)
                for (int cy = 0; cy <= 4; cy++)
                {
                    var coord = new ChunkCoord(cx, cy, cz);
                    var chunk = gen.Generate(planet, coord);
                    var origin = WorldConstants.ChunkOrigin(coord);
                    for (int lx = 0; lx < cs; lx++)
                        for (int ly = 0; ly < cs; ly++)
                            for (int lz = 0; lz < cs; lz++)
                            {
                                if (origin.Y + ly > sea && chunk.Get(lx, ly, lz) == waterId)
                                {
                                    pondCells++;
                                }
                            }
                }

        Assert.True(pondCells > 0, $"expected upland ponds (water above sea level {sea}) on a watery world");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Trees_DoNotStandInUplandPonds()
    {
        // B35: a tree must never spawn in the water — its trunk base sitting directly on a pond/sea cell. Scan a
        // watery, forested world (jungle, the same seed that grows upland ponds) and assert no wood_log has a
        // water cell directly beneath it (the only way a trunk can be "in" the water).
        var content = Content();
        var planet = content.GetPlanet("jungle")!;
        var gen = new WorldGenerator(7, content);
        var waterId = content.GetBlock("water")!.NumericId;
        var logId = content.GetBlock("wood_log")!.NumericId;
        int cs = WorldConstants.ChunkSize;

        int logs = 0, logsInWater = 0;
        for (int cx = 0; cx < 16; cx++)
            for (int cz = 0; cz < 16; cz++)
            {
                // Generate the vertical column of chunks once so a trunk base at a chunk boundary can see the cell
                // below it (in the chunk underneath).
                var col = new ChunkData[6];
                for (int cy = 0; cy < col.Length; cy++)
                {
                    col[cy] = gen.Generate(planet, new ChunkCoord(cx, cy, cz));
                }

                for (int cy = 0; cy < col.Length; cy++)
                    for (int lx = 0; lx < cs; lx++)
                        for (int ly = 0; ly < cs; ly++)
                            for (int lz = 0; lz < cs; lz++)
                            {
                                if (col[cy].Get(lx, ly, lz) != logId)
                                {
                                    continue;
                                }

                                logs++;
                                var below = ly > 0 ? col[cy].Get(lx, ly - 1, lz)
                                          : cy > 0 ? col[cy - 1].Get(lx, cs - 1, lz)
                                          : BlockId.Air;
                                if (below == waterId)
                                {
                                    logsInWater++;
                                }
                            }
            }

        Assert.True(logs > 0, "expected the jungle world to grow trees (the test would be meaningless otherwise)");
        Assert.Equal(0, logsInWater);
    }

    [Fact]
    public void IsSurfaceWater_FlagsPondsAndDryLand()
    {
        // Guards the helper that keeps trees (B35) and ship landings (B36) out of the water: on a watery,
        // partly-dry world it must report BOTH wet columns (a sea or upland pond exists) and dry ones (land
        // exists), so the landing search can actually find dry ground.
        var content = Content();
        var planet = content.GetPlanet("jungle")!;
        var gen = new WorldGenerator(7, content);

        bool anyWet = false, anyDry = false;
        for (int x = 0; x < 400 && !(anyWet && anyDry); x += 3)
            for (int z = -200; z < 200 && !(anyWet && anyDry); z += 7)
            {
                if (gen.IsSurfaceWater(planet, x, z))
                {
                    anyWet = true;
                }
                else
                {
                    anyDry = true;
                }
            }

        Assert.True(anyWet, "expected some surface water (sea/pond) on a watery world");
        Assert.True(anyDry, "expected some dry land on a watery world (else ships could never land dry)");
    }

    [Fact]
    public void VoidPlanet_GeneratesEmptySpace()
    {
        var content = Content();
        var planet = content.GetPlanet("orbital_station")!; // Void=true
        var gen = new WorldGenerator(123, content);

        // A void world (an orbital station's own location) is pure air — only its stamped structure lives
        // there, so every generated cell across a vertical span is empty.
        for (int cy = 0; cy <= 6; cy++)
        {
            var chunk = gen.Generate(planet, new ChunkCoord(0, cy, 0));
            foreach (var b in chunk.RawBlocks)
            {
                Assert.Equal(BlockId.AirValue, b);
            }
        }
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentTerrain()
    {
        var content = Content();
        var planet = content.GetPlanet("rocky")!;
        var coord = new ChunkCoord(0, 4, 0);

        var a = new WorldGenerator(1, content).Generate(planet, coord);
        var b = new WorldGenerator(2, content).Generate(planet, coord);

        Assert.False(a.RawBlocks.SequenceEqual(b.RawBlocks));
    }

    [Fact]
    public void Terrain_HasSolidGroundAndAirAbove()
    {
        var content = Content();
        var planet = content.GetPlanet("rocky")!;
        var gen = new WorldGenerator(999, content);

        // A vertical span of chunks around the base height should contain both solid and air.
        int solid = 0, air = 0;
        for (int cy = 2; cy <= 5; cy++)
        {
            var chunk = gen.Generate(planet, new ChunkCoord(0, cy, 0));
            foreach (var b in chunk.RawBlocks)
            {
                if (b == BlockId.AirValue) air++; else solid++;
            }
        }

        Assert.True(solid > 0, "Expected some solid blocks.");
        Assert.True(air > 0, "Expected some air blocks.");
    }

    [Fact]
    public void SurfaceHeight_IsContinuous_NoLargeCliffsBetweenNeighbours()
    {
        var content = Content();
        var planet = content.GetPlanet("rocky")!;
        var gen = new WorldGenerator(555, content);

        // Continuity guard: neighbouring columns must never JUMP (a seam/noise bug shows as 10+ block
        // discontinuities). Steeper-but-continuous flanks are allowed — the per-world terrain-drama factor
        // ("Welten reicher" W-R1, up to ~1.5×) intentionally makes some worlds more rugged (was ≤4).
        int prev = gen.SurfaceHeight(planet, 0, 0);
        for (int x = 1; x < 64; x++)
        {
            int h = gen.SurfaceHeight(planet, x, 0);
            Assert.True(System.Math.Abs(h - prev) <= 6, $"Unexpectedly steep terrain step at x={x}.");
            prev = h;
        }
    }

    private static ushort SurfaceBlockAt(WorldGenerator gen, BlocksBeyondTheStars.Shared.Definitions.PlanetType planet, int x, int z)
    {
        int y = gen.SurfaceHeight(planet, x, z);
        var coord = WorldConstants.WorldToChunk(new Vector3i(x, y, z));
        var origin = WorldConstants.ChunkOrigin(coord);
        var chunk = gen.Generate(planet, coord);
        return chunk.Get(x - origin.X, y - origin.Y, z - origin.Z).Value;
    }

    [Fact]
    public void SingleBiomePlanet_HasItsSurfaceBlock()
    {
        // Use a genuinely single-biome, sea-free world (crystal) — desert is now a multi-biome dune world.
        var content = Content();
        var planet = content.GetPlanet("crystal")!;
        var gen = new WorldGenerator(2024, content);
        ushort crystal = content.GetBlock("crystal")!.NumericId.Value;

        Assert.Equal(crystal, SurfaceBlockAt(gen, planet, 10, 10));
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void MultiBiomeWorld_HasSeveralSurfaceBlocks()
    {
        var content = Content();
        var planet = content.GetPlanet("varied")!;
        var gen = new WorldGenerator(2024, content);

        var surfaces = new HashSet<ushort>();
        for (int x = 0; x < 600; x += 20)
        {
            for (int z = 0; z < 600; z += 20)
            {
                surfaces.Add(SurfaceBlockAt(gen, planet, x, z));
            }
        }

        Assert.True(surfaces.Count >= 2, $"Expected a multi-biome world to show several surface blocks (got {surfaces.Count}).");
    }

    [Fact]
    public void MultiBiomeWorld_ShufflesWhichBiomesMakeTheCut()
    {
        // #696: the per-world biome subset must vary in MEMBERSHIP, not only in size — before the fix
        // the first N pool entries always won, so a later entry could never appear without all earlier
        // ones and the first entry was present on every world of the type.
        var content = Content();
        var planet = content.GetPlanet("varied")!; // pool: sand, grass, mud, stone
        int pool = planet.Biomes.Count;
        Assert.True(pool >= 3, "test needs a planet type with a multi-biome pool");
        var firstPoolSurface = content.GetBlock(planet.Biomes[0].SurfaceBlock)!.NumericId;

        bool sawWorldWithoutFirstEntry = false;
        var seenSurfaces = new HashSet<BlockId>();
        for (int seed = 1; seed <= 80; seed++)
        {
            var biomes = new WorldGenerator(seed, content).ResolveBiomes(planet);
            Assert.InRange(biomes.Count, 2, pool);
            var surfaces = biomes.Select(b => b.Surface).ToHashSet();
            Assert.Equal(biomes.Count, surfaces.Count); // no biome picked twice
            seenSurfaces.UnionWith(surfaces);
            sawWorldWithoutFirstEntry |= !surfaces.Contains(firstPoolSurface);
        }

        Assert.True(sawWorldWithoutFirstEntry,
            "across many seeds some world should skip the first pool entry — the subset is shuffled, not a prefix");
        Assert.Equal(pool, seenSurfaces.Count); // every pool entry appears on some world
    }

    [Fact]
    public void ResolveBiomes_IsDeterministic_ForSameSeed()
    {
        var content = Content();
        var planet = content.GetPlanet("varied")!;
        var a = new WorldGenerator(4242, content).ResolveBiomes(planet);
        var b = new WorldGenerator(4242, content).ResolveBiomes(planet);
        Assert.Equal(a.Select(x => x.Surface), b.Select(x => x.Surface));
    }

    [Fact]
    public void GeneratedOres_AreAmongPlanetDefinition()
    {
        var content = Content();
        var planet = content.GetPlanet("rocky")!;
        var gen = new WorldGenerator(77, content);

        // Scan the DEEP interior only (Y 0..31, well below any rocky surface): up there the surface layer
        // now legitimately mixes in biome surfaces, snow caps (#476), volcano basalt (#477) and vegetation
        // (#479). Down here only rock, veins, caches, caves — and the lava pockets of the deep lava table
        // (#472/#477 L-A) plus the seeded mantle rocks — may appear.
        var allowed = new HashSet<ushort> { BlockId.AirValue };
        allowed.Add(content.GetBlock(planet.DeepBlock)!.NumericId.Value);
        allowed.Add(content.GetBlock("data_cache")!.NumericId.Value);
        allowed.Add(content.GetBlock("lava")!.NumericId.Value);      // deep lava table fills carved cells
        allowed.Add(content.GetBlock("bedrock")!.NumericId.Value);
        allowed.Add(content.GetBlock("basalt")!.NumericId.Value);    // per-world mantle rocks
        allowed.Add(content.GetBlock("deepslate")!.NumericId.Value);
        allowed.Add(content.GetBlock("granite")!.NumericId.Value);
        foreach (var ore in planet.Ores)
        {
            allowed.Add(content.GetBlock(ore.Block)!.NumericId.Value);
        }

        for (int cy = 0; cy <= 1; cy++)
        {
            var chunk = gen.Generate(planet, new ChunkCoord(0, cy, 0));
            foreach (var b in chunk.RawBlocks)
            {
                Assert.Contains(b, allowed);
            }
        }
    }

    private static int CountBlock(WorldGenerator gen, BlocksBeyondTheStars.Shared.Definitions.PlanetType planet, ushort block,
        int cyLo = 3, int cyHi = 5)
    {
        int n = 0;
        for (int cx = 0; cx < 8; cx++)
            for (int cz = 0; cz < 8; cz++)
                for (int cy = cyLo; cy <= cyHi; cy++) // default: a wide span around typical sea levels (Y ≈ 48..95)
                {
                    var chunk = gen.Generate(planet, new ChunkCoord(cx, cy, cz));
                    foreach (var b in chunk.RawBlocks)
                    {
                        if (b == block) n++;
                    }
                }

        return n;
    }

    [Fact]
    public void AtmosphereWorld_FillsBasinsWithWater()
    {
        var content = Content();
        var planet = content.GetPlanet("jungle")!; // breathable atmosphere → water seas
        var gen = new WorldGenerator(777, content);
        ushort water = content.GetBlock("water")!.NumericId.Value;

        Assert.True(CountBlock(gen, planet, water) > 0, "An atmosphere world should pool water in its basins.");
    }

    [Fact]
    public void AtmosphereWorld_GrowsAquaticFlora()
    {
        var content = Content();
        var planet = content.GetPlanet("jungle")!; // water seas + flora → kelp on the seabed, lilies on top
        var gen = new WorldGenerator(777, content);
        ushort kelp = content.GetBlock("flora_kelp")!.NumericId.Value;
        ushort lily = content.GetBlock("flora_lily")!.NumericId.Value;

        Assert.True(CountBlock(gen, planet, kelp) + CountBlock(gen, planet, lily) > 0,
            "A watery flora world should grow kelp or lily pads in its seas.");
    }

    [Fact]
    public void AirlessFloraWorld_GrowsNoAquaticFlora()
    {
        var content = Content();
        var planet = content.GetPlanet("lava")!; // lava seas, never water → no kelp/lily
        var gen = new WorldGenerator(777, content);
        ushort kelp = content.GetBlock("flora_kelp")!.NumericId.Value;
        ushort lily = content.GetBlock("flora_lily")!.NumericId.Value;

        Assert.Equal(0, CountBlock(gen, planet, kelp) + CountBlock(gen, planet, lily));
    }

    [Fact]
    public void WateryWorld_CanGrowCoralOrSeagrass()
    {
        // The coral + seagrass aquatic archetypes used to be defined but never placed (StampWaterFlora only knew
        // kelp/lily) — so they were dead content. Across a span of seeds, at least one watery world must now grow
        // one of them in its seas/ponds.
        var content = Content();
        var planet = content.GetPlanet("jungle")!;
        ushort coral = content.GetBlock("flora_coral")!.NumericId.Value;
        ushort seagrass = content.GetBlock("flora_seagrass")!.NumericId.Value;

        int total = 0;
        for (long seed = 1; seed <= 25 && total == 0; seed++)
        {
            var gen = new WorldGenerator(seed, content);
            total += CountBlock(gen, planet, coral) + CountBlock(gen, planet, seagrass);
        }

        Assert.True(total > 0, "coral/seagrass aquatic flora should be placed on some watery worlds.");
    }

    [Fact]
    [Trait("Category", "Slow")] // 1024² scan seeking a pooled reach (#469) — full-tier only (PRs skip Slow)
    public void TryGetWaterSurface_LandsInsideGeneratedWater()
    {
        // Guards the fauna fix: aquatic creatures spawn at the column TryGetWaterSurface reports. The old probe
        // (surface+1) sat in the air above flush ponds, so swimmers never spawned. Here we confirm that the
        // mid-water cell the helper points a swimmer at is actually a water block in the generated world.
        var content = Content();
        var planet = content.GetPlanet("jungle")!;
        var gen = new WorldGenerator(7, content);
        // A column is a genuine water body if its cells hold water OR the aquatic flora world gen plants in it
        // (kelp/seagrass stalks, a coral clump, or a lily pad can each occupy a water cell).
        var aquatic = new System.Collections.Generic.HashSet<ushort>
        {
            content.GetBlock("water")!.NumericId.Value,
            content.GetBlock("flora_kelp")!.NumericId.Value,
            content.GetBlock("flora_lily")!.NumericId.Value,
            content.GetBlock("flora_coral")!.NumericId.Value,
            content.GetBlock("flora_seagrass")!.NumericId.Value,
        };
        int cs = WorldConstants.ChunkSize;

        // #469: the old 25-column cap stopped at the first ponds and never reached a POOLED river column —
        // whose surface sits ABOVE the local terrain, exactly the case the helper used to get wrong (it
        // reconstructed the band from surfaceY and reported water inside solid rock). Verify many more
        // columns AND at least one pooled one (WaterSurfaceY above the terrain).
        var field = gen.RiverFieldFor(planet);
        int verified = 0, pooledVerified = 0;
        for (int wx = 0; wx < 1024 && (verified < 400 || pooledVerified == 0); wx++)
            for (int wz = 0; wz < 1024 && (verified < 400 || pooledVerified == 0); wz++)
            {
                if (!gen.TryGetWaterSurface(planet, wx, wz, out int top, out int bed))
                {
                    continue;
                }

                Assert.True(top > bed, "a water column must have at least one water cell above the seabed");

                // Every cell of the reported [seabed+1 .. top] span must be water or aquatic flora in the generated
                // world — i.e. the helper points a swimmer at a genuine, fully-filled water body (the old surface+1
                // probe sat in the air above flush ponds, which is what kept water creatures from ever spawning).
                for (int y = bed + 1; y <= top; y++)
                {
                    var coord = new ChunkCoord(wx / cs, y / cs, wz / cs);
                    var chunk = gen.Generate(planet, coord);
                    ushort cell = chunk.Get(wx % cs, y % cs, wz % cs).Value;
                    Assert.True(aquatic.Contains(cell), $"cell ({wx},{y},{wz}) in the reported water column should be water/aquatic flora");
                }

                verified++;
                if (field.TryGet(wx, wz, out var col) && col.WaterSurfaceY > gen.SurfaceHeight(planet, wx, wz))
                {
                    pooledVerified++; // a pooled reach — the regression case fish used to spawn in rock on
                }
            }

        Assert.True(verified > 0, "expected to find water columns on a watery world.");
    }

    [Fact]
    public void WateryWorld_CarvesRivers()
    {
        // A wet world must carve routed rivers (RiverNetwork/RiverField), and a river column must actually be
        // filled with water near its surface (water itself, or the aquatic flora that grows in it).
        var content = Content();
        var planet = content.GetPlanet("jungle")!; // WaterAbundance defaults to 0.55 (≥ 0.4) → rivers
        var gen = new WorldGenerator(7, content);
        var aquatic = new System.Collections.Generic.HashSet<ushort>
        {
            content.GetBlock("water")!.NumericId.Value,
            content.GetBlock("flora_kelp")!.NumericId.Value,
            content.GetBlock("flora_lily")!.NumericId.Value,
            content.GetBlock("flora_coral")!.NumericId.Value,
            content.GetBlock("flora_seagrass")!.NumericId.Value,
        };
        int cs = WorldConstants.ChunkSize;

        int rivers = 0;
        for (int wx = 0; wx < 1200 && rivers < 8; wx++)
            for (int wz = 0; wz < 1200 && rivers < 8; wz++)
            {
                if (gen.SurfaceRiverDepth(planet, wx, wz) <= 0)
                {
                    continue;
                }

                // The routed channel's water surface follows the terrain; some cell near the surface must be
                // water/aquatic flora (sheet at terrain, or a pooled/waterfall cell just above it).
                int surfaceY = gen.SurfaceHeight(planet, wx, wz);
                bool wet = false;
                for (int y = surfaceY - 3; y <= surfaceY + 6 && !wet; y++)
                {
                    if (y < 0) continue;
                    var coord = new ChunkCoord(wx / cs, y / cs, wz / cs);
                    var chunk = gen.Generate(planet, coord);
                    if (aquatic.Contains(chunk.Get(wx % cs, ((y % cs) + cs) % cs, wz % cs).Value)) wet = true;
                }

                Assert.True(wet, $"routed river column ({wx},{wz}) holds no water near surface {surfaceY}");
                rivers++;
            }

        Assert.True(rivers > 0, "a wet world should carve routed river channels.");
    }

    [Fact]
    public void LavaWorld_CarvesLavaRivers()
    {
        // L2: the routed-river machinery is reused for lava on the `lava`/`ashen` worlds — magma channels
        // that flow downhill into the lava sea. The field fills with LAVA (not water), and a routed channel
        // column holds lava near its surface.
        var content = Content();
        var planet = content.GetPlanet("lava")!; // basalt surface → volcanic → lava sea
        var gen = new WorldGenerator(7, content);
        var lavaId = content.GetBlock("lava")!.NumericId;

        var field = gen.RiverFieldFor(planet);
        Assert.True(field.ColumnCount > 0, "the lava world produced no routed lava channels");
        Assert.Equal(lavaId, field.FillFluid); // the field fills with lava, not water

        int cs = WorldConstants.ChunkSize;
        int rivers = 0;
        for (int wx = 0; wx < 1500 && rivers < 6; wx++)
            for (int wz = 0; wz < 1500 && rivers < 6; wz++)
            {
                if (gen.SurfaceRiverDepth(planet, wx, wz) <= 0) continue;

                // Scan around the CHANNEL's own water surface: since the terrain-wonders wave a channel
                // may run entrenched a few blocks below its neighbouring columns, so the column surface
                // is not a reliable anchor for where the routed fill sits.
                Assert.True(field.TryGet(wx, wz, out var col), "SurfaceRiverDepth and the field disagree");
                bool molten = false;
                for (int y = col.BedY; y <= col.WaterSurfaceY + col.WaterfallDrop + 1 && !molten; y++)
                {
                    if (y < 0) continue;
                    var chunk = gen.Generate(planet, new ChunkCoord(wx / cs, y / cs, wz / cs));
                    if (chunk.Get(wx % cs, ((y % cs) + cs) % cs, wz % cs).Value == lavaId.Value) molten = true;
                }

                Assert.True(molten, $"routed lava column ({wx},{wz}) holds no lava in its channel band");
                rivers++;
            }

        Assert.True(rivers > 0, "the lava world should carve routed lava channels.");
    }

    [Fact]
    public void Rivers_RouteWithoutFloatingWater()
    {
        // Routed rivers replace the old flat-ground gate: instead of being erased wherever the ground tilts, a
        // river now flows downhill with its water surface FOLLOWING the terrain (a steep step becomes a waterfall).
        // The regression this guards: a normal (non-waterfall) river column must never leave floating water — the
        // cell under its carved bed is solid, and its surface cell holds water/aquatic flora.
        var content = Content();
        var planet = content.GetPlanet("jungle")!;
        var gen = new WorldGenerator(7, content);
        var field = gen.RiverFieldFor(planet);
        Assert.True(field.ColumnCount > 0, "a wet world produced no routed river columns");

        int cs = WorldConstants.ChunkSize;
        var aquatic = new System.Collections.Generic.HashSet<ushort>
        {
            content.GetBlock("water")!.NumericId.Value,
            content.GetBlock("flora_kelp")!.NumericId.Value,
            content.GetBlock("flora_lily")!.NumericId.Value,
            content.GetBlock("flora_coral")!.NumericId.Value,
            content.GetBlock("flora_seagrass")!.NumericId.Value,
        };

        int verified = 0;
        for (int wx = 0; wx < 1200 && verified < 60; wx++)
            for (int wz = 0; wz < 1200 && verified < 60; wz++)
            {
                // SurfaceRiverDepth is the truth Generate uses: it excludes pond columns (pond precedence) and
                // sea-owned columns, so a >0 here means Generate actually places this river. Use the field only
                // for the bed/surface levels.
                if (gen.SurfaceRiverDepth(planet, wx, wz) <= 0) continue;
                if (!field.TryGet(wx, wz, out var col) || col.WaterfallDrop > 0)
                {
                    continue; // waterfall columns intentionally stand water above the lower ground
                }

                // No floating water: the bed cell is solid (supports the column) and the surface cell is water.
                var bedCoord = new ChunkCoord(wx / cs, col.BedY / cs, wz / cs);
                var bedChunk = gen.Generate(planet, bedCoord);
                Assert.False(bedChunk.Get(wx % cs, ((col.BedY % cs) + cs) % cs, wz % cs).IsAir,
                    $"river bed under ({wx},{wz}) is air — floating water");

                var surfCoord = new ChunkCoord(wx / cs, col.WaterSurfaceY / cs, wz / cs);
                var surfChunk = gen.Generate(planet, surfCoord);
                Assert.Contains(surfChunk.Get(wx % cs, ((col.WaterSurfaceY % cs) + cs) % cs, wz % cs).Value, aquatic);
                verified++;
            }

        Assert.True(verified > 0, "found no normal river columns to verify");
    }

    [Fact]
    public void FloraWorld_GrowsTrees()
    {
        var content = Content();
        var planet = content.GetPlanet("jungle")!; // grass surface + flora → multi-block trees
        var gen = new WorldGenerator(777, content);
        ushort log = content.GetBlock("wood_log")!.NumericId.Value;
        ushort leaves = content.GetBlock("tree_leaves")!.NumericId.Value;

        Assert.True(CountBlock(gen, planet, log) > 0, "A flora world should grow tree trunks.");
        Assert.True(CountBlock(gen, planet, leaves) > 0, "…topped with leaf crowns.");
    }

    [Fact]
    public void AirlessWorld_HasNoWater()
    {
        var content = Content();
        var planet = content.GetPlanet("asteroid")!; // no atmosphere → no water anywhere
        var gen = new WorldGenerator(777, content);
        ushort water = content.GetBlock("water")!.NumericId.Value;

        Assert.Equal(0, CountBlock(gen, planet, water));
    }

    [Fact]
    public void VolcanicWorld_FillsBasinsWithLava()
    {
        var content = Content();
        var planet = content.GetPlanet("lava")!; // airless + basalt → lava seas, no water
        var gen = new WorldGenerator(777, content);
        ushort lava = content.GetBlock("lava")!.NumericId.Value;
        ushort water = content.GetBlock("water")!.NumericId.Value;

        // Scan around the world's ACTUAL lava sea level — the percentile-based level moves with the
        // terrain (and #576's rift gorges can pull it below the old fixed Y 48..95 window).
        int sea = gen.SeaLevel(planet);
        Assert.True(sea > int.MinValue, "A volcanic world should have a lava sea level.");
        int seaCy = WorldConstants.WorldToChunk(sea);

        Assert.True(CountBlock(gen, planet, lava, seaCy - 1, seaCy + 1) > 0,
            "A volcanic world should pool lava in its basins.");
        Assert.Equal(0, CountBlock(gen, planet, water, seaCy - 1, seaCy + 1));
    }

    [Fact]
    public void IceWorld_SeaFreezesOver_NoDivingFromTheSurface()
    {
        // #494: on a deep-frozen world (ice type, −38 ± 6 °C at the waterline) every sea column carries
        // at least a 4-block ice cap — often frozen through — so the waterline is walkable solid ice and
        // there is no open water to fall into from the surface.
        var content = Content();
        var planet = content.GetPlanet("ice")!;
        var gen = new WorldGenerator(7, content);
        int sea = gen.SeaLevel(planet);
        Assert.True(sea > int.MinValue, "expected the icy world to pool a (frozen) sea");
        var iceId = content.GetBlock("ice")!.NumericId;
        int cs = WorldConstants.ChunkSize;

        var chunks = new System.Collections.Generic.Dictionary<ChunkCoord, ChunkData>();
        BlockId At(int wx, int wy, int wz)
        {
            var c = new ChunkCoord(wx / cs, wy / cs, wz / cs);
            if (!chunks.TryGetValue(c, out var ch))
            {
                chunks[c] = ch = gen.Generate(planet, c);
            }

            return ch.Get(wx % cs, wy % cs, wz % cs);
        }

        int verified = 0;
        for (int x = 0; x < 1024 && verified < 12; x += 5)
            for (int z = 0; z < 512 && verified < 12; z += 5)
            {
                int depth = sea - gen.SurfaceHeight(planet, x, z);
                if (depth < 4)
                {
                    continue; // want genuinely deep sea columns — shallows freeze through trivially
                }

                int ice = gen.SurfaceIceThickness(planet, x, z);
                Assert.True(ice >= 4, $"deep-frozen sea column at ({x},{z}) carries only {ice} ice");

                // Whatever a would-be diver touches from above is solid ice, not water.
                Assert.Equal(iceId, At(x, sea, z));
                Assert.Equal(iceId, At(x, sea - 3, z));

                // Any liquid the helper still reports sits BELOW the cap, never at the waterline.
                if (gen.TryGetWaterSurface(planet, x, z, out int top, out _))
                {
                    Assert.True(top <= sea - 4, "liquid water reported inside the ice cap");
                }

                verified++;
            }

        Assert.True(verified > 0, "expected to find deep sea columns on the icy world");
    }

    [Fact]
    public void TundraWorld_IceSheet_KeepsLiquidWaterBelow()
    {
        // #494: a merely-cold world (tundra, −22 ± 6 °C) freezes a 1..4-block sheet you can mine
        // through — with real liquid water (and the fauna the helpers place there) underneath on deep
        // bodies. The helper trio must agree exactly with the generated blocks.
        var content = Content();
        var planet = content.GetPlanet("tundra")!;
        var gen = new WorldGenerator(7, content);
        int sea = gen.SeaLevel(planet);
        Assert.True(sea > int.MinValue, "expected the tundra world to pool a sea");
        var iceId = content.GetBlock("ice")!.NumericId;
        var waterId = content.GetBlock("water")!.NumericId;
        int cs = WorldConstants.ChunkSize;

        var chunks = new System.Collections.Generic.Dictionary<ChunkCoord, ChunkData>();
        BlockId At(int wx, int wy, int wz)
        {
            var c = new ChunkCoord(wx / cs, wy / cs, wz / cs);
            if (!chunks.TryGetValue(c, out var ch))
            {
                chunks[c] = ch = gen.Generate(planet, c);
            }

            return ch.Get(wx % cs, wy % cs, wz % cs);
        }

        int verified = 0;
        for (int x = 0; x < 1024 && verified < 12; x += 5)
            for (int z = 0; z < 512 && verified < 12; z += 5)
            {
                int surfaceY = gen.SurfaceHeight(planet, x, z);
                int depth = sea - surfaceY;
                if (depth < 6)
                {
                    continue; // deep water — a sheet (max 4) can never freeze it through
                }

                int ice = gen.SurfaceIceThickness(planet, x, z);
                Assert.InRange(ice, 1, depth - 1);

                // The liquid column the fauna spawner sees starts right under the sheet.
                Assert.True(gen.TryGetWaterSurface(planet, x, z, out int top, out int bed));
                Assert.Equal(sea - ice, top);
                Assert.Equal(surfaceY, bed);

                Assert.Equal(iceId, At(x, sea, z));           // frozen waterline…
                Assert.Equal(iceId, At(x, sea - ice + 1, z)); // …down to the sheet's underside
                Assert.Equal(waterId, At(x, top, z));         // liquid directly below it
                verified++;
            }

        Assert.True(verified > 0, "expected to find deep sea columns on the tundra world");
    }

    [Fact]
    public void WarmWorld_SeaStaysLiquid_NoIceSheet()
    {
        // #494 guard: the freeze pass must not touch warm seas — a jungle world's waterline stays
        // open water, and the water-surface helper still reports the sea level itself.
        var content = Content();
        var planet = content.GetPlanet("jungle")!;
        var gen = new WorldGenerator(7, content);
        int sea = gen.SeaLevel(planet);
        Assert.True(sea > int.MinValue, "expected the jungle world to pool a sea");

        int verified = 0;
        for (int x = 0; x < 512 && verified < 50; x += 5)
            for (int z = 0; z < 512 && verified < 50; z += 5)
            {
                if (sea - gen.SurfaceHeight(planet, x, z) < 2)
                {
                    continue;
                }

                Assert.Equal(0, gen.SurfaceIceThickness(planet, x, z));
                Assert.True(gen.TryGetWaterSurface(planet, x, z, out int top, out _));
                Assert.Equal(sea, top);
                verified++;
            }

        Assert.True(verified > 0, "expected to find sea columns on the jungle world");
    }
}
