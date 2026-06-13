namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// One "data cube" living on a body's surface for the client to render + let the player interact with.
/// A data cube is a glowing download terminal: walking up to it and pressing E grants a small bundled
/// minigame for the player's personal arcade collection. Server-authoritative placement (deterministic
/// from the world seed); the cube is never consumed, so every player on the body can download from it.
///
/// The server stays ignorant of the minigame catalogue: it only owns the cube's position and a stable
/// <see cref="Seed"/>. The client maps <c>Seed → game</c> against its bundled catalogue (consistent across
/// clients of the same build), shows the title, and on download sends back the resolved
/// <see cref="UnlockGameIntent.GameKey"/>. Minigames carry no gameplay effect, so this client-side mapping
/// is safe.
/// </summary>
public sealed class NetDataCube
{
    public int Id { get; set; }

    /// <summary>Cube centre in world space (sits just above the surface).</summary>
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>Stable seed the client hashes against its bundled catalogue to decide which game this cube holds.</summary>
    public long Seed { get; set; }
}

/// <summary>Full set of data cubes the client should render for its current world (server → client).</summary>
public sealed class DataCubeList
{
    public NetDataCube[] Cubes { get; set; } = System.Array.Empty<NetDataCube>();
}

/// <summary>Player "downloads" a data cube they're standing at — press E (client → server). The server
/// validates the cube exists and the player is within reach, then unlocks <see cref="GameKey"/> (the game the
/// client resolved from the cube's seed) into their persisted collection.</summary>
public sealed class UnlockGameIntent
{
    /// <summary>Id of the data cube being downloaded (proximity is validated against it).</summary>
    public int CubeId { get; set; }

    /// <summary>The minigame key the client resolved for this cube; added to the player's collection.</summary>
    public string GameKey { get; set; } = string.Empty;
}

/// <summary>The player's full set of downloaded minigame keys (server → client). Sent on join and after each
/// successful download; the client mirrors it to drive the arcade collection menu.</summary>
public sealed class GameUnlocks
{
    public string[] Unlocked { get; set; } = System.Array.Empty<string>();
}
