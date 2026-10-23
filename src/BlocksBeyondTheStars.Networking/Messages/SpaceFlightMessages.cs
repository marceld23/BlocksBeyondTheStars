// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>Client asks to launch into free space flight around its current location.</summary>
public sealed class EnterSpaceIntent
{
}

/// <summary>Client fires the ship's tractor beam as a manual sweep (quick-bar): pull nearby salvage in.</summary>
public sealed class TractorPullIntent
{
    /// <summary>The salvage drop the client auto-locked onto. When set, the server pulls THAT drop in from a
    /// generous range (depth is hard to judge in 3D flight, so the old blind radius sweep often missed drops
    /// that looked close). Empty = legacy radius sweep. Contractless-additive: old payloads default to empty.</summary>
    public string TargetEntityId { get; set; } = string.Empty;
}

/// <summary>Client asks to leave the space instance and return to the surface/base.</summary>
public sealed class LeaveSpaceIntent
{
    /// <summary>The body to land on. Empty (or the current body) = land back where you launched. A
    /// different body in the same system = land there (you flew to it in system-scale flight). Cross-
    /// system landing is not offered — use a hyperspace jump (the star map) instead.</summary>
    public string DestinationBodyId { get; set; } = string.Empty;

    /// <summary>Which fixed landing pad to touch down on (item 38); -1 = auto-pick the first free pad.</summary>
    public int PadIndex { get; set; } = -1;
}

/// <summary>Client fires a built ship weapon at a space entity. The server validates and resolves the hit.
/// Contractless-additive aim fields (#693): the ship's forward direction at the moment of firing, so the
/// server can enforce a firing arc. An all-zero direction (older client) skips the arc check.</summary>
public sealed class FireWeaponIntent
{
    public string WeaponKey { get; set; } = string.Empty;
    public string TargetEntityId { get; set; } = string.Empty;
    public float DirX { get; set; }
    public float DirY { get; set; }
    public float DirZ { get; set; }
}

/// <summary>Client reports its ship's position while flying in space (server validates + checks collisions).</summary>
public sealed class ShipMoveIntent
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>Heading of the ship (or the floating EVA suit) so other players in the instance can see which
    /// way you face.</summary>
    public float Yaw { get; set; }
}

/// <summary>Client → server: the player starts (Active = true) or ends (Active = false) an EVA spacewalk —
/// floating outside the ship in space on foot. The server honours a start only while the player is actually
/// in a space instance; on EVA the suit's life support is off, so oxygen drains until they board again.</summary>
public sealed class SetEvaIntent
{
    public bool Active { get; set; }
}

/// <summary>Authoritative ship hull/shield, sent on join and whenever they change.</summary>
public sealed class ShipCombatStatus
{
    public float Hull { get; set; }
    public float HullMax { get; set; }
    public float Shield { get; set; }
    public float ShieldMax { get; set; }

    /// <summary>Space-radar range in world units (base + radar-module bonus); drives the HUD radar scale.</summary>
    public float RadarRange { get; set; } = 130f;

    /// <summary>The active ship's fitted module keys — lets the flight HUD build its ship-systems quick-bar
    /// (weapon, tractor beam, …) from what the ship actually carries.</summary>
    public string[] Modules { get; set; } = System.Array.Empty<string>();
}

/// <summary>Snapshot of the space instance a player is in (sent on entry and on change).</summary>
public sealed class SpaceState
{
    public string InstanceId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public NetCombatEntity[] Entities { get; set; } = System.Array.Empty<NetCombatEntity>();

    /// <summary>True when the flight view should appear without the take-off sequence — you were already in
    /// space (e.g. taking the helm again from inside the ship), so there is no launch from a surface.</summary>
    public bool SkipLaunch { get; set; }

    /// <summary>True when the player arrived in this flight via a hyperjump into a new star system — the
    /// client plays the warp VFX as the view opens (there is no surface take-off).</summary>
    public bool Hyperjump { get; set; }

    /// <summary>#2118: set when the flight resumes where the ship was left floating (taking the helm again from the
    /// ship interior, stepping out of its airlock, undocking back to an EVA): the ship's position in this instance's
    /// flight coordinates and its heading (degrees) — the view places the ship there instead of the launch point.
    /// Additive; an older client ignores it.</summary>
    public bool HasResumePose { get; set; }
    public float ResumeX { get; set; }
    public float ResumeY { get; set; }
    public float ResumeZ { get; set; }
    public float ResumeYaw { get; set; }

    /// <summary>#1614: this flight is an automatic transit from a parked ship. The client plays its stages
    /// hands-off — the take-off, or after a warp a short hold — and reports each one with
    /// <see cref="TransitLaunchDoneIntent"/>; the server answers with the next stage. Re-sent false when a transit
    /// ends in flight (a refused landing), so the client hands the controls back.</summary>
    public bool AutomaticTransit { get; set; } = false;

    /// <summary>#1614: the body the transit lands on, so the landing descent heads for it. Empty when the transit
    /// ends in flight or no transit runs. Additive; an older client ignores it.</summary>
    public string TransitDestinationBodyId { get; set; } = string.Empty;

