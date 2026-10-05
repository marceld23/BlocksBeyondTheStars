// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Networking.Messages;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#2277 / #2283: the flight target lock's pure rules — disposition, cycle order, range with hysteresis, the
/// newcomer watch behind the auto-lock, the threat ticks and the edge arrow's placement (incl. behind the camera).</summary>
public sealed class SpaceTargetingTests
{
    private const float Eps = 1e-3f;
    private const float Radar = 130f;

    private static void Near(float expected, float actual, float tolerance = Eps)
        => Assert.True(System.Math.Abs(expected - actual) <= tolerance, $"expected {expected} ± {tolerance}, got {actual}");

    private static TargetCandidate Entity(string id, string kind, float z, bool hostile = false, float x = 0f)
        => new TargetCandidate(id, kind, hostile, false, false, x, 0f, z);

    private static TargetCandidate Pilot(string id, float z)
        => TargetCandidate.FromPilot(new NetSpacePlayer { PlayerId = id, Name = id, Z = z });

    private static string[] Ids(System.Collections.Generic.IEnumerable<TargetCandidate> list)
    {
        var ids = new System.Collections.Generic.List<string>();
        foreach (var c in list)
        {
            ids.Add(c.Id);
        }

        return ids.ToArray();
    }

    // ---- Disposition -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("Drone", true, TargetDisposition.Hostile)]
    [InlineData("Ufo", true, TargetDisposition.Hostile)]
    [InlineData("Cruiser", true, TargetDisposition.Hostile)]
    [InlineData("BanditShip", true, TargetDisposition.Hostile)]
    [InlineData("BanditShip", false, TargetDisposition.Caution)] // it talks first: demands cargo
    [InlineData("SpaceStation", false, TargetDisposition.Friendly)]
    [InlineData("EscapePod", false, TargetDisposition.Friendly)]
    [InlineData("Wreck", false, TargetDisposition.Neutral)]
    [InlineData("Asteroid", false, TargetDisposition.Neutral)]
    [InlineData("Anomaly", false, TargetDisposition.Neutral)]
    [InlineData("Wormhole", false, TargetDisposition.Neutral)]
    [InlineData("ResourceDrop", false, TargetDisposition.Neutral)]
    [InlineData("Body", false, TargetDisposition.Neutral)]
    public void Classify_ReadsTheOneDispositionTable(string kind, bool hostile, TargetDisposition expected)
        => Assert.Equal(expected, SpaceTargeting.Classify(kind, hostile));

    [Fact]
    public void AttackRange_IsTheServersEngageRange_FromTheOneSharedConstant()
    {
        // #2284: the client used to keep its own copy of the server's ShipEngageRange ("matches the server's 70").
        Assert.Equal(BlocksBeyondTheStars.Shared.Definitions.SpaceCombatRules.EngageRange, SpaceTargeting.AttackRange);
    }

    [Fact]
    public void Pilots_AreFriendly_AndNpcTraders_Neutral()
    {
        Assert.Equal(TargetDisposition.Friendly, Pilot("papa", 10f).Disposition);
        var trader = Pilot("npc:trader_1", 10f);
        Assert.True(trader.IsTrader);
        Assert.Equal(SpaceTargeting.TraderKind, trader.Kind);
        Assert.Equal(TargetDisposition.Neutral, trader.Disposition);
    }

    [Fact]
    public void KindLists_MatchWhatEachSystemTargets()
    {
        Assert.True(SpaceTargeting.IsFireTargetKind("Asteroid"));
        Assert.True(SpaceTargeting.IsFireTargetKind("Wreck"));
        Assert.True(SpaceTargeting.IsFireTargetKind("BanditShip"));
        Assert.False(SpaceTargeting.IsFireTargetKind("SpaceStation"));
        Assert.False(SpaceTargeting.IsFireTargetKind("ResourceDrop"));

        Assert.True(SpaceTargeting.IsScannableKind("Wormhole"));
        Assert.True(SpaceTargeting.IsScannableKind("SpaceStation"));
        Assert.False(SpaceTargeting.IsScannableKind("ResourceDrop"));

        Assert.True(SpaceTargeting.IsCycleKind("Drone"));
        Assert.True(SpaceTargeting.IsCycleKind("EscapePod"));
        Assert.False(SpaceTargeting.IsCycleKind("Asteroid"));
        Assert.False(SpaceTargeting.IsCycleKind("ResourceDrop"));
        Assert.False(SpaceTargeting.IsCycleKind("Body"));
    }

