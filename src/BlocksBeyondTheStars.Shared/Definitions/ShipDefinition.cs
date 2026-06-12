namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// A craftable ship type / design (data-driven, <c>data/ships.json</c>). Defines the base
/// combat stats, the hull design footprint the server stamps into the world, the cargo size,
/// the modules it starts with, and the blueprint + cost to craft it. The "starter" type has no
/// blueprint/cost (every player owns it).
/// </summary>
public sealed class ShipDefinition
{
    public string Key { get; set; } = string.Empty;
    public string NameKey { get; set; } = string.Empty;
    public string DescriptionKey { get; set; } = string.Empty;

    public float BaseHull { get; set; } = 100f;
    public float BaseShield { get; set; }

    /// <summary>Flight handling, relative to the balanced starter (1.0). Higher = faster cruise speed
    /// (<see cref="FlightSpeed"/>) and snappier turning (<see cref="Handling"/>). Presentation-side in
    /// the space view today; archetypes: hauler slow/sluggish, scout fast/agile.</summary>
    public float FlightSpeed { get; set; } = 1f;
    public float Handling { get; set; } = 1f;

    /// <summary>Interior footprint (odd numbers read best) + height; drives the stamped hull size.</summary>
    public int InteriorWidth { get; set; } = 5;
    public int InteriorLength { get; set; } = 7;
    public int Height { get; set; } = 4;

    public int CargoSlots { get; set; } = 48;

    /// <summary>Blueprint that must be unlocked to craft this ship (null/empty = always available).</summary>
    public string? RequiredBlueprint { get; set; }

    /// <summary>Resources consumed to craft the ship.</summary>
    public List<ItemAmount> CraftCost { get; set; } = new();

    /// <summary>Modules the ship is built with.</summary>
    public List<string> StartModules { get; set; } = new();

    /// <summary>Optional voxel layout key (a <see cref="ShipLayout"/> in <c>data/ship_layouts/</c>): when
    /// set, the server stamps that exact design instead of the parametric box. Null = parametric box.</summary>
    public string? Layout { get; set; }
}
