// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client.Cutscenes
{
    /// <summary>
    /// How the director's camera travels along a staged move over its shot (#2477): a <c>dolly</c> eases from its start to
    /// its end and settles there, a <c>flyover</c> eases along its arc (rising in the middle) and settles, a <c>pan</c> eases
    /// across its sweep and back again while a page-gated caption holds the shot. With "Camera motion" off every move is a
    /// still framing (the dolly's close end, the pan's and the flyover's middle). Pure maths with no allocation, so the
    /// camera rig calls it every frame and the paths are covered by plain .NET tests.
    /// </summary>
    public static class CutsceneMoves
    {
        /// <summary>The eased progress (0..1) of a one-way move: 0 at the shot's start, 1 at <paramref name="duration"/>,
        /// then it holds (settles). A shot without a length is already there.</summary>
        public static float Eased(float elapsed, float duration)
            => duration <= 0f ? 1f : CinematicTimeline.EaseInOut(elapsed / duration);

        /// <summary>The eased progress (0..1) of a move that goes back and forth: out over <paramref name="duration"/>, back
        /// over the next, and so on — the pan keeps turning gently while the reader is still reading.</summary>
        public static float PingPong(float elapsed, float duration)
        {
            if (duration <= 0f)
            {
                return 0.5f;
            }

            float t = Math.Max(0f, elapsed) / duration;
            float cycle = t % 2f;
            return CinematicTimeline.EaseInOut(cycle <= 1f ? cycle : 2f - cycle);
        }

        /// <summary>The still framing of a move when the camera may not move: the dolly at its (close) end, the others in
        /// their middle.</summary>
        public static float Still(StagedCameraKind kind) => kind == StagedCameraKind.Dolly ? 1f : 0.5f;

        /// <summary>The progress of <paramref name="shot"/> at <paramref name="elapsed"/> seconds into a shot of
        /// <paramref name="duration"/> seconds; <paramref name="motion"/> = the player's "Camera motion" comfort setting.</summary>
        public static float Progress(StagedCamera shot, float elapsed, float duration, bool motion)
        {
            if (shot is null)
            {
                return 0f;
            }

            if (!motion)
            {
                return Still(shot.Kind);
            }

            return shot.Kind == StagedCameraKind.Pan ? PingPong(elapsed, duration) : Eased(elapsed, duration);
        }

        /// <summary>The camera position of a move at progress <paramref name="k"/> (0..1): the dolly on its straight line, the
        /// flyover on its arc (<see cref="StagedCamera.Height"/> plus the rise, highest in the middle), the pan at its pivot;
        /// any other shot at its settled orbit pose.</summary>
        public static void Position(StagedCamera shot, float k, out float x, out float y, out float z)
        {
            k = Clamp01(k);
            switch (shot.Kind)
            {
                case StagedCameraKind.Dolly:
                    x = shot.StartX + (shot.EndX - shot.StartX) * k;
                    y = shot.StartY + (shot.EndY - shot.StartY) * k;
                    z = shot.StartZ + (shot.EndZ - shot.StartZ) * k;
                    return;
                case StagedCameraKind.Pan:
                    x = shot.StartX;
                    y = shot.StartY;
                    z = shot.StartZ;
                    return;
                case StagedCameraKind.Flyover:
                    {
                        float angle = shot.StartAngle + (shot.EndAngle - shot.StartAngle) * k;
                        CutsceneStaging.Polar(shot.CenterX, shot.CenterZ, angle, shot.Radius, out x, out z);
                        y = shot.CenterY + shot.Height + shot.Rise * (float)Math.Sin(Math.PI * k);
                        return;
                    }

                default:
                    CutsceneStaging.Polar(shot.CenterX, shot.CenterZ, shot.StartAngle, shot.Radius, out x, out z);
                    y = shot.CenterY + shot.Height;
                    return;
            }
        }

        /// <summary>The pan's yaw at progress <paramref name="k"/> (degrees; the orbit convention: forward = (sin, cos)).</summary>
        public static float Yaw(StagedCamera shot, float k) => shot.StartAngle + (shot.EndAngle - shot.StartAngle) * Clamp01(k);

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