    // ---- Order -----------------------------------------------------------------------------------------

    [Fact]
    public void Order_AttackingHostilesFirst_ThenOtherHostiles_ThenNavigation_ThenPilots()
    {
        var all = new[]
        {
            Pilot("papa", 20f),
            Entity("station", "SpaceStation", 400f),
            Entity("far_drone", "Drone", 110f, hostile: true),
            Entity("rock", "Asteroid", 5f),
            Entity("drop", "ResourceDrop", 6f),
            Entity("near_drone", "Drone", 60f, hostile: true),
            Entity("ufo", "Ufo", 30f, hostile: true),
            Entity("bandit", "BanditShip", 50f), // demanding cargo: a hostile-tier target, not yet attacking
            Entity("pod", "EscapePod", 90f),
            Pilot("npc:trader", 100f),
        };

        var ordered = SpaceTargeting.Order(all, 0f, 0f, 0f, Radar, pingActive: false);

        Assert.Equal(new[] { "ufo", "near_drone", "bandit", "far_drone", "pod", "station", "papa", "npc:trader" }, Ids(ordered));
        Near(30f, ordered[0].Distance);
    }

    [Fact]
    public void Order_KeepsHostilesAndPilots_InsideTheRadar_ButNavigationSystemWide()
    {
        var all = new[]
        {
            Entity("drone", "Drone", 200f, hostile: true),
            Entity("wreck", "Wreck", 1500f),
            Pilot("papa", 400f),
        };

        Assert.Equal(new[] { "wreck" }, Ids(SpaceTargeting.Order(all, 0f, 0f, 0f, Radar, pingActive: false)));

        // The radar array (300) reaches the drone; the Quantum scanner's system ping reaches everything.
        Assert.Equal(new[] { "drone", "wreck" }, Ids(SpaceTargeting.Order(all, 0f, 0f, 0f, 300f, pingActive: false)));
        Assert.Equal(new[] { "drone", "wreck", "papa" }, Ids(SpaceTargeting.Order(all, 0f, 0f, 0f, Radar, pingActive: true)));
    }

    [Fact]
    public void Order_IsMeasuredFromTheShip_AndStableOnTies()
    {
        var all = new[] { Entity("b", "Wreck", 100f), Entity("a", "Wreck", 100f), Entity("c", "Wreck", 0f) };

        var ordered = SpaceTargeting.Order(all, 0f, 0f, 100f, Radar, pingActive: false);

        Assert.Equal(new[] { "a", "b", "c" }, Ids(ordered));
        Near(0f, ordered[0].Distance);
    }

    [Fact]
    public void Next_WrapsAtTheEnd_StartsOverForAVanishedTarget_AndIsNullForNothing()
    {
        var ordered = SpaceTargeting.Order(new[] { Entity("a", "Drone", 10f, true), Entity("b", "Drone", 20f, true), Entity("c", "Wreck", 30f) },
            0f, 0f, 0f, Radar, false);

        Assert.Equal("a", SpaceTargeting.Next(null, ordered)!.Value.Id);
        Assert.Equal("b", SpaceTargeting.Next("a", ordered)!.Value.Id);
        Assert.Equal("c", SpaceTargeting.Next("b", ordered)!.Value.Id);
        Assert.Equal("a", SpaceTargeting.Next("c", ordered)!.Value.Id);
        Assert.Equal("a", SpaceTargeting.Next("gone", ordered)!.Value.Id);
        Assert.Null(SpaceTargeting.Next("a", System.Array.Empty<TargetCandidate>()));
    }

