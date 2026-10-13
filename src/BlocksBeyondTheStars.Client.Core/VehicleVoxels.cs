// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The hand-authored voxel designs of the speeder and the boat (#1215), in their 3×2×5 hull box
    /// (<see cref="VehicleHull"/>, the bow / windscreen at z = 4). One source for the parked or driven vehicle
    /// (<c>SpeederView</c>) and the miniature held in the hand (#2534, "in my hand it looked like a tool"), so the two
    /// can never drift apart. Unity-free so the headless suite pins the designs. Each material is a chain of block keys,
    /// first one the content has wins; a design whose main material is missing entirely comes back empty.
    /// </summary>
    public static class VehicleVoxels
    {
        /// <summary>The vehicle kind a <c>vehicle</c> item names for the boat (everything else is a speeder).</summary>
        public const string BoatKind = "boat";

        private static readonly string[] SpeederHull = { "iron_wall", "metal_panel", "stone" };
        private static readonly string[] SpeederGlass = { "glass" };
        private static readonly string[] BoatWood = { "wood_log", "wood_crate", "iron_wall", "stone" };
        private static readonly string[] BoatMetal = { "iron_wall", "metal_panel" };

        /// <summary>The cells of the vehicle of <paramref name="kind"/> (<see cref="BoatKind"/> or a speeder), with
        /// <paramref name="block"/> resolving a block key to its id (null = not in this content).</summary>
        public static Dictionary<Vector3i, BlockId> Cells(string? kind, Func<string, BlockId?> block)
            => kind == BoatKind ? BoatCells(block) : SpeederCells(block);

        /// <summary>The speeder: a small open sled — chassis floor, side rails, a backrest and a glass windscreen.</summary>
        public static Dictionary<Vector3i, BlockId> SpeederCells(Func<string, BlockId?> block)
        {
            var cells = new Dictionary<Vector3i, BlockId>();
            var hull = First(SpeederHull, block);
            if (hull == null)
            {
                return cells;
            }

            var glass = First(SpeederGlass, block) ?? hull.Value;

            // Floor (y=0): a 3×5 chassis.
            for (int x = 0; x <= 2; x++)
            {
                for (int z = 0; z <= 4; z++)
                {
                    cells[new Vector3i(x, 0, z)] = hull.Value;
                }
            }

            // Side rails (y=1, x=0 and x=2) along the middle.
            for (int z = 1; z <= 3; z++)
            {
                cells[new Vector3i(0, 1, z)] = hull.Value;
                cells[new Vector3i(2, 1, z)] = hull.Value;
            }

            // Backrest (y=1, z=0) and the glass windscreen at the front (y=1, z=4).
            for (int x = 0; x <= 2; x++)
            {
                cells[new Vector3i(x, 1, 0)] = hull.Value;
                cells[new Vector3i(x, 1, 4)] = glass;
            }

            return cells;
        }

        /// <summary>The boat: a 3×5 wooden hull with a pointed bow, low gunwales, a transom and a small metal outboard
        /// motor at the stern. Same footprint as the speeder so the centring offset and the driver seat line up.</summary>
        public static Dictionary<Vector3i, BlockId> BoatCells(Func<string, BlockId?> block)
        {
            var cells = new Dictionary<Vector3i, BlockId>();
            var wood = First(BoatWood, block);
            if (wood == null)
            {
                return cells;
            }

            var metal = First(BoatMetal, block) ?? wood.Value;

            // Hull floor (y=0): full 3 wide from the stern to z=3, a single centre block as the bow tip.
            for (int x = 0; x <= 2; x++)
            {
                for (int z = 0; z <= 3; z++)
                {
                    cells[new Vector3i(x, 0, z)] = wood.Value;
                }
            }

            cells[new Vector3i(1, 0, 4)] = wood.Value;

            // Gunwales (y=1) along both sides, meeting at the bow post.
            for (int z = 1; z <= 3; z++)
            {
                cells[new Vector3i(0, 1, z)] = wood.Value;
                cells[new Vector3i(2, 1, z)] = wood.Value;
            }

            cells[new Vector3i(1, 1, 4)] = wood.Value;

            // Transom (y=1, z=0) with the outboard motor in the middle.
            cells[new Vector3i(0, 1, 0)] = wood.Value;
            cells[new Vector3i(2, 1, 0)] = wood.Value;
            cells[new Vector3i(1, 1, 0)] = metal;

            return cells;
        }

        private static BlockId? First(string[] keys, Func<string, BlockId?> block)
        {
            foreach (var key in keys)
            {
                var id = block(key);
                if (id != null)
                {
                    return id;
                }
            }

            return null;
        }
    }
}
