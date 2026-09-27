// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Draws the monorail's energy lines (#2113): for every rail line the server lists, the shared <see cref="RailSpline"/>
    /// through its pylon tops is sampled into a glowing tube (a chain of thin boxes in the Crystal Net's glow material) with
    /// a brighter band travelling along it, and an amber marker at every stop. No collider — the line is light, the train
    /// hovers over it. The splines are cached per line (by the server's list reference) and handed to <see cref="TrainView"/>,
    /// the player controller and the remote avatars, so every consumer poses against the same curve.
    /// </summary>
    public sealed class RailView : MonoBehaviour
    {
        public GameBootstrap Game;

        private const float SampleStep = 1.0f;
        private const float Thickness = 0.22f;
        private const float RebuildSeconds = 3f;

        private static readonly Color32 LineColor = new Color32(120, 235, 255, 230);
        private static readonly Color32 StopColor = new Color32(255, 180, 40, 245);

        private readonly Dictionary<int, RailSpline> _splines = new Dictionary<int, RailSpline>();
        private NetRailLine[] _built;
        private float _builtAt;
        private GameObject _go;
        private Mesh _mesh;
        private Material _mat;
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Color32> _colors = new List<Color32>();
        private readonly List<int> _tris = new List<int>();

        /// <summary>The spline of a line (null for an unknown line or one too short to run on).</summary>
        public RailSpline SplineOf(int lineId)
        {
            if (Game?.Rails == null)
            {
                return null;
            }

            if (!ReferenceEquals(_built, Game.Rails))
            {
                RefreshSplines();
            }

            return _splines.TryGetValue(lineId, out var s) && s.TotalArc > 0f ? s : null;
        }

        private void RefreshSplines()
        {
            _splines.Clear();
            int circ = Game != null ? Game.Circumference : Shared.World.WorldConstants.Circumference;
            foreach (var line in Game.Rails)
            {
                if (line == null || line.Points == null || line.Points.Length < 6)
                {
                    continue;
                }

                var pts = new List<Vector3f>(line.Points.Length / 3);
                for (int i = 0; i + 2 < line.Points.Length; i += 3)
                {
                    pts.Add(new Vector3f(line.Points[i], line.Points[i + 1], line.Points[i + 2]));
                }

                _splines[line.Id] = RailSpline.Build(pts, line.Closed, circ);
            }
        }

        private void Update()
        {
            if (Game == null || Game.Rails == null)
            {
                return;
            }

            bool changed = !ReferenceEquals(_built, Game.Rails);
            if (!changed && Time.time - _builtAt < RebuildSeconds)
            {
                return;
            }

            if (changed)
            {
                RefreshSplines();
            }

            _built = Game.Rails;
            _builtAt = Time.time;
            Rebuild();
        }

        private void Rebuild()
        {
            EnsureLayer();
            _verts.Clear();
            _colors.Clear();
            _tris.Clear();
            if (Game.Rails.Length == 0 || Game.World == null)
            {
                _go.SetActive(false);
                return;
            }

            foreach (var line in Game.Rails)
            {
                if (line == null || !_splines.TryGetValue(line.Id, out var spline) || spline.TotalArc <= 0f)
                {
                    continue;
                }

                var prev = Game.ScenePos(spline.PointAt(0f).X, spline.PointAt(0f).Y, spline.PointAt(0f).Z);
                float total = spline.TotalArc;
                for (float s = SampleStep; s <= total + 0.001f; s += SampleStep)
                {
                    var p = spline.PointAt(Mathf.Min(s, total));
                    var cur = Game.ScenePos(p.X, p.Y, p.Z);
                    if ((cur - prev).sqrMagnitude > 400f)
                    {
                        prev = cur; // the lap seam: do not stretch a box across the world
                        continue;
                    }

                    AddSegment(prev, cur, Thickness, LineColor);
                    prev = cur;
                }

                if (line.StopArcs != null)
                {
                    foreach (float arc in line.StopArcs)
                    {
                        var p = spline.PointAt(arc);
                        var c = Game.ScenePos(p.X, p.Y + 0.6f, p.Z);
                        AddBox(c - Vector3.one * 0.35f, c + Vector3.one * 0.35f, StopColor);
                    }
                }
            }

            _mesh.Clear();
            if (_verts.Count == 0)
            {
                _go.SetActive(false);
                return;
            }

            _mesh.SetVertices(_verts);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateBounds();
            _go.SetActive(true);
        }

        private void EnsureLayer()
        {
            if (_go != null)
            {
                return;
            }

            var shader = Shader.Find("BlocksBeyondTheStars/ParticleAlpha") ?? Shader.Find("Unlit/Transparent");
            _mat = new Material(shader) { renderQueue = 3050 };
            _go = new GameObject("RailLines");
            _go.transform.SetParent(transform, false);
            _mesh = new Mesh { name = "RailLines", indexFormat = IndexFormat.UInt32 };
            _mesh.MarkDynamic();
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = _go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>A thin box from <paramref name="a"/> to <paramref name="b"/>: the tube's segment.</summary>
        private void AddSegment(Vector3 a, Vector3 b, float half, Color32 color)
        {
            var dir = b - a;
            float len = dir.magnitude;
            if (len < 1e-4f)
            {
                return;
            }

            dir /= len;
            var side = Vector3.Cross(Vector3.up, dir);
            if (side.sqrMagnitude < 1e-4f)
            {
                side = Vector3.right;
            }

            side = side.normalized * half;
            var up = Vector3.Cross(dir, side).normalized * half;
            int i = _verts.Count;
            _verts.Add(a - side - up); _verts.Add(a + side - up); _verts.Add(a + side + up); _verts.Add(a - side + up);
            _verts.Add(b - side - up); _verts.Add(b + side - up); _verts.Add(b + side + up); _verts.Add(b - side + up);
            for (int k = 0; k < 8; k++)
            {
                _colors.Add(color);
            }

            Quad(i + 0, i + 3, i + 2, i + 1);
            Quad(i + 4, i + 5, i + 6, i + 7);
            Quad(i + 0, i + 4, i + 7, i + 3);
            Quad(i + 1, i + 2, i + 6, i + 5);
            Quad(i + 3, i + 7, i + 6, i + 2);
            Quad(i + 0, i + 1, i + 5, i + 4);
        }

        private void AddBox(Vector3 lo, Vector3 hi, Color32 color)
        {
            int i = _verts.Count;
            _verts.Add(new Vector3(lo.x, lo.y, lo.z)); _verts.Add(new Vector3(hi.x, lo.y, lo.z));
            _verts.Add(new Vector3(hi.x, hi.y, lo.z)); _verts.Add(new Vector3(lo.x, hi.y, lo.z));
            _verts.Add(new Vector3(lo.x, lo.y, hi.z)); _verts.Add(new Vector3(hi.x, lo.y, hi.z));
            _verts.Add(new Vector3(hi.x, hi.y, hi.z)); _verts.Add(new Vector3(lo.x, hi.y, hi.z));
            for (int k = 0; k < 8; k++)
            {
                _colors.Add(color);
            }

            Quad(i + 0, i + 3, i + 2, i + 1);
            Quad(i + 5, i + 6, i + 7, i + 4);
            Quad(i + 4, i + 7, i + 3, i + 0);
            Quad(i + 1, i + 2, i + 6, i + 5);
            Quad(i + 3, i + 7, i + 6, i + 2);
            Quad(i + 4, i + 0, i + 1, i + 5);
        }

        private void Quad(int a, int b, int c, int d)
        {
            _tris.Add(a); _tris.Add(b); _tris.Add(c);
            _tris.Add(a); _tris.Add(c); _tris.Add(d);
        }
    }
}
