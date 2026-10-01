// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Shader-side point lights for effects (VFX overhaul #2152, fixes the invisible effect lights of #2151). URP
    /// additional lights are switched off by design (ADR 0003: the voxels light themselves from baked flood-fill and the
    /// <c>_Sc_*</c> globals), so a Unity <see cref="Light"/> on a muzzle flash, a beam pad or an emergency lamp lit
    /// nothing. This keeps a short list of requested lights — one-shot flashes and persistent emitters — and every frame
    /// uploads the most relevant few (by intensity × radius over distance to the camera) as the
    /// <c>_Sc_FxLightPos/_Sc_FxLightCol/_Sc_FxLightCount</c> globals that <c>FxCommon.hlsl</c> adds in BlockAtlas,
    /// LitColor, VertexColorOpaque and FxDebris. Slot count follows the quality preset (8 / 4 on Low / 2 on Potato).
    /// </summary>
    public static class FxLights
    {
        /// <summary>Array size declared in FxCommon.hlsl — the upload is always this long (Unity fixes a global array's
        /// size on its first upload).</summary>
        public const int Slots = 8;

        /// <summary>How many slots the preset may use (set by <see cref="FxKit.Configure"/>).</summary>
        public static int MaxLights = Slots;

        private sealed class Entry
        {
            public int Id;
            public Vector3 Position;
            public Transform Follow;
            public Vector3 FollowOffset;
            public Func<Vector3> Track;
            public Color Color;
            public float Intensity;
            public float Radius;
            public float Life;   // <= 0 = persistent until removed
            public float Age;
            public bool Flicker;
        }

        private static readonly List<Entry> Entries = new List<Entry>();
        private static readonly Vector4[] Pos = new Vector4[Slots];
        private static readonly Vector4[] Col = new Vector4[Slots];
        private static readonly List<(float score, Entry e)> Ranked = new List<(float, Entry)>();
        private static int _nextId = 1;
        private static bool _uploadedEmpty;

        private static readonly int PosId = Shader.PropertyToID("_Sc_FxLightPos");
        private static readonly int ColId = Shader.PropertyToID("_Sc_FxLightCol");
        private static readonly int CountId = Shader.PropertyToID("_Sc_FxLightCount");

        /// <summary>A one-shot light that fades out over <paramref name="life"/> seconds (muzzle flash, impact,
        /// explosion). Colour is sRGB-authored; intensity 1 ≈ a bright lamp. Reduce flashes dims it.</summary>
        public static void Flash(Vector3 at, Color color, float intensity, float radius, float life)
        {
            Entries.Add(new Entry
            {
                Id = _nextId++,
                Position = at,
                Color = color,
                Intensity = intensity * Mathf.Lerp(1f, 0.6f, FxKit.ReduceFlashes ? 1f : 0f),
                Radius = radius,
                Life = Mathf.Max(0.02f, life),
            });
        }

        /// <summary>A one-shot light that follows a moving point (a plasma bolt lighting the cave as it flies).</summary>
        public static int Track(Func<Vector3> position, Color color, float intensity, float radius, float life)
        {
            var e = new Entry { Id = _nextId++, Track = position, Color = color, Intensity = intensity, Radius = radius, Life = Mathf.Max(0.02f, life) };
            Entries.Add(e);
            return e.Id;
        }

        /// <summary>A persistent light attached to <paramref name="follow"/> (a beam pad, a landing engine, an emergency
        /// lamp) until <see cref="Remove"/> — or until the transform is destroyed.</summary>
        public static int Attach(Transform follow, Vector3 localOffset, Color color, float intensity, float radius, bool flicker = false)
        {
            var e = new Entry { Id = _nextId++, Follow = follow, FollowOffset = localOffset, Color = color, Intensity = intensity, Radius = radius, Flicker = flicker };
            Entries.Add(e);
            return e.Id;
        }

        /// <summary>Changes a persistent light's colour/intensity (e.g. an engine glow following throttle).</summary>
        public static void Set(int id, Color color, float intensity)
        {
            foreach (var e in Entries)
            {
                if (e.Id == id)
                {
                    e.Color = color;
                    e.Intensity = intensity;
                    return;
                }
            }
        }

        public static void Remove(int id)
        {
            Entries.RemoveAll(e => e.Id == id);
        }

        /// <summary>Drops every light and clears the globals (world teardown).</summary>
        public static void Reset()
        {
            Entries.Clear();
            Shader.SetGlobalFloat(CountId, 0f);
            _uploadedEmpty = true;
        }

        /// <summary>Ages the lights and uploads the best <see cref="MaxLights"/> (called once per frame by FxKit).</summary>
        public static void Upload(Vector3 camera, float dt)
        {
            Ranked.Clear();
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                var e = Entries[i];
                if (e.Follow == null && e.Track == null && e.Life <= 0f)
                {
                    Entries.RemoveAt(i); // a persistent light whose transform is gone
                    continue;
                }

                if (e.Life > 0f)
                {
                    e.Age += dt;
                    if (e.Age >= e.Life)
                    {
                        Entries.RemoveAt(i);
                        continue;
                    }
                }

                if (e.Follow != null)
                {
                    e.Position = e.Follow.TransformPoint(e.FollowOffset);
                }
                else if (e.Track != null)
                {
                    try
                    {
                        e.Position = e.Track();
                    }
                    catch (Exception)
                    {
                        Entries.RemoveAt(i);
                        continue;
                    }
                }

                float fade = e.Life > 0f ? (1f - e.Age / e.Life) : 1f;
                float flicker = e.Flicker ? 0.8f + 0.2f * Mathf.PerlinNoise(Time.time * 9f, e.Id * 0.37f) : 1f;
                float strength = e.Intensity * fade * fade * flicker;
                if (strength <= 0.001f)
                {
                    continue;
                }

                float dist = Vector3.Distance(camera, e.Position);
                if (dist > e.Radius + 60f)
                {
                    continue; // far beyond its reach and out of view range
                }

                Ranked.Add((strength * e.Radius / (dist + 2f), e));
            }

            int n = Mathf.Min(Mathf.Clamp(MaxLights, 0, Slots), Ranked.Count);
            if (n == 0)
            {
                if (!_uploadedEmpty)
                {
                    Shader.SetGlobalFloat(CountId, 0f);
                    _uploadedEmpty = true;
                }

                return;
            }

            Ranked.Sort((a, b) => b.score.CompareTo(a.score));
            for (int i = 0; i < Slots; i++)
            {
                if (i < n)
                {
                    var e = Ranked[i].e;
                    float fade = e.Life > 0f ? (1f - e.Age / e.Life) : 1f;
                    float flicker = e.Flicker ? 0.8f + 0.2f * Mathf.PerlinNoise(Time.time * 9f, e.Id * 0.37f) : 1f;
                    var lin = ShaderColor.Srgb(e.Color) * (e.Intensity * fade * fade * flicker);
                    Pos[i] = new Vector4(e.Position.x, e.Position.y, e.Position.z, e.Radius);
                    Col[i] = new Vector4(lin.r, lin.g, lin.b, 0f);
                }
                else
                {
                    Pos[i] = Vector4.zero;
                    Col[i] = Vector4.zero;
                }
            }

            Shader.SetGlobalVectorArray(PosId, Pos);
            Shader.SetGlobalVectorArray(ColId, Col);
            Shader.SetGlobalFloat(CountId, n);
            _uploadedEmpty = false;
        }
    }
}
