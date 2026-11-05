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

        /// <summary>The cue when nothing better is known (stone, dirt, ore, air).</summary>
        public const string DefaultCue = "step_rock";

        /// <summary>#3239: wading — the cell at the feet is water while the floor below holds.</summary>
        public const string WaterCue = "step_water";

        /// <summary>#3239: planks, logs, crates, a wooden trapdoor.</summary>
        public const string WoodCue = "step_wood";

        /// <summary>#3239: foliage and litter — a walk-through crown, moss, needles.</summary>
        public const string LeavesCue = "step_leaves";

        /// <summary>#3239: an open metal deck that rattles — a bridge deck, a lift platform, a catwalk.</summary>
        public const string GrateCue = "step_grate";

        /// <summary>#3239: crystal and glass.</summary>
        public const string CrystalCue = "step_crystal";

        private const string GrassCue = "step_grass";

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
        /// <paramref name="z"/>): what covers the ground at the feet (#3239: shallow water, moss, a grass tuft), else the cell
        /// half a block below the feet — ship first in both.</summary>
        public static string CueUnderFeet(Func<int, int, int, string?>? shipKeyAt, Func<int, int, int, string?>? worldKeyAt, float x, float y, float z)
            => CoverCueFor(KeyAtPoint(shipKeyAt, worldKeyAt, x, y + 0.1f, z))
               ?? CueFor(KeyAtPoint(shipKeyAt, worldKeyAt, x, y - 0.5f, z));

        /// <summary>#3239: the cue of what covers the ground at the feet, or null when it adds nothing. Water there is wading;
        /// a walk-through plant cell (moss, needles, a leaf crown, a grass tuft) colours the step over the floor below —
        /// they have no collider, so the cell under the feet alone is always the soil beneath them.</summary>
        public static string? CoverCueFor(string? feetKey)
        {
            string k = feetKey ?? string.Empty;
            if (k == "water")
            {
                return WaterCue;
            }

            if (IsFoliage(k))
            {
                return LeavesCue;
            }

            return Has(k, "grass") || Has(k, "clover") ? GrassCue : null;
        }

        /// <summary>The footstep clip for a block key (key heuristic) — the floor underfoot, or the wall a climber's hands and
        /// boots touch (#2188). #3239 added water, wood, leaves, grate and crystal; an ore is the stone it sits in (it used to
        /// clank like metal for its "iron").</summary>
        public static string CueFor(string? key)
        {
            string k = key ?? string.Empty;
            if (k.Length == 0)
            {
                return DefaultCue;
            }

            if (k == "water")
            {
                return WaterCue;
            }

            if (Has(k, "grate") || Has(k, "catwalk") || Has(k, "mesh") || k == "bridge_deck" || k == "lift_platform")
            {
                return GrateCue;
            }

            if (Has(k, "crystal") || Has(k, "glass") || Has(k, "quartz") || Has(k, "gem") || Has(k, "prism") || k == "obsidian")
            {
                return CrystalCue;
            }

            if (k.EndsWith("_ore", StringComparison.Ordinal))
            {
                return DefaultCue;
            }

            return MaterialCue(k);
        }

        /// <summary>#3239: the cue for a take of a surface — <c>step_wood</c>, <c>step_wood_2</c>, <c>step_wood_3</c> — falling
        /// back to take 1 when that take is not bundled, and to <see cref="DefaultCue"/> when the surface has no clip at all.
        /// <paramref name="has"/> answers whether a clip exists (null = assume it does).</summary>
        public static string TakeClip(string cue, int take, Func<string, bool>? has)
        {
            string surface = string.IsNullOrEmpty(cue) ? DefaultCue : cue;
            if (has != null && !has(surface))
            {
                surface = DefaultCue;
            }

            if (take <= 1)
            {
                return surface;
            }

            string id = surface + "_" + take;
            return has == null || has(id) ? id : surface;
        }

        /// <summary>The built and natural materials, in an order where the more specific word wins: wood before metal (a wooden
        /// crate is no transport crate), foliage after both, then the soft grounds.</summary>
        private static string MaterialCue(string k)
        {
            if (Has(k, "wood") || Has(k, "plank") || k.EndsWith("_log", StringComparison.Ordinal) || k.StartsWith("trapdoor", StringComparison.Ordinal)
                || k == "workbench" || k == "ladder" || (Has(k, "crate") && k != "transport_crate"))
            {
                return WoodCue;
            }

            if (Has(k, "iron") || Has(k, "metal") || Has(k, "steel") || Has(k, "panel") || Has(k, "scrap") || Has(k, "floor")
                || Has(k, "wall") || Has(k, "pipe") || Has(k, "tank") || k.StartsWith("ship_", StringComparison.Ordinal)
                || k.EndsWith("_block", StringComparison.Ordinal) || k == "transport_crate")
            {
                return MetalCue;
            }

            if (IsFoliage(k))
            {
                return LeavesCue;
            }

            if ((Has(k, "sand") && !Has(k, "sandstone")) || k == "salt" || k == "ash")
            {
                return "step_sand";
            }

            if (Has(k, "grass"))
            {
                return GrassCue;
            }

            return Has(k, "snow") || k == "ice" ? "step_snow" : DefaultCue;
        }

        /// <summary>Leaves, needles, fronds, litter and moss — but a mossy stone is still stone.</summary>
        private static bool IsFoliage(string k)
            => Has(k, "leaves") || Has(k, "leaf") || Has(k, "needles") || Has(k, "frond") || Has(k, "litter")
               || (Has(k, "moss") && !Has(k, "stone")) || Has(k, "lichen");

        private static bool Has(string key, string word) => key.Contains(word, StringComparison.Ordinal);
    }
}
