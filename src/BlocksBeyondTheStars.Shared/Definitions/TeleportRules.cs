// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The teleport devices' numbers, shared by the server's gates and the client's panels so a cost label can never drift
/// from the cost the server charges (#2994).
/// <list type="bullet">
/// <item>The <b>suit teleporter</b> (#1056) jumps back to the ship, to an ally on the same body and, since #2994, to the
/// fixed places of this body: own and allied bases, beam pads and radio beacons, and map markers.</item>
/// <item>The <b>Quantum Teleporter</b> (#2995) does all of that and also jumps to typed coordinates or the map waypoint —
/// dearer and slower to recharge, so the pad network and the beacons keep their worth.</item>
/// <item>The <b>beam pads</b> stay the cheap way between two fixed points.</item>
/// </list>
/// </summary>
public static class TeleportRules
{
    /// <summary>The suit teleporter's item key.</summary>
    public const string SuitTeleporterItem = "suit_teleporter";

    /// <summary>The Quantum Teleporter's item key (#2995).</summary>
    public const string QuantumTeleporterItem = "quantum_teleporter";

    /// <summary>Suit energy one jump of the suit teleporter costs (also the Quantum Teleporter's fixed-place jumps).</summary>
    public const float SuitEnergyCost = 10f;

    /// <summary>Seconds after a suit-teleporter jump before the next one (shared by every teleporter jump).</summary>
    public const double SuitCooldownSeconds = 30.0;

    /// <summary>Suit energy a coordinate or waypoint jump of the Quantum Teleporter costs.</summary>
    public const float QuantumEnergyCost = 20f;

    /// <summary>Seconds after a coordinate or waypoint jump before the next teleporter jump.</summary>
    public const double QuantumCooldownSeconds = 60.0;

    /// <summary>Suit energy one beam from pad to pad costs.</summary>
    public const float BeamEnergyCost = 6f;

    /// <summary>Seconds after a beam before the next one (the pads' own cooldown, apart from the teleporter's).</summary>
    public const double BeamCooldownSeconds = 6.0;

    /// <summary>The destination kinds of <c>TeleportToPointIntent</c>.</summary>
    public const string KindBase = "base";
    public const string KindBeam = "beam";
    public const string KindBeacon = "beacon";
    public const string KindMarker = "marker";
    public const string KindCoords = "coords";
    public const string KindWaypoint = "waypoint";

    /// <summary>True for the kinds only the Quantum Teleporter reaches: a free point, typed or clicked on the map.</summary>
    public static bool IsQuantumKind(string? kind) => kind == KindCoords || kind == KindWaypoint;

    /// <summary>True for an item that opens the teleporter's destination picker.</summary>
    public static bool IsTeleporter(string? itemKey) => itemKey == SuitTeleporterItem || itemKey == QuantumTeleporterItem;

    /// <summary>The energy a jump of <paramref name="kind"/> costs.</summary>
    public static float EnergyCostOf(string? kind) => IsQuantumKind(kind) ? QuantumEnergyCost : SuitEnergyCost;

    /// <summary>The cooldown a jump of <paramref name="kind"/> leaves.</summary>
    public static double CooldownOf(string? kind) => IsQuantumKind(kind) ? QuantumCooldownSeconds : SuitCooldownSeconds;
}
