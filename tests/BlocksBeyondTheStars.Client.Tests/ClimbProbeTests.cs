// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The wall-climbing probe (#2188, #2190): a grab needs a wall at the knees AND the hands that the push points at, so a
/// one-block step is never a wall; a hanging climber sees wall, ledge or nothing; the wall's side edge stops a sideways
/// climb; an overhang stops the way up; and a pull-up only ends where the body fits standing on a block that holds.
/// The test world is a wall face at x = 5 (cells x = 5), the climber pressed against it from the west.
/// </summary>
public sealed class ClimbProbeTests
{
    /// <summary>Feet of a climber pressed against the wall face at x = 5 (radius 0.35 + skin).</summary>
    private const float FaceX = 4.62f;

    private sealed class Grid
    {
        private readonly Dictionary<(int, int, int), ClimbSurface> _cells = new();

        public Grid Wall(int height, ClimbSurface surface = ClimbSurface.Normal, int x = 5, int zFrom = -3, int zTo = 3, int yFrom = 0)
        {
            for (int y = yFrom; y < yFrom + height; y++)
            {
                for (int z = zFrom; z <= zTo; z++)
                {
                    _cells[(x, y, z)] = surface;
                }
            }

            return this;
        }

        public Grid Block(int x, int y, int z, ClimbSurface surface = ClimbSurface.Normal)
        {
            _cells[(x, y, z)] = surface;
            return this;
        }

        public ClimbProbe Probe() => new ClimbProbe(
            (x, y, z) => _cells.TryGetValue((x, y, z), out var s) ? s : ClimbSurface.None,
            (x, y, z) => _cells.ContainsKey((x, y, z)));
    }

    [Fact]
    public void ATallWall_PushedAt_IsGrabbed_FacingIt()
    {
        var probe = new Grid().Wall(height: 5).Probe();

        Assert.True(probe.TryFindWall(FaceX, 1f, 0.5f, 1f, 0f, out var hold));
        Assert.Equal(1, hold.DirX);
        Assert.Equal(0, hold.DirZ);
        Assert.Equal(ClimbSurface.Normal, hold.Surface);
        Assert.Equal(90f, hold.FacingYaw, 3); // facing +X
    }

    [Fact]
    public void AOneBlockStep_IsNeverAWall()
    {
        var probe = new Grid().Wall(height: 1).Probe();

        Assert.False(probe.TryFindWall(FaceX, 0.2f, 0.5f, 1f, 0f, out _));
    }

    [Fact]
    public void PushingAwayOrAlongTheWall_DoesNotGrab_ADiagonalPushDoes()
    {
        var probe = new Grid().Wall(height: 5).Probe();

        Assert.False(probe.TryFindWall(FaceX, 1f, 0.5f, -1f, 0f, out _)); // pulling away
        Assert.False(probe.TryFindWall(FaceX, 1f, 0.5f, 0.3f, 1f, out _)); // walking along it (≈ 73° off the wall)
        Assert.False(probe.TryFindWall(FaceX, 1f, 0.5f, 0f, 0f, out _));   // no push at all
        Assert.True(probe.TryFindWall(FaceX, 1f, 0.5f, 1f, 1f, out var hold)); // 45° — within reach
        Assert.Equal(1, hold.DirX);
    }

    [Fact]
    public void Glass_GivesNoHold_AndASlipperyKnee_MakesTheGripSlippery()
    {
        Assert.False(new Grid().Wall(height: 5, surface: ClimbSurface.None).Probe().TryFindWall(FaceX, 1f, 0.5f, 1f, 0f, out _));

        var mixed = new Grid().Wall(height: 1, surface: ClimbSurface.Slippery, yFrom: 1).Wall(height: 4, yFrom: 2).Probe();
        Assert.True(mixed.TryFindWall(FaceX, 1f, 0.5f, 1f, 0f, out var hold));
        Assert.Equal(ClimbSurface.Slippery, hold.Surface);
    }

