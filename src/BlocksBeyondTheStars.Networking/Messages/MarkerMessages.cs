// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Named map markers + ping (#1217). A marker is a labelled, icon/colour-coded spot a player saves on a world
/// (8 per player per world, persisted on their player blob); a SHARED marker is also shown to players the
/// owner is allied or crewed with while they are on the same body. A PING is the transient cousin: a
/// "look here" pulse at the crosshair with a 30-second lifetime, rate-limited, never persisted. The planet
/// map draws both as icon pins with labels; the compass shows palette-coloured blips.
/// </summary>
public sealed class NetMarker
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Owning player id (== display name); the client compares it to its own id for "mine".</summary>
    public string OwnerId { get; set; } = string.Empty;

    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>Label ≤ 24 chars (screened server-side); empty renders a localized default.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Icon index 0..7: flag, home, ore, danger, water, star, heart, question.</summary>
    public int Icon { get; set; }

    /// <summary>Colour index into the shared marker palette (0..5).</summary>
    public int Color { get; set; }

    /// <summary>True when visible to the owner's allies + crew on this body (false = private).</summary>
    public bool Shared { get; set; }

    /// <summary>True for a transient ping (expires server-side ~30 s after it was raised).</summary>
    public bool Ping { get; set; }
}

/// <summary>The full set of markers this player should see on their CURRENT world (server → client): their
/// own, the shared markers of allies + crew mates, and any live pings. Sent on join, world switch and on any
/// change. Replaces the previous set wholesale.</summary>
public sealed class MarkerList
{
    public NetMarker[] Markers { get; set; } = System.Array.Empty<NetMarker>();
}

/// <summary>
/// Every marker verb in one envelope (client → server), one NetCodec tag. <see cref="Kind"/> picks the verb:
/// <c>set</c> (create with empty <see cref="Id"/>, or update an own marker by id — position, label, icon,
/// colour, shared), <c>remove</c> (own marker by id), <c>ping</c> (X/Y/Z only; TTL + rate limit are the
/// server's), <c>list</c> (re-request — the one verb allowed while paused).
/// </summary>
public sealed class MarkerActionIntent
{
    public string Kind { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public string Label { get; set; } = string.Empty;

    public int Icon { get; set; }

    public int Color { get; set; }

    public bool Shared { get; set; }
}
