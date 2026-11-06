// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The cockpit camera of the landing and lift-off cinematics (#3258) and the pure flight paths both the director and the
/// shot plan fly: behind the helm at eye height looking out along the nose, the eye fallback, the cabin glide check, the
/// touchdown's ease and the lift-off's rise and climb.
/// </summary>
public sealed class TransitionCockpitTests
{
    // A hauler-like cabin: floor y 0, walls round x 0..6 / z 0..8, helm console at (3, 1, 7), engines at the stern z -1.
    private static readonly List<(int X, int Y, int Z)> SternEngines = new() { (1, 1, -1), (5, 1, -1), (1, 2, -1), (5, 2, -1) };

    private static bool Cabin(int x, int y, int z)
        => y == 0 || y == 4 || x == 0 || x == 6 || z == 0 || z == 8 || (x == 3 && y == 1 && z == 7);

    [Fact]
    public void Nose_RunsFromTheEnginesToTheHelm()
    {
        var nose = TransitionCockpit.Nose(SternEngines, 3.5f, 7.5f);
        Assert.Equal(0f, nose.X, 3);
        Assert.Equal(1f, nose.Z, 3);

        var side = TransitionCockpit.Nose(new List<(int, int, int)> { (-2, 1, 4) }, 6.5f, 4.5f);
        Assert.Equal(1f, side.X, 3); // engines on the left → the nose points +X

        Assert.Equal((0f, 1f), TransitionCockpit.Nose(null, 3f, 3f));                                    // no engines: +Z
        Assert.Equal((0f, 1f), TransitionCockpit.Nose(new List<(int, int, int)> { (3, 1, 7) }, 3.5f, 7.5f)); // degenerate
    }

    [Fact]
    public void AtHelm_StandsOneCellBehindTheHelmAtEyeHeightLookingOutAndDown()
    {
        var pose = TransitionCockpit.AtHelm(3, 1, 7, TransitionCockpit.Nose(SternEngines, 3.5f, 7.5f), Cabin);

        Assert.True(pose.AtHelm);
        Assert.Equal(3.5f, pose.X, 3);
        Assert.Equal(6.5f, pose.Z, 3);                           // one cell behind (toward the engines)
        Assert.Equal(1f + TransitionCockpit.EyeHeight, pose.Y, 3); // the floor's top is y 1
        Assert.True(pose.DirZ > 0.98f);                          // along the nose ...
        Assert.Equal(-Math.Sin(8.0 * Math.PI / 180.0), pose.DirY, 3); // ... 8° down
        Assert.Equal(1f, pose.DirX * pose.DirX + pose.DirY * pose.DirY + pose.DirZ * pose.DirZ, 3);
    }

    [Fact]
    public void AtHelm_WithTheCellBehindBuiltUp_StandsOverTheHelm()
    {
        bool Cramped(int x, int y, int z) => Cabin(x, y, z) || (x == 3 && z == 6 && y <= 2);

        var pose = TransitionCockpit.AtHelm(3, 1, 7, (0f, 1f), Cramped);

        Assert.Equal(3.5f, pose.X, 3);
        Assert.Equal(7.5f, pose.Z, 3);
        Assert.Equal(1f + TransitionCockpit.EyeHeight, pose.Y, 3);
    }

    [Fact]
    public void AtHelm_NoseAlongX_StepsBackAlongX()
    {
        var pose = TransitionCockpit.AtHelm(3, 1, 4, (-1f, 0f), Cabin);
        Assert.Equal(4.5f, pose.X, 3);
        Assert.Equal(4.5f, pose.Z, 3);
        Assert.True(pose.DirX < -0.98f);
    }

    [Fact]
    public void AtEye_KeepsTheEyeAndTurnsToTheNose()
    {
        var pose = TransitionCockpit.AtEye(2.5f, 2.6f, 3.5f, (0f, 1f));
        Assert.False(pose.AtHelm);
        Assert.Equal((2.5f, 2.6f, 3.5f), (pose.X, pose.Y, pose.Z));
        Assert.True(pose.DirZ > 0.98f && pose.DirY < 0f);
    }

    [Fact]
    public void CabinPathClear_OnlyThroughOpenCells()
    {
        Assert.True(TransitionCockpit.CabinPathClear(Cabin, 3.5f, 2.6f, 6.5f, 2.5f, 2.6f, 2.5f));
        bool Partition(int x, int y, int z) => Cabin(x, y, z) || z == 4;
        Assert.False(TransitionCockpit.CabinPathClear(Partition, 3.5f, 2.6f, 6.5f, 2.5f, 2.6f, 2.5f));
    }

    [Fact]
    public void TouchdownPath_SinksOnAnEaseOutAndSettles()
    {
        const float drop = 46.5f;
        Assert.Equal(drop, TransitionFlightPath.TouchdownHeight(0f, drop), 3);
        Assert.Equal(0f, TransitionFlightPath.TouchdownHeight(TransitionFlightPath.DescendSeconds, drop), 3);
        Assert.Equal(0f, TransitionFlightPath.TouchdownHeight(TransitionFlightPath.TouchdownSeconds, drop), 3);

        // Fast high up, slow near the ground: the first second covers far more than the last.
        float first = drop - TransitionFlightPath.TouchdownHeight(1f, drop);
        float last = TransitionFlightPath.TouchdownHeight(TransitionFlightPath.DescendSeconds - 1f, drop);
        Assert.True(first > 4f * last);

        // The dust beat is the moment the stand-in passes the dust's reach.
        float dust = TransitionFlightPath.TouchdownTimeAt(TransitionFlightPath.DustHeight, drop);
        Assert.Equal(TransitionFlightPath.DustHeight, TransitionFlightPath.TouchdownHeight(dust, drop), 2);
        Assert.Equal(0f, TransitionFlightPath.TouchdownTimeAt(drop + 1f, drop));

        var rest = TransitionFlightPath.Touchdown(TransitionFlightPath.DescendSeconds, drop);
        Assert.Equal((0f, 0f, 0f, 0f), (rest.X, rest.Y, rest.Z, rest.RotX));
    }

    [Fact]
    public void LiftoffPath_WaitsRisesThenClimbsNoseUp()
    {
        var ignite = TransitionFlightPath.Liftoff(TransitionFlightPath.IgniteAt);
        Assert.Equal(0f, ignite.Y, 3);
        Assert.Equal(0f, ignite.RotX, 3);

        var hover = TransitionFlightPath.Liftoff(TransitionFlightPath.ClimbAt);
        Assert.Equal(5f, hover.Y, 3); // the heavy rise to five blocks

        var clouds = TransitionFlightPath.Liftoff(TransitionFlightPath.CloudsAt);
        Assert.Equal(-16f, clouds.RotX, 3); // the nose up 16°
        Assert.True(clouds.Y > 20f && clouds.Z > 5f, $"climbing away up and along the nose ({clouds.Y}, {clouds.Z})");
        Assert.Equal(Math.Sin(16.0 * Math.PI / 180.0), clouds.NoseY, 3);
    }
}
