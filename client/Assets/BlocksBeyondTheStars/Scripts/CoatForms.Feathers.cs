// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Feathers as geometry: a wing's flight feathers and coverts, a tail fan and a crest. A feather is a
    /// slim flat slat with a rounded end and a slightly raised keel. Everything is built in the animal's UNIT space
    /// (lengths divided by its unit; the feather object is scaled by the unit), so every animal of a species shares
    /// the meshes. As with the fur, a vertex holds brightness, accent weight (the tip or a band), dark weight (the
    /// keel) and bend weight; the vane reads the wing's coat through a flat texture coordinate.
    /// </summary>
    public static partial class CoatForms
    {
        /// <summary>The feather look of one species, rolled once from its seed.</summary>
        public struct Plumes
        {
            public int Secondaries, Primaries;

            /// <summary>A factor on the flight feathers' length.</summary>
            public float Length;

            /// <summary>How far the outermost primary turns outward (radians from "straight back").</summary>
            public float Sweep;

            /// <summary>0 = round ends, 1 = pointed, 2 = square.</summary>
            public int Tip;

            /// <summary>0 = plain, 1 = the tip in the accent colour, 2 = a band in the accent colour.</summary>
            public int Pattern;

            /// <summary>0 = a fan, 1 = a fork, 2 = a short fan with two long streamers.</summary>
            public int TailShape;

            public int TailCount;
            public float TailLength;
            public int CrestCount;
        }

        /// <summary>One wing panel's measures, in units (the animal's unit = 1).</summary>
        public struct Wing
        {
            /// <summary>The panel's span, the wing's chord and the old slab's thickness.</summary>
            public float Span, Chord, Thick;

            /// <summary>−1 = the left wing (it grows toward −X), +1 = the right one.</summary>
            public float Side;

            /// <summary>The species' unit in metres: the coat's texture repeats once per <see cref="TiledCube.TileMetres"/>.</summary>
            public float Unit;

            public uint Seed;
        }

        /// <summary>How much of the old wing slab's chord stays as the arm at the leading edge, and where it sits.</summary>
        public const float ArmShare = 0.28f, ArmCentre = 0.36f;

        /// <summary>The wrist's fold in the rig (degrees): a folded outer panel lies back along the flank.</summary>
        public const float FoldYaw = 140f;

        public static Plumes PlumesFor(uint seed)
        {
            var rng = new System.Random(unchecked((int)(seed * 2246822519u) ^ 0x7f3a1));
            int pattern = rng.Next(4);
            return new Plumes
            {
                Secondaries = 4 + rng.Next(3),
                Primaries = 6 + rng.Next(4),
                Length = Next(rng, 0.85f, 1.1f),
                Sweep = Next(rng, 0.9f, 1.25f),
                Tip = rng.Next(3),
                Pattern = pattern == 0 ? 0 : pattern == 3 ? 2 : 1,
                TailShape = rng.Next(4) switch { 0 => 1, 1 => 2, _ => 0 },
                TailCount = 5 + (2 * rng.Next(3)),
                TailLength = Next(rng, 0.8f, 1.25f),
                CrestCount = 3 + rng.Next(3),
            };
        }

        /// <summary>The feathers of one wing panel, around the panel's pivot (the shoulder or the wrist): the flight
        /// feathers under the arm's rear edge and two rows of coverts shingled over their roots.</summary>
        public static Shape WingPanel(in Wing w, in Plumes look, bool outer)
        {
            var shape = new Shape();
            var rng = new System.Random(unchecked((int)(w.Seed ^ (outer ? 0x9e37u : 0x1b87u) ^ (w.Side < 0f ? 0x55u : 0xaau))));
            int count = outer ? look.Primaries : look.Secondaries;
            float uv = w.Unit / TiledCube.TileMetres;
            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0.5f : i / (float)(count - 1);
                float x = w.Span * (outer ? 0.05f + (0.95f * (i + 0.5f) / count) : 0.14f + (0.86f * (i + 0.5f) / count));
                float theta = outer ? Mathf.Lerp(0.22f, look.Sweep, t * t * (3f - (2f * t))) : Mathf.Lerp(0.05f, 0.22f, t);
                float length = w.Chord * look.Length * (outer ? Mathf.Lerp(0.66f, 0.9f, t) : Mathf.Lerp(0.66f, 0.72f, t));
                float half = Mathf.Max(w.Span / count * 0.75f, length * (outer ? 0.085f : 0.07f));
                float folded = outer ? (FoldYaw * Mathf.PI / 180f) - (0.14f * (1f - t)) : theta * 0.2f;
                var root = new Vector3(x * w.Side, -0.15f * w.Thick, 0.26f * w.Chord);
                Feather(shape, root, Back(theta, w.Side), new Vector3(0f, 1f, 0f), length, half, 0.16f * w.Side,
                    -(folded - theta) * w.Side, look, 1f, Next(rng, 0.94f, 1.04f) * (i % 2 == 0 ? 1f : 0.95f), (float)rng.NextDouble(), uv);
            }

            Coverts(shape, w, look, outer, count + 1, 0.34f, 0.30f, 0.50f, rng, uv);
            Coverts(shape, w, look, outer, count + 2, 0.21f, 0.43f, 0.62f, rng, uv);
            return shape;
        }

        /// <summary>A direction in the wing's plane: <paramref name="theta"/> radians from "straight back" toward the tip.</summary>
        private static Vector3 Back(float theta, float side) => new Vector3(Mathf.Sin(theta) * side, 0f, -Mathf.Cos(theta));

        private static void Coverts(Shape shape, in Wing w, in Plumes look, bool outer, int count, float lengthShare, float rootZ, float rootY,
            System.Random rng, float uv)
        {
            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0.5f : i / (float)(count - 1);
                float x = w.Span * (0.04f + (0.96f * (i + 0.5f) / count));
                float theta = (outer ? Mathf.Lerp(0.2f, look.Sweep, t * t) : Mathf.Lerp(0.04f, 0.2f, t)) * 0.8f;
                float folded = outer ? (FoldYaw * Mathf.PI / 180f) - 0.2f : theta * 0.2f;
                var root = new Vector3(x * w.Side, rootY * w.Thick, rootZ * w.Chord);
                Feather(shape, root, Back(theta, w.Side), new Vector3(0f, 1f, 0f), w.Chord * lengthShare * Next(rng, 0.9f, 1.1f),
                    w.Span / count * 0.72f, 0.12f * w.Side, -(folded - theta) * w.Side, look, 0.35f, Next(rng, 1.0f, 1.1f), (float)rng.NextDouble(), uv);
            }
        }

        /// <summary>The tail fan, around the point the feathers grow from: they point back (−Z), fanned to both sides.
        /// <paramref name="length"/> is the middle feather's length in units before the species' own factor.</summary>
        public static Shape TailFan(uint seed, in Plumes look, float length, float unit)
        {
            var shape = new Shape();
            var rng = new System.Random(unchecked((int)(seed ^ 0x3c6efu)));
            int count = Mathf.Max(3, look.TailCount);
            float full = length * look.TailLength;
            float uv = unit / TiledCube.TileMetres;
            for (int i = 0; i < count; i++)
            {
                float t = ((i / (float)(count - 1)) * 2f) - 1f; // −1 … 1 across the fan
                float outerShare = Mathf.Abs(t);
                float feather, half = full * 0.075f;
                switch (look.TailShape)
                {
                    case 1: feather = full * (0.68f + (0.5f * outerShare)); break;
                    case 2:
                        bool streamer = i == 0 || i == count - 1;
                        feather = full * (streamer ? 1.5f : 0.6f);
                        half *= streamer ? 0.6f : 1f;
                        break;
                    default: feather = full * (1f - (0.26f * outerShare * outerShare)); break;
                }

                float angle = t * 0.52f;
                var dir = new Vector3(Mathf.Sin(angle), 0.07f, -Mathf.Cos(angle)).normalized;
                var root = new Vector3(t * full * 0.05f, -0.004f * i, 0f);
                Feather(shape, root, dir, new Vector3(0f, 1f, 0f), feather, half, 0.2f * (t < 0f ? -1f : 1f), 0f, look, 1f,
                    Next(rng, 0.94f, 1.05f), (float)rng.NextDouble(), uv);
            }

            return shape;
        }

        /// <summary>A crest on the head: a file of feathers standing up and back along the skull's middle, their flat
        /// sides to the left and right. Around the middle of the head's top; <paramref name="depth"/> and
        /// <paramref name="height"/> are the head's, in units.</summary>
        public static Shape Crest(uint seed, in Plumes look, float depth, float height, float unit)
        {
            var shape = new Shape();
            var rng = new System.Random(unchecked((int)(seed ^ 0x85ebcu)));
            int count = Mathf.Max(2, look.CrestCount);
            float uv = unit / TiledCube.TileMetres;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                float pitch = Mathf.Lerp(1.15f, 0.5f, t); // the front one stands, the last one lies back
                float length = height * Mathf.Lerp(0.36f, 0.6f, Mathf.Sin(t * Mathf.PI)) * Next(rng, 0.9f, 1.1f);
                var root = new Vector3(0f, 0f, Mathf.Lerp(0.3f, -0.36f, t) * depth);
                var dir = new Vector3(0f, Mathf.Sin(pitch), -Mathf.Cos(pitch));
                Feather(shape, root, dir, new Vector3(1f, 0f, 0f), length, length * 0.17f, 0f, 0f, look, 1f, Next(rng, 0.95f, 1.06f),
                    (float)rng.NextDouble(), uv);
            }

            return shape;
        }

        private static readonly float[] FeatherT = { 0f, 0.22f, 0.58f, 0.70f, 0.90f, 1f };
        private static readonly float[][] FeatherWidth =
        {
            new[] { 0.32f, 0.86f, 1f, 1f, 0.74f, 0.14f },   // round
            new[] { 0.30f, 0.80f, 0.90f, 0.82f, 0.46f, 0f }, // pointed
            new[] { 0.34f, 0.90f, 1f, 1f, 1f, 0.62f },      // square
        };

        // Two rows close together (0.58 and 0.70) give the accent a clean edge instead of a long blend.
        private static readonly float[][] FeatherAccent =
        {
            new[] { 0f, 0f, 0f, 0f, 0f, 0f },   // plain
            new[] { 0f, 0f, 0f, 1f, 1f, 1f },   // the tip
            new[] { 0f, 0f, 0f, 1f, 0f, 0f },   // a band
        };

        /// <summary>One feather: six cross rows of left edge, keel and right edge from the root to the end.
        /// <paramref name="up"/> is the flat side's normal before <paramref name="roll"/> turns the slat about its own
        /// length (so neighbours shingle instead of lying in one plane); <paramref name="fold"/> is the angle the
        /// shader turns it about the object's Y when the wing folds.</summary>
        private static void Feather(Shape shape, Vector3 root, Vector3 dir, Vector3 up, float length, float half, float roll, float fold,
            in Plumes look, float accentShare, float tone, float phase, float uvScale)
        {
            var flat = Vector3.Cross(up, dir).normalized;
            var side = (flat * Mathf.Cos(roll)) + (up * Mathf.Sin(roll));
            var normal = Vector3.Cross(dir, side).normalized;
            float[] widths = FeatherWidth[Mathf.Clamp(look.Tip, 0, 2)];
            float[] accents = FeatherAccent[Mathf.Clamp(look.Pattern, 0, 2)];
            var rootData = new Vector4(root.x, root.y, root.z, fold);
            int prev = -1;
            for (int k = 0; k < FeatherT.Length; k++)
            {
                float t = FeatherT[k];
                var centre = root + (dir * (t * length)) - (up * (t * t * length * 0.05f));
                float w = half * widths[k];
                float bright = Mathf.Lerp(0.62f, 0.84f, t) * tone;
                float accent = accents[k] * accentShare;
                float bend = t * t * 0.6f;
                var coat = new Vector4(centre.z * uvScale, -centre.x * uvScale, phase, 0f);
                int l = shape.Add(centre - (side * w), (normal - (side * 0.3f)).normalized, new Color(bright, accent, 0f, bend), coat, rootData);
                shape.Add(centre + (normal * (half * 0.16f * Mathf.Sin(t * 2.8f))), normal,
                    new Color(bright, accent, Mathf.Lerp(0.5f, 0.2f, t), bend), coat, rootData);
                shape.Add(centre + (side * w), (normal + (side * 0.3f)).normalized, new Color(bright, accent, 0f, bend), coat, rootData);
                if (prev >= 0)
                {
                    shape.Tri(prev, prev + 1, l);
                    shape.Tri(prev + 1, l + 1, l);
                    shape.Tri(prev + 1, prev + 2, l + 1);
                    shape.Tri(prev + 2, l + 2, l + 1);
                }

                prev = l;
            }

            shape.Blades++;
            shape.Reach = Mathf.Max(shape.Reach, length * 0.6f);
        }
    }
}
