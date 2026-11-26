// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Numerics;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #4027: the third-person view. The High view (the new default) orbits a raised pivot so the figure sits in the lower
/// third of the screen at every pitch; Behind is the view before #4027 unchanged; the spring arm pulls in from the pivot;
/// the interaction ray reaches as far past the figure as in first person.
/// </summary>
public sealed class ThirdPersonRigTests
{
    private const float Fov = 80f;
    private const float HeadTop = 1.85f;

    [Fact]
    public void Behind_IsTheOldView()
    {
        Assert.Equal(new Vector3(0f, 1.9f, -3.5f), ThirdPersonRig.Eye(ThirdPersonView.Behind, 0f));
        Assert.Equal(new Vector3(0f, 1.9f, -3.5f), ThirdPersonRig.Eye(ThirdPersonView.Behind, 60f)); // the pitch only turns it
    }

    [Fact]
    public void Shoulder_SitsRightOfTheHead_AndDoesNotOrbit()
    {
        var eye = ThirdPersonRig.Eye(ThirdPersonView.Shoulder, 0f);
        Assert.True(eye.X > 0.3f);
        Assert.True(eye.Y > HeadTop);
        Assert.True(eye.Z < -2f);
        Assert.Equal(eye, ThirdPersonRig.Eye(ThirdPersonView.Shoulder, 50f));
    }

    [Fact]
    public void High_AtALevelLook_IsThePivotPlusTheArm()
    {
        Assert.Equal(ThirdPersonRig.Pivot + ThirdPersonRig.HighArm, ThirdPersonRig.Eye(ThirdPersonView.High, 0f));
    }

    [Theory]
    [InlineData(-25f)]
    [InlineData(-10f)]
    [InlineData(0f)]
    [InlineData(30f)]
    [InlineData(60f)]
    [InlineData(75f)]
    public void High_KeepsTheHead_InTheLowerThird(float pitch)
    {
        float y = ScreenHeightOf(ThirdPersonView.High, pitch, new Vector3(0f, HeadTop, 0f));
        Assert.InRange(y, 0.25f, 0.4f);
    }

    [Fact]
    public void Behind_PutsTheHead_UnderTheCrosshair()
    {
        // The report: the old view hid what lay ahead — the top of the head sat in the middle of the screen.
        Assert.InRange(ScreenHeightOf(ThirdPersonView.Behind, 0f, new Vector3(0f, HeadTop, 0f)), 0.45f, 0.55f);
    }

    [Fact]
    public void TheOrbit_IsClamped_ButKeepsTheArmLength()
    {
        Assert.Equal(-25f, ThirdPersonRig.OrbitPitch(-89f));
        Assert.Equal(75f, ThirdPersonRig.OrbitPitch(89f));
        Assert.Equal(30f, ThirdPersonRig.OrbitPitch(30f));
        Assert.Equal(ThirdPersonRig.Eye(ThirdPersonView.High, 75f), ThirdPersonRig.Eye(ThirdPersonView.High, 89f));
        Assert.Equal(ThirdPersonRig.Eye(ThirdPersonView.High, -25f), ThirdPersonRig.Eye(ThirdPersonView.High, -89f));
        float arm = ThirdPersonRig.HighArm.Length();
        Assert.Equal(arm, (ThirdPersonRig.Eye(ThirdPersonView.High, 40f) - ThirdPersonRig.Pivot).Length(), 3);
    }

    [Fact]
    public void LookingUp_TheHighCamera_StaysAboveTheFeet()
    {
        Assert.True(ThirdPersonRig.Eye(ThirdPersonView.High, -89f).Y > 1f);
    }

    [Fact]
    public void LookingDown_TheHighCamera_RisesOverTheFigure()
    {
        var down = ThirdPersonRig.Eye(ThirdPersonView.High, 60f);
        Assert.True(down.Y > 4.5f);
        Assert.True(down.Z > -1.5f);
    }

    [Fact]
    public void TheSpringArm_PullsInAtOnce_AndEasesOut()
    {
        Assert.Equal(3.7f, ThirdPersonRig.AllowedBoom(3.7f, null));
        Assert.Equal(1.95f, ThirdPersonRig.AllowedBoom(3.7f, 2f), 4);
        Assert.Equal(ThirdPersonRig.BoomMinimum, ThirdPersonRig.AllowedBoom(3.7f, 0.1f));
        Assert.Equal(3.7f, ThirdPersonRig.EaseBoom(-1f, 3.7f, 0.016f)); // just switched on
        Assert.Equal(1f, ThirdPersonRig.EaseBoom(3.7f, 1f, 0.016f));    // in at once
        Assert.Equal(1f + (0.1f * ThirdPersonRig.BoomExtendSpeed), ThirdPersonRig.EaseBoom(1f, 3.7f, 0.1f), 4);
        Assert.Equal(3.7f, ThirdPersonRig.EaseBoom(3.6f, 3.7f, 1f));    // never past the allowed length
    }

