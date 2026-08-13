// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Argument parsing for the admin slash commands (issue #980). The rule under test: a player name is
/// the whole rest of the line, because names contain spaces — taking the token after the verb turned
/// "mincraft Fan" into "mincraft" and the server answered "target player not found".
/// </summary>
public sealed class AdminChatCommandTests
{
    [Theory]
    [InlineData("/tpp mincraft Fan", "mincraft Fan")]
    [InlineData("/tpp Marcel", "Marcel")]
    [InlineData("/where   mincraft   Fan  ", "mincraft   Fan")] // inner spacing is part of the name
    [InlineData("/tpp \"mincraft Fan\"", "mincraft Fan")]       // quoting a name is tolerated
    [InlineData("/tpp @mincraft Fan", "mincraft Fan")]          // the @Name habit from other games
    [InlineData("/tpp \"@mincraft Fan\"", "mincraft Fan")]      // …even both at once
    [InlineData("/paintwipe #42", "#42")]                       // a design id must survive untouched
    public void PlayerArgument_TakesTheWholeRestOfTheLine(string line, string expected)
    {
        Assert.Equal(expected, AdminChatCommand.PlayerArgument(line));
    }

    [Theory]
    [InlineData("/tpp")]
    [InlineData("/tpp   ")]
    [InlineData("")]
    [InlineData(null)]
    public void PlayerArgument_IsEmpty_WhenNothingFollowsTheVerb(string? line)
    {
        Assert.Equal(string.Empty, AdminChatCommand.PlayerArgument(line));
    }

    /// <summary>"/give &lt;item&gt; &lt;count&gt; &lt;name…&gt;" — only the trailing token run is the name.</summary>
    [Theory]
    [InlineData("/give iron_plate 5 mincraft Fan", "mincraft Fan")]
    [InlineData("/give iron_plate 5 @Justus", "Justus")]
    [InlineData("/give iron_plate 5", "")]
    public void PlayerArgument_SkipsLeadingTokens_ForGive(string line, string expected)
    {
        Assert.Equal(expected, AdminChatCommand.PlayerArgument(line, 3));
    }

    /// <summary>The client cleans the name the same way the server matches it, so a pasted "@Name" or a
    /// quoted name resolves instead of silently missing.</summary>
    [Theory]
    [InlineData("  Marcel  ", "Marcel")]
    [InlineData("\"mincraft Fan\"", "mincraft Fan")]
    [InlineData("@Justus", "Justus")]
    [InlineData(null, "")]
    public void CleanName_StripsWhitespaceQuotesAndAt(string? raw, string expected)
    {
        Assert.Equal(expected, AdminChatCommand.CleanName(raw));
    }
}
