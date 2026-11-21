// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client.Feel
{
    /// <summary>
    /// One figure's own way of walking (#3882): a villager's cadence, arm swing, bounce and lean, drawn once from its look
    /// seed, so a crowd is not a drill squad. <see cref="Neutral"/> is the stock gait (players, previews, machines).
    /// </summary>
    public readonly struct GaitStyle
    {
        public GaitStyle(float cadence, float armSwing, float armAsymmetry, float elbowRest, float bounce, float lean, float armOut,
            float idlePace, float idleOffset)
        {
            Cadence = cadence;
            ArmSwing = armSwing;
            ArmAsymmetry = armAsymmetry;
            ElbowRest = elbowRest;
            Bounce = bounce;
            Lean = lean;
            ArmOut = armOut;
            IdlePace = idlePace;
            IdleOffset = idleOffset;
        }

        /// <summary>The stock gait: every figure without a look seed walks like this.</summary>
        public static GaitStyle Neutral => new GaitStyle(1f, 1f, 0f, 3f, 1f, 0.5f, 4f, 1f, 0f);

        /// <summary>× the step rate. The leg swing follows the ground the body covers, so a slower cadence is a longer
        /// stride — the feet stay planted either way.</summary>
        public float Cadence { get; }

        /// <summary>× the arm swing.</summary>
        public float ArmSwing { get; }

        /// <summary>The left arm swings this fraction more than the right (negative: less) — nobody is symmetric.</summary>
        public float ArmAsymmetry { get; }

        /// <summary>Degrees the elbows hang bent at rest, beyond the base bend.</summary>
        public float ElbowRest { get; }

        /// <summary>× the body's bounce over the steps (not the dip that keeps the feet on the ground).</summary>
        public float Bounce { get; }

        /// <summary>Degrees the upper body leans forward while walking.</summary>
        public float Lean { get; }

        /// <summary>Degrees the arms hang away from the body.</summary>
        public float ArmOut { get; }

        /// <summary>× the speed of the idle weight shift.</summary>
        public float IdlePace { get; }

        /// <summary>Phase of the idle weight shift (radians), so a standing crowd does not shift in step.</summary>
        public float IdleOffset { get; }
    }

    /// <summary>
    /// The humanoid figure's walk, Unity-free (#3882–#3886): the curves <c>PlayerAvatar</c> poses every villager, player and
    /// bandit with. Angles are degrees; a leg or an arm angle is positive BACK, negative forward (the rig's convention). The
    /// gait phase <c>p</c> runs 0 → 1 per stride of one leg: 0 is its heel strike (the leg furthest forward), 0.5 its toe-off
    /// (furthest back); stance is the first half, swing the second.
    /// <list type="bullet">
    /// <item>The knee gives a little after heel strike and folds deep in the swing — it used to bend hardest with the leg at
    /// its rearmost point and to swing forward straight (a stilt walk), and a stroll bent it barely 18°.</item>
    /// <item>The elbow bends on the arm's forward swing (it used to bend on the back swing).</item>
    /// <item>The foot is held level through the stance, its toes come up for the heel strike and its heel lifts at push-off.</item>
    /// <item>The body dips at double support — as far as the spread legs ask, so the feet stay on the floor — and sways over the
    /// standing foot; the hips turn with the stride and the shoulders against them.</item>
    /// </list>
    /// </summary>
    public static class HumanGait
    {
        /// <summary>How lively an android or a G.D.S. guardian moves (#3886): stiffer than a villager (1), less stiff than the
        /// old rigid figure (0).</summary>
        public const float MachineLiveliness = 0.5f;

        /// <summary>The figure's leg length at scale 1 (the hip pivots' height).</summary>
        public const float LegLength = 0.92f;

        private const float Tau = (float)(2.0 * Math.PI);
        private const float DegToRad = (float)(Math.PI / 180.0);

        /// <summary>The look seed's gait. Seed 0 (players, previews, machines) walks <see cref="GaitStyle.Neutral"/>.</summary>
        public static GaitStyle StyleFor(int seed)
        {
            if (seed == 0)
            {
                return GaitStyle.Neutral;
            }

            uint h = Mix((uint)seed);
            return new GaitStyle(
                cadence: Range(h, 0, 0.9f, 1.1f),
                armSwing: Range(h, 1, 0.75f, 1.25f),
                armAsymmetry: Range(h, 2, -0.12f, 0.12f),
                elbowRest: Range(h, 3, 0f, 8f),
                bounce: Range(h, 4, 0.7f, 1.35f),
                lean: Range(h, 5, -1f, 3f),
                armOut: Range(h, 6, 2f, 6f),
                idlePace: Range(h, 7, 0.8f, 1.25f),
                idleOffset: Range(Mix(h ^ 0x9E3779B9u), 0, 0f, Tau));
        }

        /// <summary>The left leg's gait phase for the rig's walk phase (radians): the rig swings the left leg as
        /// <c>sin(walkPhase)</c>, which is <see cref="Hip"/> of this phase.</summary>
        public static float Phase(float walkPhaseRadians) => Frac((walkPhaseRadians + (Tau / 4f)) / Tau);

        /// <summary>The leg's swing at <paramref name="p"/>, −1 (forward, heel strike) … +1 (back, toe-off).</summary>
        public static float Hip(float p) => -(float)Math.Cos(Tau * p);

        /// <summary>How far the walk has set in (0 standing … 1): the deep knee fold of a swing reads as marching on the spot
        /// until the body really moves.</summary>
        public static float WalkIn(float moving) => Clamp01(moving / 0.15f);

        /// <summary>The knee's bend (≥ 0) at <paramref name="p"/>: the loading give after heel strike, the fold of the swing.
        /// <paramref name="moving"/> (0 … 1) and <paramref name="run"/> (0 … 1) are the rig's walk and sprint blends.</summary>
        public static float Knee(float p, float moving, float run)
        {
            float walkIn = WalkIn(moving);
            float swing = (Lerp(40f, 58f, moving) + (32f * run)) * walkIn;
            float load = (6f + (8f * moving)) * (1f - (0.5f * run)) * walkIn;
            return (load * Bump(p, 0.12f, 0.12f)) + (swing * Bump(p, 0.68f, 0.32f));
        }

        /// <summary>The elbow's bend (≥ 0; the rig turns it negative = forward). <paramref name="forward"/> is how far the
        /// arm is swung forward (0 at or behind the body … 1 at the front of its swing).</summary>
        public static float Elbow(float forward, float moving, float run, float rest)
        {
            float f = Clamp01(forward);
            return 10f + rest + ((8f + (18f * moving)) * f) + (run * (40f + (20f * f)));
        }

        /// <summary>The foot's pitch against the ground (degrees, + = toes down) at <paramref name="p"/>: toes up for the heel
        /// strike, flat through the stance, the heel off at push-off, the toes lifted clear in the swing.</summary>
        public static float FootPitch(float p, float moving, float run)
        {
            float k = WalkIn(moving) * Lerp(0.6f, 1f, moving) * (1f + (0.3f * run));
            return k * Keyed(Frac(p));
        }

        /// <summary>How far the body drops (≥ 0, blocks) at <paramref name="p"/>: lowest at double support. The first term is
        /// what the spread legs ask (<paramref name="legLength"/> × (1 − cos swing)), so the feet stay on the floor; the second
        /// is a little bounce of the figure's own (<paramref name="bounce"/> × <paramref name="liveliness"/>).</summary>
        public static float BodyDrop(float p, float hipAmpDeg, float legLength, float moving, float run, float bounce, float liveliness)
        {
            float scale = legLength / LegLength;
            float swing = hipAmpDeg * Math.Abs(Hip(p)) * DegToRad;
            float spread = Math.Min(legLength * (1f - (float)Math.Cos(swing)), 0.07f * scale);
            float twice = 0.5f + (0.5f * (float)Math.Cos(2f * Tau * p)); // 1 at double support, 0 over the standing foot
            float extra = (0.012f + (0.03f * run)) * bounce * liveliness * scale * twice;
            return (spread + extra) * WalkIn(moving);
        }

        /// <summary>The body's side shift (−1 = over the left foot … +1 = over the right) at <paramref name="p"/>.</summary>
        public static float Sway(float p) => -(float)Math.Sin(Tau * p);

        /// <summary>The hips' turn with the stride (+1 = the left hip forward, at the left heel strike).</summary>
        public static float HipTurn(float p) => (float)Math.Cos(Tau * p);

        /// <summary>The idle weight shift at time <paramref name="t"/>: −1 … +1, holding on one leg for a while (+1 = the
        /// weight on the left), then moving over to the other.</summary>
        public static float IdleShift(float t, GaitStyle style)
            => Math.Max(-1f, Math.Min(1f, 1.4f * (float)Math.Sin((t * 0.21f * style.IdlePace) + style.IdleOffset)));

        /// <summary>A raised-cosine bump: 1 at <paramref name="centre"/>, 0 from <paramref name="halfWidth"/> away; the phase
        /// wraps round (a bump at 0.95 reaches into 0.05).</summary>
        public static float Bump(float p, float centre, float halfWidth)
        {
            float d = Frac(p - centre + 0.5f) - 0.5f;
            return Math.Abs(d) >= halfWidth ? 0f : 0.5f + (0.5f * (float)Math.Cos(Math.PI * d / halfWidth));
        }

        // The foot pitch over the stride, eased between keys: heel strike (toes up), flat, push-off (heel up), toe lift.
        private static readonly float[] FootKeys = { 0f, 0.10f, 0.40f, 0.58f, 0.70f, 0.85f, 1f };
        private static readonly float[] FootValues = { -12f, 0f, 0f, 24f, 4f, -6f, -12f };

        private static float Keyed(float p)
        {
            for (int i = 1; i < FootKeys.Length; i++)
            {
                if (p <= FootKeys[i])
                {
                    float u = (p - FootKeys[i - 1]) / (FootKeys[i] - FootKeys[i - 1]);
                    u = u * u * (3f - (2f * u));
                    return Lerp(FootValues[i - 1], FootValues[i], u);
                }
            }

            return FootValues[FootValues.Length - 1];
        }

        private static uint Mix(uint x)
        {
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x;
        }

        // Four bits per field out of one hash would repeat too soon: each field re-mixes the hash with its own index.
        private static float Range(uint h, uint field, float lo, float hi)
            => Lerp(lo, hi, (Mix(h + (field * 0x632BE5ABu)) & 0xFFFFu) / 65535f);

        private static float Frac(float x) => x - (float)Math.Floor(x);

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;

        private static float Lerp(float a, float b, float t) => a + ((b - a) * Clamp01(t));
    }
}
