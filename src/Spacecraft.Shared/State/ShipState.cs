namespace Spacecraft.Shared.State;

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

    public bool HasModule(string moduleKey) => Modules.Contains(moduleKey);
}
