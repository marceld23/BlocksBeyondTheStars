// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// When the "last played" picture of a local world is taken: the moments that make a bad picture
/// (<see cref="WorldPreviewRules"/>) and the clock of clear view behind it (<see cref="WorldPreviewSchedule"/>) — the
/// first picture after half a minute, then every three minutes, one when the player leaves, none while the view settles.
/// </summary>
public sealed class WorldPreviewScheduleTests
{
    private static readonly WorldPreviewMoment Clear = new WorldPreviewMoment { HasCamera = true };

    /// <summary>Runs <paramref name="seconds"/> in 0.1 s frames; true when any frame said "take it now".</summary>
    private static bool Run(WorldPreviewSchedule schedule, double seconds, WorldPreviewBlocker blocker = WorldPreviewBlocker.None)
    {
        bool due = false;
        for (double t = 0; t < seconds - 1e-9; t += 0.1)
        {
            due |= schedule.Tick(0.1, blocker);
        }

        return due;
    }

    // ── Rules ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AClearView_IsAGoodMoment()
    {
        Assert.Equal(WorldPreviewBlocker.None, WorldPreviewRules.Blocker(Clear));
    }

    [Fact]
    public void EachBadMoment_HasItsReason()
    {
        Assert.Equal(WorldPreviewBlocker.NoCamera, WorldPreviewRules.Blocker(new WorldPreviewMoment()));
        Assert.Equal(WorldPreviewBlocker.Loading, WorldPreviewRules.Blocker(Clear with { Loading = true }));
        Assert.Equal(WorldPreviewBlocker.Cutscene, WorldPreviewRules.Blocker(Clear with { Cutscene = true }));
        Assert.Equal(WorldPreviewBlocker.Death, WorldPreviewRules.Blocker(Clear with { Dead = true }));
        Assert.Equal(WorldPreviewBlocker.Menu, WorldPreviewRules.Blocker(Clear with { MenuOpen = true }));
        Assert.Equal(WorldPreviewBlocker.Chat, WorldPreviewRules.Blocker(Clear with { ChatOpen = true }));
    }

    [Fact]
    public void TheHardReason_Wins()
    {
        var everything = new WorldPreviewMoment { HasCamera = true, Loading = true, Cutscene = true, Dead = true, MenuOpen = true, ChatOpen = true };
        Assert.Equal(WorldPreviewBlocker.Loading, WorldPreviewRules.Blocker(everything));
        Assert.Equal(WorldPreviewBlocker.Death, WorldPreviewRules.Blocker(Clear with { Dead = true, MenuOpen = true }));
    }

    [Fact]
    public void AHyperjump_KeepsThePictureBack_ForAFewSeconds()
    {
        Assert.Equal(WorldPreviewBlocker.Loading, WorldPreviewRules.Blocker(Clear with { SecondsSinceJump = 2 }));
        Assert.Equal(WorldPreviewBlocker.None,
            WorldPreviewRules.Blocker(Clear with { SecondsSinceJump = WorldPreviewRules.JumpQuietSeconds }));
        Assert.Equal(WorldPreviewBlocker.None, WorldPreviewRules.Blocker(Clear with { SecondsSinceJump = null }));
    }

    [Fact]
    public void OnlyAMenuAndTheChat_AreSoft()
    {
        Assert.True(WorldPreviewRules.IsSoft(WorldPreviewBlocker.Menu));
        Assert.True(WorldPreviewRules.IsSoft(WorldPreviewBlocker.Chat));
        Assert.False(WorldPreviewRules.IsSoft(WorldPreviewBlocker.None));
        Assert.False(WorldPreviewRules.IsSoft(WorldPreviewBlocker.Loading));
        Assert.False(WorldPreviewRules.IsSoft(WorldPreviewBlocker.Cutscene));
        Assert.False(WorldPreviewRules.IsSoft(WorldPreviewBlocker.Death));
        Assert.False(WorldPreviewRules.IsSoft(WorldPreviewBlocker.NoCamera));
    }

    // ── Schedule ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheFirstPicture_ComesAfterHalfAMinuteOfClearView()
    {
        var schedule = new WorldPreviewSchedule();

        Assert.False(Run(schedule, WorldPreviewSchedule.FirstSeconds - 1));
        Assert.True(Run(schedule, 1.5));
    }

