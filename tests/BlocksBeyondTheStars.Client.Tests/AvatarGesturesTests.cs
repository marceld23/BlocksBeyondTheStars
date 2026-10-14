// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2593 — the greeting gestures' curves (<see cref="AvatarGestures"/>): the wave, the nod and the cheer start and end at
/// rest (no snap over the pose underneath), the wave wags five times, and an NPC's head turns to a nearby player only
/// within its field of view.
/// </summary>
public sealed class AvatarGesturesTests
{
    [Fact]
    public void TheWave_StartsAndEndsAtRest_AndIsFullyRaisedInTheMiddle()
    {
        Assert.Equal((0f, 0f), AvatarGestures.Wave(0f));
        Assert.Equal((0f, 0f), AvatarGestures.Wave(1f));
        Assert.Equal((0f, 0f), AvatarGestures.Wave(-0.5f));
        Assert.Equal((0f, 0f), AvatarGestures.Wave(1.5f));
        Assert.Equal(1f, AvatarGestures.Wave(0.5f).Blend, 4);
        Assert.True(AvatarGestures.Wave(0.01f).Blend < 0.05f);
        Assert.True(AvatarGestures.Wave(0.99f).Blend < 0.05f);
    }

    [Fact]
    public void TheWave_WagsFiveTimes()
    {
        // Five half-waves of the forearm = four sign changes inside the gesture.
        int changes = 0;
        float last = AvatarGestures.Wave(0.001f).Wag;
        for (int i = 2; i < 1000; i++)
        {
            float wag = AvatarGestures.Wave(i / 1000f).Wag;
            if (Math.Sign(wag) != 0 && Math.Sign(last) != 0 && Math.Sign(wag) != Math.Sign(last))
            {
                changes++;
            }

            if (Math.Sign(wag) != 0)
            {
                last = wag;
            }
        }

        Assert.Equal(4, changes);
    }

    [Fact]
    public void TheNod_DipsDown_AndReturnsToRest()
    {
        Assert.Equal(0f, AvatarGestures.Nod(0f));
        Assert.Equal(0f, AvatarGestures.Nod(1f));
        Assert.Equal(0f, AvatarGestures.Nod(2f));
        float deepest = Enumerable.Range(1, 99).Select(i => AvatarGestures.Nod(i / 100f)).Max();
        Assert.InRange(deepest, 5f, AvatarGestures.NodDegrees);
        Assert.True(Math.Abs(AvatarGestures.Nod(0.999f)) < 0.1f);
    }

    [Fact]
    public void TheCheer_RaisesBothArms_AndReturnsToRest()
    {
        Assert.Equal((0f, 0f), AvatarGestures.Cheer(0f));
        Assert.Equal((0f, 0f), AvatarGestures.Cheer(1f));
        Assert.Equal(1f, AvatarGestures.Cheer(0.5f).Blend, 4);
        Assert.True(AvatarGestures.Cheer(0.99f).Blend < 0.05f);
        Assert.InRange(AvatarGestures.Cheer(0.3f).Bob, -1f, 1f);
    }

    [Fact]
    public void AnNpc_LooksAtAPlayerInFront_AndToTheSide()
    {
        // NPC facing +Z (yaw 0); the player straight ahead, level.
        Assert.True(AvatarGestures.HeadLook(0f, 0f, 0f, 3f, out float yaw, out float pitch));
        Assert.Equal(0f, yaw, 3);
        Assert.Equal(0f, pitch, 3);

        // To the right (+X): a positive yaw, clamped to the neck's limit.
        Assert.True(AvatarGestures.HeadLook(0f, 3f, 0f, 3f, out yaw, out _));
        Assert.Equal(45f, yaw, 3);
        Assert.True(AvatarGestures.HeadLook(0f, 3f, 0f, 0.5f, out yaw, out _));
        Assert.Equal(AvatarGestures.LookYawLimit, yaw, 3);

        // A taller player (eye above the head): the NPC looks up — up is negative, like the camera.
        Assert.True(AvatarGestures.HeadLook(0f, 0f, 1f, 2f, out _, out pitch));
        Assert.True(pitch < 0f);
        Assert.True(AvatarGestures.HeadLook(0f, 0f, 10f, 1f, out _, out pitch));
        Assert.Equal(-AvatarGestures.LookPitchLimit, pitch, 3);

        // The NPC's own facing counts: facing +X (90°), a player at +X is straight ahead.
        Assert.True(AvatarGestures.HeadLook(90f, 4f, 0f, 0f, out yaw, out _));
        Assert.Equal(0f, yaw, 3);
    }

    [Fact]
    public void AnNpc_DoesNotTwistItsNeck_ForAPlayerBehindIt()
    {
        Assert.False(AvatarGestures.HeadLook(0f, 0f, 0f, -3f, out _, out _));
        Assert.False(AvatarGestures.HeadLook(0f, 1f, 0f, -3f, out _, out _));
        Assert.False(AvatarGestures.HeadLook(0f, 0f, 0f, 0f, out _, out _)); // standing inside each other
    }

    [Theory]
    [InlineData(0f, 90f, 90f)]
    [InlineData(350f, 10f, 20f)]
    [InlineData(10f, 350f, -20f)]
    [InlineData(-170f, 170f, -20f)]
    [InlineData(720f, 0f, 0f)]
    public void DeltaAngle_IsTheShortestSignedTurn(float from, float to, float expected)
        => Assert.Equal(expected, AvatarGestures.DeltaAngle(from, to), 3);
}
