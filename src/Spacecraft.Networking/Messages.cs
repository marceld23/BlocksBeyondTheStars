namespace Spacecraft.Networking.Messages;

// Wire messages. Plain classes with parameterless constructors and get/set properties so
// they serialize cleanly with MessagePack's contractless resolver (no attributes needed).
//
// Client -> Server messages are *intents*: the client asks; the server decides.
// Server -> Client messages are authoritative *state*.

// ---------------- Client -> Server (intents) ----------------

public sealed class JoinRequest
{
    public int ProtocolVersion { get; set; } = Spacecraft.Networking.Protocol.Version;
    public string PlayerName { get; set; } = string.Empty;
    public string? Password { get; set; }
}

public sealed class MoveIntent
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }
}

public sealed class MineBlockIntent
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

public sealed class PlaceBlockIntent
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public string ItemKey { get; set; } = string.Empty;
}

public sealed class CraftIntent
{
    public string RecipeKey { get; set; } = string.Empty;
    public int Count { get; set; } = 1;
}

public sealed class UnlockBlueprintIntent
{
    public string BlueprintKey { get; set; } = string.Empty;
}

public sealed class SelectHotbarIntent
{
    public int Slot { get; set; }
}

// ---------------- Server -> Client (state) ----------------

public sealed class JoinAccepted
{
    public int ProtocolVersion { get; set; } = Spacecraft.Networking.Protocol.Version;
    public string PlayerId { get; set; } = string.Empty;
    public long WorldSeed { get; set; }
    public string PlanetType { get; set; } = string.Empty;
}

public sealed class JoinRejected
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class ChunkDataMessage
{
    public int Cx { get; set; }
    public int Cy { get; set; }
    public int Cz { get; set; }
    public ushort[] Blocks { get; set; } = System.Array.Empty<ushort>();
}

public sealed class BlockChanged
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public ushort Block { get; set; }
}

/// <summary>A single item stack in an inventory update.</summary>
public sealed class NetItemStack
{
    public int Slot { get; set; }
    public string Item { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class InventoryUpdate
{
    public NetItemStack[] Personal { get; set; } = System.Array.Empty<NetItemStack>();
    public NetItemStack[] Cargo { get; set; } = System.Array.Empty<NetItemStack>();
}

public sealed class PlayerStateUpdate
{
    public string PlayerId { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public float Health { get; set; }
    public float Oxygen { get; set; }
    public float SuitEnergy { get; set; }
}

public sealed class CraftResult
{
    public bool Success { get; set; }
    public string RecipeKey { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public sealed class ActionRejected
{
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class ServerMessage
{
    public string Text { get; set; } = string.Empty;
}
