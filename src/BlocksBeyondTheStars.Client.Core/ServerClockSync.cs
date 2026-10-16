// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Maps the server's snapshot clock onto the client's render clock (#2664/#2665). Entity lists carry the server time of
    /// the tick that produced them; stamping a snapshot with THAT time (shifted by a learned offset) instead of with the
    /// moment it happened to arrive keeps the spacing between snapshots exact — packet jitter, frame quantisation and
    /// TCP bursts on WebGL no longer bend the motion. The offset tracks the fastest arrival seen (the least delayed packet
    /// is the best estimate of the clock difference) — in bounded steps, so one fast packet cannot squeeze a span — and
    /// creeps up slowly, so a latency that genuinely grew is absorbed; a jump bigger than <see cref="ResyncSeconds"/> (a new
    /// server, a held world resuming) re-syncs at once. Call it ONCE per received message (every snapshot of a list shares
    /// that stamp) — it learns from every call. Pure — no UnityEngine — so it is tested headless; one instance per
    /// connection is shared by every entity view.
    /// </summary>
    public sealed class ServerClockSync
    {
        /// <summary>A sample this far from the current offset is a new clock, not jitter: adopt it outright.</summary>
        public const double ResyncSeconds = 0.75;

        /// <summary>How far each later-than-expected arrival pulls the offset up (fraction of the difference).</summary>
        public const double CreepRate = 0.02;

        /// <summary>The most an earlier-than-expected arrival lowers the offset per message (seconds). A lower offset stamps
        /// the next snapshot earlier relative to the one before — adopting a 40 ms gain at once would squeeze one span into
        /// 70 % of its time (a visible spurt); in 10 ms steps it is never more than a few per cent.</summary>
        public const double MaxDropPerSample = 0.01;

        private double _offset;
        private bool _has;

        /// <summary>True once a server-stamped sample has set the offset.</summary>
        public bool Synced => _has;

        /// <summary>The current offset (local − server), seconds.</summary>
        public double Offset => _offset;

        /// <summary>The local render-clock time of a snapshot the server stamped <paramref name="serverTime"/>, given that it
        /// is being read at local time <paramref name="localNow"/>. An unstamped snapshot (an older server sends 0) is
        /// stamped on arrival.</summary>
        public double ToLocal(double serverTime, double localNow)
        {
            if (serverTime <= 0)
            {
                return localNow;
            }

            double sample = localNow - serverTime;
            if (!_has || Math.Abs(sample - _offset) > ResyncSeconds)
            {
                _offset = sample;
                _has = true;
            }
            else if (sample < _offset)
            {
                _offset = Math.Max(sample, _offset - MaxDropPerSample);
            }
            else
            {
                _offset += (sample - _offset) * CreepRate;
            }

            return serverTime + _offset;
        }

        /// <summary>Forgets the offset (a world reset / reconnect): the next stamped sample sets it afresh.</summary>
        public void Reset()
        {
            _has = false;
            _offset = 0;
        }
    }
}
