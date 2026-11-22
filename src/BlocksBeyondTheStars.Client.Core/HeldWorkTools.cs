// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Numerics;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The working NPCs' hoe and hammer (#1869; the broom is a re-tinted hoe, the blockfarmer's pick a re-tinted hammer) as
    /// held parts. The cubes are the ones <c>HeldItem.Build</c> always drew; they live here so the form rule
    /// (<see cref="HeldPartForms.Automatic"/>) gives their wooden handles the chamfered corners of every other
    /// tool's shaft.
    /// Kept apart from <see cref="HeldItemShapes.NpcToolParts"/> on purpose: that list is the wilderness folk's (#2647).
    /// </summary>
    public static class HeldWorkTools
    {
        /// <summary>The kind name (<c>HeldItem.Kind</c>) of the gardener's hoe.</summary>
        public const string HoeKind = "Hoe";

        /// <summary>The kind name (<c>HeldItem.Kind</c>) of the craftsman's hammer.</summary>
        public const string HammerKind = "Hammer";

        private static readonly HeldItemShapes.Rgb HoeWood = new HeldItemShapes.Rgb(0.45f, 0.30f, 0.16f);
        private static readonly HeldItemShapes.Rgb HammerWood = new HeldItemShapes.Rgb(0.40f, 0.27f, 0.15f);

        /// <summary>
        /// The parts of the hoe or the hammer in <paramref name="tint"/> (the iron), or null for any other kind.
        /// <list type="bullet">
        /// <item><b>Hoe</b> — a long wooden shaft (4.5 × 4.5 × 70 cm, a bar: chamfered) with a flat iron blade bent down at its
        /// far end (16 × 14 × 3 cm: its wide sides differ by 12 %, no plate — a box).</item>
        /// <item><b>Hammer</b> — a short handle (5 × 5 × 34 cm, a bar: chamfered) with a heavy head across its end
        /// (9 × 18 × 9 cm: a bar wider than 8 cm, a body — a box).</item>
        /// </list>
        /// </summary>
        public static List<HeldItemShapes.Part>? Parts(string kind, HeldItemShapes.Rgb tint)
        {
            if (kind == HoeKind)
            {
                return HeldPartForms.Automatic(kind, new List<HeldItemShapes.Part>
                {
                    new HeldItemShapes.Part(new Vector3(0f, 0f, 0.20f), new Vector3(0.045f, 0.045f, 0.70f), HoeWood, false, HeldMaterials.Wood), // shaft
                    new HeldItemShapes.Part(new Vector3(0f, -0.07f, 0.54f), new Vector3(0.16f, 0.14f, 0.03f), tint, false, HeldMaterials.Metal),  // blade
                }, null);
            }

            if (kind == HammerKind)
            {
                return HeldPartForms.Automatic(kind, new List<HeldItemShapes.Part>
                {
                    new HeldItemShapes.Part(new Vector3(0f, 0f, 0.12f), new Vector3(0.05f, 0.05f, 0.34f), HammerWood, false, HeldMaterials.Wood), // handle
                    new HeldItemShapes.Part(new Vector3(0f, 0f, 0.30f), new Vector3(0.09f, 0.18f, 0.09f), tint, false, HeldMaterials.Metal),       // head
                }, null);
            }

            return null;
        }
    }
}
