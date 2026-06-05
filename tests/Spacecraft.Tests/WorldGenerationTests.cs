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
}