    [Fact]
    public void NearestHostile_FirstPress_TheNearest_AgainTheNextNearest()
    {
        var ordered = SpaceTargeting.Order(new[]
        {
            Entity("station", "SpaceStation", 5f),
            Entity("far", "Drone", 100f, true),
            Entity("bandit", "BanditShip", 80f), // demanding cargo — still an enemy for "nearest enemy"
            Entity("near", "Drone", 65f, true),
        }, 0f, 0f, 0f, Radar, false);

        Assert.Equal("near", SpaceTargeting.NearestHostile(null, ordered)!.Value.Id);
        Assert.Equal("near", SpaceTargeting.NearestHostile("station", ordered)!.Value.Id);
        Assert.Equal("bandit", SpaceTargeting.NearestHostile("near", ordered)!.Value.Id);
        Assert.Equal("far", SpaceTargeting.NearestHostile("bandit", ordered)!.Value.Id);
        Assert.Equal("near", SpaceTargeting.NearestHostile("far", ordered)!.Value.Id);

        var peaceful = SpaceTargeting.Order(new[] { Entity("station", "SpaceStation", 5f) }, 0f, 0f, 0f, Radar, false);
        Assert.Null(SpaceTargeting.NearestHostile(null, peaceful));
    }

    [Fact]
    public void FirstAttacking_IsTheNearestAttacker_ForTheAutoAdvanceAfterAKill()
    {
        var ordered = SpaceTargeting.Order(new[]
        {
            Entity("far", "Drone", 100f, true),
            Entity("b", "Ufo", 50f, true),
            Entity("a", "Drone", 40f, true),
        }, 0f, 0f, 0f, Radar, false);

        Assert.Equal("a", SpaceTargeting.FirstAttacking(ordered)!.Value.Id);
        Assert.Equal("b", SpaceTargeting.FirstAttacking(ordered, excludeId: "a")!.Value.Id);

        var calm = SpaceTargeting.Order(new[] { Entity("far", "Drone", 100f, true) }, 0f, 0f, 0f, Radar, false);
        Assert.Null(SpaceTargeting.FirstAttacking(calm));
    }

    // ---- Range + hysteresis ----------------------------------------------------------------------------

    [Fact]
    public void StillValid_KeepsALockTo110Percent_OfTheRadarRange()
    {
        Assert.True(SpaceTargeting.StillValid(Entity("d", "Drone", Radar * 1.05f, true), 0f, 0f, 0f, Radar, false));
        Assert.False(SpaceTargeting.StillValid(Entity("d", "Drone", Radar * 1.11f, true), 0f, 0f, 0f, Radar, false));
        Assert.True(SpaceTargeting.StillValid(Entity("d", "Drone", Radar * 3f, true), 0f, 0f, 0f, Radar, pingActive: true));
        Assert.True(SpaceTargeting.StillValid(Entity("s", "SpaceStation", 5000f), 0f, 0f, 0f, Radar, false));
        Assert.True(SpaceTargeting.InLockRange("Body", 9000f, Radar, false));
    }

    // ---- Auto-lock + threat ticks ----------------------------------------------------------------------

    [Fact]
    public void AttackWatch_ReportsOnlyHostilesThatStartAttacking()
    {
        var watch = new AttackWatch();
        var far = new[] { Entity("d1", "Drone", 100f, true) };
        Assert.Null(watch.Update(far, 0f, 0f, 0f));

        // d1 closes in: a newcomer once …
        var close = new[] { Entity("d1", "Drone", 60f, true) };
        Assert.Equal("d1", watch.Update(close, 0f, 0f, 0f));
        // … and not again while it keeps attacking (a cleared lock stays cleared).
        Assert.Null(watch.Update(close, 0f, 0f, 0f));
        Assert.True(watch.IsAttacking("d1"));

        // A second, nearer attacker arrives: it is the newcomer.
        var two = new[] { Entity("d1", "Drone", 60f, true), Entity("d2", "Ufo", 40f, true), Entity("bandit", "BanditShip", 30f) };
        Assert.Equal("d2", watch.Update(two, 0f, 0f, 0f));
        Assert.Equal(2, watch.Count); // the talking raider does not shoot (yet)

        watch.Reset();
        Assert.Equal("d2", watch.Update(two, 0f, 0f, 0f));
    }

