namespace Spacecraft.Networking.Messages;

/// <summary>
/// Snapshot of one settlement NPC (a humanoid inhabitant) for the client's avatar renderer.
/// Server-authoritative — the client only renders what the server reports here.
/// </summary>
public sealed class NetNpc
{
    public int Id { get; set; }

    /// <summary>Role at the settlement: "vendor", "quartermaster" or "settler".</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Inhabitant theme (settlers/miners/traders/researchers) — flavour for the avatar.</summary>
    public string Theme { get; set; } = string.Empty;

    /// <summary>Localization key for the NPC's display name / role label.</summary>
    public string NameKey { get; set; } = string.Empty;

    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>Facing yaw in radians — the avatar turns toward a nearby player, else its stroll heading.</summary>
    public float Facing { get; set; }

    /// <summary>Avatar build hints: humanoid scale, skin/outfit tint, and organic-vs-android body.</summary>
    public float Size { get; set; }
    public uint SkinRgb { get; set; }
    public uint OutfitRgb { get; set; }
    public bool IsRobot { get; set; }
}

/// <summary>Full set of NPCs the client should currently render (server → client).</summary>
public sealed class NpcList
{
    public NetNpc[] Npcs { get; set; } = System.Array.Empty<NetNpc>();
}
