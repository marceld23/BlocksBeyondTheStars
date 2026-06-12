namespace BlocksBeyondTheStars.Shared.World;

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

    /// <summary>Flora/tree density factor (world options; Normal = unchanged, Off = barren).</summary>
    public static double FloraFactor(this Frequency f) => f switch
    {
        Frequency.Off => 0.0,
        Frequency.VeryRare => 0.45,
        Frequency.Rare => 0.7,
        Frequency.Normal => 1.0,
        Frequency.Frequent => 1.6,
        _ => 1.0,
    };

    /// <summary>Ore-richness factor (world options). The DEFAULT of <see cref="WorldDescription.RareResources"/>
    /// is Rare, so Rare maps to 1.0 (existing worlds unchanged); Off still leaves lean veins — a world
    /// with no ore at all would dead-end the progression.</summary>
    public static double OreFactor(this Frequency f) => f switch
    {
        Frequency.Off => 0.55,
        Frequency.VeryRare => 0.75,
        Frequency.Rare => 1.0,
        Frequency.Normal => 1.35,
        Frequency.Frequent => 1.8,
        _ => 1.0,
    };

    /// <summary>Structure-density factor (settlements/wrecks/vaults; Normal = today's moderate density).</summary>
    public static double StructureFactor(this Frequency f) => f switch
    {
        Frequency.Off => 0.0,
        Frequency.VeryRare => 0.4,
        Frequency.Rare => 0.7,
        Frequency.Normal => 1.0,
        Frequency.Frequent => 1.7,
        _ => 1.0,
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

    // --- World options (creation-time; baked into the save's metadata — they shape worldgen) ---

    /// <summary>Flora/tree density factor applied on top of each world's seeded variation.</summary>
    public Frequency FloraDensity { get; set; } = Frequency.Normal;

    /// <summary>How readily inhabited/ruined settlements stamp on suitable worlds.</summary>
    public Frequency Settlements { get; set; } = Frequency.Normal;

    /// <summary>How readily a crashed-ship wreck stamps on a world's surface.</summary>
    public Frequency PlanetWrecks { get; set; } = Frequency.Normal;

    /// <summary>How readily buried vault ruins stamp (0–2 per world at Normal).</summary>
    public Frequency Vaults { get; set; } = Frequency.Normal;

    /// <summary>Scales the spawn weights of EXOTIC planet types (data flag `exotic` in planets.json)
    /// relative to the common ones — one slider instead of 17 (an advanced per-type page can still
    /// fill <see cref="PlanetTypeFrequencies"/>, which always wins when non-empty).</summary>
    public Frequency ExoticWorlds { get; set; } = Frequency.Normal;
}
