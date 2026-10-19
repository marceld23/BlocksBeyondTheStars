// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Numerics;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #2836: the surface of a held model's cubes. Every cube part is made of a material (<see cref="HeldMaterials"/>) whose
    /// light tile the client multiplies over the part's colour, so a wooden handle shows its grain and a metal body its
    /// brushing instead of one flat colour. A part names its material in the data (<c>"m"</c>); without one it is guessed
    /// from the colour (<see cref="Infer"/>), so every model — the item data, the kinds' fallbacks, the NPC tools and a
    /// player's own tool look (#1963) — has one. The tiles repeat in METRES (<see cref="TileMetres"/>): a long blade shows
    /// the pattern several times instead of one stretched tile. Unity-free so the headless suite pins it; <c>HeldTextures</c>
    /// builds the meshes and materials.
    /// </summary>
    public static class HeldMaterialRules
    {
        /// <summary>How many metres one tile covers on a held part (64 texels over 20 cm).</summary>
        public const float TileMetres = 0.2f;

        /// <summary>The steps a tile is split into for the UV buckets: a part's face shows a whole number of eighths of
        /// a tile, so parts of nearly the same size share one mesh.</summary>
        public const int UvSteps = 8;

        /// <summary>Most steps along one axis: a 2 m part (<see cref="HeldModelPart.IsValid"/>'s limit) is 10 tiles of
        /// <see cref="UvSteps"/> steps.</summary>
        public const int MaxUvSteps = 80;

        /// <summary>The mean brightness a tile is lifted to before it is multiplied over a colour (<see cref="LiftTile"/>).</summary>
        public const float TargetMean = 0.88f;

        /// <summary>The declared material when the client knows it, otherwise the guess from the colour and the glow.</summary>
        public static string Resolve(string? declared, HeldItemShapes.Rgb color, bool glow)
            => HeldMaterials.IsKnown(declared) ? declared! : Infer(color, glow);

        /// <summary>
        /// The material a part of this colour is most likely made of — the rule for a part without an <c>"m"</c>:
        /// <list type="number">
        /// <item>it glows → <c>energy</c>;</item>
        /// <item>a brown (hue 15–50°, saturation ≥ 0.3, value ≤ 0.75): saturation over 0.8 → <c>metal</c> (copper,
        /// bronze), value under 0.32 → <c>leather</c>, otherwise <c>wood</c>;</item>
        /// <item>very dark (luminance under 0.18) → <c>rubber</c>;</item>
        /// <item>a grey (chroma — max − min channel — under 0.25, which keeps the blue-tinted steel greys): luminance under
        /// 0.40 → <c>darkmetal</c>, otherwise <c>metal</c>;</item>
        /// <item>any other, saturated colour → <c>plastic</c>.</item>
        /// </list>
        /// Glass, crystal, stone, bone and carbon are never guessed: a model names them. Luminance is
        /// 0.2126 R + 0.7152 G + 0.0722 B on the colour's 0..1 channels.
        /// </summary>
        public static string Infer(HeldItemShapes.Rgb color, bool glow)
        {
            if (glow)
            {
                return HeldMaterials.Energy;
            }

            float r = Clamp01(color.R), g = Clamp01(color.G), b = Clamp01(color.B);
            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            float chroma = max - min;
            float saturation = max > 0f ? chroma / max : 0f;
            float hue = Hue(r, g, b, max, chroma);
            if (chroma > 0f && hue >= 15f && hue <= 50f && saturation >= 0.3f && max <= 0.75f)
            {
                if (saturation > 0.8f)
                {
                    return HeldMaterials.Metal;
                }

                return max < 0.32f ? HeldMaterials.Leather : HeldMaterials.Wood;
            }

            float luminance = (0.2126f * r) + (0.7152f * g) + (0.0722f * b);
            if (luminance < 0.18f)
            {
                return HeldMaterials.Rubber;
            }

            if (chroma < 0.25f)
            {
                return luminance < 0.40f ? HeldMaterials.DarkMetal : HeldMaterials.Metal;
            }

            return HeldMaterials.Plastic;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        // The HSV hue in degrees (0..360); 0 for a grey.
        private static float Hue(float r, float g, float b, float max, float chroma)
        {
            if (chroma <= 0f)
            {
                return 0f;
            }

            float h = max == r ? ((g - b) / chroma) % 6f
                : max == g ? ((b - r) / chroma) + 2f
                : ((r - g) / chroma) + 4f;
            h *= 60f;
            return h < 0f ? h + 360f : h;
        }

        /// <summary>The UV bucket of a part <paramref name="size"/> metres big: per axis, how many
        /// 1/<see cref="UvSteps"/> of a tile its faces span along that axis (at least one step).</summary>
        public static (int X, int Y, int Z) UvBucket(Vector3 size) => (Steps(size.X), Steps(size.Y), Steps(size.Z));

        private static int Steps(float metres)
        {
            if (float.IsNaN(metres) || metres <= 0f)
            {
                return 1;
            }

            int steps = (int)Math.Round(metres / TileMetres * UvSteps, MidpointRounding.AwayFromZero);
            return steps < 1 ? 1 : steps > MaxUvSteps ? MaxUvSteps : steps;
        }

        /// <summary>A unit cube (the extent of Unity's primitive: −0.5..0.5) whose faces carry UVs in tiles.</summary>
        public sealed class CubeMesh
        {
            internal CubeMesh(Vector3[] positions, Vector3[] normals, Vector2[] uvs, int[] triangles)
            {
                Positions = positions;
                Normals = normals;
                Uvs = uvs;
                Triangles = triangles;
            }

            public Vector3[] Positions { get; }

            public Vector3[] Normals { get; }

            /// <summary>In tiles: a face spans 0..(its steps / <see cref="UvSteps"/>) along each of its two axes.</summary>
            public Vector2[] Uvs { get; }

            /// <summary>Two triangles per face, wound as Unity's front faces: cross(b − a, c − a) along the outward normal.</summary>
            public int[] Triangles { get; }
        }

        // Per face: the outward normal, then the two face axes t and b with t × b = normal; u runs along t, v along b.
        private static readonly Vector3[][] Faces =
        {
            new[] { Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY },
            new[] { -Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY },
            new[] { Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ },
            new[] { -Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ },
            new[] { Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY },
            new[] { -Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY },
        };

        /// <summary>
        /// The cube for a UV bucket (<see cref="UvBucket"/>): 24 vertices, each face showing as much of the tile as the part
        /// is long along that face's axes. The cube itself stays a unit cube — the part's transform scales it to its size, as
        /// it scaled the primitive — so the hand's measuring (<c>HeldItem.HeldBounds</c>) and the muzzle are unchanged.
        /// </summary>
        public static CubeMesh BuildCube(int stepsX, int stepsY, int stepsZ)
        {
            float[] extent = { Math.Max(1, stepsX) / (float)UvSteps, Math.Max(1, stepsY) / (float)UvSteps, Math.Max(1, stepsZ) / (float)UvSteps };
            var positions = new Vector3[24];
            var normals = new Vector3[24];
            var uvs = new Vector2[24];
            var triangles = new int[36];
            for (int f = 0; f < Faces.Length; f++)
            {
                Vector3 n = Faces[f][0], t = Faces[f][1], b = Faces[f][2];
                float u = extent[AxisOf(t)], v = extent[AxisOf(b)];
                int i = f * 4;
                positions[i] = (n - t - b) * 0.5f;
                positions[i + 1] = (n + t - b) * 0.5f;
                positions[i + 2] = (n + t + b) * 0.5f;
                positions[i + 3] = (n - t + b) * 0.5f;
                uvs[i] = new Vector2(0f, 0f);
                uvs[i + 1] = new Vector2(u, 0f);
                uvs[i + 2] = new Vector2(u, v);
                uvs[i + 3] = new Vector2(0f, v);
                for (int k = 0; k < 4; k++)
                {
                    normals[i + k] = n;
                }

                int tri = f * 6;
                triangles[tri] = i;
                triangles[tri + 1] = i + 1;
                triangles[tri + 2] = i + 2;
                triangles[tri + 3] = i;
                triangles[tri + 4] = i + 2;
                triangles[tri + 5] = i + 3;
            }

            return new CubeMesh(positions, normals, uvs, triangles);
        }

        private static int AxisOf(Vector3 axis) => axis.X != 0f ? 0 : axis.Y != 0f ? 1 : 2;

        /// <summary>
        /// A material tile (raw RGBA32) prepared to be multiplied over a part's colour: its brightness is pulled toward white
        /// until its mean is <see cref="TargetMean"/> — v′ = 1 − (1 − v)·d — so the pattern stays (only compressed) and the
        /// part does not come out much darker than its flat colour did. A tile already that bright is left as it is; alpha
        /// is kept. Always a new array (the source is shared with the texture layers). Null in, null out.
        /// </summary>
        public static byte[]? LiftTile(byte[]? raw)
        {
            if (raw == null)
            {
                return null;
            }

            var lifted = (byte[])raw.Clone();
            int pixels = raw.Length / 4;
            if (pixels == 0)
            {
                return lifted;
            }

            double sum = 0;
            for (int p = 0; p < pixels * 4; p += 4)
            {
                sum += ((0.2126 * raw[p]) + (0.7152 * raw[p + 1]) + (0.0722 * raw[p + 2])) / 255.0;
            }

            double mean = sum / pixels;
            if (mean >= TargetMean)
            {
                return lifted;
            }

            double d = (1.0 - TargetMean) / (1.0 - mean);
            for (int p = 0; p < pixels * 4; p += 4)
            {
                for (int c = 0; c < 3; c++)
                {
                    double v = raw[p + c] / 255.0;
                    lifted[p + c] = (byte)Math.Round((1.0 - ((1.0 - v) * d)) * 255.0);
                }
            }

            return lifted;
        }
    }
}