    [Fact]
    public void AfterAPicture_TheNextWaitsTheInterval()
    {
        var schedule = new WorldPreviewSchedule();
        Run(schedule, WorldPreviewSchedule.FirstSeconds + 1);
        schedule.MarkTaken();

        Assert.True(schedule.Taken);
        Assert.False(Run(schedule, WorldPreviewSchedule.IntervalSeconds - 1));
        Assert.True(Run(schedule, 1.5));
    }

    [Fact]
    public void BadMoments_DoNotCount_AndAreNeverPictured()
    {
        var schedule = new WorldPreviewSchedule();

        Assert.False(Run(schedule, 600, WorldPreviewBlocker.Menu), "ten minutes in the console make no picture");
        Assert.False(Run(schedule, 600, WorldPreviewBlocker.Cutscene));
        Assert.False(Run(schedule, WorldPreviewSchedule.FirstSeconds - 1), "and they did not count as clear view");
    }

    [Fact]
    public void AfterLoading_TheViewSettlesFirst()
    {
        var schedule = new WorldPreviewSchedule();
        Run(schedule, WorldPreviewSchedule.FirstSeconds - 0.5);
        Run(schedule, 3, WorldPreviewBlocker.Loading); // a landing: the world builds up again

        Assert.False(Run(schedule, WorldPreviewSchedule.SettleSeconds - 1), "due, but the new view is still settling");
        Assert.True(Run(schedule, 1.5));
    }

    [Fact]
    public void AfterAMenu_OnlyTheBlurFades()
    {
        var schedule = new WorldPreviewSchedule();
        Run(schedule, WorldPreviewSchedule.FirstSeconds - 0.5);
        Run(schedule, 20, WorldPreviewBlocker.Menu);

        Assert.False(Run(schedule, 0.5), "the menu blur is still fading");
        Assert.True(Run(schedule, WorldPreviewSchedule.CalmSeconds), "no settle time: the world stood behind the menu");
    }

    [Fact]
    public void Leaving_TakesAPicture_OnceTheViewSettled()
    {
        var schedule = new WorldPreviewSchedule();
        Assert.False(schedule.WantsLeavePicture(), "nothing to show the moment the world opened");

        Run(schedule, WorldPreviewSchedule.SettleSeconds + 0.5);
        Assert.True(schedule.WantsLeavePicture(), "a short visit still gets its picture");
    }

    [Fact]
    public void Leaving_RightAfterAPicture_TakesNoSecond()
    {
        var schedule = new WorldPreviewSchedule();
        Run(schedule, WorldPreviewSchedule.FirstSeconds + 1);
        schedule.MarkTaken();

        Run(schedule, WorldPreviewSchedule.LeaveGapSeconds - 2);
        Assert.False(schedule.WantsLeavePicture());
        Run(schedule, 2.5);
        Assert.True(schedule.WantsLeavePicture());
    }

    [Fact]
    public void Leaving_FromAnOpenMenu_TakesNone()
    {
        // The pad reaches the pause menu through the console: its blur is on the world, so the timed pictures stand.
        var schedule = new WorldPreviewSchedule();
        Run(schedule, 20);
        Run(schedule, 1, WorldPreviewBlocker.Menu);

        Assert.False(schedule.WantsLeavePicture());
    }

    [Fact]
    public void AFailedPicture_IsTriedAgainLater()
    {
        var schedule = new WorldPreviewSchedule();
        Assert.True(Run(schedule, WorldPreviewSchedule.FirstSeconds + 0.5));
        schedule.MarkFailed();

        Assert.False(Run(schedule, WorldPreviewSchedule.RetrySeconds - 1));
        Assert.True(Run(schedule, 1.5));
    }

    [Fact]
    public void ABrokenFrameTime_CountsAsNothing()
    {
        var schedule = new WorldPreviewSchedule();

        Assert.False(schedule.Tick(double.NaN, WorldPreviewBlocker.None));
        Assert.False(schedule.Tick(-1000, WorldPreviewBlocker.None));
        Assert.False(schedule.Tick(double.NegativeInfinity, WorldPreviewBlocker.None));
        Assert.False(schedule.Tick(double.PositiveInfinity, WorldPreviewBlocker.None));
        Assert.False(Run(schedule, WorldPreviewSchedule.FirstSeconds - 1));
    }
}
