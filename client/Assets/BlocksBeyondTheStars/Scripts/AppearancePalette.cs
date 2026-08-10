// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The offered base colours for an avatar's skin, torso, arms and legs (and the ship hull) — the tints a
    /// painting shows through, as opposed to the per-pixel <see cref="FacePalette"/>.
    /// <para>
    /// Nothing about the format limits these: they travel as plain RGB (<c>SetAppearanceIntent</c>) and
    /// persist as colours, so the picker also offers the colour wheel for anything not in this list. What the
    /// list is for is a good first answer — a curated set that looks right on the figure, with enough skin
    /// tones that no child has to settle for someone else's.
    /// </para>
    /// <para>
    /// It replaces two DIFFERENT hard-coded arrays (ten colours in the in-game menu, twelve in the main-menu
    /// Avatar Designer), which meant the same avatar offered different colours depending on where you edited
    /// it. The first ten entries are the in-game list in its original order, so a saved colour keeps its
    /// place under the arrow-key cycling that still backs the controller path.
    /// </para>
    /// </summary>
    public static class AppearancePalette
    {
        public static readonly Color[] Colors =
        {
            // The original in-game ten (order preserved — CycleAppearance walks this array).
            new Color(0.85f, 0.68f, 0.55f), // light skin
            new Color(0.55f, 0.40f, 0.28f), // brown
            new Color(0.90f, 0.85f, 0.80f), // bone
            new Color(0.80f, 0.20f, 0.20f), // red
            new Color(0.20f, 0.45f, 0.80f), // blue
            new Color(0.20f, 0.65f, 0.35f), // green
            new Color(0.90f, 0.75f, 0.20f), // gold
            new Color(0.55f, 0.30f, 0.70f), // purple
            new Color(0.25f, 0.25f, 0.32f), // dark slate
            new Color(0.92f, 0.92f, 0.95f), // white

            // Skin tones — the old list had two, which is not a choice.
            new Color(0.97f, 0.85f, 0.74f), // pale
            new Color(0.93f, 0.76f, 0.60f), // fair
            new Color(0.74f, 0.55f, 0.39f), // tan
            new Color(0.53f, 0.36f, 0.24f), // deep tan
            new Color(0.35f, 0.23f, 0.16f), // dark brown

            // Suit / clothing tones, each hue with a light and a dark option to pair against.
            new Color(0.12f, 0.16f, 0.24f), // navy
            new Color(0.35f, 0.62f, 0.92f), // sky
            new Color(0.10f, 0.42f, 0.42f), // teal
            new Color(0.40f, 0.85f, 0.80f), // aqua
            new Color(0.14f, 0.40f, 0.20f), // forest
            new Color(0.55f, 0.82f, 0.35f), // lime
            new Color(0.60f, 0.12f, 0.14f), // crimson
            new Color(0.96f, 0.55f, 0.45f), // coral
            new Color(0.85f, 0.45f, 0.12f), // orange
            new Color(0.98f, 0.85f, 0.45f), // sand
            new Color(0.35f, 0.14f, 0.45f), // violet
            new Color(0.80f, 0.60f, 0.95f), // lilac
            new Color(0.95f, 0.60f, 0.80f), // pink
            new Color(0.45f, 0.47f, 0.52f), // steel grey
            new Color(0.08f, 0.08f, 0.10f), // near black
        };

        /// <summary>The next colour along the list — the arrow-key/controller path that predates the swatch
        /// grid. A colour that is not in the list (picked freely off the wheel) starts the walk at the top.</summary>
        public static Color Next(Color current, int dir = 1)
        {
            int idx = IndexOf(current);
            int next = ((idx < 0 ? 0 : idx) + dir + Colors.Length) % Colors.Length;
            return Colors[next];
        }

        /// <summary>Position of a colour in the list, or -1 for a freely picked one.</summary>
        public static int IndexOf(Color c)
        {
            for (int i = 0; i < Colors.Length; i++)
            {
                if (Mathf.Approximately(Colors[i].r, c.r)
                    && Mathf.Approximately(Colors[i].g, c.g)
                    && Mathf.Approximately(Colors[i].b, c.b))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
