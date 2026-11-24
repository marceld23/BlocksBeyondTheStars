// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// A placement seat that is handed in instead of searched for (#3980): the footprint's centre column, its quarter turn and
/// — when the caller knows it — the site height (the structure's ground row). The stamper does no search: the footprint's
/// origin corner is the centre minus half the footprint, the ground under it is graded to the site height with the
/// generator's site flats, the box joins the footprint registry so every rolled structure stamped after it keeps clear, and
/// the placement record pins it like any other seat (seat name <see cref="SeatName"/>). Landmarks take it from a list of
/// given places, a city place carries one for its centre.
/// </summary>
public readonly struct GivenSeat
{
    /// <summary>The seat name the placement record keeps for a structure placed on a given seat.</summary>
    public const string SeatName = "given";

    public GivenSeat(int x, int z, int turns = 0, int? groundY = null)
    {
        X = x;
        Z = z;
        Turns = turns & 3;
        GroundY = groundY;
    }

    /// <summary>The footprint's centre column (world X).</summary>
    public int X { get; }

    /// <summary>The footprint's centre column (world Z).</summary>
    public int Z { get; }

    /// <summary>The quarter turn the structure stands at (0..3).</summary>
    public int Turns { get; }

    /// <summary>The site height (the ground row) when the caller fixes it; null lets the stamper take the footprint's
    /// median ground height.</summary>
    public int? GroundY { get; }

    /// <summary>The footprint's origin corner (world X, Z) for a footprint of <paramref name="width"/> × <paramref name="length"/>
    /// — the same rounding the placement searches use for a centre (centre minus half the extent).</summary>
    public (int X, int Z) OriginFor(int width, int length) => (X - (width / 2), Z - (length / 2));
}
