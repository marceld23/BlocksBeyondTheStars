// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Rich-text neutralisation for the chat scrollback (issue #507): the log renders with
/// <c>supportRichText</c> on, so anything merely displayed there — chat lines, system lines — must not be
/// able to open a uGUI tag.
/// </summary>
public sealed class ChatMarkupTests
{
    [Theory]
    [InlineData("<color=#ff0000>everything is red now", "< color=#ff0000>everything is red now")]
    [InlineData("<b>shouting</b>", "< b>shouting< /b>")]
    [InlineData("<size=200>huge", "< size=200>huge")]
    public void RichSafe_BreaksTagsApart(string input, string expected)
        => Assert.Equal(expected, ChatMarkup.RichSafe(input));

    /// <summary>Text without a tag start is returned untouched — including maths a player might type.</summary>
    [Theory]
    [InlineData("hello there")]
    [InlineData("5 < 10 and 10 > 5")] // "< " / "> " never start a tag, so they stay as typed
    [InlineData("")]
    public void RichSafe_LeavesPlainTextAlone(string input)
        => Assert.Equal(input, ChatMarkup.RichSafe(input));

    [Fact]
    public void RichSafe_HandlesNull()
        => Assert.Equal(string.Empty, ChatMarkup.RichSafe(null));

    /// <summary>A trailing "&lt;" cannot open a tag and must not walk off the end of the string.</summary>
    [Fact]
    public void RichSafe_HandlesTrailingAngleBracket()
        => Assert.Equal("what<", ChatMarkup.RichSafe("what<"));
}
