// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>Client asks for a body's fixed landing pads + their live occupancy, to show the pad chooser before
/// touching down (item 38). The server replies with a <see cref="LandingPadList"/> for that body.</summary>
public sealed class RequestLandingPadsIntent
{
    public string BodyId { get; set; } = string.Empty;
}

/// <summary>One fixed landing pad of a body: where it is and whether it's currently taken (and by whom).</summary>
public sealed class NetLandingPad
{
    public int Index { get; set; }
    public int X { get; set; }
    public int Z { get; set; }

    /// <summary>True if the pad is blocked FOR THE RECEIVING PLAYER — someone else (or a landed trader)
    /// holds it. A player's own reservation is never "occupied" to themselves (#977): a pad held while
    /// its owner is up in space is exactly the pad they want to come back down on.</summary>
    public bool Occupied { get; set; }

    /// <summary>Name of the player currently on this pad (empty if free) — shown in the chooser. Set for
    /// the receiver's own pad too, which carries <see cref="Mine"/> instead of <see cref="Occupied"/>.</summary>
    public string Occupant { get; set; } = string.Empty;

    /// <summary>The pad lies on the sea floor (#1454): the ship parks in a dry shaft under the water. The
    /// chooser labels such pads so a first-time player is not surprised by walls of water.</summary>
    public bool Wet { get; set; }

    /// <summary>True if the receiving player is the one holding this pad (#977) — their parked or reserved
    /// ship's pad. Drawn as theirs and stays selectable in the chooser. Appended field: contractless
    /// MessagePack, so an older peer simply reads it as false (no protocol bump).</summary>
    public bool Mine { get; set; }

    /// <summary>Blocks of water standing over a <see cref="Wet"/> pad's ground (#1622), 0 when dry — the
    /// chooser prints it so a wade and a shaft read differently. Appended contractless field: an older peer
    /// reads 0.</summary>
    public int Depth { get; set; }

    /// <summary>The pad lies in lava (a save from before terrain generation 8, whose pads never move): the chooser
    /// shows it red and the server only parks a ship there when no other pad is free. Appended contractless field:
    /// an older peer reads false.</summary>
    public bool Lava { get; set; }

    /// <summary>The weather at the pad right now (#2173) — the state key the surface would report there, computed by
    /// the server with the shared per-position formula on the body's live (ambient) weather. Empty from an older
    /// server (appended contractless field).</summary>
    public string Weather { get; set; } = string.Empty;

    /// <summary>What falls at the pad ("none", "rain", "snow", …) — temperature-resolved like the surface.</summary>
    public string Precipitation { get; set; } = string.Empty;

    // --- #3069: a player-built pad next to a base. Appended contractless fields: an older peer reads a communal pad. ---

    /// <summary>A player-built landing pad in a base (#3069) rather than one of the body's communal pads. It is addressed by
    /// its cell (<see cref="CellX"/>…), never by <see cref="Index"/> (which is -1).</summary>
    public bool Base { get; set; }

    /// <summary>The pad block's cell (a base pad's identity on its body); the ship's floor stands one block above it.</summary>
    public int CellX { get; set; }
    public int CellY { get; set; }
    public int CellZ { get; set; }

    /// <summary>The name the owner gave the pad (may be empty).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The base the pad belongs to: its core cell (the chooser groups a base's pads under one marker there) and name.</summary>
    public int BaseX { get; set; }
    public int BaseY { get; set; }
    public int BaseZ { get; set; }
    public string BaseName { get; set; } = string.Empty;

    /// <summary>The owner's player name (the chooser and the pad dialog show whose pad it is).</summary>
    public string OwnerName { get; set; } = string.Empty;

    /// <summary>Whose pad this is for the receiver — <see cref="NetBase.RelationOwn"/>, <see cref="NetBase.RelationAlly"/> or
    /// <see cref="NetBase.RelationOther"/>, as for bases.</summary>
    public byte Relation { get; set; }

    /// <summary>Who may land here (<c>BasePadAccess</c>: 0 only the owner, 1 the owner and allies, 2 everyone).</summary>
    public int Access { get; set; }

    /// <summary>The receiver may not land here (the access shuts them out) — drawn locked, never offered.</summary>
    public bool Locked { get; set; }

    /// <summary>#3146: somebody built in the pad's landing square, so no ship lands here until it is cleared — the chooser
    /// says "built over". Comes with <see cref="Occupied"/> set (an older client reads a taken pad). Never set on the
    /// receiver's own pad (<see cref="Mine"/>). Appended contractless field: an older peer reads false.</summary>
    public bool BuiltOver { get; set; }
}

/// <summary>A body's fixed landing pads + occupancy (server → client): drives the land chooser in the flight
/// view and the pad markers on the world map. Sent on request (any body) and on world entry (the active body).</summary>
public sealed class LandingPadList
{
    public string BodyId { get; set; } = string.Empty;
    public NetLandingPad[] Pads { get; set; } = System.Array.Empty<NetLandingPad>();

    /// <summary>The body's day fraction (0..1) the player will arrive at when landing — its current time if the
    /// body is already active, else the activation default. The chooser map draws the day/night terminator from
    /// this (local time at a pad = TimeOfDay + padX/circumference), so a pad shown on the dark band means a
    /// night-side touchdown. Matches what the surface actually shows on arrival.</summary>
    public float TimeOfDay { get; set; } = 0.35f;
}

/// <summary>Tells the players already on a body that another player's ship is arriving or departing at a pad, so
/// they see a landing/launch animation (item 38). Server → the other players on that body (not the mover).</summary>
public sealed class ShipTransitFx
{
    /// <summary>The mover's player id — keys the cached remote ship design so the animation shows
    /// their REAL voxel ship (a generic silhouette is the fallback).</summary>
    public string PlayerId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; } // ground level at the pad — the ship descends to / launches from here
    public float Z { get; set; }

    /// <summary>True = landing (descends onto the pad); false = launching (rises off the pad).</summary>
    public bool Landing { get; set; }

    /// <summary>The mover's ship hull colour (packed 0xRRGGBB) so the animated ship matches their ship (item 32).</summary>
    public int Hull { get; set; }

    /// <summary>#3177: above 0 the ship is still high up, coming in to land on this pad in about this many seconds (a trader's
    /// approach, sent when it decides to land) — the client draws a glint and a contrail in the sky towards the pad; the real
    /// descent follows with a plain landing FX at touchdown. 0 = the touchdown or the lift-off itself. Additive: an older
    /// message reads 0.</summary>
    public float ApproachSeconds { get; set; }
}

/// <summary>#3069: the owner names a base landing pad and sets who may land on it. Answered with the refreshed pad list.</summary>
public sealed class SetBasePadIntent
{
    /// <summary>The pad block's cell on the player's current body.</summary>
    public int CellX { get; set; }
    public int CellY { get; set; }
    public int CellZ { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary><c>BasePadAccess</c>: 0 only the owner, 1 the owner and allies, 2 everyone.</summary>
    public int Access { get; set; }
}
