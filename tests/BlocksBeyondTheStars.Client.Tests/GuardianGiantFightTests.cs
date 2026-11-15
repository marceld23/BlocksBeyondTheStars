// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3676: what the client makes of the guardian giant's status — the move's clock counted on locally (re-synced only on a
/// real drift, a repeated move starting afresh), the blend between moves, the boss bar (shown while a giant with hull fights
/// near, hidden on the end of the fight or when the statuses stop) and the fight music's switch.
/// </summary>
public sealed class GuardianGiantFightTests
{
    private const string Site = "planet_a#0";

    private static GuardianGiantStatus Status(GuardianGiantMove move, float elapsed, float hull = 400f, string site = Site)
        => new GuardianGiantStatus
        {
            SiteId = site,
            EntityId = "gg-" + site,
            Move = (byte)move,
            MoveElapsed = elapsed,
            Hull = hull,
            HullMax = 600f,
            AnchorX = 10,
            AnchorY = 20,
            AnchorZ = 30,
            Facing = 1,
            Seed = 99,
        };

    [Fact]
    public void TheMoveClock_CountsOnFromTheStatus()
    {
        var fight = new GuardianGiantFight();
        fight.Receive(Status(GuardianGiantMove.Walk, 0.5f), 10f);
        var t = Assert.Single(fight.Tracks);
        Assert.Equal(GuardianGiantMove.Walk, t.Move);
        Assert.Equal(0.7f, t.Elapsed(10.2f), 4);
        Assert.Equal(1, t.MoveSerial);
        Assert.Equal(1f, t.Blend(10f), 4); // the first move heard needs no blend
        Assert.Equal("gg-" + Site, t.EntityId);
        Assert.Same(t, fight.FindByEntity("gg-" + Site));
        Assert.Null(fight.FindByEntity(string.Empty));
    }

    [Fact]
    public void ASmallDisagreement_LeavesTheClock_ARealDriftMovesIt()
    {
        var fight = new GuardianGiantFight();
        fight.Receive(Status(GuardianGiantMove.Idle, 1f), 10f);
        var t = fight.Find(Site)!;
        fight.Receive(Status(GuardianGiantMove.Idle, 1.6f), 10.5f); // 0.1 s off: jitter
        Assert.Equal(9f, t.MoveStart, 4);
        fight.Receive(Status(GuardianGiantMove.Idle, 2.6f), 11f);   // 0.6 s off: adopt
        Assert.Equal(8.4f, t.MoveStart, 4);
        Assert.Equal(1, t.MoveSerial);
    }

    [Fact]
    public void ANewMove_BlendsInOverTheOldOne()
    {
        var fight = new GuardianGiantFight();
        fight.Receive(Status(GuardianGiantMove.Walk, 3f), 10f);
        fight.Receive(Status(GuardianGiantMove.Stomp, 0.1f), 12f);
        var t = fight.Find(Site)!;
        Assert.Equal(GuardianGiantMove.Stomp, t.Move);
        Assert.Equal(GuardianGiantMove.Walk, t.PrevMove);
        Assert.Equal(5f, t.PrevElapsed(12f), 4);
        Assert.Equal(0f, t.Blend(12f), 4);
        Assert.Equal(1f, t.Blend(12f + GuardianGiantFight.BlendSeconds), 4);
        Assert.Equal(2, t.MoveSerial);
    }

    [Fact]
    public void TheSameMoveAgain_IsANewMove()
    {
        var fight = new GuardianGiantFight();
        fight.Receive(Status(GuardianGiantMove.Stomp, 0.2f), 10f);
        fight.Receive(Status(GuardianGiantMove.Stomp, 1.2f), 11f);  // the same stomp
        var t = fight.Find(Site)!;
        Assert.Equal(1, t.MoveSerial);
        fight.Receive(Status(GuardianGiantMove.Stomp, 0.1f), 13f);  // a second stomp
        Assert.Equal(2, t.MoveSerial);
        Assert.Equal(0.1f, t.Elapsed(13f), 4);
    }

