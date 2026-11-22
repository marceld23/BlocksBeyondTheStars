// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Drives one creature's fur and feathers (<see cref="CoatForms"/>, <c>CreatureCoat.shader</c>): it
    /// switches the short felt off beyond its range, and hands every coat mesh the lean of its blades — they trail
    /// the movement of the part they grow on (read from the rig's transforms, the animator is untouched) — and a
    /// wing's fold. The values travel in a property block per renderer, next to the hit flash's.
    /// </summary>
    public sealed class CoatMotion : MonoBehaviour
    {
        /// <summary>One coat mesh.</summary>
        public sealed class Entry
        {
            public Renderer Renderer;

            /// <summary>The animal's coat placement for this part (<c>_CoatUv</c>).</summary>
            public Vector4 Uv;

            /// <summary>True for a mesh that holds short felt: off beyond the felt's range.</summary>
            public bool Felt;

            /// <summary>The point whose movement the blades trail, in the renderer's own space (a wing: out on the panel).</summary>
            public Vector3 Probe;

            /// <summary>How far a tip leans per metre per second of that movement.</summary>
            public float Gain = 0.1f;

            /// <summary>A wing's wrist and its rest rotation: the fold is read from it. Null for everything else.</summary>
            public Transform Wrist;
            public Quaternion WristRest = Quaternion.identity;

            internal Vector3 Last, Velocity;
            internal bool Seen;
        }

        private static readonly int UvId = Shader.PropertyToID("_CoatUv");
        private static readonly int MotionId = Shader.PropertyToID("_CoatMotion");
        private static MaterialPropertyBlock _block;

        private Entry[] _entries = System.Array.Empty<Entry>();
        private float _idle;
        private bool _feltOn = true;
        private int _feltTriangles;

        /// <summary>The mark of a coat object's name: the defeat effect leaves these with their part.</summary>
        public const char Mark = '~';

        public void Init(List<Entry> entries)
        {
            _entries = entries.ToArray();
            _idle = 1f; // the first frame writes every block
            _feltTriangles = 0;
            foreach (var e in _entries)
            {
                var filter = e.Felt && e.Renderer != null ? e.Renderer.GetComponent<MeshFilter>() : null;
                if (filter != null && filter.sharedMesh != null)
                {
                    _feltTriangles += (int)(filter.sharedMesh.GetIndexCount(0) / 3);
                }
            }
        }

        private void LateUpdate() => Tick();

        /// <summary>One update (the frame's, or a caller's by hand).</summary>
        public void Tick()
        {
            var eye = Camera.main;
            if (eye == null || _entries.Length == 0)
            {
                return;
            }

            float distance = Vector3.Distance(eye.transform.position, transform.position);
            float feltRange = GameLook.CoatRangeNow;
            // A little hysteresis, so a grazing animal at the boundary does not flip its coat on and off.
            bool feltOn = distance < feltRange + (_feltOn ? 3f : 1.5f);
            bool near = distance < GameLook.CoatMotionRange;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);

            // Far away the blades stand still: the blocks are only refreshed now and then (a hit flash clears them).
            _idle += dt;
            if (!near && feltOn == _feltOn && _idle < 0.5f)
            {
                return;
            }

            _idle = 0f;
            _feltOn = feltOn;
            if (feltOn)
            {
                GameLook.CountCoat(_feltTriangles); // the coat budget shrinks the felt's range in a crowd
            }

            _block ??= new MaterialPropertyBlock();
            float blend = 1f - Mathf.Exp(-dt / 0.12f);
            for (int i = 0; i < _entries.Length; i++)
            {
                MoveEntry(_entries[i], feltOn, near, dt, blend);
            }
        }

        /// <summary>One coat mesh of <see cref="Tick"/>: switches the felt, then hands the mesh its lean and its fold
        /// through the property block — one step per mesh, in the entries' order.</summary>
        private static void MoveEntry(Entry e, bool feltOn, bool near, float dt, float blend)
        {
            if (e.Renderer == null)
            {
                return;
            }

            if (e.Felt && e.Renderer.enabled != feltOn)
            {
                e.Renderer.enabled = feltOn;
            }

            if (!e.Renderer.enabled)
            {
                return;
            }

            Vector3 lean = TrailLean(e, near, dt, blend);
            float fold = WingFold(e);
            e.Renderer.GetPropertyBlock(_block); // keeps what the hit flash wrote
            _block.SetVector(UvId, e.Uv);
            _block.SetVector(MotionId, new Vector4(lean.x, lean.y, lean.z, fold));
            e.Renderer.SetPropertyBlock(_block);
        }

        /// <summary>The lean of a mesh's blade tips: against the smoothed movement of its probe point, near the camera
        /// only (farther out the trail is forgotten, so coming back near does not start with a jump).</summary>
        private static Vector3 TrailLean(Entry e, bool near, float dt, float blend)
        {
            Vector3 lean = Vector3.zero;
            if (near)
            {
                Vector3 p = e.Renderer.transform.TransformPoint(e.Probe);
                Vector3 v = e.Seen ? (p - e.Last) / dt : Vector3.zero;
                if (v.sqrMagnitude > 900f)
                {
                    v = Vector3.zero; // a teleport, not a movement
                }

                e.Last = p;
                e.Seen = true;
                e.Velocity = Vector3.Lerp(e.Velocity, v, blend);
                lean = Vector3.ClampMagnitude(e.Velocity * -e.Gain, 0.8f);
            }
            else
            {
                e.Seen = false;
                e.Velocity = Vector3.zero;
            }

            return lean;
        }

        /// <summary>A wing's fold, 0 spread .. 1 folded, read from its wrist's yaw against the rest pose; 0 without a wrist.</summary>
        private static float WingFold(Entry e)
        {
            float fold = 0f;
            if (e.Wrist != null)
            {
                float yaw = (Quaternion.Inverse(e.WristRest) * e.Wrist.localRotation).eulerAngles.y;
                fold = Mathf.Clamp01(Mathf.Abs(Mathf.DeltaAngle(0f, yaw)) / CoatForms.FoldYaw);
            }

            return fold;
        }
    }
}
