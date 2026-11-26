// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client.FarTerrain
{
    /// <summary>
    /// How many threads sample far-terrain patches on a desktop client (#4048). The far view used to sample on one thread,
    /// one patch at a time; a 1024-block view is about a hundred patches and took ten seconds to fill on the reference
    /// laptop, a 2048-block view is close to three hundred — while most cores idled.
    /// <para>Every worker owns a world generator of its own (a generator is not thread-safe) with its column memos, so
    /// the count stays small and a machine with little memory keeps one. A quarter of the logical processors beyond the
    /// four the main thread, the render thread and the bundled server keep busy; the workers run below normal priority,
    /// so the chunk mesher goes first.</para>
    /// The browser has no threads and samples inline within a frame budget; this rule is not asked there.
    /// </summary>
    public static class FarPatchWorkers
    {
        /// <summary>One worker on every machine: what the far view always had.</summary>
        public const int Minimum = 1;

        /// <summary>At most this many, whatever the machine.</summary>
        public const int Maximum = 3;

        /// <summary>Below this much system memory (MB) the far view keeps one worker (one generator).</summary>
        public const int MinMemoryMb = 12 * 1024;

        /// <summary>The worker count for a machine with <paramref name="logicalProcessors"/> logical processors and
        /// <paramref name="systemMemoryMb"/> MB of memory (0 or less = unknown, which keeps one).</summary>
        public static int For(int logicalProcessors, int systemMemoryMb)
        {
            if (systemMemoryMb < MinMemoryMb)
            {
                return Minimum;
            }

            return Math.Max(Minimum, Math.Min(Maximum, (logicalProcessors - 4) / 4));
        }
    }
}
