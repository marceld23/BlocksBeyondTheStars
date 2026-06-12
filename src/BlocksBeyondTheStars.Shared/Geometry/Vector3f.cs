namespace BlocksBeyondTheStars.Shared.Geometry;

/// <summary>
/// Single-precision 3D vector for continuous positions (players, entities) where block
/// granularity is too coarse. Block/world coordinates use <see cref="Vector3i"/>.
/// </summary>
public readonly struct Vector3f : IEquatable<Vector3f>
{
    public readonly float X;
    public readonly float Y;
    public readonly float Z;

    public Vector3f(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static readonly Vector3f Zero = new(0f, 0f, 0f);

    public static Vector3f operator +(Vector3f a, Vector3f b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3f operator -(Vector3f a, Vector3f b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public float DistanceSquared(Vector3f other)
    {
        float dx = X - other.X;
        float dy = Y - other.Y;
        float dz = Z - other.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    public Vector3i ToBlock() => new(
        (int)System.Math.Floor(X),
        (int)System.Math.Floor(Y),
        (int)System.Math.Floor(Z));

    public bool Equals(Vector3f other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
    public override bool Equals(object? obj) => obj is Vector3f other && Equals(other);
    public override int GetHashCode() => (X, Y, Z).GetHashCode();
    public override string ToString() => $"({X:0.##}, {Y:0.##}, {Z:0.##})";
}
