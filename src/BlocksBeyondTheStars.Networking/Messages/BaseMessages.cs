namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Snapshot of one player-founded planet base for the client to show on the planet map (server → client).
/// A base is founded by placing a <c>base_core</c> block, which IS a real voxel block (the chunk mesher draws +
/// collides it); this entity carries the metadata the voxel grid can't hold — the player-typed name and the owner.
/// One base per body per player. Server-authoritative: the server owns the name + position and the client mirrors them.
/// </summary>
public sealed class NetBase
{
    public int Id { get; set; }

    /// <summary>Base-core block centre in world space (the placed cell).</summary>
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>The player-typed, free-form name shown on the planet map (e.g. "Home Base", "Outpost 1").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Owning player's id — only the owner may rename the base (everyone sees the marker).</summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>The celestial body (planet/moon/asteroid) the base sits on.</summary>
    public string BodyId { get; set; } = string.Empty;
}

/// <summary>Full set of bases the client should show for its current world (server → client).</summary>
public sealed class BaseList
{
    public NetBase[] Bases { get; set; } = System.Array.Empty<NetBase>();
}

/// <summary>The owner renames a base they founded — pressing E at the base stone, or via the Map detail "Rename base"
/// button (client → server). The server renames the requesting player's base on <see cref="BodyId"/> (one per body).</summary>
public sealed class SetBaseNameIntent
{
    public string BodyId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>The owner renames a commissioned space station they built — via the Map detail "Rename" button, or
/// pressing E at the station core (client → server). The server validates ownership before applying.</summary>
public sealed class SetStationNameIntent
{
    public string StationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
