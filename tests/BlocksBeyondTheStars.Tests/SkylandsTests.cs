using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Skylands worlds really get FLOATING islands: solid sky-island slabs hovering above the ground
/// with a clear air gap below them (you can stand on the ground and see an island overhead), and
/// trees cluster into real forest groves instead of a uniform sprinkle.
/// </summary>
public sealed class SkylandsTests
{
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void Skylands_GenerateFloatingIslands_WithAnAirGapBelow()
    {
        var planet = _content.GetPlanet("skylands");
        Assert.NotNull(planet);
        Assert.True(planet!.FloatingIslands, "skylands must be flagged for floating islands");

        var gen = new WorldGenerator(2026, _content);

        // Scan a region of columns for the signature: ground at the surface, AIR above it, then a
        // SOLID island band higher up. Sample full chunk columns over the island altitude window.
        bool foundIsland = false;
        for (int cx = 0; cx < 12 && !foundIsland; cx++)
        for (int cz = 0; cz < 12 && !foundIsland; cz++)
        {
            // The island band lives around BaseHeight+28 (±12 altitude jitter, ±~16 thickness) — chunk
            // Y=5 covers world Y 80..95 for BaseHeight 56; chunk Y=4 covers 64..79.
            foreach (int cy in new[] { 4, 5, 6 })
            {
                var coord = new ChunkCoord(cx, cy, cz);
                var chunk = gen.Generate(planet, coord);
                var origin = WorldConstants.ChunkOrigin(coord);

                for (int lx = 0; lx < WorldConstants.ChunkSize && !foundIsland; lx++)
                for (int lz = 0; lz < WorldConstants.ChunkSize && !foundIsland; lz++)
                {
                    int wx = origin.X + lx, wz = origin.Z + lz;
                    int surface = gen.SurfaceHeight(planet, wx, wz);

                    // Find a solid cell in this chunk column clearly ABOVE the terrain surface…
                    for (int ly = 0; ly < WorldConstants.ChunkSize; ly++)
                    {
                        int wy = origin.Y + ly;
                        if (wy <= surface + 6 || chunk.Get(lx, ly, lz).IsAir)
                        {
                            continue;
                        }

                        // …and verify there is an air gap between the ground and this island cell.
                        var below = gen.Generate(planet, WorldConstants.WorldToChunk(new BlocksBeyondTheStars.Shared.Geometry.Vector3i(wx, surface + 2, wz)));
                        var bo = WorldConstants.ChunkOrigin(WorldConstants.WorldToChunk(new BlocksBeyondTheStars.Shared.Geometry.Vector3i(wx, surface + 2, wz)));
                        if (below.Get(wx - bo.X, surface + 2 - bo.Y, wz - bo.Z).IsAir)
                        {
                            foundIsland = true;
                        }

                        break;
                    }
                }
            }
        }

        Assert.True(foundIsland, "expected at least one floating island (solid band above the surface with air below) in the scanned region");
    }

    [Fact]
    public void Trees_ClusterIntoForestGroves()
    {
        var planet = _content.GetPlanet("jungle");
        Assert.NotNull(planet);
        var gen = new WorldGenerator(2026, _content);
        var logId = _content.GetBlock("wood_log")!.NumericId;

        // Count tree trunks per chunk over a region: forests mean STRONG variance — some chunks hold
        // a grove (many trunks), others are nearly bare. A uniform sprinkle would spread them evenly.
        int total = 0, maxPerChunk = 0, emptyChunks = 0, chunks = 0;
        for (int cx = 0; cx < 10; cx++)
        for (int cz = 0; cz < 10; cz++)
        {
            int trunks = 0;
            foreach (int cy in new[] { 3, 4, 5 }) // surface band for jungle BaseHeight 66
            {
                var chunk = gen.Generate(planet, new ChunkCoord(cx, cy, cz));
                for (int lx = 0; lx < WorldConstants.ChunkSize; lx++)
                for (int ly = 0; ly < WorldConstants.ChunkSize; ly++)
                for (int lz = 0; lz < WorldConstants.ChunkSize; lz++)
                {
                    if (chunk.Get(lx, ly, lz) == logId)
                    {
                        trunks++;
                    }
                }
            }

            chunks++;
            total += trunks;
            if (trunks == 0) emptyChunks++;
            if (trunks > maxPerChunk) maxPerChunk = trunks;
        }

        Assert.True(total > 60, $"a jungle region should hold plenty of trees (found {total} trunk cells in {chunks} chunk columns)");
        Assert.True(maxPerChunk >= 20, $"forests should form dense groves somewhere (densest chunk column: {maxPerChunk} trunk cells)");
        Assert.True(emptyChunks >= 10, $"between groves the land should be nearly bare (only {emptyChunks}/{chunks} chunk columns are treeless)");
    }
}
