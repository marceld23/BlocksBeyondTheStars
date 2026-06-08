using Spacecraft.Shared.Content;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Primitives;
using Spacecraft.Shared.World;
using Spacecraft.WorldGeneration;
using Xunit;

namespace Spacecraft.Tests;

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

        int prev = gen.SurfaceHeight(planet, 0, 0);
        for (int x = 1; x < 64; x++)
        {
            int h = gen.SurfaceHeight(planet, x, 0);
            Assert.True(System.Math.Abs(h - prev) <= 4, $"Unexpectedly steep terrain step at x={x}.");
            prev = h;
        }
    }

    private static ushort SurfaceBlockAt(WorldGenerator gen, Spacecraft.Shared.Definitions.PlanetType planet, int x, int z)
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
        var content = Content();
        var planet = content.GetPlanet("desert")!;
        var gen = new WorldGenerator(2024, content);
        ushort sand = content.GetBlock("sand")!.NumericId.Value;

        Assert.Equal(sand, SurfaceBlockAt(gen, planet, 10, 10));
    }

    [Fact]
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
    public void GeneratedOres_AreAmongPlanetDefinition()
    {
        var content = Content();
        var planet = content.GetPlanet("rocky")!;
        var gen = new WorldGenerator(77, content);

        var allowed = new HashSet<ushort> { BlockId.AirValue };
        allowed.Add(content.GetBlock(planet.SurfaceBlock)!.NumericId.Value);
        allowed.Add(content.GetBlock(planet.SubSurfaceBlock)!.NumericId.Value);
        allowed.Add(content.GetBlock(planet.DeepBlock)!.NumericId.Value);
        allowed.Add(content.GetBlock("data_cache")!.NumericId.Value);
        allowed.Add(content.GetBlock("water")!.NumericId.Value); // surface seas fill basins below sea level
        allowed.Add(content.GetBlock("lava")!.NumericId.Value);
        foreach (var ore in planet.Ores)
        {
            allowed.Add(content.GetBlock(ore.Block)!.NumericId.Value);
        }

        for (int cy = 0; cy <= 5; cy++)
        {
            var chunk = gen.Generate(planet, new ChunkCoord(0, cy, 0));
            foreach (var b in chunk.RawBlocks)
            {
                Assert.Contains(b, allowed);
            }
        }
    }

    private static int CountBlock(WorldGenerator gen, Spacecraft.Shared.Definitions.PlanetType planet, ushort block)
    {
        int n = 0;
        for (int cx = 0; cx < 8; cx++)
        for (int cz = 0; cz < 8; cz++)
        for (int cy = 3; cy <= 5; cy++) // a wide span around typical sea levels (Y ≈ 48..95)
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

        Assert.True(CountBlock(gen, planet, lava) > 0, "A volcanic world should pool lava in its basins.");
        Assert.Equal(0, CountBlock(gen, planet, water));
    }
}
