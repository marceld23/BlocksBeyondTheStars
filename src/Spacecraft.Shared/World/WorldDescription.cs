namespace Spacecraft.Shared.World;

/// <summary>Relative frequency for procedural features (maps to a spawn weight).</summary>
public enum Frequency
{
    Off,
    VeryRare,
    Rare,
    Normal,
    Frequent,
}

public static class FrequencyExtensions
{
    /// <summary>Selection weight; 0 means the feature never spawns.</summary>
    public static int Weight(this Frequency f) => f switch
    {
        Frequency.Off => 0,
        Frequency.VeryRare => 1,
        Frequency.Rare => 3,
        Frequency.Normal => 8,
        Frequency.Frequent => 16,
        _ => 0,
    };

    /// <summary>Probability in [0,1] used for per-body chance rolls.</summary>
    public static double Probability(this Frequency f) => f switch
    {
        Frequency.Off => 0.0,
        Frequency.VeryRare => 0.05,
        Frequency.Rare => 0.15,
        Frequency.Normal => 0.4,
        Frequency.Frequent => 0.75,
        _ => 0.0,
    };
}

/// <summary>
/// The admin-defined structured description of a world's universe (technical requirements /
/// `anf_admin_einstellungen.md` §8): how many systems, how dense the bodies, which planet
/// types appear and how often. Combined with the seed it deterministically produces the
/// galaxy layout.
/// </summary>
public sealed class WorldDescription
{
    public int StarSystemCount { get; set; } = 8;

    public int PlanetsPerSystemMin { get; set; } = 2;
    public int PlanetsPerSystemMax { get; set; } = 6;

    public int MoonsPerPlanetMin { get; set; }
    public int MoonsPerPlanetMax { get; set; } = 3;

    public Frequency AsteroidFields { get; set; } = Frequency.Normal;
    public Frequency SpaceStations { get; set; } = Frequency.Rare;
    public Frequency Wrecks { get; set; } = Frequency.Normal;

    /// <summary>Planet-type key → frequency. Empty means "use all known planet types at Normal".</summary>
    public Dictionary<string, Frequency> PlanetTypeFrequencies { get; set; } = new();

    public Frequency RareResources { get; set; } = Frequency.Rare;
    public Frequency Danger { get; set; } = Frequency.Normal;
}
