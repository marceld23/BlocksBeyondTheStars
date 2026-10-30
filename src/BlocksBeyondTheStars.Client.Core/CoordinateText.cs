// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Globalization;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The player's position as the world's own block coordinates (#2993): the client's scene position runs on unbounded
    /// as the player laps a round world, so the planet map, the opt-in HUD line and <c>/pos</c> wrap it back — X into
    /// [0, circumference), Z into the latitude band, Y floored — and print it the same way everywhere:
    /// <c>X 812 · Y 71 · Z -1904</c>. These are the numbers the Quantum Teleporter takes (#2995).
    /// </summary>
    public static class CoordinateText
    {
        /// <summary>The canonical block cell of a scene position on a world of <paramref name="circumference"/>.</summary>
        public static (int X, int Y, int Z) Canonical(double sceneX, double sceneY, double sceneZ, int circumference)
        {
            int x = (int)Math.Floor(WorldConstants.WrapX(sceneX, circumference));
            int z = (int)Math.Floor(WorldConstants.WrapZ(sceneZ, circumference));
            return (x, (int)Math.Floor(sceneY), z);
        }

        /// <summary>"X 812 · Y 71 · Z -1904" for a scene position (invariant digits, so every language reads the same numbers
        /// the teleporter takes).</summary>
        public static string Format(double sceneX, double sceneY, double sceneZ, int circumference)
        {
            var (x, y, z) = Canonical(sceneX, sceneY, sceneZ, circumference);
            return "X " + x.ToString(CultureInfo.InvariantCulture)
                + " · Y " + y.ToString(CultureInfo.InvariantCulture)
                + " · Z " + z.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Reads a typed coordinate (the Quantum Teleporter's fields): an integer or a decimal with a point or a comma,
        /// optionally negative. False for anything else, or a number beyond any world.</summary>
        public static bool TryParse(string? text, out float value)
        {
            value = 0f;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string t = text!.Trim().Replace(',', '.');
            if (!float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) || float.IsNaN(v) || Math.Abs(v) > 1_000_000f)
            {
                return false;
            }

            value = v;
            return true;
        }
    }
}
