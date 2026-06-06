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

    /// <summary>
    /// East–west circumference of every planet, in blocks: world-X is a longitude that wraps, so walking
    /// this far east returns you to your start. The world is a cylinder — latitude (Z) is bounded by poles,
    /// only longitude wraps. A multiple of <see cref="ChunkSize"/> (6000 / 16 = 375 chunks) so chunk columns
    /// tile cleanly across the seam, and the same length the client uses for the longitude day/night
    /// terminator (<c>GameBootstrap.DayCircumference</c>). Terrain is generated seam-free across X = 0 ≡ X =
    /// Circumference via circular-domain noise (see <c>Spacecraft.WorldGeneration.Noise</c>).
    /// </summary>
    public const int Circumference = 6000;

    /// <summary>Number of chunk columns around the world (Circumference / ChunkSize).</summary>
    public const int ChunksAround = Circumference / ChunkSize;

    /// <summary>Latitude (Z) bound from the equator (Z = 0, where players spawn) to each pole. Longitude (X)
    /// wraps freely, but north–south is bounded by an invisible pole barrier at ±this, so the planet feels
    /// finite instead of an infinite N–S strip. ≈ a sphere's pole-to-pole span (half the equator).</summary>
    public const int LatitudeLimit = Circumference / 4;

    /// <summary>Wraps a world-X coordinate into the canonical [0, Circumference) range.</summary>
    public static int WrapX(int x)
    {
        int m = x % Circumference;
        return m < 0 ? m + Circumference : m;
    }

    /// <summary>Wraps a world-X coordinate (continuous) into the canonical [0, Circumference) range.</summary>
    public static double WrapX(double x)
    {
        double m = x % Circumference;
        return m < 0 ? m + Circumference : m;
    }

    /// <summary>Shortest signed east–west distance from <paramref name="dx"/> across the seam, in
    /// (-Circumference/2, +Circumference/2]. Use for every X-axis distance/direction calculation.</summary>
    public static int WrapDeltaX(int dx)
    {
        int m = ((dx % Circumference) + Circumference) % Circumference; // [0, C)
        return m > Circumference / 2 ? m - Circumference : m;
    }

    /// <summary>Shortest signed east–west distance (continuous) across the seam, in (-C/2, +C/2].</summary>
    public static double WrapDeltaX(double dx)
    {
        double c = Circumference;
        double m = ((dx % c) + c) % c;
        return m > c / 2 ? m - c : m;
    }

    /// <summary>Squared distance between two surface-world positions measured the short way round the
    /// longitude seam (X wraps; Y/Z are linear). Use for every on-planet proximity check so a creature, door,
    /// vendor or container just across X = 0 ≡ Circumference reads as adjacent, not a world away. (Don't use
    /// in space, which isn't a cylinder.)</summary>
    public static double WrapDistanceSquared(Vector3f a, Vector3f b)
    {
        double dx = WrapDeltaX((double)a.X - b.X);
        double dy = (double)a.Y - b.Y;
        double dz = (double)a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    /// <summary>Wraps a chunk-X index into the canonical [0, ChunksAround) range (longitude wrap).</summary>
    public static int CanonicalChunkX(int chunkX)
    {
        int m = chunkX % ChunksAround;
        return m < 0 ? m + ChunksAround : m;
    }

    /// <summary>Canonicalizes a chunk coordinate's longitude (X), leaving Y/Z untouched.</summary>
    public static ChunkCoord CanonicalChunk(ChunkCoord chunk)
        => new(CanonicalChunkX(chunk.X), chunk.Y, chunk.Z);

    /// <summary>Canonicalizes a world block position's longitude (X) into [0, Circumference).</summary>
    public static Vector3i CanonicalBlock(Vector3i world)
        => new(WrapX(world.X), world.Y, world.Z);

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
