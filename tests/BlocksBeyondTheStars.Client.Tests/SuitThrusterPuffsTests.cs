// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3241: a suit move on a spacewalk made no sound at all. <see cref="SuitThrusterPuffs"/> puffs when a push starts or
/// turns — never a constant hiss while it holds, never two closer than <see cref="SuitThrusterPuffs.MinGapSeconds"/>.
/// </summary>
public sealed class SuitThrusterPuffsTests
{
    private const float Frame = 1f / 60f;

    private static int Puffs(SuitThrusterPuffs puffs, float seconds, float strafe, float lift, float forward)
    {
        int count = 0;
        for (float t = 0f; t < seconds; t += Frame)
        {
            if (!puffs.Tick(Frame, strafe, lift, forward).IsNone)
            {
                count++;
            }
        }

        return count;
    }

    [Fact]
    public void APush_PuffsOnceWhenItStarts_NotWhileItHolds()
    {
        var puffs = new SuitThrusterPuffs(new Random(1));
        Assert.Equal(0, Puffs(puffs, 1f, 0f, 0f, 0f));
        var first = puffs.Tick(Frame, 0f, 0f, 1f);
        Assert.StartsWith("suit_thruster", first.Id);
        Assert.Equal(SuitThrusterPuffs.Level, first.Volume, 4);
        Assert.Equal(0, Puffs(puffs, 3f, 0f, 0f, 1f));
    }

    [Fact]
    public void ATurn_PuffsAgain_ASmallDriftDoesNot()
    {
        var puffs = new SuitThrusterPuffs(new Random(2));
        Assert.Equal(1, Puffs(puffs, 1f, 0f, 0f, 1f));
        Assert.Equal(0, Puffs(puffs, 1f, 0.3f, 0f, 1f)); // ~17° off: the same push
        Assert.Equal(1, Puffs(puffs, 1f, 0f, 1f, 0f));   // forward → up: a new push
        Assert.Equal(1, Puffs(puffs, 1f, 0f, -1f, 0f));  // up → down
    }

    [Fact]
    public void TappingFast_IsRateLimited()
    {
        var puffs = new SuitThrusterPuffs(new Random(3));
        int count = 0;
        for (int i = 0; i < 30; i++) // 30 taps of 3 frames on, 3 frames off: 10 taps a second
        {
            count += Puffs(puffs, 3 * Frame, 1f, 0f, 0f);
            count += Puffs(puffs, 3 * Frame, 0f, 0f, 0f);
        }

        Assert.InRange(count, 1, (int)(3f / SuitThrusterPuffs.MinGapSeconds) + 1);
    }

    [Fact]
    public void TheTwoTakes_Alternate_AndALittleNoiseIsNoPush()
    {
        var puffs = new SuitThrusterPuffs(new Random(4));
        Assert.Equal(0, Puffs(puffs, 1f, 0.1f, 0f, 0.1f)); // a resting stick's drift
        var a = puffs.Tick(Frame, 1f, 0f, 0f);
        Puffs(puffs, 0.5f, 0f, 0f, 0f);
        var b = puffs.Tick(Frame, 1f, 0f, 0f);
        Assert.False(a.IsNone);
        Assert.False(b.IsNone);
        Assert.NotEqual(a.Id, b.Id);
    }

    /// <summary>The Unity half, guarded as source text: the spacewalk feeds the move input in the look frame.</summary>
    [Fact]
    public void TheSpacewalk_FeedsThePuffs()
    {
        string eva = string.Join("\n", ClientTestPaths.ScriptParts("SpaceView"));
        Assert.Contains("EvaThrusterPuff(strafe, lift, fwd);", eva);
        Assert.Contains("new SuitThrusterPuffs(", eva);
        Assert.Contains(".PlayOwn(puff.Id, puff.Volume, puff.Pitch)", eva);
    }
}