    [Fact]
    public void TheBossBar_ShowsWhileAGiantWithHullFights_AndHidesOtherwise()
    {
        var fight = new GuardianGiantFight();
        Assert.Null(fight.BossBar(0f));
        fight.Receive(Status(GuardianGiantMove.Rise, 0f), 10f);
        var bar = fight.BossBar(10.5f);
        Assert.NotNull(bar);
        Assert.Equal(400f / 600f, bar!.HullFraction, 4);
        Assert.Null(fight.BossBar(10f + GuardianGiantFight.StaleSeconds + 0.1f)); // no status for a while: gone
        Assert.True(fight.Fighting(10.5f));

        fight.Receive(Status(GuardianGiantMove.Collapse, 0.2f, hull: 0f), 11f);
        Assert.Null(fight.BossBar(11f));
        Assert.False(fight.Fighting(11f));

        fight.Receive(Status(GuardianGiantMove.None, 0f, hull: 0f), 14f);
        Assert.Null(fight.BossBar(14f));
        Assert.False(GuardianGiantFight.Live(fight.Find(Site)!, 14f));
    }

    [Fact]
    public void LyingDownAgain_EndsTheMusic_ButKeepsTheBar()
    {
        var fight = new GuardianGiantFight();
        fight.Receive(Status(GuardianGiantMove.LieDown, 0.5f, hull: 600f), 10f);
        Assert.NotNull(fight.BossBar(10f));
        Assert.False(fight.Fighting(10f));
        fight.Receive(Status(GuardianGiantMove.Search, 4f), 12f);
        Assert.True(fight.Fighting(12f));
    }

    [Fact]
    public void TwoGiants_TheBarShowsTheOneHeardLast()
    {
        var fight = new GuardianGiantFight();
        fight.Receive(Status(GuardianGiantMove.Walk, 1f, hull: 300f), 10f);
        fight.Receive(Status(GuardianGiantMove.Walk, 1f, hull: 500f, site: "planet_a#1"), 10.2f);
        Assert.Equal("planet_a#1", fight.BossBar(10.3f)!.SiteId);
        fight.Receive(Status(GuardianGiantMove.Laser, 0f, hull: 290f), 10.4f);
        Assert.Equal(Site, fight.BossBar(10.5f)!.SiteId);
    }

    [Fact]
    public void OldFights_AreForgotten_AndBrokenStatusesIgnored()
    {
        var fight = new GuardianGiantFight();
        fight.Receive(null, 1f);
        fight.Receive(new GuardianGiantStatus(), 1f);
        Assert.Empty(fight.Tracks);
        fight.Receive(Status(GuardianGiantMove.Idle, 0f), 10f);
        fight.Prune(10f + GuardianGiantFight.ForgetSeconds - 1f);
        Assert.Single(fight.Tracks);
        fight.Prune(10f + GuardianGiantFight.ForgetSeconds + 1f);
        Assert.Empty(fight.Tracks);

        fight.Receive(Status(GuardianGiantMove.Idle, 0f), 50f);
        fight.Receive(Status(GuardianGiantMove.Walk, 0f), 5f); // the clock went back (a new world): a fresh track
        Assert.Equal(1, fight.Find(Site)!.MoveSerial);
        fight.Clear();
        Assert.Empty(fight.Tracks);
    }

    [Fact]
    public void AGuardianGiant_ShakesARiderOff_AndIsHeardFromAfar()
    {
        Assert.True(GiantRideRules.ShakesOff(GiantRideRules.GuardianGiantPlan));
        foreach (string cue in new[] { "giant_step", "giant_stomp", "giant_laser_beam", "giant_collapse", "giant_flicker" })
        {
            Assert.Equal(AudioRange.Large, AudioMixRules.RangeFor(cue));
        }
    }
}
