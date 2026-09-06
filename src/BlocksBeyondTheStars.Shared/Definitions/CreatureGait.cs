// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The footfall pattern a body walks with. Distinct from <see cref="LocomotionStyle"/> (the RHYTHM the
/// server roams with) and <see cref="MotionClass"/> (the MECHANICS it moves by): the gait says which foot
/// leaves the ground when, and it changes with speed on the same animal.
/// </summary>
public enum Gait : byte
{
    Walk,        // lateral sequence — three feet down at any moment; the slow, heavy gait
    Trot,        // diagonal couplets — the mid-speed gait of most quadrupeds
    Bound,       // front pair together, hind pair together — the fast gait (never for giants/crawlers)
    Tripod,      // insect alternating tripod — 6+ legs at speed
    Metachronal, // a wave running from the rear legs forward — 6+ legs, slow crawl
    Paddle,      // fins/flippers beating left-right out of phase — swimmers
}

/// <summary>One leg's pose at a point in its cycle, in the leg's own frame.</summary>
public readonly struct LegPose
{
    /// <summary>Hip pitch in degrees, + = swung forward (toward the nose).</summary>
    public readonly float SwingDeg;

    /// <summary>0 on the ground, rising to 1 at mid-swing — how far the foot is lifted clear.</summary>
    public readonly float Lift01;

    /// <summary>0 straight, rising to 1 at mid-swing — how far the knee folds to shorten the leg.</summary>
    public readonly float Fold01;

    public LegPose(float swingDeg, float lift01, float fold01)
    {
        SwingDeg = swingDeg;
        Lift01 = lift01;
        Fold01 = fold01;
    }
}

/// <summary>
/// Pure, deterministic gait mathematics for the client's creature rig. The point of this type is the
/// <b>stance phase</b>: a planted foot must travel backwards at exactly the body's speed, which means the
/// cycle rate follows from speed ÷ stride length rather than being an independent wave. Everything a
/// blocky quadruped/hexapod needs to stop skating lives here; the renderer only applies the angles.
///
/// Deliberately Unity-free (netstandard2.1) so the same maths is unit-tested on the server side.
/// </summary>
public static class CreatureGait
{
    /// <summary>Speed at which a body is considered to be moving at all (normalised units).</summary>
    public const float MovingThreshold = 0.02f;

    /// <summary>Hysteresis band around a gait transition, so a creature holding a borderline speed does not
    /// flicker between two footfall patterns.</summary>
    public const float TransitionHysteresis = 0.05f;

    /// <summary>The gait a body uses at a given normalised speed (0 = standing, 1 = its top speed).
    /// <paramref name="legs"/> is the species' leg count, <paramref name="giant"/> its titan/heavy flag —
    /// giants stay in the walk far longer and never bound, crawlers never bound at all.</summary>
    public static Gait Select(MotionClass cls, int legs, bool giant, float speedNorm)
    {
        if (cls == MotionClass.Swimmer || legs <= 0)
        {
            return Gait.Paddle; // fins beat; a legless body has nothing else to phase
        }

        if (legs >= 6)
        {
            // Many short legs: a slow crawl runs a wave down the body, at speed it snaps to the tripod.
            return speedNorm < 0.3f ? Gait.Metachronal : Gait.Tripod;
        }

        if (legs <= 2)
        {
            return speedNorm < 0.35f ? Gait.Walk : Gait.Trot; // a biped's "trot" is its run
        }

        if (giant || cls == MotionClass.Crawler)
        {
            return speedNorm < 0.7f ? Gait.Walk : Gait.Trot; // a giant plods, and never bounds
        }

        return speedNorm < 0.35f ? Gait.Walk : speedNorm < 0.8f ? Gait.Trot : Gait.Bound;
    }

    /// <summary>Where in the shared cycle this leg's stance begins, in [0,1).
    /// <paramref name="side"/> 0 = left, 1 = right; <paramref name="row"/> 0 = the front-most pair.</summary>
    public static float PhaseOffset(Gait gait, int side, int row, int rows)
    {
        int s = side & 1;
        int r = row < 0 ? 0 : row;
        int n = rows < 1 ? 1 : rows;
        if (r >= n)
        {
            r = n - 1;
        }

        switch (gait)
        {
            case Gait.Walk:
                // Lateral sequence (LH → LF → RH → RF): the hind foot of a side leads its own front foot by a
                // quarter cycle, and the right side follows the left by a half.
                return Frac(s * 0.5f + 0.25f * RowLead(r, n));

            case Gait.Trot:
                // Diagonal couplets: left-front with right-hind.
                return ((s + r) & 1) == 0 ? 0f : 0.5f;

            case Gait.Bound:
                // Both front feet together, both hind feet together — the side does not matter.
                return (r & 1) == 0 ? 0f : 0.5f;

            case Gait.Tripod:
                // Insect tripod: (left-front, right-middle, left-hind) against the other three.
                return ((s + r) & 1) == 0 ? 0f : 0.5f;

            case Gait.Metachronal:
                // A wave from the rear legs forward, the two sides half a cycle apart.
                return Frac(s * 0.5f + (float)(n - 1 - r) / n);

            default: // Paddle
                return s * 0.5f;
        }
    }

