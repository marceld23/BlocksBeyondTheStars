// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Doors seen from space (#2607, Screelit: "on the ground a door is a door — in space a door becomes a block!").
    /// The rules the flight view and the spacewalk share, Unity-free so they are tested headless:
    /// <list type="bullet">
    /// <item>which doors of a design are drawn and aimed at (the doors built into a ship or a player station — their cells
    /// are air, the door fills its doorway three cells high);</item>
    /// <item>that a closed ENERGY door shows its blue field out here, on a ship and on a station alike (decision 5) — on
    /// the ground the field still only shows while the door is open; the other kinds keep their panels;</item>
    /// <item>where the spacewalk shows a door preview: on the own ship, and on a station whose owner is the player or an
    /// ally — exactly where the server lets the suit build.</item>
    /// </list>
    /// </summary>
    public static class SpaceDoors
    {
        /// <summary>One door built into a design: its doorway base cell, kind and wall axis.</summary>
        public readonly struct Door
        {
            public Door(Vector3i cell, string kind, bool axisX)
            {
                Cell = cell;
                Kind = kind;
                AxisX = axisX;
            }

            public Vector3i Cell { get; }

            public string Kind { get; }

            /// <summary>True when the door's wall runs along X (the passage is along Z).</summary>
            public bool AxisX { get; }
        }

        /// <summary>The doors a design message lists (its parallel <c>Door*</c> arrays, cut to the shortest — a malformed
        /// payload never throws). A missing kind is an energy door, like the server's default.</summary>
        public static List<Door> DoorsOf(SpaceShipDesign? design)
        {
            var doors = new List<Door>();
            if (design?.DoorX is null || design.DoorY is null || design.DoorZ is null || design.DoorAxisX is null)
            {
                return doors;
            }

            int n = System.Math.Min(design.DoorX.Length, System.Math.Min(design.DoorY.Length,
                System.Math.Min(design.DoorZ.Length, design.DoorAxisX.Length)));
            for (int i = 0; i < n; i++)
            {
                string? kind = design.DoorKind != null && i < design.DoorKind.Length ? design.DoorKind[i] : null;
                doors.Add(new Door(new Vector3i(design.DoorX[i], design.DoorY[i], design.DoorZ[i]),
                    string.IsNullOrEmpty(kind) ? DoorBlocks.Energy : kind!, design.DoorAxisX[i]));
            }

            return doors;
        }

        /// <summary>The cells the doors fill — each doorway three high above its base cell. The spacewalk's aim stops on
        /// them, so a door built in space can be picked up again with the mining button.</summary>
        public static HashSet<Vector3i> DoorwayCells(IEnumerable<Door> doors)
        {
            var cells = new HashSet<Vector3i>();
            foreach (var d in doors)
            {
                for (int dy = 0; dy <= 2; dy++)
                {
                    cells.Add(new Vector3i(d.Cell.X, d.Cell.Y + dy, d.Cell.Z));
                }
            }

            return cells;
        }

        /// <summary>The hatch cells a design message names (#2587): null when the server did not name them
        /// (<c>HatchKnown</c> false — an older server; the client then guesses), else the list — EMPTY when the ship has
        /// no doorway open to space, which then gets no panel.</summary>
        public static List<(int X, int Y, int Z)>? HatchOf(SpaceShipDesign? design)
        {
            if (design is null || !design.HatchKnown)
            {
                return null;
            }

            int[] xs = design.HatchX ?? System.Array.Empty<int>();
            int[] ys = design.HatchY ?? System.Array.Empty<int>();
            int[] zs = design.HatchZ ?? System.Array.Empty<int>();
            int n = System.Math.Min(xs.Length, System.Math.Min(ys.Length, zs.Length));
            var cells = new List<(int X, int Y, int Z)>(n);
            for (int i = 0; i < n; i++)
            {
                cells.Add((xs[i], ys[i], zs[i]));
            }

            return cells;
        }

        /// <summary>Decision 5: a closed door of this kind shows its blue field in space (the energy door); every other
        /// kind shows its closed panels.</summary>
        public static bool ShowsFieldInSpace(string? kind) => kind == DoorBlocks.Energy;

        /// <summary>Whether the local player may build onto a station from a spacewalk — its owner and their allies,
        /// the server's rule. An ownerless station (a generated hull) takes no build at all.</summary>
        public static bool MayBuildOnStation(string? ownerId, string? localPlayerId, IEnumerable<string>? allyIds)
        {
            if (string.IsNullOrEmpty(ownerId) || string.IsNullOrEmpty(localPlayerId))
            {
                return false;
            }

            if (ownerId == localPlayerId)
            {
                return true;
            }

            if (allyIds != null)
            {
                foreach (var ally in allyIds)
                {
                    if (ally == ownerId)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