    [Fact]
    public void NearestAttackers_AreAtMostFour_NearestFirst_WithoutTheLock()
    {
        var all = new System.Collections.Generic.List<TargetCandidate>();
        for (int i = 0; i < 7; i++)
        {
            all.Add(Entity("d" + i, "Drone", 10f + i * 8f, true));
        }

        all.Add(Entity("far", "Drone", 120f, true));
        all.Add(Pilot("papa", 5f));

        var ticks = SpaceTargeting.NearestAttackers(all, 0f, 0f, 0f, "d0", 4, new System.Collections.Generic.List<TargetCandidate>());

        Assert.Equal(new[] { "d1", "d2", "d3", "d4" }, Ids(ticks));
    }

    // ---- Weapon assist ---------------------------------------------------------------------------------

    [Fact]
    public void LockAssistCone_StaysInsideTheServersArc()
    {
        // The server rejects AutoAim shots more than ~60° off the nose (dot < 0.5); the lock's ±40° must stay inside.
        Assert.Equal(40f, SpaceTargeting.LockAssistConeDegrees);
        Assert.True(SpaceTargeting.LockAssistMinDot > 0.5f);
        Near(0.766f, SpaceTargeting.LockAssistMinDot);
    }

    [Theory]
    [InlineData(0, "Drone", false)]     // the asteroid breaker cannot hit a hostile — no assist onto it
    [InlineData(0, "Asteroid", true)]
    [InlineData(0, "Wreck", true)]
    [InlineData(1, "Drone", true)]      // a combat cannon fights …
    [InlineData(1, "Asteroid", false)]  // … but mines only where the rules allow it: no assist
    [InlineData(2, "BanditShip", true)] // the dual laser does both
    [InlineData(2, "Asteroid", true)]
    [InlineData(2, "SpaceStation", false)]
    [InlineData(2, "ResourceDrop", false)]
    public void WeaponSuits_KeepsTheAssistOnTargetsTheWeaponIsBuiltFor(int weaponClass, string kind, bool expected)
        => Assert.Equal(expected, SpaceTargeting.WeaponSuits(weaponClass, kind));

    [Fact]
    public void AheadScore_TakesWhatTheNoseCovers_WidenedByApparentSize()
    {
        Assert.True(SpaceTargeting.AheadScore(0f, 0f, 50f, 0f, 0f, 1f, 2f, 12f, out float dead));
        Assert.True(dead < 0f); // the body itself covers the aim line

        // 20° off the nose: outside a 12° cone for a small rock, inside it for a big body.
        float x = 50f * (float)System.Math.Tan(20.0 * System.Math.PI / 180.0);
        Assert.False(SpaceTargeting.AheadScore(x, 0f, 50f, 0f, 0f, 1f, 1f, 12f, out _));
        Assert.True(SpaceTargeting.AheadScore(x, 0f, 50f, 0f, 0f, 1f, 30f, 12f, out _));

        Assert.False(SpaceTargeting.AheadScore(0f, 0f, -50f, 0f, 0f, 1f, 2f, 12f, out _)); // behind
    }

    // ---- Edge arrow ------------------------------------------------------------------------------------

    private const float W = 1600f, H = 900f;

    [Fact]
    public void EdgePlacement_APointInsideTheScreen_IsOnScreen()
    {
        var p = SpaceTargeting.EdgePlacement(900f, 500f, 40f, W, H, margin: 20f);
        Assert.True(p.OnScreen);
        Near(900f, p.X);
        Near(500f, p.Y);

        // Inside the screen but within the margin of the edge: the arrow takes over.
        Assert.False(SpaceTargeting.EdgePlacement(W - 5f, 450f, 40f, W, H, margin: 20f).OnScreen);
    }

