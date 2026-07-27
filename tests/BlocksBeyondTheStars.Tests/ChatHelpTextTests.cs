// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The in-game chat help (issue #507). Two rules the locale tables have to keep: chat-facing help and
/// usage lines carry no angle-bracket placeholders — the scrollback renders rich text, so "&lt;item&gt;"
/// reaches the player as a pseudo-HTML tag — and the admin reference is split into short grouped lines
/// instead of one 500-character wall, because "/help" used to dump all of it on every player.
/// </summary>
public sealed class ChatHelpTextTests
{
    /// <summary>Every chat line printed by a slash command, in both languages.</summary>
    private static readonly string[] ChatKeys =
    {
        "ui.chat.help_player",
        "ui.chat.help_admin_hint",
        "ui.chat.report_tip",
        "ui.chat.report_usage",
        "ui.admin.help_cheats",
        "ui.admin.help_inspect",
        "ui.admin.help_fleet",
        "ui.admin.help_story",
        "ui.admin.help_maintenance",
        "ui.cmd.usage_give",
        "ui.cmd.usage_tp",
        "ui.cmd.usage_tpp",
        "ui.cmd.usage_settime",
        "ui.cmd.usage_setweather",
        "ui.cmd.usage_where",
        "ui.cmd.usage_goto",
        "ui.cmd.usage_announce",
        "ui.cmd.usage_restart",
        "ui.cmd.usage_kick",
    };

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void Chat_help_keys_exist_in_both_languages(string language)
    {
        var table = TestLocales.Load(language);
        foreach (string key in ChatKeys)
        {
            Assert.True(table.ContainsKey(key), $"{language}.json is missing '{key}'");
            Assert.False(string.IsNullOrWhiteSpace(table[key]), $"{language}.json has an empty '{key}'");
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void Chat_help_carries_no_rich_text_markup(string language)
    {
        var table = TestLocales.Load(language);
        foreach (string key in ChatKeys)
        {
            string value = table[key];
            Assert.False(value.Contains('<'), $"{language}.json '{key}' still uses '<' — it renders as a tag in chat");
            Assert.False(value.Contains('>'), $"{language}.json '{key}' still uses '>' — it renders as a tag in chat");
        }
    }

    /// <summary>The chat log shows the last ten entries in a ~620x250 box, so one line has to stay
    /// readable on its own. The old single "ui.admin.help" was 509 characters and wrapped over the whole
    /// scrollback; the grouped lines replacing it stay well under that.</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void Admin_help_is_split_into_short_lines(string language)
    {
        var table = TestLocales.Load(language);
        Assert.False(table.ContainsKey("ui.admin.help"), "the 500-char admin help wall should be gone");

        foreach (string key in ChatKeys)
        {
            Assert.True(table[key].Length <= 160, $"{language}.json '{key}' is {table[key].Length} chars — too long for one chat line");
        }
    }

    /// <summary>The player-facing help must not advertise admin commands, and must point at the two things
    /// every player can actually use.</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void Player_help_covers_report_and_bump_only(string language)
    {
        string help = TestLocales.Load(language)["ui.chat.help_player"];
        Assert.Contains("/report", help);
        Assert.Contains("/bump", help);
        Assert.DoesNotContain("/give", help);
        Assert.DoesNotContain("/kick", help);
        Assert.Contains("/help admin", TestLocales.Load(language)["ui.chat.help_admin_hint"]);
    }
}
