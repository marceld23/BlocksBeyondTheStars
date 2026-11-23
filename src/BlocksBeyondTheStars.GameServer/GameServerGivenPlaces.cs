// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The given placement seat (#3980): a structure placed at a fixed block position handed in by data or a caller — no
/// search. <see cref="GivenSeat"/> carries the footprint's centre column, its quarter turn and (optionally) the site height;
/// the stamp takes the origin corner from the centre and the ground row from the seat or the footprint's median ground
/// (<see cref="GivenOrigin"/>). A <see cref="WorldGeneration.CityPlace"/> carries its centre as a given seat
/// (GameServerCityPlaces.cs).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A footprint on a given seat: its origin corner from the centre, its Y the ground row — the seat's, else the
    /// footprint's median ground height (the shelf's rule: the grade splits the relief evenly).</summary>
    private Vector3i GivenOrigin(GivenSeat seat, int width, int length)
    {
        var (ox, oz) = seat.OriginFor(width, length);
        int groundY = seat.GroundY ?? EvaluateFootprint(_world.Planet, ox, oz, width, length).MedianY;
        return new Vector3i(ox, groundY, oz);
    }
}