    [Fact]
    public void EdgePlacement_RightOfTheScreen_SitsOnTheEllipseRight_PointingRight()
    {
        var p = SpaceTargeting.EdgePlacement(W + 600f, H * 0.5f, 40f, W, H);
        Assert.False(p.OnScreen);
        Near(W * 0.5f + W * SpaceTargeting.EllipseX, p.X, 0.5f);
        Near(H * 0.5f, p.Y, 0.5f);
        Near(0f, p.AngleDeg, 0.01f);
    }

    [Fact]
    public void EdgePlacement_AboveTheScreen_PointsUp()
    {
        var p = SpaceTargeting.EdgePlacement(W * 0.5f, H + 300f, 40f, W, H);
        Assert.False(p.OnScreen);
        Near(H * 0.5f + H * SpaceTargeting.EllipseY, p.Y, 0.5f);
        Near(90f, p.AngleDeg, 0.01f);
    }

    [Fact]
    public void EdgePlacement_BehindTheCamera_IsMirrored_NeverForward()
    {
        // A target behind-left projects (flipped) to the RIGHT of the centre with a negative depth.
        var p = SpaceTargeting.EdgePlacement(W * 0.5f + 300f, H * 0.5f + 50f, -20f, W, H);
        Assert.False(p.OnScreen);
        Assert.True(p.X < W * 0.5f, "behind-left must point left");
        Assert.True(p.Y < H * 0.5f, "and down, the mirrored side of the flipped point");
        Assert.True(System.Math.Abs(p.AngleDeg) > 90f);

        // Even a flipped point that lands inside the screen is not "on screen".
        Assert.False(SpaceTargeting.EdgePlacement(W * 0.5f + 10f, H * 0.5f, -5f, W, H).OnScreen);
    }

    [Fact]
    public void EdgePlacement_DeadBehindTheCentre_PointsDown()
    {
        var p = SpaceTargeting.EdgePlacement(W * 0.5f, H * 0.5f, -30f, W, H);
        Assert.False(p.OnScreen);
        Near(W * 0.5f, p.X, 0.5f);
        Near(H * 0.5f - H * SpaceTargeting.EllipseY, p.Y, 0.5f);
        Near(-90f, p.AngleDeg, 0.01f);
    }

    [Theory]
    [InlineData(-5000f, -5000f)]
    [InlineData(9000f, -3000f)]
    [InlineData(-1000f, 8000f)]
    [InlineData(20000f, 20000f)]
    public void EdgePlacement_Corners_StayInsideTheScreen(float sx, float sy)
    {
        var p = SpaceTargeting.EdgePlacement(sx, sy, 10f, W, H);
        Assert.False(p.OnScreen);
        Assert.InRange(p.X, W * (0.5f - SpaceTargeting.EllipseX) - 0.5f, W * (0.5f + SpaceTargeting.EllipseX) + 0.5f);
        Assert.InRange(p.Y, H * (0.5f - SpaceTargeting.EllipseY) - 0.5f, H * (0.5f + SpaceTargeting.EllipseY) + 0.5f);

        // It lies ON the ellipse.
        float ex = (p.X - W * 0.5f) / (W * SpaceTargeting.EllipseX);
        float ey = (p.Y - H * 0.5f) / (H * SpaceTargeting.EllipseY);
        Near(1f, ex * ex + ey * ey, 1e-3f);
    }

    [Fact]
    public void UpSpriteRotation_TurnsTheUpPointingTriangleOntoTheDirection()
    {
        Near(-90f, SpaceTargeting.UpSpriteRotation(0f)); // pointing right
        Near(0f, SpaceTargeting.UpSpriteRotation(90f));  // pointing up: no turn
        Near(-180f, SpaceTargeting.UpSpriteRotation(-90f)); // pointing down
    }
}
