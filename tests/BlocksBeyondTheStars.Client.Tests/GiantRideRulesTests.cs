// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3467: riding a giant's back — the rider keeps their spot on the moving, turning body part, leaving in mid-air keeps part
/// of its speed, and the colossus, the arachnid and the Soundrak shake a rider off.
/// </summary>
public sealed class GiantRideRulesTests
{
    private static void Near(Vector3f expected, Vector3f actual, float tol = 1e-3f)
    {
        Assert.Equal(expected.X, actual.X, tol);
        Assert.Equal(expected.Y, actual.Y, tol);
        Assert.Equal(expected.Z, actual.Z, tol);
    }

    [Fact]
    public void YawAndRotation_FollowUnitysConvention()
    {
        Assert.Equal(0f, GiantRideRules.YawOf(new Vector3f(0f, 0f, 1f)), 3);
        Assert.Equal(90f, GiantRideRules.YawOf(new Vector3f(1f, 0f, 0f)), 3);
        Near(new Vector3f(1f, 0f, 0f), GiantRideRules.RotateYaw(new Vector3f(0f, 0f, 1f), 90f)); // forward turned right
        Assert.Equal(-20f, GiantRideRules.DeltaYaw(10f, 350f), 3);
        Assert.Equal(20f, GiantRideRules.DeltaYaw(350f, 10f), 3);
    }

    [Fact]
    public void ARider_IsCarriedAlong_WhenTheBodyMovesStraight()
    {
        var (delta, turn) = GiantRideRules.Carry(new Vector3f(3f, 52f, 1f), new Vector3f(0f, 50f, 0f), 0f, new Vector3f(0.2f, 50.05f, 0.1f), 0f);
        Near(new Vector3f(0.2f, 0.05f, 0.1f), delta);
        Assert.Equal(0f, turn, 4);
    }

    [Fact]
    public void ARider_TurnsWithTheBody_AndKeepsTheirSpotOnIt()
    {
        // The body turns 90° about its own middle: a rider 2 blocks ahead of the middle ends up 2 blocks to its right.
        var (delta, turn) = GiantRideRules.Carry(new Vector3f(0f, 51f, 2f), new Vector3f(0f, 50f, 0f), 0f, new Vector3f(0f, 50f, 0f), 90f);
        Near(new Vector3f(2f, 0f, -2f), delta);
        Assert.Equal(90f, turn, 3);
    }

    [Fact]
    public void AJumpOfTheBody_IsNoCarry()
    {
        var (delta, turn) = GiantRideRules.Carry(new Vector3f(0f, 0f, 0f), new Vector3f(0f, 0f, 0f), 0f, new Vector3f(30f, 0f, 0f), 0f);
        Near(new Vector3f(0f, 0f, 0f), delta);
        Assert.Equal(0f, turn);
        Assert.Equal(0f, GiantRideRules.Carry(new Vector3f(float.NaN, 0f, 0f), default, 0f, new Vector3f(1f, 0f, 0f), 0f).Delta.X);
    }

    [Fact]
    public void LeavingInMidAir_KeepsMostOfTheSpeed_Capped()
    {
        Near(new Vector3f(6.4f, 0f, 0f), GiantRideRules.LeaveMomentum(new Vector3f(8f, 3f, 0f)));
        var fast = GiantRideRules.LeaveMomentum(new Vector3f(0f, 0f, 30f));
        Assert.Equal(GiantRideRules.LeaveCap, fast.Z, 3);
        Near(new Vector3f(0f, 0f, 0f), GiantRideRules.LeaveMomentum(new Vector3f(0.5f, 0f, 0.2f)));
    }

    [Fact]
    public void OnlyTheFightingGiants_ShakeARiderOff_OutwardWithAHop()
    {
        Assert.True(GiantRideRules.ShakesOff("Colossus"));
        Assert.True(GiantRideRules.ShakesOff("Arachnid"));
        Assert.True(GiantRideRules.ShakesOff("Soundrak"));
        Assert.False(GiantRideRules.ShakesOff("SkyGiant"));
        Assert.False(GiantRideRules.ShakesOff("Leviathan"));
        Assert.False(GiantRideRules.ShakesOff("Sandworm"));
        Assert.False(GiantRideRules.ShakesOff(null));

        var push = GiantRideRules.ShakeOffImpulse(new Vector3f(0f, 5f, -3f));
        Near(new Vector3f(0f, GiantRideRules.ShakeOffHop, -GiantRideRules.ShakeOffPush), push);
        Assert.Equal(GiantRideRules.ShakeOffPush, GiantRideRules.ShakeOffImpulse(default).X, 3); // straight above: along +X
    }
}
