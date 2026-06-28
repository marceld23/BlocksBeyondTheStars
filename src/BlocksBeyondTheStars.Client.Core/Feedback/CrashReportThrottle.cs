// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Feedback
{
    /// <summary>
    /// Decides whether a client crash is worth reporting, so a single recurring fault — a NullReference thrown
    /// from <c>Update</c> fires every frame — can't flood the disk or the website with thousands of identical
    /// reports. Two gates: the same crash <em>signature</em> is reported once per session (dedup), and a hard
    /// per-session cap bounds the total. Unity-free + thread-safe (the threaded log callback can fire off the
    /// main thread), so it lives in <c>Client.Core</c> and is unit-tested headless.
    /// </summary>
    public sealed class CrashReportThrottle
    {
        private readonly int _maxPerSession;
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly object _gate = new object();
        private int _reported;

        /// <param name="maxPerSession">Hard cap on distinct crashes reported in one play session.</param>
        public CrashReportThrottle(int maxPerSession = 20)
        {
            _maxPerSession = Math.Max(1, maxPerSession);
        }

        /// <summary>Number of distinct crashes accepted so far this session.</summary>
        public int Reported
        {
            get { lock (_gate) { return _reported; } }
        }

        /// <summary>True the FIRST time a given signature is seen (until the per-session cap is hit), false for
        /// repeats and once the cap is reached. A null/empty signature is treated as its own bucket so an
        /// untagged crash is still reported once.</summary>
        public bool ShouldReport(string signature)
        {
            string key = signature ?? string.Empty;
            lock (_gate)
            {
                if (_reported >= _maxPerSession)
                {
                    return false;
                }

                if (!_seen.Add(key))
                {
                    return false; // already reported this exact crash this session
                }

                _reported++;
                return true;
            }
        }

        /// <summary>Builds a stable de-dup signature from the exception message + the first frame of the stack,
        /// so the same fault collapses to one report even though messages can carry varying tail detail.</summary>
        public static string Signature(string condition, string stackTrace)
        {
            string firstFrame = string.Empty;
            if (!string.IsNullOrEmpty(stackTrace))
            {
                int nl = stackTrace.IndexOf('\n');
                firstFrame = (nl >= 0 ? stackTrace.Substring(0, nl) : stackTrace).Trim();
            }

            return (condition ?? string.Empty).Trim() + " @ " + firstFrame;
        }
    }
}
