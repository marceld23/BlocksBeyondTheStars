// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3171 — the client's lift facts (<see cref="LiftRiding"/>): E on the platform chooses the floor, E at a stop where the
/// platform waits too (Lyxette 2026-11-04: "standing on it I can't start anything"; "up here I can only call it when it is
/// down"), the platform hides the motor under it from the voxel-only aim ray, and the picker's floors carry their names.
/// </summary>
public sealed class LiftRidingTests
{
    private const int Circumference = 1024;

    /// <summary>A lift over the motor at (10, 200, 10), resting at the bottom floor (platform cells at y 201).</summary>
    private static NetLift Lift(float platformY = 201f, bool moving = false, float targetY = 201f)
        => new()
        {
            Id = 7,
            X = 10,
            Y = 200,
            Z = 10,
            PlatformY = platformY,
            TargetY = moving ? targetY : platformY,
            Moving = moving,
            Speed = 3f,
            Levels = new[] { 201, 205, 210 },
            LevelNames = new[] { string.Empty, "Workshop", string.Empty },
            LevelHasStop = new[] { false, true, true },
        };

    [Fact]
    public void StandingOnThePlatform_IsTheRidersBox_TheShortWayRoundTheSeam()
    {
        var lifts = new[] { Lift() };
        Assert.Same(lifts[0], LiftRiding.StandingOn(lifts, 10.5, 202, 10.5, Circumference)); // on its top
        Assert.Same(lifts[0], LiftRiding.StandingOn(lifts, 11.9, 202.2, 9.2, Circumference)); // near the edge
        Assert.Null(LiftRiding.StandingOn(lifts, 12.5, 202, 10.5, Circumference));             // on the landing beside it
        Assert.Null(LiftRiding.StandingOn(lifts, 10.5, 206, 10.5, Circumference));             // far above it
        Assert.Null(LiftRiding.StandingOn(null, 10.5, 202, 10.5, Circumference));

        var seam = new[] { new NetLift { Id = 1, X = 0, Y = 200, Z = 10, PlatformY = 201f, TargetY = 201f } };
        Assert.Same(seam[0], LiftRiding.StandingOn(seam, Circumference + 0.5, 202, 10.5, Circumference)); // a lap on
    }

    [Fact]
    public void ThePlatform_HidesTheMotorUnderIt_ButNotTheLanding()
    {
        var lift = Lift();
        Assert.True(LiftRiding.HidesCell(lift, 10, 200, 10, Circumference));  // the motor at the bottom floor
        Assert.True(LiftRiding.HidesCell(lift, 9, 199, 11, Circumference));   // under the platform's corner
        Assert.False(LiftRiding.HidesCell(lift, 12, 200, 10, Circumference)); // the landing beside the shaft
        Assert.False(LiftRiding.HidesCell(lift, 10, 202, 10, Circumference)); // above the platform
    }

    [Fact]
    public void AStop_BelongsToTheLiftBesideIt_AndOpensThePickerOnlyWhereThePlatformWaits()
    {
        var resting = new[] { Lift() };
        Assert.Same(resting[0], LiftRiding.LiftOfStop(resting, 12, 202, 10, Circumference));
        Assert.Same(resting[0], LiftRiding.LiftOfStop(resting, 8, 206, 12, Circumference));
        Assert.Null(LiftRiding.LiftOfStop(resting, 13, 202, 10, Circumference)); // three cells off: no stop of it
        Assert.Null(LiftRiding.LiftOfStop(resting, 12, 201, 10, Circumference)); // below the bottom floor

        Assert.Same(resting[0], LiftRiding.WaitingAtStop(resting, 12, 202, 10, Circumference)); // the platform is here
        Assert.Null(LiftRiding.WaitingAtStop(resting, 12, 206, 10, Circumference));              // it is not: E calls it

        var moving = new[] { Lift(moving: true, targetY: 205f) };
        Assert.Null(LiftRiding.WaitingAtStop(moving, 12, 202, 10, Circumference));
    }

    [Fact]
    public void TheFloors_ComeBottomFirst_WithTheirNames_AndWhereThePlatformIs()
    {
        var floors = LiftRiding.Floors(Lift(platformY: 205f));
        Assert.Equal(new[] { 201, 205, 210 }, floors.ConvertAll(f => f.Level));
        Assert.Equal(new[] { 1, 2, 3 }, floors.ConvertAll(f => f.Number));
        Assert.Equal("Workshop", floors[1].Name);
        Assert.False(floors[0].HasStop);
        Assert.True(floors[1].Here);
        Assert.False(floors[0].Here || floors[2].Here);

        var going = LiftRiding.Floors(Lift(platformY: 203f, moving: true, targetY: 210f));
        Assert.True(going[2].Heading);
        Assert.DoesNotContain(going, f => f.Here);

        Assert.Empty(LiftRiding.Floors(new NetLift())); // an older server sends no floors
        Assert.Empty(LiftRiding.Floors(null));
        Assert.NotEqual(LiftRiding.Signature(Lift()), LiftRiding.Signature(Lift(platformY: 205f))); // the open picker follows
    }

    /// <summary>The Unity half, guarded as source text (the scripts cannot load headless): the rig gathers the lift fact
    /// and E acts on it, the HUD names it, a waiting stop opens the picker, the picker panel exists in the rig, and the
    /// device menu's name field saves itself (#3172).</summary>
    [Fact]
    public void TheUnityHalf_ActsOnTheLiftPick_AndTheNameFieldSavesItself()
    {
        string player = ClientTestPaths.PlayerControllerSources();
        Assert.Contains("GatherLiftFacts();", player);
        Assert.Contains("case InteractKind.LiftRide:", player);
        Assert.Contains("LiftRiding.WaitingAtStop(", player);

        string hud = ClientTestPaths.ScriptPartsText("HudUi");
        Assert.Contains("case InteractKind.LiftRide:", hud);
        Assert.Contains("\"ui.lift.prompt.choose\"", hud);
        Assert.Contains("\"ui.crystal.prompt.named\"", hud);

        string scripts = Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts");
        Assert.Contains("root.AddComponent<LiftFloorUi>()", File.ReadAllText(Path.Combine(scripts, "WorldRig.cs")));
        string menu = File.ReadAllText(Path.Combine(scripts, "CrystalDeviceUi.cs"));
        Assert.Contains("field.onEndEdit.AddListener(_ => SaveLabel());", menu);
        Assert.Contains("SendCrystalDevice(_dev.X, _dev.Y, _dev.Z, 7,", menu);
    }
}
