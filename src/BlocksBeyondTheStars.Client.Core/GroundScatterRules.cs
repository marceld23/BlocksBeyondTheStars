// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Where the T0 ground details (<c>GroundScatter</c>, small grass tufts) may grow. A tester found them on his planting
    /// soil, his rock floor, his roof garden, on fresh snow and in shallow tunnels the day they first became visible
    /// (v2026.11.2), so the rules are narrow: tufts only on natural soft ground (#3120: the pebbles read as boxes and are
    /// gone), never on a built cell (#3118: the server streams which cells hold an edit), and only under the open sky
    /// (#3119: the mesher's shade floor lights a cell up to six blocks under a roof). Unity-free so the headless suite
    /// pins it; <c>ChunkMesher</c> applies it.
    /// </summary>
    public static class GroundScatterRules
    {
        /// <summary>No ground detail on this block.</summary>
        public const int None = -1;

        /// <summary>A grass tuft.</summary>
        public const int Tuft = 0;

        /// <summary>The ground detail a block's top grows: a tuft on natural soft ground, nothing on anything else.</summary>
        public static int TypeOf(string? blockKey) => blockKey is "grass" or "dirt" or "mud" ? Tuft : None;

        /// <summary>Whether a host cell gets its detail: not a built cell, and the air above sees the sky straight up (the
        /// mesher's skylight is exactly 1 only there — any cover, however high, caps it below).</summary>
        public static bool Accepts(float skylightAbove, bool edited) => !edited && skylightAbove >= 1f;
    }
}
