// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// When a waiting planet map bake may start on a desktop worker (#2761). Entering space queues a map of every body of
    /// the system for the orbit spheres; the priority only orders which bake starts next, so the landing map — the one map
    /// the player is waiting for — used to queue behind the two orbit bakes already running. Interactive requests (at
    /// <see cref="InteractivePriority"/> or above) therefore get a slot of their own on top of the regular ones. The
    /// browser bakes in time slices on the main thread and serves the highest priority first every frame, so it needs
    /// no slots.
    /// </summary>
    public static class PlanetMapSchedule
    {
        /// <summary>Worker bakes running at once for the background maps (orbit spheres, sky discs).</summary>
        public const int RegularSlots = 2;

        /// <summary>Extra worker bakes reserved for interactive requests.</summary>
        public const int InteractiveSlots = 1;

        /// <summary>The priority from which a request counts as interactive: a map the player has open and is looking at
        /// (the landing-pad map and the coarse map it shows first).</summary>
        public const int InteractivePriority = 20;

        /// <summary>True when a waiting bake of <paramref name="priority"/> may start now, with <paramref name="running"/>
        /// bakes running in all and <paramref name="runningInteractive"/> of them interactive.</summary>
        public static bool MayStart(int priority, int running, int runningInteractive)
        {
            if (running < RegularSlots)
            {
                return true;
            }

            return priority >= InteractivePriority && runningInteractive < InteractiveSlots;
        }
    }
}
