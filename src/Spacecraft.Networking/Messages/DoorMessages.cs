namespace Spacecraft.Networking.Messages;

/// <summary>
/// Snapshot of one door for the client to render + collide. Server-authoritative: the server owns
/// the open/closed state (slide doors auto-open near players; hinge doors toggle on interact) and the
/// client only mirrors it. A door fills a doorway opening that stays air in the voxel world — it is a
/// dynamic object with a toggleable collider, not a baked block.
/// </summary>
public sealed class NetDoor
{
    public int Id { get; set; }

    /// <summary>Door kind: "slide" (sci-fi auto doors for stations/cities) or "hinge" (manual village doors).</summary>
    public string Kind { get; set; } = "slide";

    /// <summary>Doorway-gap centre in world space (floor level).</summary>
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>True if the wall runs along the X axis (panels slide / the leaf swings along X); else along Z.</summary>
    public bool AxisX { get; set; }

    /// <summary>Gap width in blocks along the wall axis (the door is this wide; typically 2).</summary>
    public float Width { get; set; } = 2f;

    /// <summary>Whether the door is currently open (panels retracted / leaf swung; collider disabled).</summary>
    public bool Open { get; set; }
}

/// <summary>Full set of doors the client should currently render for its world (server → client).</summary>
public sealed class DoorList
{
    public NetDoor[] Doors { get; set; } = System.Array.Empty<NetDoor>();
}

/// <summary>Player asks to toggle a (hinge) door they're standing at — press E (client → server).</summary>
public sealed class DoorInteractIntent
{
    public int DoorId { get; set; }
}
