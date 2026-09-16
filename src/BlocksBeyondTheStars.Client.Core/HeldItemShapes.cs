// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Numerics;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The cube parts of a held drill, gun, blade or scanner, per item (#1931, "all drills, pistols and knives look the same
    /// in the hand"). <c>HeldItem</c> used to build one model per kind; this table gives every item of those kinds its own
    /// body, barrel, blade and glow. The first item of each kind (and any item the table does not know) keeps the model the
    /// kind always had. Unity-free so the headless suite pins it; <c>HeldItem.Build</c> turns the parts into cubes. A part
    /// sits at <see cref="Part.Position"/> (centre) with <see cref="Part.Size"/>, pointing along +Z from the holder.
    /// </summary>
    public static class HeldItemShapes
    {
        /// <summary>A linear 0..1 colour (converted to the shader's space by the caller).</summary>
        public readonly struct Rgb
        {
            public Rgb(float r, float g, float b)
            {
                R = r;
                G = g;
                B = b;
            }

            public float R { get; }

            public float G { get; }

            public float B { get; }
        }

        /// <summary>One cube of a held model.</summary>
        public readonly struct Part
        {
            public Part(Vector3 position, Vector3 size, Rgb color)
            {
                Position = position;
                Size = size;
                Color = color;
            }

            public Vector3 Position { get; }

            public Vector3 Size { get; }

            public Rgb Color { get; }
        }

        private static readonly Rgb Metal = new Rgb(0.55f, 0.58f, 0.64f);
        private static readonly Rgb Dark = new Rgb(0.20f, 0.22f, 0.26f);
        private static readonly Rgb Steel = new Rgb(0.78f, 0.81f, 0.86f);
        private static readonly Rgb Wood = new Rgb(0.40f, 0.27f, 0.15f);

        /// <summary>The parts for a held item of <paramref name="kind"/> ("Drill", "Gun", "Blade", "Scanner"), or null for a
        /// kind this table does not shape (blocks, gadgets, the hand, NPC tools). <paramref name="tint"/> is the kind's
        /// colour for the base model.</summary>
        public static List<Part>? Parts(string kind, string? itemKey, Rgb tint) => kind switch
        {
            "Drill" => Drill(itemKey, tint),
            "Gun" => Gun(itemKey, tint),
            "Blade" => Blade(itemKey, tint),
            "Scanner" => Scanner(itemKey, tint),
            _ => null,
        };

        private static Part P(float x, float y, float z, float w, float h, float d, Rgb color)
            => new Part(new Vector3(x, y, z), new Vector3(w, h, d), color);

        private static List<Part> Drill(string? item, Rgb tint) => item switch
        {
            // Titanium: a blue-grey body, a longer steel bit, an orange power coil and a vent on top.
            "titanium_drill" => new List<Part>
            {
                P(0f, 0f, 0.04f, 0.16f, 0.16f, 0.26f, new Rgb(0.46f, 0.56f, 0.68f)),
                P(0f, 0f, 0.27f, 0.08f, 0.08f, 0.24f, Steel),
                P(0f, 0f, 0.17f, 0.13f, 0.13f, 0.03f, new Rgb(0.95f, 0.55f, 0.20f)),
                P(0f, 0.095f, 0.02f, 0.06f, 0.03f, 0.14f, Dark),
                P(0f, -0.12f, -0.04f, 0.07f, 0.16f, 0.08f, Dark),
            },

            // Diamond: a graphite body with side braces, a steel crown and a glowing diamond tip.
            "diamond_drill" => new List<Part>
            {
                P(0f, 0f, 0.04f, 0.15f, 0.15f, 0.26f, new Rgb(0.28f, 0.30f, 0.36f)),
                P(0.10f, 0f, 0.06f, 0.03f, 0.10f, 0.22f, Metal),
                P(-0.10f, 0f, 0.06f, 0.03f, 0.10f, 0.22f, Metal),
                P(0f, 0f, 0.20f, 0.12f, 0.12f, 0.05f, Metal),
                P(0f, 0f, 0.29f, 0.07f, 0.07f, 0.14f, new Rgb(0.55f, 0.95f, 1f)),
                P(0f, -0.12f, -0.04f, 0.07f, 0.16f, 0.08f, Dark),
            },

            // Mining beam: a pale emitter with a focusing ring, an orange lens and a yellow energy cell on top.
            "mining_beam" => new List<Part>
            {
                P(0f, 0f, 0.02f, 0.18f, 0.15f, 0.24f, new Rgb(0.82f, 0.84f, 0.88f)),
                P(0f, 0f, 0.20f, 0.07f, 0.07f, 0.14f, Dark),
                P(0f, 0f, 0.25f, 0.14f, 0.14f, 0.03f, Metal),
                P(0f, 0f, 0.29f, 0.06f, 0.06f, 0.04f, new Rgb(1f, 0.62f, 0.22f)),
                P(0f, 0.11f, -0.02f, 0.08f, 0.06f, 0.16f, new Rgb(0.95f, 0.80f, 0.25f)),
                P(0f, -0.12f, -0.04f, 0.07f, 0.16f, 0.08f, Dark),
            },

            // The basic drill — the model every drill had.
            _ => new List<Part>
            {
                P(0f, 0f, 0.04f, 0.16f, 0.16f, 0.26f, Metal),
                P(0f, 0f, 0.24f, 0.09f, 0.09f, 0.18f, tint),
                P(0f, -0.12f, -0.04f, 0.07f, 0.16f, 0.08f, Dark),
            },
        };

        private static List<Part> Gun(string? item, Rgb tint) => item switch
        {
            // Scrap pistol: a short rusty barrel, a bolted-on plate, a tape band and a wooden grip.
            "scrap_pistol" => new List<Part>
            {
                P(0f, 0f, 0.08f, 0.09f, 0.10f, 0.26f, new Rgb(0.45f, 0.30f, 0.20f)),
                P(0.055f, 0.02f, 0.06f, 0.02f, 0.08f, 0.12f, Metal),
                P(0f, 0f, 0.14f, 0.10f, 0.11f, 0.03f, new Rgb(0.70f, 0.62f, 0.45f)),
                P(0f, 0f, 0.23f, 0.05f, 0.05f, 0.05f, new Rgb(0.35f, 0.35f, 0.35f)),
                P(0f, -0.12f, -0.04f, 0.08f, 0.16f, 0.10f, Wood),
            },

            // Gauss pistol: twin rails over the body with cyan coils and a cyan muzzle.
            "gauss_pistol" => new List<Part>
            {
                P(0f, -0.01f, 0.06f, 0.09f, 0.09f, 0.24f, Dark),
                P(0.035f, 0.045f, 0.20f, 0.02f, 0.02f, 0.30f, Metal),
                P(-0.035f, 0.045f, 0.20f, 0.02f, 0.02f, 0.30f, Metal),
                P(0f, 0.045f, 0.12f, 0.10f, 0.04f, 0.03f, new Rgb(0.55f, 0.90f, 1f)),
                P(0f, 0.045f, 0.24f, 0.10f, 0.04f, 0.03f, new Rgb(0.55f, 0.90f, 1f)),
                P(0f, 0.045f, 0.36f, 0.05f, 0.04f, 0.03f, new Rgb(0.55f, 0.90f, 1f)),
                P(0f, -0.13f, -0.06f, 0.08f, 0.18f, 0.10f, Dark),
            },

            // Laser pistol: a sleek white body with a red stripe, a long thin barrel and a red lens.
            "laser_pistol" => new List<Part>
            {
                P(0f, 0f, 0.06f, 0.08f, 0.09f, 0.26f, new Rgb(0.85f, 0.87f, 0.90f)),
                P(0f, 0.05f, 0.06f, 0.085f, 0.012f, 0.22f, new Rgb(1f, 0.35f, 0.30f)),
                P(0f, 0.01f, 0.26f, 0.04f, 0.04f, 0.20f, Dark),
                P(0f, 0.01f, 0.37f, 0.05f, 0.05f, 0.03f, new Rgb(1f, 0.35f, 0.30f)),
                P(0f, -0.13f, -0.06f, 0.08f, 0.18f, 0.10f, Dark),
            },

            // Plasma blaster: a bulky body, a purple plasma canister on top and a wide glowing muzzle.
            "plasma_blaster" => new List<Part>
            {
                P(0f, 0f, 0.06f, 0.14f, 0.13f, 0.30f, Dark),
                P(0f, 0.10f, 0.02f, 0.07f, 0.07f, 0.16f, new Rgb(0.80f, 0.45f, 1f)),
                P(0f, 0f, 0.25f, 0.11f, 0.11f, 0.07f, Metal),
                P(0f, 0f, 0.295f, 0.08f, 0.08f, 0.03f, new Rgb(0.85f, 0.50f, 1f)),
                P(0f, -0.14f, -0.06f, 0.09f, 0.18f, 0.11f, Dark),
            },

            // Any other gun — the model every gun had.
            _ => new List<Part>
            {
                P(0f, 0f, 0.10f, 0.09f, 0.10f, 0.34f, Dark),
                P(0f, 0f, 0.30f, 0.05f, 0.05f, 0.08f, tint),
                P(0f, -0.13f, -0.06f, 0.08f, 0.18f, 0.10f, Dark),
            },
        };

        private static List<Part> Blade(string? item, Rgb tint) => item switch
        {
            // Machete: a wooden handle, a small guard and a long, wide steel blade.
            "machete" => new List<Part>
            {
                P(0f, -0.04f, -0.02f, 0.06f, 0.07f, 0.16f, Wood),
                P(0f, -0.01f, 0.07f, 0.07f, 0.10f, 0.02f, Dark),
                P(0f, 0.03f, 0.30f, 0.025f, 0.16f, 0.40f, new Rgb(0.72f, 0.75f, 0.80f)),
            },

            // Vibro knife: a short blade with a blue humming edge and a glowing emitter node.
            "vibro_knife" => new List<Part>
            {
                P(0f, -0.03f, 0f, 0.05f, 0.06f, 0.13f, Dark),
                P(0f, -0.03f, 0.075f, 0.06f, 0.07f, 0.03f, new Rgb(0.35f, 0.75f, 1f)),
                P(0f, 0f, 0.17f, 0.02f, 0.08f, 0.18f, Steel),
                P(0f, 0.045f, 0.17f, 0.025f, 0.015f, 0.16f, new Rgb(0.35f, 0.75f, 1f)),
            },

            // Plasma sword: a metal hilt, a crossguard and a long glowing magenta blade.
            "plasma_sword" => new List<Part>
            {
                P(0f, -0.04f, -0.02f, 0.06f, 0.06f, 0.18f, Metal),
                P(0f, -0.01f, 0.08f, 0.05f, 0.16f, 0.03f, Dark),
                P(0f, 0f, 0.36f, 0.035f, 0.06f, 0.52f, new Rgb(1f, 0.40f, 0.85f)),
            },

            // Any other blade — the model every blade had.
            _ => new List<Part>
            {
                P(0f, -0.04f, 0f, 0.06f, 0.06f, 0.16f, Dark),
                P(0f, 0.02f, 0.26f, 0.03f, 0.18f, 0.34f, tint),
            },
        };

        private static List<Part> Scanner(string? item, Rgb tint) => item switch
        {
            // Advanced scanner: a pale body with a screen, a dish in front and twin glowing antennas.
            "advanced_scanner" => new List<Part>
            {
                P(0f, 0f, 0.06f, 0.18f, 0.13f, 0.20f, new Rgb(0.80f, 0.86f, 0.92f)),
                P(0f, 0.07f, 0.02f, 0.13f, 0.012f, 0.12f, new Rgb(0.45f, 0.85f, 0.95f)),
                P(0f, 0f, 0.175f, 0.12f, 0.12f, 0.02f, Metal),
                P(0.05f, 0.12f, 0.13f, 0.025f, 0.12f, 0.025f, Dark),
                P(-0.05f, 0.12f, 0.13f, 0.025f, 0.12f, 0.025f, Dark),
                P(0.05f, 0.19f, 0.13f, 0.05f, 0.05f, 0.05f, new Rgb(0.45f, 0.85f, 0.95f)),
                P(-0.05f, 0.19f, 0.13f, 0.05f, 0.05f, 0.05f, new Rgb(0.45f, 0.85f, 0.95f)),
            },

            // The hand scanner — the model every scanner had.
            _ => new List<Part>
            {
                P(0f, 0f, 0.06f, 0.16f, 0.12f, 0.18f, Metal),
                P(0f, 0.10f, 0.12f, 0.03f, 0.10f, 0.03f, Dark),
                P(0f, 0.16f, 0.12f, 0.06f, 0.06f, 0.06f, tint),
            },
        };
    }
}
