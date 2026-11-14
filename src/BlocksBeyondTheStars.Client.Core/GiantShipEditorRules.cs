// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3616: the structure editor's GIANT SHIP rules that need no Unity. A doorway of a giant ship (<see cref="GiantShipRules.DoorMarker"/>)
    /// must name the axis of its wall (<see cref="GiantShipRules.AxisSetting"/>) — the derelict decay reads the rooms of a wreck in
    /// space from it. A child placing slide doors should not have to know that, so a door without an axis gets one at save time,
    /// read from the walls around its doorway: <c>axis=x</c> when the wall is a plane of constant x (it continues at z − 1 and
    /// z + 2 beside the 2-wide opening, and the rooms lie at x − 1 and x + 1), <c>axis=z</c> for a plane of constant z.
    /// </summary>
    public static class GiantShipEditorRules
    {
        /// <summary>The game's doorway is 3 cells high, the marker in its bottom cell.</summary>
        private const int DoorHeight = 3;

        /// <summary>
        /// The axis the walls around the slide door marker at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>)
        /// tell: <c>"x"</c>, <c>"z"</c>, or null when they do not (a door standing free, or walls on every side). A wall cell
        /// beside the opening counts twice, an open cell across it once — so a doorway with a cupboard in front of it still reads.
        /// </summary>
        public static string? DoorAxis(Func<int, int, int, bool> solid, int x, int y, int z)
        {
            if (solid is null)
            {
                throw new ArgumentNullException(nameof(solid));
            }

            int alongZ = 0, alongX = 0;
            for (int dy = 0; dy < DoorHeight; dy++)
            {
                int cy = y + dy;

                // axis=x: the wall is the plane x — it goes on at z - 1 and z + 2, the rooms are at x - 1 and x + 1.
                alongZ += Wall(solid(x, cy, z - 1)) + Wall(solid(x, cy, z + 2)) + Open(solid(x - 1, cy, z)) + Open(solid(x + 1, cy, z));

                // axis=z: the wall is the plane z — it goes on at x - 1 and x + 2, the rooms are at z - 1 and z + 1.
                alongX += Wall(solid(x - 1, cy, z)) + Wall(solid(x + 2, cy, z)) + Open(solid(x, cy, z - 1)) + Open(solid(x, cy, z + 1));
            }

            return alongZ > alongX ? "x" : alongX > alongZ ? "z" : null;
        }

        /// <summary>
        /// The settings line of a slide door as a save writes it: unchanged when it already names <c>axis=x</c> or <c>axis=z</c>,
        /// else with the axis the walls tell (<see cref="DoorAxis"/>). <paramref name="unclear"/> is true when the line names no
        /// valid axis and the walls do not tell one either — the save then refuses with the door's place.
        /// </summary>
        public static string WithDoorAxis(string? config, Func<int, int, int, bool> solid, int x, int y, int z, out bool unclear)
        {
            var settings = MarkerSettings.Parse(config);
            unclear = false;
            if (settings.Get(GiantShipRules.AxisSetting) is "x" or "z")
            {
                return config ?? string.Empty;
            }

            string? axis = DoorAxis(solid, x, y, z);
            if (axis is null)
            {
                unclear = true;
                return config ?? string.Empty;
            }

            return settings.Set(GiantShipRules.AxisSetting, axis).ToString();
        }

        private static int Wall(bool solid) => solid ? 2 : 0;

        private static int Open(bool solid) => solid ? 0 : 1;
    }
}
