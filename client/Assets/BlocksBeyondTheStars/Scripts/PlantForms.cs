// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The generated forms of small plants (<see cref="PlantBodies"/>). A plant is a small
    /// body instead of crossed pictures, in one of three families: a rosette of pointed leaves, a flower on round stems
    /// with a head of small balls or petals, and a bush of small cubes on twigs with round berries. The FORM is rolled
    /// from the seed per world and species (leaf count, length, droop, stem count, head kind, outline …), the way the
    /// colour is rolled today; a species has a few individuals that differ a little.
    /// <para>
    /// Form without colour: a vertex carries a brightness and the weights of the colour
    /// slots (main, second, accent, dark) and no colour. The palette comes from the world (PlantBody.shader).
    /// </para>
    /// </summary>
    public static partial class PlantForms
    {
        public const int Rosette = 0;
        public const int Flower = 1;
        public const int Bush = 2;
        public const int Tuft = 3;     // thick blades out of one root, with or without seed heads
        public const int Cushion = 4;  // low round lumps on the ground (moss, lichen)
        public const int Strand = 5;   // round strands with leaves or beads: arching on land, hanging, or standing in water
        public const int Coral = 6;    // smooth tubes with round tips
        public const int Lily = 7;     // a flat pad on the water with a cup blossom
        public const int Fruit = 8;    // what hangs under a crown: a ball, a pod, a cluster, a bunch
        public const int Sapling = 9;  // a thin stem with a few leaves

        /// <summary>Individuals per species and world.</summary>
        public const int Variants = 4;

        private const int MaxBushCubes = 42;

        // Head kinds of the flower family.
        private const int HeadUmbel = 0; // a cluster of small balls
        private const int HeadDisc = 1;  // a round middle with a ring of flat petals
        private const int HeadCup = 2;   // upright petals forming a cup
        private const int HeadSpike = 3; // small balls up the stem

        /// <summary>The family a species block is drawn in, or -1 for every species that stays a picture.</summary>
        public static int FamilyOf(string key)
        {
            switch (key)
            {
                case "flora_fern":
                case "flora_alienfern":
                case "flora_dryshrub":
                case "flora_needlebush":
                case "flora_plant":
                case "flora_palm":
                case "flora_ashweed":
                    return Rosette;
                case "flora_flower":
                case "flora_daisy":
                case "flora_tulip":
                case "flora_sunblossom":
                case "flora_thistle":
                case "flora_lupine":
                case "flora_bellflower":
                case "flora_frostflower":
                case "flora_orchid":
                case "flora_rockflower":
                case "flora_clover":
                    return Flower;
                case "flora_bush":
                case "flora_berrybush":
                case "flora_blossombush":
                case "flora_herb":
                case "flora_snowbush":
                case "flora_frostberry":
                    return Bush;
                default:
                    return MoreFamilyOf(key);
            }
        }

        /// <summary>The head kinds a flower species may roll: a tulip stays a cup and a lupine a spike, the plain flower
        /// rolls any of them.</summary>
        private static int[] HeadsOf(string key)
        {
            switch (key)
            {
                case "flora_tulip": return new[] { HeadCup };
                case "flora_lupine": return new[] { HeadSpike };
                case "flora_daisy": return new[] { HeadDisc };
                case "flora_sunblossom": return new[] { HeadDisc, HeadUmbel };
                case "flora_thistle": return new[] { HeadUmbel };
                case "flora_bellflower": return new[] { HeadCup, HeadUmbel };
                case "flora_frostflower": return new[] { HeadDisc, HeadCup };
                case "flora_orchid": return new[] { HeadCup, HeadSpike };
                case "flora_rockflower": return new[] { HeadDisc, HeadUmbel };
                case "flora_clover": return new[] { HeadUmbel };
                case "flora_marigold": return new[] { HeadDisc };
                case "bell_bloom": return new[] { HeadCup };
                case "bell_bloom_open": return new[] { HeadDisc };
                case "echo_flower": return new[] { HeadCup };
                default: return new[] { HeadUmbel, HeadDisc, HeadCup, HeadSpike };
            }
        }

        /// <summary>One built individual: the mesh and how far it reaches sideways from its root (the instance is scaled
        /// to stay inside its cell).</summary>
        public readonly struct Built
        {
            public readonly Mesh Mesh;
            public readonly float Radius;

            public Built(Mesh mesh, float radius)
            {
                Mesh = mesh;
                Radius = radius;
            }
        }

        /// <summary>Builds individual <paramref name="variant"/> of a species on a world. A pure function of its inputs:
        /// every client grows the same plants. <paramref name="tall"/> = an upper-storey species, <paramref name="accent"/>
        /// = it carries blossoms or berries in an accent colour.</summary>
        public static Built Build(int family, string key, bool tall, bool accent, long worldSeed, string location, int variant)
        {
            var s = Grow(family, key, tall, accent, worldSeed, location, variant, out float stiffness);
            return s.ToMesh($"PlantBody {key} {variant}", stiffness);
        }

        /// <summary>The buffers of individual <paramref name="variant"/> before they become a mesh (<see cref="Build"/>; the
        /// far picture's bake reads them too, PlantForms.Far.cs) and how stiff it stands in the wind.</summary>
        private static Shape Grow(int family, string key, bool tall, bool accent, long worldSeed, string location, int variant,
            out float stiffness)
        {
            uint speciesHash = Fnv($"{worldSeed}|{location}|plant-form|{key}");
            var species = new System.Random(unchecked((int)speciesHash));
            var own = new System.Random(unchecked((int)(speciesHash * 31u + (uint)variant * 2654435761u + 17u)));
            var s = new Shape();
            stiffness = 1f;
            switch (family)
            {
                case Rosette:
                    BuildRosette(s, species, own, tall, accent);
                    break;
                case Flower:
                    BuildFlower(s, species, own, tall, HeadsOf(key), key == "flora_clover");
                    break;
                case Bush:
                    BuildBush(s, species, own, accent, key == "flora_thornbush" || key == "flora_cinderbush");
                    stiffness = 0.3f; // a bush is woody: it trembles, it does not bow
                    break;
                default:
                    stiffness = BuildMore(s, family, key, species, own, tall, accent, variant);
                    break;
            }

            return s;
        }

        // --- The three families ---

        /// <summary>A rosette: pointed, folded leaves out of one root — the inner ones steep and young (second colour), the
        /// outer ones flat and dark.</summary>
        private static void BuildRosette(Shape s, System.Random species, System.Random own, bool tall, bool accent)
        {
            int count = 7 + species.Next(8);
            float length = tall ? Range(species, 0.55f, 0.85f) : Range(species, 0.24f, 0.48f);
            float widthRatio = Range(species, 0.07f, 0.16f);
            float droop = Range(species, 0.2f, 1.3f);
            float fold = Range(species, 0.18f, 0.5f);
            bool tips = species.NextDouble() < (accent ? 0.6 : 0.35);

            count = Mathf.Max(5, count + own.Next(3) - 1);
            float turn = Range(own, 0f, 6.2832f);
            var frame = Frame.Upright(Vector3.zero);
            for (int i = 0; i < count; i++)
            {
                float rank = count > 1 ? i / (float)(count - 1) : 1f; // 0 innermost … 1 outermost
                float phi = turn + (i * 2.39996f) + Range(own, -0.25f, 0.25f);
                // A tall species holds its leaves steeper: it rises (a fern's shuttlecock) instead of spreading.
                float theta = Mathf.Lerp(tall ? 0.1f : 0.16f, tall ? 0.72f : 1.15f, Mathf.Pow(rank, 0.8f)) + Range(own, -0.08f, 0.08f);
                // … and its inner leaves are the long ones (they carry its height); a low rosette's outer leaves are.
                float len = length * (tall ? Mathf.Lerp(1f, 0.7f, rank) : Mathf.Lerp(0.62f, 1f, rank)) * Range(own, 0.9f, 1.1f);
                var leaf = new Leaf
                {
                    Phi = phi,
                    Length = len,
                    HalfWidth = len * widthRatio,
                    Theta = theta,
                    Droop = droop * Mathf.Lerp(0.5f, 1f, rank) * (tall ? 0.7f : 1f),
                    Fold = fold,
                    Second = Mathf.Clamp01(0.95f - (rank * 1.7f)),
                    AccentTip = tips ? 1f : 0f,
                    Phase = Range(own, 0f, 1f),
                    Tone = Mathf.Lerp(1.05f, 0.9f, rank),
                    RootOffset = 0.012f + (0.02f * rank),
                    MaxReach = 0.42f,
                    OnGround = true,
                };
                AddLeaf(s, frame, leaf);
            }
        }

        /// <summary>A flower: one to three round, bent stems with a head each and a few leaves at the foot. A
        /// <paramref name="low"/> species (clover) is a patch: short stems, small heads, more leaves — its stems keep
        /// their thickness, a thinner one would fall below a pixel a few metres away.</summary>
        private static void BuildFlower(Shape s, System.Random species, System.Random own, bool tall, int[] heads, bool low)
        {
            int stems = 1 + species.Next(3);
            float height = tall ? Range(species, 0.75f, 1.1f) : Range(species, 0.32f, 0.6f);
            float stemRadius = Range(species, 0.012f, 0.019f) * (tall ? 1.3f : 1f);
            float nod = Range(species, 0.1f, 0.9f);
            int head = heads[species.Next(heads.Length)];
            float headSize = Range(species, 0.85f, 1.25f) * (tall ? 1.15f : 1f);
            int leaves = 2 + species.Next(3);
            float leafLength = Range(species, 0.12f, 0.22f) * (tall ? 1.4f : 1f);
            if (low)
            {
                stems = Mathf.Max(stems, 2);
                height *= 0.42f;
                headSize *= 0.75f;
                leaves += 2;
            }

            float turn = Range(own, 0f, 6.2832f);
            for (int i = 0; i < stems; i++)
            {
                AddFlowerStem(s, own, i, stems, turn, height, stemRadius, nod, head, headSize, low);
            }

            AddFlowerFootLeaves(s, own, turn, leaves, leafLength);
        }

        /// <summary>Stem <paramref name="i"/> of a flower: its own lean and length, the bent round stem and the head on
        /// top. One step because every stem rolls these from <paramref name="own"/> in this order.</summary>
        private static void AddFlowerStem(Shape s, System.Random own, int i, int stems, float turn, float height, float stemRadius,
            float nod, int head, float headSize, bool low)
        {
            float phi = turn + (i * 6.2832f / stems) + Range(own, -0.3f, 0.3f);
            float h = height * (1f - (0.18f * i)) * Range(own, 0.92f, 1.08f);
            float theta = stems == 1 ? Range(own, 0.03f, 0.12f) : Range(own, 0.14f, 0.34f);
            float bend = nod * Range(own, 0.7f, 1.2f);
            var dir = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));

            const int Points = 5;
            var line = new Vector3[Points];
            var radius = new float[Points];
            var at = dir * (stems == 1 ? 0f : 0.03f);
            Vector3 tangent = Vector3.up;
            for (int p = 0; p < Points; p++)
            {
                float t = p / (float)(Points - 1);
                line[p] = at;
                radius[p] = stemRadius * Mathf.Lerp(1f, 0.7f, t);
                float angle = theta + (bend * t * t);
                tangent = (dir * Mathf.Sin(angle)) + (Vector3.up * Mathf.Cos(angle));
                at += tangent * (h / (Points - 1));
            }

            float phase = Range(own, 0f, 1f);
            AddTube(s, line, radius, 6, 0.5f, 0.82f, phase);
            var top = line[Points - 1];
            var headFrame = Frame.Along(top, tangent);
            switch (head)
            {
                case HeadDisc:
                    AddDiscHead(s, own, headFrame, headSize, phase);
                    break;
                case HeadCup:
                    AddCupHead(s, own, headFrame, headSize, phase);
                    break;
                case HeadSpike:
                    AddSpikeHead(s, own, line, stemRadius, headSize, phase);
                    break;
                default:
                    AddUmbelHead(s, own, headFrame, headSize, phase, stems >= 3 || low, !low);
                    break;
            }
        }

        /// <summary>The leaves at a flower's foot, spread around it after its stems (their rolls come last).</summary>
        private static void AddFlowerFootLeaves(Shape s, System.Random own, float turn, int leaves, float leafLength)
        {
            var foot = Frame.Upright(Vector3.zero);
            for (int i = 0; i < leaves; i++)
            {
                float len = leafLength * Range(own, 0.8f, 1.1f);
                AddLeaf(s, foot, new Leaf
                {
                    Phi = turn + 1f + (i * 6.2832f / leaves) + Range(own, -0.4f, 0.4f),
                    Length = len,
                    HalfWidth = len * 0.2f,
                    Theta = Range(own, 0.7f, 1.1f),
                    Droop = 0.5f,
                    Fold = 0.3f,
                    Phase = Range(own, 0f, 1f),
                    Tone = 0.95f,
                    RootOffset = 0.015f,
                    MaxReach = 0.4f,
                    OnGround = true,
                });
            }
        }

        private static void AddUmbelHead(Shape s, System.Random own, Frame f, float size, float phase, bool small, bool fine)
        {
            float r = 0.04f * size;
            int balls = small ? 3 + own.Next(3) : 4 + own.Next(4); // a plant with three heads keeps each one smaller
            int segments = fine ? 7 : 5, rings = fine ? 4 : 3; // the small heads of a low patch need fewer corners
            AddBall(s, f.O + (f.A * (r * 0.9f)), r, segments, rings, Range(own, 0.88f, 1f), 0f, 1f, phase, 0.6f);
            float turn = Range(own, 0f, 6.2832f);
            for (int k = 0; k < balls; k++)
            {
                float phi = turn + (k * 6.2832f / balls);
                float lift = (k % 2 == 0 ? 0.25f : -0.3f) * r;
                var at = f.O + (f.A * lift) + (f.Out(phi) * (r * 1.35f));
                AddBall(s, at, r * Range(own, 0.82f, 1f), segments, rings, Range(own, 0.72f, 0.95f), Range(own, 0f, 0.3f), 1f,
                    Frac(phase + (0.07f * k)), 0.6f);
            }
        }

        private static void AddDiscHead(Shape s, System.Random own, Frame f, float size, float phase)
        {
            float r = 0.045f * size;
            AddBall(s, f.O + (f.A * (r * 0.35f)), r, 8, 5, 0.82f, 1f, 0f, phase, 0.5f);
            int petals = 6 + own.Next(4);
            float turn = Range(own, 0f, 6.2832f);
            float len = 0.095f * size;
            for (int k = 0; k < petals; k++)
            {
                AddLeaf(s, f, new Leaf
                {
                    Phi = turn + (k * 6.2832f / petals),
                    Length = len * Range(own, 0.92f, 1.06f),
                    HalfWidth = len * 0.3f,
                    Theta = Range(own, 1.1f, 1.35f),
                    Droop = 0.25f,
                    Fold = 0.15f,
                    AccentAll = 1f,
                    Phase = phase,
                    Tone = Range(own, 0.9f, 1.05f),
                    RootOffset = r * 0.7f,
                    MaxReach = 1f,
                    RootTone = 0.78f,
                });
            }
        }

        private static void AddCupHead(Shape s, System.Random own, Frame f, float size, float phase)
        {
            int petals = 5 + own.Next(2);
            float turn = Range(own, 0f, 6.2832f);
            float len = 0.11f * size;
            AddBall(s, f.O + (f.A * (len * 0.3f)), 0.02f * size, 5, 3, 0.85f, 1f, 0f, phase, 0.5f);
            for (int k = 0; k < petals; k++)
            {
                AddLeaf(s, f, new Leaf
                {
                    Phi = turn + (k * 6.2832f / petals),
                    Length = len * Range(own, 0.94f, 1.06f),
                    HalfWidth = len * 0.42f,
                    Theta = Range(own, 0.22f, 0.36f),
                    Droop = 0.55f,
                    Fold = 0.25f,
                    AccentAll = 1f,
                    Phase = phase,
                    Tone = Range(own, 0.9f, 1.05f),
                    RootOffset = 0.012f * size,
                    MaxReach = 1f,
                    RootTone = 0.7f,
                });
            }
        }

        private static void AddSpikeHead(Shape s, System.Random own, Vector3[] line, float stemRadius, float size, float phase)
        {
            int balls = 7 + own.Next(4);
            float turn = Range(own, 0f, 6.2832f);
            for (int k = 0; k < balls; k++)
            {
                float u = k / (float)(balls - 1);
                float t = Mathf.Lerp(0.55f, 1f, u) * (line.Length - 1);
                int seg = Mathf.Min((int)t, line.Length - 2);
                var centre = Vector3.Lerp(line[seg], line[seg + 1], t - seg);
                var f = Frame.Along(centre, (line[seg + 1] - line[seg]).normalized);
                float r = Mathf.Lerp(0.036f, 0.019f, u) * size;
                var at = centre + (f.Out(turn + (k * 2.4f)) * (stemRadius + (r * 0.7f)));
                AddBall(s, at, r, 5, 3, Range(own, 0.75f, 1f), Range(own, 0f, 0.25f), 1f, Frac(phase + (0.05f * k)), 0.5f);
            }

            AddBall(s, line[line.Length - 1], 0.019f * size, 4, 3, 0.95f, 0.2f, 1f, phase, 0.5f);
        }

        /// <summary>A bush: small cubes on a grid inside a rounded outline, its shell filled and its middle left open, on a
        /// few twigs — and round berries or blossoms on the outside when the species has an accent colour.</summary>
        private static void BuildBush(Shape s, System.Random species, System.Random own, bool accent, bool twiggy)
        {
            float rx = Range(species, 0.22f, 0.3f);
            float ry = rx * Range(species, 0.7f, 1.05f);
            float pitch = Range(species, 0.095f, 0.115f);
            float density = Range(species, 0.78f, 0.94f); // a closed shrub with a few gaps, not a loose pile
            float berryShare = accent ? Range(species, 0.2f, 0.45f) : 0f;
            float berryRadius = Range(species, 0.022f, 0.034f);

            var centre = new Vector3(0f, (ry * 0.95f) + 0.04f, 0f);
            var shift = new Vector3(Range(own, 0f, pitch), 0f, Range(own, 0f, pitch));
            var cubes = BushCells(own, centre, shift, rx, ry, pitch, density);
            ThinBush(own, cubes, twiggy);
            AddBushLeaves(s, own, cubes, pitch, centre.y + ry);
            AddBushBerries(s, own, cubes, centre, pitch, berryShare, berryRadius);
            AddBushTwigs(s, own, centre, rx, ry, twiggy);
            AddBushBranches(s, own, centre, rx, ry, twiggy);
        }

        /// <summary>The bush's leaf cubes: the cells of a shifted grid inside its rounded shell (the open middle left
        /// out), each kept by a roll against <paramref name="density"/>.</summary>
        private static List<Vector3> BushCells(System.Random own, Vector3 centre, Vector3 shift, float rx, float ry, float pitch,
            float density)
        {
            var cubes = new List<Vector3>();
            int reach = Mathf.CeilToInt(rx / pitch) + 1;
            for (int gy = 0; gy <= Mathf.CeilToInt(2f * ry / pitch) + 1; gy++)
            {
                for (int gx = -reach; gx <= reach; gx++)
                {
                    for (int gz = -reach; gz <= reach; gz++)
                    {
                        var p = new Vector3((gx * pitch) + shift.x, (gy + 0.5f) * pitch, (gz * pitch) + shift.z);
                        var rel = p - centre;
                        float wide = rx * Mathf.Lerp(0.72f, 1f, Mathf.Clamp01(p.y / (2f * ry))); // narrower at the foot
                        float f = Mathf.Sqrt((rel.x * rel.x / (wide * wide)) + (rel.y * rel.y / (ry * ry)) + (rel.z * rel.z / (wide * wide)));
                        bool roll = own.NextDouble() < density; // drawn for every cell, so the pattern does not shift with the outline
                        if (f <= 1f && f >= 0.5f && roll)
                        {
                            cubes.Add(p);
                        }
                    }
                }
            }

            return cubes;
        }

        /// <summary>Thins the cubes down to the cap, evenly (never cut off at the top); a twiggy bush (thorn bush, cinder
        /// bush) then keeps only a third of its leaves, so its branches show.</summary>
        private static void ThinBush(System.Random own, List<Vector3> cubes, bool twiggy)
        {
            while (cubes.Count > MaxBushCubes)
            {
                cubes.RemoveAt(own.Next(cubes.Count)); // thinned evenly — never cut off at the top
            }

            // A twiggy bush (thorn bush, cinder bush) keeps a third of its leaves: its branches show.
            for (int keep = Mathf.Max(6, cubes.Count / 3); twiggy && cubes.Count > keep;)
            {
                cubes.RemoveAt(own.Next(cubes.Count));
            }
        }

        /// <summary>One small cube per kept cell, lighter toward the bush's <paramref name="top"/>, a third of them young.</summary>
        private static void AddBushLeaves(Shape s, System.Random own, List<Vector3> cubes, float pitch, float top)
        {
            foreach (var p in cubes)
            {
                float half = pitch * 0.5f * Range(own, 0.72f, 1f);
                float tone = 0.5f + (0.4f * Mathf.Clamp01(p.y / top)) + Range(own, -0.12f, 0.12f);
                float second = own.NextDouble() < 0.33 ? Range(own, 0.5f, 1f) : 0f;
                AddCube(s, p, half, tone, second, Range(own, 0f, 1f), 0.7f);
            }
        }

        /// <summary>Round berries or blossoms sitting on the outside of randomly picked cubes (none without an accent:
        /// <paramref name="berryShare"/> is 0 then).</summary>
        private static void AddBushBerries(Shape s, System.Random own, List<Vector3> cubes, Vector3 centre, float pitch,
            float berryShare, float berryRadius)
        {
            int berries = Mathf.Min(10, Mathf.RoundToInt(cubes.Count * berryShare));
            for (int k = 0; k < berries && cubes.Count > 0; k++)
            {
                var p = cubes[own.Next(cubes.Count)];
                var outward = p - centre;
                outward.y = Mathf.Max(outward.y, -0.02f);
                outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.up;
                var at = p + (outward * ((pitch * 0.5f) + (berryRadius * 0.45f)));
                AddBall(s, at, berryRadius * Range(own, 0.85f, 1.1f), 6, 3, Range(own, 0.86f, 1f), 0f, 1f, Range(own, 0f, 1f), 0.7f);
            }
        }

        /// <summary>The three short twigs inside a leafy bush (none on a twiggy one, which grows branches instead).</summary>
        private static void AddBushTwigs(Shape s, System.Random own, Vector3 centre, float rx, float ry, bool twiggy)
        {
            for (int k = 0; k < 3 && !twiggy; k++)
            {
                float phi = Range(own, 0f, 6.2832f);
                var dir = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
                var end = centre + (dir * (rx * 0.45f)) + new Vector3(0f, ry * Range(own, -0.2f, 0.3f), 0f);
                var mid = Vector3.Lerp(Vector3.zero, end, 0.5f) + (dir * 0.02f);
                AddTube(s, new[] { dir * 0.02f, mid, end }, new[] { 0.014f, 0.011f, 0.008f }, 4, 0.42f, 0.6f, Range(own, 0f, 1f));
            }
        }

        /// <summary>The branches of a twiggy bush: out to its outline, crooked, with a few thorns (none on a leafy one).</summary>
        private static void AddBushBranches(Shape s, System.Random own, Vector3 centre, float rx, float ry, bool twiggy)
        {
            for (int k = 0; k < 7 && twiggy; k++)
            {
                float phi = (k * 6.2832f / 7f) + Range(own, -0.3f, 0.3f);
                var dir = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
                var end = centre + (dir * (rx * Range(own, 0.7f, 1f))) + new Vector3(0f, ry * Range(own, -0.5f, 0.9f), 0f);
                var mid = Vector3.Lerp(Vector3.zero, end, 0.55f) + new Vector3(Range(own, -0.05f, 0.05f), Range(own, 0f, 0.06f), Range(own, -0.05f, 0.05f));
                float phase = Range(own, 0f, 1f);
                AddTube(s, new[] { dir * 0.02f, mid, end }, new[] { 0.018f, 0.014f, 0.009f }, 5, 0.38f, 0.6f, phase);
                var along = (end - mid).normalized;
                for (int thorn = 0; thorn < 3; thorn++)
                {
                    var at = Vector3.Lerp(mid, end, 0.25f + (0.3f * thorn));
                    AddLeaf(s, Frame.Along(at, along), new Leaf
                    {
                        Phi = Range(own, 0f, 6.2832f), Length = 0.07f, HalfWidth = 0.012f, Theta = Range(own, 0.9f, 1.4f),
                        Fold = 0.2f, Second = 1f, Phase = phase, Tone = 1f, RootOffset = 0.01f, MaxReach = 1f, RootTone = 0.6f,
                    });
                }
            }
        }

        // --- The parts ---

        /// <summary>The frame a rosette or a head is built in: its origin, its axis and two directions across it.</summary>
        private readonly struct Frame
        {
            public readonly Vector3 O;
            public readonly Vector3 A;
            private readonly Vector3 _e1;
            private readonly Vector3 _e2;

            private Frame(Vector3 origin, Vector3 axis, Vector3 e1, Vector3 e2)
            {
                O = origin;
                A = axis;
                _e1 = e1;
                _e2 = e2;
            }

            public static Frame Upright(Vector3 origin) => new Frame(origin, Vector3.up, Vector3.right, Vector3.forward);

            public static Frame Along(Vector3 origin, Vector3 axis)
            {
                axis = axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.up;
                var reference = Mathf.Abs(axis.x) < 0.9f ? Vector3.right : Vector3.forward;
                var e1 = Vector3.Cross(reference, axis).normalized;
                return new Frame(origin, axis, e1, Vector3.Cross(axis, e1));
            }

            /// <summary>The direction across the axis at angle <paramref name="phi"/>.</summary>
            public Vector3 Out(float phi) => (_e1 * Mathf.Cos(phi)) + (_e2 * Mathf.Sin(phi));

            /// <summary>The direction across the axis a quarter turn on from <see cref="Out"/>.</summary>
            public Vector3 Side(float phi) => (_e2 * Mathf.Cos(phi)) - (_e1 * Mathf.Sin(phi));
        }

        private struct Leaf
        {
            public float Phi;        // the direction it grows in, around the frame's axis
            public float Length;
            public float HalfWidth;  // at its widest
            public float Theta;      // its angle from the axis at the root
            public float Droop;      // how much further it bends over by the tip (radians)
            public float Fold;       // how deep it is folded along the midrib (share of the half width)
            public float Second;     // weight of the second colour (young leaves)
            public float AccentTip;  // 1 = the tip wears the accent colour
            public float AccentAll;  // 1 = a petal: all accent
            public float Phase;
            public float Tone;       // factor on the brightness
            public float RootOffset; // how far from the axis it starts
            public float MaxReach;   // sideways reach it must not pass (it is shortened to fit)
            public float RootTone;   // brightness at the root (0 = the leaf default: a dark base)
            public bool OnGround;    // it grows from the ground: where it droops it lies on it, never under it
        }

        /// <summary>A leaf or petal: a pointed blade along a curve that bends away from the frame's axis, folded along its
        /// midrib, so its two halves catch the light differently. Four stations, three vertices' worth each side.</summary>
        private static void AddLeaf(Shape s, Frame f, Leaf leaf)
        {
            const int Rings = 4;
            const int Steps = 12;
            Vector3 dir = f.Out(leaf.Phi), side = f.Side(leaf.Phi);
            float length = FittedLeafLength(leaf, Steps);
            float[] width = { 0.3f, 1f, 0.75f, 0.12f };
            float[] tone = { leaf.RootTone > 0f ? leaf.RootTone : 0.42f, 0.7f, 0.86f, 0.97f };
            float[] tip = { 0f, 0f, 0.35f, 1f };
            int first = s.Count;
            var at = f.O + (dir * leaf.RootOffset);
            int done = 0;
            for (int ring = 0; ring < Rings; ring++)
            {
                float t = ring / (float)(Rings - 1);
                for (; done < ring * Steps / (Rings - 1); done++)
                {
                    float tt = (done + 0.5f) / Steps;
                    float a = leaf.Theta + (leaf.Droop * Mathf.Pow(tt, 1.5f));
                    at += ((dir * Mathf.Sin(a)) + (f.A * Mathf.Cos(a))) * (length / Steps);
                }

                float angle = leaf.Theta + (leaf.Droop * Mathf.Pow(t, 1.5f));
                var up = (f.A * Mathf.Sin(angle)) - (dir * Mathf.Cos(angle)); // the leaf's upper side
                float w = leaf.HalfWidth * (length / Mathf.Max(leaf.Length, 1e-4f)) * width[ring];
                var lift = up * (w * leaf.Fold);
                Vector3 left = at - (side * w) + lift, right = at + (side * w) + lift, middle = at;
                if (leaf.OnGround)
                {
                    left.y = Mathf.Max(left.y, 0.012f);
                    right.y = Mathf.Max(right.y, 0.012f);
                    middle.y = Mathf.Max(middle.y, 0.008f);
                }

                var normalLeft = ((up * 1f) + (side * leaf.Fold)).normalized;
                var normalRight = ((up * 1f) - (side * leaf.Fold)).normalized;
                float brightness = tone[ring] * leaf.Tone;
                float accent = Mathf.Clamp01(leaf.AccentAll + (leaf.AccentTip * tip[ring]));
                float flutter = t * t;
                s.Add(left, normalLeft, brightness, leaf.Second, accent, leaf.Phase, 0f, 1f, flutter, at);
                s.Add(middle, normalLeft, brightness - 0.1f, leaf.Second, accent, leaf.Phase, 0f, 1f, flutter, at);
                s.Add(middle, normalRight, brightness - 0.1f, leaf.Second, accent, leaf.Phase, 0f, 1f, flutter, at);
                s.Add(right, normalRight, brightness, leaf.Second, accent, leaf.Phase, 0f, 1f, flutter, at);
            }

            for (int ring = 0; ring < Rings - 1; ring++)
            {
                int lower = first + (ring * 4), upper = lower + 4;
                s.Quad(lower, lower + 1, upper + 1, upper);
                s.Quad(lower + 2, lower + 3, upper + 3, upper + 2);
            }
        }

        /// <summary>The length a leaf is built at: a leaf that would reach past its limit is shortened (the outer fronds
        /// of a tall fern are the short ones). Its sideways reach is summed over the same <paramref name="steps"/> its
        /// curve is walked in.</summary>
        private static float FittedLeafLength(Leaf leaf, int steps)
        {
            float reach = 0f;
            for (int k = 0; k < steps; k++)
            {
                float t = (k + 0.5f) / steps;
                reach += Mathf.Sin(leaf.Theta + (leaf.Droop * Mathf.Pow(t, 1.5f))) / steps;
            }

            float length = leaf.Length;
            float limit = leaf.MaxReach - leaf.RootOffset;
            if (reach * length > limit && reach > 1e-4f)
            {
                length = limit / reach;
            }

            return length;
        }

        /// <summary>A round tube along a line (a stem, a twig): rings of <paramref name="sides"/> vertices with their
        /// normals pointing straight out, so the light rounds it. Dark slot.</summary>
        private static void AddTube(Shape s, Vector3[] line, float[] radius, int sides, float toneRoot, float toneTip, float phase,
            float dark = 1f, float secondRoot = 0f, float secondTip = 0f, float flutter = 0f)
        {
            int first = s.Count;
            for (int ring = 0; ring < line.Length; ring++)
            {
                var tangent = (line[Mathf.Min(ring + 1, line.Length - 1)] - line[Mathf.Max(ring - 1, 0)]).normalized;
                var f = Frame.Along(line[ring], tangent);
                float t = ring / (float)(line.Length - 1);
                for (int k = 0; k < sides; k++)
                {
                    var outward = f.Out(k * 6.2832f / sides);
                    s.Add(line[ring] + (outward * radius[ring]), outward, Mathf.Lerp(toneRoot, toneTip, t),
                        Mathf.Lerp(secondRoot, secondTip, t), 0f, phase, dark, 0.15f, flutter * t, line[ring]);
                }
            }

            for (int ring = 0; ring < line.Length - 1; ring++)
            {
                int lower = first + (ring * sides), upper = lower + sides;
                for (int k = 0; k < sides; k++)
                {
                    int next = (k + 1) % sides;
                    s.Quad(lower + k, lower + next, upper + next, upper + k);
                }
            }
        }

        /// <summary>A small ball (a blossom, a berry): two poles and <paramref name="rings"/> - 1 rows of
        /// <paramref name="segments"/> vertices, normals straight out. It moves as one piece (its middle is its pivot).</summary>
        private static void AddBall(Shape s, Vector3 centre, float radius, int segments, int rings, float tone, float second,
            float accent, float phase, float flutter, float stretch = 1f)
        {
            int first = s.Count;
            s.Add(centre - new Vector3(0f, radius * stretch, 0f), Vector3.down, tone - 0.08f, second, accent, phase, 0f, 0.2f, flutter, centre);
            for (int row = 1; row < rings; row++)
            {
                float lat = (-0.5f + (row / (float)rings)) * Mathf.PI;
                float y = Mathf.Sin(lat), flat = Mathf.Cos(lat);
                for (int k = 0; k < segments; k++)
                {
                    float lon = k * 6.2832f / segments;
                    var normal = new Vector3(flat * Mathf.Cos(lon), y, flat * Mathf.Sin(lon));
                    var at = centre + new Vector3(normal.x * radius, normal.y * radius * stretch, normal.z * radius);
                    s.Add(at, new Vector3(normal.x, normal.y / stretch, normal.z).normalized, tone + (0.08f * y), second, accent,
                        phase, 0f, 0.2f, flutter, centre);
                }
            }

            int topIndex = s.Count;
            s.Add(centre + new Vector3(0f, radius * stretch, 0f), Vector3.up, tone + 0.08f, second, accent, phase, 0f, 0.2f, flutter, centre);

            int rows = rings - 1;
            for (int k = 0; k < segments; k++)
            {
                int next = (k + 1) % segments;
                s.Tri(first, first + 1 + k, first + 1 + next);
                int last = first + 1 + ((rows - 1) * segments);
                s.Tri(topIndex, last + next, last + k);
                for (int row = 0; row < rows - 1; row++)
                {
                    int lower = first + 1 + (row * segments), upper = lower + segments;
                    s.Quad(lower + k, lower + next, upper + next, upper + k);
                }
            }
        }

        /// <summary>A small cube (a bush's leaves): six flat faces, the upper ones a little lighter. It moves as one piece.</summary>
        private static void AddCube(Shape s, Vector3 centre, float half, float tone, float second, float phase, float flutter)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var normal = Vector3.zero;
                    normal[axis] = sign;
                    Vector3 u = Vector3.zero, v = Vector3.zero;
                    u[(axis + 1) % 3] = 1f;
                    v[(axis + 2) % 3] = 1f;
                    int first = s.Count;
                    float brightness = tone + (0.06f * normal.y);
                    for (int corner = 0; corner < 4; corner++)
                    {
                        float su = corner == 0 || corner == 3 ? -1f : 1f, sv = corner < 2 ? -1f : 1f;
                        s.Add(centre + ((normal + (u * su) + (v * sv)) * half), normal, brightness, second, 0f, phase, 0f, 0.2f,
                            flutter, centre);
                    }

                    s.Quad(first, first + 1, first + 2, first + 3);
                }
            }
        }

        // --- The buffers ---

        private sealed class Shape
        {
            private readonly List<Vector3> _verts = new List<Vector3>();
            private readonly List<Vector3> _normals = new List<Vector3>();
            private readonly List<Color> _tones = new List<Color>();    // r brightness, g second weight, b accent weight, a bend
            private readonly List<Vector4> _part = new List<Vector4>(); // x phase, y dark weight, z thin (lets light through), w flutter
            private readonly List<Vector3> _pivot = new List<Vector3>(); // the point the wind is read at (a part moves as one)
            private readonly List<int> _tris = new List<int>();

            public int Count => _verts.Count;

            public void Add(Vector3 position, Vector3 normal, float tone, float second, float accent, float phase, float dark,
                float thin, float flutter, Vector3 pivot)
            {
                _verts.Add(position);
                _normals.Add(normal);
                _tones.Add(new Color(Mathf.Clamp01(tone), Mathf.Clamp01(second), Mathf.Clamp01(accent), 0f));
                _part.Add(new Vector4(phase, dark, thin, flutter));
                _pivot.Add(pivot);
            }

            public void Tri(int a, int b, int c)
            {
                _tris.Add(a);
                _tris.Add(b);
                _tris.Add(c);
            }

            public void Quad(int a, int b, int c, int d)
            {
                Tri(a, b, c);
                Tri(a, c, d);
            }

            /// <summary>Plant far pictures: the raw buffers, copied out — positions, normals, the vertex colour
            /// (brightness, second, accent), the part data (y = the dark weight) and the triangles.</summary>
            public void CopyTo(List<Vector3> verts, List<Vector3> normals, List<Color> tones, List<Vector4> parts, List<int> tris)
            {
                verts.AddRange(_verts);
                normals.AddRange(_normals);
                tones.AddRange(_tones);
                parts.AddRange(_part);
                tris.AddRange(_tris);
            }

            /// <summary>The finished mesh. The bend of a vertex — how far the wind moves it — follows from the height of its
            /// pivot in the plant: the root stands, the top sways.</summary>
            public Built ToMesh(string name, float stiffness)
            {
                float height = 0.05f, radius = 0.05f;
                for (int i = 0; i < _verts.Count; i++)
                {
                    height = Mathf.Max(height, _verts[i].y);
                    radius = Mathf.Max(radius, new Vector2(_verts[i].x, _verts[i].z).magnitude);
                }

                float reference = Mathf.Max(height, 0.3f);
                for (int i = 0; i < _tones.Count; i++)
                {
                    var tone = _tones[i];
                    tone.a = Mathf.Pow(Mathf.Clamp01(_pivot[i].y / reference), 1.4f) * stiffness;
                    _tones[i] = tone;
                }

                var mesh = new Mesh { name = name };
                mesh.SetVertices(_verts);
                mesh.SetNormals(_normals);
                mesh.SetColors(_tones);
                mesh.SetUVs(0, _part);
                mesh.SetUVs(1, _pivot);
                mesh.SetTriangles(_tris, 0);
                mesh.bounds = new Bounds(new Vector3(0f, height * 0.5f, 0f),
                    new Vector3((radius * 2f) + 0.7f, height + 0.5f, (radius * 2f) + 0.7f)); // the body plus the sway's reach
                mesh.UploadMeshData(true);
                return new Built(mesh, radius);
            }
        }

        private static float Range(System.Random rng, float min, float max) => min + ((float)rng.NextDouble() * (max - min));

        private static float Frac(float value) => value - Mathf.Floor(value);

        /// <summary>FNV-1a over the text, spread once more: the low bits seed <see cref="System.Random"/>.</summary>
        private static uint Fnv(string text)
        {
            uint hash = 2166136261u;
            for (int i = 0; i < text.Length; i++)
            {
                hash = unchecked((hash ^ text[i]) * 16777619u);
            }

            hash ^= hash >> 15;
            hash = unchecked(hash * 0x2c1b3c6du);
            hash ^= hash >> 12;
            return hash;
        }
    }
}
