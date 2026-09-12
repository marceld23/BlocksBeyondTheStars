// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;

namespace BlocksBeyondTheStars.Client.FarTerrain
{
    /// <summary>
    /// Where the distance haze ends (#1822). Without far terrain the haze has to hide the edge of the streamed chunks,
    /// so it is clamped to the view distance. With far terrain behind the chunks the haze reaches out toward the far-view
    /// range — but per planet: the atmosphere's density decides how much of that range stays visible, so a thin or
    /// airless sky shows a long crisp horizon and a soupy one closes in. Airless bodies keep fog off entirely (Sky), and
    /// weather still pulls the haze in hard, measured against the streamed edge as before.
    /// </summary>
    public static class FarHaze
    {
        /// <summary>The base haze end in blocks, before the weather / night / dawn multipliers Sky applies on top.</summary>
        /// <param name="streamEdge">The streamed view radius in blocks (view distance × 16, at least 32).</param>
        /// <param name="farView">The far-view range in effect (0 = off).</param>
        /// <param name="airDensity">The world's atmosphere density, 0 (thin) … 1 (soupy).</param>
        public static float BaseFar(float streamEdge, int farView, float airDensity)
        {
            float density = Math.Max(0f, Math.Min(1f, airDensity));
            if (farView <= streamEdge)
            {
                // Far view off (or not beyond the chunks): the historical mapping — full haze just inside the edge.
                return streamEdge * Lerp(1.0f, 0.85f, density);
            }

            // Thin air sees nearly the whole far range, soupy air about a third of it — never less than the old edge.
            float far = farView * Lerp(0.95f, 0.32f, (float)Math.Pow(density, 0.85));
            return Math.Max(streamEdge, far);
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
