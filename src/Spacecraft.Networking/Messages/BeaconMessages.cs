namespace Spacecraft.Networking.Messages;

/// <summary>
/// Snapshot of one placed radio beacon for the client to show on the world map + compass.
/// A beacon IS a real voxel block (the chunk mesher draws + collides it); this entity carries the
/// metadata the voxel grid can't hold — the player-typed label and the owner. Server-authoritative:
/// the server owns the label + position and the client only mirrors them.
/// </summary>
public sealed class NetBeacon
{
    public int Id { get; set; }

    /// <summary>Beacon block centre in world space (the placed cell).</summary>
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>The player-typed, free-form label shown on the map/compass (e.g. "Iron Lake", "Base 1").</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Owning player's id — only the owner may rename the beacon (everyone sees the marker).</summary>
    public string OwnerId { get; set; } = string.Empty;
}

/// <summary>Full set of beacons the client should show for its world (server → client).</summary>
public sealed class BeaconList
{
    public NetBeacon[] Beacons { get; set; } = System.Array.Empty<NetBeacon>();
}

/// <summary>The owner asks to rename a beacon they placed — press E at it, type a new label (client → server).</summary>
public sealed class SetBeaconLabelIntent
{
    public int BeaconId { get; set; }
    public string Label { get; set; } = string.Empty;
}
