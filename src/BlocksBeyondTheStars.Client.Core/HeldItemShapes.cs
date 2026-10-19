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
                    P(0f, -0.12f, 0.02f, 0.07f, 0.16f, 0.08f, Dark), // #2799: the handle the fist closes on
                },
            };
        }

        /// <summary>The kind name (<c>HeldItem.Kind</c>) of the woodcutter's axe (#2647) — an NPC tool, not an item.</summary>
        public const string AxeKind = "Axe";

        /// <summary>The kind name (<c>HeldItem.Kind</c>) of the hunter's bow (#2647) — an NPC tool, not an item.</summary>
        public const string BowKind = "Bow";

        private static readonly Rgb ToolWood = new Rgb(0.45f, 0.30f, 0.16f);
        private static readonly Rgb BowGrip = new Rgb(0.28f, 0.18f, 0.11f);
        private static readonly Rgb BowString = new Rgb(0.92f, 0.88f, 0.78f);

        /// <summary>
        /// The parts of a wilderness NPC's axe or bow (#2647), or null for any other kind. Like every held model they point
        /// along +Z from the hand; <paramref name="tint"/> is the iron of the axe head and the wood of the bow.
        /// <list type="bullet">
        /// <item><b>Axe</b> — a wooden handle a little longer than the craftsman's hammer with the iron head across its far
        /// end: the blade on −Y, the side that leads in the avatar's overhead swing (the hoe's blade sits there for the same
        /// reason), a short poll on +Y.</item>
        /// <item><b>Bow</b> — a stave along Z centred on the leather grip in the hand, both limbs bending towards +Y and the
        /// string across their tips on that side. With the arm raised to aim, the hand points forward, +Z up and +Y back to
        /// the archer: the bow stands upright with its string towards the hunter.</item>
        /// </list>
        /// </summary>
        public static List<Part>? NpcToolParts(string kind, Rgb tint)
        {
            if (kind == AxeKind)
            {
                var edge = new Rgb(Lighter(tint.R), Lighter(tint.G), Lighter(tint.B));
                return new List<Part>
                {
                    P(0f, 0f, 0.16f, 0.05f, 0.05f, 0.50f, ToolWood),  // handle
                    P(0f, -0.02f, 0.37f, 0.06f, 0.10f, 0.09f, tint),  // the eye around the handle's end
                    P(0f, -0.11f, 0.37f, 0.035f, 0.10f, 0.15f, tint), // the blade, flaring out along the handle
                    P(0f, -0.17f, 0.37f, 0.03f, 0.02f, 0.17f, edge),  // the bright cutting edge
                    P(0f, 0.05f, 0.37f, 0.05f, 0.04f, 0.06f, tint),   // the poll
                };
            }

            if (kind == BowKind)
            {
                return new List<Part>
                {
                    P(0f, 0f, 0.02f, 0.05f, 0.06f, 0.12f, BowGrip),      // the grip in the hand
                    P(0f, 0.015f, 0.18f, 0.04f, 0.04f, 0.20f, tint),     // upper limb
                    P(0f, 0.05f, 0.34f, 0.035f, 0.035f, 0.14f, tint),    // upper limb's tip, bent back
                    P(0f, 0.015f, -0.14f, 0.04f, 0.04f, 0.20f, tint),    // lower limb
                    P(0f, 0.05f, -0.30f, 0.035f, 0.035f, 0.14f, tint),   // lower limb's tip
                    P(0f, 0.075f, 0.02f, 0.012f, 0.012f, 0.78f, BowString), // the string, tip to tip
                };
            }

            return null;
        }

        private static float Lighter(float c) => c + ((1f - c) * 0.45f);

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

        /// <summary>The kind name of an item worn on both hands (#2278, the shock and energy gloves). Deliberately not a
        /// shaped kind: the tool-look editor does not offer gloves (yet).</summary>
        public const string GlovesKind = "Gloves";

        private static readonly Rgb GloveShell = new Rgb(0.30f, 0.33f, 0.38f);
        private static readonly Rgb GloveCuff = new Rgb(0.16f, 0.18f, 0.22f);
        private static readonly Rgb ClimbCuff = new Rgb(0.22f, 0.24f, 0.28f);
        private static readonly Rgb ClimbPad = new Rgb(0.95f, 0.50f, 0.15f);
        private static readonly Rgb ClimbClaw = new Rgb(0.80f, 0.88f, 0.95f);

        /// <summary>
        /// Mirrors parts across the holder's x = 0 plane (#2278): a right glove becomes the left one. Only the x of each
        /// centre flips — sizes, colours and glow stay — because every part is an axis-aligned box: no negative scale, so
        /// no inside-out faces. Mirroring twice gives the parts back.
        /// </summary>
        public static List<Part> Mirror(IReadOnlyList<Part> parts)
        {
            var mirrored = new List<Part>(parts.Count);
            foreach (var p in parts)
            {
                mirrored.Add(new Part(new Vector3(-p.Position.X, p.Position.Y, p.Position.Z), p.Size, p.Color, p.Glow));
            }

            return mirrored;
        }

        /// <summary>
        /// One glove of a pair held on both hands (#2278), as a RIGHT glove pointing along +Z: the item's own model (its
        /// <c>heldModel</c> data), or — for a glove item without one, say from a content pack — a plain padded glove whose
        /// knuckle plate takes the kind's tint. The caller mirrors it for the left hand and puts the suit forearm behind it.
        /// </summary>
        public static List<Part> Glove(IReadOnlyList<HeldModelPart>? model, Rgb tint)
        {
            var own = FromModel(model, tint);
            if (own.Count > 0)
            {
                return own;
            }

            return new List<Part>
            {
                P(0f, 0f, 0.07f, 0.15f, 0.14f, 0.16f, GloveShell),     // the fist
                P(0f, -0.01f, -0.02f, 0.15f, 0.15f, 0.08f, GloveCuff),  // the cuff
                P(-0.085f, 0.01f, 0.05f, 0.05f, 0.07f, 0.09f, GloveShell), // the thumb, on the inner side
                new Part(new Vector3(0f, 0.075f, 0.09f), new Vector3(0.12f, 0.02f, 0.07f), tint, glow: true), // knuckle plate
            };
        }

        /// <summary>
        /// The worn climbing gear on a first-person suit hand (#2287, the climbing gloves and claws of #2192), as parts of
        /// a RIGHT hand in the frame of the bare hand (fist at z 0.07): a dark cuff, orange grip pads on the back of the
        /// fingers, the fingertips and the palm, and for the claws three pale spikes past the fingertips. Empty without
        /// either piece. Slightly larger than the hand's own boxes, so the two never fight over a face.
        /// </summary>
        public static List<Part> ClimbGear(bool gloves, bool claws)
        {
            var parts = new List<Part>();
            if (!gloves && !claws)
            {
                return parts;
            }

            parts.Add(P(0f, -0.02f, -0.02f, 0.135f, 0.135f, 0.08f, ClimbCuff));
            parts.Add(P(0f, 0.066f, 0.10f, 0.12f, 0.016f, 0.06f, ClimbPad));   // back of the fingers (faces the eye)
            parts.Add(P(0f, 0f, 0.146f, 0.12f, 0.10f, 0.016f, ClimbPad));      // fingertips
            parts.Add(P(0f, -0.066f, 0.07f, 0.11f, 0.016f, 0.10f, ClimbPad));  // palm
            if (claws)
            {
                for (int i = -1; i <= 1; i++)
                {
                    parts.Add(P(i * 0.04f, 0.02f, 0.17f, 0.018f, 0.018f, 0.05f, ClimbClaw));
                }
            }

            return parts;
        }

        /// <summary>How an item is held when it has no cube model of its own (#2534): as a miniature of the real thing.</summary>
        public enum Miniature
        {
            /// <summary>Not a miniature: a placed block's cube, a tool's parts, the gloves.</summary>
            None,

            /// <summary>A vehicle item (the speeder, the boat): its real voxel hull, scaled down.</summary>
            Vehicle,

            /// <summary>An item that places a plant or a slim prop (seeds, saplings, leaves, torches): the block's real
            /// in-world mesh — the crossed cut-out cards of a plant — scaled down.</summary>
            Plant,

            /// <summary>Everything else (materials, components, food …): the item's icon, extruded into pixel voxels.</summary>
            Icon,
        }

        /// <summary>
        /// The miniature an item is held as (#2534). A vehicle item shows its vehicle; an item that places a plant or a
        /// slim prop (<see cref="IsPlantBlock"/>) shows that block's in-world look; an item with neither a block, a tool
        /// nor a two-handed grip shows its extruded icon. Every other item keeps its cube model (<see cref="Miniature.None"/>).
        /// <paramref name="placed"/> is the block the item places, when it places one.
        /// </summary>
        public static Miniature MiniatureFor(ItemDefinition? item, BlockDefinition? placed)
        {
            if (item == null)
            {
                return Miniature.None;
            }

            if (item.Vehicle != null)
            {
                return Miniature.Vehicle;
            }

            if (!string.IsNullOrEmpty(item.PlacesBlock))
            {
                return placed != null && IsPlantBlock(placed.Key, placed.Category) ? Miniature.Plant : Miniature.None;
            }

            if (item.Tool != null || HeldGrips.IsGloves(item))
            {
                return Miniature.None;
            }

            return Miniature.Icon;
        }

        /// <summary>
        /// True for a block the world draws as something other than a full cube that a held cube would misrepresent:
        /// every plant (<c>flora_*</c> — crossed cut-out cards or a rounded solid plant), the leafy flora blocks (tree
        /// crowns, fronds, mushroom parts) and the torch/lantern slim props, lit or not (the mesher's slim-prop set).
        /// </summary>
        public static bool IsPlantBlock(string? key, string? category)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            return key!.StartsWith("flora_", System.StringComparison.Ordinal)
                || category == "flora"
                || key is "torch" or "lantern" or "torch_off" or "lantern_off";
        }

        private static Part P(float x, float y, float z, float w, float h, float d, Rgb color)
            => new Part(new Vector3(x, y, z), new Vector3(w, h, d), color);
    }
}
