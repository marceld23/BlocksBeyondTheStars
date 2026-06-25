using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Buffered snapshot interpolation for a remote entity's pose (B Tier1b). Network presence arrives at a low,
    /// jittery rate (~10 Hz); rendering directly toward "the latest position" with a fixed lerp rubber-bands on
    /// jitter and drops a packet ungracefully. Instead we keep a short timestamped history and render the pose at
    /// a fixed delay BEHIND the newest sample, interpolating between the two snapshots that straddle that render
    /// time — the standard entity-interpolation technique (Source/Overwatch/Gaffer-on-Games).
    ///
    /// Pure (no UnityEngine) so it lives in Client.Core and is unit-tested headless; the Unity layer just feeds it
    /// samples (stamped with the client clock) and reads the sampled pose each frame. Positions are CANONICAL
    /// world coordinates on a wrapping (toroidal) world, so interpolation follows the shortest path across the
    /// X/Z seams — a player crossing a seam must not sweep back across the whole map.
    /// </summary>
    public sealed class RemoteEntityInterpolator
    {
        private readonly struct Snapshot
        {
            public readonly double Time;
            public readonly Vector3f Pos;
            public readonly float Yaw;

            public Snapshot(double time, Vector3f pos, float yaw)
            {
                Time = time;
                Pos = pos;
                Yaw = yaw;
            }
        }

        private readonly List<Snapshot> _buffer = new List<Snapshot>();
        private readonly double _delay;
        private readonly int _maxSnapshots;

        /// <param name="interpolationDelaySeconds">How far behind the newest sample to render. Should exceed the
        /// send interval (~0.1 s at 10 Hz) so two snapshots usually straddle the render time; the default 0.15 s
        /// tolerates one late/dropped packet at the cost of seeing others ~150 ms in the past.</param>
        /// <param name="maxSnapshots">Hard cap on retained history (a safety bound; pruning normally keeps it tiny).</param>
        public RemoteEntityInterpolator(double interpolationDelaySeconds = 0.15, int maxSnapshots = 32)
        {
            _delay = interpolationDelaySeconds > 0 ? interpolationDelaySeconds : 0.15;
            _maxSnapshots = maxSnapshots < 2 ? 2 : maxSnapshots;
        }

        public bool HasData => _buffer.Count > 0;

        /// <summary>Adds a freshly received pose stamped with the client's (monotonic) clock. A sample older than
        /// the latest is dropped; one with the same timestamp replaces the latest (two arrivals in one frame).</summary>
        public void Push(double clientTime, Vector3f worldPos, float yaw)
        {
            int last = _buffer.Count - 1;
            if (last >= 0 && clientTime <= _buffer[last].Time)
            {
                if (clientTime < _buffer[last].Time)
                {
                    return; // out of order — ignore
                }

                _buffer[last] = new Snapshot(clientTime, worldPos, yaw); // same frame — keep the freshest
                return;
            }

            _buffer.Add(new Snapshot(clientTime, worldPos, yaw));
            if (_buffer.Count > _maxSnapshots)
            {
                _buffer.RemoveAt(0);
            }
        }

        /// <summary>Samples the interpolated pose for the render clock <paramref name="now"/> (the same clock used
        /// for <see cref="Push"/>). Returns false only when no samples have arrived yet.</summary>
        public bool Sample(double now, int circumference, out Vector3f pos, out float yaw)
        {
            int count = _buffer.Count;
            if (count == 0)
            {
                pos = Vector3f.Zero;
                yaw = 0f;
                return false;
            }

            if (count == 1)
            {
                pos = _buffer[0].Pos;
                yaw = _buffer[0].Yaw;
                return true;
            }

            double renderTime = now - _delay;

            // Older than everything buffered (just started) → show the oldest sample.
            if (renderTime <= _buffer[0].Time)
            {
                pos = _buffer[0].Pos;
                yaw = _buffer[0].Yaw;
                return true;
            }

            // Ahead of the newest sample (buffer starved — late/dropped packets) → HOLD the newest rather than
            // extrapolate, so a stall briefly freezes instead of overshooting and snapping back.
            var newest = _buffer[count - 1];
            if (renderTime >= newest.Time)
            {
                pos = newest.Pos;
                yaw = newest.Yaw;
                return true;
            }

            // Find the span [a, b] straddling renderTime and lerp across it (seam-aware).
            for (int i = count - 1; i > 0; i--)
            {
                var b = _buffer[i];
                var a = _buffer[i - 1];
                if (renderTime >= a.Time && renderTime <= b.Time)
                {
                    double span = b.Time - a.Time;
                    float t = span > 1e-9 ? (float)((renderTime - a.Time) / span) : 0f;
                    pos = LerpWrapped(a.Pos, b.Pos, t, circumference);
                    yaw = a.Yaw + ShortestAngleDelta(a.Yaw, b.Yaw) * t;
                    Prune(a.Time);
                    return true;
                }
            }

            pos = newest.Pos; // unreachable in practice — fall back to newest
            yaw = newest.Yaw;
            return true;
        }

        /// <summary>Drops history strictly older than the snapshot currently interpolated from (keeps ≥2).</summary>
        private void Prune(double keepFrom)
        {
            while (_buffer.Count > 2 && _buffer[1].Time <= keepFrom)
            {
                _buffer.RemoveAt(0);
            }
        }

        /// <summary>Lerp between two canonical world positions along the SHORTEST path across the torus seams,
        /// then re-canonicalize the result.</summary>
        private static Vector3f LerpWrapped(Vector3f a, Vector3f b, float t, int circumference)
        {
            double dx = WorldConstants.WrapDeltaX(b.X - a.X, circumference);
            double dz = WorldConstants.WrapDeltaZ(b.Z - a.Z, circumference);
            double x = WorldConstants.WrapX(a.X + dx * t, circumference);
            double z = WorldConstants.WrapZ(a.Z + dz * t, circumference);
            float y = a.Y + (b.Y - a.Y) * t;
            return new Vector3f((float)x, y, (float)z);
        }

        /// <summary>Shortest signed angular difference from→to in degrees, in (-180, 180].</summary>
        private static float ShortestAngleDelta(float from, float to)
        {
            float d = (to - from) % 360f;
            if (d > 180f)
            {
                d -= 360f;
            }
            else if (d < -180f)
            {
                d += 360f;
            }

            return d;
        }
    }
}