    /// <summary>Fraction of the cycle a foot spends on the ground. Above 0.5 more than one foot per pair is
    /// planted at a time (the slow, stable gaits); below 0.5 the body has airborne moments.</summary>
    public static float DutyFactor(Gait gait) => gait switch
    {
        Gait.Walk => 0.62f,
        Gait.Trot => 0.5f,
        Gait.Bound => 0.4f,
        Gait.Tripod => 0.5f,
        Gait.Metachronal => 0.72f,
        _ => 0.5f, // Paddle
    };

    /// <summary>Ground distance one stride covers: the chord the foot sweeps through while planted.
    /// The renderer divides the body's speed by this to get the cycle rate — that division is what makes a
    /// planted foot travel backwards at exactly body speed instead of sliding.</summary>
    public static float StrideLength(float legLength, float ampDeg)
    {
        float len = legLength < 0.01f ? 0.01f : legLength;
        float amp = Clamp(ampDeg, 1f, 80f) * DegToRad;
        return 2f * len * (float)System.Math.Sin(amp);
    }

    /// <summary>The cycle rate (strides per second) that keeps a planted foot still on the ground while the
    /// body travels at <paramref name="speed"/>. Clamped so a crawling body still shows a slow idle beat and
    /// a very fast one does not blur.</summary>
    public static float CycleRate(float speed, float strideLength, float cadenceScale = 1f)
    {
        float stride = strideLength < 0.05f ? 0.05f : strideLength;
        float scale = Clamp(cadenceScale, 0.1f, 4f);
        return Clamp(System.Math.Abs(speed) / stride * scale, 0f, 6f);
    }

    /// <summary>The leg's pose at <paramref name="phase01"/> of its own cycle (0 = the foot plants forward).
    /// Stance sweeps linearly backwards — constant foot velocity, which is the whole point — and the swing
    /// eases the foot back to the front while lifting and folding it clear of the ground.</summary>
    public static LegPose Evaluate(float phase01, float duty, float ampDeg)
    {
        float p = Frac(phase01);
        float d = Clamp(duty, 0.15f, 0.9f);
        float amp = Clamp(ampDeg, 0f, 80f);

        if (p < d)
        {
            // Planted. The angle is NOT swept linearly — the foot's ground POSITION is (the hip travels over a
            // still foot), so the angle is the arcsine of a linear sweep. A linear angle would leave a ~15 %
            // velocity ripple across the stance at 30°, which is exactly the residual skate this type exists
            // to remove.
            float u = d <= 0f ? 0f : p / d;
            float reach = (float)System.Math.Sin(amp * DegToRad);
            float x = reach - 2f * reach * u;
            return new LegPose((float)System.Math.Asin(Clamp(x, -1f, 1f)) * RadToDeg, 0f, 0f);
        }

        // Swinging: eased return (smoothstep) so the foot leaves and lands softly, lifted and folded at the top.
        float v = (p - d) / (1f - d);
        float e = v * v * (3f - 2f * v);
        float arc = (float)System.Math.Sin(v * System.Math.PI);
        return new LegPose(-amp + 2f * amp * e, arc, arc);
    }

    /// <summary>Vertical body bob over the cycle, in [-1,1] — the caller scales it by leg length. Walking and
    /// trotting bodies dip twice per stride (once per support pair); a bounding body dips once.</summary>
    public static float BodyBob(float phase01, Gait gait)
    {
        float p = Frac(phase01);
        if (gait == Gait.Paddle)
        {
            return 0f;
        }

        double turns = gait == Gait.Bound ? 1.0 : 2.0;
        return (float)System.Math.Sin(p * turns * 2.0 * System.Math.PI);
    }

    /// <summary>Body roll over the cycle, in [-1,1] — weight shifting onto the loaded side. Only the gaits
    /// with a left/right support asymmetry roll; a bound and an insect tripod stay level.</summary>
    public static float BodyRoll(float phase01, Gait gait)
    {
        if (gait != Gait.Walk && gait != Gait.Trot)
        {
            return 0f;
        }

        return (float)System.Math.Sin(Frac(phase01) * 2.0 * System.Math.PI);
    }

    /// <summary>Whether this gait may ever be chosen for the given body — the guard the renderer asserts on.
    /// Giants and crawlers must never bound, and only 6+ legs use the insect patterns.</summary>
    public static bool IsAllowed(Gait gait, MotionClass cls, int legs, bool giant) => gait switch
    {
        Gait.Bound => legs is >= 3 and <= 5 && !giant && cls != MotionClass.Crawler,
        Gait.Tripod or Gait.Metachronal => legs >= 6,
        Gait.Paddle => cls == MotionClass.Swimmer || legs <= 0,
        _ => true,
    };

    private const float DegToRad = (float)(System.Math.PI / 180.0);
    private const float RadToDeg = (float)(180.0 / System.Math.PI);

    /// <summary>1 for the front row, falling to 0 at the rear — the lateral-sequence lead.</summary>
    private static float RowLead(int row, int rows) => rows <= 1 ? 0f : (float)(rows - 1 - row) / (rows - 1);

    private static float Frac(float v)
    {
        float f = v - (float)System.Math.Floor(v);
        return f < 0f ? f + 1f : f >= 1f ? 0f : f;
    }

    private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
}
