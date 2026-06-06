using Spacecraft.Shared.Content;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Primitives;
using Spacecraft.Shared.World;
using Spacecraft.WorldGeneration;
using Xunit;

namespace Spacecraft.Tests;

/// <summary>
/// World wrap (W1): world-X is a wrapping longitude (cylinder world). These assert the user's core
/// requirement — the eastern edge of the map lines up <i>exactly</i> with the western edge, with no
/// visible seam — by checking generation is periodic across X = 0 ≡ X = Circumference and continuous
/// (no cliff) where the seam meets.
/// </summary>
public class WorldWrapTests
{
    private static GameContent Content() => ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private const int C = WorldConstants.Circumference;

    [Fact]
    public void Circumference_IsAWholeNumberOfChunks()
    {
        Assert.Equal(0, C % WorldConstants.ChunkSize);
        Assert.Equal(C / WorldConstants.ChunkSize, WorldConstants.ChunksAround);
    }

    [Theory]
    [InlineData("rocky")]
    [InlineData("desert")]
    [InlineData("varied")]
    public void SurfaceHeight_IsPeriodic_AcrossTheSeam(string planetKey)
    {
        var content = Content();
        var planet = content.GetPlanet(planetKey)!;
        var gen = new WorldGenerator(4242, content);

        for (int z = -200; z <= 200; z += 23)
        {
            // The same longitude reached the long way round must be the identical column.
            Assert.Equal(gen.SurfaceHeight(planet, 0, z), gen.SurfaceHeight(planet, C, z));
            Assert.Equal(gen.SurfaceHeight(planet, 7, z), gen.SurfaceHeight(planet, C + 7, z));
            Assert.Equal(gen.SurfaceHeight(planet, -3, z), gen.SurfaceHeight(planet, C - 3, z));
        }
    }

    [Fact]
    public void SurfaceHeight_HasNoCliff_AtTheSeam()
    {
        var content = Content();
        var planet = content.GetPlanet("rocky")!;
        var gen = new WorldGenerator(555, content);

        // The step from the last column before the seam (C-1) to the first after (C ≡ 0) must be no
        // larger than an ordinary neighbour step — i.e. the seam is invisible, not a wall.
        for (int z = 0; z < 400; z += 17)
        {
            int across = System.Math.Abs(gen.SurfaceHeight(planet, C, z) - gen.SurfaceHeight(planet, C - 1, z));
            Assert.True(across <= 4, $"Seam cliff at z={z}: step {across}.");
        }
    }

    [Theory]
    [InlineData("varied")]
    [InlineData("rocky")]
    public void BiomeIndex_IsPeriodic_AcrossTheSeam(string planetKey)
    {
        var content = Content();
        var planet = content.GetPlanet(planetKey)!;
        var gen = new WorldGenerator(2024, content);

        for (int z = -300; z <= 300; z += 31)
        {
            Assert.Equal(gen.BiomeIndexAt(planet, 0, z), gen.BiomeIndexAt(planet, C, z));
            Assert.Equal(gen.BiomeIndexAt(planet, 12, z), gen.BiomeIndexAt(planet, C + 12, z));
        }
    }

    [Fact]
    public void GeneratedColumn_IsIdentical_AcrossTheSeam()
    {
        // The strongest check: the full block column (caves, ore, surface, flora) at the chunk straddling
        // the seam must match the chunk a whole lap away — blocks included.
        var content = Content();
        var planet = content.GetPlanet("rocky")!;
        var gen = new WorldGenerator(31415, content);

        int chunkY = 3;
        var west = gen.Generate(planet, new ChunkCoord(0, chunkY, 1));               // columns x = 0..15
        var east = gen.Generate(planet, new ChunkCoord(WorldConstants.ChunksAround, chunkY, 1)); // x = C..C+15

        Assert.True(west.RawBlocks.SequenceEqual(east.RawBlocks),
            "Chunk a full lap away must regenerate identically (seam-free wrap).");
    }

