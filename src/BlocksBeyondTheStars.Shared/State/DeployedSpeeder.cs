// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.State;

/// <summary>
/// A hover speeder the player has deployed into the world (design: speeder-vehicle feature). Like a tamed
/// companion, it is persisted in the owner's player blob and materialised as a live entity only while the
/// owner is present on its home body — so it needs no per-world DB table. The item that places it
/// (<c>speeder</c>) is consumed on deploy and returned when the speeder is packed back up; if it is destroyed
/// the item is lost. One record per deployed item instance. Position/yaw/hull/fuel are written back here when
/// the owner parks, drives, takes damage or refuels, so a reload restores the speeder exactly where it was.
/// </summary>
public sealed class DeployedSpeeder
{
    /// <summary>Stable id (server entity id), used by the client to reference this speeder and by the owner to
    /// board / pack / refuel it.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The celestial-body id the speeder is parked on. It only materialises when its owner is on this
    /// body (mirrors <see cref="TamedCreature.HomeBodyId"/>).</summary>
    public string HomeBodyId { get; set; } = string.Empty;

    /// <summary>Parked world position (updated to the driver's position while driving, frozen on exit).</summary>
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>Heading in degrees (matches the player yaw convention).</summary>
    public float Yaw { get; set; }

    /// <summary>Structural integrity. At 0 the speeder is destroyed (and this record removed).</summary>
    public float Hull { get; set; } = 100f;
    public float HullMax { get; set; } = 100f;

    /// <summary>Onboard energy cell charge. Driving drains it; an empty cell means no propulsion until refuelled.</summary>
    public float Fuel { get; set; } = 100f;
    public float FuelMax { get; set; } = 100f;

    /// <summary>Hull paint as 0xRRGGBB (0 = default), so a player's speeder can match their ship/base colour.</summary>
    public int HullColor { get; set; }
}
