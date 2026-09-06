// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>The angles that put a two-bone limb's foot on a target, in the limb root's own frame.</summary>
public readonly struct LegSolution
{
    /// <summary>Hip rotation about X. Positive swings the leg backwards.</summary>
    public readonly float HipPitchDeg;

    /// <summary>Hip rotation about Y — how far the leg reaches out of the sagittal plane.</summary>
    public readonly float HipYawDeg;

    /// <summary>Knee fold, already signed by the limb's <c>KneeSign</c>.</summary>
    public readonly float KneeDeg;

    /// <summary>True when the target was further away than the leg can reach and was pulled in.</summary>
    public readonly bool Overreached;

    public LegSolution(float hipPitchDeg, float hipYawDeg, float kneeDeg, bool overreached)
    {
        HipPitchDeg = hipPitchDeg;
        HipYawDeg = hipYawDeg;
        KneeDeg = kneeDeg;
        Overreached = overreached;
    }
}

/// <summary>
/// Analytic two-bone inverse kinematics for a creature leg — the law of cosines, nothing more. Ten lines of
/// trigonometry instead of Unity's animation rigging: the rig is built from cubes in code, there is no
/// skeleton asset to attach a solver to, and this way the maths is unit-tested off the engine.
///
/// Frame convention matches the rig: the leg hangs along -Y from the hip, +Z is forward (the nose), +X is
/// the creature's right. The returned angles are meant for <c>Quaternion.Euler(HipPitch, HipYaw, 0)</c> on
/// the hip and <c>Quaternion.Euler(KneeDeg, 0, 0)</c> on the knee.
/// </summary>
public static class CreatureIk
{
    /// <summary>A leg never straightens completely — a locked knee looks like a prosthetic, and the solution
    /// is numerically unstable right at full extension.</summary>
    public const float MaxReachFraction = 0.98f;

    /// <summary>Solves for a foot target given in the hip's local frame.</summary>
    /// <param name="x">Target offset to the creature's right.</param>
    /// <param name="y">Target offset up (normally negative — the foot is below the hip).</param>
    /// <param name="z">Target offset forward.</param>
    /// <param name="upper">Thigh length.</param>
    /// <param name="lower">Shin length.</param>
    /// <param name="kneeSign">+1 folds the shin backwards (an elbow), -1 forwards (a stifle).</param>
    public static LegSolution SolveTwoBone(float x, float y, float z, float upper, float lower, int kneeSign)
    {
        float u = upper < 1e-4f ? 1e-4f : upper;
        float l = lower < 1e-4f ? 1e-4f : lower;
        int sign = kneeSign >= 0 ? 1 : -1;

        double dist = System.Math.Sqrt((double)x * x + (double)y * y + (double)z * z);
        if (dist < 1e-5)
        {
            // Degenerate: the target is the hip itself. Fold the leg right up rather than returning NaN.
            return new LegSolution(0f, 0f, sign * 150f, false);
        }

        double maxReach = (u + l) * MaxReachFraction;
        bool overreached = dist > maxReach;
        double scale = overreached ? maxReach / dist : 1.0;
        double tx = x * scale, ty = y * scale, tz = z * scale;
        double d = overreached ? maxReach : dist;

        // Direction to the target, as the pitch/yaw pair that rotates the resting -Y leg onto it.
        double dy = ty / d;
        double pitch = System.Math.Acos(Clamp(-dy, -1.0, 1.0));
        double sinPitch = System.Math.Sin(pitch);
        double yaw = sinPitch < 1e-6 ? 0.0 : System.Math.Atan2(-tx, -tz);

        // Law of cosines: the interior angle at the knee, and how far the thigh sits off the straight line.
        double cosKnee = Clamp(((double)u * u + (double)l * l - d * d) / (2.0 * u * l), -1.0, 1.0);
        double kneeFold = System.Math.PI - System.Math.Acos(cosKnee);

        double cosOffset = Clamp(((double)u * u + d * d - (double)l * l) / (2.0 * u * d), -1.0, 1.0);
        double offset = System.Math.Acos(cosOffset);

        // Folding the shin one way displaces the foot that way, so the thigh leans the other way to compensate.
        double hipPitch = pitch - sign * offset;

        return new LegSolution(
            (float)(hipPitch * RadToDeg),
            (float)(yaw * RadToDeg),
            (float)(sign * kneeFold * RadToDeg),
            overreached);
    }

    /// <summary>The furthest a leg of these bones will reach — what a caller clamps its foot targets to.</summary>
    public static float MaxReach(float upper, float lower) => (upper + lower) * MaxReachFraction;

    private const double RadToDeg = 180.0 / System.Math.PI;

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
}