    [Fact]
    public void AShortArm_HidesTheFigure()
    {
        Assert.True(ThirdPersonRig.HidesAvatar(ThirdPersonRig.BoomMinimum));
        Assert.False(ThirdPersonRig.HidesAvatar(1.2f));
        Assert.False(ThirdPersonRig.HidesAvatar(-1f)); // not measured yet
    }

    [Fact]
    public void TheAimRay_ReachesAsFarPastTheFigure_AsInFirstPerson()
    {
        Assert.Equal(8f + 3.7f, ThirdPersonRig.AimRayLength(8f, 3.7f));
        Assert.Equal(8f, ThirdPersonRig.AimRayLength(8f, -1f));
        Assert.True(ThirdPersonRig.CellWithinBodyReach(7, 0, 0, 0.5f, 0f, 0.5f, 8f));   // 6.5 m ahead of the body
        Assert.False(ThirdPersonRig.CellWithinBodyReach(9, 0, 0, 0.5f, 0f, 0.5f, 8f));  // 8.5 m ahead
        Assert.True(ThirdPersonRig.CellWithinBodyReach(0, -8, 0, 0.5f, 0f, 0.5f, 8f));  // straight down: the cell's top 7 m under the feet
        Assert.True(ThirdPersonRig.CellWithinBodyReach(0, 9, 0, 0.5f, 0f, 0.5f, 8f));   // over the head: 7.2 m above the top of the body
        Assert.True(ThirdPersonRig.CellWithinBodyReach(-6, 0, -6, 0.5f, 0f, 0.5f, 8f));  // behind and aside: 5.5 m on two axes, 7.8 m
        Assert.False(ThirdPersonRig.CellWithinBodyReach(-7, 0, -7, 0.5f, 0f, 0.5f, 8f)); // 6.5 m on two axes, 9.2 m
    }

    [Fact]
    public void TheSetting_CyclesThroughTheThreeViews()
    {
        Assert.Equal(ThirdPersonView.High, ThirdPersonRig.Normalize(0));
        Assert.Equal(ThirdPersonView.High, ThirdPersonRig.Normalize(7));
        Assert.Equal(ThirdPersonView.Shoulder, ThirdPersonRig.Normalize(2));
        Assert.Equal(ThirdPersonView.Behind, ThirdPersonRig.Step(ThirdPersonView.High, 1));
        Assert.Equal(ThirdPersonView.Shoulder, ThirdPersonRig.Step(ThirdPersonView.High, -1));
        Assert.Equal(ThirdPersonView.High, ThirdPersonRig.Step(ThirdPersonView.Shoulder, 1));
    }

    [Fact]
    public void TheController_PlacesTheCameraByTheRig_AndReachesFromTheBody()
    {
        string controller = ClientTestPaths.PlayerControllerSources();
        Assert.Contains("ThirdPersonRig.Eye(ThirdPersonView, _pitch)", controller);
        Assert.Contains("transform.TransformPoint(RigPoint(ThirdPersonRig.Pivot)) + lag", controller);
        Assert.Contains("ThirdPersonRig.EaseBoom(_cameraBoomLength, ThirdPersonRig.AllowedBoom(full, hitAt), dt)", controller);
        Assert.Contains("ShowOwnFigureByBoom();", controller);
        Assert.Contains("ThirdPersonRig.AimRayLength(Reach, _cameraBoomLength)", controller);
        Assert.Contains("ThirdPersonRig.CellWithinBodyReach(x, y, z, feet.x, feet.y, feet.z, Reach)", controller);
        Assert.DoesNotContain("t <= Reach;", controller); // the block marches run AimRayLength
        Assert.Contains("pc.ThirdPersonView = ThirdPersonRig.Normalize((int)shell.Settings.ThirdPersonView);",
            ClientTestPaths.ScriptPartsText("WorldRig"));
        Assert.Contains("L(\"ui.settings.third_person_view\")", ClientTestPaths.ScriptPartsText("UiSettings"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void TheSetting_HasATextForEveryView(string code)
    {
        var table = VegaLineBudgetTests.Core(code);
        foreach (string key in new[] { "high", "behind", "shoulder" })
        {
            Assert.False(string.IsNullOrWhiteSpace(table["ui.settings.third_person_view." + key]));
        }

        Assert.False(string.IsNullOrWhiteSpace(table["ui.settings.third_person_view_hint"]));
    }

    /// <summary>How high up the screen (0 bottom … 1 top) a point over the feet shows for the camera of
    /// <paramref name="view"/> turned to <paramref name="pitch"/>, at the default field of view.</summary>
    private static float ScreenHeightOf(ThirdPersonView view, float pitch, Vector3 point)
    {
        var inCamera = ThirdPersonRig.Orbit(point - ThirdPersonRig.Eye(view, pitch), -pitch);
        Assert.True(inCamera.Z > 0f);
        float half = (float)Math.Tan(Fov * 0.5 * Math.PI / 180.0);
        return 0.5f + (0.5f * inCamera.Y / inCamera.Z / half);
    }
}
