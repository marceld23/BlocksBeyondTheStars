// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3860: how the body moves up and down in water. Swimming used to bounce: the climb-out hop (#131) fired a full ground
    /// jump every time the head broke the surface while Jump and forward were held — in open water too — and there was no
    /// way to stay at the surface (idle sank, Jump popped the chest out and gravity threw it back) and no key to dive.
    /// Now the body floats at the surface, hops out only where a bank stands in front of it, and the Descend key (Ctrl / C,
    /// pad B — the same key that sinks in zero-g and flight) dives. Sprint swims faster in a level swim pose
    /// (<see cref="SprintPoseSpeed"/>). Client-only like all on-foot movement: the server trusts the reported position.
    /// </summary>
    public static class SwimRules
    {
        /// <summary>Dive speed (m/s) while the Descend key is held.</summary>
        public const float DiveSpeed = 3f;

        /// <summary>How fast (m/s²) the vertical speed eases toward the float at the surface — stiffer than the swim's own
        /// easing so a rise from below settles at the waterline instead of popping the chest out of the water.</summary>
        public const float SurfaceAccel = 24f;

        /// <summary>Horizontal speed (m/s) above which a swimming figure lies level in the water — between the plain swim
        /// (walk 6 × 0.62 ≈ 3.7) and the sprint swim (× 1.5 ≈ 5.6), so it reads the same for the own figure and for a
        /// remote one, whose speed is only measured from its motion.</summary>
        public const float SprintPoseSpeed = 4.6f;

        /// <summary>
        /// Whether the climb-out hop fires this frame: only at the surface (<paramref name="atSurface"/>: open air over the
        /// head), while pushing, and only where something stands in front to climb — a low bank the body walks into
        /// (<paramref name="ledgeAhead"/>), or, with Jump held, any bank face ahead (<paramref name="bankAhead"/>). Open
        /// water never hops.
        /// </summary>
        public static bool ClimbOutHop(bool pushing, bool atSurface, bool jumpHeld, bool ledgeAhead, bool bankAhead)
            => pushing && atSurface && (ledgeAhead || (jumpHeld && bankAhead));

        /// <summary>
        /// The vertical speed the body eases toward in water: Descend dives; at the surface the body floats (Jump or not —
        /// it is already up); below it Jump rises and nothing held sinks slowly. <paramref name="upSpeed"/> and
        /// <paramref name="sinkSpeed"/> are the controller's swim speeds (a gas giant's dense gas passes its own).
        /// </summary>
        public static float VerticalTarget(bool atSurface, bool jumpHeld, bool diveHeld, float upSpeed, float sinkSpeed)
        {
            if (diveHeld)
            {
                return -DiveSpeed;
            }

            if (atSurface)
            {
                return 0f;
            }

            return jumpHeld ? upSpeed : -sinkSpeed;
        }

        /// <summary>How fast the vertical speed eases (m/s²): stiffer while floating at the surface, the swim's own
        /// <paramref name="swimAccel"/> otherwise.</summary>
        public static float VerticalAccel(bool atSurface, bool diveHeld, float swimAccel)
            => atSurface && !diveHeld ? System.Math.Max(swimAccel, SurfaceAccel) : swimAccel;

        /// <summary>Whether a swimming figure moving at <paramref name="speed"/> m/s lies level (the sprint swim pose).</summary>
        public static bool LiesLevel(bool swimming, float speed) => swimming && speed > SprintPoseSpeed;
    }
}
