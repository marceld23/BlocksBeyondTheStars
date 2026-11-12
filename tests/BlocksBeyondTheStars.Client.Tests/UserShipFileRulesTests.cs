// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3520: what the ship editor writes into <c>usercontent/ship_types/</c> — never a key the game reserves (the copy moves to
/// the first free <c>_2</c>, <c>_3</c>, …), and a re-save keeps the file's <c>uses</c>, an empty list included.
/// </summary>
public sealed class UserShipFileRulesTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    [Theory]
    [InlineData("custom", "custom_2")]
    [InlineData("Custom", "Custom_2")]
    [InlineData("starter", "starter_2")]
    [InlineData("scout", "scout_2")]
    [InlineData("ship_scout", "ship_scout_2")]
    [InlineData("my_ship", "my_ship")]
    public void AReservedKey_MovesToTheFirstFreeSuffix(string key, string written)
        => Assert.Equal(written, UserShipFileRules.FreeKey(key, Content.IsReservedUserShipKey));

    [Fact]
    public void TheSuffixCountsOn_AndTheKeyStaysWithinTheLoadersLimit()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal) { "custom", "custom_2", "custom_3" };
        Assert.Equal("custom_4", UserShipFileRules.FreeKey("custom", taken.Contains));

        string longKey = new('a', UserShipFileRules.MaxKeyLength);
        string free = UserShipFileRules.FreeKey(longKey, k => k == longKey);
        Assert.Equal(UserShipFileRules.MaxKeyLength, free.Length);
        Assert.EndsWith("_2", free, StringComparison.Ordinal);
    }

    [Fact]
    public void AReSave_KeepsTheUsesTheFileNamed_AnEmptyListIncluded()
    {
        Assert.Empty(UserShipFileRules.UsesToWrite("{\"key\":\"a\",\"uses\": [ ]}", new List<string>()));
        Assert.Equal(new[] { "npc", "craft" }, UserShipFileRules.UsesToWrite("{\"uses\":[\"npc\",\"craft\"]}", new List<string> { "npc", "craft" }));
    }

    [Fact]
    public void ANewFileOrOneWithoutUses_GetsTheDefault_NpcOnly()
    {
        Assert.Equal(UserShipUses.Default, UserShipFileRules.UsesToWrite(null, null));
        Assert.Equal(UserShipUses.Default, UserShipFileRules.UsesToWrite("{\"key\":\"a\"}", new List<string>()));
        Assert.Equal(new[] { "npc" }, UserShipFileRules.UsesToWrite(null, null));

        // A description that merely quotes the word is no uses field.
        Assert.False(UserShipFileRules.NamesUses("{\"description\":\"see \\\"uses\\\": none\"}"));
        Assert.True(UserShipFileRules.NamesUses("{\n  \"uses\" :\n []\n}"));
    }
}
