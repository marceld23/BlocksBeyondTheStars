using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Missions;

namespace Spacecraft.Shared.State;

/// <summary>Permission level (technical requirements / `anf_admin_einstellungen.md` §10–11).</summary>
public enum PlayerRole
{
    Player,
    Moderator,
    Admin,
    WorldAdmin,
}

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

    /// <summary>Permission level; the world creator becomes <see cref="PlayerRole.WorldAdmin"/>.</summary>
    public PlayerRole Role { get; set; } = PlayerRole.Player;

    // Session cheat toggles (admin only, server-authoritative; not persisted).
    public bool GodMode { get; set; }
    public bool Fly { get; set; }
    public bool InstantBuild { get; set; }

    public bool IsAdmin => Role is PlayerRole.Admin or PlayerRole.WorldAdmin;

    /// <summary>Accepted missions and their progress.</summary>
    public List<MissionProgress> Missions { get; set; } = new();
}
