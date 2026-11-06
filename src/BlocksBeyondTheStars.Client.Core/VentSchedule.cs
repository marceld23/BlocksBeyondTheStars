// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// When a geyser vent erupts (#3275), Unity-free so the headless tests pin it. A vent breathes steam all the time and
    /// erupts at random intervals. The clock is cut into slots of <see cref="SlotSeconds"/>; each slot holds exactly one
    /// eruption, at an offset within its first <see cref="OffsetRange"/> seconds and with a strength, both hashed from the
    /// vent's cell and the slot. So:
    /// <list type="bullet">
    /// <item>two eruptions of a vent lie <see cref="MinInterval"/>..<see cref="MaxInterval"/> seconds apart, and every vent
    /// keeps its own rhythm;</item>
    /// <item>the schedule is a pure function of the cell and the clock — O(1) for any clock value, no list of timers;</item>
    /// <item>a long frame or a jump of the clock fires at most ONE eruption, and only a fresh one (within
    /// <see cref="MaxLateness"/> seconds of its time) — never a backlog, never an eruption storm after a pause.</item>
    /// </list>
    /// One clock only: the caller passes the world time (it pauses with the world), and a clock that runs backwards (a new
    /// world) simply re-arms the schedule. #3276: the old view stamped a vent with <c>Time.time</c> and compared it with the
    /// world time.
    /// </summary>
    public sealed class VentSchedule
    {
        /// <summary>One eruption per slot of this many seconds (the mean interval).</summary>
        public const float SlotSeconds = 24f;

        /// <summary>The eruption sits this far into its slot at most.</summary>
        public const float OffsetRange = 16f;

        /// <summary>The shortest gap between two eruptions of one vent (seconds).</summary>
        public const float MinInterval = SlotSeconds - OffsetRange;

        /// <summary>The longest gap between two eruptions of one vent (seconds).</summary>
        public const float MaxInterval = SlotSeconds + OffsetRange;

        /// <summary>The weakest eruption (1 = the strongest); big ones are rarer than small ones.</summary>
        public const float MinStrength = 0.4f;

        /// <summary>An eruption found later than this after its time is skipped (a long frame, a pause, a clock jump).</summary>
        public const float MaxLateness = 1.5f;

        private readonly uint _key;
        private float _last;
        private bool _armed;

        /// <summary>The schedule of the vent in world cell (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>)
        /// — pass the canonical cell, so the same vent keeps its rhythm whichever copy of it the player stands near.</summary>
        public VentSchedule(int x, int y, int z)
        {
            _key = Fmix(unchecked(((uint)x * 73856093u) ^ ((uint)y * 19349663u) ^ ((uint)z * 83492791u)));
        }

        /// <summary>The eruption of a slot: its time on the clock and its strength (<see cref="MinStrength"/>..1).</summary>
        public (float Time, float Strength) EruptionInSlot(long slot)
        {
            uint h = Fmix(_key ^ unchecked((uint)slot * 0x9E3779B9u) ^ (uint)(slot >> 32));
            float offset = (h & 0xFFFF) / 65536f * OffsetRange;
            float roll = (h >> 16) / 65536f;
            float strength = MinStrength + (1f - MinStrength) * roll * roll; // squared: the big eruptions are the rare ones
            return ((float)(slot * (double)SlotSeconds + offset), strength);
        }

        /// <summary>The latest eruption at or before <paramref name="now"/>.</summary>
        public (float Time, float Strength) LatestAtOrBefore(float now)
        {
            long slot = (long)Math.Floor(now / SlotSeconds);
            var e = EruptionInSlot(slot);
            return e.Time <= now ? e : EruptionInSlot(slot - 1);
        }

        /// <summary>The first eruption after <paramref name="now"/>.</summary>
        public (float Time, float Strength) NextAfter(float now)
        {
            long slot = (long)Math.Floor(now / SlotSeconds);
            var e = EruptionInSlot(slot);
            return e.Time > now ? e : EruptionInSlot(slot + 1);
        }

        /// <summary>
        /// Advances the schedule to <paramref name="now"/> (the world time). True, with the eruption's strength, when an
        /// eruption fell after the previous call and at most <see cref="MaxLateness"/> seconds before now. The first call
        /// (and a call with a clock that ran backwards) only arms the schedule and never fires.
        /// </summary>
        public bool Poll(float now, out float strength)
        {
            strength = 0f;
            if (float.IsNaN(now) || float.IsInfinity(now))
            {
                return false;
            }

            if (!_armed || now < _last)
            {
                _armed = true;
                _last = now;
                return false;
            }

            var e = LatestAtOrBefore(now);
            bool fire = e.Time > _last && now - e.Time <= MaxLateness;
            _last = now;
            if (fire)
            {
                strength = e.Strength;
            }

            return fire;
        }

        /// <summary>MurmurHash3's 32-bit finaliser: every input bit reaches every output bit.</summary>
        private static uint Fmix(uint h)
        {
            unchecked
            {
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return h;
            }
        }
    }
}
