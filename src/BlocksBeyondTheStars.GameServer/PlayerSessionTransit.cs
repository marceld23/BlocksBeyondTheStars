// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.GameServer;

/// <summary>Per-connection automatic-transit bookkeeping of a <see cref="PlayerSession"/>
/// (RAM only, never persisted).</summary>
public sealed class PlayerSessionTransit
{
    /// <summary>#1614: the body an automatic landed-ship transit lands on at its end, or null when no transit is
    /// pending or the transit ends in free flight ("Hyperjump to this system" from a parked ship).</summary>
    public string? PendingTransitBodyId { get; set; }

    /// <summary>#1614: the star system the transit still has to warp into before it lands (or, without a
    /// <see cref="PendingTransitBodyId"/>, before it hands over the controls), or null once the warp is done or
    /// the destination lies in the system the ship took off in.</summary>
    public string? PendingTransitSystemId { get; set; }

    /// <summary>#1614: the pilot is in an automatic transit — the client plays the take-off, the warp and the
    /// landing on its own and reports each stage with <c>TransitLaunchDoneIntent</c>.</summary>
    public bool AutomaticTransit { get; set; }

    /// <summary>Pad index reserved for an automatic landed-ship transit, or -1 when none is pending.</summary>
    public int PendingTransitPadIndex { get; set; } = -1;

    // Automatic landed-ship transit (#1614): seconds since the current stage began — the server moves on by
    // itself after ServerConfig.TransitLaunchTimeoutSeconds if the client never reports the stage as played.
    public double TransitLaunchTimer { get; set; }
}
