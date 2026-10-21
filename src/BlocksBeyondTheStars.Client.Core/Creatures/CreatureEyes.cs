// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The look of a species' eyes (#2866).</summary>
    public enum EyeLook
    {
        Round,    // a round iris with a round pupil (the classic eye)
        Button,   // a big black pupil with two highlights — the cute one
        Slit,     // a cat's vertical slit
        Bar,      // a goat's horizontal bar
        Compound, // an insect's faceted eye, dark all over
        Ringed,   // an iris with a light ring
        Double,   // two pupils side by side
        Star,     // a four-pointed star pupil
        Oval,     // a tall oval iris and pupil
        Gleam,    // a big pupil with a big highlight
    }

    /// <summary>
    /// The eyes of a creature (#2866): about ten looks, drawn in code as a small pixel-art texture per species — a sclera
    /// background, the iris in the species' eye colour, the pupil and a highlight. The texture is mapped onto the front of
    /// the eye sphere (<see cref="SphereUv"/>), so the eye keeps its round shape; the separate pupil and glint spheres are
    /// gone. A glowing species' iris (and a coloured iris from <c>EyeRgb</c>, the contaminated animals' green) shines: its
    /// texels carry alpha 0, which the creature shader reads as glow.
    /// </summary>
    public static class CreatureEyes
    {
        /// <summary>The edge of an eye texture in texels.</summary>
        public const int Size = 32;

        /// <summary>A natural iris palette for species without an <c>EyeRgb</c>.</summary>
        public static readonly IReadOnlyList<int> IrisPalette = new[]
        {
            0xC8862A, // amber
            0x7A4A22, // brown
            0x4E9A3A, // green
            0x3A78C8, // blue
            0xE0B030, // gold
            0xC03A30, // red
            0x8A4AC8, // violet
            0x8A9098, // grey
            0x2FA39A, // teal
        };

        /// <summary>The sclera (the eye white).</summary>
        public const int ScleraRgb = 0xF5F2E3;

        /// <summary>The sclera of a glowing species — a cool, pale shine.</summary>
        public const int GlowScleraRgb = 0xDDF7EE;

        /// <summary>The pupil.</summary>
        public const int PupilRgb = 0x0C0C10;

        /// <summary>The eye look of a species: the classic round eye for an authored species, a button eye for the
        /// flowerling, otherwise a pick by the seed weighted by the body — compound eyes on many legs and arachnids, slits
        /// on hunters, bars and round eyes on grazers.</summary>
        public static EyeLook LookFor(NetCreature c)
        {
            if (CreatureCoat.IsAuthored(c.SpeciesId))
            {
                return c.BodyPlan == "Floral" ? EyeLook.Button : EyeLook.Round;
            }

            if (c.BodyPlan == "Floral")
            {
                return EyeLook.Button;
            }

            var bag = new List<EyeLook>();
            void Add(EyeLook look, int weight)
            {
                for (int i = 0; i < weight; i++)
                {
                    bag.Add(look);
                }
            }

            if (c.BodyPlan == "Arachnid" || c.Legs >= 6)
            {
                Add(EyeLook.Compound, 4);
                Add(EyeLook.Round, 1);
                Add(EyeLook.Ringed, 1);
                Add(EyeLook.Button, 1);
            }
            else if (c.BodyPlan == "Biped")
            {
                Add(EyeLook.Round, 2);
                Add(EyeLook.Button, 2);
                Add(EyeLook.Gleam, 2);
                Add(EyeLook.Oval, 1);
                Add(EyeLook.Ringed, 1);
            }
            else if (CreatureHides.SpeciesHostile(c.Temperament))
            {
                Add(EyeLook.Slit, 4);
                Add(EyeLook.Round, 1);
                Add(EyeLook.Ringed, 1);
                Add(EyeLook.Star, 1);
                Add(EyeLook.Oval, 1);
                Add(EyeLook.Double, 1);
            }
            else
            {
                Add(EyeLook.Bar, 2);
                Add(EyeLook.Round, 2);
                Add(EyeLook.Button, 2);
                Add(EyeLook.Gleam, 2);
                Add(EyeLook.Ringed, 1);
                Add(EyeLook.Oval, 1);
                Add(EyeLook.Double, 1);
                Add(EyeLook.Star, 1);
                Add(EyeLook.Slit, 1);
            }

            uint seed = CreatureCoat.SpeciesSeed(c.SpeciesId, c.VoiceSeed);
            return bag[(int)(CoatMix.Mix(seed, CoatMix.SaltEye) % (uint)bag.Count)];
        }

        /// <summary>The iris colour: the species' <c>EyeRgb</c> when it has one, else a pick from <see cref="IrisPalette"/>.
        /// </summary>
        public static int IrisRgb(NetCreature c)
        {
            if (c.EyeRgb != 0)
            {
                return c.EyeRgb & 0xFFFFFF;
            }

            uint seed = CreatureCoat.SpeciesSeed(c.SpeciesId, c.VoiceSeed);
            return IrisPalette[(int)(CoatMix.Mix(seed, CoatMix.SaltIris) % (uint)IrisPalette.Count)];
        }

        /// <summary>True when the iris shines (a glowing species, or a coloured iris from <c>EyeRgb</c> — the eyes that used
        /// to be drawn self-lit in their colour).</summary>
        public static bool IrisGlows(NetCreature c) => c.Glows || c.EyeRgb != 0;

        /// <summary>The colour of the eye sphere around the texture: the sclera, or the dark iris tone of a compound eye.
        /// </summary>
        public static int SphereRgb(EyeLook look, int irisRgb, bool glows)
        {
            if (look == EyeLook.Compound)
            {
                return Shade(irisRgb, 0.35f);
            }

            return glows ? GlowScleraRgb : ScleraRgb;
        }

        /// <summary>Draws the eye texture (<see cref="Size"/>² RGBA32, rows bottom-up as <c>LoadRawTextureData</c> reads
        /// them). Opaque except the shining iris of a glowing eye (alpha 0).</summary>
        public static byte[] Draw(EyeLook look, int irisRgb, bool glows)
        {
            var px = new byte[Size * Size * 4];
            int sclera = SphereRgb(look, irisRgb, glows);
            int irisDark = Shade(irisRgb, 0.62f);
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float dx = x + 0.5f - Size / 2f, dy = y + 0.5f - Size / 2f;
                    var (rgb, iris) = Texel(look, dx, dy, irisRgb, irisDark, sclera);
                    int p = (y * Size + x) * 4;
                    px[p] = (byte)((rgb >> 16) & 0xFF);
                    px[p + 1] = (byte)((rgb >> 8) & 0xFF);
                    px[p + 2] = (byte)(rgb & 0xFF);
                    px[p + 3] = (byte)(iris && glows ? 0 : 255);
                }
            }

            return px;
        }

        /// <summary>One texel: its colour, and whether it is iris (the part that shines on a glowing eye).</summary>
        private static (int Rgb, bool Iris) Texel(EyeLook look, float dx, float dy, int iris, int irisDark, int sclera)
        {
            const int White = 0xFFFFFF;
            float r = (float)Math.Sqrt(dx * dx + dy * dy);
            switch (look)
            {
                case EyeLook.Compound:
                    {
                        // Facets: a hex-ish grid of cells with light rims, a soft highlight top-left.
                        if (r > 15f)
                        {
                            return (sclera, false);
                        }

                        float row = (float)Math.Floor((dy + 16f) / 4f);
                        float col = (dx + 16f + (row % 2f) * 2f) / 4f;
                        float fx = col - (float)Math.Floor(col), fy = (dy + 16f) / 4f - row;
                        bool rim = fx < 0.25f || fy < 0.25f;
                        if (Highlight(dx, dy, -5f, 5f, 2.5f))
                        {
                            return (Shade(iris, 1.4f), false);
                        }

                        return (rim ? Shade(iris, 0.75f) : Shade(iris, 0.42f), !rim);
                    }

                case EyeLook.Button:
                    if (Highlight(dx, dy, -4f, 4f, 2.2f) || Highlight(dx, dy, 3.5f, -3.5f, 1.1f))
                    {
                        return (White, false);
                    }

                    if (r < 10f)
                    {
                        return (PupilRgb, false);
                    }

                    return r < 12.5f ? (iris, true) : (sclera, false);

                case EyeLook.Gleam:
                    if (Highlight(dx, dy, -4f, 4.5f, 2.8f) || Highlight(dx, dy, 4f, -3f, 1.2f))
                    {
                        return (White, false);
                    }

                    if (r < 7.5f)
                    {
                        return (PupilRgb, false);
                    }

                    return r < 13f ? (r > 11.5f ? irisDark : iris, true) : (sclera, false);

                case EyeLook.Oval:
                    {
                        float e = (dx * dx) / (8f * 8f) + (dy * dy) / (13f * 13f);
                        float pe = (dx * dx) / (3.5f * 3.5f) + (dy * dy) / (7f * 7f);
                        if (Highlight(dx, dy, -3f, 6f, 1.6f))
                        {
                            return (White, false);
                        }

                        if (pe < 1f)
                        {
                            return (PupilRgb, false);
                        }

                        return e < 1f ? (e > 0.75f ? irisDark : iris, true) : (sclera, false);
                    }

                default:
                    break;
            }

            // Round-iris looks: the iris disc with a darker rim, a pupil of the look's shape, a highlight top-left.
            if (r >= 12.5f)
            {
                return (sclera, false);
            }

            if (Highlight(dx, dy, -4f, 4f, 1.7f))
            {
                return (White, false);
            }

            bool pupil = look switch
            {
                EyeLook.Slit => Math.Abs(dx) < 1.8f * (1f - (dy * dy) / (11f * 11f)) && Math.Abs(dy) < 11f,
                EyeLook.Bar => Math.Abs(dy) < 2.2f && Math.Abs(dx) < 8.5f - Math.Max(0f, Math.Abs(dy) - 1f) * 2f,
                EyeLook.Double => (dx - 4.2f) * (dx - 4.2f) + dy * dy < 12.5f || (dx + 4.2f) * (dx + 4.2f) + dy * dy < 12.5f,
                EyeLook.Star => StarPupil(dx, dy),
                EyeLook.Ringed => r < 4f,
                _ => r < 5.2f,
            };
            if (pupil)
            {
                return (PupilRgb, false);
            }

            if (look == EyeLook.Ringed && r > 7.5f && r < 9f)
            {
                return (Shade(iris, 1.45f), true);
            }

            return (r > 10.8f ? irisDark : iris, true);
        }

        private static bool StarPupil(float dx, float dy)
        {
            float ax = Math.Abs(dx), ay = Math.Abs(dy);
            return ax + ay * 3.2f < 7f || ay + ax * 3.2f < 7f || ax + ay < 3.2f;
        }

        private static bool Highlight(float dx, float dy, float cx, float cy, float radius)
            => (dx - cx) * (dx - cx) + (dy - cy) * (dy - cy) < radius * radius;

        /// <summary>A colour scaled by <paramref name="k"/> (darker below 1, lighter above), clamped.</summary>
        public static int Shade(int rgb, float k)
        {
            CreatureCoat.Unpack(rgb, out float r, out float g, out float b);
            if (k <= 1f)
            {
                return CreatureCoat.Pack(r * k, g * k, b * k);
            }

            float t = k - 1f;
            return CreatureCoat.Pack(r + (1f - r) * t, g + (1f - g) * t, b + (1f - b) * t);
        }

        /// <summary>The eye sphere's mesh (<see cref="BuildSphere"/>): plain arrays, Unity-free.</summary>
        public sealed class SphereData
        {
            public SphereData(float[] positions, float[] normals, float[] uvs, int[] triangles)
            {
                Positions = positions;
                Normals = normals;
                Uvs = uvs;
                Triangles = triangles;
            }

            /// <summary>x, y, z per vertex, on a sphere of diameter 1.</summary>
            public float[] Positions { get; }

            /// <summary>x, y, z per vertex (outward).</summary>
            public float[] Normals { get; }

            /// <summary>u, v per vertex.</summary>
            public float[] Uvs { get; }

            /// <summary>Vertex indices, three per triangle, wound outward the way Unity culls ((b − a) × (c − a) points out).
            /// </summary>
            public int[] Triangles { get; }
        }

        /// <summary>The eye sphere (diameter 1, the eye looking along +Z): latitude rings round the Z axis, so the front
        /// half's projected texture coordinates (<see cref="SphereUv"/>) never meet the back half's in one triangle — the
        /// equator ring is doubled, its front copy on the texture's rim, its back copy on the sclera corner like every back
        /// vertex. (Unity's sphere has its poles on Y: its triangles straddle z = 0 and smeared the iris round the side.)
        /// <paramref name="rings"/> must be even.</summary>
        public static SphereData BuildSphere(int rings = 8, int segments = 16)
        {
            int half = rings / 2;
            var pos = new List<float>();
            var nrm = new List<float>();
            var uv = new List<float>();
            var tri = new List<int>();

            int Add(float x, float y, float z, bool back)
            {
                pos.Add(x); pos.Add(y); pos.Add(z);
                float len = (float)Math.Sqrt(x * x + y * y + z * z);
                nrm.Add(x / len); nrm.Add(y / len); nrm.Add(z / len);
                float u, v;
                if (back)
                {
                    u = v = 0.03f;
                }
                else
                {
                    SphereUv(x, y, Math.Max(z, 1e-4f), out u, out v);
                }

                uv.Add(u); uv.Add(v);
                return pos.Count / 3 - 1;
            }

            // Ring k (1..rings-1) at polar angle θ = π·k/rings from +Z; the front half owns rings 1..half, the back half its
            // own copy of ring half and rings half+1..rings-1.
            int[] Ring(int k, bool back)
            {
                double theta = Math.PI * k / rings;
                var ring = new int[segments];
                for (int j = 0; j < segments; j++)
                {
                    double phi = 2.0 * Math.PI * j / segments;
                    float r = (float)(0.5 * Math.Sin(theta));
                    ring[j] = Add(r * (float)Math.Cos(phi), r * (float)Math.Sin(phi), (float)(0.5 * Math.Cos(theta)), back);
                }

                return ring;
            }

            // (b − a) × (c − a) must point out. Seen from outside a band between ring A (nearer +Z) and ring B, the order
            // A[j] → B[j] → B[j+1] runs that way round (φ grows counter-clockwise about +Z).
            void Band(int[] a, int[] b)
            {
                for (int j = 0; j < segments; j++)
                {
                    int j1 = (j + 1) % segments;
                    tri.Add(a[j]); tri.Add(b[j]); tri.Add(b[j1]);
                    tri.Add(a[j]); tri.Add(b[j1]); tri.Add(a[j1]);
                }
            }

            int front = Add(0f, 0f, 0.5f, false);
            var prev = Ring(1, false);
            for (int j = 0; j < segments; j++)
            {
                tri.Add(front); tri.Add(prev[j]); tri.Add(prev[(j + 1) % segments]);
            }

            for (int k = 2; k <= half; k++)
            {
                var next = Ring(k, false);
                Band(prev, next);
                prev = next;
            }

            prev = Ring(half, true);
            for (int k = half + 1; k < rings; k++)
            {
                var next = Ring(k, true);
                Band(prev, next);
                prev = next;
            }

            int backPole = Add(0f, 0f, -0.5f, true);
            for (int j = 0; j < segments; j++)
            {
                tri.Add(backPole); tri.Add(prev[(j + 1) % segments]); tri.Add(prev[j]);
            }

            return new SphereData(pos.ToArray(), nrm.ToArray(), uv.ToArray(), tri.ToArray());
        }

        /// <summary>The texture coordinate of an eye-sphere vertex (a unit-diameter sphere, +Z out of the face): the front
        /// half is projected straight onto the texture, so the iris sits on the sphere's front; the back half takes a
        /// sclera texel near the corner.</summary>
        public static void SphereUv(float x, float y, float z, out float u, out float v)
        {
            if (z <= 0f)
            {
                u = 0.03f;
                v = 0.03f;
                return;
            }

            u = Math.Max(0f, Math.Min(1f, x + 0.5f));
            v = Math.Max(0f, Math.Min(1f, y + 0.5f));
        }
    }
}
