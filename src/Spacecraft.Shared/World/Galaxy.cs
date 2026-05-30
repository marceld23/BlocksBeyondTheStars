namespace Spacecraft.Shared.World;

/// <summary>Kind of celestial body shown on the star map.</summary>
public enum CelestialKind
{
    Planet,
    Moon,
    AsteroidField,
    SpaceStation,
    Wreck,
}

/// <summary>Lifecycle of a location (technical requirements / `anf_admin_einstellungen.md` §9.4).</summary>
public enum GenerationStatus
{
    NotGenerated,
    Generated,
    Discovered,
    Visited,
}

/// <summary>A single body in a star system (planet, moon, asteroid field, station, wreck).</summary>
public sealed class CelestialBody
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CelestialKind Kind { get; set; }

    /// <summary>Planet-type key for planets/moons (else null).</summary>
    public string? PlanetType { get; set; }

    public string SystemId { get; set; } = string.Empty;
    public GenerationStatus Status { get; set; } = GenerationStatus.NotGenerated;
}

/// <summary>A star system: a named cluster of bodies on the star map.</summary>
public sealed class StarSystem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>2D star-map coordinates.</summary>
    public float MapX { get; set; }
    public float MapY { get; set; }

    public List<CelestialBody> Bodies { get; set; } = new();
}

/// <summary>The full procedurally generated universe layout for a world.</summary>
public sealed class Galaxy
{
    public List<StarSystem> Systems { get; set; } = new();

    public IEnumerable<CelestialBody> AllBodies()
    {
        foreach (var system in Systems)
        {
            foreach (var body in system.Bodies)
            {
                yield return body;
            }
        }
    }

    public CelestialBody? FindBody(string id)
    {
        foreach (var body in AllBodies())
        {
            if (body.Id == id)
            {
                return body;
            }
        }

        return null;
    }
}
