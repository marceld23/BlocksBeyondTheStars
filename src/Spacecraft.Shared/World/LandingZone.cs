namespace Spacecraft.Shared.World;

/// <summary>
/// A player's personal landing zone on a location (technical requirements /
/// `anf_space_flight.md` §3): a reserved, optionally protected area where the player's ship
/// lands. Horizontal protection is a square of <see cref="Radius"/> around the center.
/// </summary>
public sealed class LandingZone
{
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>Location id (planet type / body) this zone belongs to.</summary>
    public string LocationId { get; set; } = string.Empty;

    public int CenterX { get; set; }
    public int CenterZ { get; set; }
    public int Radius { get; set; } = 8;

    /// <summary>When protected, other players may not mine/place/build inside it.</summary>
    public bool Protected { get; set; }

    public bool Contains(int x, int z)
        => System.Math.Abs(x - CenterX) <= Radius && System.Math.Abs(z - CenterZ) <= Radius;
}