    [Fact]
    public void CanonicalChunk_And_Block_WrapLongitudeOnly()
    {
        // A chunk / block a whole lap away canonicalizes to the same X, leaving Y and Z untouched.
        var chunk = WorldConstants.CanonicalChunk(new ChunkCoord(WorldConstants.ChunksAround + 2, 5, -3));
        Assert.Equal(new ChunkCoord(2, 5, -3), chunk);

        var block = WorldConstants.CanonicalBlock(new Vector3i(C + 9, 70, -40));
        Assert.Equal(new Vector3i(9, 70, -40), block);

        // The chunk straddling the seam edge maps cleanly (16 divides C, so no chunk straddles it).
        Assert.Equal(0, WorldConstants.CanonicalChunkX(WorldConstants.ChunksAround));
        Assert.Equal(WorldConstants.ChunksAround - 1, WorldConstants.CanonicalChunkX(-1));
    }

    [Fact]
    public void WrapX_And_WrapDeltaX_BehaveAcrossTheSeam()
    {
        Assert.Equal(0, WorldConstants.WrapX(C));
        Assert.Equal(1, WorldConstants.WrapX(C + 1));
        Assert.Equal(C - 1, WorldConstants.WrapX(-1));

        // Shortest way round: 2 east from C-1 to (C+1 ≡ 1) is +2, not -(C-2).
        Assert.Equal(2, WorldConstants.WrapDeltaX(2));
        Assert.Equal(-2, WorldConstants.WrapDeltaX(-2));
        Assert.Equal(1, WorldConstants.WrapDeltaX(C + 1));
        Assert.Equal(-1, WorldConstants.WrapDeltaX(C - 1)); // C-1 east == 1 west
        Assert.True(System.Math.Abs(WorldConstants.WrapDeltaX(C / 2 + 5)) <= C / 2);
    }

    [Fact]
    public void WrapDistanceSquared_MeasuresProximityAcrossTheSeam()
    {
        // Two points 4 blocks apart across X = 0 ≡ C read as 4², not (C-4)² — so a creature/door/vendor just
        // across the seam counts as adjacent. (This is what the surface proximity checks now use.)
        var west = new Vector3f(C - 2, 64, 10);
        var east = new Vector3f(2, 64, 10);
        Assert.Equal(16.0, WorldConstants.WrapDistanceSquared(west, east), 3);

        // Y and Z stay linear (not wrapped).
        var a = new Vector3f(2, 64, 10);
        var b = new Vector3f(2, 70, 18);
        Assert.Equal(36.0 + 64.0, WorldConstants.WrapDistanceSquared(a, b), 3);
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(6000)]
    [InlineData(9008)]
    public void WrapHelpers_ArePeriodic_ForAnyCircumference(int circ)
    {
        Assert.Equal(0, WorldConstants.WrapX(circ, circ));
        Assert.Equal(WorldConstants.WrapX(100, circ), WorldConstants.WrapX(circ + 100, circ));
        Assert.Equal(-1, WorldConstants.WrapDeltaX(circ - 1, circ)); // one east of the seam == one west
        Assert.Equal(circ / 16, WorldConstants.ChunksAroundOf(circ));
        Assert.Equal(circ / 4, WorldConstants.LatitudeLimitFor(circ));
    }

    [Fact]
    public void CircumferenceFor_SizesByClass_DeterministicAndChunkAligned()
    {
        int ast = WorldConstants.CircumferenceFor("body-a", WorldConstants.WorldSizeClass.Asteroid);
        int moon = WorldConstants.CircumferenceFor("body-a", WorldConstants.WorldSizeClass.Moon);
        int planet = WorldConstants.CircumferenceFor("body-a", WorldConstants.WorldSizeClass.Planet);

        Assert.InRange(ast, 800, 1600);
        Assert.InRange(moon, 2500, 4000);
        Assert.InRange(planet, 5000, 12000);
        Assert.True(ast < moon && moon < planet, "asteroid < moon < planet");

        Assert.Equal(0, planet % WorldConstants.ChunkSize); // whole chunks → seam tiles cleanly
        Assert.Equal(planet, WorldConstants.CircumferenceFor("body-a", WorldConstants.WorldSizeClass.Planet)); // deterministic
    }

    [Fact]
    public void SizeClassFor_DistinguishesAsteroidMoonPlanet()
    {
        Assert.Equal(WorldConstants.WorldSizeClass.Asteroid, WorldConstants.SizeClassFor(CelestialKind.Planet, "asteroid"));
        Assert.Equal(WorldConstants.WorldSizeClass.Moon, WorldConstants.SizeClassFor(CelestialKind.Moon, "rocky"));
        Assert.Equal(WorldConstants.WorldSizeClass.Planet, WorldConstants.SizeClassFor(CelestialKind.Planet, "rocky"));
    }
}
