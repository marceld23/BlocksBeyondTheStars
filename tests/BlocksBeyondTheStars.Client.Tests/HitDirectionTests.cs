// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2564 ("Gegner finden"): the HUD's damage marker points at the attacker the server names, in the view's frame, and
/// adds an up or down chevron when that attacker is clearly above or below — a flier biting from overhead used to read
/// as "ahead". The tracker tells a fresh blow from the same old report repeated in every state update.
/// </summary>
public sealed class HitDirectionTests
{
    // Looking north (+Z): right is +X.
    private const float North = 1f;

    [Theory]
    [InlineData(0f, 5f, 0f)]      // straight ahead → the top of the ring
    [InlineData(5f, 0f, 90f)]     // to the right
    [InlineData(-5f, 0f, -90f)]   // to the left
    [InlineData(3f, 3f, 45f)]     // ahead-right
    public void TheAngle_IsMeasuredAroundTheViewAxis(float dx, float dz, float expected)
    {
        var b = HitDirection.Classify(dx, 0f, dz, 0f, North);
        Assert.Equal(expected, b.AngleDegrees, 2);
        Assert.Equal(HitHeight.Level, b.Height);
    }

    [Fact]
    public void BehindTheViewer_IsHalfARoundEitherWay()
    {
        var b = HitDirection.Classify(0f, 0f, -5f, 0f, North);
        Assert.Equal(180f, System.Math.Abs(b.AngleDegrees), 2);
    }

    [Fact]
    public void TheAngle_TurnsWithTheView()
    {
        // Looking east (+X): something to the north is on the left; any heading length works.
        var b = HitDirection.Classify(0f, 0f, 5f, 3f, 0f);
        Assert.Equal(-90f, b.AngleDegrees, 2);
    }

    [Fact]
    public void AFlierOverhead_IsAbove_NotAhead()
    {
        // The report: a flier ten blocks above, a little ahead.
        var b = HitDirection.Classify(0f, 10f, 1f, 0f, North);
        Assert.Equal(HitHeight.Above, b.Height);
        Assert.Equal(0f, b.AngleDegrees, 2);
    }

    [Fact]
    public void StraightOverhead_HasNoDirection_ButIsAbove()
    {
        var b = HitDirection.Classify(0f, 8f, 0f, 0.3f, 0.7f);
        Assert.Equal(HitHeight.Above, b.Height);
        Assert.Equal(0f, b.AngleDegrees);
    }

    [Fact]
    public void ACreatureInAPitBelow_IsBelow()
    {
        var b = HitDirection.Classify(2f, -4f, 0f, 0f, North);
        Assert.Equal(HitHeight.Below, b.Height);
        Assert.Equal(90f, b.AngleDegrees, 2);
    }

    [Theory]
    [InlineData(2.4f, 1f)]   // steep but only 2.4 blocks up: a creature on a step — level
    [InlineData(3f, 10f)]    // 3 blocks up but 10 away (17°): a long, gentle slope — level
    [InlineData(-2.4f, 1f)]
    [InlineData(-3f, 10f)]
    public void SmallOrShallowHeightDifferences_StayLevel(float dy, float ahead)
    {
        var b = HitDirection.Classify(0f, dy, ahead, 0f, North);
        Assert.Equal(HitHeight.Level, b.Height);
    }

    [Fact]
    public void JustPastBothThresholds_CountsAsAboveOrBelow()
    {
        // 3 blocks up, 4 ahead: 36.9° and more than 2.5 blocks.
        Assert.Equal(HitHeight.Above, HitDirection.Classify(0f, 3f, 4f, 0f, North).Height);
        Assert.Equal(HitHeight.Below, HitDirection.Classify(0f, -3f, 4f, 0f, North).Height);
    }

    [Fact]
    public void ScreenHeading_IsTheFlatForward_AndTheUpVectorWhenLookingStraightDownOrUp()
    {
        HitDirection.ScreenHeading(0.6f, -0.5f, 0.6f, 0f, 0f, out float hx, out float hz);
        Assert.Equal((0.6f, 0.6f), (hx, hz));

        // Straight down while facing north: the camera's up leans north — that is the top of the screen.
        HitDirection.ScreenHeading(0f, -1f, 0f, 0f, 1f, out hx, out hz);
        Assert.Equal((0f, 1f), (hx, hz));

        // Straight up while facing north: the camera's up leans south, so it is flipped back to north.
        HitDirection.ScreenHeading(0f, 1f, 0f, 0f, -1f, out hx, out hz);
        Assert.Equal((0f, 1f), (hx, hz));
    }

    // ---------------- the tracker ----------------

    [Fact]
    public void Tracker_ANewSequence_IsAFreshBlow_TheSameOneRepeatedIsNot()
    {
        var t = new HitSourceTracker();
        Assert.False(t.IsFresh(0.0));

        Assert.True(t.Observe(1, "creature:7", 1f, 2f, 3f, now: 10.0));
        Assert.Equal("creature:7", t.EntityId);
        Assert.Equal((1f, 2f, 3f), (t.X, t.Y, t.Z));
        Assert.True(t.IsFresh(10.5));
        Assert.True(t.IsFreshFrom("creature:7", 10.5));
        Assert.False(t.IsFreshFrom("enemy:3", 10.5));

        // Every later state update repeats the same report — not a new blow, and it does not refresh the clock.
        Assert.False(t.Observe(1, "creature:7", 1f, 2f, 3f, now: 11.5));
        Assert.False(t.IsFresh(11.5));

        Assert.True(t.Observe(2, "enemy:3", 4f, 5f, 6f, now: 12.0));
        Assert.True(t.IsFreshFrom("enemy:3", 12.2, maxAge: 0.4));
        Assert.False(t.IsFreshFrom("enemy:3", 12.5, maxAge: 0.4));
    }

    [Fact]
    public void Tracker_ANewSession_StartsAtZero_AndCountsAgain()
    {
        var t = new HitSourceTracker();
        Assert.False(t.Observe(0, string.Empty, 0f, 0f, 0f, now: 1.0)); // the join: no blow yet
        Assert.True(t.Observe(57, "creature:1", 0f, 0f, 0f, now: 2.0));

        // Reconnected to a fresh session: 0 forgets the old attacker, and its first blow (1) is new although it is lower.
        Assert.False(t.Observe(0, string.Empty, 0f, 0f, 0f, now: 2.1));
        Assert.False(t.IsFresh(2.1));
        Assert.Equal(string.Empty, t.EntityId);
        Assert.True(t.Observe(1, "creature:9", 0f, 0f, 0f, now: 3.0));
        Assert.True(t.IsFreshFrom("creature:9", 3.0));
    }

    [Fact]
    public void Tracker_ANullEntityId_IsNeverAnAttacker()
    {
        var t = new HitSourceTracker();
        Assert.True(t.Observe(1, null, 1f, 1f, 1f, now: 0.0));
        Assert.True(t.IsFresh(0.1));
        Assert.False(t.IsFreshFrom(string.Empty, 0.1));
        Assert.False(t.IsFreshFrom(null, 0.1));
    }
}
