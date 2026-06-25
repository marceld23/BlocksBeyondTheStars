// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Server → client: a localized hyperspace flash at a world position in a space instance, shown to everyone
/// in the instance when a ship warps in or out. Peaceful NPC traders use it so other players visibly see
/// arrivals and departures (the existing <c>HyperspaceWarp</c> overlay is first-person only).
/// </summary>
public sealed class SpaceWarpFx
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>True = warp-in (arrival flash); false = warp-out (departure flash).</summary>
    public bool Arriving { get; set; }
}
