// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Numerics;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The cube parts of a held drill, gun, blade or scanner (#1931, "all drills, pistols and knives look the same in
    /// the hand"). Since #1962 an item's own look is DATA — <see cref="ItemDefinition.HeldModel"/> in
    /// <c>data/items.json</c> — so a content pack can give a new tool its look, and a player's own tool look (#1963)
    /// is the same kind of thing as an official one. What stays here is the model every item of a KIND falls back
    /// to (the basic drill, the plain gun …), which takes the kind's tint. Unity-free so the headless suite pins it;
    /// <c>HeldItem.Build</c> turns the parts into cubes. A part sits at <see cref="Part.Position"/> (centre) with
    /// <see cref="Part.Size"/>, pointing along +Z from the holder.
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
            public Part(Vector3 position, Vector3 size, Rgb color, bool glow = false)
            {
                Position = position;
                Size = size;
                Color = color;
                Glow = glow;
            }

            public Vector3 Position { get; }

            public Vector3 Size { get; }

            public Rgb Color { get; }

            /// <summary>True when the part glows (keeps its colour in the dark).</summary>
            public bool Glow { get; }
        }

        private static readonly Rgb Metal = new Rgb(0.55f, 0.58f, 0.64f);
        private static readonly Rgb Dark = new Rgb(0.20f, 0.22f, 0.26f);

        /// <summary>True for the kinds that are built from parts ("Drill", "Gun", "Blade", "Scanner") — the ones an
        /// item model or a player's tool look can replace.</summary>
        public static bool IsShapedKind(string kind) => kind is "Drill" or "Gun" or "Blade" or "Scanner";

        /// <summary>
        /// The parts for a held item of <paramref name="kind"/>, or null for a kind that is not built from parts
        /// (blocks, gadgets, the hand, NPC tools). <paramref name="model"/> is the item's own look — from the item
        /// data, or a player's tool look — and wins when it holds at least one drawable part; otherwise the kind's
        /// model is used. <paramref name="tint"/> is the kind's colour, which a part asks for with <c>"tint"</c>.
        /// </summary>
        public static List<Part>? Parts(string kind, Rgb tint, IReadOnlyList<HeldModelPart>? model = null)
        {
            if (!IsShapedKind(kind))
            {
                return null;
            }

            var own = FromModel(model, tint);
            if (own.Count > 0)
            {
                return own;
            }

            return kind switch
            {
                "Drill" => new List<Part>
                {
                    P(0f, 0f, 0.04f, 0.16f, 0.16f, 0.26f, Metal),
                    P(0f, 0f, 0.24f, 0.09f, 0.09f, 0.18f, tint),
                    P(0f, -0.12f, -0.04f, 0.07f, 0.16f, 0.08f, Dark),
                },
                "Gun" => new List<Part>
                {
                    P(0f, 0f, 0.10f, 0.09f, 0.10f, 0.34f, Dark),
                    P(0f, 0f, 0.30f, 0.05f, 0.05f, 0.08f, tint),
                    P(0f, -0.13f, -0.06f, 0.08f, 0.18f, 0.10f, Dark),
                },
                "Blade" => new List<Part>
                {
                    P(0f, -0.04f, 0f, 0.06f, 0.06f, 0.16f, Dark),
                    P(0f, 0.02f, 0.26f, 0.03f, 0.18f, 0.34f, tint),
                },
                _ => new List<Part> // Scanner
                {
                    P(0f, 0f, 0.06f, 0.16f, 0.12f, 0.18f, Metal),
                    P(0f, 0.10f, 0.12f, 0.03f, 0.10f, 0.03f, Dark),
                    P(0f, 0.16f, 0.12f, 0.06f, 0.06f, 0.06f, tint),
                },
            };
        }

        /// <summary>Turns model data into parts, dropping what cannot be drawn and everything past
        /// <see cref="HeldModelPart.MaxParts"/> — a model from a content pack or another player is not trusted.</summary>
        public static List<Part> FromModel(IReadOnlyList<HeldModelPart>? model, Rgb tint)
        {
            var parts = new List<Part>();
            if (model == null)
            {
                return parts;
            }

            foreach (var m in model)
            {
                if (parts.Count >= HeldModelPart.MaxParts)
                {
                    break;
                }

                if (m == null || !m.IsValid())
                {
                    continue;
                }

                Rgb color = tint;
                if (m.C != "tint" && HeldModelPart.TryParseColor(m.C, out float r, out float g, out float b))
                {
                    color = new Rgb(r, g, b);
                }

                parts.Add(new Part(new Vector3(m.P[0], m.P[1], m.P[2]), new Vector3(m.S[0], m.S[1], m.S[2]), color, m.G));
            }

            return parts;
        }

        private static Part P(float x, float y, float z, float w, float h, float d, Rgb color)
            => new Part(new Vector3(x, y, z), new Vector3(w, h, d), color);
    }
}
