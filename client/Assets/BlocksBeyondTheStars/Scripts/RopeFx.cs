// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The energy rope's look (#2322), style <c>rope</c>: the shot (a muzzle flash and a bright head flying to the
    /// anchor), the hook biting (flash, ring, sparks, a light), the rope itself (a rented beam-shader line of
    /// <see cref="RopeLine.PointCount"/> points whose pulses run from the anchor to the hand — "reeling in"), a small
    /// glow at the anchor while it holds, and the fizzle when it lets go. Built from the <see cref="FxKit"/> toolkit
    /// with the <c>FxBeam</c> shader the lasers use — no new shader. One line and no light per roped player, so a
    /// browser tab and a Pi draw it as easily as the laser.
    /// </summary>
    public static class RopeFx
    {
        private const float Width = 0.05f;

        /// <summary>The rope's material: a bright core, slow noise, and rings travelling along it (from point 0 on).</summary>
        public static Material LineMaterial(FxLook look)
            => FxKit.BeamMaterial("rope", look.Color2, coreWidth: 0.5f, noiseScale: 0.6f, noiseSpeed: 4f, pulse: 1.5f, intensity: 2.2f);

        /// <summary>A line for one rope, rented from the kit; give it back with <see cref="Return"/>.</summary>
        public static LineRenderer Rent(FxLook look)
        {
            var mat = LineMaterial(look);
            if (mat == null)
            {
                return null;
            }

            var lr = FxKit.Ensure().RentBeam(mat);
            lr.positionCount = RopeLine.PointCount;
            lr.widthMultiplier = Width;
            var c = FxKit.Lin(look.Color);
            lr.startColor = c;
            lr.endColor = c;
            return lr;
        }

        /// <summary>Returns a rented rope line to the kit.</summary>
        public static void Return(LineRenderer lr)
        {
            if (lr == null)
            {
                return;
            }

            lr.positionCount = 2;
            FxKit.Ensure().ReleaseBeam(lr);
        }

        /// <summary>Lays the rope from <paramref name="hand"/> to <paramref name="anchor"/> for this frame. The points run
        /// anchor → hand so the material's pulses travel toward the body. <paramref name="scratch"/> holds
        /// <see cref="RopeLine.PointCount"/> entries.</summary>
        public static void Draw(LineRenderer lr, Vector3 hand, Vector3 anchor, bool slack, float phase, Vector3f[] scratch)
        {
            if (lr == null || scratch == null)
            {
                return;
            }

            RopeLine.Points(new Vector3f(anchor.x, anchor.y, anchor.z), new Vector3f(hand.x, hand.y, hand.z), slack, phase, scratch);
            if (lr.positionCount != RopeLine.PointCount)
            {
                lr.positionCount = RopeLine.PointCount;
            }

            for (int i = 0; i < RopeLine.PointCount; i++)
            {
                lr.SetPosition(i, new Vector3(scratch[i].X, scratch[i].Y, scratch[i].Z));
            }
        }

        /// <summary>The shot: a muzzle flash and a bright head racing from <paramref name="from"/> to <paramref name="to"/>,
        /// then the hook biting there. <paramref name="local"/> adds the shooter's own camera punch.</summary>
        public static void Shot(FxLook look, Vector3 from, Vector3 to, bool local)
        {
            var dir = to - from;
            float len = dir.magnitude;
            if (len < 0.05f)
            {
                return;
            }

            dir /= len;
            FxShots.Muzzle(look, from, dir, 0.26f);
            float flight = Mathf.Clamp(len / 60f, 0.08f, 0.35f);
            var mat = LineMaterial(look);
            if (mat != null)
            {
                FxKit.Beam(from, from, look.Color, Width * 1.6f, flight + 0.1f, mat);
            }

            FxKit.Animate(flight, t =>
            {
                var p = Vector3.Lerp(from, to, t);
                FxKit.Emit(FxKit.Kind.Glow, p, Vector3.zero, 0.14f, 0.06f, look.Color2);
            }, () => Anchor(look, to, -dir));
            if (local)
            {
                FxCamera.FovPunch(3f);
            }
        }

        /// <summary>The hook biting at <paramref name="at"/> on a face with <paramref name="normal"/>: a flash, a ring on the
        /// face, a few sparks and a short light.</summary>
        public static void Anchor(FxLook look, Vector3 at, Vector3 normal)
        {
            if (normal.sqrMagnitude < 0.01f)
            {
                normal = Vector3.up;
            }

            FxKit.Flash(at + normal * 0.05f, look.Color2, 0.3f, 0.1f);
            FxKit.Ring(at + normal * 0.02f, normal, look.Color, 0.1f, 0.8f, 0.35f, thickness: 0.15f);
            FxKit.Burst(FxKit.Kind.Sparks, at + normal * 0.05f, 6, normal, 60f, 2f, 5f, 0.03f, 0.05f, 0.2f, 0.4f, look.Color, look.Color2);
            FxLights.Flash(at + normal * 0.4f, look.Color, 1.2f, 4f, 0.2f);
        }

        /// <summary>A small glow at the anchor while the rope holds — call every ~0.15 s.</summary>
        public static void Knot(FxLook look, Vector3 at)
            => FxKit.Emit(FxKit.Kind.Glow, at + Random.insideUnitSphere * 0.06f, Vector3.zero, 0.12f, 0.2f, look.Color);

        /// <summary>The rope let go at <paramref name="at"/>: a short fizz of motes and a flash.</summary>
        public static void Fizzle(FxLook look, Vector3 at)
        {
            FxKit.Flash(at, look.Color, 0.25f, 0.1f);
            FxKit.Burst(FxKit.Kind.Motes, at, 8, Vector3.up, 180f, 0.5f, 1.5f, 0.03f, 0.05f, 0.2f, 0.4f, look.Color);
        }

        /// <summary>Takes the rented line back and leaves a fading copy from <paramref name="hand"/> to <paramref name="anchor"/>.</summary>
        public static void FadeOut(ref LineRenderer lr, FxLook look, Vector3 hand, Vector3 anchor)
        {
            Return(lr);
            lr = null;
            var mat = LineMaterial(look);
            if (mat != null)
            {
                FxKit.Beam(hand, anchor, look.Color, Width, 0.18f, mat);
            }
        }
    }
}
