// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The timing backbone of the scripted cinematics (#759/#760): leg lookup must be exact at the
/// boundaries, clamped outside the timeline, and the shared easing/fade helpers must stay in 0..1 —
/// a camera fed a value outside that range snaps visibly.
/// </summary>
public sealed class CinematicTimelineTests
{
    [Fact]
    public void Legs_MapTimeToIndexAndProgress()
    {
        var tl = new CinematicTimeline(2f, 3f, 5f);

        Assert.Equal(10f, tl.Total);
        Assert.Equal(3, tl.LegCount);
        Assert.Equal((0, 0f), tl.At(0f));
        Assert.Equal((0, 0.5f), tl.At(1f));
        Assert.Equal((1, 0f), tl.At(2f));     // boundary belongs to the NEXT leg
        Assert.Equal((1, 0.5f), tl.At(3.5f));
        Assert.Equal((2, 0f), tl.At(5f));
        Assert.Equal((2, 0.5f), tl.At(7.5f));
    }

    [Fact]
    public void OutsideTheTimeline_IsClamped()
    {
        var tl = new CinematicTimeline(4f, 4f);

        Assert.Equal((0, 0f), tl.At(-1f));
        Assert.Equal((1, 1f), tl.At(8f));   // exact end
        Assert.Equal((1, 1f), tl.At(99f));  // far past the end
        Assert.False(tl.Done(7.99f));
        Assert.True(tl.Done(8f));
    }

    [Fact]
    public void StartOf_ReturnsAbsoluteLegStarts()
    {
        var tl = new CinematicTimeline(1.5f, 2.5f, 4f);
        Assert.Equal(0f, tl.StartOf(0));
        Assert.Equal(1.5f, tl.StartOf(1));
        Assert.Equal(4f, tl.StartOf(2));
    }

    [Fact]
    public void InvalidLegs_Throw()
    {
        Assert.Throws<ArgumentException>(() => new CinematicTimeline());
        Assert.Throws<ArgumentException>(() => new CinematicTimeline(2f, 0f));
        Assert.Throws<ArgumentException>(() => new CinematicTimeline(-1f));
    }

    [Fact]
    public void Easing_IsClampedAndHitsTheEndpoints()
    {
        Assert.Equal(0f, CinematicTimeline.EaseInOut(-0.5f));
        Assert.Equal(0f, CinematicTimeline.EaseInOut(0f));
        Assert.Equal(0.5f, CinematicTimeline.EaseInOut(0.5f), 3);
        Assert.Equal(1f, CinematicTimeline.EaseInOut(1f));
        Assert.Equal(1f, CinematicTimeline.EaseInOut(1.5f));

        Assert.Equal(0f, CinematicTimeline.EaseOut(0f));
        Assert.Equal(1f, CinematicTimeline.EaseOut(1f));
        Assert.Equal(1f, CinematicTimeline.EaseOut(2f));
    }

    [Fact]
    public void FadeWindow_FadesInHoldsAndFadesOut()
    {
        // Window 2..8 with a 1 s fade on both edges.
        Assert.Equal(0f, CinematicTimeline.FadeWindow(0f, 2f, 8f, 1f));
        Assert.Equal(0f, CinematicTimeline.FadeWindow(2f, 2f, 8f, 1f));
        Assert.Equal(0.5f, CinematicTimeline.FadeWindow(2.5f, 2f, 8f, 1f), 3);
        Assert.Equal(1f, CinematicTimeline.FadeWindow(5f, 2f, 8f, 1f));
        Assert.Equal(0.5f, CinematicTimeline.FadeWindow(7.5f, 2f, 8f, 1f), 3);
        Assert.Equal(0f, CinematicTimeline.FadeWindow(8f, 2f, 8f, 1f));
        Assert.Equal(0f, CinematicTimeline.FadeWindow(9f, 2f, 8f, 1f));
    }

    [Fact]
    public void FadeWindow_DegenerateAndOversizedFades_StaySafe()
    {
        // Empty/inverted windows are fully transparent.
        Assert.Equal(0f, CinematicTimeline.FadeWindow(3f, 5f, 5f, 1f));
        Assert.Equal(0f, CinematicTimeline.FadeWindow(3f, 6f, 5f, 1f));

        // A fade longer than half the window is clamped so the peak still reaches 1 at the centre.
        Assert.Equal(1f, CinematicTimeline.FadeWindow(5f, 4f, 6f, 10f));

        // A zero fade never divides by zero.
        Assert.Equal(1f, CinematicTimeline.FadeWindow(5f, 4f, 6f, 0f));
    }
}
