// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The editors' gamepad canvas rules (#1954), extracted from the pixel editor so every editor behaves the same:
/// Start swaps tool panel and canvas, B only ever leaves the canvas, and a held stick steps once, waits, then
/// repeats — never sixty cells a second. CI has no pad, so these rules are pure functions.
/// </summary>
public sealed class PadCanvasLogicTests
{
    [Theory]
    [InlineData(false, true, false, true)]   // Start on the panel → canvas
    [InlineData(true, true, false, false)]   // Start on the canvas → panel
    [InlineData(true, false, true, false)]   // B on the canvas → panel
    [InlineData(false, false, true, false)]  // B on the panel is the screen's own "back", not ours
    [InlineData(true, false, false, true)]   // nothing pressed → stay
    [InlineData(false, false, false, false)]
    [InlineData(true, true, true, false)]    // Start wins over B (it toggles)
    public void FocusMode_FollowsStartAndB(bool canvas, bool menuDown, bool cancelDown, bool expected)
        => Assert.Equal(expected, PadCanvasLogic.NextCanvasMode(canvas, menuDown, cancelDown));

    [Fact]
    public void AHeldStick_StepsOnce_Waits_ThenRepeats()
    {
        float gate = 0f;
        int x = 5, y = 5;

        Assert.True(PadCanvasLogic.StepCursor(1f, 0f, now: 10f, ref gate, ref x, ref y, 64, 64));
        Assert.Equal(6, x);

        // Still inside the first delay: no step.
        Assert.False(PadCanvasLogic.StepCursor(1f, 0f, 10f + (PadCanvasLogic.StepFirst * 0.5f), ref gate, ref x, ref y, 64, 64));
        Assert.Equal(6, x);

        // Past it: one step, and from now on the short repeat applies.
        float t = 10f + PadCanvasLogic.StepFirst + 0.001f;
        Assert.True(PadCanvasLogic.StepCursor(1f, 0f, t, ref gate, ref x, ref y, 64, 64));
        Assert.Equal(7, x);
        Assert.False(PadCanvasLogic.StepCursor(1f, 0f, t + (PadCanvasLogic.StepRepeat * 0.5f), ref gate, ref x, ref y, 64, 64));
        Assert.True(PadCanvasLogic.StepCursor(1f, 0f, t + PadCanvasLogic.StepRepeat + 0.001f, ref gate, ref x, ref y, 64, 64));
        Assert.Equal(8, x);
    }

    [Fact]
    public void ReleasingTheStick_MakesTheNextPushImmediate()
    {
        float gate = 0f;
        int x = 5, y = 5;
        PadCanvasLogic.StepCursor(1f, 0f, 10f, ref gate, ref x, ref y, 64, 64);

        PadCanvasLogic.StepCursor(0f, 0f, 10.01f, ref gate, ref x, ref y, 64, 64); // at rest
        Assert.Equal(0f, gate);
        Assert.True(PadCanvasLogic.StepCursor(1f, 0f, 10.02f, ref gate, ref x, ref y, 64, 64));
        Assert.Equal(7, x);
    }

    [Fact]
    public void Up_IsTowardsRowZero_AndTheCursorStaysOnTheCanvas()
    {
        float gate = 0f;
        int x = 0, y = 0;

        PadCanvasLogic.StepCursor(-1f, 1f, 1f, ref gate, ref x, ref y, 8, 8); // up-left in the top-left corner
        Assert.Equal((0, 0), (x, y));

        gate = 0f;
        PadCanvasLogic.StepCursor(0f, -1f, 2f, ref gate, ref x, ref y, 8, 8); // down
        Assert.Equal((0, 1), (x, y));

        x = 7;
        y = 7;
        gate = 0f;
        Assert.False(PadCanvasLogic.StepCursor(1f, -1f, 3f, ref gate, ref x, ref y, 8, 8)); // bottom-right corner: nowhere to go
        Assert.Equal((7, 7), (x, y));
    }

    [Fact]
    public void ASlightPush_IsNoPush()
    {
        float gate = 0f;
        int x = 3, y = 3;

        Assert.False(PadCanvasLogic.StepCursor(PadCanvasLogic.StickThreshold - 0.01f, 0f, 1f, ref gate, ref x, ref y, 8, 8));
        Assert.Equal(3, x);
    }

    [Fact]
    public void AShrunkCanvas_PullsTheCursorBackIn()
    {
        int x = 60, y = 40;

        PadCanvasLogic.ClampCursor(ref x, ref y, 32, 32);
        Assert.Equal((31, 31), (x, y));

        PadCanvasLogic.ClampCursor(ref x, ref y, 0, 0); // a degenerate canvas must not produce a negative cell
        Assert.Equal((0, 0), (x, y));
    }
}