    [Fact]
    public void Ahead_ReadsWall_ThenLedge_ThenNothing_AsTheClimberRises()
    {
        var probe = new Grid().Wall(height: 2).Probe();
        var hold = new WallHold(1, 0, ClimbSurface.Normal);

        Assert.Equal(WallAhead.Wall, probe.Ahead(FaceX, 0f, 0.5f, hold, out _));
        Assert.Equal(WallAhead.Ledge, probe.Ahead(FaceX, 0.8f, 0.5f, hold, out var ledgeSurface));
        Assert.Equal(ClimbSurface.Normal, ledgeSurface);
        Assert.Equal(WallAhead.Lost, probe.Ahead(FaceX, 2.5f, 0.5f, hold, out _));
    }

    [Fact]
    public void ASidewaysClimb_StopsAtTheWallsEdge()
    {
        var probe = new Grid().Wall(height: 5).Probe(); // z = −3 … 3
        var hold = new WallHold(1, 0, ClimbSurface.Normal);

        Assert.True(probe.WallContinues(FaceX, 1f, 3.4f, hold, 0f, 0.05f));  // still on the wall
        Assert.False(probe.WallContinues(FaceX, 1f, 3.9f, hold, 0f, 0.05f)); // the next step reaches past its edge
        Assert.True(probe.WallContinues(FaceX, 1f, 3.9f, hold, 0f, -0.05f)); // back along it is fine
    }

    [Fact]
    public void AnOverhang_BlocksTheWayUp()
    {
        var probe = new Grid().Wall(height: 6).Block(4, 3, 0).Probe();

        Assert.True(probe.BlockedAbove(FaceX, 1.2f, 0.5f));
        Assert.False(probe.BlockedAbove(FaceX, 0f, 0.5f));
    }

    [Fact]
    public void APullUp_EndsStandingOnTheTop_OnTheWallColumn()
    {
        var probe = new Grid().Wall(height: 2).Probe();
        var hold = new WallHold(1, 0, ClimbSurface.Normal);

        Assert.True(probe.TryFindLedge(FaceX, 0.8f, 0.5f, hold, 0.05f, out float x, out float y, out float z));
        Assert.Equal(5.5f, x, 3);
        Assert.Equal(2f, y, 3);
        Assert.Equal(0.5f, z, 3);
        Assert.True(probe.BodyFits(x, y, z));
    }

    [Fact]
    public void APullUp_NeedsRoomOnTop_AndAnOpenColumn_AndAHoldingTop()
    {
        var hold = new WallHold(1, 0, ClimbSurface.Normal);

        // Something on top of the wall where the body would stand.
        Assert.False(new Grid().Wall(height: 2).Block(5, 3, 0).Probe().TryFindLedge(FaceX, 0.8f, 0.5f, hold, 0.05f, out _, out _, out _));

        // The climber's own column is blocked above the head.
        Assert.False(new Grid().Wall(height: 2).Block(4, 3, 0).Probe().TryFindLedge(FaceX, 0.8f, 0.5f, hold, 0.05f, out _, out _, out _));

        // A glass top gives no pull-up either.
        Assert.False(new Grid().Wall(height: 1).Wall(height: 1, surface: ClimbSurface.None, yFrom: 1).Probe()
            .TryFindLedge(FaceX, 0.8f, 0.5f, hold, 0.05f, out _, out _, out _));
    }

    [Fact]
    public void AJumpAtATwoBlockWall_PullsOver_ButAOneBlockStepIsLeftToTheJump()
    {
        var twoHigh = new Grid().Wall(height: 2).Probe();
        Assert.True(twoHigh.TryFindLedgeAhead(FaceX, 1.0f, 0.5f, 1f, 0f, ClimbProbe.StepHeight,
            out var hold, out _, out float y, out _));
        Assert.Equal(1, hold.DirX);
        Assert.Equal(2f, y, 3);

        var step = new Grid().Wall(height: 1).Probe();
        Assert.False(step.TryFindLedgeAhead(FaceX, 0.5f, 0.5f, 1f, 0f, ClimbProbe.StepHeight, out _, out _, out _, out _));
    }

    [Fact]
    public void ALedgeAboveTheHands_IsOutOfReach()
    {
        var probe = new Grid().Wall(height: 4).Probe();
        var hold = new WallHold(1, 0, ClimbSurface.Normal);

        Assert.False(probe.TryFindLedge(FaceX, 0.5f, 0.5f, hold, 0.05f, out _, out _, out _)); // top at 4, 3.5 above the feet
    }
}
