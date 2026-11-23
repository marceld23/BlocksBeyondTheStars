// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The plant forms beyond the first three families (<see cref="PlantForms"/>): blade tufts with seed
    /// heads, moss cushions, strands (arching on land, hanging from a ceiling, standing in water — the last two in
    /// segments that join across stacked cells), tube coral, the lily pad, the fruits under a crown and the sapling.
    /// With them every small plant of the game has a body. The same rule: form from the seed, no colour in the mesh.
    /// </summary>
    public static partial class PlantForms
    {
        private const int TuftNone = 0;  // blades only
        private const int TuftEar = 1;   // an ear of small balls on a stalk
        private const int TuftPlume = 2; // a feathery plume
        private const int TuftMace = 3;  // a reed mace: a dark club under the tip

        private const int StrandLeaves = 0;
        private const int StrandBeads = 1;
        private const int StrandRibbons = 2; // flat bands and no stem (seagrass)

        private const int FruitRound = 0;
        private const int FruitPod = 1;
        private const int FruitCluster = 2;
        private const int FruitBunch = 3;

        /// <summary>The height of the water surface in a water cell (ChunkMesher.WaterSurfaceInset below its top), where
        /// a lily pad floats.</summary>
        private const float WaterSurface = 0.86f;

        private static int MoreFamilyOf(string key)
        {
            switch (key)
            {
                case "flora_grasstuft":
                case "flora_saltgrass":
                case "flora_foxtail":
                case "flora_featherbush":
                case "flora_reed":
                case "flora_icereed":
                case "flora_cropgrain":
                case "chime_reed":
                    return Tuft;
                case "flora_moss":
                case "flora_lichen":
                case "flora_glowmoss":
                    return Cushion;
                case "flora_vine":
                case "flora_tendril":
                case "flora_droopbush":
                case "flora_kelp":
                case "flora_seagrass":
                case "flora_hangkelp":
                case "flora_glowthread":
                case "harp_vine":
                case "flora_glowvine":
                    return Strand;
                case "flora_coral":
                    return Coral;
                case "flora_lily":
                    return Lily;
                case "flora_fruit_round":
                case "flora_fruit_long":
                case "flora_fruit_grape":
                case "flora_fruit_banana":
                case "flora_fifi_berries":
                case "flora_lantern_fruit":
                    return Fruit;
                case "flora_thornbush":
                case "flora_cinderbush":
                case "flora_cropberry":
                    return Bush;
                case "flora_marigold":
                case "bell_bloom":
                case "bell_bloom_open":
                case "echo_flower":
                    return Flower;
                default:
                    return key != null && key.StartsWith("flora_sapling", System.StringComparison.Ordinal) ? Sapling : SolidFamilyOf(key);
            }
        }

        /// <summary>True for a strand species that stands or hangs straight — in water or from a ceiling — and so may be
        /// stacked over several cells: its body is one cell tall where the same plant goes on beyond it (individuals 2
        /// and 3) and ends in a tip where it does not (0 and 1). Every other strand arches over on land.</summary>
        public static bool StandsStraight(string key)
            => key == "flora_kelp" || key == "flora_seagrass" || key == "flora_hangkelp" || key == "flora_glowthread" || key == "harp_vine";

        private static int[] TuftHeadsOf(string key)
        {
            switch (key)
            {
                case "flora_foxtail":
                case "flora_cropgrain":
                case "chime_reed":
                    return new[] { TuftEar };
                case "flora_featherbush": return new[] { TuftPlume };
                case "flora_reed": return new[] { TuftMace };
                case "flora_icereed": return new[] { TuftMace, TuftNone };
                case "flora_saltgrass": return new[] { TuftNone };
                default: return new[] { TuftNone, TuftEar };
            }
        }

        private static int FruitKindOf(string key)
        {
            switch (key)
            {
                case "flora_fruit_long": return FruitPod;
                case "flora_fruit_grape":
                case "flora_fifi_berries":
                    return FruitCluster;
                case "flora_fruit_banana": return FruitBunch;
                default: return FruitRound;
            }
        }

        /// <summary>Builds one of the families of this file; returns how much the wind bows the plant (1 = a soft plant).</summary>
        private static float BuildMore(Shape s, int family, string key, System.Random species, System.Random own, bool tall,
            bool accent, int variant)
        {
            switch (family)
            {
                case Tuft:
                    BuildTuft(s, species, own, tall, TuftHeadsOf(key));
                    return 1f;
                case Cushion:
                    BuildCushion(s, species, own, key == "flora_lichen");
                    return 0.05f;
                case Strand:
                    int style = key == "flora_seagrass" ? StrandRibbons
                        : key == "flora_glowthread" || key == "harp_vine" || key == "flora_tendril" || key == "flora_glowvine" ? StrandBeads : StrandLeaves;
                    if (StandsStraight(key))
                    {
                        BuildStraightStrands(s, species, own, style, variant >= 2);
                        return 0.8f;
                    }

                    BuildArchingStrands(s, species, own, style, tall, key == "flora_tendril", key == "flora_droopbush");
                    return 1f;
                case Coral:
                    BuildCoral(s, species, own);
                    return 0.3f;
                case Lily:
                    BuildLily(s, species, own, accent && variant % 2 == 0);
                    return 0.2f;
                case Fruit:
                    BuildFruit(s, species, own, FruitKindOf(key));
                    return 0.5f;
                case Sapling:
                    BuildSapling(s, species, own, key == "flora_sapling_conifer");
                    return 0.7f;
                default:
                    return BuildSolid(s, family, key, species, own, accent);
            }
        }

        /// <summary>A tuft: thick blades out of one root, the inner ones upright and long, the outer ones arching over —
        /// and, by species, a few stalks that carry an ear, a plume or a reed mace above them.</summary>
        private static void BuildTuft(Shape s, System.Random species, System.Random own, bool tall, int[] heads)
        {
            int count = 9 + species.Next(8);
            float height = tall ? Range(species, 0.55f, 0.95f) : Range(species, 0.3f, 0.5f);
            float widthRatio = Range(species, 0.035f, 0.06f);
            float droop = Range(species, 0.25f, 1f);
            float fold = Range(species, 0.1f, 0.3f);
            int head = heads[species.Next(heads.Length)];
            int stalks = head == TuftNone ? 0 : 1 + species.Next(3);

            count = Mathf.Max(7, count + own.Next(5) - 2);
            float turn = Range(own, 0f, 6.2832f);
            AddTuftBlades(s, own, count, turn, height, widthRatio, droop, fold);
            for (int i = 0; i < stalks; i++)
            {
                float phi = turn + 1.3f + (i * 6.2832f / stalks) + Range(own, -0.4f, 0.4f);
                var dir = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
                float theta = Range(own, 0.03f, 0.18f);
                float stalkHeight = height * Range(own, 1f, 1.2f);
                var line = Curve(dir * 0.02f, dir, theta, 0.25f, stalkHeight, 5, out var tangent);
                var radius = new[] { 0.012f, 0.012f, 0.011f, 0.01f, 0.009f };
                float phase = Range(own, 0f, 1f);
                AddTube(s, line, radius, 5, 0.5f, 0.8f, phase);
                if (head == TuftEar)
                {
                    AddSpikeHead(s, own, line, 0.01f, 0.75f, phase);
                }
                else if (head == TuftPlume)
                {
                    var top = Frame.Along(line[line.Length - 1], tangent);
                    for (int k = 0; k < 6; k++)
                    {
                        AddLeaf(s, top, new Leaf
                        {
                            Phi = k * 1.0472f, Length = Range(own, 0.13f, 0.18f), HalfWidth = 0.018f, Theta = Range(own, 0.25f, 0.6f),
                            Droop = 0.6f, Fold = 0.2f, Second = 1f, Phase = phase, Tone = 1.05f, RootOffset = 0.004f, MaxReach = 1f,
                            RootTone = 0.7f,
                        });
                    }
                }
                else
                {
                    // The mace: a dark club around the stalk, which goes on a little above it.
                    Vector3 a = PointOn(line, 0.66f), b = PointOn(line, 0.72f), c = PointOn(line, 0.86f), d = PointOn(line, 0.92f);
                    AddTube(s, new[] { a, b, c, d }, new[] { 0.014f, 0.03f, 0.03f, 0.014f }, 6, 0.34f, 0.42f, phase);
                }
            }
        }

        /// <summary>A tuft's blades out of one root, spiralling round it: the inner ones upright and long, the outer ones
        /// shorter and arching over. They are rolled before the stalks.</summary>
        private static void AddTuftBlades(Shape s, System.Random own, int count, float turn, float height, float widthRatio,
            float droop, float fold)
        {
            var frame = Frame.Upright(Vector3.zero);
            for (int i = 0; i < count; i++)
            {
                float rank = i / (float)(count - 1);
                float len = height * Mathf.Lerp(1f, 0.6f, rank) * Range(own, 0.85f, 1.1f);
                AddLeaf(s, frame, new Leaf
                {
                    Phi = turn + (i * 2.39996f) + Range(own, -0.3f, 0.3f),
                    Length = len,
                    HalfWidth = Mathf.Max(0.016f, len * widthRatio),
                    Theta = Mathf.Lerp(0.06f, 0.55f, Mathf.Pow(rank, 0.9f)) + Range(own, -0.05f, 0.05f),
                    Droop = droop * Mathf.Lerp(0.6f, 1f, rank),
                    Fold = fold,
                    Second = own.NextDouble() < 0.35 ? Range(own, 0.4f, 0.9f) : 0f,
                    Phase = Range(own, 0f, 1f),
                    Tone = Range(own, 0.9f, 1.05f),
                    RootOffset = 0.01f + (0.03f * rank),
                    MaxReach = 0.42f,
                    OnGround = true,
                });
            }
        }

        /// <summary>A cushion: low round lumps close to the ground, the middle one the largest. A <paramref name="flat"/>
        /// one (lichen) is a crust with a lighter rim.</summary>
        private static void BuildCushion(Shape s, System.Random species, System.Random own, bool flat)
        {
            int lumps = 5 + species.Next(6);
            float spread = Range(species, 0.2f, 0.34f);
            float radius = Range(species, 0.07f, 0.12f);
            float rise = flat ? Range(species, 0.2f, 0.35f) : Range(species, 0.45f, 0.8f);
            float rim = flat ? Range(species, 0.5f, 1f) : Range(species, 0f, 0.4f);

            lumps = Mathf.Max(4, lumps + own.Next(3) - 1);
            for (int i = 0; i < lumps; i++)
            {
                float distance = i == 0 ? 0f : spread * Mathf.Sqrt(Range(own, 0.08f, 1f));
                float phi = Range(own, 0f, 6.2832f);
                float r = radius * (i == 0 ? 1.3f : Range(own, 0.7f, 1.15f));
                var centre = new Vector3(Mathf.Cos(phi) * distance, 0f, Mathf.Sin(phi) * distance);
                AddDome(s, centre, r, r * rise * Range(own, 0.8f, 1.2f), 7, 3, Range(own, 0.72f, 0.95f),
                    own.NextDouble() < 0.3 ? 0.6f : 0f, rim, Range(own, 0f, 1f));
            }
        }

        /// <summary>Strands that stand in water or hang from a ceiling (the instance turns a hanging one over): one to
        /// three round stems with leaves or beads, or a handful of flat bands. Every strand keeps its place and sways
        /// in a wave one cell long, both rolled per SPECIES — so a <paramref name="through"/> segment (exactly one cell
        /// tall) joins the segment in the next cell, and the last cell's strands end in tips.</summary>
        private static void BuildStraightStrands(Shape s, System.Random species, System.Random own, int style, bool through)
        {
            int count = style == StrandRibbons ? 3 + species.Next(3) : style == StrandLeaves ? 2 + species.Next(2) : 1 + species.Next(3);
            float radius = Range(species, 0.012f, 0.018f);
            float wave = Range(species, 0.025f, 0.055f);
            float step = Range(species, 0.16f, 0.24f) * (style == StrandLeaves ? 0.65f : 1f); // kelp is leafy
            float partSize = Range(species, 0.85f, 1.2f);
            for (int i = 0; i < count; i++)
            {
                float place = Range(species, 0f, 6.2832f), away = count == 1 ? 0f : Range(species, 0.04f, 0.13f);
                float waveTurn = Range(species, 0f, 6.2832f), wavePhase = Range(species, 0f, 6.2832f);
                var offset = new Vector3(Mathf.Cos(place) * away, 0f, Mathf.Sin(place) * away);
                var waveDir = new Vector3(Mathf.Cos(waveTurn), 0f, Mathf.Sin(waveTurn));
                float length = through ? 1f : Range(own, 0.55f, 0.95f);
                float phase = Range(own, 0f, 1f);
                var path = new StrandWave(offset, waveDir, wave, wavePhase);

                const int Points = 5;
                var line = new Vector3[Points];
                var radii = new float[Points];
                for (int p = 0; p < Points; p++)
                {
                    float t = p / (float)(Points - 1);
                    line[p] = path.At(length * t);
                    radii[p] = radius * (through ? 1f : Mathf.Lerp(1f, 0.5f, t));
                }

                if (style == StrandRibbons)
                {
                    var across = new Vector3(-waveDir.z, 0f, waveDir.x);
                    var widths = new float[Points];
                    for (int p = 0; p < Points; p++)
                    {
                        widths[p] = Range(species, 0.9f, 1.1f) * 0.03f * partSize * (through || p < Points - 1 ? 1f : 0.2f);
                    }

                    AddRibbon(s, line, widths, across, 0.5f, 0.92f, i % 2 == 0 ? 0f : 0.5f, phase);
                    continue;
                }

                AddTube(s, line, radii, 5, 0.62f, 0.9f, phase, dark: 0.3f, flutter: 0.6f); // a light stem: seen through water it must not go black
                AddStraightStrandParts(s, own, path, style, length, step, waveTurn, radius, partSize, phase);
            }
        }

        /// <summary>The path a straight strand sways along: its place beside the plant's axis and a sideways wave one
        /// cell long (both rolled per species, so stacked segments join).</summary>
        private readonly struct StrandWave
        {
            private readonly Vector3 _offset;
            private readonly Vector3 _dir;
            private readonly float _amplitude;
            private readonly float _phase;

            public StrandWave(Vector3 offset, Vector3 dir, float amplitude, float phase)
            {
                _offset = offset;
                _dir = dir;
                _amplitude = amplitude;
                _phase = phase;
            }

            /// <summary>The strand's point at height <paramref name="y"/>.</summary>
            public Vector3 At(float y) => _offset + (_dir * (_amplitude * Mathf.Sin((6.2832f * y) + _phase))) + new Vector3(0f, y, 0f);
        }

        /// <summary>The beads or leaves up a straight strand, one every <paramref name="step"/>, turning round it.</summary>
        private static void AddStraightStrandParts(Shape s, System.Random own, StrandWave path, int style, float length, float step,
            float waveTurn, float radius, float partSize, float phase)
        {
            int index = 0;
            for (float y = step * 0.5f; y < length - 0.04f; y += step, index++)
            {
                var at = path.At(y);
                var along = (path.At(y + 0.02f) - at).normalized;
                var frame = Frame.Along(at, along);
                float phi = (index * 2.4f) + waveTurn;
                if (style == StrandBeads)
                {
                    float bead = 0.028f * partSize * Range(own, 0.8f, 1.1f);
                    AddBall(s, at + (frame.Out(phi) * (radius + (bead * 0.8f))), bead, 5, 3, Range(own, 0.8f, 1f), 0.2f, 1f,
                        Frac(phase + (0.09f * index)), 0.6f);
                }
                else
                {
                    float len = 0.19f * partSize * Range(own, 0.85f, 1.15f);
                    AddLeaf(s, frame, new Leaf
                    {
                        Phi = phi, Length = len, HalfWidth = len * 0.34f, Theta = Range(own, 1f, 1.3f), Droop = 0.3f, Fold = 0.25f,
                        Second = y / Mathf.Max(length, 0.1f) > 0.7f ? 0.6f : 0f, Phase = Frac(phase + (0.09f * index)), Tone = 1f,
                        RootOffset = radius * 0.6f, MaxReach = 1f,
                    });
                }
            }
        }

        /// <summary>Strands on land: a few round stems out of one root that rise and bend over — a vine arches, a
        /// weeping bush hangs its ends to the ground, a tendril curls up — with leaves or beads along them.</summary>
        private static void BuildArchingStrands(Shape s, System.Random species, System.Random own, int style, bool tall, bool curl,
            bool weeping)
        {
            int count = weeping ? 4 + species.Next(3) : 2 + species.Next(3);
            float length = tall ? Range(species, 0.9f, 1.4f) : Range(species, 0.55f, 0.85f);
            float arch = curl ? Range(species, 3.6f, 5.2f) : weeping ? Range(species, 2.2f, 2.9f) : Range(species, 1.3f, 2.1f);
            float radius = Range(species, 0.012f, 0.017f);
            float step = Range(species, 0.13f, 0.2f);
            float partSize = Range(species, 0.85f, 1.2f);

            float turn = Range(own, 0f, 6.2832f);
            for (int i = 0; i < count; i++)
            {
                float phi = turn + (i * 6.2832f / count) + Range(own, -0.3f, 0.3f);
                var dir = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
                float theta = Range(own, 0.08f, 0.3f);
                float len = length * Range(own, 0.8f, 1.1f);

                // The stem's path; one that reaches out of the cell is drawn smaller as a whole.
                const int Points = 8;
                var line = new Vector3[Points];
                var at = dir * 0.025f;
                float reach = 0f;
                for (int p = 0; p < Points; p++)
                {
                    float t = p / (float)(Points - 1);
                    line[p] = at;
                    reach = Mathf.Max(reach, new Vector2(at.x, at.z).magnitude);
                    float angle = theta + (arch * Mathf.Pow(t, 1.6f));
                    at += ((dir * Mathf.Sin(angle)) + (Vector3.up * Mathf.Cos(angle))) * (len / (Points - 1));
                }

                float fit = reach > 0.42f ? 0.42f / reach : 1f;
                var radii = new float[Points];
                for (int p = 0; p < Points; p++)
                {
                    line[p] = new Vector3(line[p].x * fit, Mathf.Max(line[p].y * fit, 0.03f), line[p].z * fit);
                    radii[p] = radius * Mathf.Lerp(1f, 0.55f, p / (float)(Points - 1));
                }

                float phase = Range(own, 0f, 1f);
                AddTube(s, line, radii, 5, 0.5f, 0.8f, phase, dark: 0.6f, flutter: 0.8f);
                int parts = Mathf.Max(2, Mathf.RoundToInt(len * fit / step));
                AddArchingStrandParts(s, own, line, style, parts, radius, partSize, phase);
            }
        }

        /// <summary>The <paramref name="parts"/> beads or leaves along an arching strand's stem, spread evenly from just
        /// above its root to just before its end; the last quarter's leaves are young.</summary>
        private static void AddArchingStrandParts(Shape s, System.Random own, Vector3[] line, int style, int parts, float radius,
            float partSize, float phase)
        {
            for (int k = 0; k < parts; k++)
            {
                float t = (k + 0.7f) / (parts + 0.4f);
                var point = PointOn(line, t);
                var along = (PointOn(line, Mathf.Min(1f, t + 0.04f)) - point).normalized;
                var frame = Frame.Along(point, along);
                if (style == StrandBeads)
                {
                    float bead = 0.028f * partSize * Range(own, 0.8f, 1.1f);
                    AddBall(s, point + (frame.Out(k * 2.4f) * (radius + (bead * 0.8f))), bead, 5, 3, Range(own, 0.8f, 1f), 0.2f, 1f,
                        Frac(phase + (0.09f * k)), 0.6f);
                }
                else
                {
                    float leaf = 0.14f * partSize * Range(own, 0.85f, 1.15f);
                    AddLeaf(s, frame, new Leaf
                    {
                        Phi = k * 2.4f, Length = leaf, HalfWidth = leaf * 0.3f, Theta = Range(own, 0.9f, 1.3f), Droop = 0.3f,
                        Fold = 0.25f, Second = t > 0.75f ? 0.6f : 0f, Phase = Frac(phase + (0.09f * k)), Tone = 1f,
                        RootOffset = radius * 0.6f, MaxReach = 1f,
                    });
                }
            }
        }

        /// <summary>Tube coral: smooth fingers with round tips out of one foot, some with a side branch; the colour
        /// runs from the main colour at the foot to the second at the tips.</summary>
        private static void BuildCoral(Shape s, System.Random species, System.Random own)
        {
            int fingers = 4 + species.Next(5);
            float height = Range(species, 0.28f, 0.6f);
            float radius = Range(species, 0.03f, 0.05f);
            float spread = Range(species, 0.15f, 0.6f);
            bool branches = species.NextDouble() < 0.6;

            fingers = Mathf.Max(3, fingers + own.Next(3) - 1);
            float turn = Range(own, 0f, 6.2832f);
            for (int i = 0; i < fingers; i++)
            {
                float phi = turn + (i * 6.2832f / fingers) + Range(own, -0.3f, 0.3f);
                var dir = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
                float r = radius * Range(own, 0.8f, 1.1f);
                var line = Curve(dir * Range(own, 0.02f, 0.12f), dir, spread * Range(own, 0.3f, 1f), Range(own, -0.3f, 0.3f),
                    height * Range(own, 0.55f, 1.05f), 4, out _);
                Fit(line, 0.42f - r);
                float phase = Range(own, 0f, 1f);
                AddTube(s, line, new[] { r * 1.05f, r, r * 0.95f, r * 0.9f }, 6, 0.5f, 0.92f, phase, dark: 0f, secondTip: 0.9f, flutter: 0.3f);
                AddBall(s, line[3], r * 0.95f, 6, 4, 0.95f, 1f, 0f, phase, 0.3f);
                if (branches && i % 2 == 0)
                {
                    var from = Vector3.Lerp(line[1], line[2], 0.5f);
                    var side = new Vector3(Mathf.Cos(phi + 1.4f), 0f, Mathf.Sin(phi + 1.4f));
                    float length = (line[3] - line[0]).magnitude * 0.45f;
                    var branch = new[] { from, from + (side * (length * 0.45f)) + (Vector3.up * (length * 0.4f)), from + (side * (length * 0.6f)) + (Vector3.up * length) };
                    Fit(branch, 0.44f - r);
                    AddTube(s, branch, new[] { r * 0.8f, r * 0.75f, r * 0.7f }, 6, 0.6f, 0.92f, phase, dark: 0f, secondRoot: 0.3f, secondTip: 0.9f, flutter: 0.3f);
                    AddBall(s, branch[2], r * 0.74f, 6, 4, 0.95f, 1f, 0f, phase, 0.3f);
                }
            }
        }

        /// <summary>A water lily: a round pad with a notch floating on the water surface — its cell is the top water cell,
        /// so the pad lies a little under the cell's top — on a thin stem to the bed, a smaller pad beside it now and then,
        /// and on every other individual of an accent species a cup blossom.</summary>
        private static void BuildLily(Shape s, System.Random species, System.Random own, bool blossom)
        {
            float radius = Range(species, 0.24f, 0.34f);
            var centre = new Vector3(0f, WaterSurface, 0f);
            float phase = Range(own, 0f, 1f);
            AddPad(s, centre, radius, Range(own, 0f, 6.2832f), 0f, phase);
            if (own.NextDouble() < 0.6)
            {
                float phi = Range(own, 0f, 6.2832f);
                AddPad(s, centre + new Vector3(Mathf.Cos(phi) * radius * 0.75f, -0.004f, Mathf.Sin(phi) * radius * 0.75f), radius * 0.5f,
                    phi + 3f, 0.5f, Frac(phase + 0.4f));
            }

            AddTube(s, new[] { Vector3.zero, new Vector3(0.03f, WaterSurface * 0.5f, 0.02f), new Vector3(0f, WaterSurface - 0.01f, 0f) },
                new[] { 0.012f, 0.01f, 0.01f }, 5, 0.4f, 0.6f, phase);
            if (blossom)
            {
                AddCupHead(s, own, Frame.Upright(centre + new Vector3(0f, 0.012f, 0f)), 0.95f, phase);
            }
        }

        /// <summary>A fruit under a crown (the instance turns it over: its root is where it hangs): a short stalk and a
        /// ball, a long pod, a cluster of small balls or a bunch of bent fingers. All in the main colour slot — a tree
        /// fruit's colour is its cell's own.</summary>
        private static void BuildFruit(Shape s, System.Random species, System.Random own, int kind)
        {
            float stalk = Range(species, 0.1f, 0.18f);
            float size = Range(species, 0.85f, 1.15f);
            float sway = Range(own, -0.04f, 0.04f);
            var top = new Vector3(sway, stalk, sway * 0.5f);
            float phase = Range(own, 0f, 1f);
            AddTube(s, new[] { Vector3.zero, top * 0.5f, top }, new[] { 0.012f, 0.01f, 0.01f }, 5, 0.4f, 0.55f, phase);
            if (kind == FruitPod)
            {
                float r = 0.05f * size, stretch = Range(species, 2.4f, 3.2f);
                AddBall(s, top + new Vector3(0f, r * stretch * 0.95f, 0f), r, 7, 5, 0.9f, 0f, 0f, phase, 0.3f, stretch);
            }
            else if (kind == FruitCluster)
            {
                float r = 0.04f * size;
                int[] rows = { 4, 3, 3, 1 };
                for (int row = 0; row < rows.Length; row++)
                {
                    float ring = rows[row] == 1 ? 0f : r * 1.15f * (rows[row] / 4f);
                    for (int k = 0; k < rows[row]; k++)
                    {
                        float phi = (k * 6.2832f / rows[row]) + (row * 0.8f);
                        var at = top + new Vector3(Mathf.Cos(phi) * ring, r * (0.8f + (1.45f * row)), Mathf.Sin(phi) * ring);
                        AddBall(s, at, r * Range(own, 0.9f, 1.05f), 6, 3, Range(own, 0.8f, 1f), 0f, 0f, Frac(phase + (0.05f * k)), 0.3f);
                    }
                }
            }
            else if (kind == FruitBunch)
            {
                int fingers = 3 + own.Next(3);
                float length = 0.24f * size;
                for (int k = 0; k < fingers; k++)
                {
                    float phi = (k - ((fingers - 1) * 0.5f)) * 0.7f;
                    var dir = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
                    var line = Curve(top, dir, 1.1f, -1f, length, 4, out _);
                    AddTube(s, line, new[] { 0.018f, 0.03f, 0.03f, 0.016f }, 6, 0.8f, 1f, phase, dark: 0f, flutter: 0.2f);
                }
            }
            else
            {
                float r = 0.1f * size;
                AddBall(s, top + new Vector3(0f, r * 0.95f, 0f), r, 8, 5, 0.9f, 0f, 0f, phase, 0.3f);
            }
        }

        /// <summary>A sapling: a thin stem with leaves up its upper half, the top ones young — or, for a conifer, three
        /// tiers of needles that get shorter toward the tip.</summary>
        private static void BuildSapling(Shape s, System.Random species, System.Random own, bool conifer)
        {
            float height = Range(species, 0.38f, 0.58f);
            int leaves = 5 + species.Next(4);
            float leafLength = Range(species, 0.14f, 0.2f);
            float phi = Range(own, 0f, 6.2832f);
            var dir = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
            var line = Curve(Vector3.zero, dir, Range(own, 0.02f, 0.15f), 0.2f, height * Range(own, 0.9f, 1.1f), 4, out var tangent);
            float phase = Range(own, 0f, 1f);
            AddTube(s, line, new[] { 0.016f, 0.014f, 0.012f, 0.009f }, 6, 0.42f, 0.62f, phase);
            if (conifer)
            {
                for (int tier = 0; tier < 3; tier++)
                {
                    float t = 0.45f + (0.235f * tier);
                    var frame = Frame.Along(PointOn(line, t), tangent);
                    float len = Mathf.Lerp(0.2f, 0.09f, tier / 2f);
                    for (int k = 0; k < 5; k++)
                    {
                        AddLeaf(s, frame, new Leaf
                        {
                            Phi = (k * 1.2566f) + (tier * 0.6f), Length = len * Range(own, 0.9f, 1.1f), HalfWidth = Mathf.Max(0.012f, len * 0.1f),
                            Theta = 1.25f, Droop = 0.5f, Fold = 0.3f, Second = tier == 2 ? 0.6f : 0f, Phase = Frac(phase + (0.1f * k)),
                            Tone = 1f, RootOffset = 0.008f, MaxReach = 1f,
                        });
                    }
                }

                AddLeaf(s, Frame.Along(line[3], tangent), new Leaf
                {
                    Length = 0.09f, HalfWidth = 0.012f, Theta = 0.05f, Fold = 0.3f, Second = 0.8f, Phase = phase, Tone = 1f, MaxReach = 1f,
                });
                return;
            }

            for (int k = 0; k < leaves; k++)
            {
                float t = 0.45f + (0.55f * k / (leaves - 1));
                float len = leafLength * (1f - (0.25f * t)) * Range(own, 0.9f, 1.1f);
                AddLeaf(s, Frame.Along(PointOn(line, t), tangent), new Leaf
                {
                    Phi = k * 2.4f, Length = len, HalfWidth = len * 0.32f, Theta = Mathf.Lerp(1.2f, 0.5f, t), Droop = 0.4f, Fold = 0.3f,
                    Second = t > 0.8f ? 0.8f : 0f, Phase = Frac(phase + (0.1f * k)), Tone = 1f, RootOffset = 0.008f, MaxReach = 1f,
                });
            }
        }

        // --- More parts ---

        /// <summary>A line that starts at <paramref name="from"/>, leans by <paramref name="theta"/> from upright toward
        /// <paramref name="dir"/> and bends on by <paramref name="bend"/> to its end, <paramref name="length"/> long.</summary>
        private static Vector3[] Curve(Vector3 from, Vector3 dir, float theta, float bend, float length, int points, out Vector3 tangent)
        {
            var line = new Vector3[points];
            var at = from;
            tangent = Vector3.up;
            for (int p = 0; p < points; p++)
            {
                float t = p / (float)(points - 1);
                line[p] = at;
                float angle = theta + (bend * t * t);
                tangent = (dir * Mathf.Sin(angle)) + (Vector3.up * Mathf.Cos(angle));
                at += tangent * (length / (points - 1));
            }

            return line;
        }

        /// <summary>The point at share <paramref name="t"/> of a line's points.</summary>
        private static Vector3 PointOn(Vector3[] line, float t)
        {
            float at = Mathf.Clamp01(t) * (line.Length - 1);
            int segment = Mathf.Min((int)at, line.Length - 2);
            return Vector3.Lerp(line[segment], line[segment + 1], at - segment);
        }

        /// <summary>Pulls a line toward the plant's axis so that it stays within <paramref name="reach"/> of it.</summary>
        private static void Fit(Vector3[] line, float reach)
        {
            float far = 0f;
            for (int i = 0; i < line.Length; i++)
            {
                far = Mathf.Max(far, new Vector2(line[i].x, line[i].z).magnitude);
            }

            if (far <= reach)
            {
                return;
            }

            float scale = reach / far;
            for (int i = 0; i < line.Length; i++)
            {
                line[i] = new Vector3(line[i].x * scale, line[i].y, line[i].z * scale);
            }
        }

        /// <summary>A low dome (a moss lump): rows of vertices from its foot up to one at the top, the normals those of the
        /// flattened ball it is the upper half of. Its foot is darker, and wears <paramref name="rim"/> of the second colour.</summary>
        private static void AddDome(Shape s, Vector3 centre, float radius, float height, int segments, int rings, float tone,
            float second, float rim, float phase)
        {
            int first = s.Count;
            for (int row = 0; row < rings; row++)
            {
                float angle = row / (float)rings * (Mathf.PI * 0.5f);
                float flat = Mathf.Cos(angle), up = Mathf.Sin(angle);
                float share = row / (float)rings;
                for (int k = 0; k < segments; k++)
                {
                    float lon = (k * 6.2832f / segments) + (row * 0.3f);
                    var at = centre + new Vector3(flat * Mathf.Cos(lon) * radius, up * height, flat * Mathf.Sin(lon) * radius);
                    var normal = new Vector3(flat * Mathf.Cos(lon) / radius, up / height, flat * Mathf.Sin(lon) / radius).normalized;
                    s.Add(at, normal, tone - (0.25f * (1f - share)), Mathf.Lerp(Mathf.Max(second, rim), second, share), 0f, phase, 0f, 0.2f,
                        0f, centre);
                }
            }

            int topIndex = s.Count;
            s.Add(centre + new Vector3(0f, height, 0f), Vector3.up, tone + 0.05f, second, 0f, phase, 0f, 0.2f, 0f, centre);
            for (int k = 0; k < segments; k++)
            {
                int next = (k + 1) % segments;
                for (int row = 0; row < rings - 1; row++)
                {
                    int lower = first + (row * segments), upper = lower + segments;
                    s.Quad(lower + k, lower + next, upper + next, upper + k);
                }

                int last = first + ((rings - 1) * segments);
                s.Tri(topIndex, last + next, last + k);
            }
        }

        /// <summary>A flat band along a line (a seagrass blade), <paramref name="across"/> its width's direction.</summary>
        private static void AddRibbon(Shape s, Vector3[] line, float[] halfWidth, Vector3 across, float toneRoot, float toneTip,
            float second, float phase)
        {
            int first = s.Count;
            for (int p = 0; p < line.Length; p++)
            {
                float t = p / (float)(line.Length - 1);
                var tangent = (line[Mathf.Min(p + 1, line.Length - 1)] - line[Mathf.Max(p - 1, 0)]).normalized;
                var normal = Vector3.Cross(tangent, across).normalized;
                float tone = Mathf.Lerp(toneRoot, toneTip, t);
                s.Add(line[p] - (across * halfWidth[p]), normal, tone, second, 0f, phase, 0f, 1f, t * t, line[p]);
                s.Add(line[p] + (across * halfWidth[p]), normal, tone, second, 0f, phase, 0f, 1f, t * t, line[p]);
            }

            for (int p = 0; p < line.Length - 1; p++)
            {
                int lower = first + (p * 2);
                s.Quad(lower, lower + 1, lower + 3, lower + 2);
            }
        }

        /// <summary>A lily pad: a round leaf lying flat, a notch cut into it, its rim a little raised and lighter.</summary>
        private static void AddPad(Shape s, Vector3 centre, float radius, float notch, float second, float phase)
        {
            const int Rim = 12;
            int first = s.Count;
            s.Add(centre, Vector3.up, 0.72f, second, 0f, phase, 0f, 1f, 0f, centre);
            for (int k = 0; k < Rim; k++)
            {
                float angle = notch + 0.35f + (k * (6.2832f - 0.7f) / (Rim - 1));
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                s.Add(centre + (outward * radius) + new Vector3(0f, 0.012f, 0f), (Vector3.up - (outward * 0.25f)).normalized, 0.92f, second, 0f,
                    phase, 0f, 1f, 0.3f, centre);
            }

            for (int k = 0; k < Rim - 1; k++)
            {
                s.Tri(first, first + 1 + k, first + 2 + k);
            }
        }
    }
}
