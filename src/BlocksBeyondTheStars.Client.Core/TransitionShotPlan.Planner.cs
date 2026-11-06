// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The search of <see cref="TransitionShotPlan"/>: the candidate spots, the sampled stage, the scoring.</summary>
    public sealed partial class TransitionShotPlan
    {
        private const int DirectionCount = 8, DistanceCount = 2, HeightCount = 3;

        /// <summary>Candidate spots per plan: 8 directions × 2 distances × 3 heights.</summary>
        public const int CandidateCount = DirectionCount * DistanceCount * HeightCount;

        // The score is the share of the shot played outside, a bonus for the classic glide between the spot and the eyes,
        // and a small preference for the low, rear-quarter, far spot (the shot as it always was when nothing is in the way).
        // The glide outweighs any direction (another low spot with a clear way home beats a blink) but not a height step (a
        // low shot that blinks home beats a crane that glides); a higher spot has to see more of the shot to win.
        private const float GlideBonus = 0.035f;
        private const float HeightPenalty = 0.04f, DirectionPenalty = 0.004f, DistancePenalty = 0.002f;

        /// <summary>No camera inside the hull's box (or this close to it), whatever the samplers say.</summary>
        private const float BoxMargin = 1f;

        /// <summary>The camera's push-in toward the hull while it settles (a quarter of the way).</summary>
        private const float PushIn = 0.25f;

        /// <summary>The direction candidates, best first: rear quarters (the engines in frame), front quarters, sides, rear, front.</summary>
        private static readonly float[] DirX = { 0.8f, -0.8f, 1f, -1f, 1f, -1f, 0f, 0f };
        private static readonly float[] DirZ = { -1f, -1f, 0.6f, 0.6f, 0f, 0f, -1f, 1f };

        /// <summary>The lift-off beats a cut outside lands on (the first one from which the spot sees the rest).</summary>
        private static readonly float[] LiftoffBeats =
        {
            TransitionFlightPath.LiftAt, TransitionFlightPath.ClimbAt, TransitionFlightPath.BoomAt,
        };

        /// <summary>Plans the touchdown: the stand-in sinking from <see cref="TransitionShotInput.Drop"/> onto its pad, the
        /// settle with its push-in, then home to the eyes.</summary>
        public static TransitionShotPlan ForTouchdown(TransitionShotInput input)
            => Build(input, TransitionShotStage.Touchdown, TransitionFlightPath.TouchdownSeconds);

        /// <summary>Plans the lift-off: the glide out of the eyes, the hull rising and climbing away up to the clouds.</summary>
        public static TransitionShotPlan ForLiftoff(TransitionShotInput input)
            => Build(input, TransitionShotStage.Liftoff, TransitionFlightPath.CloudsAt);

        /// <summary>The moment of sample <paramref name="index"/> on a stage of <paramref name="duration"/> seconds.</summary>
        public static float TimeAt(int index, float duration) => duration * index / (SampleCount - 1);

        /// <summary>The touchdown's outside stretch when its first <paramref name="visibleSamples"/> samples are visible: to the
        /// end when all are, else up to the last visible one (back on the dust beat when that is near), and nothing when
        /// that would be shorter than <see cref="MinOutsideSeconds"/>.</summary>
        public static (float From, float To) TouchdownWindow(int visibleSamples, float duration, float dustBeat)
        {
            if (visibleSamples >= SampleCount)
            {
                return (0f, duration);
            }

            float last = visibleSamples > 0 ? TimeAt(visibleSamples - 1, duration) : -1f;
            if (last < MinOutsideSeconds)
            {
                return (0f, 0f);
            }

            bool snap = dustBeat >= MinOutsideSeconds && dustBeat <= last && last - dustBeat <= BeatSnapSeconds;
            return (0f, snap ? dustBeat : last);
        }

        /// <summary>The lift-off's outside stretch when every sample from <paramref name="firstVisible"/> on is visible: all
        /// of it with a clear glide out of the eyes; else from the first beat at or after that sample (or the sample itself
        /// past the last beat), and nothing when less than <see cref="MinTailSeconds"/> would be left.</summary>
        public static (float From, float To) LiftoffWindow(int firstVisible, float duration, bool glideClear)
        {
            if (firstVisible >= SampleCount)
            {
                return (0f, 0f);
            }

            if (firstVisible <= 0 && glideClear)
            {
                return (0f, duration);
            }

            float from = TimeAt(Math.Max(0, firstVisible), duration);
            float start = from;
            foreach (float beat in LiftoffBeats)
            {
                if (beat >= from)
                {
                    start = beat;
                    break;
                }
            }

            return duration - start < MinTailSeconds ? (0f, 0f) : (start, duration);
        }

        private static TransitionShotPlan Build(TransitionShotInput input, TransitionShotStage stage, float duration)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            var plan = new TransitionShotPlan(stage, duration);
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < CandidateCount; i++)
            {
                int h = i / (DirectionCount * DistanceCount), d = i / DistanceCount % DirectionCount, r = i % DistanceCount;
                var spot = SpotFor(input, d, r, h);
                var o = stage == TransitionShotStage.Touchdown
                    ? EvaluateTouchdown(input, duration, spot)
                    : EvaluateLiftoff(input, duration, spot);
                plan.CandidatesChecked = i + 1;
                if (o.OutsideTo <= o.OutsideFrom)
                {
                    continue;
                }

                bool glide = o.GlideClear && (stage == TransitionShotStage.Touchdown ? o.OutsideTo >= duration : o.OutsideFrom <= 0f);
                float share = (o.OutsideTo - o.OutsideFrom) / duration;
                float score = share + (glide ? GlideBonus : 0f) - (h * HeightPenalty + d * DirectionPenalty + r * DistancePenalty);
                if (score > bestScore)
                {
                    bestScore = score;
                    plan.Adopt(o, spot, d, r, h);
                }

                if (glide && share >= 1f)
                {
                    break; // every later candidate carries a bigger penalty — nothing can beat this one any more
                }
            }

            return plan;
        }

        private void Adopt(Outcome o, Point spot, int direction, int distance, int height)
        {
            HasSpot = true;
            SpotX = spot.X;
            SpotY = spot.Y;
            SpotZ = spot.Z;
            Direction = direction;
            DistanceStep = distance;
            HeightStep = height;
            VisibleFrom = o.VisibleFrom;
            VisibleTo = o.VisibleTo;
            OutsideFrom = o.OutsideFrom;
            OutsideTo = o.OutsideTo;
            GlideClear = o.GlideClear;
        }

        /// <summary>Candidate (direction <paramref name="d"/>, distance step <paramref name="r"/>, height step
        /// <paramref name="h"/>): around the hull at its radius plus <see cref="TransitionShotInput.Distance"/> (or 70 % of
        /// that), at the ground height, just over the roofline, or high up on a crane — both scaled with the hull.</summary>
        private static Point SpotFor(TransitionShotInput input, int d, int r, int h)
        {
            float len = (float)Math.Sqrt(DirX[d] * DirX[d] + DirZ[d] * DirZ[d]);
            float radius = (float)Math.Sqrt(input.ExtentX * input.ExtentX + input.ExtentY * input.ExtentY + input.ExtentZ * input.ExtentZ);
            float reach = radius + input.Distance;
            float dist = r == 0 ? reach : reach * 0.7f;
            float height = h == 0 ? input.GroundHeight
                : h == 1 ? Math.Max(6f, 2f * input.ExtentY + 1f)
                : Math.Max(14f, 2f * input.ExtentY + 8f);
            return new Point(input.CenterX + DirX[d] / len * dist, input.CenterY - input.ExtentY + height,
                input.CenterZ + DirZ[d] / len * dist);
        }
    }
}
