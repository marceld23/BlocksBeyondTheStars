namespace Spacecraft.Shared.Definitions;

/// <summary>
/// A buildable ship module (the second progression step after its blueprint is
/// unlocked), loaded from <c>data/ship_modules.json</c>. Stats are intentionally a
/// free-form key→value map so new module effects can be added without code changes.
/// </summary>
public sealed class ShipModuleDefinition
{
    public string Key { get; set; } = string.Empty;
    public string NameKey { get; set; } = string.Empty;
    public string DescriptionKey { get; set; } = string.Empty;

    /// <summary>Blueprint that must be unlocked before this module can be built.</summary>
    public string? RequiredBlueprint { get; set; }

    /// <summary>Whether this module is mandatory and cannot be removed (cockpit, power, life support).</summary>
    public bool Mandatory { get; set; }

    /// <summary>Resources consumed to build the module.</summary>
    public List<ItemAmount> BuildCost { get; set; } = new();

    /// <summary>
    /// Stat contributions, e.g. "cargo_capacity": 200, "oxygen_production": 5.
    /// Aggregated by the server to compute the ship's effective stats.
    /// </summary>
    public Dictionary<string, double> Stats { get; set; } = new();
}
