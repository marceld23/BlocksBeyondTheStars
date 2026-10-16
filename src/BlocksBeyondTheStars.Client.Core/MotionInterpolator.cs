// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One pose read off a <see cref="MotionInterpolator"/> for the current render time.</summary>
    public struct MotionSample
    {
        /// <summary>Canonical world position (X/Z wrapped).</summary>
        public Vector3f Position;

        /// <summary>The drawn curve's velocity, blocks/s — what the body is visibly doing (facing, gait, pitch).</summary>
        public Vector3f Velocity;

        /// <summary>Heading in radians, interpolated along the shortest arc.</summary>
        public float Yaw;

        /// <summary>The motion flags of the snapshot the render time sits in (airborne, perched, …; caller-defined bits).</summary>
        public byte Flags;

        /// <summary>The stream starved and the pose is a short, braking extrapolation past the newest snapshot.</summary>
        public bool Extrapolating;
    }

    /// <summary>
    /// Buffered snapshot interpolation for planet entities — animals, NPCs, machines and bandits (#2664, #2666, #2670).
    /// Snapshots go in stamped on the render clock (<see cref="ServerClockSync"/>); the pose comes out a short, ADAPTIVE delay
    /// behind the newest one, so two snapshots straddle the render time and the body glides between real positions instead
    /// of chasing the newest one (the old lerp made the drawn speed pulse 0.6×–1.6× at 5 Hz) or guessing past it (the old
    /// creature dead reckoning pulsed 0.17×–2× at 2 Hz and drew a stopped animal up to 0.8 blocks past where it stood).
    /// <list type="bullet">
    /// <item><b>Cubic Hermite</b> between two snapshots, with the server's measured velocity as the tangents (or a backward
    /// difference when the snapshot has none) — curves stay curves, not polygons with a corner at every packet. Each tangent
    /// component is held to <see cref="TangentLimit"/>× that segment's displacement, which keeps every component monotone:
    /// a server-side snap (an NPC stepping up a block within one tick) eases instead of bulging past the step.</item>
    /// <item>The <b>delay adapts</b> to the spacing the entity actually gets (~0.2 s with the 7.5 Hz creature stream, ~0.7 s for a
    /// far animal on the 2 Hz list) and moves toward its target gently, so a change of rate never jerks the render time.</item>
    /// <item><b>Starvation</b> brakes: past the newest snapshot the body coasts on its last velocity, slowing to a stop within
    /// a fraction of a second; when data comes back the gap is closed by a fading correction instead of a snap.</item>
    /// <item>A <b>teleport</b> (farther than anything can run in the gap) resets the buffer; a long silence followed by
    /// movement gets a synthetic copy of the old pose so the motion starts at once instead of smearing across the silence.</item>
    /// <item>Everything is <b>wrap-aware</b> (the world seam is crossed along the short way).</item>
    /// </list>
    /// Pure — no UnityEngine — so it lives in Client.Core and is tested headless.
    /// </summary>
    public sealed class MotionInterpolator
    {
        private struct Snapshot
        {
            public double Time;
            public Vector3f Pos;
            public Vector3f Vel;
            public bool HasVel;
            public float Yaw;
            public byte Flags;
        }

        /// <summary>The render delay is this many sample intervals …</summary>
        public const double DelayIntervals = 1.25;

        /// <summary>… plus this jitter margin (seconds).</summary>
        public const double JitterMargin = 0.05;

        /// <summary>How fast the delay may grow toward its target (seconds of delay per second) — the motion plays at half
        /// speed for a moment, which beats running dry: an animal leaving the 7.5 Hz stream's radius gets the right delay for
        /// the 2 Hz list within about a second.</summary>
        public const double DelaySlewUp = 0.5;

        /// <summary>How fast the delay may shrink (seconds of delay per second) — 1.25× speed for a moment reads as nothing.</summary>
        public const double DelaySlewDown = 0.25;

        /// <summary>Weight of a longer spacing in the running interval estimate (rises fast — a slower stream must be
        /// covered at once) …</summary>
        public const double IntervalWeightUp = 0.5;

        /// <summary>… and of a shorter one (falls slowly — one quick pair is no new rate).</summary>
        public const double IntervalWeightDown = 0.2;

        /// <summary>A silence longer than this is not a beat (a keep-alive, a world that was held, an entity out of range).</summary>
        public const double LongGapSeconds = 1.0;

        /// <summary>Snapshots farther apart than this AND farther than <see cref="MaxPlausibleSpeed"/> could cover are a teleport.</summary>
        public const float TeleportDistance = 4f;

        /// <summary>Blocks/s nothing on a planet surface outruns (a bolting animal, a sandworm's lunge) — beyond it, a teleport.</summary>
        public const float MaxPlausibleSpeed = 40f;

        /// <summary>Each Hermite tangent component, scaled to the segment, is held within this many times the segment's own
        /// displacement on that axis (3 is the classic monotonicity bound) plus <see cref="TangentSlack"/>.</summary>
        public const float TangentLimit = 3f;

        /// <summary>Blocks of tangent allowed even on an axis that did not move — a curve may lean a little.</summary>
        public const float TangentSlack = 0.02f;

        /// <summary>The longest braking coast past the newest snapshot (seconds); shorter when snapshots come faster.</summary>
        public const double MaxExtrapolation = 0.25;

        /// <summary>Time constant of the fading correction after a starved stretch (seconds).</summary>
        public const double CorrectionTau = 0.12;

        /// <summary>A correction larger than this is not worth fading — the pose snaps.</summary>
        public const float MaxCorrection = 2f;

        private const int MaxSnapshots = 32;
        private const double SameTickEpsilon = 1e-6;

        private readonly List<Snapshot> _buffer = new List<Snapshot>(8);
        private readonly double _minDelay;
        private readonly double _maxDelay;
        private double _interval;
        private double _delay = -1;
        private double _lastNow = double.NaN;
        private Vector3f _lastOutput;
        private bool _hasOutput;
        private Vector3f _correction;
        private bool _wasStarved;
        private double _seenNewest = double.NaN;

        /// <param name="initialInterval">The spacing assumed before any has been measured (the stream's nominal beat).</param>
        /// <param name="minDelay">The shortest render delay, seconds.</param>
        /// <param name="maxDelay">The longest render delay, seconds.</param>
        public MotionInterpolator(double initialInterval = 0.15, double minDelay = 0.1, double maxDelay = 0.8)
        {
            _interval = initialInterval > 0 ? initialInterval : 0.15;
            _minDelay = Math.Max(0, minDelay);
            _maxDelay = Math.Max(_minDelay, maxDelay);
        }

        /// <summary>Snapshots currently buffered.</summary>
        public int Count => _buffer.Count;

        /// <summary>True once at least one snapshot arrived.</summary>
        public bool HasData => _buffer.Count > 0;

        /// <summary>The running estimate of the snapshot spacing, seconds.</summary>
        public double Interval => _interval;

        /// <summary>The render delay in use, seconds (−1 before the first snapshot).</summary>
        public double Delay => _delay;

        /// <summary>The delay the current spacing asks for.</summary>
        public double TargetDelay => Clamp(_interval * DelayIntervals + JitterMargin, _minDelay, _maxDelay);

        /// <summary>The newest snapshot's position (the server's latest word), or false when there is none.</summary>
        public bool TryNewest(out Vector3f pos)
        {
            if (_buffer.Count == 0)
            {
                pos = Vector3f.Zero;
                return false;
            }

            pos = _buffer[_buffer.Count - 1].Pos;
            return true;
        }

        /// <summary>Adds a snapshot stamped on the render clock. <paramref name="hasVelocity"/> false = the sender had no
        /// velocity to give (an older server); the curve then takes its tangents from the positions. An older stamp than
        /// the newest is dropped; the same stamp replaces the newest (two messages from one server tick).</summary>
        public void Push(double time, Vector3f pos, Vector3f velocity, bool hasVelocity, float yaw, byte flags, int circumference)
        {
            var snap = new Snapshot { Time = time, Pos = pos, Vel = velocity, HasVel = hasVelocity, Yaw = yaw, Flags = flags };
            int last = _buffer.Count - 1;
            if (last < 0)
            {
                _buffer.Add(snap);
                if (_delay < 0)
                {
                    _delay = TargetDelay;
                }

                return;
            }

            var prev = _buffer[last];
            if (time < prev.Time - SameTickEpsilon)
            {
                return; // out of order — ignore
            }

            if (time - prev.Time <= SameTickEpsilon)
            {
                _buffer[last] = snap; // the same tick twice (the full list and the motion stream) — keep the freshest
                return;
            }

            double gap = time - prev.Time;
            float dist = WrappedDistance(prev.Pos, pos, circumference);
            if (dist > TeleportDistance && dist > MaxPlausibleSpeed * gap)
            {
                Reset(time, pos, velocity, hasVelocity, yaw, flags);
                return;
            }

            if (gap < LongGapSeconds)
            {
                _interval += (gap - _interval) * (gap > _interval ? IntervalWeightUp : IntervalWeightDown);
            }
            else if (dist > 0.01f)
            {
                // A long silence, then the entity is somewhere else: it stood at the old spot until a beat before this
                // snapshot — without the copy the move would be smeared across the whole silence.
                double at = time - Math.Min(_interval, gap * 0.5);
                _buffer.Add(new Snapshot { Time = at, Pos = prev.Pos, Vel = Vector3f.Zero, HasVel = true, Yaw = prev.Yaw, Flags = prev.Flags });
            }

            _buffer.Add(snap);
            while (_buffer.Count > MaxSnapshots)
            {
                _buffer.RemoveAt(0);
            }
        }

        /// <summary>Drops the history and starts over at this pose (a teleport, a pose snap such as sitting down).</summary>
        public void Reset(double time, Vector3f pos, Vector3f velocity, bool hasVelocity, float yaw, byte flags)
        {
            _buffer.Clear();
            _buffer.Add(new Snapshot { Time = time, Pos = pos, Vel = velocity, HasVel = hasVelocity, Yaw = yaw, Flags = flags });
            _correction = Vector3f.Zero;
            _wasStarved = false;
            _hasOutput = false;
            if (_delay < 0)
            {
                _delay = TargetDelay;
            }
        }

        /// <summary>Reads the pose for render-clock time <paramref name="now"/> (the clock the stamps are on). False only
        /// while nothing has arrived.</summary>
        public bool Sample(double now, int circumference, out MotionSample sample)
        {
            sample = default;
            int count = _buffer.Count;
            if (count == 0)
            {
                return false;
            }

            double dtNow = double.IsNaN(_lastNow) ? 0 : Math.Max(0, now - _lastNow);
            _lastNow = now;
            double current = _delay < 0 ? TargetDelay : _delay;
            _delay = MoveToward(current, TargetDelay, dtNow * (TargetDelay > current ? DelaySlewUp : DelaySlewDown));
            double render = now - _delay;

            Vector3f raw;
            bool starved = false;
            var newest = _buffer[count - 1];
            if (count == 1 || render <= _buffer[0].Time)
            {
                var first = _buffer[0];
                raw = first.Pos;
                sample.Velocity = Vector3f.Zero;
                sample.Yaw = first.Yaw;
                sample.Flags = first.Flags;
                starved = count == 1 && render > first.Time;
            }
            else if (render >= newest.Time)
            {
                // Starved: coast on the newest velocity, braking to a stop within the coast window.
                starved = true;
                double maxE = Math.Min(MaxExtrapolation, Math.Max(0.1, _interval));
                double e = render - newest.Time;
                Vector3f vel = TangentOf(count - 1, circumference);
                double eff = e < maxE ? e - (e * e) / (2 * maxE) : maxE * 0.5;
                raw = Offset(newest.Pos, vel.X * (float)eff, vel.Y * (float)eff, vel.Z * (float)eff, circumference);
                float keep = (float)Math.Max(0, 1 - e / maxE);
                sample.Velocity = new Vector3f(vel.X * keep, vel.Y * keep, vel.Z * keep);
                sample.Yaw = newest.Yaw;
                sample.Flags = newest.Flags;
            }
            else
            {
                int ib = count - 1;
                while (ib > 1 && _buffer[ib - 1].Time > render)
                {
                    ib--;
                }

                int ia = ib - 1;
                var a = _buffer[ia];
                var b = _buffer[ib];
                double span = b.Time - a.Time;
                float u = span > 1e-9 ? (float)((render - a.Time) / span) : 1f;
                Hermite(a, TangentOf(ia, circumference), b, TangentOf(ib, circumference), (float)span, u, circumference,
                    out raw, out var vel);
                sample.Velocity = vel;
                sample.Yaw = a.Yaw + ShortestAngleDelta(a.Yaw, b.Yaw) * u;
                sample.Flags = a.Flags;
                Prune(ia);
            }

            // Data came back after a starved stretch — or a late snapshot landed while the render time is still past it: the
            // body was drawn on a guessed coast, so fade from there instead of snapping onto the real curve.
            bool fresh = newest.Time != _seenNewest;
            _seenNewest = newest.Time;
            if (_wasStarved && _hasOutput && (!starved || fresh))
            {
                float dx = (float)WorldConstants.WrapDeltaX((double)_lastOutput.X - raw.X, circumference);
                float dz = (float)WorldConstants.WrapDeltaZ((double)_lastOutput.Z - raw.Z, circumference);
                var c = new Vector3f(dx, _lastOutput.Y - raw.Y, dz);
                _correction = Length(c) <= MaxCorrection ? c : Vector3f.Zero;
            }

            if (_correction.X != 0f || _correction.Y != 0f || _correction.Z != 0f)
            {
                float k = (float)Math.Exp(-dtNow / CorrectionTau);
                _correction = new Vector3f(_correction.X * k, _correction.Y * k, _correction.Z * k);
                if (Length(_correction) < 1e-4f)
                {
                    _correction = Vector3f.Zero;
                }
            }

            var pos = Offset(raw, _correction.X, _correction.Y, _correction.Z, circumference);
            _wasStarved = starved;
            _lastOutput = pos;
            _hasOutput = true;
            sample.Position = pos;
            sample.Extrapolating = starved;
            return true;
        }

        /// <summary>The tangent at snapshot <paramref name="i"/>: the sender's velocity, or the backward difference to the
        /// snapshot before it (never the one after — a tangent must not change once the span it bounds is on screen).</summary>
        private Vector3f TangentOf(int i, int circumference)
        {
            var s = _buffer[i];
            if (s.HasVel)
            {
                return s.Vel;
            }

            if (i == 0)
            {
                return Vector3f.Zero;
            }

            var p = _buffer[i - 1];
            double dt = s.Time - p.Time;
            if (dt <= 1e-6)
            {
                return Vector3f.Zero;
            }

            return new Vector3f(
                (float)(WorldConstants.WrapDeltaX((double)s.Pos.X - p.Pos.X, circumference) / dt),
                (float)((s.Pos.Y - p.Pos.Y) / dt),
                (float)(WorldConstants.WrapDeltaZ((double)s.Pos.Z - p.Pos.Z, circumference) / dt));
        }

        /// <summary>Cubic Hermite from <paramref name="a"/> to <paramref name="b"/> at <paramref name="u"/> ∈ [0,1], per axis
        /// with the tangent limit, along the short way across the seam; also returns the curve's velocity.</summary>
        private static void Hermite(Snapshot a, Vector3f va, Snapshot b, Vector3f vb, float span, float u, int circumference,
            out Vector3f pos, out Vector3f vel)
        {
            float dx = (float)WorldConstants.WrapDeltaX((double)b.Pos.X - a.Pos.X, circumference);
            float dy = b.Pos.Y - a.Pos.Y;
            float dz = (float)WorldConstants.WrapDeltaZ((double)b.Pos.Z - a.Pos.Z, circumference);
            float ox = Axis(dx, va.X * span, vb.X * span, u, span, out float vx);
            float oy = Axis(dy, va.Y * span, vb.Y * span, u, span, out float vy);
            float oz = Axis(dz, va.Z * span, vb.Z * span, u, span, out float vz);
            pos = Offset(a.Pos, ox, oy, oz, circumference);
            vel = new Vector3f(vx, vy, vz);
        }

        /// <summary>One axis of the Hermite segment from 0 to <paramref name="d"/>, tangents <paramref name="m0"/>/<paramref name="m1"/>
        /// already scaled by the span; returns the offset at <paramref name="u"/> and the velocity (per second).</summary>
        private static float Axis(float d, float m0, float m1, float u, float span, out float velocity)
        {
            float limit = TangentLimit * Math.Abs(d) + TangentSlack;
            m0 = Clamp(m0, -limit, limit);
            m1 = Clamp(m1, -limit, limit);
            float u2 = u * u, u3 = u2 * u;
            float h10 = u3 - 2f * u2 + u;
            float h01 = -2f * u3 + 3f * u2;
            float h11 = u3 - u2;
            float dh10 = 3f * u2 - 4f * u + 1f;
            float dh01 = -6f * u2 + 6f * u;
            float dh11 = 3f * u2 - 2f * u;
            // The segment starts at the local origin, so the h00 (start point) term is zero in both.
            velocity = span > 1e-6f ? (dh10 * m0 + dh01 * d + dh11 * m1) / span : 0f;
            return h10 * m0 + h01 * d + h11 * m1;
        }

        /// <summary>Drops history older than the snapshot before <paramref name="spanStart"/> — that one stays as the
        /// backward-difference anchor of the span's first tangent.</summary>
        private void Prune(int spanStart)
        {
            int drop = spanStart - 1;
            if (drop > 0)
            {
                _buffer.RemoveRange(0, drop);
            }
        }

        private static Vector3f Offset(Vector3f p, float dx, float dy, float dz, int circumference)
            => new Vector3f(
                (float)WorldConstants.WrapX((double)p.X + dx, circumference),
                p.Y + dy,
                (float)WorldConstants.WrapZ((double)p.Z + dz, circumference));

        private static float WrappedDistance(Vector3f a, Vector3f b, int circumference)
        {
            float dx = (float)WorldConstants.WrapDeltaX((double)b.X - a.X, circumference);
            float dz = (float)WorldConstants.WrapDeltaZ((double)b.Z - a.Z, circumference);
            float dy = b.Y - a.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static float Length(Vector3f v) => (float)Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

        private static double MoveToward(double cur, double target, double maxDelta)
        {
            double d = target - cur;
            return Math.Abs(d) <= maxDelta ? target : cur + Math.Sign(d) * maxDelta;
        }

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        /// <summary>Shortest signed angle from <paramref name="from"/> to <paramref name="to"/>, radians, in (−π, π].</summary>
        public static float ShortestAngleDelta(float from, float to)
        {
            const float Tau = 6.2831853f;
            float d = (to - from) % Tau;
            if (d > Math.PI)
            {
                d -= Tau;
            }
            else if (d < -Math.PI)
            {
                d += Tau;
            }

            return d;
        }
    }
}
