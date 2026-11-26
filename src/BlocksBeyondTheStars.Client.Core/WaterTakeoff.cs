// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #4028: creative flight (#836) from the water's surface. Flight ends in water (the swim has its own vertical rules),
    /// and the double-tap of Jump that starts it was not even counted while the chest was in water. Before #3860 that did not
    /// matter: Jump at the surface hopped the chest out of the water and the second tap counted. Since the float holds the
    /// chest 0.2 m under the surface (#3860/#3923, <see cref="SwimRules.FloatDepth"/>) and Jump no longer hops in open water,
    /// flight could never start there. Now a double-tap counts while the swimmer floats at the open surface (never under
    /// water) and starts flight with a short take-off: for <see cref="Grace"/> seconds the body rises at least at
    /// <see cref="Lift"/> and the water does not end the flight — about 1.6 m, enough to lift the feet out of the water.
    /// After the take-off, flying into water ends the flight as before.
    /// </summary>
    public static class WaterTakeoff
    {
        /// <summary>How long (s) a flight started from the surface ignores the water.</summary>
        public const float Grace = 0.4f;

        /// <summary>The take-off's least rise (m/s); Jump held still rises at the flight's own speed.</summary>
        public const float Lift = 4f;

        /// <summary>Whether a Jump tap may count toward the double-tap: always out of the water, in water only at the open
        /// surface (the swimmer floats with the head above it).</summary>
        public static bool TapCounts(bool inWater, bool atSurface) => !inWater || atSurface;

        /// <summary>Whether the water ends a flight this frame: in water, once the take-off's grace is spent.</summary>
        public static bool WaterEndsFlight(bool inWater, float graceLeft) => inWater && graceLeft <= 0f;

        /// <summary>The grace left after a frame of <paramref name="dt"/> seconds (never below zero).</summary>
        public static float Tick(float graceLeft, float dt) => Math.Max(0f, graceLeft - Math.Max(0f, dt));

        /// <summary>The grace a flight toggled on <paramref name="fromWater"/> starts with — none from land or the air.</summary>
        public static float Start(bool fromWater) => fromWater ? Grace : 0f;

        /// <summary>The flight's vertical speed: <paramref name="flightInput"/> (Jump rises, Descend sinks), but at least
        /// <see cref="Lift"/> while the take-off's grace lasts.</summary>
        public static float Vertical(float flightInput, float graceLeft)
            => graceLeft > 0f ? Math.Max(flightInput, Lift) : flightInput;

        /// <summary>Whether the controls line names the double-tap that flies: in a world that allows flight, out of the
        /// water and at the surface — under water the tap does nothing (#3924 dropped the hint there, and at the surface
        /// too, where it works again).</summary>
        public static bool ShowsFlyHint(bool canFly, bool swimming, bool atSurface) => canFly && (!swimming || atSurface);
    }
}
