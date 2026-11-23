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
    /// pad B — the same key that sinks in zero-g and flight) dives. Client-only like all on-foot movement: the server trusts
    /// the reported position.
    /// <para>#3923: the float settles at <see cref="FloatDepth"/> — it used to stop wherever the rise ran out once the top of
    /// the head left the water, which left the server's breath point (+1.5) a few centimetres under water: the tank drained
    /// while the player floated "with the head above the water", as the manual promises.</para>
    /// <para>#3924: the body tilts with the swim (<see cref="BodyTilt"/>) — upright while treading water, lying forward in a
    /// crawl while it swims, head first while it dives. Pose only: the collision capsule stays upright, and a remote figure's
    /// tilt comes from its measured motion like every other pose (no network change).</para>
    /// </summary>
    public static class SwimRules
    {
        /// <summary>Dive speed (m/s) while the Descend key is held.</summary>
        public const float DiveSpeed = 3f;

        /// <summary>How fast (m/s²) the vertical speed eases toward the float at the surface — stiffer than the swim's own
        /// easing so a rise from below settles at the waterline instead of popping the chest out of the water.</summary>
        public const float SurfaceAccel = 24f;

        /// <summary>#3923: how deep (m) the feet float under the water surface: the server's breath point (+1.5) is 0.2 m above
        /// the water and the first-person eye (+1.6) 0.3 m, while the chest (+1.1, where the body counts as swimming) stays
        /// 0.2 m under it — so the float neither drains the tank nor leaves the water.</summary>
        public const float FloatDepth = 1.3f;

        /// <summary>How hard the float pulls the feet toward <see cref="FloatDepth"/>: m/s per metre off — a settle of about a
        /// quarter of a second, with no overshoot.</summary>
        public const float FloatSpring = 4f;

        /// <summary>The float's fastest correction (m/s), up or down.</summary>
        public const float FloatMaxSpeed = 2f;

        /// <summary>The plain swim's speed (m/s): the walk (6) × the water's 0.62.</summary>
        public const float PlainSwimSpeed = 3.7f;

        /// <summary>The sprint swim's speed (m/s): the plain swim × the run's factor.</summary>
        public const float SprintSwimSpeed = PlainSwimSpeed * SprintState.SpeedFactor;

        /// <summary>Below this forward speed (m/s) the swimmer treads water upright.</summary>
        public const float StillSpeed = 0.6f;

        /// <summary>The body's tilt (degrees forward) treading water at rest.</summary>
        public const float RestTilt = 4f;

        /// <summary>The tilt treading water at <see cref="StillSpeed"/>.</summary>
        public const float TreadTilt = 10f;

        /// <summary>The tilt of the plain swim — a crawl just under the surface.</summary>
        public const float PlainTilt = 62f;

        /// <summary>The tilt of the sprint swim — nearly level.</summary>
        public const float SprintTilt = 78f;

        /// <summary>The tilt of a dive: head first, a little past level.</summary>
        public const float DiveTilt = 100f;

        /// <summary>How far (degrees) a swimmer leans back while it pushes itself backwards at the plain swim's speed.</summary>
        public const float BackLean = -12f;

        /// <summary>The tilt a rise toward the surface allows at most — the head comes up first.</summary>
        public const float RiseTilt = 25f;

        /// <summary>The sink speed (m/s) from which the body starts to dive head first, and the one at which it fully has —
        /// past the idle sink (1.5 m/s), which stays upright, and below the dive (<see cref="DiveSpeed"/>).</summary>
        public const float DiveFrom = 1.6f, DiveFull = 2.6f;

        /// <summary>The rise speed (m/s) from which the head starts to come up, and the one at which it fully has (the swim
        /// up is 4 m/s).</summary>
        public const float RiseFrom = 1f, RiseFull = 2.5f;

        /// <summary>
        /// Whether the climb-out hop fires this frame: only at the surface (<paramref name="atSurface"/>: open air over the
        /// head), while pushing, and only where something stands in front to climb — a low bank the body walks into
        /// (<paramref name="ledgeAhead"/>), or, with Jump held, any bank face ahead (<paramref name="bankAhead"/>). Open
        /// water never hops.
        /// </summary>
        public static bool ClimbOutHop(bool pushing, bool atSurface, bool jumpHeld, bool ledgeAhead, bool bankAhead)
            => pushing && atSurface && (ledgeAhead || (jumpHeld && bankAhead));

        /// <summary>
        /// #3956: whether the swimmer is at the surface — open air over the head (the cell at feet + 1.9): neither water nor
        /// another liquid (<paramref name="aboveLiquid"/>) nor a block with a collider (<paramref name="aboveColliding"/>).
        /// A ceiling over flooded water used to count as the surface: since the surface floats (#3860) and the float settles
        /// <see cref="FloatDepth"/> under the first cell that is not water (#3923), a swimmer in a flooded tunnel hung under
        /// its ceiling instead of sinking. Under a ceiling the body now sinks slowly like anywhere below the surface.
        /// </summary>
        public static bool OpenSurface(bool aboveLiquid, bool aboveColliding) => !aboveLiquid && !aboveColliding;

        /// <summary>
        /// The vertical speed the body eases toward in water: Descend dives; at the surface the body floats (Jump or not —
        /// it is already up) toward <see cref="FloatDepth"/> (<see cref="FloatTarget"/>; <paramref name="feetDepth"/> is how
        /// far the feet are under the surface); below it Jump rises and nothing held sinks slowly. <paramref name="upSpeed"/>
        /// and <paramref name="sinkSpeed"/> are the controller's swim speeds (a gas giant's dense gas passes its own).
        /// </summary>
        public static float VerticalTarget(bool atSurface, bool jumpHeld, bool diveHeld, float upSpeed, float sinkSpeed,
            float feetDepth = FloatDepth)
        {
            if (diveHeld)
            {
                return -DiveSpeed;
            }

            if (atSurface)
            {
                return FloatTarget(feetDepth);
            }

            return jumpHeld ? upSpeed : -sinkSpeed;
        }

        /// <summary>#3923: the float's vertical speed for feet <paramref name="feetDepth"/> metres under the surface — up while
        /// they hang deeper than <see cref="FloatDepth"/>, down while they ride higher, at most <see cref="FloatMaxSpeed"/>.</summary>
        public static float FloatTarget(float feetDepth)
            => Clamp((feetDepth - FloatDepth) * FloatSpring, -FloatMaxSpeed, FloatMaxSpeed);

        /// <summary>How fast the vertical speed eases (m/s²): stiffer while floating at the surface, the swim's own
        /// <paramref name="swimAccel"/> otherwise.</summary>
        public static float VerticalAccel(bool atSurface, bool diveHeld, float swimAccel)
            => atSurface && !diveHeld ? System.Math.Max(swimAccel, SurfaceAccel) : swimAccel;

        /// <summary>
        /// #3924: how far (degrees, forward; negative leans back) a swimming body tilts for its speed along the way it faces
        /// (<paramref name="forwardSpeed"/>, m/s, negative backwards) and its vertical speed (<paramref name="verticalSpeed"/>):
        /// nearly still it treads water upright (<see cref="RestTilt"/>–<see cref="TreadTilt"/>), the plain swim lies at
        /// <see cref="PlainTilt"/>, the sprint at <see cref="SprintTilt"/>, a dive goes head first (<see cref="DiveTilt"/>),
        /// a rise brings the head up (at most <see cref="RiseTilt"/>) and a swim backwards leans back a little.
        /// </summary>
        public static float BodyTilt(float forwardSpeed, float verticalSpeed)
        {
            float tilt = forwardSpeed >= 0f
                ? ForwardTilt(forwardSpeed)
                : Lerp(RestTilt, BackLean, -forwardSpeed / PlainSwimSpeed);
            tilt = Lerp(tilt, DiveTilt, SmoothStep(DiveFrom, DiveFull, -verticalSpeed));
            float rise = SmoothStep(RiseFrom, RiseFull, verticalSpeed);
            return Lerp(tilt, System.Math.Min(tilt, RiseTilt), rise);
        }

        /// <summary>The crawl's share of the stroke for a body tilted <paramref name="tilt"/> degrees: none while it swims
        /// fairly upright (the breast stroke), all of it from the plain swim's tilt on.</summary>
        public static float CrawlBlend(float tilt) => SmoothStep(25f, 60f, tilt);

        /// <summary>The treading pose's share (sculling arms, slow kicks) for a body tilted <paramref name="tilt"/> degrees: all
        /// of it while the body is near upright, none once it swims.</summary>
        public static float TreadBlend(float tilt) => 1f - SmoothStep(8f, 24f, System.Math.Abs(tilt));

        /// <summary>
        /// #3924: the short controls line while swimming — its first part (moving) and its last (the menu, or whatever the
        /// moment puts there) around <paramref name="swimKeys"/> ("Space up · Ctrl dive"), so it still names four keys and no
        /// longer calls the key that swims up "jump". Every language writes the short lines as " · "-separated parts; a line
        /// of one part keeps it and adds the keys.
        /// </summary>
        public static string SwimControlsLine(string shortLine, string? swimKeys)
        {
            if (swimKeys is null || swimKeys.Length == 0 || string.IsNullOrEmpty(shortLine))
            {
                return shortLine;
            }

            const string Sep = " · ";
            int first = shortLine.IndexOf(Sep, System.StringComparison.Ordinal);
            if (first < 0)
            {
                return shortLine + Sep + swimKeys;
            }

            int last = shortLine.LastIndexOf(Sep, System.StringComparison.Ordinal);
            return shortLine.Substring(0, first) + Sep + swimKeys + shortLine.Substring(last);
        }

        private static float ForwardTilt(float speed)
        {
            if (speed <= StillSpeed)
            {
                return Lerp(RestTilt, TreadTilt, speed / StillSpeed);
            }

            if (speed <= PlainSwimSpeed)
            {
                return Lerp(TreadTilt, PlainTilt, SmoothStep(StillSpeed, PlainSwimSpeed, speed));
            }

            return Lerp(PlainTilt, SprintTilt, (speed - PlainSwimSpeed) / (SprintSwimSpeed - PlainSwimSpeed));
        }

        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;

        private static float Lerp(float a, float b, float t) => a + ((b - a) * Clamp(t, 0f, 1f));

        private static float SmoothStep(float from, float to, float x)
        {
            float t = Clamp((x - from) / (to - from), 0f, 1f);
            return t * t * (3f - (2f * t));
        }
    }
}
