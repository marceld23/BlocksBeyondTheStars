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

    // --- Longitude wrap helpers ---
    // Each takes an explicit circumference so a world can be any size; the no-arg overloads use the default
    // Circumference (6000) so existing callers/tests are unaffected. ChunksAround/LatitudeLimit have per-circ
    // forms too (a world's chunk count + pole bound scale with its size).

    /// <summary>Chunk columns around a world of the given circumference.</summary>
    public static int ChunksAroundOf(int circumference) => circumference / ChunkSize;

    /// <summary>Latitude (Z) bound from the equator for a world of the given circumference (a quarter of it).</summary>
    public static int LatitudeLimitFor(int circumference) => circumference / 4;

    /// <summary>Wraps a world-X coordinate into [0, circumference).</summary>
    public static int WrapX(int x, int circumference)
    {
        int m = x % circumference;
        return m < 0 ? m + circumference : m;
    }

    public static int WrapX(int x) => WrapX(x, Circumference);

    /// <summary>Wraps a continuous world-X coordinate into [0, circumference).</summary>
    public static double WrapX(double x, int circumference)
    {
        double m = x % circumference;
        return m < 0 ? m + circumference : m;
    }

    public static double WrapX(double x) => WrapX(x, Circumference);

    /// <summary>Shortest signed east–west distance across the seam, in (-circ/2, +circ/2]. Use for every
    /// X-axis distance/direction calculation.</summary>
    public static int WrapDeltaX(int dx, int circumference)
    {
        int m = ((dx % circumference) + circumference) % circumference; // [0, C)
        return m > circumference / 2 ? m - circumference : m;
    }

    public static int WrapDeltaX(int dx) => WrapDeltaX(dx, Circumference);

    /// <summary>Shortest signed east–west distance (continuous) across the seam, in (-circ/2, +circ/2].</summary>
    public static double WrapDeltaX(double dx, int circumference)
    {
        double c = circumference;
        double m = ((dx % c) + c) % c;
        return m > c / 2 ? m - c : m;
    }

    public static double WrapDeltaX(double dx) => WrapDeltaX(dx, Circumference);

    /// <summary>Squared distance between two surface-world positions measured the short way round the
    /// longitude seam (X wraps; Y/Z are linear). Use for every on-planet proximity check so a creature, door,
    /// vendor or container just across the seam reads as adjacent, not a world away. (Don't use in space.)</summary>
    public static double WrapDistanceSquared(Vector3f a, Vector3f b, int circumference)
    {
        double dx = WrapDeltaX((double)a.X - b.X, circumference);
        double dy = (double)a.Y - b.Y;
        double dz = (double)a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    public static double WrapDistanceSquared(Vector3f a, Vector3f b) => WrapDistanceSquared(a, b, Circumference);

    /// <summary>Wraps a chunk-X index into [0, ChunksAround) for a world of the given circumference.</summary>
    public static int CanonicalChunkX(int chunkX, int circumference)
    {
        int around = ChunksAroundOf(circumference);
        int m = chunkX % around;
        return m < 0 ? m + around : m;
    }

    public static int CanonicalChunkX(int chunkX) => CanonicalChunkX(chunkX, Circumference);

    /// <summary>Canonicalizes a chunk coordinate's longitude (X), leaving Y/Z untouched.</summary>
    public static ChunkCoord CanonicalChunk(ChunkCoord chunk, int circumference)
        => new(CanonicalChunkX(chunk.X, circumference), chunk.Y, chunk.Z);

    public static ChunkCoord CanonicalChunk(ChunkCoord chunk) => CanonicalChunk(chunk, Circumference);

    /// <summary>Canonicalizes a world block position's longitude (X) into [0, circumference).</summary>
    public static Vector3i CanonicalBlock(Vector3i world, int circumference)
        => new(WrapX(world.X, circumference), world.Y, world.Z);

    public static Vector3i CanonicalBlock(Vector3i world) => CanonicalBlock(world, Circumference);

    // --- Per-world size ---

    /// <summary>Rough size class of a celestial body, which sets how big its walkable cylinder is.</summary>
    public enum WorldSizeClass
    {
        /// <summary>Landable asteroid (PlanetType "asteroid") — very small, a quick stroll around.</summary>
        Asteroid,
        /// <summary>A moon — small.</summary>
        Moon,
        /// <summary>A full planet — large.</summary>
        Planet,
    }

    /// <summary>Classifies a body by its star-map kind + planet-type so server (active world) and client
    /// (orbit view) agree on its size. Landable asteroids use PlanetType "asteroid"; moons are
    /// <see cref="CelestialKind.Moon"/>; everything else landable is a planet.</summary>
    public static WorldSizeClass SizeClassFor(CelestialKind kind, string planetKey)
        => string.Equals(planetKey, "asteroid", System.StringComparison.OrdinalIgnoreCase) ? WorldSizeClass.Asteroid
         : kind == CelestialKind.Moon ? WorldSizeClass.Moon
         : WorldSizeClass.Planet;

    /// <summary>A deterministic walkable circumference for a body: very small for asteroids, small for moons,
    /// large for planets, varied within each class from the body key, and rounded to a whole number of chunks
    /// (so chunks tile cleanly across the seam). Same body → same size, on server and client.</summary>
    public static int CircumferenceFor(string bodyKey, WorldSizeClass cls)
    {
        (int lo, int hi) = cls switch
        {
            WorldSizeClass.Asteroid => (800, 1600),
            WorldSizeClass.Moon => (2500, 4000),
            _ => (5000, 12000),
        };

        uint h = (uint)StableHash(bodyKey);
        int span = hi - lo;
        int raw = lo + (int)(h % (uint)span);
        int rounded = (int)System.Math.Round(raw / (double)ChunkSize) * ChunkSize; // whole chunks
        return System.Math.Max(ChunkSize, rounded);
    }

    private static int StableHash(string s)
    {
        int h = 17;
        foreach (char c in s ?? string.Empty)
        {
            h = h * 31 + c;
        }

        return h & 0x7fffffff;
    }

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
