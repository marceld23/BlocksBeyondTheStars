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
}
