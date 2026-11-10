// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3458 — F sets a rider down on a real standing spot. The wagon floor hovers 1.7 blocks over the pylon's top cell, off
/// the block grid, so a platform one block up was a step nobody could take and F used to drop the rider beside the wagon at
/// floor height whatever stood there. Now the spot outside the door is searched: the rider's side first, from one below to
/// two above the floor; then the other side; then behind the train; the old point last.
/// </summary>
public sealed class RailExitTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    /// <summary>A halted cab on the arena's line with the player standing in it at wagon-local <paramref name="local"/>.</summary>
    private static (RailTravelArena A, Vector3f Pos, float Yaw) Aboard(string name, Vector3f local)
    {
        var a = new RailTravelArena(Content.Value, name);
        a.PlaceTrain(NpcLifeWorld.Cx);
        a.Server.SetTrainForTest(a.PlayerId, a.TrainId, autopilot: 0, halt: 1);
        var (pos, yaw) = a.Server.WagonPoseForTest(a.TrainId, 0)!.Value;
        a.W.Player.State.Position = pos;
        a.Server.EnterTrainForTest(a.PlayerId, a.TrainId, 0);
        a.Server.FramedMoveForTest(a.PlayerId, a.W.Player.State.InTrain, local);
        Assert.NotEqual(string.Empty, a.W.Player.State.InTrain);
        return (a, pos, yaw);
    }

    /// <summary>The cells under a band beside the wagon (wagon-local X from <paramref name="x0"/> to <paramref name="x1"/>,
    /// Z ±<paramref name="halfZ"/>).</summary>
    private static HashSet<Vector3i> Band(Vector3f pos, float yaw, float x0, float x1, float halfZ, int y)
    {
        var cells = new HashSet<Vector3i>();
        for (float x = x0; x <= x1; x += 0.25f)
        {
            for (float z = -halfZ; z <= halfZ; z += 0.25f)
            {
                var w = RailRules.LocalToWorld(pos, yaw, new Vector3f(x, 0f, z));
                cells.Add(new Vector3i((int)Math.Floor(w.X), y, (int)Math.Floor(w.Z)));
            }
        }

        return cells;
    }

    private static void Fill(RailTravelArena a, IEnumerable<Vector3i> cells, string key = "stone")
    {
        foreach (var c in cells)
        {
            a.W.Set(c.X, c.Y, c.Z, key);
        }
    }

    private static float LocalX(Vector3f pos, float yaw, Vector3f at) => RailRules.WorldToLocal(pos, yaw, at).X;

    [Fact]
    public void OnOpenGround_FPutsTheRiderOnTheGround_OnTheirSide()
    {
        var (a, pos, yaw) = Aboard("rail_exit_ground", new Vector3f(0.9f, 0f, 0.2f));
        using (a)
        {
            a.Server.ExitTrainForTest(a.PlayerId);
            var at = a.W.Player.State.Position;
            Assert.Equal(string.Empty, a.W.Player.State.InTrain);
            Assert.Equal(a.W.Feet, at.Y, 3); // on the pad, not in the air at floor height
            Assert.InRange(LocalX(pos, yaw, at), 1.9f, 3.6f);
        }
    }

    [Theory]
    [InlineData(1)] // a platform one block high: its top 0.7 below the hovering floor (pylon top + 1.7)
    [InlineData(2)] // two blocks: 0.3 above the floor
    [InlineData(3)] // three blocks: 1.3 above the floor — the step that trapped Lyxette (the door is two blocks high)
    public void APlatformStepBesideTheDoor_IsWhereFPutsTheRider(int up)
    {
        var (a, pos, yaw) = Aboard("rail_exit_step_" + up, new Vector3f(0.9f, 0f, 0f));
        using (a)
        {
            int top = a.W.Feet + up; // the platform's surface: feet stand here
            for (int y = a.W.Feet; y < top; y++)
            {
                Fill(a, Band(pos, yaw, 1.6f, 4.5f, 2.5f, y));
            }

            a.Server.ExitTrainForTest(a.PlayerId);
            var at = a.W.Player.State.Position;
            Assert.Equal(top, at.Y, 3);
            Assert.True(LocalX(pos, yaw, at) > 1.5f, "set down on the platform's side");
        }
    }

    [Fact]
    public void ABlockedSide_SetsTheRiderDownOnTheOther_BothBlockedCloseBy_OneRingFarther_NeverBehindTheTrain()
    {
        var (a, pos, yaw) = Aboard("rail_exit_blocked", new Vector3f(0.9f, 0f, 0f));
        using (a)
        {
            // A wall on the rider's side, from the ground to above the wagon's roof.
            for (int y = a.W.Feet; y <= a.W.Feet + 5; y++)
            {
                Fill(a, Band(pos, yaw, 1.8f, 3.4f, 2.5f, y));
            }

            a.Server.ExitTrainForTest(a.PlayerId);
            var at = a.W.Player.State.Position;
            Assert.True(LocalX(pos, yaw, at) < -1.5f, "the other side");
            Assert.Equal(a.W.Feet, at.Y, 3);

            // Both sides walled in close by (a narrow cutting): one ring farther out, beside the train — never on the line's
            // axis behind it, where the wagons would run into the rider.
            Reboard(a, pos);
            for (int y = a.W.Feet; y <= a.W.Feet + 5; y++)
            {
                Fill(a, Band(pos, yaw, -3.4f, -1.8f, 2.5f, y));
            }

            a.Server.ExitTrainForTest(a.PlayerId);
            at = a.W.Player.State.Position;
            var local = RailRules.WorldToLocal(pos, yaw, at);
            Assert.InRange(Math.Abs(local.X), 3.6f, 4.8f);
            Assert.InRange(local.Z, -RailRules.WagonLength * 0.5f, RailRules.WagonLength * 0.5f);
            Assert.Equal(a.W.Feet, at.Y, 3);

            // Walled in everywhere: today's point beside the wagon is the last resort.
            Reboard(a, pos);
            for (int y = a.W.Feet; y <= a.W.Feet + 5; y++)
            {
                Fill(a, Band(pos, yaw, 3.6f, 5f, 2.5f, y));
                Fill(a, Band(pos, yaw, -5f, -3.6f, 2.5f, y));
            }

            a.Server.ExitTrainForTest(a.PlayerId);
            local = RailRules.WorldToLocal(pos, yaw, a.W.Player.State.Position);
            Assert.Equal(RailRules.ExitSide, local.X, 2);
        }
    }

    private static void Reboard(RailTravelArena a, Vector3f pos)
    {
        a.W.Player.State.Position = pos;
        a.Server.EnterTrainForTest(a.PlayerId, a.TrainId, 0);
        a.Server.FramedMoveForTest(a.PlayerId, a.W.Player.State.InTrain, new Vector3f(0.9f, 0f, 0f));
    }

    [Fact]
    public void AFrameReport_ArrivingAfterTheRiderWasSetDown_IsDropped()
    {
        var (a, _, _) = Aboard("rail_exit_late_frame", new Vector3f(0.9f, 0f, 0f));
        using (a)
        {
            string frame = a.W.Player.State.InTrain;
            a.Server.ExitTrainForTest(a.PlayerId);
            var setDown = a.W.Player.State.Position;
            a.Server.FramedMoveForTest(a.PlayerId, frame, new Vector3f(1.2f, 0f, 0.3f)); // still on its way when F was handled
            Assert.Equal(setDown.X, a.W.Player.State.Position.X, 3);
            Assert.Equal(setDown.Z, a.W.Player.State.Position.Z, 3);
            Assert.Equal(string.Empty, a.W.Player.State.InTrain);
        }
    }
}
