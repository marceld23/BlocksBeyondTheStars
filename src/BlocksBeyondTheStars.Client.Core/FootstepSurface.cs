// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What the player stands on, for the footsteps, the sneak edge-stop and the ladder check (#2743). A parked ship is
    /// its own object (ship-as-object: <c>GameBootstrap.LandedShips</c>), not part of the world's block grid — so the
    /// world alone read air under a player on a ship's deck and the steps sounded like rock. Here a ship's cell comes
    /// first and the world's behind it; the key → cue heuristic (formerly <c>PlayerController.StepCueFor</c>) lives here
    /// too. Unity-free: the lookups are delegates, so the headless tests drive it with a plain grid.
    /// </summary>
    public static class FootstepSurface
    {
        /// <summary>The cue for metal underfoot — a ship's deck, a monorail wagon's steel floor.</summary>
        public const string MetalCue = "step_metal";

        /// <summary>The cue when nothing better is known (stone, dirt, air).</summary>
        public const string DefaultCue = "step_rock";

        /// <summary>The block key at a cell: a parked ship's cell when there is one there (not air), else the world's.
        /// Either lookup may be null or return null/empty for "nothing known".</summary>
        public static string? KeyAt(Func<int, int, int, string?>? shipKeyAt, Func<int, int, int, string?>? worldKeyAt, int x, int y, int z)
        {
            string? ship = shipKeyAt?.Invoke(x, y, z);
            if (!string.IsNullOrEmpty(ship) && ship != "air")
            {
                return ship;
            }

            return worldKeyAt?.Invoke(x, y, z);
        }

        /// <summary>The block key at a world position (floored to its cell), ship first.</summary>
        public static string? KeyAtPoint(Func<int, int, int, string?>? shipKeyAt, Func<int, int, int, string?>? worldKeyAt, float x, float y, float z)
            => KeyAt(shipKeyAt, worldKeyAt, (int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z));

        /// <summary>The footstep cue for a player whose feet are at (<paramref name="x"/>, <paramref name="y"/>,
        /// <paramref name="z"/>): the cell half a block below the feet, ship first.</summary>
        public static string CueUnderFeet(Func<int, int, int, string?>? shipKeyAt, Func<int, int, int, string?>? worldKeyAt, float x, float y, float z)
            => CueFor(KeyAtPoint(shipKeyAt, worldKeyAt, x, y - 0.5f, z));

        /// <summary>The footstep clip for a block key (key heuristic) — the floor underfoot, or the wall a climber's hands and
        /// boots touch (#2188).</summary>
        public static string CueFor(string? key)
        {
            string k = key ?? string.Empty;
            if (k.Contains("iron", StringComparison.Ordinal) || k.Contains("metal", StringComparison.Ordinal) || k.Contains("steel", StringComparison.Ordinal))
            {
                return MetalCue;
            }

            if (k.Contains("sand", StringComparison.Ordinal))
            {
                return "step_sand";
            }

            if (k.Contains("grass", StringComparison.Ordinal))
            {
                return "step_grass";
            }

            if (k.Contains("snow", StringComparison.Ordinal) || k.Contains("ice", StringComparison.Ordinal))
            {
                return "step_snow";
            }

            return DefaultCue;
        }
    }
}
