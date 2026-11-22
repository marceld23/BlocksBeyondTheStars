// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The far stage of the rounded boxes: in the distance the plain cube. A rounded part of a
    /// figure, an animal or a held tool costs 216 vertices where its cube costs 24; from far enough away the rounding is
    /// smaller than a pixel and pays for nothing. Every rounded part is handed in here with its sharp twin
    /// (<see cref="Assign"/>), and a sweep over a slice of the parts per frame swaps the two by distance.
    /// <para>
    /// <b>The distance</b> is each part's own: a rounded edge leaves its sharp corner by 0.29 of the radius, and the part
    /// goes sharp where that is under half a pixel (at half that distance on the low tiers, where a whole pixel is
    /// accepted) — so an arm switches at about 17 m on a 1080-line screen, a head at about 40 m, a giant's limb only
    /// hundreds of metres away, and never nearer than <see cref="MinFar"/>. Measured to the NEAREST enabled camera, not to
    /// the main one: the appearance editor's figure stands far from the world, in front of its own camera. A band of an
    /// eighth keeps a part on the line from flickering.
    /// </para>
    /// Display only. <see cref="GameLook.RoundFar"/> is a factor on that distance (0 would keep every part round).
    /// </summary>
    public sealed class RoundLod : MonoBehaviour
    {
        /// <summary>No part goes sharp nearer than this (metres): a tool in a figure's hand right in front of the camera.</summary>
        private const float MinFar = 5f;

        /// <summary>Parts looked at per frame — a village of a hundred figures is swept in about a quarter of a second.</summary>
        private const int Slice = 320;

        /// <summary>How far a rounded edge leaves the sharp corner, as a share of its radius (1 − 1/√2).</summary>
        private const float CornerGap = 0.2929f;

        private struct Entry
        {
            public EntityId Id;
            public MeshFilter Filter;
            public Transform At;
            public Mesh Round, Sharp;
            public float Share;
            public bool IsSharp;
        }

        private static readonly List<Entry> Entries = new List<Entry>(1024);
        private static readonly Dictionary<EntityId, int> Index = new Dictionary<EntityId, int>(1024);
        private static RoundLod _instance;

        private Camera[] _cameras = new Camera[8];
        private readonly Vector3[] _eyes = new Vector3[8];
        private int _cursor;

        /// <summary>True when rounded parts get a far stage at all (<see cref="GameLook.RoundFarScale"/> above 0).</summary>
        public static bool On => GameLook.RoundFarScale > 0f;

        /// <summary>
        /// Gives <paramref name="filter"/> its rounded mesh and, when it has a <paramref name="sharp"/> twin and the far
        /// stage is on, tracks the pair. <paramref name="share"/> is the radius as a share of the part's thinnest side
        /// (the part is a unit cube its transform scales). A part that is tracked already keeps the stage it shows —
        /// far away the new sharp twin, near the new round one; without a twin it is released and shows
        /// <paramref name="round"/>.
        /// </summary>
        public static void Assign(MeshFilter filter, Mesh round, Mesh sharp, float share)
        {
            if (filter == null)
            {
                return;
            }

            var id = filter.GetEntityId();
            bool track = On && round != null && sharp != null && sharp != round && share > 0f;
            if (Index.TryGetValue(id, out int at))
            {
                if (!track)
                {
                    RemoveAt(at);
                    filter.sharedMesh = round;
                    return;
                }

                var entry = Entries[at];
                entry.Round = round;
                entry.Sharp = sharp;
                entry.Share = share;
                Entries[at] = entry;
                filter.sharedMesh = entry.IsSharp ? sharp : round;
                return;
            }

            filter.sharedMesh = round;
            if (!track)
            {
                return;
            }

            Index[id] = Entries.Count;
            Entries.Add(new Entry { Id = id, Filter = filter, At = filter.transform, Round = round, Sharp = sharp, Share = share });
            EnsureInstance();
        }

        /// <summary>The rounded mesh of a tracked part (whichever stage it shows), null for a part not tracked.</summary>
        public static Mesh RoundOf(MeshFilter filter)
            => filter != null && Index.TryGetValue(filter.GetEntityId(), out int at) ? Entries[at].Round : null;

        /// <summary>The sharp twin of a tracked part, null for a part not tracked.</summary>
        public static Mesh SharpOf(MeshFilter filter)
            => filter != null && Index.TryGetValue(filter.GetEntityId(), out int at) ? Entries[at].Sharp : null;

        /// <summary>The distance (metres) at which a part whose thinnest side is <paramref name="thinnest"/> metres and
        /// whose radius is <paramref name="share"/> of it goes sharp, on a screen <paramref name="screenHeight"/> pixels
        /// high: where the rounding's gap at a corner is <paramref name="pixels"/> wide (a vertical field of view of 70°
        /// is assumed — a narrower one only makes the part switch a little early).</summary>
        public static float FarOf(float thinnest, float share, int screenHeight, float pixels, float scale)
        {
            const float fieldOfView = 1.2217f; // 70° in radians
            float gap = CornerGap * share * thinnest;
            float far = gap * screenHeight / (fieldOfView * Mathf.Max(0.05f, pixels)) * scale;
            return Mathf.Max(MinFar, far);
        }

        private static void EnsureInstance()
        {
            if (_instance != null || !Application.isPlaying)
            {
                return;
            }

            var go = new GameObject("RoundLod") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<RoundLod>();
        }

        private static void RemoveAt(int at)
        {
            int last = Entries.Count - 1;
            Index.Remove(Entries[at].Id); // the id is kept in the entry: the part itself may be destroyed already
            if (at != last)
            {
                var moved = Entries[last];
                Entries[at] = moved;
                Index[moved.Id] = at;
            }

            Entries.RemoveAt(last);
        }

        private void LateUpdate()
        {
            if (Entries.Count == 0)
            {
                return;
            }

            int eyes = ReadEyes();
            if (eyes == 0)
            {
                return;
            }

            // A whole pixel of gap is accepted on the low tiers, half a pixel on a desktop on Medium or High.
            bool fine = HighDetails.DesktopClass && FxKit.Preset >= QualityPreset.Medium;
            float pixels = fine ? 0.5f : 1f;
            float scale = GameLook.RoundFarScale;
            int height = Mathf.Max(360, Screen.height);

            int steps = Mathf.Min(Slice, Entries.Count);
            for (int n = 0; n < steps && Entries.Count > 0; n++)
            {
                if (_cursor >= Entries.Count)
                {
                    _cursor = 0;
                }

                var entry = Entries[_cursor];
                if (entry.Filter == null)
                {
                    RemoveAt(_cursor); // the part is gone; the entry moved here is looked at next
                    continue;
                }

                Sweep(ref entry, eyes, height, pixels, scale);
                Entries[_cursor] = entry;
                _cursor++;
            }
        }

        /// <summary>The positions of the enabled cameras, into <see cref="_eyes"/>; returns how many.</summary>
        private int ReadEyes()
        {
            int count = Camera.allCamerasCount;
            if (count > _cameras.Length)
            {
                _cameras = new Camera[count + 4];
            }

            count = Camera.GetAllCameras(_cameras);
            int eyes = 0;
            for (int i = 0; i < count && eyes < _eyes.Length; i++)
            {
                if (_cameras[i] != null)
                {
                    _eyes[eyes++] = _cameras[i].transform.position;
                }
            }

            return eyes;
        }

        private void Sweep(ref Entry entry, int eyes, int height, float pixels, float scale)
        {
            Vector3 at = entry.At.position, size = entry.At.lossyScale;
            float thinnest = Mathf.Min(Mathf.Abs(size.x), Mathf.Min(Mathf.Abs(size.y), Mathf.Abs(size.z)));
            float far = FarOf(thinnest, entry.Share, height, pixels, scale);
            if (entry.IsSharp)
            {
                far *= 0.875f; // comes back round a little nearer than it went sharp
            }

            float nearest = float.MaxValue;
            for (int i = 0; i < eyes; i++)
            {
                float d = (at - _eyes[i]).sqrMagnitude;
                nearest = d < nearest ? d : nearest;
            }

            bool sharp = nearest > far * far;
            if (sharp == entry.IsSharp)
            {
                return;
            }

            var mesh = sharp ? entry.Sharp : entry.Round;
            if (mesh == null)
            {
                return; // a painted twin was destroyed and not replaced yet
            }

            entry.Filter.sharedMesh = mesh;
            entry.IsSharp = sharp;
        }
    }
}
