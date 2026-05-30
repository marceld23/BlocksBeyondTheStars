using Spacecraft.Shared.Geometry;

namespace Spacecraft.Shared.World;

/// <summary>
/// Fundamental world/chunk dimensions and coordinate conversions.
/// A chunk is a cube of <see cref="ChunkSize"/> blocks per axis.
/// </summary>
public static class WorldConstants
{
    public const int ChunkSize = 16;
    public const int BlocksPerChunk = ChunkSize * ChunkSize * ChunkSize;

    /// <summary>Floor-divides a world coordinate to its chunk coordinate (handles negatives).</summary>
    public static int WorldToChunk(int world) => (int)System.Math.Floor(world / (double)ChunkSize);

    /// <summary>Maps a world coordinate to the local [0, ChunkSize) coordinate inside its chunk.</summary>
    public static int WorldToLocal(int world)
    {
        int local = world % ChunkSize;
        return local < 0 ? local + ChunkSize : local;
    }

    public static ChunkCoord WorldToChunk(Vector3i world)
        => new(WorldToChunk(world.X), WorldToChunk(world.Y), WorldToChunk(world.Z));

    public static Vector3i WorldToLocal(Vector3i world)
        => new(WorldToLocal(world.X), WorldToLocal(world.Y), WorldToLocal(world.Z));

    /// <summary>World-space origin (minimum corner) of the given chunk.</summary>
    public static Vector3i ChunkOrigin(ChunkCoord chunk)
        => new(chunk.X * ChunkSize, chunk.Y * ChunkSize, chunk.Z * ChunkSize);

    /// <summary>Flat array index for a local block position. Assumes 0 &lt;= coord &lt; ChunkSize.</summary>
    public static int LocalIndex(int x, int y, int z) => (y * ChunkSize + z) * ChunkSize + x;
}
