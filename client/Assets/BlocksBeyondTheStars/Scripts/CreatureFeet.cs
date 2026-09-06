// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Plants a creature's feet on the actual ground and decides when each one steps.
    ///
    /// The gait alone already stops the skate — a planted foot travels backwards at body speed — but it does
    /// it in the BODY's frame, so all feet still sit on one flat plane at one body-relative height. On a
    /// hillside half of them hang in the air and half sink into the hill, and a 1-block step just slides the
    /// whole animal up it. Here a foot instead gets a real world-space target on a real block: it stays there
    /// while it carries weight, and swings to a new one when the body has walked past it.
    ///
    /// Two things this deliberately does NOT do. It never probes blocks per frame — only once per new foot
    /// target, which is a handful of lookups per second per creature. And it never trusts the root's position
    /// blindly: creature positions arrive at ~2 Hz and are lerped and dead-reckoned (<see cref="CreatureView"/>),
    /// so a foot planted in world space would drag or pop every time a correction landed. Targets are
    /// re-planted once they slip too far, and a teleport-sized jump resets every foot.
    /// </summary>
    public sealed class CreatureFeet
    {
        /// <summary>Finds the ground under a scene position: scans down at most <c>maxDrop</c> and reports the
        /// surface Y. Returns false when there is nothing to stand on (an unloaded chunk, deep water, a hole).</summary>
        public delegate bool GroundProbe(Vector3 scenePos, float maxDrop, out float groundY);

        // --- tuning ---
        private const float SlipToStep = 0.35f;    // fraction of a stride a planted foot may drift before it steps
        private const float LiftFraction = 0.18f;  // how high a swinging foot arcs, as a fraction of leg length
        private const float MaxSwingSeconds = 0.6f;
        private const float ResetJump = 3f;        // root moved this many leg-lengths in a frame → teleport
        private const float MaxBodyTiltDeg = 20f;

        private struct Foot
        {
            public bool Valid;      // has a target at all
            public bool Planted;    // false = mid-swing
            public Vector3 Target;  // scene space, where the sole sits
            public Vector3 SwingFrom;
            public float SwingT;
            public float SwingDur;
        }

        private LegRig[] _legs = System.Array.Empty<LegRig>();
        private Foot[] _feet = System.Array.Empty<Foot>();
        private GroundProbe _probe;
        private Vector3 _lastRoot;
        private bool _hasRoot;
        private float _standHeight = 1f;

        /// <summary>True while the feet are actually driving the legs — the caller hands its own slope pitch
        /// over to <see cref="BodyPitchDeg"/> when this is set, so the body is not tilted twice.</summary>
        public bool Active { get; private set; }

        /// <summary>Body adjustment derived from the plane through the planted feet.</summary>
        public float BodyOffsetY { get; private set; }

        public float BodyPitchDeg { get; private set; }

        public float BodyRollDeg { get; private set; }

        public void Init(LegRig[] legs, GroundProbe probe, float legLength)
        {
            _legs = legs ?? System.Array.Empty<LegRig>();
            _feet = new Foot[_legs.Length];
            _probe = probe;
            _standHeight = Mathf.Max(0.05f, legLength * 0.92f); // a standing leg keeps a slight bend
        }

        /// <summary>Drops every foot, so the next frame re-plants from scratch. Used when the rig comes back
        /// from a frozen LOD tier, when the creature teleports, and when the planner is switched off.</summary>
        public void Reset()
        {
            for (int i = 0; i < _feet.Length; i++)
            {
                _feet[i].Valid = false;
                _feet[i].Planted = false;
            }

            Active = false;
            BodyOffsetY = 0f;
            BodyPitchDeg = 0f;
            BodyRollDeg = 0f;
            _hasRoot = false;
        }

        /// <summary>Decides plant/swing for every foot this frame and derives the body's tilt from the result.
        /// Call before the body and the legs are posed.</summary>
        public void Plan(Transform root, float dt, Gait gait, float walkPhase, float cycleRate,
            float stride, Vector3 velocity, bool enabled)
        {
            if (!enabled || _legs.Length == 0 || _probe == null || root == null)
            {
                if (Active)
                {
                    Reset();
                }

                return;
            }

            // A teleport (spawn shove, eviction, a longitude wrap re-anchoring the scene) invalidates every
            // world-space target at once. Re-plant rather than dragging the feet across the map.
            var rootPos = root.position;
            if (_hasRoot && (rootPos - _lastRoot).sqrMagnitude > ResetJump * _standHeight * (ResetJump * _standHeight))
            {
                Reset();
            }

            _lastRoot = rootPos;
            _hasRoot = true;
            Active = true;

            float duty = CreatureGait.DutyFactor(gait);
            float swingDur = cycleRate > 0.05f
                ? Mathf.Clamp((1f - duty) / cycleRate, 0.08f, MaxSwingSeconds)
                : MaxSwingSeconds;
            float slipLimit = Mathf.Max(0.05f, stride * SlipToStep);
            int swinging = CountSwinging();
            int maxSwinging = Mathf.Max(1, _legs.Length <= 4 ? 2 : _legs.Length / 2);

            for (int i = 0; i < _legs.Length; i++)
            {
                var leg = _legs[i];
                if (leg?.Hip == null)
                {
                    continue;
                }

                // Where this foot would stand if the animal were still: straight under the hip, on the ground.
                Vector3 neutral = NeutralFor(leg, rootPos);

                if (!_feet[i].Valid)
                {
                    _feet[i].Valid = true;
                    _feet[i].Planted = true;
                    _feet[i].Target = neutral;
                    continue;
                }

                if (_feet[i].Planted)
                {
                    // Step when this leg's own gait phase says it is this foot's turn AND the body has
                    // actually walked past it. Standing still there is no slip, so a standing animal keeps
                    // all its feet exactly where they are instead of marching on the spot.
                    float phase = Frac(walkPhase + CreatureGait.PhaseOffset(gait, leg.Side, leg.Row, leg.Rows));
                    var drift = _feet[i].Target - neutral;
                    drift.y = 0f;
                    if (phase >= duty && drift.magnitude > slipLimit && swinging < maxSwinging)
                    {
                        // Aim where the hip will be by the time the foot lands, not where it is now.
                        Vector3 lead = velocity * (swingDur * 0.55f);
                        lead.y = 0f;
                        _feet[i].SwingFrom = _feet[i].Target;
                        _feet[i].Target = Ground(neutral + lead);
                        _feet[i].Planted = false;
                        _feet[i].SwingT = 0f;
                        _feet[i].SwingDur = swingDur;
                        swinging++;
                    }
                    else if (drift.magnitude > slipLimit * 3f)
                    {
                        // Far past any reasonable stride — a network correction, not a step. Snap, do not drag.
                        _feet[i].Target = Ground(neutral);
                    }
                }
                else
                {
                    _feet[i].SwingT += dt;
                    if (_feet[i].SwingT >= _feet[i].SwingDur)
                    {
                        _feet[i].Planted = true;
                        _feet[i].SwingT = 0f;
                    }
                }
            }

            DeriveBodyFromFeet(root, rootPos, dt);
        }

        /// <summary>Where the sole should be right now, in scene space.</summary>
        public Vector3 FootPosition(int index)
        {
            var foot = _feet[index];
            if (foot.Planted || foot.SwingDur <= 0f)
            {
                return foot.Target;
            }

            float s = Mathf.Clamp01(foot.SwingT / foot.SwingDur);
            float eased = s * s * (3f - 2f * s);
            var p = Vector3.Lerp(foot.SwingFrom, foot.Target, eased);
            p.y += Mathf.Sin(s * Mathf.PI) * _standHeight * LiftFraction;
            return p;
        }

        /// <summary>Whether this foot has a target to solve for yet.</summary>
        public bool HasTarget(int index) => index >= 0 && index < _feet.Length && _feet[index].Valid;

        /// <summary>The foot target in the hip's own frame — the offset from the hip pivot, expressed in the
        /// body rig's space. Deliberately not <c>Hip.InverseTransformPoint</c>: the hip's rotation is what we
        /// are about to solve for, so reading it back would be circular.</summary>
        public Vector3 LocalTarget(int index)
        {
            var leg = _legs[index];
            var parent = leg.Hip.parent;
            Vector3 inParent = parent != null
                ? parent.InverseTransformPoint(FootPosition(index))
                : FootPosition(index);
            return inParent - leg.HipRestPos;
        }

        private int CountSwinging()
        {
            int n = 0;
            for (int i = 0; i < _feet.Length; i++)
            {
                if (_feet[i].Valid && !_feet[i].Planted)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>The resting stance position for a leg: under its hip, on whatever is actually there.</summary>
        private Vector3 NeutralFor(LegRig leg, Vector3 rootPos)
        {
            Vector3 hip = leg.Hip.position;
            return Ground(new Vector3(hip.x, rootPos.y, hip.z));
        }

        /// <summary>Snaps a target down onto the ground under it. One probe per new target — never per frame.</summary>
        private Vector3 Ground(Vector3 p)
        {
            if (_probe != null && _probe(p, _standHeight * 1.6f, out float y))
            {
                p.y = y;
            }

            return p;
        }

        /// <summary>Tilts the body to the plane through the planted feet — the thing that makes an animal
        /// standing across a slope look like it is standing on the slope rather than levitating level.</summary>
        private void DeriveBodyFromFeet(Transform root, Vector3 rootPos, float dt)
        {
            float sum = 0f, front = 0f, rear = 0f, left = 0f, right = 0f;
            int n = 0, nFront = 0, nRear = 0, nLeft = 0, nRight = 0;

            for (int i = 0; i < _legs.Length; i++)
            {
                if (!_feet[i].Valid || !_feet[i].Planted)
                {
                    continue;
                }

                var leg = _legs[i];
                float y = _feet[i].Target.y;
                sum += y;
                n++;
                if (leg.Rows > 1)
                {
                    if (leg.Row == 0) { front += y; nFront++; }
                    else if (leg.Row == leg.Rows - 1) { rear += y; nRear++; }
                }

                if (leg.Side == 0) { left += y; nLeft++; }
                else { right += y; nRight++; }
            }

            if (n == 0)
            {
                return;
            }

            float k = 1f - Mathf.Exp(-6f * dt);
            float span = Mathf.Max(0.2f, _standHeight);

            float wantOffset = Mathf.Clamp((sum / n) - rootPos.y, -span * 0.5f, span * 0.5f) * 0.5f;
            BodyOffsetY = Mathf.Lerp(BodyOffsetY, wantOffset, k);

            float wantPitch = nFront > 0 && nRear > 0
                ? Mathf.Clamp(Mathf.Atan2((front / nFront) - (rear / nRear), span) * Mathf.Rad2Deg,
                    -MaxBodyTiltDeg, MaxBodyTiltDeg)
                : 0f;
            BodyPitchDeg = Mathf.Lerp(BodyPitchDeg, wantPitch, k);

            float wantRoll = nLeft > 0 && nRight > 0
                ? Mathf.Clamp(Mathf.Atan2((left / nLeft) - (right / nRight), span) * Mathf.Rad2Deg,
                    -MaxBodyTiltDeg, MaxBodyTiltDeg)
                : 0f;
            BodyRollDeg = Mathf.Lerp(BodyRollDeg, wantRoll, k);
        }

        private static float Frac(float v)
        {
            float f = v - Mathf.Floor(v);
            return f < 0f ? f + 1f : f;
        }
    }
}
