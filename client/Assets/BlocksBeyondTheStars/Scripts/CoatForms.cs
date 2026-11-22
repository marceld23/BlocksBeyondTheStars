// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Fur as geometry. A creature's box part grows a coat of real blades: short felt everywhere, long
    /// hanging strands at the places a species rolled (mane, ridge, chest, tail tuft, fetlocks, beard), flat tabs for a
    /// bird's plumage. One mesh per part, in the part's own unit-cube space (it hangs under the scaled cube), so every
    /// animal of a species shares it.
    /// <para>
    /// The form carries no colour: a vertex holds a brightness, an accent weight, a dark weight and a bend weight, and
    /// the texture coordinate of the coat right under the blade's root — <c>CreatureCoat.shader</c> reads the animal's
    /// own coat there, so stripes, spots, the mood tint and the glow pattern reach the fur by themselves.
    /// </para>
    /// <para>
    /// Kept free of everything but vectors, colours, lists and <see cref="Mesh"/>, so the file compiles outside Unity.
    /// The feathers live in <c>CoatForms.Feathers.cs</c>.
    /// </para>
    /// </summary>
    public static partial class CoatForms
    {
        /// <summary>The kinds of body cover.</summary>
        public const int Felt = 0, Bristle = 1, Shaggy = 2, Plumage = 3;

        /// <summary>The places a species may wear long strands (bit flags).</summary>
        public const int Mane = 1, Ridge = 2, Chest = 4, TailTuft = 8, Fetlock = 16, Beard = 32;

        /// <summary>What a box is on the body: it decides the lie of the hair and where strands grow.</summary>
        public const int Torso = 0, Head = 1, Jaw = 2, Thigh = 3, Shin = 4, Tail = 5, Belly = 6, Slab = 7, Paw = 8;

        /// <summary>Faces of a part cube in <see cref="TiledCube"/>'s order, as bits of <see cref="Box.Skip"/>.</summary>
        public const int FaceRight = 1, FaceLeft = 2, FaceTop = 4, FaceBottom = 8, FaceFront = 16, FaceBack = 32;

        private const int MaxVertices = 60000; // a part's coat stays inside a 16-bit index buffer
        private const float BrightScale = 1.25f; // the shader multiplies a vertex's brightness by this

        /// <summary>One box part and how it is to be covered.</summary>
        public struct Box
        {
            /// <summary>The part's size in metres at the SPECIES' size (not the individual's), so one mesh serves all.</summary>
            public Vector3 Size;

            /// <summary>The species' unit (half its size): blade sizes follow it.</summary>
            public float Unit;

            public int Kind;
            public int Zone;

            /// <summary>The front-most torso segment / the last tail link.</summary>
            public bool First, Last;

            /// <summary>The species' strand places (<see cref="Mane"/> …).</summary>
            public int Styles;

            public uint Seed;

            /// <summary>1 = the full coat, 0.5 = half the blades (the Medium preset).</summary>
            public float Density;

            /// <summary>The tiled cube's repeats and pinning — the roots take the coat's texture coordinate from it.</summary>
            public int Rx, Ry, Rz;
            public bool PinV;

            /// <summary>Faces that stay bare (hidden against a neighbour): <see cref="FaceRight"/> ….</summary>
            public int Skip;

            /// <summary>The radius of the part's rounded edges in metres at the species' size (0 = a sharp box): the
            /// coat follows the rounded surface (<see cref="RoundedCube"/>).</summary>
            public float Round;

            /// <summary>The eyes on the head's front face (x, y in the part's unit-cube space) and their radius there.</summary>
            public Vector2[] Eyes;
            public float EyeRadius;
        }

        /// <summary>A coat being built: plain lists, turned into a mesh at the end.</summary>
        public sealed class Shape
        {
            public readonly List<Vector3> Verts = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Color> Colors = new List<Color>();
            public readonly List<Vector4> Coat = new List<Vector4>(); // xy = coat texture coordinate, z = phase, w = 1 felt (fades with range)
            public readonly List<Vector4> Root = new List<Vector4>(); // xyz = the blade's root (object space), w = a feather's fold angle
            public readonly List<int> Tris = new List<int>();

            /// <summary>The longest blade in object units — the mesh bounds grow by it, so the wind never culls a coat.</summary>
            public float Reach;

            public int Blades;

            public int Add(Vector3 p, Vector3 n, Color tone, Vector4 coat, Vector4 root)
            {
                Verts.Add(p);
                Normals.Add(n);
                Colors.Add(tone);
                Coat.Add(coat);
                Root.Add(root);
                return Verts.Count - 1;
            }

            public void Tri(int a, int b, int c)
            {
                Tris.Add(a);
                Tris.Add(b);
                Tris.Add(c);
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(Verts);
                mesh.SetNormals(Normals);
                mesh.SetColors(Colors);
                mesh.SetUVs(0, Coat);
                mesh.SetUVs(1, Root);
                mesh.SetTriangles(Tris, 0);
                var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                for (int i = 0; i < Verts.Count; i++)
                {
                    var p = Verts[i];
                    min = new Vector3(Mathf.Min(min.x, p.x), Mathf.Min(min.y, p.y), Mathf.Min(min.z, p.z));
                    max = new Vector3(Mathf.Max(max.x, p.x), Mathf.Max(max.y, p.y), Mathf.Max(max.z, p.z));
                }

                if (Verts.Count == 0)
                {
                    min = max = Vector3.zero;
                }

                float grow = 2f * Reach;
                mesh.bounds = new Bounds((min + max) * 0.5f, (max - min) + new Vector3(grow, grow, grow));
                return mesh;
            }
        }

        /// <summary>The strand places of a species: two or three of the six, by its seed. A shaggy coat wears strands all
        /// over anyway and keeps one or two accents.</summary>
        public static int StylesFor(uint seed, int kind)
        {
            if (kind == Plumage)
            {
                return 0;
            }

            var rng = new System.Random(unchecked((int)(seed * 2654435761u) ^ 0x51ab3));
            int wanted = kind == Shaggy ? 1 + rng.Next(2) : 2 + rng.Next(2);
            int styles = 0;
            for (int guard = 0; guard < 24 && Count(styles) < wanted; guard++)
            {
                styles |= 1 << rng.Next(6);
            }

            return styles;
        }

        private static int Count(int bits)
        {
            int n = 0;
            for (; bits != 0; bits &= bits - 1)
            {
                n++;
            }

            return n;
        }

        /// <summary>The length of a felt blade and of a strand in metres for a species of <paramref name="unit"/>: a
        /// big animal wears longer hair, but less than in proportion.</summary>
        public static float FeltLength(float unit) => 0.026f * Mathf.Pow(Mathf.Max(0.05f, unit) / 0.5f, 0.7f);

        public static float StrandLength(float unit) => 0.105f * Mathf.Pow(Mathf.Max(0.05f, unit) / 0.5f, 0.8f);

        private static float FeltSpacing(float unit) => 0.016f * Mathf.Pow(Mathf.Max(0.05f, unit) / 0.5f, 0.8f);

        /// <summary>The coat of one box: felt on every open face (<paramref name="felt"/>) and strands where the
        /// species wears them (<paramref name="strands"/>). The two are asked for separately: the felt is switched off
        /// beyond its range, a mane stays.</summary>
        public static Shape Fur(in Box box, bool felt = true, bool strands = true)
        {
            var shape = new Shape();
            var data = TiledCube.Build(box.Rx, box.Ry, box.Rz, box.PinV);
            float density = Mathf.Max(0.1f, box.Density);
            float spacing = FeltSpacing(box.Unit) / Mathf.Sqrt(density);
            float area = 2f * ((box.Size.x * box.Size.y) + (box.Size.y * box.Size.z) + (box.Size.x * box.Size.z));
            int maxBlades = MaxVertices / (box.Kind == Plumage ? 5 : 3);
            if (area / (spacing * spacing) > maxBlades)
            {
                spacing = Mathf.Sqrt(area / maxBlades);
            }

            for (int face = 0; face < 6; face++)
            {
                if ((box.Skip & (1 << face)) != 0)
                {
                    continue;
                }

                var f = new Face(data, face, box.Size, box.Round);
                var rng = new System.Random(unchecked((int)(box.Seed ^ (uint)((box.Zone * 7919) + (face * 104729) + (box.First ? 31 : 0) + (box.Last ? 67 : 0)))));
                if (felt)
                {
                    FeltFace(shape, box, f, spacing, rng);
                }

                if (strands)
                {
                    StrandFace(shape, box, f, spacing * 1.9f, rng);
                }
            }

            return shape;
        }

        /// <summary>One face of the tiled cube: its corner, edges and normal in the unit cube, its texture coordinates,
        /// and the part's size to turn metres into unit-cube offsets.</summary>
        private readonly struct Face
        {
            public readonly int Index;
            public readonly Vector3 P00, EdgeU, EdgeV, Normal, Size;
            public readonly Vector2 Uv00, UvU, UvV;
            public readonly float LenU, LenV, Radius;

            public Face(TiledCube.Data d, int face, Vector3 size, float radius)
            {
                Index = face;
                Size = size;
                Radius = radius;
                int v = face * 4;
                P00 = Pos(d, v);
                EdgeU = Pos(d, v + 1) - P00;
                EdgeV = Pos(d, v + 2) - P00;
                Normal = new Vector3(d.Normals[v * 3], d.Normals[(v * 3) + 1], d.Normals[(v * 3) + 2]);
                Uv00 = new Vector2(d.Uvs[v * 2], d.Uvs[(v * 2) + 1]);
                UvU = new Vector2(d.Uvs[(v + 1) * 2] - Uv00.x, d.Uvs[((v + 1) * 2) + 1] - Uv00.y);
                UvV = new Vector2(d.Uvs[(v + 2) * 2] - Uv00.x, d.Uvs[((v + 2) * 2) + 1] - Uv00.y);
                LenU = Scale(EdgeU, size).magnitude;
                LenV = Scale(EdgeV, size).magnitude;
            }

            private static Vector3 Pos(TiledCube.Data d, int v)
                => new Vector3(d.Positions[v * 3], d.Positions[(v * 3) + 1], d.Positions[(v * 3) + 2]);

            public Vector3 Point(float a, float b) => P00 + (EdgeU * a) + (EdgeV * b);

            /// <summary>The point of the part's real surface over <see cref="Point"/> (unit cube) and the surface's
            /// normal there (world): on a rounded part the rim of a face bends round the edge.</summary>
            public Vector3 Surface(float a, float b, out Vector3 normal)
            {
                var flat = Point(a, b);
                if (Radius <= 0f)
                {
                    normal = Normal;
                    return flat;
                }

                var at = RoundedCube.Round(Scale(flat, Size), Size * 0.5f, Radius, out normal);
                return new Vector3(at.x / Size.x, at.y / Size.y, at.z / Size.z);
            }

            public Vector2 Uv(float a, float b) => new Vector2(Uv00.x + (UvU.x * a) + (UvV.x * b), Uv00.y + (UvU.y * a) + (UvV.y * b));

            /// <summary>A world-space offset (metres) as an offset in the unit cube.</summary>
            public Vector3 ToCube(Vector3 metres) => new Vector3(metres.x / Size.x, metres.y / Size.y, metres.z / Size.z);

            /// <summary>A world-space normal as the object-space normal that the scaled cube turns back into it.</summary>
            public Vector3 NormalToCube(Vector3 n) => Scale(n, Size).normalized;
        }

        private static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);

        /// <summary>The lie of the hair on a face: the zone's stroke (back and down on a body, down on a leg) laid into
        /// the face's plane.</summary>
        private static Vector3 Grain(int zone, Vector3 normal)
        {
            var stroke = zone == Thigh || zone == Shin ? new Vector3(0f, -0.94f, -0.34f) : new Vector3(0f, -0.55f, -0.83f);
            var flat = stroke - (normal * Vector3.Dot(stroke, normal));
            if (flat.sqrMagnitude < 0.04f)
            {
                flat = new Vector3(0f, -1f, 0f) - (normal * Vector3.Dot(new Vector3(0f, -1f, 0f), normal));
            }

            if (flat.sqrMagnitude < 0.04f)
            {
                flat = new Vector3(0f, 0f, -1f);
            }

            return flat.normalized;
        }

        private static float Next(System.Random rng, float min, float max) => min + ((float)rng.NextDouble() * (max - min));

        /// <summary>Turns <paramref name="v"/> about the unit axis <paramref name="axis"/> (v is perpendicular to it).</summary>
        private static Vector3 Twist(Vector3 v, Vector3 axis, float radians)
            => (v * Mathf.Cos(radians)) + (Vector3.Cross(axis, v) * Mathf.Sin(radians));

        // ------------------------------------------------------------------------------------------------ felt

        private static void FeltFace(Shape shape, in Box box, in Face f, float spacing, System.Random rng)
        {
            // Under a shaggy coat's strands the felt may be thinner; a plumage's tabs are larger than a blade.
            float share = box.Kind == Shaggy ? 0.75f : box.Kind == Plumage ? 0.55f : 1f;
            float step = spacing / Mathf.Sqrt(share);
            int nu = Mathf.Max(1, Mathf.RoundToInt(f.LenU / step));
            int nv = Mathf.Max(1, Mathf.RoundToInt(f.LenV / step));
            float length = FeltLength(box.Unit) * ZoneLength(box.Zone);
            for (int iv = 0; iv < nv; iv++)
            {
                for (int iu = 0; iu < nu; iu++)
                {
                    float a = (iu + Next(rng, 0.08f, 0.92f)) / nu;
                    float b = (iv + Next(rng, 0.08f, 0.92f)) / nv;
                    float face = FaceFactor(box, f, a, b);
                    if (face <= 0f)
                    {
                        continue;
                    }

                    FeltBlade(shape, box.Kind, box.Zone, f, a, b, length * face * Next(rng, 0.75f, 1.25f), step, rng);
                }
            }
        }

        private static float ZoneLength(int zone) => zone switch
        {
            Head => 0.85f,
            Jaw => 0.6f,
            Paw => 0.6f,
            Shin => 0.8f,
            Belly => 0.9f,
            _ => 1f,
        };

        /// <summary>The felt's length factor at a point: half on the face, nothing around the eyes.</summary>
        private static float FaceFactor(in Box box, in Face f, float a, float b)
        {
            if (box.Zone != Head || f.Index != 4)
            {
                return 1f;
            }

            float factor = 0.55f;
            if (box.Eyes != null)
            {
                var p = f.Point(a, b);
                for (int i = 0; i < box.Eyes.Length; i++)
                {
                    float dx = (p.x - box.Eyes[i].x) * box.Size.x, dy = (p.y - box.Eyes[i].y) * box.Size.y;
                    float d = Mathf.Sqrt((dx * dx) + (dy * dy));
                    if (d < box.EyeRadius)
                    {
                        return 0f;
                    }

                    if (d < box.EyeRadius * 1.5f)
                    {
                        factor = 0.3f;
                    }
                }
            }

            return factor;
        }

        private static void FeltBlade(Shape shape, int kind, int zone, in Face f, float a, float b, float length, float spacing, System.Random rng)
        {
            var root = f.Surface(a, b, out Vector3 skin);
            var grain = Grain(zone, skin);
            float lean, widthShare, bend;
            switch (kind)
            {
                case Bristle: lean = Next(rng, 0.30f, 0.62f); widthShare = 0.36f; bend = 0.3f; length *= 1.4f; break;
                case Plumage: lean = Next(rng, 1.12f, 1.30f); widthShare = 0.74f; bend = 0.22f; length *= 1.7f; break;
                default: lean = Next(rng, 0.84f, 1.12f); widthShare = 0.5f; bend = 0.55f; break;
            }

            var lie = Twist(grain, skin, Next(rng, -0.38f, 0.38f));
            var dir = ((skin * Mathf.Cos(lean)) + (lie * Mathf.Sin(lean))).normalized;
            var side = Vector3.Cross(skin, lie).normalized;
            float w = spacing * widthShare;
            var uv = f.Uv(a, b);
            float phase = (float)rng.NextDouble();
            float tone = Next(rng, 0.94f, 1.05f);
            var coat = new Vector4(uv.x, uv.y, phase, 1f);
            var rootData = new Vector4(root.x, root.y, root.z, 0f);
            var jitter = new Vector3(Next(rng, -0.14f, 0.14f), Next(rng, -0.14f, 0.14f), Next(rng, -0.14f, 0.14f));
            var normal = f.NormalToCube(((skin * 0.82f) + (dir * 0.34f) + jitter).normalized);
            var low = new Color(0.66f * tone, 0f, 0f, 0f);
            var high = new Color(0.80f * tone, 0f, 0f, bend);

            int l = shape.Add(root - f.ToCube(side * w), normal, low, coat, rootData);
            int r = shape.Add(root + f.ToCube(side * w), normal, low, coat, rootData);
            if (kind == Plumage)
            {
                // A flat tab with a rounded end: a small covert feather lying on the body.
                var mid = new Color(0.75f * tone, 0f, 0f, bend * 0.5f);
                int ml = shape.Add(root + f.ToCube((dir * (length * 0.62f)) - (side * (w * 0.92f))), normal, mid, coat, rootData);
                int mr = shape.Add(root + f.ToCube((dir * (length * 0.62f)) + (side * (w * 0.92f))), normal, mid, coat, rootData);
                int tip = shape.Add(root + f.ToCube(dir * length), normal, high, coat, rootData);
                shape.Tri(l, r, mr);
                shape.Tri(l, mr, ml);
                shape.Tri(ml, mr, tip);
            }
            else
            {
                int tip = shape.Add(root + f.ToCube(dir * length), normal, high, coat, rootData);
                shape.Tri(l, r, tip);
            }

            shape.Blades++;
            shape.Reach = Mathf.Max(shape.Reach, f.ToCube(dir * length).magnitude);
        }

        // ------------------------------------------------------------------------------------------------ strands

        private static void StrandFace(Shape shape, in Box box, in Face f, float spacing, System.Random rng)
        {
            if (box.Kind == Plumage)
            {
                return;
            }

            int nu = Mathf.Max(1, Mathf.RoundToInt(f.LenU / spacing));
            int nv = Mathf.Max(1, Mathf.RoundToInt(f.LenV / spacing));
            float length = StrandLength(box.Unit);
            for (int iv = 0; iv < nv; iv++)
            {
                for (int iu = 0; iu < nu; iu++)
                {
                    float a = (iu + Next(rng, 0.1f, 0.9f)) / nu;
                    float b = (iv + Next(rng, 0.1f, 0.9f)) / nv;
                    var p = f.Point(a, b);
                    float share = StrandShare(box, f.Index, p, out Vector3 pull);
                    if (share <= 0f)
                    {
                        continue;
                    }

                    Strand(shape, f, a, b, pull, length * share * Next(rng, 0.7f, 1.25f), spacing, rng);
                }
            }
        }

        /// <summary>How long a strand grows at a point of a part (0 = none) and which way it is pulled: the species'
        /// places, and a shaggy coat everywhere on back and flanks.</summary>
        private static float StrandShare(in Box box, int face, Vector3 p, out Vector3 pull)
        {
            var down = new Vector3(0f, -1f, 0f);
            var back = new Vector3(0f, -0.25f, -1f);
            pull = down;
            bool side = face == 0 || face == 1, top = face == 2, bottom = face == 3, front = face == 4, rear = face == 5;
            switch (box.Zone)
            {
                case Torso:
                    return TorsoStrandShare(box, p, side, top, front, back, ref pull);
                case Head:
                    return HeadStrandShare(box, p, top, rear);
                case Jaw:
                    return (box.Styles & Beard) != 0 && (bottom || (front && p.y < 0f)) && p.z > -0.15f ? 0.6f : 0f;
                case Belly:
                    return BellyStrandShare(box, p, side, bottom);
                case Thigh:
                    return box.Kind == Shaggy && !top && !bottom && p.y > -0.1f ? 0.55f : 0f;
                case Shin:
                    return ShinStrandShare(box, p, top, bottom, ref pull);
                case Tail:
                    return TailStrandShare(box, p, top, front, back, ref pull);
                default:
                    return 0f;
            }
        }

        /// <summary><see cref="StrandShare"/> on a torso segment: mane and chest on the front-most one, a ridge along the
        /// back (pulled back), and a shaggy coat over back and flanks — the longest of them wins.</summary>
        private static float TorsoStrandShare(in Box box, Vector3 p, bool side, bool top, bool front, Vector3 back, ref Vector3 pull)
        {
            float share = 0f;
            if ((box.Styles & Mane) != 0 && box.First && ((top && p.z > -0.25f) || (side && p.y > 0.15f && p.z > -0.05f)))
            {
                share = top ? 1f : 0.8f;
            }

            if ((box.Styles & Ridge) != 0 && top && Mathf.Abs(p.x) < 0.2f)
            {
                share = Mathf.Max(share, 0.7f);
                pull = back;
            }

            if ((box.Styles & Chest) != 0 && box.First && front && p.y < 0.2f)
            {
                share = Mathf.Max(share, 0.9f);
            }

            if (box.Kind == Shaggy && (side || top))
            {
                share = Mathf.Max(share, 0.85f);
            }

            return share;
        }

        /// <summary><see cref="StrandShare"/> on a head: the mane over the crown and the nape, a shaggy forelock.</summary>
        private static float HeadStrandShare(in Box box, Vector3 p, bool top, bool rear)
        {
            float share = 0f;
            if ((box.Styles & Mane) != 0 && ((top && p.z < 0.05f) || (rear && p.y > -0.25f)))
            {
                share = 0.8f;
            }

            if (box.Kind == Shaggy && top && p.z < 0.25f)
            {
                share = Mathf.Max(share, 0.55f);
            }

            return share;
        }

        /// <summary><see cref="StrandShare"/> on a belly: a chest fringe toward the front, shaggy flanks.</summary>
        private static float BellyStrandShare(in Box box, Vector3 p, bool side, bool bottom)
        {
            float share = 0f;
            if ((box.Styles & Chest) != 0 && p.z > 0.2f && (side || bottom))
            {
                share = 0.6f;
            }

            if (box.Kind == Shaggy && side)
            {
                share = Mathf.Max(share, 0.6f);
            }

            return share;
        }

        /// <summary><see cref="StrandShare"/> on a shin: the fetlock tuft round the lower leg, pulled down and back.</summary>
        private static float ShinStrandShare(in Box box, Vector3 p, bool top, bool bottom, ref Vector3 pull)
        {
            if ((box.Styles & Fetlock) != 0 && !top && !bottom && p.y < -0.05f && p.y > -0.4f)
            {
                pull = new Vector3(0f, -1f, -0.3f);
                return 0.5f;
            }

            return 0f;
        }

        /// <summary><see cref="StrandShare"/> on a tail link: the tuft on the last one, else a ridge, else a shaggy coat.</summary>
        private static float TailStrandShare(in Box box, Vector3 p, bool top, bool front, Vector3 back, ref Vector3 pull)
        {
            if ((box.Styles & TailTuft) != 0 && box.Last && !front)
            {
                pull = new Vector3(0f, -0.35f, -1f);
                return 1.3f;
            }

            if ((box.Styles & Ridge) != 0 && top && Mathf.Abs(p.x) < 0.3f)
            {
                pull = back;
                return 0.6f;
            }

            return box.Kind == Shaggy && !front ? 0.7f : 0f;
        }

        /// <summary>One strand: a tapering ribbon of three joints that leaves the skin and then follows the pull.</summary>
        private static void Strand(Shape shape, in Face f, float a, float b, Vector3 pull, float length, float spacing, System.Random rng)
        {
            var root = f.Surface(a, b, out Vector3 skin);
            var uv = f.Uv(a, b);
            var grain = pull - (skin * Vector3.Dot(pull, skin));
            grain = grain.sqrMagnitude < 0.02f ? Grain(Torso, skin) : grain.normalized;
            grain = Twist(grain, skin, Next(rng, -0.3f, 0.3f));
            var dir = ((skin * 0.6f) + (grain * 0.8f)).normalized;
            var side = Vector3.Cross(skin, grain).normalized;
            float w = spacing * Next(rng, 0.34f, 0.48f);
            float phase = (float)rng.NextDouble();
            float tone = Next(rng, 0.90f, 1.08f);
            var coat = new Vector4(uv.x, uv.y, phase, 0f);
            var rootData = new Vector4(root.x, root.y, root.z, 0f);
            var normal = f.NormalToCube(((skin * 0.75f) + (dir * 0.3f)).normalized);
            var pullDir = pull.normalized;

            var at = Vector3.zero; // metres from the root
            int prevL = -1, prevR = -1;
            for (int k = 0; k <= 3; k++)
            {
                float t = k / 3f;
                float half = w * (k == 0 ? 1f : k == 1 ? 0.82f : k == 2 ? 0.5f : 0f);
                var tint = new Color(Mathf.Lerp(0.64f, 0.82f, t) * tone, 0f, 0f, Mathf.Pow(t, 1.4f));
                if (k == 3)
                {
                    int tip = shape.Add(root + f.ToCube(at), normal, tint, coat, rootData);
                    shape.Tri(prevL, prevR, tip);
                    break;
                }

                int l = shape.Add(root + f.ToCube(at - (side * half)), normal, tint, coat, rootData);
                int r = shape.Add(root + f.ToCube(at + (side * half)), normal, tint, coat, rootData);
                if (k > 0)
                {
                    shape.Tri(prevL, prevR, r);
                    shape.Tri(prevL, r, l);
                }

                prevL = l;
                prevR = r;
                at += dir * (length / 3f);
                dir = (dir + (pullDir * 0.75f)).normalized; // the strand gives in to the pull joint by joint
            }

            shape.Blades++;
            shape.Reach = Mathf.Max(shape.Reach, f.ToCube(at).magnitude * 1.2f);
        }
    }
}
