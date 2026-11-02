// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3082: the words around an achievement's reward — the unlock banner names the present, and the "no room yet" notice
/// names it with the slots to free (it used to be one easy-to-miss status line that said neither). The reward list comes
/// from the client's own content, so the server only sends the key and the slot count.
/// </summary>
public sealed class AchievementRewardTextTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static Dictionary<string, string> Table(string code)
        => ContentLoader.ParseLocaleTable(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "locales", code + ".json")));

    private static readonly Dictionary<string, string> English = Table("en");
    private static readonly Dictionary<string, string> German = Table("de");

    private static string En(string key) => English.TryGetValue(key, out var t) ? t : "[" + key + "]";

    private static string De(string key) => German.TryGetValue(key, out var t) ? t : "[" + key + "]";

    private static string ItemEn(string item) => En($"item.{item}.name");

    private static string ItemDe(string item) => De($"item.{item}.name");

    [Fact]
    public void TheHalloweenPresent_IsReadFromTheContent_AndNamedOnTheBanner()
    {
        var rewards = AchievementRewardText.RewardsOf(Content, "festival_halloween");
        Assert.Equal(2, rewards.Count);

        Assert.Equal("Reward: Pumpkin lantern ×4, Lantern ×6", AchievementRewardText.RewardLine(rewards, ItemEn, En));
        Assert.Equal("Belohnung: Kürbislaterne ×4, Laterne ×6", AchievementRewardText.RewardLine(rewards, ItemDe, De));
    }

    [Fact]
    public void TheWaitingNotice_NamesTheSlotsToFree_OneSlotInItsOwnWords()
    {
        var rewards = AchievementRewardText.RewardsOf(Content, "festival_halloween");

        Assert.Equal("Free 2 slots for Pumpkin lantern ×4, Lantern ×6", AchievementRewardText.RoomLine(rewards, 2, ItemEn, En));
        Assert.Equal("Free 1 slot for Pumpkin lantern ×4, Lantern ×6", AchievementRewardText.RoomLine(rewards, 1, ItemEn, En));
        Assert.Equal("Mach 2 Plätze frei für Kürbislaterne ×4, Laterne ×6", AchievementRewardText.RoomLine(rewards, 2, ItemDe, De));
        Assert.Equal("Mach 1 Platz frei für Kürbislaterne ×4, Laterne ×6", AchievementRewardText.RoomLine(rewards, 1, ItemDe, De));

        // An older server sends no count: the notice still names the reward.
        Assert.Equal("Make room for Pumpkin lantern ×4, Lantern ×6", AchievementRewardText.RoomLine(rewards, 0, ItemEn, En));

        string status = AchievementRewardText.StatusLine("Pumpkin Party", rewards, 2, ItemEn, En);
        Assert.StartsWith("Pumpkin Party: ", status);
        Assert.Contains("Free 2 slots for Pumpkin lantern ×4, Lantern ×6", status);
        Assert.DoesNotContain("{", status);
        Assert.DoesNotContain("{", AchievementRewardText.StatusLine("Kürbisfest", rewards, 2, ItemDe, De));
    }

    [Fact]
    public void AnUnknownOrRewardlessAchievement_HasNoRewardLine()
    {
        Assert.Empty(AchievementRewardText.RewardsOf(Content, "no_such_achievement"));
        Assert.Empty(AchievementRewardText.RewardsOf(null, "festival_halloween"));
        Assert.Equal(string.Empty, AchievementRewardText.RewardLine(new List<ItemAmount>(), ItemEn, En));
    }
}
