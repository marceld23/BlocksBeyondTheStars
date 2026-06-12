namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>A planned landing pad the generator levels at generation time (ship-as-object: the landed
/// ship is a placed structure that needs flat, clear ground — terrain is never mutated per landing).
/// Positions come from the server's deterministic pad planning (same every load).</summary>
public readonly struct LandingPadFlatten
{
    public readonly int CenterX;
    public readonly int CenterZ;
    public readonly int SurfaceY;
    public readonly int Radius;

    public LandingPadFlatten(int centerX, int centerZ, int surfaceY, int radius)
    {
        CenterX = centerX;
        CenterZ = centerZ;
        SurfaceY = surfaceY;
        Radius = radius;
    }
}
