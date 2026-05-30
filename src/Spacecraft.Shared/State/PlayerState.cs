using Spacecraft.Shared.Geometry;

namespace Spacecraft.Shared.State;

/// <summary>
/// Authoritative per-player state owned by the server. The client only renders a view
/// of this; it never decides these values itself.
/// </summary>
public sealed class PlayerState
{
    public string PlayerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public Vector3f Position { get; set; } = Vector3f.Zero;
    public float Yaw { get; set; }
    public float Pitch { get; set; }

    /// <summary>Where the player respawns — the heal-tank in their ship's Medbay.</summary>
    public Vector3f RespawnPoint { get; set; } = Vector3f.Zero;

    public float Health { get; set; } = 100f;
    public float Oxygen { get; set; } = 100f;
    public float SuitEnergy { get; set; } = 100f;

    /// <summary>The player's personal inventory.</summary>
    public Inventory Inventory { get; set; } = new(24);

    /// <summary>Currently selected hotbar slot index.</summary>
    public int SelectedHotbarSlot { get; set; }

    /// <summary>Blueprint keys the player has unlocked (gates crafting/building).</summary>
    public HashSet<string> UnlockedBlueprints { get; set; } = new();

    /// <summary>True when the player is currently aboard their ship (enables cargo crafting).</summary>
    public bool AboardShip { get; set; } = true;
}
