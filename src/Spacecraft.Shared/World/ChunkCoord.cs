namespace Spacecraft.Shared.World;

/// <summary>
/// Identifies a chunk in the chunk grid (one unit = <see cref="WorldConstants.ChunkSize"/> blocks).
/// </summary>
public readonly struct ChunkCoord : IEquatable<ChunkCoord>
{
    public readonly int X;
    public readonly int Y;
    public readonly int Z;

    public ChunkCoord(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public int DistanceSquared(ChunkCoord other)
    {
        int dx = X - other.X;
        int dy = Y - other.Y;
        int dz = Z - other.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    public bool Equals(ChunkCoord other) => X == other.X && Y == other.Y && Z == other.Z;

    public override bool Equals(object? obj) => obj is ChunkCoord other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + X;
            hash = hash * 31 + Y;
            hash = hash * 31 + Z;
            return hash;
        }
    }

    public override string ToString() => $"Chunk({X}, {Y}, {Z})";

    public static bool operator ==(ChunkCoord a, ChunkCoord b) => a.Equals(b);
    public static bool operator !=(ChunkCoord a, ChunkCoord b) => !a.Equals(b);
}
