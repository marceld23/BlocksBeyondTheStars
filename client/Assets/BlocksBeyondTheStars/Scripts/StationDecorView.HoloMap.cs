// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using static BlocksBeyondTheStars.Client.ModelKit;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The cockpit's holographic system map (#4054, #4055).</summary>
    public sealed partial class StationDecorView
    {
        /// <summary>
        /// The cockpit hologram: the system the ship is in, as <see cref="HoloSystemMap"/> lays it out — the star in its own
        /// colour, every body as a tinted dot where the travel chart shows it, an orbit ring through every planet, a belt as
        /// one band — over a faint round sheet, and the planets travelling along their rings in time lapse. It lies in its
        /// own tilted plane just over the console (<see cref="StationDecorForms.HoloMapHeight"/>), fitted to the station's
        /// cell. Built when the player first comes near and again when the star map or the location changes; grows in on
        /// approach and collapses to zero scale while the player is away, when it costs nothing.
        /// <para>The same on every graphics tier: a handful of small spheres and three flat meshes.</para>
        /// </summary>
        private sealed class HoloMap : MonoBehaviour
        {
            public GameBootstrap Game;

            /// <summary>Blocks per chart unit: the model's chart fitted to the map's radius.</summary>
            private const float Unit = StationDecorForms.HoloMapChartRadius / HoloSystemMap.ChartHalf;

            private const float RingHalfWidth = 0.004f; // blocks
            private const float BeltPad = 4f * Unit;    // a belt band reaches this far past its outermost member
            private const int Segments = 48;

            private static readonly Color SheetColor = new Color(0.35f, 0.8f, 1f, 0.10f);
            private static readonly Color RingColor = new Color(0.45f, 0.85f, 1f, 0.45f);
            private static readonly Color StarFallback = new Color(1f, 0.85f, 0.5f);

            private readonly Dictionary<int, Material> _dotMats = new Dictionary<int, Material>();
            private readonly List<Mesh> _meshes = new List<Mesh>();
            private readonly List<Transform> _dots = new List<Transform>();
            private readonly List<Vector2> _bands = new List<Vector2>();
            private Material _sheetMat, _ringMat;
            private HoloSystemMap _model;
            private StarMapData _builtFor;
            private string _builtAt;
            private bool _built;
            private float _seconds;

            private void Update()
            {
                bool near = CameraNear(transform, 5f);
                if (near && Stale())
                {
                    Build();
                }

                transform.localScale = Vector3.Lerp(transform.localScale, near ? Vector3.one : Vector3.zero, Time.deltaTime * 6f);
                if (near && _model != null)
                {
                    _seconds += Time.deltaTime;
                    Place();
                }
            }

            private void OnDestroy()
            {
                Clear();
                foreach (var mat in _dotMats.Values)
                {
                    Destroy(mat);
                }

                _dotMats.Clear();
                if (_sheetMat != null)
                {
                    Destroy(_sheetMat);
                }

                if (_ringMat != null)
                {
                    Destroy(_ringMat);
                }
            }

            /// <summary>True until the map is built, and again when a new star map arrived or the ship is somewhere else.</summary>
            private bool Stale()
            {
                var map = Game != null ? Game.StarMap : null;
                return !_built || !ReferenceEquals(map, _builtFor) || (map != null ? map.ActiveLocationId : null) != _builtAt;
            }

            private void Build()
            {
                if (!_built)
                {
                    transform.localScale = Vector3.zero; // the first projection grows in
                }

                Clear();
                var map = Game != null ? Game.StarMap : null;
                _built = true;
                _builtFor = map;
                _builtAt = map != null ? map.ActiveLocationId : null;

                // The clock runs on over a rebuild, so a fresh star map of the same system does not set the planets back.
                var system = HoloSystemMap.ActiveSystem(map);
                var bodies = system != null && system.Bodies != null ? system.Bodies : System.Array.Empty<NetBody>();
                _model = HoloSystemMap.Build(bodies, _builtAt);

                _bands.Clear();
                _bands.Add(new Vector2(0f, StationDecorForms.HoloMapRadius));
                AddFlat("HoloSheet", SheetMaterial(), -StationDecorForms.HoloMapSheetDrop);

                _bands.Clear();
                foreach (var belt in _model.Belts)
                {
                    _bands.Add(new Vector2(Mathf.Max(0f, (belt.Inner * Unit) - BeltPad), (belt.Outer * Unit) + BeltPad));
                }

                AddFlat("HoloBelts", SheetMaterial(), 0f);

                _bands.Clear();
                foreach (var b in _model.Bodies)
                {
                    if (b.RingRadius > 0f)
                    {
                        _bands.Add(new Vector2((b.RingRadius * Unit) - RingHalfWidth, (b.RingRadius * Unit) + RingHalfWidth));
                    }
                }

                AddFlat("HoloRings", RingMaterial(), 0f);

                AddDot(HoloSystemMap.StarRadius, system != null && system.StarColor != 0 ? TransitionDirector.Rgb(system.StarColor) : StarFallback);
                foreach (var b in _model.Bodies)
                {
                    var body = bodies[b.Index];
                    var colour = SystemMapWidget.PlanetColor(body.PlanetType, body.Kind);
                    _dots.Add(AddDot(b.DotRadius, b.Active ? Color.Lerp(colour, Color.white, 0.4f) : colour)); // where you are glows brighter
                }

                Place();
            }

            /// <summary>Puts every body where it is now. The pilot stands on the map's +Z side and looks toward −Z, so the
            /// chart is laid out turned half round: its +x to the pilot's right and its +y away from the pilot, as the
            /// travel chart shows it on screen.</summary>
            private void Place()
            {
                for (int i = 0; i < _dots.Count; i++)
                {
                    _model.PositionAt(i, _seconds, out float x, out float y);
                    _dots[i].localPosition = new Vector3(-x * Unit, 0f, -y * Unit);
                }
            }

            /// <summary>Destroys what the last build made (the materials stay; they are shared by colour).</summary>
            private void Clear()
            {
                for (int i = transform.childCount - 1; i >= 0; i--)
                {
                    Destroy(transform.GetChild(i).gameObject);
                }

                foreach (var mesh in _meshes)
                {
                    Destroy(mesh);
                }

                _meshes.Clear();
                _dots.Clear();
                _model = null;
            }

            private Transform AddDot(float chartRadius, Color c)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                StripCollider(go);
                go.transform.SetParent(transform, false);
                go.transform.localScale = Vector3.one * (chartRadius * Unit * 2f);
                var c32 = (Color32)c;
                int key = (c32.r << 16) | (c32.g << 8) | c32.b;
                if (!_dotMats.TryGetValue(key, out var mat) || mat == null)
                {
                    mat = Unlit(c);
                    _dotMats[key] = mat;
                }

                var mr = go.GetComponent<Renderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                return go.transform;
            }

            private Material SheetMaterial() => _sheetMat != null ? _sheetMat : _sheetMat = Translucent(SheetColor);

            private Material RingMaterial() => _ringMat != null ? _ringMat : _ringMat = Translucent(RingColor);

            /// <summary>Hangs the bands collected in <see cref="_bands"/> (inner and outer radius, in blocks) into the map as one
            /// flat mesh, <paramref name="y"/> above the plane of the bodies. No bands, no object.</summary>
            private void AddFlat(string name, Material mat, float y)
            {
                if (_bands.Count == 0)
                {
                    return;
                }

                var mesh = BandMesh(name, _bands);
                _meshes.Add(mesh);
                var go = new GameObject(name);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, y, 0f);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            /// <summary>Flat rings in the XZ plane around the origin, all in one mesh. Seen from both sides: the translucent
            /// shader does not cull.</summary>
            private static Mesh BandMesh(string name, List<Vector2> bands)
            {
                int perBand = (Segments + 1) * 2;
                var verts = new Vector3[bands.Count * perBand];
                var normals = new Vector3[verts.Length];
                var tris = new int[bands.Count * Segments * 6];
                for (int b = 0; b < bands.Count; b++)
                {
                    for (int s = 0; s <= Segments; s++)
                    {
                        float a = s / (float)Segments * Mathf.PI * 2f;
                        float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                        int i = (b * perBand) + (s * 2);
                        verts[i] = new Vector3(cos * bands[b].x, 0f, sin * bands[b].x);
                        verts[i + 1] = new Vector3(cos * bands[b].y, 0f, sin * bands[b].y);
                        normals[i] = normals[i + 1] = Vector3.up;
                        if (s < Segments)
                        {
                            int t = ((b * Segments) + s) * 6;
                            tris[t] = i;
                            tris[t + 1] = i + 2;
                            tris[t + 2] = i + 1;
                            tris[t + 3] = i + 1;
                            tris[t + 4] = i + 2;
                            tris[t + 5] = i + 3;
                        }
                    }
                }

                var mesh = new Mesh { name = name, vertices = verts, normals = normals, triangles = tris };
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
