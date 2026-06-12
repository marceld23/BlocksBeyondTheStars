namespace BlocksBeyondTheStars.Shared.State;

/// <summary>
/// Authoritative state of the player's ship: which modules are built and the shared
/// cargo hold. Effective ship stats are derived from the built modules' definitions.
/// </summary>
public sealed class ShipState
{
    /// <summary>Keys of ship modules currently built (see <c>data/ship_modules.json</c>).</summary>
    public List<string> Modules { get; set; } = new();

    /// <summary>The shared cargo hold; its slot count grows as cargo modules are built.</summary>
    public Inventory Cargo { get; set; } = new(48);

    /// <summary>Identifier of the system/planet/station the ship is currently at.</summary>
    public string CurrentLocationId { get; set; } = string.Empty;

    /// <summary>Ship type/design key (see <c>data/ships.json</c>); drives the hull design + base stats.</summary>
    public string ShipType { get; set; } = "starter";

    /// <summary>
    /// Current hull integrity (space combat, `anf_space_flight.md` §8.4). Reaching 0 disables
    /// the ship and recovers it to its base — there is no permanent ship loss (§8.5). The
    /// maximum is derived from built modules; the server clamps and restores this value.
    /// </summary>
    public float Hull { get; set; } = 100f;

    /// <summary>Current shield charge; regenerates out of combat up to the module-derived maximum.</summary>
    public float Shield { get; set; }

    public bool HasModule(string moduleKey) => Modules.Contains(moduleKey);
}
