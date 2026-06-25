// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Snapshot of one placed beam block (teleporter pad) for the client to show on the world map + as a named
/// landmark. Like a radio beacon, a beam block IS a real voxel block (the chunk mesher draws + collides it);
/// this entity carries the metadata the voxel grid can't hold — the player-typed name and the owner. Pressing
/// E on a beam block opens the transporter, which beams the player to one of their own or an allied player's
/// beam blocks on the same world. Server-authoritative: the server owns the name + position + teleport.
/// </summary>
public sealed class NetBeam
{
    public int Id { get; set; }

    /// <summary>Beam block cell centre in world space (the placed cell).</summary>
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>The player-typed, free-form name shown on the map + transporter list (e.g. "Mine", "Home").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Owning player's id — destinations are filtered to the local player's own + allied beam blocks.</summary>
    public string OwnerId { get; set; } = string.Empty;
}

/// <summary>Full set of beam blocks the client should show for its world (server → client).</summary>
public sealed class BeamList
{
    public NetBeam[] Beams { get; set; } = System.Array.Empty<NetBeam>();
}

/// <summary>The owner asks to rename a beam block they placed — press E at it, type a new name (client → server).</summary>
public sealed class SetBeamNameIntent
{
    public int BeamId { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>The player asks to beam from the pad they're standing at to a chosen destination pad (client → server).
/// The server validates reach to the source, that both pads are owned/allied, suit energy and cooldown.</summary>
public sealed class BeamTeleportIntent
{
    public int SourceId { get; set; }
    public int TargetId { get; set; }
}

/// <summary>Sent to the teleporting player after a successful beam (server → client): the authoritative arrival
/// position so the client snaps the body there (mirrors the respawn snap) and plays the arrival effect/sound.</summary>
public sealed class BeamTeleported
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

/// <summary>Broadcast to everyone on the world for a beam jump (server → client): spawn the beam column VFX +
/// a positional whoosh at both the departure and arrival pads, so other players see the transit too.</summary>
public sealed class BeamFx
{
    public float FromX { get; set; }
    public float FromY { get; set; }
    public float FromZ { get; set; }
    public float ToX { get; set; }
    public float ToY { get; set; }
    public float ToZ { get; set; }
}
