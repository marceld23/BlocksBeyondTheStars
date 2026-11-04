// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BodyPaint = BlocksBeyondTheStars.Shared.State.BodyPaint;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// #2750: what the player paints must read on the avatar as painted — the left of the canvas on the left of
    /// someone looking at that face, so writing reads the right way round. The pixel face always did (pinned here);
    /// the body paint (torso, arms, legs, helmet) was drawn as its mirror image.
    /// <para>
    /// Unity is left-handed: someone looking at a face along −normal has <c>Cross(up, forward)</c> to their right — for
    /// the figure's front (+Z, looked at along −Z) that is −X.
    /// </para>
    /// </summary>
    public sealed class AvatarPaintOrientationEditModeTests
    {
        private const float Eps = 1e-4f;

        // Faces in the painted mesh's order: +x, −x, +y, −y, +z, −z.
        private static readonly Vector3[] Normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        // Screen-up of someone looking at each face: up for the sides, the far edge (−z) for the top seen from in front,
        // the front (+z) for the bottom seen from below.
        private static readonly Vector3[] Ups = { Vector3.up, Vector3.up, Vector3.back, Vector3.forward, Vector3.up, Vector3.up };

        /// <summary>The observer's right for face <paramref name="f"/>: Unity's camera right, Cross(up, forward).</summary>
        private static Vector3 RightOf(int f) => Vector3.Cross(Ups[f], -Normals[f]);

        [Test]
        public void ObserverRight_InUnitysFrame()
        {
            Assert.That((Quaternion.LookRotation(Vector3.back) * Vector3.right - Vector3.left).sqrMagnitude, Is.LessThan(1e-8f),
                "Someone in front of the figure looks along −Z; their right is −X.");
            Assert.That(RightOf(4), Is.EqualTo(Vector3.left));
            Assert.That(RightOf(5), Is.EqualTo(Vector3.right));
            Assert.That(RightOf(0), Is.EqualTo(Vector3.forward));
            Assert.That(RightOf(1), Is.EqualTo(Vector3.back));
            Assert.That(RightOf(2), Is.EqualTo(Vector3.left), "Looking down on the top from in front.");
            Assert.That(RightOf(3), Is.EqualTo(Vector3.left), "Looking up at the bottom, the front up.");
        }

        [Test]
        public void TheFacePlate_PutsCanvasColumnZeroOnTheObserversLeft()
        {
            // The texture: canvas column 0 is texture x = 0 (u = 0), canvas row 0 the top.
            var grid = new int[FacePalette.Pixels];
            for (int row = 0; row < FacePalette.Size; row++)
            {
                grid[row * FacePalette.Size] = 1;
            }

            var tex = FacePalette.BuildAvatarTexture(FacePalette.Encode(grid), Color.white);
            try
            {
                Color32 painted = tex.GetPixel(0, 0), expected = FacePalette.ColorOf(1), far = tex.GetPixel(FacePalette.Size - 1, 0);
                Assert.That((painted.r, painted.g, painted.b), Is.EqualTo((expected.r, expected.g, expected.b)), "Canvas column 0 → texture x 0.");
                Assert.That((far.r, far.g, far.b), Is.EqualTo(((byte)255, (byte)255, (byte)255)), "The far column is skin.");
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }

            // The plate is a Unity cube; its +Z face (the figure's front) maps u = 0 to +X — the observer's left.
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var mesh = cube.GetComponent<MeshFilter>().sharedMesh;
                var verts = mesh.vertices;
                var normals = mesh.normals;
                var uv = mesh.uv;
                int front = 0;
                for (int i = 0; i < verts.Length; i++)
                {
                    if (Vector3.Dot(normals[i], Vector3.forward) < 0.99f)
                    {
                        continue;
                    }

                    front++;
                    Assert.That(Vector3.Dot(verts[i], RightOf(4)) < 0f, Is.EqualTo(uv[i].x < 0.5f),
                        $"Front-face vertex {verts[i]} with u {uv[i].x}: u = 0 must be on the observer's left.");
                    Assert.That(verts[i].y < 0f, Is.EqualTo(uv[i].y < 0.5f), "v = 0 at the bottom.");
                }

                Assert.That(front, Is.EqualTo(4));
            }
            finally
            {
                Object.DestroyImmediate(cube);
            }
        }

        [Test]
        public void TheFacePlate_SitsUnturnedOnTheHeadFront()
        {
            LogAssert.ignoreFailingMessages = true; // the avatar's part builder calls Object.Destroy, which logs outside play mode
            var go = new GameObject("AvatarOrientationTest");
            try
            {
                var avatar = go.AddComponent<PlayerAvatar>();
                avatar.Build(new Color(0.8f, 0.6f, 0.5f), Color.blue, Color.blue, Color.gray, spacesuit: true);
                var grid = new int[FacePalette.Pixels];
                grid[0] = 1;
                avatar.SetFace(FacePalette.Encode(grid));

                Transform plate = null;
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "FacePlate")
                    {
                        plate = t;
                    }
                }

                Assert.IsNotNull(plate, "SetFace builds the face plate.");
                var relative = Quaternion.Inverse(go.transform.rotation) * plate.rotation;
                Assert.That(Quaternion.Angle(relative, Quaternion.identity), Is.LessThan(0.01f), "Not turned against the figure.");
                Assert.That(plate.localScale.x > 0f && plate.localScale.y > 0f && plate.localScale.z > 0f, Is.True, "Not mirrored by a negative scale.");
                Assert.That(plate.localPosition.z, Is.GreaterThan(0f), "On the head's front (+Z).");
            }
            finally
            {
                Object.DestroyImmediate(go);
                LogAssert.ignoreFailingMessages = false;
            }
        }

        private static IEnumerable<(int Part, int Row)> PartRows()
        {
            yield return (BodyPaint.Torso, 0);
            yield return (BodyPaint.Arms, 0);
            yield return (BodyPaint.Arms, 1);
            yield return (BodyPaint.Legs, 0);
            yield return (BodyPaint.Legs, 1);
            yield return (BodyPaint.Helmet, 0);
        }

        /// <summary>One unit segment filling its row's box, so every face spans its whole chunk.</summary>
        private static Mesh Segment(int part, int row)
            => PlayerAvatar.PaintedSegmentMesh(part, row, Vector3.zero, Vector3.one, -0.5f * Vector3.one, 0.5f * Vector3.one);

        /// <summary>The canvas chunk a face's UVs lie in, or -1 for the solid-tint pad.</summary>
        private static int ChunkOf(int part, Vector2 uvCentre)
        {
            for (int k = 0; k < BodyPaint.ChunksOf(part); k++)
            {
                if (BodyPaintKit.ChunkRect(part, k).Contains(uvCentre))
                {
                    return k;
                }
            }

            return -1;
        }

        private static int FaceOf(Vector3 normal)
        {
            for (int f = 0; f < Normals.Length; f++)
            {
                if (Vector3.Dot(normal, Normals[f]) > 0.99f)
                {
                    return f;
                }
            }

            return -1;
        }

        /// <summary>One face of a painted mesh: a run of vertices sharing one normal — a side is a quad (4 vertices), a top or
        /// bottom without a chunk a cap of four triangles (12, #3178).</summary>
        private readonly struct FaceRun
        {
            public FaceRun(int start, int count, Vector3 normal)
            {
                Start = start;
                Count = count;
                Normal = normal;
            }

            public int Start { get; }

            public int Count { get; }

            public Vector3 Normal { get; }

            public bool IsCap => Count == 12;
        }

        /// <summary>The faces of a painted mesh in vertex order (the mesh lays them out face by face).</summary>
        private static List<FaceRun> Runs(Mesh mesh)
        {
            var normals = mesh.normals;
            var runs = new List<FaceRun>();
            int start = 0;
            for (int i = 1; i <= normals.Length; i++)
            {
                if (i == normals.Length || Vector3.Dot(normals[i], normals[start]) < 0.99f)
                {
                    runs.Add(new FaceRun(start, i - start, normals[start]));
                    start = i;
                }
            }

            return runs;
        }

        /// <summary>The face of <paramref name="mesh"/> facing <see cref="Normals"/>[<paramref name="face"/>].</summary>
        private static FaceRun RunOf(Mesh mesh, int face)
        {
            foreach (var run in Runs(mesh))
            {
                if (FaceOf(run.Normal) == face)
                {
                    return run;
                }
            }

            Assert.Fail($"The mesh has no face {face}.");
            return default;
        }

        private static Vector2 Centroid(Vector2[] uv, int t) => (uv[t] + uv[t + 1] + uv[t + 2]) / 3f;

        [Test]
        public void EveryPaintedBodyFace_ReadsAsPainted_AndFacesOutward()
        {
            foreach (var (part, row) in PartRows())
            {
                var mesh = Segment(part, row);
                try
                {
                    var verts = mesh.vertices;
                    var normals = mesh.normals;
                    var uv = mesh.uv;
                    var tris = mesh.triangles;
                    var runs = Runs(mesh);
                    Assert.That(runs.Count, Is.EqualTo(6), $"Part {part} row {row}: one run of vertices per cube face.");
                    Assert.That(verts.Length, Is.EqualTo(part == BodyPaint.Helmet ? 24 : 40),
                        "Four side quads and two caps of four triangles (#3178); the helmet's six faces are all quads.");
                    for (int t = 0; t < tris.Length; t += 3)
                    {
                        Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], c = verts[tris[t + 2]];
                        Assert.That(Vector3.Dot(Vector3.Cross(b - a, c - a), normals[tris[t]]), Is.GreaterThan(0f),
                            $"Part {part} row {row}: triangle {t / 3} must face outward (Unity's clockwise front).");
                    }

                    int painted = 0;
                    foreach (var run in runs)
                    {
                        int first = run.Start;
                        int f = FaceOf(run.Normal);
                        Assert.That(f, Is.GreaterThanOrEqualTo(0));
                        if (run.IsCap)
                        {
                            Assert.That(f == 2 || f == 3, Is.True, $"Part {part}: only a top or a bottom is a cap.");
                            continue; // pinned by TheCaps_ContinueTheEdgeRowsOfTheirSides
                        }

                        Assert.That(run.Count, Is.EqualTo(4), $"Part {part} face {f}: a side is one quad.");
                        int chunk = ChunkOf(part, Centre(uv, run));
                        if (chunk < 0)
                        {
                            continue; // the pad (the helmet's open front)
                        }

                        painted++;
                        var rect = BodyPaintKit.ChunkRect(part, chunk);
                        Vector3 right = RightOf(f), up = Ups[f];
                        for (int i = first; i < first + 4; i++)
                        {
                            bool left = Vector3.Dot(verts[i], right) < 0f, low = Vector3.Dot(verts[i], up) < 0f;
                            Assert.That(uv[i].x, Is.EqualTo(left ? rect.xMin : rect.xMax).Within(Eps),
                                $"Part {part} row {row} face {f}: the canvas's left edge must be on the observer's left.");
                            Assert.That(uv[i].y, Is.EqualTo(low ? rect.yMin : rect.yMax).Within(Eps),
                                $"Part {part} row {row} face {f}: the canvas's bottom edge must be at the bottom.");
                        }
                    }

                    Assert.That(painted, Is.GreaterThanOrEqualTo(4), $"Part {part}: the four sides (the helmet: three, chin and top) are painted.");
                }
                finally
                {
                    Object.DestroyImmediate(mesh);
                }
            }
        }

        /// <summary>The label of the canvas column each face of a part shows: the figure's own sides.</summary>
        private static string LabelOf(int part, int row, int face)
        {
            var mesh = Segment(part, row);
            try
            {
                var run = RunOf(mesh, face);
                int chunk = run.IsCap ? -1 : ChunkOf(part, Centre(mesh.uv, run));
                return chunk < 0 ? null : BodyPaintKit.ColumnKeys(part)[chunk % BodyPaintKit.Columns(part)];
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void TheLabelledColumns_LandOnTheFiguresOwnSides()
        {
            // The figure faces +Z; its own left is −X.
            Assert.That(LabelOf(BodyPaint.Torso, 0, 4), Is.EqualTo("ui.paint.body.front"));
            Assert.That(LabelOf(BodyPaint.Torso, 0, 1), Is.EqualTo("ui.paint.body.left"));
            Assert.That(LabelOf(BodyPaint.Torso, 0, 5), Is.EqualTo("ui.paint.body.back"));
            Assert.That(LabelOf(BodyPaint.Torso, 0, 0), Is.EqualTo("ui.paint.body.right"));

            Assert.That(LabelOf(BodyPaint.Helmet, 0, 1), Is.EqualTo("ui.paint.body.left"));
            Assert.That(LabelOf(BodyPaint.Helmet, 0, 5), Is.EqualTo("ui.paint.body.back"));
            Assert.That(LabelOf(BodyPaint.Helmet, 0, 0), Is.EqualTo("ui.paint.body.right"));
            Assert.That(LabelOf(BodyPaint.Helmet, 0, 3), Is.EqualTo("ui.paint.body.chin"));
            Assert.That(LabelOf(BodyPaint.Helmet, 0, 2), Is.EqualTo("ui.paint.body.top"));
            Assert.That(LabelOf(BodyPaint.Helmet, 0, 4), Is.Null, "The helmet is open in front.");

            // Limbs: the outer side faces away from the body — −X for the left limb (row 0), +X for the right (row 1).
            foreach (int part in new[] { BodyPaint.Arms, BodyPaint.Legs })
            {
                Assert.That(LabelOf(part, 0, 1), Is.EqualTo("ui.paint.body.outer"));
                Assert.That(LabelOf(part, 0, 0), Is.EqualTo("ui.paint.body.inner"));
                Assert.That(LabelOf(part, 1, 0), Is.EqualTo("ui.paint.body.outer"));
                Assert.That(LabelOf(part, 1, 1), Is.EqualTo("ui.paint.body.inner"));
                Assert.That(LabelOf(part, 1, 4), Is.EqualTo("ui.paint.body.front"));
            }
        }

        [Test]
        public void TheTorsoStrip_WrapsRoundTheFigure_EdgeToEdge()
        {
            // Walking round as you look at it: front → its left side → back → its right side → front. Each column's
            // right edge (u max) must meet the next column's left edge (u min) on the same vertical box edge.
            var mesh = Segment(BodyPaint.Torso, 0);
            try
            {
                var verts = mesh.vertices;
                var uv = mesh.uv;
                int[] walk = { 4, 1, 5, 0 };
                for (int i = 0; i < walk.Length; i++)
                {
                    int a = walk[i], b = walk[(i + 1) % walk.Length];
                    FaceRun runA = RunOf(mesh, a), runB = RunOf(mesh, b);
                    var rectA = BodyPaintKit.ChunkRect(BodyPaint.Torso, ChunkOf(BodyPaint.Torso, Centre(uv, runA)));
                    var rectB = BodyPaintKit.ChunkRect(BodyPaint.Torso, ChunkOf(BodyPaint.Torso, Centre(uv, runB)));
                    var edgeA = EdgeXz(verts, uv, runA, rectA.xMax);
                    var edgeB = EdgeXz(verts, uv, runB, rectB.xMin);
                    Assert.That((edgeA - edgeB).sqrMagnitude, Is.LessThan(1e-6f),
                        $"Face {a}'s right edge {edgeA} must meet face {b}'s left edge {edgeB}.");
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        /// <summary>#2755: the first-person hand (forearm, fist, thumb) wears the avatar's own painted arm, turned onto the
        /// raised arm: every painted face reads as painted, with the painting's top toward the shoulder (−Z in the hand's
        /// frame), the outer chunk on top as the hero face, and the arm's own chunks on the other sides.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void TheFirstPersonHand_WearsTheArmsPainting_ReadAsPainted(bool left)
        {
            LogAssert.ignoreFailingMessages = true; // the part builder calls Object.Destroy, which logs outside play mode
            HeldItem.HandPaintResolver = () => new string('1', BodyPaint.ExpectedLength(BodyPaint.Arms));
            var root = new GameObject("FirstPersonHandTest");
            try
            {
                var holder = HeldItem.Build(root.transform, HeldItem.Kind.Hand, Color.blue, left: left);
                Assert.IsNotNull(holder);
                var painted = new List<MeshFilter>();
                foreach (var filter in holder.GetComponentsInChildren<MeshFilter>())
                {
                    if (filter.sharedMesh != null && filter.sharedMesh.name.StartsWith("HandPaint", System.StringComparison.Ordinal))
                    {
                        painted.Add(filter);
                    }
                }

                Assert.That(painted.Count, Is.EqualTo(3), "Forearm, fist and thumb wear the painting; the cuff stays plain.");
                int row = left ? 0 : 1;
                var up = Vector3.back; // the painting's top runs back toward the shoulder
                foreach (var filter in painted)
                {
                    Assert.That(filter.transform.localRotation, Is.EqualTo(Quaternion.identity), "The part itself is not turned.");
                    var mesh = filter.sharedMesh;
                    var verts = mesh.vertices;
                    var normals = mesh.normals;
                    var uv = mesh.uv;
                    var tris = mesh.triangles;
                    for (int t = 0; t < tris.Length; t += 3)
                    {
                        Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], c = verts[tris[t + 2]];
                        Assert.That(Vector3.Dot(Vector3.Cross(b - a, c - a), normals[tris[t]]), Is.GreaterThan(0f), "Every face faces outward.");
                    }

                    foreach (var run in Runs(mesh))
                    {
                        int first = run.Start;
                        Vector3 n = run.Normal;
                        if (Mathf.Abs(n.z) > 0.99f)
                        {
                            // The arm's ends (here the near and far end): caps continuing the arm's own sides, as on the
                            // avatar (#3178) — every triangle in one of this arm's chunks.
                            Assert.That(run.IsCap, Is.True, "The arm's ends are caps, as on the avatar.");
                            for (int t = first; t < first + run.Count; t += 3)
                            {
                                int capChunk = ChunkOf(BodyPaint.Arms, Centroid(uv, t));
                                Assert.That(capChunk >= 0 && capChunk / 4 == row, Is.True, $"End {n}: the {(left ? "left" : "right")} arm's own row.");
                            }

                            continue;
                        }

                        Assert.That(run.Count, Is.EqualTo(4), $"Face {n}: a side is one quad.");
                        int chunk = ChunkOf(BodyPaint.Arms, Centre(uv, run));
                        Assert.That(chunk / 4, Is.EqualTo(row), $"Face {n}: the {(left ? "left" : "right")} arm's own row.");
                        string label = BodyPaintKit.ColumnKeys(BodyPaint.Arms)[chunk % 4];
                        string expected = n.y > 0.99f ? "ui.paint.body.outer"
                            : n.y < -0.99f ? "ui.paint.body.inner"
                            : (n.x < 0f) != left ? "ui.paint.body.front" // the arm's front turns toward the body
                            : "ui.paint.body.back";
                        Assert.That(label, Is.EqualTo(expected), $"Face {n} of the {(left ? "left" : "right")} hand.");

                        // Read as painted: the smallest u on the observer's left, the smallest v at the canvas bottom.
                        Vector3 right = Vector3.Cross(up, -n);
                        float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
                        for (int i = first; i < first + 4; i++)
                        {
                            uMin = Mathf.Min(uMin, uv[i].x);
                            uMax = Mathf.Max(uMax, uv[i].x);
                            vMin = Mathf.Min(vMin, uv[i].y);
                            vMax = Mathf.Max(vMax, uv[i].y);
                        }

                        for (int i = first; i < first + 4; i++)
                        {
                            Assert.That(uv[i].x, Is.EqualTo(Vector3.Dot(verts[i], right) < 0f ? uMin : uMax).Within(Eps),
                                $"Face {n}: the canvas's left edge must be on the observer's left.");
                            Assert.That(uv[i].y, Is.EqualTo(Vector3.Dot(verts[i], up) < 0f ? vMin : vMax).Within(Eps),
                                $"Face {n}: the canvas's top must run toward the shoulder.");
                        }
                    }
                }

                // The same painting as the third-person arm: the forearm is its lower arm, the fist its hand.
                AssertSameUvs(PlayerAvatar.PaintedArmMesh(row, hand: false), painted[0].sharedMesh, "forearm ↔ lower arm");
                AssertSameUvs(PlayerAvatar.PaintedArmMesh(row, hand: true), painted[1].sharedMesh, "fist ↔ hand");
            }
            finally
            {
                Object.DestroyImmediate(root);
                HeldItem.HandPaintResolver = null;
                HeldItem.ReleaseHandAtlas();
                LogAssert.ignoreFailingMessages = false;
            }
        }

        /// <summary>Both meshes carry the same texture coordinates (in any vertex order); destroys <paramref name="reference"/>.</summary>
        private static void AssertSameUvs(Mesh reference, Mesh actual, string what)
        {
            try
            {
                var a = new List<Vector2>(reference.uv);
                var b = new List<Vector2>(actual.uv);
                System.Comparison<Vector2> order = (p, q) => p.x != q.x ? p.x.CompareTo(q.x) : p.y.CompareTo(q.y);
                a.Sort(order);
                b.Sort(order);
                Assert.That(b.Count, Is.EqualTo(a.Count), what);
                for (int i = 0; i < a.Count; i++)
                {
                    Assert.That((a[i] - b[i]).sqrMagnitude, Is.LessThan(1e-10f), $"{what}: uv {i}");
                }
            }
            finally
            {
                Object.DestroyImmediate(reference);
            }
        }

        /// <summary>The mean UV of a face's vertices (for a quad: the middle of its rect).</summary>
        private static Vector2 Centre(Vector2[] uv, FaceRun run)
        {
            var sum = Vector2.zero;
            for (int i = run.Start; i < run.Start + run.Count; i++)
            {
                sum += uv[i];
            }

            return sum / run.Count;
        }

        /// <summary>The (x, z) of the vertical edge of a side face whose vertices carry u = <paramref name="u"/>.</summary>
        private static Vector2 EdgeXz(Vector3[] verts, Vector2[] uv, FaceRun run, float u)
        {
            for (int i = run.Start; i < run.Start + run.Count; i++)
            {
                if (Mathf.Abs(uv[i].x - u) < Eps)
                {
                    return new Vector2(verts[i].x, verts[i].z);
                }
            }

            Assert.Fail($"Face {run.Normal} has no vertex at u = {u}.");
            return default;
        }

        /// <summary>
        /// #3178 (the school club's "blue gaps"): the body-paint editor has no tops or bottoms, and the torso's and limbs'
        /// tops and bottoms showed the blue base tint however completely the part was painted — the shoulder tops, the strip
        /// under the chest, the wrist ring, the elbow wedge. Now each is a cap whose triangles continue the side they border:
        /// that side's chunk, at the same u along the shared edge, along the texel row the side shows right at that edge.
        /// </summary>
        [Test]
        public void TheCaps_ContinueTheEdgeRowsOfTheirSides()
        {
            foreach (var (part, row) in PartRows())
            {
                if (part == BodyPaint.Helmet)
                {
                    continue; // its top and chin are painted chunks of their own
                }

                var mesh = Segment(part, row);
                try
                {
                    AssertCapsContinueTheirSides(part, mesh, $"part {part} row {row}");
                }
                finally
                {
                    Object.DestroyImmediate(mesh);
                }
            }
        }

        /// <summary>#3178: a segment part-way up its limb continues the row at ITS edge — the lower arm's top the row at the
        /// elbow, the hand's top (the wrist ring round the narrower forearm) the row at the wrist. The arm's box runs from the
        /// hand's bottom (−1.0 in the shoulder frame) to the shoulder (0): the elbow sits at 0.58 of it, the wrist at 0.16 —
        /// texel rows 18 and 5 of the arm's chunks.</summary>
        [TestCase(0, false, 18)]
        [TestCase(0, true, 5)]
        [TestCase(1, false, 18)]
        [TestCase(1, true, 5)]
        public void TheArmsCaps_ContinueTheRowAtTheElbowAndAtTheWrist(int row, bool hand, int expectedRow)
        {
            var mesh = PlayerAvatar.PaintedArmMesh(row, hand);
            try
            {
                AssertCapsContinueTheirSides(BodyPaint.Arms, mesh, hand ? "hand" : "lower arm");
                var top = RunOf(mesh, 2);
                var uv = mesh.uv;
                Assert.That(top.IsCap, Is.True, "The top is a cap.");
                for (int t = top.Start; t < top.Start + top.Count; t += 3)
                {
                    var rect = BodyPaintKit.ChunkRect(BodyPaint.Arms, ChunkOf(BodyPaint.Arms, Centroid(uv, t)));
                    for (int k = t; k < t + 3; k++)
                    {
                        float v = (uv[k].y - rect.yMin) / rect.height;
                        Assert.That(Mathf.FloorToInt(v * BodyPaintKit.Face), Is.EqualTo(expectedRow), $"Cap vertex {k}: v {v}.");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        /// <summary>Every cap triangle of <paramref name="mesh"/> continues the side quad it borders (see
        /// <see cref="TheCaps_ContinueTheEdgeRowsOfTheirSides"/>); a top and a bottom cap exist.</summary>
        private static void AssertCapsContinueTheirSides(int part, Mesh mesh, string what)
        {
            var verts = mesh.vertices;
            var uv = mesh.uv;
            var runs = Runs(mesh);
            int caps = 0;
            foreach (var cap in runs)
            {
                if (!cap.IsCap)
                {
                    continue;
                }

                caps++;
                bool top = FaceOf(cap.Normal) == 2;
                for (int t = cap.Start; t < cap.Start + cap.Count; t += 3)
                {
                    var outer = new List<int>();
                    for (int k = t; k < t + 3; k++)
                    {
                        if (Mathf.Abs(verts[k].x) > Eps || Mathf.Abs(verts[k].z) > Eps)
                        {
                            outer.Add(k); // off the cap's centre: one of the edge's two corners
                        }
                    }

                    Assert.That(outer.Count, Is.EqualTo(2), $"{what}: a cap triangle runs from the centre to one edge.");
                    Vector3 mid = (verts[outer[0]] + verts[outer[1]]) * 0.5f;
                    var side = SideToward(runs, new Vector3(mid.x, 0f, mid.z).normalized, what);
                    int chunk = ChunkOf(part, Centre(uv, side));
                    Assert.That(chunk, Is.GreaterThanOrEqualTo(0), $"{what}: the side is painted.");
                    Assert.That(ChunkOf(part, Centroid(uv, t)), Is.EqualTo(chunk), $"{what}: the triangle samples its side's chunk.");
                    var rect = BodyPaintKit.ChunkRect(part, chunk);
                    foreach (int k in outer)
                    {
                        int q = SameCorner(verts, side, verts[k]);
                        Assert.That(uv[k].x, Is.EqualTo(uv[q].x).Within(Eps), $"{what}: the edge's u runs on from the side face.");
                        float sideV = (uv[q].y - rect.yMin) / rect.height, capV = (uv[k].y - rect.yMin) / rect.height;
                        Assert.That(Mathf.FloorToInt(capV * BodyPaintKit.Face), Is.EqualTo(BodyPaintEdges.EdgeTexel(sideV, top, BodyPaintKit.Face)),
                            $"{what}: the cap continues the texel row the side shows at that edge.");
                    }
                }
            }

            Assert.That(caps, Is.EqualTo(2), $"{what}: a top and a bottom cap.");
        }

        private static FaceRun SideToward(List<FaceRun> runs, Vector3 normal, string what)
        {
            foreach (var run in runs)
            {
                if (!run.IsCap && Vector3.Dot(run.Normal, normal) > 0.99f)
                {
                    return run;
                }
            }

            Assert.Fail($"{what}: no side face toward {normal}.");
            return default;
        }

        /// <summary>The vertex of a side face at the same corner as <paramref name="p"/>.</summary>
        private static int SameCorner(Vector3[] verts, FaceRun side, Vector3 p)
        {
            for (int i = side.Start; i < side.Start + side.Count; i++)
            {
                if ((verts[i] - p).sqrMagnitude < 1e-8f)
                {
                    return i;
                }
            }

            Assert.Fail($"The side {side.Normal} has no corner at {p}.");
            return -1;
        }

        /// <summary>#3178: the atlas's pad — what is left without a chunk or a side to continue — holds the part's most common
        /// colour: the paint of a part painted all over, the tint of one with only a small motif.</summary>
        [Test]
        public void ThePad_HoldsThePartsMostCommonColour()
        {
            const int part = BodyPaint.Torso;
            int cells = BodyPaintKit.CanvasW(part) * BodyPaintKit.CanvasH(part);
            var allOver = new int[cells];
            var motif = new int[cells];
            for (int i = 0; i < cells; i++)
            {
                allOver[i] = i < 40 ? 4 : 8; // red all over, a small white motif
                motif[i] = i < 40 ? 8 : 0;   // only a small red motif on the blue suit
            }

            AssertPad(part, BodyPaintKit.FromCanvas(part, allOver), Color.blue, FacePalette.ColorOf(8));
            AssertPad(part, BodyPaintKit.FromCanvas(part, motif), Color.blue, Color.blue);
        }

        private static void AssertPad(int part, string pixels, Color tint, Color expected)
        {
            var tex = BodyPaintKit.BuildAtlas(part, pixels, tint);
            try
            {
                Color32 pad = tex.GetPixel(BodyPaintKit.CanvasW(part) + 1, 0), want = expected;
                Assert.That((pad.r, pad.g, pad.b), Is.EqualTo((want.r, want.g, want.b)), "The atlas pad.");
                Color32 kit = BodyPaintKit.PadColor(part, pixels, tint);
                Assert.That((kit.r, kit.g, kit.b), Is.EqualTo((want.r, want.g, want.b)), "PadColor is what the atlas bakes.");
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        /// <summary>#3178: the first-person cuff is a darker shade of the painted arm's colour, not of the blue suit under it.</summary>
        [Test]
        public void TheFirstPersonCuff_TakesThePaintedArmsColour()
        {
            LogAssert.ignoreFailingMessages = true; // the part builder calls Object.Destroy, which logs outside play mode
            HeldItem.HandPaintResolver = () => new string(FacePalette.SymbolOf(8), BodyPaint.ExpectedLength(BodyPaint.Arms)); // red
            var root = new GameObject("FirstPersonCuffTest");
            try
            {
                var holder = HeldItem.Build(root.transform, HeldItem.Kind.Hand, Color.blue);
                Renderer cuff = null;
                foreach (var filter in holder.GetComponentsInChildren<MeshFilter>())
                {
                    if (filter.name == "HandPart" && filter.sharedMesh != null
                        && !filter.sharedMesh.name.StartsWith("HandPaint", System.StringComparison.Ordinal))
                    {
                        cuff = filter.GetComponent<Renderer>();
                    }
                }

                Assert.IsNotNull(cuff, "The bare hand has its plain cuff.");
                var c = cuff.sharedMaterial.color;
                Assert.That(c.r, Is.GreaterThan(c.b * 2f), $"The cuff {c} is a shade of the red painting, not of the blue suit.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                HeldItem.HandPaintResolver = null;
                HeldItem.ReleaseHandAtlas();
                LogAssert.ignoreFailingMessages = false;
            }
        }
    }
}
