// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Server → client (#3904): where this player stands — on our side or on the other side. The other side is the same
/// world in a player state (grey for whoever is over there, see <c>docs/developer/OTHER_SIDE.md</c>); the server owns
/// the state and sends this message to the player it is about, and to nobody else: at the join (always, so a client
/// that reconnects starts from the truth) and on every change — a crossing, a return, a forced return, and whenever the
/// sound-block count changes (the sound layer, #3908).
/// <para>The client draws from it: the grey look and the HUD mark while <see cref="Over"/>, the transition when
/// <see cref="Over"/> flips (<see cref="Reason"/> says why), the marker of the entry gate on compass and map while
/// <see cref="HasGate"/>, and the sound-block meter. No protocol bump — an older client drops the message, and it can
/// never cross anyway (capability word <c>otherside1</c>).</para>
/// </summary>
public sealed class OtherSideState
{
    /// <summary>True while the player is on the other side.</summary>
    public bool Over { get; set; }

    /// <summary>The entry gate's centre (the middle of its rune stones), in the coordinates of the world the player is
    /// on. Only meaningful while <see cref="HasGate"/>.</summary>
    public float GateX { get; set; }

    /// <summary>See <see cref="GateX"/>.</summary>
    public float GateY { get; set; }

    /// <summary>See <see cref="GateX"/>.</summary>
    public float GateZ { get; set; }

    /// <summary>True while an entry gate is remembered — exactly while the player is over there. The gate a player came
    /// through stays marked on compass and map until they are back.</summary>
    public bool HasGate { get; set; }

    /// <summary>Why this state was sent — one of <see cref="OtherSideReasons"/>. A client that does not know a reason
    /// treats it as <see cref="OtherSideReasons.Sync"/> (no transition, just the state).</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>How many sound blocks of this player stand in the current world (#3908, filled by the sound layer).</summary>
    public int SoundBlocksUsed { get; set; }

    /// <summary>How many sound blocks one player may have standing in one world (<c>OtherSideRules.SoundBlockCap</c>;
    /// 0 from a server without the sound layer).</summary>
    public int SoundBlockCap { get; set; }
}

/// <summary>
/// Server → client (#3906): the rune gates the server knows around a player who is on the other side, so the client can
/// stand a tall light over each of them — over there the doors home must be recognisable from far away. Sent only to a
/// player who is over there: at the crossing, then whenever the set changed or the player moved on (at most once every
/// few seconds), and once more — empty — at the return. The set is what the server knows without searching the world:
/// the entry gate, the monuments that carry a rune group, the complete rune shrines, the melody sites (the Singing
/// Shrine, the Choir Tower, a ring) and the rune groups found in the loaded chunks around the player. No protocol bump —
/// an older client drops the message, and it can never cross anyway.
/// </summary>
public sealed class OtherSideGates
{
    /// <summary>The gates, nearest first. Empty when the player is back on our side.</summary>
    public NetOtherSideGate[] Gates { get; set; } = System.Array.Empty<NetOtherSideGate>();
}

/// <summary>One rune gate of <see cref="OtherSideGates"/>.</summary>
public sealed class NetOtherSideGate
{
    /// <summary>The gate's centre (the middle of its rune stones), in the coordinates of the world the player is on.</summary>
    public float X { get; set; }

    /// <summary>See <see cref="X"/>.</summary>
    public float Y { get; set; }

    /// <summary>See <see cref="X"/>.</summary>
    public float Z { get; set; }

    /// <summary>A crack runs through this gate (the Sounders' story, #3909) — the client draws it between the stones
    /// until the flag clears.</summary>
    public bool Cracked { get; set; }

    /// <summary>The gate this player came through (the one <see cref="OtherSideState"/> marks on compass and map).</summary>
    public bool Entry { get; set; }
}

/// <summary>The values of <see cref="OtherSideState.Reason"/>.</summary>
public static class OtherSideReasons
{
    /// <summary>The state as it is — the join, or the sound-block count changed. No transition.</summary>
    public const string Sync = "sync";

    /// <summary>The player crossed at a rune gate (the crossing disc in the chest's socket).</summary>
    public const string Crossed = "crossed";

    /// <summary>The player came back at a rune gate.</summary>
    public const string Returned = "returned";

    /// <summary>The server brought the player back to the entry gate (a story beat, a rule).</summary>
    public const string Forced = "forced";

    /// <summary>The player was moved by something else (an admin teleport, a world change, a respawn, a seat) and is
    /// back on our side where that put them — no snap to the gate.</summary>
    public const string Moved = "moved";

    /// <summary>The player left the game over there and now stands at the entry gate on our side.</summary>
    public const string Rejoined = "rejoined";

    /// <summary>The admin verb <c>/otherside</c> flipped the state.</summary>
    public const string Admin = "admin";
}