    /// <summary>Friendly names of the star system and the body this flight is anchored on (#1565). An in-flight
    /// hyperjump never lands, so no <see cref="WorldReset"/> carries the new identity — the HUD, the F1 form and
    /// the orbit view kept the departure system's name until the next landing.</summary>
    public string SystemName { get; set; } = string.Empty;
    public string BodyName { get; set; } = string.Empty;

    /// <summary>The OTHER players sharing this space instance (excludes the recipient) — their ship or floating
    /// EVA suit, so everyone can see each other out here.</summary>
    public NetSpacePlayer[] Players { get; set; } = System.Array.Empty<NetSpacePlayer>();

    /// <summary>#2737: the players flying in the OTHER flight instances of this star system (they launched from another
    /// body), already converted into this instance's flight frame (the star-map offset of the two anchor bodies ×
    /// <c>SystemBodyLayout.FlightViewScale</c>, like the belt rocks and wrecks). Each carries
    /// <see cref="NetSpacePlayer.AnchorBodyId"/>. A navigation target only: the client offers them in the target lock and
    /// on the radar, never as a ship in the scene (no docking, no collision). Additive; an older client ignores it.</summary>
    public NetSpacePlayer[] SystemPilots { get; set; } = System.Array.Empty<NetSpacePlayer>();

    /// <summary>#2238: the entities of this instance the RECIPIENT has already read with the ship scanner (anomalies,
    /// wrecks, wormholes — the kinds the server remembers per object). The lock-on label shows "✓ scanned" and an
    /// anomaly stays calm across visits. Additive; an older client ignores it.</summary>
    public string[] ScannedIds { get; set; } = System.Array.Empty<string>();

    /// <summary>#2242: true when the player arrived in this flight through a wormhole — the client plays the rift
    /// transit instead of the hyperspace warp. Additive.</summary>
    public bool Wormhole { get; set; }

    /// <summary>#2636: set when the flight opens on an EVA with the suit at a spot of its own — a derelict's boarder who
    /// fell out through its hull floats next to the hole, not beside the ship (which waits at the resume pose). The suit's
    /// eye position in this instance's flight coordinates and its heading (degrees); the view starts the spacewalk there
    /// once the server reports the EVA. Additive; an older client starts the suit beside the ship.</summary>
    public bool HasEvaStart { get; set; }
    public float EvaStartX { get; set; }
    public float EvaStartY { get; set; }
    public float EvaStartZ { get; set; }
    public float EvaStartYaw { get; set; }
}

/// <summary>A remote player's presence in a space instance: their ship (or floating EVA suit) position +
/// heading, for the flight view to render. Server → client, inside <see cref="SpaceState"/>.</summary>
public sealed class NetSpacePlayer
{
    public string PlayerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }

    /// <summary>True = a floating suit on an EVA; false = piloting a ship.</summary>
    public bool Eva { get; set; }

    /// <summary>Ship hull colour (packed 0xRRGGBB) so other players see this pilot's ship in their colour
    /// (item 32). 0 = unset → the client falls back to the default steel tint.</summary>
    public int Hull { get; set; }

    /// <summary>#2431: the ship floats parked while its pilot walks inside it — other clients keep drawing the hull,
    /// engines off. It used to vanish for everyone the moment the pilot left the helm. Additive; an older client
    /// ignores it and draws the ship as flying.</summary>
    public bool Parked { get; set; }

    /// <summary>#2709: on a spacewalk (<see cref="Eva"/>) the X/Y/Z/Yaw above are the suit — this pose is the pilot's
    /// ship floating where they stepped out, so the others keep seeing it (it used to vanish while its pilot was
    /// outside). Only meaningful with <see cref="HasShipPose"/>. Additive; an older client ignores it.</summary>
    public bool HasShipPose { get; set; }

    public float ShipX { get; set; }
    public float ShipY { get; set; }
    public float ShipZ { get; set; }
    public float ShipYaw { get; set; }

    /// <summary>#2709: the pilot's suit colours (packed 0xRRGGBB, as in <see cref="PlayerPresence"/>), so a spacewalking
    /// partner is drawn in their own spacesuit look. 0 = unset (an NPC trader's pose) → the client's default suit.</summary>
    public int Skin { get; set; }
    public int Torso { get; set; }
    public int Arms { get; set; }
    public int Legs { get; set; }

    /// <summary>#2737: on a <see cref="SpaceState.SystemPilots"/> entry, the body whose flight this pilot is in (the body
    /// they launched from) — the lock label reads "Flying near …", since you meet them by landing there and launching
    /// again. Empty on a <see cref="SpaceState.Players"/> entry. Additive; an older client ignores it.</summary>
    public string AnchorBodyId { get; set; } = string.Empty;
}

/// <summary>A space entity was destroyed (asteroid mined or enemy defeated).</summary>
public sealed class SpaceEntityDestroyed
{
    public string Id { get; set; } = string.Empty;
}

/// <summary>The player has left space (manually, or because the ship was disabled — no permanent loss).</summary>
public sealed class SpaceClosed
{
    public string Reason { get; set; } = string.Empty;

    /// <summary>True when the exit was forced by ship defeat (ship recovered to base, player respawned).</summary>
    public bool ShipDisabled { get; set; }
}
