// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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

    /// <summary>Cells the core's life-support field currently supplies beyond the unconditional cube — the
    /// sealed rooms (#794) — and how many such rooms there are. Shown when aiming at your own core (#1267).
    /// New fields on an existing contractless MessagePack message: an older client ignores them.</summary>
    public int AirCells { get; set; }
    public int SealedRooms { get; set; }

    /// <summary>Owning player's id — only the owner may rename the base (everyone sees the marker).</summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>The celestial body (planet/moon/asteroid) the base sits on.</summary>
    public string BodyId { get; set; } = string.Empty;

    /// <summary>#2679: how many residents live at the base (the founding settler included) and how many it can hold —
    /// "Residents: 2/5" in the readout when aiming at your own core. <see cref="ResidentCap"/> 0 = a server that does not
    /// send them (the client then shows nothing). Additive, contractless: an older client ignores them.</summary>
    public int Residents { get; set; }
    public int ResidentCap { get; set; }
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

/// <summary>Client → server (#2717): the owner pressed E at their base core (Grundstein) — who lives here? The server answers
/// with a <see cref="BaseResidentList"/>. No protocol bump: an older server drops the unknown tag (the screen then shows
/// the rename field alone).</summary>
public sealed class RequestBaseResidentsIntent
{
    /// <summary>The body the base stands on — the player's base there (one per body).</summary>
    public string BodyId { get; set; } = string.Empty;
}

/// <summary>One resident in a <see cref="BaseResidentList"/> (#2717): everything is a locale key or a plain value, so the
/// client words it in its own language.</summary>
public sealed class NetBaseResident
{
    /// <summary>The resident's coined name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>What they do at the base, as the nameplate's role key (<c>npc.role.craftsman</c>, <c>npc.theme.settlers</c>,
    /// a profession's name key …).</summary>
    public string RoleKey { get; set; } = string.Empty;

    /// <summary>What they are doing right now, as a locale key (<c>npc.activity.sleeping</c>, <c>npc.activity.working</c>,
    /// <c>ui.base.res_strolling</c> …); empty when they are not here.</summary>
    public string ActivityKey { get; set; } = string.Empty;

    /// <summary>Whether they are on the base's world right now — false: "not here right now" (the world reloads them on
    /// the next scan, or they have not moved in on this world yet).</summary>
    public bool Here { get; set; }

    /// <summary>The compass direction from the core as a locale key (<c>dir.ne</c>); empty = right by the core.</summary>
    public string DirectionKey { get; set; } = string.Empty;

    /// <summary>How far from the core they are, in whole metres (horizontal).</summary>
    public int Distance { get; set; }

    /// <summary>Whether they have a bed of their own (else they sleep rough until one more bed stands).</summary>
    public bool HasBed { get; set; }

    /// <summary>The founding settler — the one who never moves out.</summary>
    public bool Founder { get; set; }
}

/// <summary>Server → client (#2717): who lives at the player's base, founding settler first. <see cref="Cap"/> 0 = there is
/// no base of theirs on that body. No protocol bump: an older client drops the unknown tag.</summary>
public sealed class BaseResidentList
{
    public string BodyId { get; set; } = string.Empty;

    /// <summary>The base's name.</summary>
    public string BaseName { get; set; } = string.Empty;

    /// <summary>How many beds count inside the base, and the most residents it can hold (1 + one per bed, up to the cap).</summary>
    public int Beds { get; set; }
    public int Cap { get; set; }

    public NetBaseResident[] People { get; set; } = System.Array.Empty<NetBaseResident>();
}
