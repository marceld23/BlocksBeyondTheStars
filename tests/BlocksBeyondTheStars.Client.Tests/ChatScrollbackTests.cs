// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#1922: the chat box pages back through the recent lines instead of losing what did not fit.</summary>
public sealed class ChatScrollbackTests
{
    [Fact]
    public void WheelUpAndPageUp_ShowOlderLines_AndStopAtTheOldest()
    {
        int offset = ChatScrollback.Step(0, 10, wheel: 1f, pageUp: false, pageDown: false);
        Assert.Equal(1, offset);
        offset = ChatScrollback.Step(offset, 10, 0f, pageUp: true, pageDown: false);
        Assert.Equal(1 + ChatScrollback.PageLines, offset);
        for (int i = 0; i < 10; i++)
        {
            offset = ChatScrollback.Step(offset, 10, 0f, pageUp: true, pageDown: false);
        }

        Assert.Equal(9, offset); // one line always stays in view
        Assert.Equal(1, ChatScrollback.End(offset, 10));
    }

    [Fact]
    public void WheelDownAndPageDown_ComeBackToTheNewestLine_AndNoFurther()
    {
        int offset = ChatScrollback.Step(2, 10, wheel: -1f, pageUp: false, pageDown: false);
        Assert.Equal(1, offset);
        offset = ChatScrollback.Step(offset, 10, 0f, pageUp: false, pageDown: true);
        Assert.Equal(0, offset);
        Assert.Equal(10, ChatScrollback.End(offset, 10));
    }

    [Fact]
    public void AnEmptyLog_NeverScrolls()
    {
        Assert.Equal(0, ChatScrollback.Step(0, 0, 1f, pageUp: true, pageDown: false));
        Assert.Equal(0, ChatScrollback.End(3, 0));
    }

    [Fact]
    public void ANewLine_KeepsAReadersView_ButFollowsTheBottom()
    {
        Assert.Equal(0, ChatScrollback.OnLineAdded(0, 11));
        Assert.Equal(4, ChatScrollback.OnLineAdded(3, 11)); // the same lines stay in view
        Assert.Equal(39, ChatScrollback.OnLineAdded(39, 40)); // a full buffer clamps
    }

    [Fact]
    public void SliceStart_TakesTheNewestLinesThatFit()
    {
        // Ten 20-high lines, room for 70: the newest three fit (60), a fourth would not.
        Assert.Equal(7, ChatScrollback.SliceStart(10, 70f, _ => 20f));
        Assert.Equal(0, ChatScrollback.SliceStart(3, 70f, _ => 20f)); // a short history shows whole
        Assert.Equal(0, ChatScrollback.SliceStart(0, 70f, _ => 20f)); // nothing to show
    }

    [Fact]
    public void SliceStart_ShowsAtLeastOneLine_EvenOneTallerThanTheWindow()
    {
        Assert.Equal(4, ChatScrollback.SliceStart(5, 30f, _ => 100f));
    }

    [Fact]
    public void SliceStart_CountsWrappedLinesByTheirRealHeight()
    {
        // Line 8 wraps over three rows (60): with room for 80 only lines 8 and 9 fit.
        float[] heights = { 20f, 20f, 20f, 20f, 20f, 20f, 20f, 20f, 60f, 20f };
        Assert.Equal(8, ChatScrollback.SliceStart(10, 80f, i => heights[i]));
    }

    [Fact]
    public void SliceStart_MeasuresOnlyTheLinesItWalksOver()
    {
        var asked = new System.Collections.Generic.List<int>();
        ChatScrollback.SliceStart(200, 60f, i =>
        {
            asked.Add(i);
            return 20f;
        });
        Assert.Equal(new[] { 199, 198, 197, 196 }, asked); // the fourth one is the one that no longer fits
    }

    [Fact]
    public void MaxOffset_StopsWithTheOldestLineAtTheTopOfAFullWindow()
    {
        int max = ChatScrollback.MaxOffset(10, 70f, _ => 20f);
        Assert.Equal(7, max); // three lines fit, so seven can hide below them
        Assert.Equal(0, ChatScrollback.SliceStart(ChatScrollback.End(max, 10), 70f, _ => 20f));
        Assert.Equal(0, ChatScrollback.MaxOffset(2, 70f, _ => 20f)); // everything fits: nothing to scroll
        Assert.Equal(0, ChatScrollback.MaxOffset(0, 70f, _ => 20f));
        Assert.Equal(4, ChatScrollback.MaxOffset(5, 10f, _ => 20f)); // one line always stays in view
    }

    [Fact]
    public void Scrollbar_MapsTheOffsetBothWays()
    {
        Assert.Equal(0f, ChatScrollback.BarValue(0, 10));
        Assert.Equal(1f, ChatScrollback.BarValue(10, 10));
        Assert.Equal(0.5f, ChatScrollback.BarValue(5, 10));
        Assert.Equal(1f, ChatScrollback.BarValue(12, 10)); // clamped
        Assert.Equal(0f, ChatScrollback.BarValue(3, 0)); // nothing to scroll: the handle rests at the bottom
        Assert.Equal(5, ChatScrollback.OffsetForBar(0.5f, 10));
        Assert.Equal(10, ChatScrollback.OffsetForBar(2f, 10));
        Assert.Equal(0, ChatScrollback.OffsetForBar(-1f, 10));
        Assert.Equal(0, ChatScrollback.OffsetForBar(0.7f, 0));
        for (int offset = 0; offset <= 37; offset++)
        {
            Assert.Equal(offset, ChatScrollback.OffsetForBar(ChatScrollback.BarValue(offset, 37), 37));
        }
    }

    [Fact]
    public void Scrollbar_HandleIsTheShownShare_NeverASliverTooThinToGrab()
    {
        Assert.Equal(1f, ChatScrollback.BarSize(5, 5));
        Assert.Equal(0.25f, ChatScrollback.BarSize(50, 200));
        Assert.Equal(0.08f, ChatScrollback.BarSize(1, 200));
        Assert.Equal(1f, ChatScrollback.BarSize(0, 0));
    }
}
