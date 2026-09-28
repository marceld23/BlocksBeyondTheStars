// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The client's quick "is somebody already sitting there?" look before it sits down (#2122): a seated NPC or a
    /// seated remote player whose reported feet fall inside the aimed seat cell. The server decides for real (and
    /// answers a taken seat with a rejection that stands the player back up); this only keeps the camera from
    /// dropping into a body for the round trip. Seam-aware on both wrap axes, like every on-planet cell compare.
    /// </summary>
    public static class SeatCells
    {
        /// <summary>How far below a cell's floor reported feet may read and still be in it (float round-trips).</summary>
        public const float FeetSlack = 0.05f;

        /// <summary>Whether the world position (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) lies in
        /// the cell (<paramref name="cellX"/>, <paramref name="cellY"/>, <paramref name="cellZ"/>) — the short way round
        /// the longitude and latitude seams of a world of <paramref name="circumference"/>. A sitter's feet rest on the
        /// cell's floor, so a hair of float error below it still counts (<see cref="FeetSlack"/>).</summary>
        public static bool Covers(float x, float y, float z, int cellX, int cellY, int cellZ, int circumference)
        {
            if ((int)Math.Floor(y + FeetSlack) != cellY)
            {
                return false;
            }

            return WorldConstants.WrapDeltaX((int)Math.Floor(x) - cellX, circumference) == 0
                && WorldConstants.WrapDeltaZ((int)Math.Floor(z) - cellZ, circumference) == 0;
        }
    }
}
