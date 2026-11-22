// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The forms of the SOLID plants (<see cref="PlantForms"/>): cactus, mushrooms, puffballs and bulbs,
    /// crystals, the succulent, the pitcher plant, the pumpkin and the ember bloom. These keep their collider — the
    /// plain cylinder, cone, dome or ball the chunk mesh always built for them — so a body here is built to FILL its
    /// cell (about 0.46 across from its axis, up to 1 high) and the instance is scaled like that collider.
    /// </summary>
    public static partial class PlantForms
    {
        public const int Cactus = 10;
        public const int Mushroom = 11;
        public const int Puff = 12;      // puffball, spore pod, bulb, gas bloom: round bodies on the ground or a short stalk
        public const int Crystal = 13;   // cut prisms: a cluster, or a bloom of them
        public const int Succulent = 14; // a rosette of fat leaves
        public const int Pitcher = 15;   // upright pitchers with a lid
        public const int Pumpkin = 16;
        public const int Bloom = 17;     // a large cup on a thick stem (the ember bloom)

        private static int SolidFamilyOf(string key)
        {
            switch (key)
            {
                case "flora_cactus": return Cactus;
                case "flora_mushroom":
                case "flora_glowcap":
                case "flora_cavecap":
                case "flora_cropshroom":
                    return Mushroom;
                case "flora_puffball":
                case "flora_sporepod":
                case "flora_bulb":
                case "flora_gasbloom":
                    return Puff;
                case "flora_crystal":
                case "flora_shardbloom":
                case "flora_prismbloom":
                    return Crystal;
                case "flora_succulent": return Succulent;
                case "flora_pitcher": return Pitcher;
                case "flora_pumpkin": return Pumpkin;
                case "flora_emberbloom": return Bloom;
                default: return -1;
            }
        }

        /// <summary>Builds one of the solid families; returns how much the wind bows it (0 = it stands still).</summary>
        private static float BuildSolid(Shape s, int family, string key, System.Random species, System.Random own, bool accent)
        {
            switch (family)
            {
                case Cactus:
                    BuildCactus(s, species, own, accent);
                    return 0.05f;
                case Mushroom:
                    BuildMushrooms(s, species, own, key == "flora_cavecap", accent);
                    return 0.25f;
                case Puff:
                    BuildPuff(s, species, own, key);
                    return 0.15f;
                case Crystal:
                    BuildCrystals(s, species, own, key != "flora_crystal");
                    return 0f;
                case Succulent:
                    BuildSucculent(s, species, own);
                    return 0.1f;
                case Pitcher:
                    BuildPitcher(s, species, own);
                    return 0.4f;
                case Pumpkin:
                    BuildPumpkin(s, species, own);
                    return 0f;
                default:
                    BuildBloom(s, species, own);
                    return 0.5f;
            }
        }

        /// <summary>A cactus: a thick round column with a domed top, up to two arms that turn upward, and on an accent
        /// species a few round blossoms on the tops.</summary>
        private static void BuildCactus(Shape s, System.Random species, System.Random own, bool accent)
        {
            float radius = Range(species, 0.13f, 0.2f);
            float height = Range(species, 0.72f, 0.95f);
            int arms = species.Next(3);
            float phase = Range(own, 0f, 1f);
            var top = new Vector3(0f, height - radius, 0f);
            AddTube(s, new[] { Vector3.zero, top * 0.35f, top * 0.7f, top }, new[] { radius * 0.9f, radius, radius, radius * 0.94f }, 8,
                0.55f, 0.9f, phase, dark: 0f, secondTip: 0.35f);
            AddBall(s, top, radius * 0.94f, 8, 5, 0.92f, 0.35f, 0f, phase, 0f);
            if (accent)
            {
                AddBall(s, top + new Vector3(Range(own, -0.04f, 0.04f), radius * 0.9f, Range(own, -0.04f, 0.04f)), 0.04f, 6, 3, 0.95f, 0f, 1f, phase, 0f);
            }

            float turn = Range(own, 0f, 6.2832f);
            for (int i = 0; i < arms; i++)
            {
                float phi = turn + (i * 3.1416f) + Range(own, -0.5f, 0.5f);
                var dir = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
                float armRadius = radius * 0.6f;
                var from = new Vector3(0f, height * Range(own, 0.3f, 0.5f), 0f) + (dir * radius * 0.6f);
                var elbow = from + (dir * Mathf.Min(0.16f, 0.44f - radius - armRadius));
                var end = elbow + new Vector3(0f, height * Range(own, 0.2f, 0.32f), 0f);
                AddTube(s, new[] { from, Vector3.Lerp(from, elbow, 0.8f) + new Vector3(0f, 0.02f, 0f), elbow + new Vector3(0f, 0.07f, 0f), end },
                    new[] { armRadius, armRadius, armRadius, armRadius * 0.92f }, 7, 0.6f, 0.9f, phase, dark: 0f, secondTip: 0.35f);
                AddBall(s, end, armRadius * 0.92f, 7, 4, 0.92f, 0.35f, 0f, phase, 0f);
                if (accent && own.NextDouble() < 0.6)
                {
                    AddBall(s, end + new Vector3(0f, armRadius * 0.9f, 0f), 0.035f, 6, 3, 0.95f, 0f, 1f, phase, 0f);
                }
            }
        }

        /// <summary>Mushrooms: one large and up to two small ones, each a pale stem under a cap with a lighter underside —
        /// flat, round or (a cave cap) pointed, and spotted in the accent colour where the species has one.</summary>
        private static void BuildMushrooms(Shape s, System.Random species, System.Random own, bool pointed, bool accent)
        {
            int count = 1 + species.Next(3);
            float height = Range(species, 0.55f, 0.9f);
            float capRadius = pointed ? Range(species, 0.16f, 0.26f) : Range(species, 0.24f, 0.4f);
            float rise = capRadius * (pointed ? Range(species, 1.1f, 1.6f) : Range(species, 0.4f, 0.9f));
            float stem = Range(species, 0.05f, 0.085f);

            float turn = Range(own, 0f, 6.2832f);
            for (int i = 0; i < count; i++)
            {
                float scale = i == 0 ? 1f : Range(own, 0.42f, 0.66f);
                float phi = turn + (i * 2.6f);
                float away = i == 0 ? (count > 1 ? 0.06f : 0f) : Mathf.Min(Range(own, 0.2f, 0.3f), 0.45f - (capRadius * scale));
                var foot = new Vector3(Mathf.Cos(phi) * away, 0f, Mathf.Sin(phi) * away) * (i == 0 ? -1f : 1f);
                float capBase = Mathf.Min(height * scale, 0.97f - (rise * scale)) - (rise * scale * 0.25f);
                var under = foot + new Vector3(Range(own, -0.03f, 0.03f), Mathf.Max(capBase, 0.08f), Range(own, -0.03f, 0.03f));
                float phase = Range(own, 0f, 1f);
                AddTube(s, new[] { foot, Vector3.Lerp(foot, under, 0.5f) + new Vector3(0.015f, 0f, 0.01f), under },
                    new[] { stem * scale * 1.15f, stem * scale, stem * scale * 0.9f }, 6, 0.7f, 0.92f, phase, dark: 0f, secondRoot: 0.85f, secondTip: 0.85f);
                float r = capRadius * scale;
                AddDome(s, under, r, rise * scale, 9, 4, 0.93f, 0f, 0f, phase);
                AddDisc(s, under, r, 9, 0.5f, 0.6f, phase);
                for (int spot = 0; accent && spot < 4; spot++)
                {
                    float around = Range(own, 0f, 6.2832f), up = Range(own, 0.25f, 0.85f);
                    float flat = Mathf.Cos(up * 1.5708f);
                    var on = under + new Vector3(flat * Mathf.Cos(around) * r, Mathf.Sin(up * 1.5708f) * rise * scale, flat * Mathf.Sin(around) * r);
                    AddBall(s, on, 0.03f * Mathf.Sqrt(scale), 5, 3, 0.95f, 0f, 1f, phase, 0f, 0.6f);
                }
            }
        }

        /// <summary>The round ones: a puffball is a few balls on the ground, a spore pod a long pod on a stalk with
        /// spores around its top, a bulb one large ball in a collar of leaves, a gas bloom a ball on a stem over a ring
        /// of petals.</summary>
        private static void BuildPuff(Shape s, System.Random species, System.Random own, string key)
        {
            float phase = Range(own, 0f, 1f);
            if (key == "flora_puffball")
            {
                BuildPuffballs(s, species, own, phase);
                return;
            }

            if (key == "flora_sporepod")
            {
                BuildSporePod(s, species, own, phase);
                return;
            }

            bool bulb = key == "flora_bulb";
            float radius = bulb ? Range(species, 0.26f, 0.34f) : Range(species, 0.2f, 0.28f);
            float lift = bulb ? radius * 0.8f : Range(species, 0.3f, 0.45f) + radius;
            var middle = new Vector3(0f, Mathf.Min(lift, 0.96f - radius), 0f);
            if (!bulb)
            {
                AddTube(s, new[] { Vector3.zero, middle * 0.5f + new Vector3(0.015f, 0f, 0.01f), middle }, new[] { 0.055f, 0.045f, 0.05f }, 6, 0.5f, 0.72f, phase);
            }

            AddBall(s, middle, radius, 9, 6, 0.9f, 0f, 0f, phase, 0f, bulb ? 0.9f : 1f);
            AddBall(s, middle + new Vector3(0f, radius * (bulb ? 0.85f : 0.95f), 0f), radius * 0.28f, 6, 4, 0.98f, 1f, 0f, phase, 0f);
            int leaves = 5 + own.Next(3);
            var collar = Frame.Upright(bulb ? Vector3.zero : middle - new Vector3(0f, radius * 0.7f, 0f));
            for (int i = 0; i < leaves; i++)
            {
                float len = radius * Range(own, 0.9f, 1.25f);
                AddLeaf(s, collar, new Leaf
                {
                    Phi = (i * 6.2832f / leaves) + Range(own, -0.2f, 0.2f), Length = len, HalfWidth = len * 0.28f,
                    Theta = bulb ? Range(own, 0.7f, 1.1f) : Range(own, 1.3f, 1.7f), Droop = bulb ? 0.5f : 0.3f, Fold = 0.25f,
                    Second = bulb ? 0f : 0.7f, AccentAll = bulb ? 0f : 1f, Phase = Frac(phase + (0.1f * i)), Tone = 1f,
                    RootOffset = bulb ? radius * 0.55f : radius * 0.5f, MaxReach = 0.46f, OnGround = bulb,
                });
            }
        }

        /// <summary>A puffball: a few balls on the ground, the first the largest in the middle, the others around it.</summary>
        private static void BuildPuffballs(Shape s, System.Random species, System.Random own, float phase)
        {
            int balls = 2 + species.Next(4);
            float big = Range(species, 0.17f, 0.24f);
            float turn = Range(own, 0f, 6.2832f);
            for (int i = 0; i < balls; i++)
            {
                float r = i == 0 ? big : big * Range(own, 0.4f, 0.7f);
                float away = i == 0 ? 0f : Mathf.Min(big + (r * 0.5f), 0.45f - r);
                float phi = turn + (i * 6.2832f / Mathf.Max(1, balls - 1));
                AddBall(s, new Vector3(Mathf.Cos(phi) * away, r * 0.75f, Mathf.Sin(phi) * away), r, 8, 5, Range(own, 0.82f, 0.96f),
                    own.NextDouble() < 0.4 ? 0.5f : 0f, 0f, phase, 0f, 0.85f);
            }
        }

        /// <summary>A spore pod: a long pod on a short stalk with small glowing spores around its top.</summary>
        private static void BuildSporePod(Shape s, System.Random species, System.Random own, float phase)
        {
            float r = Range(species, 0.14f, 0.19f), stretch = Range(species, 1.5f, 2f);
            float stalk = Range(species, 0.12f, 0.22f);
            AddTube(s, new[] { Vector3.zero, new Vector3(0.01f, stalk * 0.5f, 0f), new Vector3(0f, stalk, 0f) }, new[] { 0.06f, 0.05f, 0.055f }, 6,
                0.5f, 0.7f, phase);
            var centre = new Vector3(0f, stalk + (r * stretch * 0.9f), 0f);
            AddBall(s, centre, r, 8, 5, 0.88f, 0f, 0f, phase, 0f, stretch);
            int spores = 4 + own.Next(3);
            for (int i = 0; i < spores; i++)
            {
                float phi = (i * 6.2832f / spores) + Range(own, -0.2f, 0.2f);
                var at = centre + new Vector3(Mathf.Cos(phi) * r * 0.85f, r * stretch * Range(own, 0.2f, 0.7f), Mathf.Sin(phi) * r * 0.85f);
                AddBall(s, at, 0.035f, 5, 3, 0.98f, 1f, 1f, Frac(phase + (0.1f * i)), 0.4f);
            }
        }

        /// <summary>Crystals: cut six-sided prisms with pointed tips, every face its own flat facet — a cluster with one
        /// tall crystal and smaller ones leaning away, or a <paramref name="bloom"/> of them ringing a glowing middle.</summary>
        private static void BuildCrystals(Shape s, System.Random species, System.Random own, bool bloom)
        {
            int sides = species.NextDouble() < 0.7 ? 6 : 5;
            float radius = Range(species, 0.07f, 0.115f);
            float length = Range(species, 0.5f, 0.74f);
            Range(species, 1.3f, 2.2f); // (the old tip length: the roll stays, so the species keep their other numbers)
            int count = 4 + species.Next(4);
            float phase = Range(own, 0f, 1f);
            float turn = Range(own, 0f, 6.2832f);
            if (bloom)
            {
                var heart = new Vector3(0f, Range(species, 0.22f, 0.34f), 0f);
                AddPrism(s, Vector3.zero, Vector3.up, radius * 0.55f, heart.y, radius * 0.3f, sides, 0.6f, 0f, phase);
                AddBall(s, heart + new Vector3(0f, 0.03f, 0f), radius * 0.95f, 7, 4, 0.98f, 1f, 0f, phase, 0f);
                for (int i = 0; i < count; i++)
                {
                    float phi = turn + (i * 6.2832f / count);
                    float lean = Range(own, 0.6f, 1.05f);
                    var axis = (new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi)) * Mathf.Sin(lean)) + (Vector3.up * Mathf.Cos(lean));
                    float len = Mathf.Min(length * Range(own, 0.45f, 0.65f), 0.4f / Mathf.Max(Mathf.Sin(lean), 0.2f));
                    AddPrism(s, heart, axis, radius * 0.6f, len * 0.6f, len * 0.4f, sides, Range(own, 0.75f, 0.95f), Range(own, 0.3f, 0.8f), phase);
                }

                return;
            }

            // A cut stone in the middle — a narrow foot, a broad girdle, a crown and a point — and smaller stones
            // leaning out around it (the gems of the concept pictures, not slim needles).
            float girdle = Range(species, 0.17f, 0.25f);
            float height = Range(species, 0.66f, 0.9f);
            float waist = Range(species, 0.28f, 0.4f);
            AddGem(s, Vector3.zero, new Vector3(Range(own, -0.06f, 0.06f), 1f, Range(own, -0.06f, 0.06f)).normalized, girdle, height, waist,
                sides, 0.86f, phase, turn);
            for (int i = 0; i < count; i++)
            {
                float phi = turn + (i * 6.2832f / count) + Range(own, -0.3f, 0.3f);
                float lean = Range(own, 0.25f, 0.7f);
                var dir = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
                var axis = (dir * Mathf.Sin(lean)) + (Vector3.up * Mathf.Cos(lean));
                float g = girdle * Range(own, 0.36f, 0.62f);
                float h = Mathf.Min(height * Range(own, 0.3f, 0.6f), 0.34f / Mathf.Max(Mathf.Sin(lean), 0.2f));
                AddGem(s, dir * (girdle * Range(own, 0.55f, 0.95f)), axis, g, h, waist, sides, Range(own, 0.7f, 0.92f), phase, phi);
            }
        }

        /// <summary>A cut stone: rings at the foot, the girdle (its widest) and the crown, and the point — every face
        /// flat, lit as one facet. Marked as a gem for the shader (thin = 2): deep where a facet turns away, light
        /// where one looks into it, with a light of its own.</summary>
        private static void AddGem(Shape s, Vector3 from, Vector3 axis, float girdle, float height, float waist, int sides, float tone,
            float phase, float turn)
        {
            const float GemMark = 2f;
            var f = Frame.Along(from, axis);
            float[] at = { 0f, height * waist, height * (waist + ((1f - waist) * 0.55f)), height };
            float[] radius = { girdle * 0.55f, girdle, girdle * 0.5f, 0f };
            float[] lift = { -0.14f, 0.02f, 0.1f };
            for (int k = 0; k < sides; k++)
            {
                float a0 = turn + (k * 6.2832f / sides), a1 = turn + ((k + 1) * 6.2832f / sides);
                var mid = f.Out((a0 + a1) * 0.5f);
                float facet = tone + (k % 2 == 0 ? 0.06f : -0.06f) + (k % 3 == 0 ? 0.04f : 0f);
                for (int ring = 0; ring < 3; ring++)
                {
                    Vector3 l0 = from + (f.A * at[ring]) + (f.Out(a0) * radius[ring]), l1 = from + (f.A * at[ring]) + (f.Out(a1) * radius[ring]);
                    Vector3 u0 = from + (f.A * at[ring + 1]) + (f.Out(a0) * radius[ring + 1]), u1 = from + (f.A * at[ring + 1]) + (f.Out(a1) * radius[ring + 1]);
                    var normal = Vector3.Cross(l1 - l0, u0 - l0);
                    normal = normal.sqrMagnitude > 1e-10f ? normal.normalized : mid;
                    if (Vector3.Dot(normal, mid) < 0f)
                    {
                        normal = -normal;
                    }

                    float t = facet + lift[ring];
                    int first = s.Count;
                    s.Add(l0, normal, t, 0f, 0f, phase, 0f, GemMark, 0f, from);
                    s.Add(l1, normal, t, 0f, 0f, phase, 0f, GemMark, 0f, from);
                    if (ring == 2)
                    {
                        s.Add(u0, normal, t + 0.06f, 0f, 0f, phase, 0f, GemMark, 0f, from);
                        s.Tri(first, first + 1, first + 2);
                    }
                    else
                    {
                        s.Add(u1, normal, t, 0f, 0f, phase, 0f, GemMark, 0f, from);
                        s.Add(u0, normal, t, 0f, 0f, phase, 0f, GemMark, 0f, from);
                        s.Quad(first, first + 1, first + 2, first + 3);
                    }
                }
            }
        }

        /// <summary>A succulent: a rosette of fat round leaves, the inner ones upright and the outer ones lying flat, their
        /// tips in the second colour.</summary>
        private static void BuildSucculent(Shape s, System.Random species, System.Random own)
        {
            int leaves = 8 + species.Next(6);
            float length = Range(species, 0.26f, 0.4f);
            float thick = Range(species, 0.05f, 0.075f);
            float turn = Range(own, 0f, 6.2832f);
            for (int i = 0; i < leaves; i++)
            {
                float rank = i / (float)(leaves - 1);
                float phi = turn + (i * 2.39996f);
                var dir = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
                float len = length * Mathf.Lerp(0.6f, 1f, rank) * Range(own, 0.9f, 1.1f);
                var line = Curve(dir * 0.02f + new Vector3(0f, 0.03f, 0f), dir, Mathf.Lerp(0.15f, 1.25f, rank), -0.35f, len, 4, out _);
                Fit(line, 0.44f - thick);
                float phase = Range(own, 0f, 1f);
                AddTube(s, line, new[] { thick * 0.7f, thick, thick * 0.95f, thick * 0.6f }, 6, 0.55f, 0.92f, phase, dark: 0f, secondTip: 0.8f);
                AddBall(s, line[3], thick * 0.6f, 6, 3, 0.95f, 0.9f, 0f, phase, 0f);
            }
        }

        /// <summary>A pitcher plant: two or three upright pitchers — a belly, a neck, a wide lip in the second colour and a
        /// small lid over it — and a few leaves at the foot.</summary>
        private static void BuildPitcher(Shape s, System.Random species, System.Random own)
        {
            int pitchers = 2 + species.Next(2);
            float height = Range(species, 0.6f, 0.88f);
            float belly = Range(species, 0.08f, 0.11f);
            float turn = Range(own, 0f, 6.2832f);
            for (int i = 0; i < pitchers; i++)
            {
                float scale = i == 0 ? 1f : Range(own, 0.55f, 0.8f);
                float phi = turn + (i * 6.2832f / pitchers);
                var dir = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
                var line = Curve(dir * 0.06f, dir, Range(own, 0.1f, 0.3f), -0.25f, height * scale, 5, out _);
                Fit(line, 0.44f - (belly * scale));
                float phase = Range(own, 0f, 1f);
                float b = belly * scale;
                AddTube(s, line, new[] { b * 0.45f, b * 0.8f, b, b * 0.75f, b * 1.05f }, 7, 0.55f, 0.95f, phase, dark: 0f, secondTip: 0.9f, flutter: 0.3f);
                AddPad(s, line[4] + new Vector3(0f, b * 0.5f, 0f) + (dir * b * 0.3f), b * 0.9f, phi + 3.1416f, 0.6f, phase);
            }

            var foot = Frame.Upright(Vector3.zero);
            for (int i = 0; i < 3; i++)
            {
                float len = Range(own, 0.16f, 0.24f);
                AddLeaf(s, foot, new Leaf
                {
                    Phi = turn + 1f + (i * 2.0944f), Length = len, HalfWidth = len * 0.25f, Theta = Range(own, 0.9f, 1.2f), Droop = 0.5f,
                    Fold = 0.3f, Phase = Range(own, 0f, 1f), Tone = 0.95f, RootOffset = 0.03f, MaxReach = 0.44f, OnGround = true,
                });
            }
        }

        /// <summary>A pumpkin: one large, slightly flat ball with a short crooked stalk and a leaf.</summary>
        private static void BuildPumpkin(Shape s, System.Random species, System.Random own)
        {
            float radius = Range(species, 0.38f, 0.44f);
            float flat = Range(species, 0.72f, 0.9f);
            float phase = Range(own, 0f, 1f);
            var middle = new Vector3(0f, radius * flat, 0f);
            AddBall(s, middle, radius, 12, 6, 0.9f, 0f, 0f, phase, 0f, flat);
            var top = middle + new Vector3(0f, radius * flat * 0.96f, 0f);
            float lean = Range(own, 0f, 6.2832f);
            var tip = top + new Vector3(Mathf.Cos(lean) * 0.05f, 0.1f, Mathf.Sin(lean) * 0.05f);
            AddTube(s, new[] { top - new Vector3(0f, 0.02f, 0f), Vector3.Lerp(top, tip, 0.5f) + new Vector3(0f, 0.02f, 0f), tip }, new[] { 0.04f, 0.03f, 0.028f }, 6,
                0.4f, 0.55f, phase);
            AddLeaf(s, Frame.Upright(top), new Leaf
            {
                Phi = lean + 2f, Length = 0.2f, HalfWidth = 0.07f, Theta = 1.35f, Droop = 0.5f, Fold = 0.25f, Second = 1f, Phase = phase,
                Tone = 1f, RootOffset = 0.03f, MaxReach = 0.46f,
            });
        }

        /// <summary>The ember bloom: a thick stem and one large open cup around a glowing middle.</summary>
        private static void BuildBloom(Shape s, System.Random species, System.Random own)
        {
            float height = Range(species, 0.4f, 0.6f);
            float phase = Range(own, 0f, 1f);
            float phi = Range(own, 0f, 6.2832f);
            var dir = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
            var line = Curve(Vector3.zero, dir, Range(own, 0.03f, 0.12f), 0.2f, height, 4, out var tangent);
            AddTube(s, line, new[] { 0.055f, 0.048f, 0.042f, 0.04f }, 6, 0.45f, 0.75f, phase);
            var head = Frame.Along(line[3], tangent);
            AddBall(s, line[3] + (tangent * 0.06f), 0.07f, 7, 4, 0.98f, 1f, 0f, phase, 0.3f);
            int petals = 6 + own.Next(3);
            float len = Range(species, 0.22f, 0.3f);
            for (int k = 0; k < petals; k++)
            {
                AddLeaf(s, head, new Leaf
                {
                    Phi = k * 6.2832f / petals, Length = len * Range(own, 0.92f, 1.06f), HalfWidth = len * 0.34f, Theta = Range(own, 0.45f, 0.7f),
                    Droop = 0.6f, Fold = 0.25f, AccentAll = 1f, Phase = phase, Tone = Range(own, 0.9f, 1.05f), RootOffset = 0.03f,
                    MaxReach = 0.46f, RootTone = 0.7f,
                });
            }
        }

        // --- Parts ---

        /// <summary>A cut prism (a crystal): flat side faces from a slightly narrower foot up to a point — every face with
        /// its own normal and its own brightness, so the facets show. Stands still (no wind).</summary>
        private static void AddPrism(Shape s, Vector3 from, Vector3 axis, float radius, float length, float tip, int sides, float tone,
            float second, float phase)
        {
            var f = Frame.Along(from, axis);
            Vector3 top = from + (f.A * length), apex = top + (f.A * tip);
            for (int k = 0; k < sides; k++)
            {
                float a0 = k * 6.2832f / sides, a1 = (k + 1) * 6.2832f / sides;
                Vector3 o0 = f.Out(a0) * radius, o1 = f.Out(a1) * radius;
                var normal = f.Out((a0 + a1) * 0.5f);
                float facet = tone + (k % 2 == 0 ? 0.07f : -0.07f) + (k % 3 == 0 ? 0.04f : 0f);
                int first = s.Count;
                s.Add(from + (o0 * 0.85f), normal, facet - 0.2f, 0f, 0f, phase, 0f, 2f, 0f, from);
                s.Add(from + (o1 * 0.85f), normal, facet - 0.2f, 0f, 0f, phase, 0f, 2f, 0f, from);
                s.Add(top + o1, normal, facet, 0f, 0f, phase, 0f, 2f, 0f, from);
                s.Add(top + o0, normal, facet, 0f, 0f, phase, 0f, 2f, 0f, from);
                s.Quad(first, first + 1, first + 2, first + 3);

                var slope = ((normal * tip) + (f.A * radius)).normalized;
                first = s.Count;
                s.Add(top + o0, slope, facet + 0.08f, 0f, 0f, phase, 0f, 2f, 0f, from);
                s.Add(top + o1, slope, facet + 0.08f, 0f, 0f, phase, 0f, 2f, 0f, from);
                s.Add(apex, slope, facet + 0.14f, 0f, 0f, phase, 0f, 2f, 0f, from);
                s.Tri(first, first + 1, first + 2);
            }
        }

        /// <summary>A flat round disc facing down (the underside of a mushroom cap).</summary>
        private static void AddDisc(Shape s, Vector3 centre, float radius, int segments, float tone, float second, float phase)
        {
            int first = s.Count;
            s.Add(centre, Vector3.down, tone, second, 0f, phase, 0f, 0.2f, 0f, centre);
            for (int k = 0; k < segments; k++)
            {
                float angle = k * 6.2832f / segments;
                s.Add(centre + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius), Vector3.down, tone - 0.1f, second, 0f, phase,
                    0f, 0.2f, 0f, centre);
            }

            for (int k = 0; k < segments; k++)
            {
                s.Tri(first, first + 1 + k, first + 1 + ((k + 1) % segments));
            }
        }
    }
}
