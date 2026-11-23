// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The ceiling light aboard no longer burns out to one white area. <see cref="Cap"/> is the brightest a white texel of
    /// a strong emitter (emission above 0.45: lamps, strip lights, torches, lava, glowing plants, force fields, glow-dyed
    /// blocks) may reach under cover, in linear HDR before tone mapping; a soft knee starts at 0.6 × that value. The block
    /// shader scales such a face as a whole (<c>BbtsLookCabinLimit</c> in <c>BlockAtlas.shader</c>), so its lens and rim
    /// keep their contrast and the bloom only takes a glow. A light_white face goes from about 4.5 to about 1.2.
    /// </summary>
    public static class LookCabinLight
    {
        private static readonly int CapId = Shader.PropertyToID("_Sc_LookCabin");

        /// <summary>The limit.</summary>
        public const float Cap = 1.5f;

        /// <summary>Puts the limit into the block shader's global (cheap; called with every grade update).</summary>
        public static void Publish() => Shader.SetGlobalFloat(CapId, Cap);
    }
}
