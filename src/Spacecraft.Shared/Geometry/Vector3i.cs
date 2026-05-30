namespace Spacecraft.Shared.Geometry;

/// <summary>
/// Integer 3D vector used for block/world coordinates. Immutable value type.
/// </summary>
public readonly struct Vector3i : IEquatable<Vector3i>
{
    public readonly int X;
    public readonly int Y;
    public readonly int Z;

    public Vector3i(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static readonly Vector3i Zero = new(0, 0, 0);
    public static readonly Vector3i One = new(1, 1, 1);

    public static Vector3i operator +(Vector3i a, Vector3i b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3i operator -(Vector3i a, Vector3i b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3i operator *(Vector3i a, int s) => new(a.X * s, a.Y * s, a.Z * s);

    public static bool operator ==(Vector3i a, Vector3i b) => a.Equals(b);
    public static bool operator !=(Vector3i a, Vector3i b) => !a.Equals(b);

    /// <summary>Squared Euclidean distance — avoids a sqrt for range checks.</summary>
    public int DistanceSquared(Vector3i other)
    {
        int dx = X - other.X;
        int dy = Y - other.Y;
        int dz = Z - other.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    public bool Equals(Vector3i other) => X == other.X && Y == other.Y && Z == other.Z;

    public override bool Equals(object? obj) => obj is Vector3i other && Equals(other);

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

    public override string ToString() => $"({X}, {Y}, {Z})";
}
