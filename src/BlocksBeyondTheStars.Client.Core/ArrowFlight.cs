// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #2647: the flight of a hunter's arrow (the server's <c>WorldFx</c> kind <c>"arrow"</c>, from the bow to the animal).
    /// A short, fast shot on a gentle arc — about a quarter of a second over a hunter's 12 m — that simply disappears at the
    /// target: no damage, no shake (the animal's sparkle comes from the creature-defeated path). Unity-free; the client's
    /// arrow effect samples it every frame.
    /// </summary>
    public static class ArrowFlight
    {
        /// <summary>How fast an arrow flies (metres per second): a hunter's 12 m shot takes 0.25 s.</summary>
        public const float SpeedMetresPerSecond = 48f;

        /// <summary>A shot at point-blank range still takes this long, so the arrow is seen at all.</summary>
        public const float MinSeconds = 0.12f;

        /// <summary>A long shot never takes longer than this.</summary>
        public const float MaxSeconds = 0.3f;

        /// <summary>The arc's height per metre of the shot (a 12 m shot rises 0.36 m in the middle).</summary>
        public const float ArcPerMetre = 0.03f;

        /// <summary>The arc never rises higher than this.</summary>
        public const float MaxArc = 0.5f;

        /// <summary>How long a shot from <paramref name="from"/> to <paramref name="to"/> flies (seconds).</summary>
        public static float Seconds(Vector3 from, Vector3 to) => Seconds(Vector3.Distance(from, to));

        /// <summary>How long a shot over <paramref name="distance"/> metres flies (seconds).</summary>
        public static float Seconds(float distance)
        {
            float s = distance / SpeedMetresPerSecond;
            return s < MinSeconds ? MinSeconds : s > MaxSeconds ? MaxSeconds : s;
        }

        /// <summary>How high the arc rises over the straight line in the middle of a shot of <paramref name="distance"/>.</summary>
        public static float ArcHeight(float distance) => Math.Min(MaxArc, Math.Max(0f, distance) * ArcPerMetre);

        /// <summary>The arrow's position at progress <paramref name="c"/> (0 at the bow, 1 at the target; clamped): the
        /// straight line plus a parabolic rise of <see cref="ArcHeight"/> at the middle.</summary>
        public static Vector3 Position(Vector3 from, Vector3 to, float c)
        {
            c = Clamp01(c);
            var p = Vector3.Lerp(from, to, c);
            p.Y += 4f * ArcHeight(Vector3.Distance(from, to)) * c * (1f - c);
            return p;
        }

        /// <summary>The direction the arrow points at progress <paramref name="c"/> — the tangent of its arc, so it noses
        /// up as it leaves the bow and down as it arrives. A unit vector; +Z for a shot of no length.</summary>
        public static Vector3 Direction(Vector3 from, Vector3 to, float c)
        {
            c = Clamp01(c);
            var d = to - from;
            d.Y += 4f * ArcHeight(d.Length()) * (1f - (2f * c));
            float len = d.Length();
            return len > 1e-5f ? d / len : Vector3.UnitZ;
        }

        private static float Clamp01(float c) => c < 0f ? 0f : c > 1f ? 1f : c;
    }

    /// <summary>
    /// #2647: where hunters' arrows are about to land. The server removes a shot animal in the same tick as it sends the
    /// arrow, so the client would break the animal into sparkles while the arrow is still on its way; an animal defeated
    /// within <see cref="MatchRadius"/> of an arrow's landing point waits for the arrow (never longer than
    /// <see cref="MaxHoldSeconds"/>). Unity-free; the creature view asks it per defeated animal.
    /// </summary>
    public sealed class ArrowLandings
    {
        /// <summary>A defeated animal this close (metres) to an arrow's landing point waits for that arrow.</summary>
        public const float MatchRadius = 3f;

        /// <summary>A defeated animal waits at most this long for an arrow.</summary>
        public const float MaxHoldSeconds = 1f;

        /// <summary>At most this many arrows are tracked; the oldest is dropped beyond it.</summary>
        public const int Capacity = 16;

        private readonly List<(Vector3 At, float LandsAt)> _landings = new List<(Vector3 At, float LandsAt)>();

        /// <summary>The arrows still tracked (landed ones go on the next <see cref="Prune"/>).</summary>
        public int Count => _landings.Count;

        /// <summary>An arrow will land at <paramref name="at"/> at the time <paramref name="landsAt"/>.</summary>
        public void Add(Vector3 at, float landsAt)
        {
            if (_landings.Count >= Capacity)
            {
                _landings.RemoveAt(0);
            }

            _landings.Add((at, landsAt));
        }

        /// <summary>Whether an animal defeated at <paramref name="position"/> keeps standing at <paramref name="now"/>:
        /// an arrow is still flying towards a spot within <see cref="MatchRadius"/> of it, landing within
        /// <see cref="MaxHoldSeconds"/>.</summary>
        public bool Holds(Vector3 position, float now)
        {
            foreach (var (at, landsAt) in _landings)
            {
                if (now < landsAt && landsAt - now <= MaxHoldSeconds
                    && Vector3.DistanceSquared(position, at) <= MatchRadius * MatchRadius)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Forgets the arrows that have landed by <paramref name="now"/>.</summary>
        public void Prune(float now) => _landings.RemoveAll(l => l.LandsAt <= now);

        /// <summary>Forgets every arrow (a world change).</summary>
        public void Clear() => _landings.Clear();
    }
}
