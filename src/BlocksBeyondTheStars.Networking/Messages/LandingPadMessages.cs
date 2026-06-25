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
    public bool Occupied { get; set; }

    /// <summary>Name of the player currently on this pad (empty if free) — shown in the chooser.</summary>
    public string Occupant { get; set; } = string.Empty;
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
}
